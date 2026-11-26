"""Local-only JSON-lines worker. Network access is confined to --setup."""
import argparse
import json
import os
from pathlib import Path
import re
import sys
import time
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent
MODELS = ROOT / "models"
ASR = MODELS / "whisper-base.en"
MT = MODELS / "translation"
os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")


def emit(**kwargs):
    print(json.dumps(kwargs, ensure_ascii=False), flush=True)


def model_dir():
    for p in MT.rglob("metadata.json"):
        meta = json.loads(p.read_text(encoding="utf-8"))
        if meta.get("from_code") == "en" and meta.get("to_code") == "zh":
            if (p.parent / "model" / "model.bin").is_file() and (p.parent / "sentencepiece.model").is_file():
                return p.parent
    raise RuntimeError("缺少英翻中模型，請先執行『安裝模型』。")


def setup():
    from faster_whisper.utils import download_model
    MODELS.mkdir(parents=True, exist_ok=True)
    if all((ASR / name).is_file() for name in ("model.bin", "config.json", "tokenizer.json", "vocabulary.txt")):
        print("英文辨識模型已存在，沿用已下載檔案。", flush=True)
    else:
        print("下載英文辨識模型 base.en（首次下載可能需數分鐘）...", flush=True)
        download_model("base.en", output_dir=str(ASR))
    try:
        model_dir()
    except RuntimeError:
        # Both addresses are published by the model provider. Retain the
        # known version/filename so a blocked index does not prevent repair.
        links = [
            "https://data.argosopentech.com/argospm/v1/translate-en_zh-1_9.argosmodel",
            "https://argos-net.com/v1/translate-en_zh-1_9.argosmodel",
        ]
        archive = MODELS / "en-zh.argosmodel"
        imported = MODELS / "translate-en_zh-1_9.argosmodel"
        if imported.is_file():
            if not zipfile.is_zipfile(imported):
                raise RuntimeError("手動下載的模型不是有效壓縮檔，請重新下載。")
            archive = imported
            print("使用手動下載的英翻中模型。", flush=True)
        elif not (archive.is_file() and zipfile.is_zipfile(archive)):
            download_translation(links, archive)
        MT.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(archive) as z:
            for member in z.infolist():
                target = (MT / member.filename).resolve()
                if not target.is_relative_to(MT.resolve()):
                    raise RuntimeError("模型壓縮檔含有不合法路徑。")
            z.extractall(MT)
        model_dir()
        if archive != imported:
            archive.unlink()
    print("驗證本機模型與翻譯...", flush=True)
    engine = Engine()
    translated = engine.translate("Welcome to today's technology talk.")
    if not translated:
        raise RuntimeError("翻譯模型未產生文字。")
    print(f"翻譯驗證：{translated}", flush=True)
    print("模型安裝完成，可離線使用。", flush=True)


def download_translation(links, archive):
    partial = archive.with_suffix(".part")
    failures = []
    for index, link in enumerate(links, 1):
        try:
            print(f"下載英翻中模型（來源 {index}/{len(links)}）...", flush=True)
            request = urllib.request.Request(link, headers={"User-Agent": "EarSubtitle/1.1 (local model installer)"})
            with urllib.request.urlopen(request, timeout=60) as response, partial.open("wb") as output:
                received = 0
                while block := response.read(1024 * 1024):
                    output.write(block)
                    received += len(block)
                    if received // (5 * 1024 * 1024) != (received - len(block)) // (5 * 1024 * 1024):
                        print(f"英翻中模型已下載 {received / 1024 / 1024:.0f} MB", flush=True)
            if not zipfile.is_zipfile(partial):
                raise RuntimeError("伺服器未回傳有效模型檔案。")
            partial.replace(archive)
            return
        except Exception as exc:
            failures.append(str(exc))
            print(f"來源 {index} 無法下載：{exc}", flush=True)
    raise RuntimeError("翻譯模型下載失敗：" + "; ".join(failures)
                       + "。可將手動下載的 translate-en_zh-1_9.argosmodel 放入 models 資料夾後重試。")


class Engine:
    def __init__(self):
        # Never contact model services during transcription/translation.
        os.environ["HF_HUB_OFFLINE"] = "1"
        os.environ["TRANSFORMERS_OFFLINE"] = "1"
        from faster_whisper import WhisperModel
        import ctranslate2
        import sentencepiece
        from opencc import OpenCC
        if not (ASR / "model.bin").is_file():
            raise RuntimeError("缺少英文辨識模型，請先執行『安裝模型』。")
        mt = model_dir()
        self.meta = json.loads((mt / "metadata.json").read_text(encoding="utf-8"))
        threads = min(4, max(1, (os.cpu_count() or 2) // 2))
        self.asr = WhisperModel(str(ASR), device="cpu", compute_type="int8", cpu_threads=threads,
                                local_files_only=True)
        self.tokenizer = sentencepiece.SentencePieceProcessor(model_file=str(mt / "sentencepiece.model"))
        self.mt = ctranslate2.Translator(str(mt / "model"), device="cpu", compute_type="int8", intra_threads=threads)
        self.opencc = OpenCC("s2twp")

    def translate(self, text):
        if not text.strip():
            return ""
        sentences = [s.strip() for s in re.split(r"(?<=[.!?])\s+", text) if s.strip()]
        tokens = [self.tokenizer.encode(s, out_type=str) for s in sentences]
        prefix = self.meta.get("target_prefix", "")
        kwargs = {"target_prefix": [[prefix]] * len(tokens)} if prefix else {}
        results = self.mt.translate_batch(tokens, beam_size=2, replace_unknowns=True,
                                          max_decoding_length=256, length_penalty=0.2, **kwargs)
        pieces = [self.tokenizer.decode(r.hypotheses[0]).removeprefix(prefix).strip() for r in results]
        return self.opencc.convert("".join(pieces))

    def transcribe(self, audio):
        segments, _ = self.asr.transcribe(audio, language="en", beam_size=1, vad_filter=True,
                                           condition_on_previous_text=False,
                                           vad_parameters={"min_silence_duration_ms": 500})
        return " ".join(s.text.strip() for s in segments if s.no_speech_prob < 0.6).strip()


def worker():
    engine = Engine()
    emit(type="ready")
    import base64
    import numpy as np
    for line in sys.stdin:
        request = {}
        try:
            request = json.loads(line)
            began = time.monotonic()
            if "text" in request:
                english = request["text"]
            else:
                # PCM goes over an anonymous local pipe; no meeting recordings are saved.
                raw = base64.b64decode(request["pcm"], validate=True)
                audio = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0
                if not 0 < audio.size <= 16000 * 30:
                    raise ValueError("音訊長度必須介於 0～30 秒。")
                english = engine.transcribe(audio)
            emit(type="result", id=request["id"], en=english, zh=engine.translate(english),
                 elapsed=round(time.monotonic() - began, 2))
        except Exception as exc:
            emit(type="error", id=request.get("id"), message=str(exc))


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("--setup", action="store_true")
    parser.add_argument("--worker", action="store_true")
    args = parser.parse_args()
    try:
        if args.setup:
            setup()
        elif args.worker:
            worker()
        else:
            parser.print_help()
    except Exception as exc:
        if args.worker:
            message = f"辨識套件尚未安裝完成，請先按『安裝模型』。詳細原因：{exc}" if isinstance(exc, ModuleNotFoundError) else str(exc)
            emit(type="fatal", message=message)
        else:
            print(f"安裝失敗：{exc}", file=sys.stderr, flush=True)
        sys.exit(1)
