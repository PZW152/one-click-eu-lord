[CmdletBinding()]
param([ValidateSet('dev', 'release')][string]$Profile = 'release', [string]$Toolchain)

if ($Toolchain) { $env:RUSTUP_TOOLCHAIN = $Toolchain }

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    cargo test --workspace --locked
    if ($LASTEXITCODE -ne 0) { throw 'cargo test failed' }
    cargo build --profile $Profile --bin eulord --locked
    if ($LASTEXITCODE -ne 0) { throw 'cargo build failed' }
    $built = if ($Profile -eq 'release') { 'target\release\eulord.exe' } else { 'target\debug\eulord.exe' }
    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
    Copy-Item $built (Join-Path $dist 'EULord.exe') -Force
    $hash = Get-FileHash (Join-Path $dist 'EULord.exe') -Algorithm SHA256
    Set-Content (Join-Path $dist 'EULord.exe.sha256') "$($hash.Hash.ToLower())  EULord.exe" -Encoding ascii
    Write-Host "Built $dist\EULord.exe ($Profile)" -ForegroundColor Green
}
finally {
    Pop-Location
}
