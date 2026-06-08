@echo off
cd /d "D:\ATEQ Test\ATEQ-Leak-Test"
call stop-remote-server.cmd >nul 2>nul
if exist server.out del /q server.out
if exist server.err del /q server.err
start "ateq-backend" /b runtime18\node-v18.20.8-win-x64\node.exe server.js 1>server.out 2>server.err
ping -n 8 127.0.0.1 >nul
powershell -NoProfile -Command "try { (Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:3000/api/health' -TimeoutSec 5).Content } catch { $_.Exception.Message }"
powershell -NoProfile -Command "$body = @{ products = @(@{ productModel = 'HB11'; ateqProgramNo = 1; qrKeyword = 'HB11'; isActive = $true }) } | ConvertTo-Json -Depth 5; try { (Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:3000/api/settings/products' -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 5).Content } catch { $_.Exception.Message }"
powershell -NoProfile -Command "try { (Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:3000/api/settings/products' -TimeoutSec 5).Content } catch { $_.Exception.Message }"
powershell -NoProfile -Command "try { (Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:3000/api/test/active' -TimeoutSec 5).Content } catch { $_.Exception.Message }"
powershell -NoProfile -Command "$body = @{ qrCode = 'NO_MATCH'; startMode = 'scan' } | ConvertTo-Json; try { (Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:3000/api/start' -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 5).Content } catch { if ($_.Exception.Response) { 'START_STATUS=' + [int]$_.Exception.Response.StatusCode } else { $_.Exception.Message } }"
call stop-remote-server.cmd >nul 2>nul
