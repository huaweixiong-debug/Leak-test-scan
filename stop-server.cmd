@echo off
setlocal

echo Stopping ATEQ Leak Test server...

schtasks /End /TN "ATEQ-LeakTest-Server" >nul 2>&1
schtasks /Delete /TN "ATEQ-LeakTest-Server" /F >nul 2>&1

powershell -NoProfile -ExecutionPolicy Bypass -Command "$targets = Get-CimInstance Win32_Process | Where-Object { $_.Name -ieq 'ATEQ.LeakTest.Web.exe' -or ($_.Name -ieq 'dotnet.exe' -and $_.CommandLine -like '*ATEQ.LeakTest.Web*') }; if (-not $targets) { Write-Output 'No running ATEQ server found.'; exit 0 }; foreach ($target in $targets) { Stop-Process -Id $target.ProcessId -Force -ErrorAction SilentlyContinue; Write-Output ('Stopped PID ' + $target.ProcessId + ' (' + $target.Name + ')') }"

endlocal
