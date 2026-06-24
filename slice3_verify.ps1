$ErrorActionPreference = 'Continue'
$base = 'http://127.0.0.1:3000'

function Post($ep, $body) {
    try { return Invoke-RestMethod -Uri "$base$ep" -Method Post -Body $body -ContentType 'application/json' }
    catch { return $_.Exception.Response }
}
function Get($ep) {
    try { return Invoke-RestMethod -Uri "$base$ep" -Method Get }
    catch { return $null }
}

# ============================================================
# Setup: mock ATEQ + scanner + product + operator
# ============================================================
Write-Host '=== SETUP ==='
Post '/api/config/ateq' '{"comPort":"MOCK_ATEQ","baudrate":9600,"dataBits":8,"parity":"none","stopBits":1,"slaveId":1,"timeoutMs":5000,"pollIntervalMs":500,"dtr":true,"rts":true,"enabled":true}' | Out-Null
Post '/api/config/scanner' '{"comPort":"MOCK_SCANNER","baudrate":9600,"dataBits":8,"parity":"none","stopBits":1,"timeoutMs":5000,"pollIntervalMs":100,"dtr":true,"rts":true,"enabled":true}' | Out-Null
Start-Sleep 1

Post '/api/settings/products' '{"products":[{"productModel":"TP","ateqProgramNo":1,"qrKeyword":"T","isActive":true,"scanConfirmEnabled":false}]}' | Out-Null
Post '/api/settings/operators' '{"operators":[{"name":"Op","isActive":true}]}' | Out-Null
Write-Host 'Setup done'

# ============================================================
# TEST 1: Manual OK
# ============================================================
Write-Host ''
Write-Host '=== TEST 1: Manual OK ==='
Post '/api/debug/mock/ateq/next-result' '{"resultCode":"OK"}' | Out-Null
Post '/api/start' '{"productModel":"TP","startMode":"manual"}' | Out-Null
Start-Sleep 6
$r = Get '/api/tests/latest'
$rec = $r.records[0]
Write-Host "Records: $($r.total)"
Write-Host "  resultCode=$($rec.resultCode) errorCode=$($rec.errorCode) rawStatusWord=$($rec.rawStatusWord)"
Write-Host "  finalPressure=$($rec.finalPressure) finalLeak=$($rec.finalLeak)"
if ($rec.resultCode -eq 'OK') { Write-Host 'TEST 1 PASS: Manual OK saved as OK' -ForegroundColor Green }
else { Write-Host "TEST 1 FAIL: expected OK got $($rec.resultCode)" -ForegroundColor Red }

# ============================================================
# TEST 2: Manual NG with ALARM
# ============================================================
Write-Host ''
Write-Host '=== TEST 2: Manual NG ==='
Post '/api/debug/mock/ateq/next-result' '{"resultCode":"NG","errorCode":"ATEQ_ALARM"}' | Out-Null
Post '/api/start' '{"productModel":"TP","startMode":"manual"}' | Out-Null
Start-Sleep 6
$r = Get '/api/tests/latest'
$rec = $r.records[0]
Write-Host "Records: $($r.total)"
Write-Host "  resultCode=$($rec.resultCode) errorCode=$($rec.errorCode) rawStatusWord=$($rec.rawStatusWord)"
if ($rec.resultCode -eq 'NG' -and $rec.errorCode -eq 'ATEQ_ALARM') { Write-Host 'TEST 2 PASS: NG with ALARM saved correctly' -ForegroundColor Green }
else { Write-Host "TEST 2 FAIL: expected NG/ATEQ_ALARM got $($rec.resultCode)/$($rec.errorCode)" -ForegroundColor Red }

# ============================================================
# TEST 3: Scan-triggered mock flow
# ============================================================
Write-Host ''
Write-Host '=== TEST 3: Scan-triggered flow ==='
# Add scan-enabled product
Post '/api/settings/products' '{"products":[{"productModel":"TP","ateqProgramNo":1,"qrKeyword":"T","isActive":true,"scanConfirmEnabled":false},{"productModel":"SP","ateqProgramNo":2,"qrKeyword":"KW","isActive":true,"scanConfirmEnabled":true,"scanAutoStartEnabled":true,"scanMatchEnabled":true}]}' | Out-Null
Post '/api/debug/mock/ateq/next-result' '{"resultCode":"OK"}' | Out-Null
Post '/api/reset' '{}' | Out-Null
Start-Sleep 1

# Inject scan that matches SP product (keyword KW)
$scanResult = Post '/api/scanner/debug/mock-scan' '{"rawText":"PART-KW-002"}'
Write-Host "Scan inject: success=$($scanResult.success)"

Start-Sleep 6
$r = Get '/api/tests/latest'
$rec = $r.records[0]
Write-Host "Records: $($r.total)"
Write-Host "  resultCode=$($rec.resultCode) productModel=$($rec.productModel) startMode=$($rec.startMode) qrCode=$($rec.qrCode)"
if ($rec.startMode -eq 'scan' -and $rec.qrCode -like '*KW*') { Write-Host 'TEST 3 PASS: Scan-triggered test saved with scan context' -ForegroundColor Green }
else { Write-Host "TEST 3 FAIL: startMode=$($rec.startMode) qrCode=$($rec.qrCode)" -ForegroundColor Red }

# ============================================================
# REGRESSION: Switch to COM3, verify real-path not polluted
# ============================================================
Write-Host ''
Write-Host '=== REGRESSION: Real-path COM3 ==='
Post '/api/config/ateq' '{"comPort":"COM3","baudrate":9600,"dataBits":8,"parity":"none","stopBits":1,"slaveId":1,"timeoutMs":2000,"pollIntervalMs":500,"dtr":true,"rts":true,"enabled":true}' | Out-Null
Start-Sleep 2

$status = Get '/api/status'
Write-Host "Status: errorCode=$($status.errorCode)"
if ($status.errorCode -like '*Modbus response too short*' -or $status.errorCode -like '*serial connect failed*') {
    Write-Host 'REGRESSION PASS: Real-path returns real serial error, not mock data' -ForegroundColor Green
} elseif ($status.errorCode -like '*not enabled*') {
    Write-Host 'REGRESSION PASS: Not enabled (no COM3 config applied)' -ForegroundColor Yellow
} else {
    Write-Host "REGRESSION WARN: unexpected errorCode=$($status.errorCode)" -ForegroundColor Yellow
}

$cfg = Get '/api/config/ateq'
Write-Host "Config port: $($cfg.config.comPort)"
if ($cfg.config.comPort -eq 'COM3') { Write-Host 'REGRESSION PASS: Config reverted to COM3' -ForegroundColor Green }
else { Write-Host "REGRESSION FAIL: expected COM3 got $($cfg.config.comPort)" -ForegroundColor Red }

Write-Host ''
Write-Host '=== ALL DONE ==='
