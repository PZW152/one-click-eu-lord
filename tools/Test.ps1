param([string]$Toolchain)

if ($Toolchain) { $env:RUSTUP_TOOLCHAIN = $Toolchain }

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    cargo fmt --all -- --check
    if ($LASTEXITCODE -ne 0) { throw 'cargo fmt check failed' }
    cargo clippy --workspace --all-targets --locked -- -D warnings
    if ($LASTEXITCODE -ne 0) { throw 'cargo clippy failed' }
    cargo test --workspace --locked
    if ($LASTEXITCODE -ne 0) { throw 'cargo test failed' }
    Write-Host 'All checks passed.' -ForegroundColor Green
}
finally {
    Pop-Location
}
