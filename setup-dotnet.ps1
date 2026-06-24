# PowerShell script to install .NET 8.0 SDK
# Run: powershell -ExecutionPolicy Bypass -File setup-dotnet.ps1

Write-Host "Checking .NET SDK..." -ForegroundColor Cyan

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    Write-Host "Found: $(& dotnet --version)" -ForegroundColor Green
    dotnet --list-sdks
    exit 0
}

Write-Host ".NET SDK not found. Downloading .NET 8.0 installer..." -ForegroundColor Yellow

$url = "https://download.visualstudio.microsoft.com/download/pr/5226a5a4-4a6b-4fc4-b25c-8f00e6f81ee4/5b822eac02d58a5d98daa5a3d39aa31c/dotnet-sdk-8.0.406-win-x64.exe"
$installer = "$env:TEMP\dotnet-sdk-8.0.exe"

Write-Host "Downloading..." -ForegroundColor Yellow
Invoke-WebRequest -Uri $url -OutFile $installer

Write-Host "Installing (this requires admin)... " -ForegroundColor Yellow
Start-Process -FilePath $installer -ArgumentList "/quiet /norestart" -Wait -Verb RunAs

Remove-Item $installer -Force -ErrorAction SilentlyContinue

# Refresh PATH
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

Write-Host "Done! .NET SDK installed." -ForegroundColor Green
dotnet --version
