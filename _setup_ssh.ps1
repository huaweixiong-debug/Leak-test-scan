# Run this on the remote PC (100.95.136.69) to set up SSH key auth
# This will allow SSH from the dev machine (WIN-0HH52TJ5O4R)

$key = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIDn7oKvpYWdi2lj0bTmLhZkmINA2besauwkDrnuqi+2R administrator@WIN-0HH52TJ5O4R"

Write-Host "Setting up SSH key for dev machine..."

# For Administrator account on Windows, authorized_keys is in ProgramData
$authFile = "C:\ProgramData\ssh\administrators_authorized_keys"
$sshDir = "C:\ProgramData\ssh"

# Ensure ssh directory exists
if (-not (Test-Path $sshDir)) {
    New-Item -ItemType Directory -Path $sshDir -Force | Out-Null
}

# Check if key already exists
$keyExists = $false
if (Test-Path $authFile) {
    $existing = Get-Content $authFile
    if ($existing -match [regex]::Escape($key)) {
        $keyExists = $true
    }
}

if (-not $keyExists) {
    Add-Content -Path $authFile -Value $key
    Write-Host "  Key added to $authFile"
} else {
    Write-Host "  Key already in $authFile"
}

# Fix permissions (must be readable only by SYSTEM and Administrators)
icacls $authFile /inheritance:r /grant "SYSTEM:(R)" /grant "BUILTIN\Administrators:(R)" 2>&1 | Out-Null

# Also set up in user profile as fallback
$userAuthFile = "C:\Users\Administrator\.ssh\authorized_keys"
$userSshDir = "C:\Users\Administrator\.ssh"
if (-not (Test-Path $userSshDir)) {
    New-Item -ItemType Directory -Path $userSshDir -Force | Out-Null
}
if (-not (Test-Path $userAuthFile)) {
    # Just copy the ProgramData one
    Copy-Item $authFile $userAuthFile -Force
    icacls $userAuthFile /inheritance:r /grant "SYSTEM:(R)" /grant "BUILTIN\Administrators:(R)" /grant "WIN-0HH52TJ5O4R\Administrator:(R)" 2>&1 | Out-Null
    Write-Host "  Created $userAuthFile"
}

# Check sshd_config to make sure key auth is enabled
Write-Host ""
Write-Host "SSH config check:"
$sshdConfig = "C:\ProgramData\ssh\sshd_config"
if (Test-Path $sshdConfig) {
    $config = Get-Content $sshdConfig
    Write-Host "  PubkeyAuthentication: $(if ($config -match '^PubkeyAuthentication\s+yes') { 'yes' } else { 'NOT SET' })"
    Write-Host "  PasswordAuthentication: $(if ($config -match '^PasswordAuthentication\s+yes') { 'yes' } else { 'NOT SET' })"
}

Write-Host ""
Write-Host "Done. Test SSH from dev machine:"
Write-Host "  ssh Administrator@100.95.136.69 'echo OK'"
