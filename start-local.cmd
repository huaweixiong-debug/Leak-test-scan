@echo off
setlocal
cd /d "%~dp0"

where node >nul 2>nul
if errorlevel 1 (
  echo.
  echo [ERROR] Node.js was not found.
  echo Please install Node.js 18 or newer, then run this file again.
  echo Download: https://nodejs.org/
  echo.
  pause
  exit /b 1
)

node -e "const major=Number(process.versions.node.split('.')[0]); if(major<18){process.exit(1)}" >nul 2>nul
if errorlevel 1 (
  echo.
  echo [ERROR] Node.js 18 or newer is required.
  node -v
  echo Download: https://nodejs.org/
  echo.
  pause
  exit /b 1
)

if not exist "node_modules" (
  echo.
  echo [INFO] Installing dependencies. This may take a few minutes...
  call npm install
  if errorlevel 1 (
    echo.
    echo [ERROR] npm install failed. Check the network connection or npm registry.
    echo.
    pause
    exit /b 1
  )
)

echo.
echo [INFO] Running setup check...
call npm run doctor
if errorlevel 1 (
  echo.
  echo [ERROR] Setup check failed. Please fix the items above.
  echo.
  pause
  exit /b 1
)

echo.
echo [INFO] Starting service: http://127.0.0.1:3000
echo Press Ctrl+C to stop.
echo.
call npm start
