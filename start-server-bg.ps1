# PowerShell: Launch ATEQ server in background
# Usage: powershell -ExecutionPolicy Bypass -File start-server-bg.ps1
#
# Uses schtasks to create a one-time scheduled task that survives SSH disconnect.
# Falls back to Start-Process if schtasks is unavailable.

param(
    [switch]$NoSchtask  # Force Start-Process even when schtasks is available
)

function Stop-AteqProcesses {
    $targets = Get-CimInstance Win32_Process | Where-Object {
        $_.Name -ieq "ATEQ.LeakTest.Web.exe" -or
        ($_.Name -ieq "dotnet.exe" -and $_.CommandLine -like "*ATEQ.LeakTest.Web*")
    }

    foreach ($target in $targets) {
        Stop-Process -Id $target.ProcessId -Force -ErrorAction SilentlyContinue
        Write-Host "  Stopped PID $($target.ProcessId)"
    }
}

function Wait-ForHealth {
    param(
        [string]$Url,
        [int]$Attempts = 20
    )

    for ($attempt = 0; $attempt -lt $Attempts; $attempt++) {
        Start-Sleep -Seconds 1

        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                return $true
            }
        }
        catch {
            Start-Sleep -Milliseconds 200
        }
    }

    return $false
}

function Start-AteqWithHiddenProcess {
    param(
        [string]$DotnetPath,
        [string]$Dll,
        [string]$WorkingDir,
        [string]$StdOutLog,
        [string]$StdErrLog,
        [string]$Urls
    )

    $previousAspNetCoreUrls = [Environment]::GetEnvironmentVariable("ASPNETCORE_URLS", "Process")
    [Environment]::SetEnvironmentVariable("ASPNETCORE_URLS", $Urls, "Process")
    try {
        $process = Start-Process -FilePath $DotnetPath `
            -ArgumentList $Dll `
            -WorkingDirectory $WorkingDir `
            -WindowStyle Hidden `
            -PassThru `
            -RedirectStandardOutput $StdOutLog `
            -RedirectStandardError $StdErrLog
    }
    finally {
        [Environment]::SetEnvironmentVariable("ASPNETCORE_URLS", $previousAspNetCoreUrls, "Process")
    }

    Write-Host "  Started via Start-Process (PID: $($process.Id))"
    return $process
}

$Dotnet = $null
if (Test-Path "C:\Users\A\AppData\Local\Microsoft\dotnet\dotnet.exe") {
    $Dotnet = "C:\Users\A\AppData\Local\Microsoft\dotnet\dotnet.exe"
}
elseif (Test-Path "C:\Program Files\dotnet\dotnet.exe") {
    $Dotnet = "C:\Program Files\dotnet\dotnet.exe"
}
else {
    $DotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($DotnetCommand) {
        $Dotnet = $DotnetCommand.Source
    }
}

if (-not $Dotnet) {
    throw "dotnet runtime not found"
}
$ScriptDir = Split-Path -Parent $PSCommandPath
$ProjectDir = $ScriptDir
$AppDir = Join-Path $ProjectDir "src\ATEQ.LeakTest.Web"
$DllPath = Join-Path $AppDir "bin\Release\net8.0\ATEQ.LeakTest.Web.dll"
$LogFile = Join-Path $ProjectDir "server.log"
$ErrorLogFile = Join-Path $ProjectDir "server_error.log"
$HealthUrl = "http://127.0.0.1:3000/api/health"
$TaskName = "ATEQ-LeakTest-Server"
$AspNetCoreUrls = "http://0.0.0.0:3000"

if (-not (Test-Path $DllPath)) {
    throw "Built server DLL not found: $DllPath. Run .\build.cmd first."
}

# Use schtasks as the primary method — it survives SSH session termination.
# Start-Process does NOT survive SSH disconnect on some Windows configurations.
$UseSchtask = -not $NoSchtask

if ($UseSchtask) {
    # Check if schtasks is functional
    $SchtaskCheck = schtasks /? 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "schtasks not available, falling back to Start-Process" -ForegroundColor Yellow
        $UseSchtask = $false
    }
}

# ---------- Port conflict detection ----------
$PortOwner = Get-NetTCPConnection -LocalPort 3000 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($PortOwner) {
    $OwnerPid = $PortOwner.OwningProcess
    $OwnerProc = Get-Process -Id $OwnerPid -ErrorAction SilentlyContinue
    $OwnerName = if ($OwnerProc) { $OwnerProc.Name } else { "Unknown" }

    if ($OwnerName -ne "dotnet" -and $OwnerName -ne "ATEQ.LeakTest.Web") {
        Write-Host "ERROR: Port 3000 is occupied by $OwnerName (PID $OwnerPid)" -ForegroundColor Red
        Write-Host "This is NOT the C# ATEQ server. It may be a legacy Node.js process." -ForegroundColor Red
        Write-Host "Stop it first: taskkill /F /PID $OwnerPid" -ForegroundColor Yellow
        Write-Host "Then re-run this script." -ForegroundColor Yellow
        exit 1
    }
    # It is dotnet.exe — verify it is OUR ATEQ service, not some unrelated dotnet app
    try {
        $HealthCheck = Invoke-WebRequest -UseBasicParsing -Uri $HealthUrl -TimeoutSec 2
        $Body = $HealthCheck.Content | ConvertFrom-Json
        if ($Body.build -ne "dotnet-1.0.0") {
            Write-Host "ERROR: Port 3000 is owned by dotnet.exe (PID $OwnerPid) but it is NOT the ATEQ C# service." -ForegroundColor Red
            Write-Host "health response: $($HealthCheck.Content)" -ForegroundColor Yellow
            Write-Host "Stop it first: taskkill /F /PID $OwnerPid" -ForegroundColor Yellow
            exit 1
        }
        Write-Host "Port 3000 is owned by the ATEQ C# service (PID $OwnerPid) — will stop it." -ForegroundColor Cyan
    } catch {
        Write-Host "ERROR: Port 3000 is owned by dotnet.exe (PID $OwnerPid) but /api/health is not responding." -ForegroundColor Red
        Write-Host "It is not the ATEQ C# service. Stop it first: taskkill /F /PID $OwnerPid" -ForegroundColor Yellow
        exit 1
    }
}

# ---------- Kill any existing instance ----------
Write-Host "Stopping any existing ATEQ server..." -ForegroundColor Cyan

# Stop running scheduled task
if ($UseSchtask) {
    schtasks /End /TN $TaskName 2>&1 | Out-Null
    schtasks /Delete /TN $TaskName /F 2>&1 | Out-Null
}

Stop-AteqProcesses

Start-Sleep -Seconds 1

# ---------- Start server ----------
Write-Host "Starting ATEQ Leak Test server..." -ForegroundColor Cyan
$LaunchMethod = $null

if ($UseSchtask) {
    # Create a one-time scheduled task that runs immediately
    $TaskCommand = "cmd /c cd /d `"$ProjectDir`" && set ASPNETCORE_URLS=$AspNetCoreUrls && `"$Dotnet`" `"$DllPath`" > `"$LogFile`" 2>&1"

    schtasks /Create `
        /TN $TaskName `
        /SC ONCE `
        /ST 00:00 `
        /TR $TaskCommand `
        /IT `
        /F 2>&1 | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Failed to create scheduled task. Falling back to Start-Process." -ForegroundColor Red
        $UseSchtask = $false
    }
    else {
        Write-Host "  Scheduled task created: $TaskName"

        schtasks /Run /TN $TaskName 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Failed to start scheduled task." -ForegroundColor Red
            $UseSchtask = $false
        }
        else {
            Write-Host "  Task started via schtasks"
            $LaunchMethod = "schtasks"
        }
    }
}

if (-not $UseSchtask) {
    # Fallback: Start-Process (works for interactive/RDP sessions and keeps COM devices in the user session)
    Start-AteqWithHiddenProcess `
        -DotnetPath $Dotnet `
        -Dll $DllPath `
        -WorkingDir $ProjectDir `
        -StdOutLog $LogFile `
        -StdErrLog $ErrorLogFile `
        -Urls $AspNetCoreUrls | Out-Null
    $LaunchMethod = "Start-Process"
}

# ---------- Health check ----------
Write-Host "Waiting for server to be ready..." -ForegroundColor Cyan

$Healthy = Wait-ForHealth -Url $HealthUrl

if (-not $Healthy -and $LaunchMethod -eq "schtasks") {
    Write-Host "Scheduled task launch did not pass health check. Retrying in the current user session..." -ForegroundColor Yellow

    schtasks /End /TN $TaskName 2>&1 | Out-Null
    schtasks /Delete /TN $TaskName /F 2>&1 | Out-Null
    Stop-AteqProcesses
    Start-Sleep -Seconds 1

    Start-AteqWithHiddenProcess `
        -DotnetPath $Dotnet `
        -Dll $DllPath `
        -WorkingDir $ProjectDir `
        -StdOutLog $LogFile `
        -StdErrLog $ErrorLogFile `
        -Urls $AspNetCoreUrls | Out-Null

    $LaunchMethod = "Start-Process"
    Write-Host "Waiting for server after Start-Process retry..." -ForegroundColor Cyan
    $Healthy = Wait-ForHealth -Url $HealthUrl
}

# ---------- Result ----------
if (-not $Healthy) {
    Write-Host "ERROR: Server failed to pass health check at $HealthUrl" -ForegroundColor Red
    if (Test-Path $LogFile) {
        Write-Host "--- server.log (last 30 lines) ---" -ForegroundColor Yellow
        Get-Content $LogFile -Tail 30
    }
    if (Test-Path $ErrorLogFile) {
        Write-Host "--- server_error.log (last 30 lines) ---" -ForegroundColor Yellow
        Get-Content $ErrorLogFile -Tail 30
    }
    exit 1
}

Write-Host "Server started successfully" -ForegroundColor Green
Write-Host "  Health check passed: $HealthUrl"
Write-Host "  Launch method: $LaunchMethod"
Write-Host "  Log: $LogFile"
Write-Host ""
Write-Host "To stop: run stop-server.cmd or: schtasks /End /TN $TaskName"
exit 0
