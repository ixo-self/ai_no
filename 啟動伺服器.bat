@echo off
chcp 65001 >nul
cd /d "%~dp0"
title Starlight-C# Server
if not exist ".env" (
    copy /Y ".env.example" ".env" >nul
    echo [!] Please set GEMINI_API_KEY in .env first.
    pause
    exit /b 1
)
where dotnet >nul 2>nul
if %errorlevel% equ 0 (
    dotnet run --project StarlightAI.csproj
) else (
    node server.js
)
pause
