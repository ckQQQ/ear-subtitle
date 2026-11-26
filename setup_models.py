"""Creates a private venv; never changes the user's existing Conda packages."""
from pathlib import Path
import os
import subprocess
import sys
import venv

root = Path(__file__).resolve().parent
python = root / ".venv" / "Scripts" / "python.exe"
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

try:
    if sys.version_info < (3, 10):
        raise RuntimeError("需要 Python 3.10 以上，建議 Python 3.10～3.12 x64。")
    if not python.exists():
        print("建立獨立 Python 環境...", flush=True)
        venv.create(root / ".venv", with_pip=True)
    env = os.environ.copy()
    env["PYTHONUTF8"] = "1"
    env["PIP_CACHE_DIR"] = str(root / ".cache" / "pip")
    env["PIP_CONFIG_FILE"] = os.devnull
    env["PIP_INDEX_URL"] = "https://pypi.org/simple"
    env.pop("PIP_EXTRA_INDEX_URL", None)
    print("安裝本機辨識與翻譯套件...", flush=True)
    subprocess.run([str(python), "-m", "pip", "install", "--disable-pip-version-check", "--timeout", "25", "--retries", "1",
                    "-r", str(root / "requirements.txt")], check=True, env=env)
    subprocess.run([str(python), "-u", str(root / "backend.py"), "--setup"], check=True, env=env)
except Exception as exc:
    print(f"安裝未完成：{exc}\n請檢查網路是否能連線 PyPI、Hugging Face 與 Argos 模型站。", flush=True)
    sys.exit(1)
