# PLC Integration Deployment & Verification Script
# Run this on remote machine (desktop-epprc71, user A) as Administrator
# Usage: powershell -ExecutionPolicy Bypass -File deploy-plc-verify.ps1

$ErrorActionPreference = "Continue"
$ProjectDir = "D:\ATEQ\src\ATEQ.LeakTest.Web"
$ServerUrl = "http://127.0.0.1:3000"
$ReportFile = "D:\ATEQ\plc-verify-report.txt"

function Write-Step($msg) {
    $ts = Get-Date -Format "HH:mm:ss"
    $line = "[$ts] $msg"
    Write-Host $line -ForegroundColor Cyan
    Add-Content -Path $ReportFile -Value $line
}

function Api-Get($path) {
    try {
        $r = Invoke-RestMethod -Uri "$ServerUrl$path" -Method Get -TimeoutSec 10
        return $r | ConvertTo-Json -Depth 6 -Compress
    } catch {
        return "ERROR: $_"
    }
}

function Api-Post($path, $body) {
    try {
        $r = Invoke-RestMethod -Uri "$ServerUrl$path" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 10
        return $r | ConvertTo-Json -Depth 6 -Compress
    } catch {
        return "ERROR: $_"
    }
}

# ==================== HEADER ====================
"=== PLC Integration Verification Report ===" | Out-File $ReportFile
"Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Add-Content $ReportFile
"Machine: $env:COMPUTERNAME" | Add-Content $ReportFile
"" | Add-Content $ReportFile

# ==================== STEP 1: Check current state ====================
Write-Step "STEP 1: Checking current server state"

$portOwner = netstat -ano | Select-String ":3000"
"Port 3000: $portOwner" | Add-Content $ReportFile

try {
    $health = Invoke-RestMethod -Uri "$ServerUrl/api/health" -TimeoutSec 5
    "Health: $($health | ConvertTo-Json -Compress)" | Add-Content $ReportFile
    Write-Step "Health check OK: $($health.build)"
} catch {
    Write-Step "Health check FAILED: $_"
    "Health: FAILED - $_" | Add-Content $ReportFile
}

# ==================== STEP 2: Stop server ====================
Write-Step "STEP 2: Stopping server"
$dotnetProcs = Get-Process -Name dotnet -ErrorAction SilentlyContinue
if ($dotnetProcs) {
    Write-Step "Killing dotnet processes: $($dotnetProcs.Id -join ', ')"
    $dotnetProcs | Stop-Process -Force
    Start-Sleep -Seconds 3
}

# Check if node is still running
$nodeProcs = Get-Process -Name node -ErrorAction SilentlyContinue
if ($nodeProcs) {
    Write-Step "WARNING: node.exe still running on PID $($nodeProcs.Id -join ',')"
    Write-Step "Killing node.exe..."
    $nodeProcs | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# ==================== STEP 3: Sync files from P: ====================
Write-Step "STEP 3: Syncing files from P:\ATEQ"

$filesToSync = @(
    "Infrastructure\PlcModbusTcpClient.cs",
    "Services\PlcService.cs",
    "Services\PlcCoordinatorService.cs",
    "Services\TestWorkflowService.cs",
    "Controllers\ConfigController.cs",
    "Controllers\PlcTestController.cs",
    "Data\AppDbContext.cs",
    "Data\DatabaseService.cs",
    "Models\PlcConfig.cs",
    "Models\PlcIoSnapshot.cs",
    "Models\Dto\PlcRequests.cs",
    "Program.cs",
    "wwwroot\plc-test.html",
    "wwwroot\comm-config.html",
    "wwwroot\index.html"
)

foreach ($f in $filesToSync) {
    $src = "P:\ATEQ\src\ATEQ.LeakTest.Web\$f"
    $dst = "$ProjectDir\$f"
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination $dst -Force
        Write-Host "  OK: $f" -ForegroundColor Green
    } else {
        Write-Host "  MISSING: $src" -ForegroundColor Red
        "MISSING: $f" | Add-Content $ReportFile
    }
}

# ==================== STEP 4: Backup DB + config ====================
Write-Step "STEP 4: Backing up DB and config"

$backupDir = "D:\ATEQ\backup\plc-deploy-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
Copy-Item -Path "$ProjectDir\appsettings.json" -Destination $backupDir -ErrorAction SilentlyContinue
Copy-Item -Path "$ProjectDir\data\ateq.db" -Destination $backupDir -ErrorAction SilentlyContinue
Copy-Item -Path "$ProjectDir\data\ateq.db-shm" -Destination $backupDir -ErrorAction SilentlyContinue
Copy-Item -Path "$ProjectDir\data\ateq.db-wal" -Destination $backupDir -ErrorAction SilentlyContinue
Write-Step "Backup saved to $backupDir"

# ==================== STEP 5: Build ====================
Write-Step "STEP 5: Building"

Set-Location $ProjectDir
Remove-Item -Recurse -Force obj, bin -ErrorAction SilentlyContinue
$buildResult = dotnet build 2>&1
$buildResult | Add-Content $ReportFile

if ($LASTEXITCODE -eq 0) {
    Write-Step "Build SUCCESS"
} else {
    Write-Step "Build FAILED"
    Write-Host $buildResult
    exit 1
}

# ==================== STEP 6: Start server ====================
Write-Step "STEP 6: Starting server"

$logFile = "D:\ATEQ\server-plc-verify.log"
$proc = Start-Process -FilePath "dotnet" -ArgumentList "run --urls http://127.0.0.1:3000" -WorkingDirectory $ProjectDir -NoNewWindow -RedirectStandardOutput $logFile -RedirectStandardError "$logFile.err" -PassThru
Write-Step "Server started with PID $($proc.Id), log: $logFile"

# Wait for server to be ready
$maxWait = 30
for ($i = 1; $i -le $maxWait; $i++) {
    Start-Sleep -Seconds 1
    try {
        $h = Invoke-RestMethod -Uri "$ServerUrl/api/health" -TimeoutSec 2
        if ($h.build) {
            Write-Step "Server ready after ${i}s"
            break
        }
    } catch { }
}
if ($i -gt $maxWait) {
    Write-Step "WARNING: Server did not respond within ${maxWait}s"
}

# ==================== STEP 7: Configure PLC ====================
Write-Step "STEP 7: Configuring PLC"

$plcConfig = @{
    enabled = $true
    host = "192.168.2.1"
    port = 502
    unitId = 1
    pollIntervalMs = 250
    startAddressM1 = 8193
    okAddressM2 = 8194
    ngAddressM3 = 8195
    resetAddressM4 = 8196
} | ConvertTo-Json

Write-Step "Saving PLC config: $plcConfig"
$saveResult = Invoke-RestMethod -Uri "$ServerUrl/api/config/plc" -Method Post -Body $plcConfig -ContentType "application/json" -TimeoutSec 15
"PLC config save: $($saveResult | ConvertTo-Json -Compress)" | Add-Content $ReportFile
Write-Step "coordinatorRunning = $($saveResult.coordinatorRunning)"

Start-Sleep -Seconds 2

# Read back
$plcStatus = Invoke-RestMethod -Uri "$ServerUrl/api/plc/status" -TimeoutSec 5
"PLC status: $($plcStatus | ConvertTo-Json -Compress)" | Add-Content $ReportFile
Write-Step "PLC status: coordinatorRunning=$($plcStatus.data.coordinatorRunning) online=$($plcStatus.data.online) enabled=$($plcStatus.data.enabled)"

# ==================== STEP 8: Basic PLC read/write ====================
Write-Step "STEP 8: Basic PLC I/O test"

try {
    $readMap = Invoke-RestMethod -Uri "$ServerUrl/api/plc/read-map" -TimeoutSec 5
    "ReadMap: $($readMap | ConvertTo-Json -Compress)" | Add-Content $ReportFile
    Write-Step "ReadMap coils: $($readMap.data.values -join ', ')"
} catch {
    Write-Step "ReadMap FAILED: $_"
    "ReadMap ERROR: $_" | Add-Content $ReportFile
}

# ==================== SUMMARY ====================
Write-Step "STEP 9: Initial verification complete"

$summary = @"
=== INITIAL VERIFICATION DONE ===
Full report: $ReportFile
Server log: $logFile

Next steps - run these manually:
1. Open http://127.0.0.1:3000/plc-test in browser
2. Verify M1-M4 read matches PLC actual state
3. Run Case A-F per HANDOVER_CSHARP.md Slice 12
"@

$summary | Add-Content $ReportFile
Write-Host $summary -ForegroundColor Yellow
