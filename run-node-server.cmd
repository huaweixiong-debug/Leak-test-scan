@echo off
setlocal
cd /d "%~dp0"
where node >nul 2>nul
if errorlevel 1 (
  echo Node.js 18+ is required. Download: https://nodejs.org/
  exit /b 1
)
if not exist "node_modules" call npm install
call npm start 1>>"%~dp0server.out" 2>>"%~dp0server.err"
