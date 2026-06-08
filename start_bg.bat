@echo off
cd /d "%~dp0"
where node >nul 2>nul || exit /b 1
if not exist "node_modules" call npm install
start "" /B cmd /c "npm start 1>>server.out 2>>server.err"
