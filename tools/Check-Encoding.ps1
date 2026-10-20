<#
.SYNOPSIS
    Guards the file encodings that have bitten this project before.

.DESCRIPTION
    Three rules, each learned the hard way in v1:

    .cs  must have a UTF-8 BOM. Without one, csc.exe decodes the file with the
         machine's ANSI code page, so on a default zh-TW install (cp950) every
         Chinese string in the catalog and the UI becomes mojibake, and a swallowed
         trail byte can break parsing outright. This machine runs with UTF-8 as the
         system code page, which is exactly why the bug would not show up here.

    .ps1 must use CRLF. A release zip once shipped LF scripts because
         .gitattributes only converts on a fresh checkout.

    .json fixtures must have no BOM. The shipped policy file has none, our rewrite
         must keep it that way, and a fixture with one would hide a regression.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$problems = New-Object System.Collections.Generic.List[string]

function Test-Bom {
    param([string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    return $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
}

function Get-Relative {
    param([string]$Path)
    return $Path.Substring($repoRoot.Length).TrimStart('\', '/')
}

# --- C# sources: UTF-8 with BOM ---------------------------------------------
$csFiles = @(Get-ChildItem -LiteralPath $repoRoot -Recurse -Filter *.cs -File |
             Where-Object { $_.FullName -notmatch '\\dist\\' })
if ($csFiles.Count -eq 0) { $problems.Add('no C# sources were found at all') }

foreach ($file in $csFiles) {
    if (-not (Test-Bom $file.FullName)) {
        $problems.Add("$(Get-Relative $file.FullName): needs a UTF-8 BOM so csc does not guess the code page")
    }
}

# --- PowerShell: CRLF --------------------------------------------------------
foreach ($file in @(Get-ChildItem -LiteralPath $repoRoot -Recurse -Filter *.ps1 -File |
                    Where-Object { $_.FullName -notmatch '\\dist\\' })) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $lf = ([regex]::Matches($text, "(?<!`r)`n")).Count
    if ($lf -gt 0) {
        $problems.Add("$(Get-Relative $file.FullName): has $lf bare LF line ending(s); Windows scripts use CRLF")
    }
}

# --- JSON fixtures: no BOM ---------------------------------------------------
$fixtures = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tests\fixtures') -Filter *.json -File -ErrorAction SilentlyContinue)
if ($fixtures.Count -eq 0) { $problems.Add('no JSON fixtures were found') }

foreach ($file in $fixtures) {
    if (Test-Bom $file.FullName) {
        $problems.Add("$(Get-Relative $file.FullName): must not have a BOM; the shipped policy file has none")
    }
}

# --- the catalog really does carry its Chinese ------------------------------
# A mojibake'd source file still compiles, so check a known string survives.
$catalog = [IO.File]::ReadAllText((Join-Path $repoRoot 'src\EULord\Core\Catalog.cs'), [Text.Encoding]::UTF8)
if ($catalog -notmatch '可以解除安裝 Edge') {
    $problems.Add('src\EULord\Core\Catalog.cs: the Traditional Chinese text did not survive; the file is probably mis-encoded')
}
if ($catalog -notmatch '可以卸载 Edge') {
    $problems.Add('src\EULord\Core\Catalog.cs: the Simplified Chinese text did not survive; the file is probably mis-encoded')
}

Write-Host ("checked    {0} C# file(s), {1} fixture(s)" -f $csFiles.Count, $fixtures.Count)

if ($problems.Count -gt 0) {
    Write-Host ''
    foreach ($problem in $problems) { Write-Host "  $problem" }
    throw "$($problems.Count) encoding problem(s)"
}

Write-Host 'ok'
