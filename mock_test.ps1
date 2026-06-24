$ErrorActionPreference = 'Stop'
$base = 'http://127.0.0.1:3000'

function Invoke-Post($ep, $body) {
    $r = Invoke-RestMethod -Uri "$base$ep" -Method Post -Body $body -ContentType 'application/json'
    return $r | ConvertTo-Json -Depth 6 -Compress
}
function Invoke-Get($ep) {
    $r = Invoke-RestMethod -Uri "$base$ep" -Method Get
    return $r | ConvertTo-Json -Depth 6 -Compress
}

# Health
$h = Invoke-Get '/api/health' | ConvertFrom-Json
Write-Host "Health: $($h.success)"

# Configure mock
$ateqBody = '{"comPort":"MOCK_ATEQ","baudrate":9600,"dataBits":8,"parity":"none","stopBits":1,"slaveId":1,"timeoutMs":5000,"pollIntervalMs":500,"dtr":true,"rts":true,"enabled":true}'
$scanBody = '{"comPort":"MOCK_SCANNER","baudrate":9600,"dataBits":8,"parity":"none","stopBits":1,"timeoutMs":5000,"pollIntervalMs":100,"dtr":true,"rts":true,"enabled":true}'

Write-Host '--- ATEQ mock config ---'
$r = Invoke-Post '/api/config/ateq' $ateqBody
Write-Host $r

Write-Host '--- Scanner mock config ---'
$r = Invoke-Post '/api/config/scanner' $scanBody
Write-Host $r

# Status
Write-Host '--- Status ---'
$r = Invoke-Get '/api/status' | ConvertFrom-Json
Write-Host "step=$($r.currentStep) err=$($r.errorCode)"

# Settings: two products - one for manual, one for scan auto-start
$prodBody = '{"products":[' +
    '{"productModel":"Test-Product","ateqProgramNo":1,"qrKeyword":"TEST","isActive":true,"scanConfirmEnabled":false,"scanAutoStartEnabled":false,"scanMatchEnabled":false},' +
    '{"productModel":"Scan-Product","ateqProgramNo":2,"qrKeyword":"KW","isActive":true,"scanConfirmEnabled":true,"scanAutoStartEnabled":true,"scanMatchEnabled":true}' +
    ']}'
$opBody = '{"operators":[{"name":"Test-Op","isActive":true}]}'
Invoke-Post '/api/settings/products' $prodBody | Out-Null
Invoke-Post '/api/settings/operators' $opBody | Out-Null
Write-Host 'Settings: OK (2 products)'

# Context + Start
Invoke-Post '/api/test/context' '{"productModel":"Test-Product","operatorName":"Test-Op"}' | Out-Null
Start-Sleep -Milliseconds 300
$r = Invoke-Post '/api/start' '{"productModel":"Test-Product","operatorName":"Test-Op","startMode":"manual"}'
Write-Host "Start: $r"

# Poll
Write-Host '--- Polling test/active ---'
for ($i=0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 400
    $r = Invoke-Get '/api/test/active' | ConvertFrom-Json
    $at = $r.activeTest
    Write-Host "  +$([math]::Round($i*0.4,1))s  stage=$($at.stage)  result=$($at.resultCode)"
    if ($at.stage -eq 'completed' -or $at.stage -eq 'failed') { break }
}

# Records
$r = Invoke-Get '/api/tests/latest' | ConvertFrom-Json
$recResult = 'none'
if ($r.records.Count -gt 0) { $recResult = $r.records[0].resultCode }
Write-Host "Records: total=$($r.total) first=$recResult"

# Mock scan - use Scan-Product which has scanAutoStartEnabled + scanMatchEnabled with keyword "KW"
Write-Host '--- Scan mock flow ---'
Invoke-Post '/api/reset' '{}' | Out-Null
Start-Sleep 1
$r = Invoke-Post '/api/scanner/debug/mock-scan' '{"rawText":"PART-KW-001"}'
Write-Host "Scan inject: $r"
Start-Sleep 6
$r = Invoke-Get '/api/tests/latest' | ConvertFrom-Json
Write-Host "Records after scan: $($r.total)"
if ($r.records.Count -gt 0) { Write-Host "  last result=$($r.records[0].resultCode) product=$($r.records[0].productModel)" }

# NG test
Write-Host '--- NG result test ---'
Invoke-Post '/api/debug/mock/ateq/next-result' '{"resultCode":"NG","errorCode":"ATEQ_ALARM"}' | Out-Null
Invoke-Post '/api/start' '{"productModel":"Test-Product","operatorName":"Test-Op","startMode":"manual"}' | Out-Null
Start-Sleep 5
$r = Invoke-Get '/api/tests/latest' | ConvertFrom-Json
if ($r.records.Count -gt 0) {
    Write-Host "Last record: result=$($r.records[0].resultCode) err=$($r.records[0].errorCode)"
}
Write-Host "Final total records: $($r.total)"

Write-Host '=== SLICE 3 MOCK FLOW DONE ==='
