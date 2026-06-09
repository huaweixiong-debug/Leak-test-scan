@echo off
setlocal
cd /d "%~dp0"

echo [INFO] Force stopping anything listening on port 3000...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$conns = Get-NetTCPConnection -LocalPort 3000 -State Listen -ErrorAction SilentlyContinue; if ($conns) { foreach ($conn in $conns) { Write-Host ('[INFO] killing PID ' + $conn.OwningProcess); Stop-Process -Id $conn.OwningProcess -Force -ErrorAction SilentlyContinue } } else { Write-Host '[INFO] port 3000 is already free' }; Start-Sleep -Seconds 2; $left = Get-NetTCPConnection -LocalPort 3000 -State Listen -ErrorAction SilentlyContinue; if ($left) { Write-Host '[ERROR] port 3000 is still occupied. Run this file as Administrator.'; $left | Format-Table -AutoSize; exit 1 }"
if errorlevel 1 pause & exit /b 1

echo [INFO] Starting backend...
call "%~dp0start-remote-server.cmd"
if errorlevel 1 (
  echo [ERROR] start failed. Last server.err:
  if exist "%~dp0server.err" type "%~dp0server.err"
  pause
  exit /b 1
)

echo [INFO] Checking health...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$health = Invoke-RestMethod -Uri 'http://127.0.0.1:3000/api/health' -TimeoutSec 8 -ErrorAction Stop; $health | ConvertTo-Json -Depth 5"
if errorlevel 1 (
  echo [ERROR] health check failed. Last server.err:
  if exist "%~dp0server.err" type "%~dp0server.err"
  pause
  exit /b 1
)

echo [INFO] Restart completed.
pause
