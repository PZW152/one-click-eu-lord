<#
.SYNOPSIS
    Builds dist\EULord.exe with the C# compiler that ships inside Windows.

.DESCRIPTION
    No SDK, no NuGet, no MSBuild. .NET Framework 4.8 is in-box on Windows 11, so
    csc.exe under %WINDIR%\Microsoft.NET is always present and the result is a single
    exe of roughly 165 KB that needs no runtime installed.

    The UI is built in C# rather than XAML precisely so this works: compiling XAML
    would require MSBuild and turn a one-command build into a toolchain.

.PARAMETER Configuration
    Release (default, optimised) or Debug.

.PARAMETER OutputDir
    Where to put EULord.exe. Defaults to dist\ in the repository root.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $repoRoot 'src\EULord'
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'dist' }

. (Join-Path $PSScriptRoot 'BuildCommon.ps1')

$csc = Resolve-Csc
Write-Host "compiler   $csc"

$sources = @(Get-ChildItem -LiteralPath $sourceDir -Recurse -Filter *.cs | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "No C# sources found under $sourceDir" }
Write-Host "sources    $($sources.Count) files"

$referenceNames = @(
    'System', 'System.Core', 'System.Xml',
    'PresentationFramework', 'PresentationCore', 'WindowsBase', 'System.Xaml'
)
$references = foreach ($name in $referenceNames) { Resolve-Reference -Name $name }
foreach ($reference in $references) { Write-Host "reference  $reference" }

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$exePath = Join-Path $OutputDir 'EULord.exe'
$iconPath = Join-Path $OutputDir 'eulord.ico'
$manifestPath = Join-Path $sourceDir 'app.manifest'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Missing $manifestPath" }

New-AppIcon -Path $iconPath
Write-Host "icon       $iconPath"

$arguments = @(
    '/nologo'
    # Without /noconfig, csc also reads csc.rsp from its own directory, which
    # references System.dll and friends from there. On a machine with the .NET
    # developer pack installed -- the GitHub runner, for one -- our references
    # resolve to the Reference Assemblies copies instead, and csc rejects the two
    # as duplicate identities (CS1703). Suppressing the response file makes the
    # reference set exactly the list below, on every machine.
    '/noconfig'
    '/target:winexe'
    '/platform:anycpu'
    '/langversion:5'                       # the in-box compiler is C# 5; keep CI honest about it
    '/warn:4'
    '/warnaserror+'
    "/out:$exePath"
    "/win32manifest:$manifestPath"
    "/win32icon:$iconPath"
)
if ($Configuration -eq 'Release') { $arguments += @('/optimize+', '/debug-') }
else { $arguments += @('/optimize-', '/debug+', '/define:DEBUG') }

$arguments += foreach ($reference in $references) { "/reference:$reference" }
$arguments += $sources

& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "csc.exe failed with exit code $LASTEXITCODE" }

$info = Get-Item -LiteralPath $exePath
Write-Host ''
Write-Host ("built      {0}" -f $info.FullName)
Write-Host ("size       {0:N0} bytes ({1:N0} KB)" -f $info.Length, [math]::Round($info.Length / 1KB))
Write-Host ("config     {0}" -f $Configuration)
