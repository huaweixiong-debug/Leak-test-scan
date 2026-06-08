@echo off
for /f "tokens=5" %%i in ('netstat -ano ^| findstr ":3000" ^| findstr "LISTENING"') do taskkill /pid %%i /f >nul 2>nul
echo stopped
