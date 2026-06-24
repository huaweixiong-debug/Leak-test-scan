$b='http://127.0.0.1:3000'
function Post($ep,$body){try{Invoke-RestMethod "$b$ep" -Method Post -Body $body -ContentType 'application/json'|Out-Null}catch{Write-Host "POST $ep FAIL: $_"}}
function Get($ep){try{return Invoke-RestMethod "$b$ep"}catch{return $null}}

# SETUP
Post '/api/config/ateq' '{"comPort":"MOCK_ATEQ","baudrate":9600,"dataBits":8,"parity":"none","stopBits":1,"slaveId":1,"timeoutMs":5000,"pollIntervalMs":500,"dtr":true,"rts":true,"enabled":true}'
Post '/api/config/scanner' '{"comPort":"MOCK_SCANNER","baudrate":9600,"dataBits":8,"parity":"none","stopBits":1,"timeoutMs":5000,"pollIntervalMs":100,"dtr":true,"rts":true,"enabled":true}'
Post '/api/settings/products' '{"products":[{"productModel":"TP","ateqProgramNo":1,"qrKeyword":"T","isActive":true,"scanConfirmEnabled":false}]}'
Post '/api/debug/mock/ateq/next-result' '{"resultCode":"OK"}'
Start-Sleep 1

# === TEST1: Manual OK ===
Write-Host '--- TEST1 OK ---'
$r=Post '/api/start' '{"productModel":"TP","startMode":"manual"}'
for($i=0;$i -lt 15;$i++){Start-Sleep 0.5;$a=Get '/api/test/active';$s=$a.activeTest.stage;Write-Host "  $s";if($s -eq 'completed'){break}}
$r=Get '/api/tests/latest'
Write-Host "OK RESULT: total=$($r.total) rc=$($r.records[0].resultCode) sw=$($r.records[0].rawStatusWord)"

# === RESET ===
Post '/api/reset' '{}'
Start-Sleep 1

# === TEST2: Manual NG (TRACE THE GATE) ===
Write-Host '--- TEST2 NG GATE TRACE ---'
Post '/api/debug/mock/ateq/next-result' '{"resultCode":"NG","errorCode":"ATEQ_ALARM"}'
Start-Sleep 0.5

# Try start - capture response
try {
    $startResp = Invoke-RestMethod "$b/api/start" -Method Post -Body '{"productModel":"TP","startMode":"manual"}' -ContentType 'application/json'
    Write-Host "Start response: success=$($startResp.success) msg=$($startResp.message)"
} catch {
    Write-Host "Start FAILED: $_"
    $errResp = $_.Exception.Response
    if ($errResp) {
        $reader = New-Object System.IO.StreamReader($errResp.GetResponseStream())
        Write-Host "Error body: $($reader.ReadToEnd())"
    }
}

# Poll active test
for($i=0;$i -lt 20;$i++){
    Start-Sleep 0.5
    $a=Get '/api/test/active'
    $at=$a.activeTest
    Write-Host "  +$($i*0.5)s stage=$($at.stage) running=$($at.running) msg=$($at.message)"
    if($at.stage -eq 'completed' -or $at.stage -eq 'failed'){break}
}

# Check status (what does the ATEQ report?)
$s=Get '/api/status'
Write-Host "Status: step=$($s.currentStep) err=$($s.errorCode)"

# Records
$r=Get '/api/tests/latest'
Write-Host "NG RESULT: total=$($r.total)"
if($r.records.Count -gt 0){Write-Host "  rc=$($r.records[0].resultCode) ec=$($r.records[0].errorCode) sw=$($r.records[0].rawStatusWord)"}

Write-Host 'DONE'
