# ATEQ Leak Test - Force Restart Script
# Run this on the remote PC (100.95.136.69) as Administrator
# Path: D:\ATEQ Test\ATEQ-Leak-Test\_force_restart.ps1

$ErrorActionPreference = "Continue"
$projectDir = $PSScriptRoot

Write-Host "=== ATEQ Leak Test - Force Restart ==="
Write-Host "Project dir: $projectDir"
Write-Host ""

# Step 1: Kill ALL node.exe processes
Write-Host "[1/5] Killing all node.exe processes..."
$killed = $false
Get-Process -Name "node" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "  Killing node.exe PID $($_.Id)..."
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    $killed = $true
}
if (-not $killed) {
    Write-Host "  No node processes found."
}
Start-Sleep -Seconds 2

# Step 2: Force kill any process on port 3000
Write-Host "[2/5] Checking port 3000..."
$conns = Get-NetTCPConnection -LocalPort 3000 -ErrorAction SilentlyContinue
if ($conns) {
    foreach ($conn in $conns) {
        $owningId = $conn.OwningProcess
        Write-Host "  Port 3000 occupied by PID $owningId ($($conn.State)). Killing..."
        Stop-Process -Id $owningId -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 2
}

$left = Get-NetTCPConnection -LocalPort 3000 -State Listen -ErrorAction SilentlyContinue
if ($left) {
    Write-Host "  ERROR: Port 3000 still occupied. Run as Administrator."
    $left | Format-Table LocalAddress, LocalPort, State, OwningProcess -AutoSize
    exit 1
}
Write-Host "  Port 3000 is free."

# Step 3: Clear old logs
Write-Host "[3/5] Clearing old logs..."
Remove-Item "$projectDir\server.out*" -ErrorAction SilentlyContinue
Remove-Item "$projectDir\server.err*" -ErrorAction SilentlyContinue
Write-Host "  Logs cleared."

# Step 4: Start server
Write-Host "[4/5] Starting server..."
$nodeExe = "$projectDir\runtime18\node-v18.20.8-win-x64\node.exe"
if (-not (Test-Path $nodeExe)) {
    Write-Host "  ERROR: node.exe not found at $nodeExe"
    exit 1
}

$outFile = "$projectDir\server.out"
$errFile = "$projectDir\server.err"

$proc = Start-Process -FilePath $nodeExe -ArgumentList "server.js" -WorkingDirectory $projectDir -RedirectStandardOutput $outFile -RedirectStandardError $errFile -PassThru -WindowStyle Hidden
$procId = $proc.Id
Write-Host "  Started node.exe PID: $procId"

# Step 5: Health check
Write-Host "[5/5] Health check..."
$maxWait = 20
for ($i = 1; $i -le $maxWait; $i++) {
    Start-Sleep -Seconds 1
    try {
        $health = Invoke-RestMethod -Uri "http://127.0.0.1:3000/api/health" -TimeoutSec 3
        Write-Host ""
        Write-Host "=== HEALTH RESPONSE ==="
        $health | ConvertTo-Json -Depth 5
        Write-Host ""
        if ($health.build -eq "monitor-30min-samples-10000") {
            Write-Host "SUCCESS: New runtime is active!"
            Write-Host "Build marker: $($health.build)"
            exit 0
        } else {
            Write-Host "WARNING: Old runtime detected."
            Write-Host "Build: '$($health.build)'"
            Write-Host "Expected: 'monitor-30min-samples-10000'"
            exit 1
        }
    } catch {
        $dots = "." * $i
        Write-Host "  $dots"
    }
}

Write-Host ""
Write-Host "FAILED: Server did not respond within $($maxWait)s."
Write-Host ""
if (Test-Path $errFile) {
    Write-Host "=== server.err ==="
    Get-Content $errFile
}
if (Test-Path $outFile) {
    Write-Host "=== server.out ==="
    Get-Content $outFile
}
Write-Host ""
Write-Host "=== Port 3000 status ==="
Get-NetTCPConnection -LocalPort 3000 -ErrorAction SilentlyContinue | Format-Table -AutoSize
exit 1
