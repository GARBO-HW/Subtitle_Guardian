# IPC 協定（UI ↔ Whisper Worker）

## 目標

- UI 與 Worker 分離：Worker 崩潰不拖垮 UI，可重啟
- 單一 worker 可同時處理多 job（預設可先序列化）
- 支援：Start / Progress / Result / Cancel / Error
- 支援：版本化、擴充欄位、向後相容

## 傳輸層（MVP）

- Worker 以獨立 Process 啟動
- 使用 stdin/stdout 傳遞 JSON Lines（每行一個 JSON 物件，以 `\n` 結束）
- stderr 只輸出診斷文字（不走協定）

## 訊息格式

所有訊息共用外層：

```json
{
  "v": 1,
  "type": "Start",
  "jobId": "0f2f6a8b-6ed5-4da6-a4d3-9a1a2d83f2c1",
  "ts": "2026-02-28T12:34:56.789Z",
  "payload": {}
}
```

欄位：

- `v`：協定版本（整數）
- `type`：訊息種類（字串）
- `jobId`：工作識別（UUID）
- `ts`：時間戳（ISO 8601，UTC）
- `payload`：各 type 專屬內容（JSON 物件）

## 訊息種類

### Start（UI → Worker）

啟動一個轉錄任務。

```json
{
  "v": 1,
  "type": "Start",
  "jobId": "…",
  "ts": "…",
  "payload": {
    "audioPath": "C:\\path\\to\\audio.wav",
    "options": {
      "language": "zh",
      "quality": "Balanced",
      "enableWordTimestamps": false
    }
  }
}
```

規則：

- `audioPath` 必須是可讀取的本機路徑
- `options` 以 Domain 的 `TranscribeOptions` 為準（worker 允許忽略未知欄位）

### Cancel（UI → Worker）

請求取消指定 job。

```json
{
  "v": 1,
  "type": "Cancel",
  "jobId": "…",
  "ts": "…",
  "payload": {}
}
```

規則：

- 若 job 尚未開始：worker 應直接標記為取消並回報 `Error` 或 `Result`（建議用 `Error`，code=JobCanceled）
- 若 job 執行中：worker 應儘速停止推論，釋放模型/檔案/記憶體等資源

### Progress（Worker → UI）

回報進度與訊息。

```json
{
  "v": 1,
  "type": "Progress",
  "jobId": "…",
  "ts": "…",
  "payload": {
    "percent": 42,
    "message": "Decoding…"
  }
}
```

規則：

- `percent`：0–100
- 可高頻回報，但 UI 端應做節流（例如 100ms 合併一次）

### Result（Worker → UI）

成功完成 job，回傳 segments。

```json
{
  "v": 1,
  "type": "Result",
  "jobId": "…",
  "ts": "…",
  "payload": {
    "segments": [{ "startMs": 0, "endMs": 1200, "text": "…" }]
  }
}
```

規則：

- `segments` 必須符合統一契約（不可負時間、不可重疊、end >= start）
- `words`（可選）：
  - 若 worker 有 word timestamps，可回傳 `words` 陣列（每個 segment 內）

### Error（Worker → UI）

失敗或無法完成（包含取消）。

```json
{
  "v": 1,
  "type": "Error",
  "jobId": "…",
  "ts": "…",
  "payload": {
    "code": "ModelMissing",
    "message": "Whisper model not found",
    "details": {
      "requiredAssets": ["models/whisper/…"]
    }
  }
}
```

建議錯誤碼（MVP）：

- `JobCanceled`
- `InvalidAudio`
- `ModelMissing`
- `ModelCorrupted`
- `OutOfMemory`
- `InternalError`

## Worker 生命週期（MVP）

- UI 啟動時可延後啟動 worker（lazy）
- UI 偵測 worker exit 時：
  - 將所有 Running/Pending job 標記失敗（code=WorkerCrashed）
  - 允許使用者重試（重新啟動 worker）
