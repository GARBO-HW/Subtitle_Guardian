# ModelManager 目錄規範（Whisper）

## 目標

- 統一模型與 runtime 的落地路徑，方便下載/校驗/清理
- 版本可回溯：每個資產都有 version 與校驗資訊
- 可跨機器搬移：整個 models 目錄可直接複製使用

## Root

以 App 的資料目錄（AppData/Local 或可攜模式目錄）為 Root：

- `models/`：模型資產（whisper）
- `runtime/`：推論 runtime（例如 whispercpp）
- `cache/`：暫存（下載中、解壓中、臨時音訊等）

## 目錄結構（建議）

```
root/
  models/
    whisper/
      {modelId}/
        manifest.json
        files/
          ggml-model.bin
          tokenizer.json
  runtime/
    whispercpp/
      {version}/
        manifest.json
        bin/
          whisper-cli.exe
          main.exe
  cache/
    downloads/
    temp/
```

命名：

- `{modelId}`：可用 `name@version` 或 `name-version`（避免空白）
- `files/`：只放實際檔案（便於校驗與清理）
- `manifest.json`：描述與校驗（唯一資料來源）

補充：

- whisper.cpp runtime 為可執行檔集合（`bin/`），不一定需要 `files/`

## manifest.json（MVP）

所有資產共用 schema：

```json
{
  "schema": 1,
  "kind": "model",
  "engine": "whisper",
  "id": "whisper-small@1",
  "version": "1",
  "createdAt": "2026-02-28T00:00:00Z",
  "files": [
    {
      "path": "files/ggml-model.bin",
      "size": 123456789,
      "sha256": "…"
    }
  ]
}
```

欄位：

- `schema`：manifest 版本
- `kind`：`model` 或 `runtime`
- `engine`：`whisper`
- `id`：資產識別（顯示/索引用）
- `version`：字串版本（排序/顯示）
- `files[]`：相對於資產根目錄的檔案清單與校驗資訊

## 狀態與流程（MVP）

- 下載：
  - 先寫入 `cache/downloads/{jobId}.partial`
  - 下載完成後校驗 sha256
  - 解壓/搬移到目標 `{modelId}/files/`
  - 最後一步寫入 `manifest.json`（atomic：先寫 .tmp 再 rename）
- 校驗：
  - 以 `manifest.json` 為準，逐檔檢查 size/sha256
  - 校驗失敗：標記為 Corrupted，提示重下載
- 清理：
  - 依 `manifest.json` 推算占用空間
  - 刪除 `{modelId}/` 整個目錄即可

## UI 行為（對應驗收）

- 未下載模型：不允許開始任務，顯示「需要下載」並提供下載入口
- 缺檔/校驗失敗：顯示「模型損壞」，提供修復（重下載）與刪除
- 顯示占用空間：依 manifest 加總 size
