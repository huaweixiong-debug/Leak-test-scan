@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$conns = Get-NetTCPConnection -LocalPort 3000 -State Listen -ErrorAction SilentlyContinue; if ($conns) { Write-Host '[WARN] port 3000 is already in use. Run stop-remote-server.cmd first.'; $conns | Format-Table -AutoSize; exit 1 }"
if errorlevel 1 exit /b 1
REM Rotate logs (keep 3 versions)
if exist server.out.2 del /q server.out.2
if exist server.out.1 ren server.out.1 server.out.2 2>nul
if exist server.out ren server.out server.out.1 2>nul
if exist server.err.2 del /q server.err.2
if exist server.err.1 ren server.err.1 server.err.2 2>nul
if exist server.err ren server.err server.err.1 2>nul
wscript.exe //B "%~dp0start_vbs.vbs"
ping -n 4 127.0.0.1 >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "$health = Invoke-RestMethod -Uri 'http://127.0.0.1:3000/api/health' -TimeoutSec 5 -ErrorAction SilentlyContinue; if ($health) { Write-Host ('[INFO] started: ' + $health.build); exit 0 }; Write-Host '[ERROR] service did not respond. Check server.err'; exit 1"
