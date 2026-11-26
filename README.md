# EarSubtitle：耳機英文 → 繁體中文字幕

Windows x64 桌面程式：擷取耳機或喇叭播放的英文，以本機模型辨識、翻譯成繁體中文字幕，適用於 Teams 或瀏覽器播放的英文內容。

## 本次發布的是輕量版

Release 的 `EarSubtitle_v1_windows_source.zip` 約 0.12 MB，包含 C#／Python 原始碼與已編譯 Windows 介面。**不含模型、Python 或 .NET 執行環境，不是未準備環境的電腦解壓後就能使用的完整離線包。**

Repository 保存原始碼、測試、腳本與文件；Releases 提供原始輕量 ZIP 及 SHA-256。本次未發布另外製作的完整可攜版。

## 使用方法

1. 安裝 .NET 10 SDK（包含 Windows Desktop Runtime），以及 Python 3.10～3.12 x64。
2. 下載並解壓縮輕量 ZIP，保留整個資料夾，雙擊 `Launch.cmd`。
3. 必要時按「選擇 Python」指定自己的 `python.exe`。第一次按「① 安裝模型」，建立獨立 `.venv` 並下載套件、英文辨識及翻譯模型。
4. 選擇聲音實際播放到的耳機／喇叭，播放英文並按「測試音源（5 秒）」。插入耳機後可按「重新整理」。
5. 測試通過後按「② 開始字幕」。支援浮動字幕、停止與 SRT 匯出；再次開始會清除上一場字幕。

Realtek 耳機可能顯示為 `Realtek HD Audio 2nd output`。如果選到喇叭、聲音卻走耳機，會出現 `0 samples`，請改選實際播放的裝置。

首次安裝需連線 PyPI、Hugging Face 與 Argos 模型站。模型備妥後，辨識與翻譯在本機執行，不使用麥克風、不上傳音訊、不保存錄音。只有匯出才儲存字幕。

擷取的是所選播放裝置的混合聲音，可能包含通知等其他程式聲音。按停頓或最長約六秒的語音片段處理，另加 CPU 運算時間，不是逐字同步字幕。

## 架構

| 工作 | 技術 |
|---|---|
| 介面、聲音擷取、浮動字幕、SRT | C# / .NET 10 / WASAPI |
| 英文語音辨識 | Python / faster-whisper base.en / CPU INT8 |
| 英翻中 | Argos en→zh / CTranslate2 / SentencePiece |
| 繁體字與用語轉換 | OpenCC s2twp |

## 重新建置與驗證

安裝 .NET 10 SDK 後，在專案目錄執行 `Build.cmd`，再從 `Launch.cmd` 開啟。C# 沒有外部 NuGet 相依套件，Python 套件與模型另行安裝。

```powershell
dotnet restore tests/Tests.csproj --configfile NuGet.Config -p:NuGetAudit=false
dotnet run --project tests --no-restore
python tests/test_download.py
Get-FileHash .\EarSubtitle_v1_windows_source.zip -Algorithm SHA256
```

ZIP 的 SHA-256 應與 Release 的 `.sha256` 附件一致：

```text
caf3ba1279d19e65ae9a8c0d7c99a5312a5dddb06cfb6c3d87d36eca407ec1cb
```

既有測試涵蓋音訊分段、PCM、SRT、403 下載備援及拒絕把 HTML 錯誤頁當模型。此版曾在 Windows x64 實際擷取播放聲音並顯示英文、繁體中文字幕；不宣稱每種電腦皆已驗證。`VALIDATION.md` 保留初次建置紀錄，其中模型下載阻礙為當時狀態，之後已透過官方備援來源完成安裝與字幕實測。

## 第三方來源

- https://github.com/SYSTRAN/faster-whisper
- https://huggingface.co/Systran/faster-whisper-base.en
- https://github.com/argosopentech/argospm-index
- https://data.argosopentech.com/argospm/v1/
- https://github.com/yichen0831/opencc-python

各第三方套件及模型保留其自身授權；本儲存庫不包含模型權重。
