<#
.SYNOPSIS
    Normalizes source encodings and line endings to what Check-Encoding.ps1 requires.

.DESCRIPTION
    Rewrites .cs files as UTF-8 with a BOM and .ps1 files with CRLF, leaving the
    text itself alone. Run it after adding or editing sources; CI runs the checker,
    not this.

.PARAMETER WhatIf
    Report what would change without writing anything.
#>
[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$changed = 0

function Convert-File {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][bool]$WantBom
    )

    $bytes = [IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF

    # Decoding as UTF-8 either way: a file without a BOM that contains only ASCII is
    # unchanged, and one that already holds UTF-8 bytes round trips exactly.
    $text = [Text.Encoding]::UTF8.GetString($bytes)
    if ($hasBom) { $text = $text.TrimStart([char]0xFEFF) }

    $normalized = ($text -replace "`r`n", "`n") -replace "`n", "`r`n"

    $encoding = New-Object Text.UTF8Encoding($WantBom)
    $wanted = $encoding.GetPreamble() + $encoding.GetBytes($normalized)

    if ($wanted.Length -eq $bytes.Length) {
        $same = $true
        for ($i = 0; $i -lt $wanted.Length; $i++) { if ($wanted[$i] -ne $bytes[$i]) { $same = $false; break } }
        if ($same) { return $false }
    }

    $relative = $Path.Substring($repoRoot.Length).TrimStart('\', '/')
    if ($PSCmdlet.ShouldProcess($relative, 'normalize')) {
        [IO.File]::WriteAllBytes($Path, $wanted)
        Write-Host "  normalized  $relative"
    }
    else {
        Write-Host "  would fix   $relative"
    }
    return $true
}

foreach ($file in @(Get-ChildItem -LiteralPath $repoRoot -Recurse -Filter *.cs -File |
                    Where-Object { $_.FullName -notmatch '\\dist\\' })) {
    if (Convert-File -Path $file.FullName -WantBom $true) { $changed++ }
}

foreach ($pattern in '*.ps1', '*.manifest') {
    foreach ($file in @(Get-ChildItem -LiteralPath $repoRoot -Recurse -Filter $pattern -File |
                        Where-Object { $_.FullName -notmatch '\\dist\\' })) {
        if (Convert-File -Path $file.FullName -WantBom $false) { $changed++ }
    }
}

Write-Host ''
Write-Host ("{0} file(s) needed normalizing" -f $changed)
