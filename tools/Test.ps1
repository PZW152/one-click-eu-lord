<#
.SYNOPSIS
    Compiles and runs the test suite.

.DESCRIPTION
    The tests are compiled into the same assembly as the product so they can reach
    internal types, with csc /main choosing their entry point instead of the real one.

    Nothing here needs administrator rights and nothing touches the machine: before
    the first test runs, the data folder, the policy file and the ProgramData base
    are all redirected into a temporary sandbox, and the suite refuses to start if
    that redirection did not take.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $repoRoot 'src\EULord'
$testDir = Join-Path $repoRoot 'tests'
$outputDir = Join-Path $repoRoot 'dist'

. (Join-Path $PSScriptRoot 'BuildCommon.ps1')

$csc = Resolve-Csc
$sources = @(Get-ChildItem -LiteralPath $sourceDir -Recurse -Filter *.cs | ForEach-Object { $_.FullName })
$sources += @(Get-ChildItem -LiteralPath $testDir -Filter *.cs | ForEach-Object { $_.FullName })

$referenceNames = @(
    'System', 'System.Core', 'System.Xml',
    'PresentationFramework', 'PresentationCore', 'WindowsBase', 'System.Xaml'
)
$references = foreach ($name in $referenceNames) { Resolve-Reference -Name $name }

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$testExe = Join-Path $outputDir 'EULord.Tests.exe'

Write-Host "compiling  $($sources.Count) files -> $testExe"
$arguments = @(
    '/nologo'
    # Without /noconfig, csc also reads csc.rsp from its own directory, which
    # references System.dll and friends from there. On a machine with the .NET
    # developer pack installed -- the GitHub runner, for one -- our references
    # resolve to the Reference Assemblies copies instead, and csc rejects the two
    # as duplicate identities (CS1703). Suppressing the response file makes the
    # reference set exactly the list below, on every machine.
    '/noconfig'
    '/target:exe'
    '/platform:anycpu'
    '/langversion:5'
    '/warn:4'
    '/warnaserror+'
    '/optimize-'
    '/debug+'
    '/main:EULord.Tests.TestRunner'
    "/out:$testExe"
)
$arguments += foreach ($reference in $references) { "/reference:$reference" }
$arguments += $sources

& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "csc.exe failed with exit code $LASTEXITCODE" }

Write-Host ''
& $testExe $repoRoot
$testExit = $LASTEXITCODE

Write-Host ''
if ($testExit -eq 0) { Write-Host 'all tests passed' }
else { Write-Host "tests failed (exit $testExit)" }

exit $testExit
