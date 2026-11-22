"use strict";

const http = require("http");
const fs = require("fs");
const path = require("path");
const executeHandler = require("./api/execute");

// 讀取本地 .env 環境變數 (不輸出任何金鑰內容)
function loadDotEnv() {
  const envPath = path.join(__dirname, ".env");
  if (!fs.existsSync(envPath)) return;
  const lines = fs.readFileSync(envPath, "utf8").split(/\r?\n/);
  for (const raw of lines) {
    const line = raw.trim();
    if (!line || line.startsWith("#")) continue;
    const eq = line.indexOf("=");
    if (eq <= 0) continue;
    const key = line.slice(0, eq).trim();
    let val = line.slice(eq + 1).trim();
    if ((val.startsWith('"') && val.endsWith('"')) || (val.startsWith("'") && val.endsWith("'"))) {
      val = val.slice(1, -1);
    }
    if (!process.env[key]) {
      process.env[key] = val;
    }
  }
}

loadDotEnv();

const MIME_TYPES = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "application/javascript; charset=utf-8",
  ".ico": "image/x-icon"
};

const wwwroot = path.join(__dirname, "wwwroot");

const server = http.createServer(async (req, res) => {
  const urlPath = (req.url || "/").split("?")[0];

  if (urlPath === "/api/execute") {
    await executeHandler(req, res);
    return;
  }

  if (req.method !== "GET" && req.method !== "HEAD") {
    res.statusCode = 405;
    res.end();
    return;
  }

  const relFile = urlPath === "/" ? "index.html" : urlPath.replace(/^\/+/, "");
  const safePath = path.normalize(path.join(wwwroot, relFile));

  if (!safePath.startsWith(wwwroot) || !fs.existsSync(safePath) || fs.statSync(safePath).isDirectory()) {
    res.statusCode = 404;
    res.end();
    return;
  }

  const ext = path.extname(safePath).toLowerCase();
  res.statusCode = 200;
  res.setHeader("Content-Type", MIME_TYPES[ext] || "application/octet-stream");
  fs.createReadStream(safePath).pipe(res);
});

const port = Number(process.env.PORT) || 5000;
server.listen(port, "0.0.0.0", () => {
  console.log(`[✦] 星輝服務已啟動 ▸ http://localhost:${port}`);
});
