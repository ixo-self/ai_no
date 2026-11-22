# ai_no

星輝編碼 (Starlight-C#) 沙盒執行終端。

---

## ✦ 環境變數設定

於部署平台（Render / Vercel / Railway / Docker）或本地 `.env` 設定以下環境變數：

```ini
GEMINI_API_KEY=your_gemini_api_key_here
PORT=5000
```

- `GEMINI_API_KEY` 僅存放於伺服器端環境變數，所有 API 調用皆由伺服器內部封裝完成。
- 用戶端僅傳送星輝編碼並接收最終純文字輸出結果，無任何 Debug 或環境資訊回傳。

---

## ✦ 啟動方式

### 方案 A：.NET 8 (C# 原生伺服器)
```bash
dotnet run --project StarlightAI.csproj
```

### 方案 B：Node.js / Vercel
```bash
npm start
```

### 方案 C：Windows 一鍵啟動
雙擊執行 `啟動伺服器.bat`，服務預設運行於 `http://localhost:5000`。
