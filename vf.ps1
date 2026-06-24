$b='http://127.0.0.1:3000'

function Post($ep,$body){Invoke-RestMethod "$b$ep" -Method Post -Body $body -ContentType 'application/json'|Out-Null}

# SETUP
Post '/api/config/ateq' '{"comPort":"MOCK_ATEQ","baudrate":9600,"dataBits":8,"parity":"none","stopBits":1,"slaveId":1,"timeoutMs":5000,"pollIntervalMs":500,"dtr":true,"rts":true,"enabled":true}'
Post '/api/settings/products' '{"products":[{"productModel":"TP","ateqProgramNo":1,"qrKeyword":"T","isActive":true,"scanConfirmEnabled":false}]}'
Post '/api/debug/mock/ateq/next-result' '{"resultCode":"OK"}'
Start-Sleep 1

# TEST1 OK
Post '/api/start' '{"productModel":"TP","startMode":"manual"}'
Start-Sleep 7
$r=Invoke-RestMethod "$b/api/tests/latest"
Write-Host "OK:rc=$($r.records[0].resultCode) sw=$($r.records[0].rawStatusWord)"

# RESET + TEST2 NG
Post '/api/reset' '{}'
Start-Sleep 1
Post '/api/debug/mock/ateq/next-result' '{"resultCode":"NG","errorCode":"ATEQ_ALARM"}'
Start-Sleep 0.5
Post '/api/start' '{"productModel":"TP","startMode":"manual"}'
Start-Sleep 7
$r=Invoke-RestMethod "$b/api/tests/latest"
Write-Host "NG:total=$($r.total) rc=$($r.records[0].resultCode) ec=$($r.records[0].errorCode) sw=$($r.records[0].rawStatusWord)"

# RESET + SCAN
Post '/api/reset' '{}'
Start-Sleep 1
Post '/api/settings/products' '{"products":[{"productModel":"TP","ateqProgramNo":1,"qrKeyword":"T","isActive":true,"scanConfirmEnabled":false},{"productModel":"SP","ateqProgramNo":2,"qrKeyword":"KW","isActive":true,"scanConfirmEnabled":true,"scanAutoStartEnabled":true,"scanMatchEnabled":true}]}'
Post '/api/debug/mock/ateq/next-result' '{"resultCode":"OK"}'
Start-Sleep 0.5
Post '/api/scanner/debug/mock-scan' '{"rawText":"PART-KW-002"}'
Start-Sleep 7
$r=Invoke-RestMethod "$b/api/tests/latest"
Write-Host "SCAN:total=$($r.total) rc=$($r.records[0].resultCode) sm=$($r.records[0].startMode) qr=$($r.records[0].qrCode)"

# REGRESSION
Post '/api/config/ateq' '{"comPort":"COM3","baudrate":9600,"dataBits":8,"parity":"none","stopBits":1,"slaveId":1,"timeoutMs":2000,"pollIntervalMs":500,"dtr":true,"rts":true,"enabled":true}'
Start-Sleep 3
$s=Invoke-RestMethod "$b/api/status"
Write-Host "REG:err=$($s.errorCode)"

Write-Host 'DONE'
