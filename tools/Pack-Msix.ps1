[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [string]$PfxPath,
    [string]$PfxPassword,
    [switch]$SkipSign,
    [string]$Toolchain
)

$ErrorActionPreference = 'Stop'
if ($Toolchain) { $env:RUSTUP_TOOLCHAIN = $Toolchain }
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    cargo build --release --bin eulord
    if ($LASTEXITCODE -ne 0) { throw 'cargo build failed' }

    $stage = Join-Path $root 'dist\msix'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Path (Join-Path $stage 'Assets') -Force | Out-Null
    Copy-Item 'target\release\eulord.exe' (Join-Path $stage 'eulord.exe') -Force
    Copy-Item 'packaging\assets\*' (Join-Path $stage 'Assets') -Force
    $manifest = Get-Content 'packaging\AppxManifest.xml' -Raw
    $manifest = $manifest.Replace('__ARCH__', $Architecture)
    Set-Content (Join-Path $stage 'AppxManifest.xml') $manifest -Encoding utf8NoBOM

    $makeappx = Get-Command MakeAppx.exe -ErrorAction SilentlyContinue
    if (-not $makeappx) {
        $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
        $makeappx = Get-ChildItem $sdkRoot -Recurse -Filter MakeAppx.exe -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    }
    if (-not $makeappx) { throw 'MakeAppx.exe not found; install the Windows SDK' }
    $makeappxPath = if ($makeappx -is [System.Management.Automation.CommandInfo]) { $makeappx.Source } else { $makeappx.FullName }

    $msix = Join-Path $root 'dist\OneClickEULord.msix'
    & $makeappxPath pack /d $stage /p $msix /o
    if ($LASTEXITCODE -ne 0) { throw 'MakeAppx failed' }

    if (-not $SkipSign -and $PfxPath) {
        $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if (-not $signtool) {
            $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
            $signtool = Get-ChildItem $sdkRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
        }
        if (-not $signtool) { throw 'signtool.exe not found; install the Windows SDK' }
        $signtoolPath = if ($signtool -is [System.Management.Automation.CommandInfo]) { $signtool.Source } else { $signtool.FullName }
        & $signtoolPath sign /fd SHA256 /a /f $PfxPath /p $PfxPassword $msix
        if ($LASTEXITCODE -ne 0) { throw 'signtool failed' }
    }

    $appinstaller = Get-Content 'packaging\OneClickEULord.appinstaller' -Raw
    $appinstaller = $appinstaller.Replace('ProcessorArchitecture="x64"', "ProcessorArchitecture=`"$Architecture`"")
    Set-Content 'dist\OneClickEULord.appinstaller' $appinstaller -Encoding utf8NoBOM
    Write-Host "MSIX ready: $msix" -ForegroundColor Green
    if ($SkipSign -or -not $PfxPath) { Write-Warning 'Package is unsigned. Provide -PfxPath (and -PfxPassword) before distributing.' }
}
finally {
    Pop-Location
}
