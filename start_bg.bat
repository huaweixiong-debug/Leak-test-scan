@echo off
cd /d "%~dp0"
set "NODE_EXE=%~dp0runtime18\node-v18.20.8-win-x64\node.exe"

if exist "%NODE_EXE%" (
  start "" /B cmd /c ""%NODE_EXE%" server.js 1>>"%~dp0server.out" 2>>"%~dp0server.err""
  exit /b 0
)

where node >nul 2>nul || exit /b 1
if not exist "node_modules" call npm install
start "" /B cmd /c "npm start 1>>"%~dp0server.out" 2>>"%~dp0server.err""
