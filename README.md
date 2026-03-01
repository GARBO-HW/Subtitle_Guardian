# Subtitle Guardian (字幕御守)

**Subtitle Guardian (字幕御守)** 是一個專為影片創作者的字幕工具。本應用程式整合了語音識別技術 (ASR)，旨在協助使用者輕鬆生成高品質字幕，並提供精確的時間軸對齊功能，大幅縮短繁瑣的字幕製作流程。

## ✨ 主要功能

### 1. 🎙️ 自動語音轉錄 (Automatic Speech Recognition)
利用 OpenAI Whisper 模型，將音訊或影片檔案自動轉錄為文字或字幕檔。
- **支援格式**：常見的音訊與影片格式 (如 MP3, WAV, MP4, MKV, M4A, AAC, FLAC, WEBM 等)。
- **輸出格式**：SRT 字幕檔、TXT 純文字檔。
- **多語言支援**：支援自動偵測語言，或手動選擇中文（繁體/簡體）、英文、日文、韓文等。
- **模型選擇**：有 Tiny, Base, Small, Medium, Large 等不同大小的模型(目前提供Medium模型，其他的自行下載)，讓使用者依據硬體效能與準確度需求自由切換。

### 2. ⏱️ 字幕對齊 文稿匹配 (Subtitle Alignment)
將現有的文字稿與音訊/影片進行時間軸對齊，生成精確的字幕檔。這對於已經擁有逐字稿但缺乏時間軸的情況非常實用。
- **輸入來源**：
  - 音訊/影片檔案：支援拖曳匯入。
  - 正確文字稿：支援匯入文字檔 (.txt, .srt, .md, .csv) 或直接貼上文字內容。
- **參數設定**：可設定語言及單行最大字數，確保字幕閱讀體驗與排版美觀。
- **自動對齊**：自動分析語音波形與文字內容的對應關係，生成帶有精確時間軸的 SRT 檔案。

### 3. 📋 任務管理
- **背景執行**：支援多工處理，可同時進行多個轉錄或對齊任務，不影響前台操作。
- **進度監控**：即時顯示任務進度、耗時與狀態，讓您隨時掌握處理狀況。
- **任務控制**：可隨時取消執行中的任務。

## 🛠️ 技術架構

本專案採用 .NET (WPF) 開發，並遵循分層架構 (Layered Architecture) 設計，確保程式碼的可維護性與擴充性：
- **SubtitleGuardian.App**: WPF 使用者介面層 (MVVM Pattern)。
- **SubtitleGuardian.Application**: 應用程式邏輯層 (Use Cases, Services)。
- **SubtitleGuardian.Domain**: 領域模型與介面定義 (Core Business Logic)。
- **SubtitleGuardian.Infrastructure**: 基礎設施層 (檔案系統存取、外部服務整合)。
- **SubtitleGuardian.Engines**: ASR 引擎實作 (整合 Whisper)。

## 💻 系統需求

- **作業系統**: Windows 10 / 11 (64-bit)
- **Runtime**: .NET Runtime (建議安裝最新版本)
- **硬體建議**: 
  - 建議配備獨立顯示卡 (NVIDIA GPU) 以加速 Whisper 模型推論。
  - 較大的模型 (如 Medium, Large) 需要較多的系統記憶體 (RAM)。

## 🚀 快速開始

1. **下載**: 從 Release 頁面下載最新版本的 `SubtitleGuardian`。
2. **執行**: 解壓縮後執行 `SubtitleGuardian.App.exe`。
3. **轉錄**: 切換至「ASR」分頁，拖曳您的媒體檔案，選擇語言與模型，點擊「開始轉錄」。
4. **對齊**: 若已有文稿，切換至「Alignment」分頁，分別匯入音訊與文字檔，點擊「開始匹配」。

## 🤝 參與開發

若您希望參與開發或自行編譯：
1. 確保已安裝 .NET SDK。
2. 使用 Visual Studio 或 Rider 開啟解決方案。
3. 編譯並執行 `SubtitleGuardian.App` 專案。

---
*註：本專案使用 OpenAI Whisper 模型進行語音識別，準確度取決於音訊品質與模型大小。*
