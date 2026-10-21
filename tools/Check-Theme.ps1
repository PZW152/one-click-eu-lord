<#
.SYNOPSIS
    Fails if src\EULord\Ui\Theme.cs is not what design\tokens.json generates.

.DESCRIPTION
    The design system and the product are meant to read the same numbers. That only
    holds if nobody hand-edits the generated file, so CI regenerates it to a
    temporary path and compares.

    If this fails, run tools\New-Theme.ps1 and commit the result -- and if the
    change was wanted, make it in design\tokens.json first.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$committed = Join-Path $repoRoot 'src\EULord\Ui\Theme.cs'
$temp = Join-Path ([IO.Path]::GetTempPath()) ("eulord-theme-" + [Guid]::NewGuid().ToString('N') + '.cs')

try {
    & (Join-Path $PSScriptRoot 'New-Theme.ps1') -OutFile $temp | Out-Null

    $a = [IO.File]::ReadAllBytes($committed)
    $b = [IO.File]::ReadAllBytes($temp)

    $same = $a.Length -eq $b.Length
    if ($same) {
        for ($i = 0; $i -lt $a.Length; $i++) {
            if ($a[$i] -ne $b[$i]) { $same = $false; break }
        }
    }

    if (-not $same) {
        Write-Host ''
        Write-Host '  src\EULord\Ui\Theme.cs does not match design\tokens.json.'
        Write-Host '  Run tools\New-Theme.ps1 and commit the result.'
        Write-Host ''
        # Show the first difference so the failure is actionable, not just a verdict.
        $left = [IO.File]::ReadAllLines($committed)
        $right = [IO.File]::ReadAllLines($temp)
        for ($i = 0; $i -lt [Math]::Max($left.Count, $right.Count); $i++) {
            $l = if ($i -lt $left.Count) { $left[$i] } else { '(end of file)' }
            $r = if ($i -lt $right.Count) { $right[$i] } else { '(end of file)' }
            if ($l -ne $r) {
                Write-Host ("  line {0}" -f ($i + 1))
                Write-Host ("    committed: {0}" -f $l.Trim())
                Write-Host ("    generated: {0}" -f $r.Trim())
                break
            }
        }
        throw 'Theme.cs is out of date'
    }

    Write-Host ("Theme.cs matches design\tokens.json ({0:N0} bytes)" -f $a.Length)
    Write-Host 'ok'
}
finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
}
