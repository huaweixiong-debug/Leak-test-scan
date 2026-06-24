@echo off
setlocal

echo Stopping ATEQ Leak Test server...

:: Stop and remove the scheduled task (if any)
schtasks /End /TN "ATEQ-LeakTest-Server" >nul 2>&1
schtasks /Delete /TN "ATEQ-LeakTest-Server" /F >nul 2>&1

:: Kill by exe name
taskkill /F /IM ATEQ.LeakTest.Web.exe >nul 2>&1

:: Kill dotnet processes with ATEQ cmdline (PowerShell filter)
powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $targets = Get-CimInstance Win32_Process | Where-Object { $_.Name -ieq 'dotnet.exe' -and $_.CommandLine -like '*ATEQ.LeakTest.Web*' }; if (-not $targets) { Write-Output 'No running dotnet host found.'; exit 0 }; foreach ($target in $targets) { Stop-Process -Id $target.ProcessId -Force -ErrorAction SilentlyContinue; Write-Output ('Stopped PID ' + $target.ProcessId + ' (' + $target.Name + ')') } }"

endlocal
