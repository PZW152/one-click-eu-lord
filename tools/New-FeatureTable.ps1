<#
.SYNOPSIS
    Prints the policy table used in docs/*/FEATURES.md from a policy file.

.DESCRIPTION
    Reads IntegratedServicesRegionPolicySet.json (read-only) and prints one
    Markdown table row per policy: its comment, the global default state, the
    state inside the EEA, and a marker where the two differ. Run it on a new
    Windows build and paste the rows into the three FEATURES.md files.

.EXAMPLE
    powershell -NoProfile -File tools\New-FeatureTable.ps1 > rows.md
#>
param(
    [string]$Path = (Join-Path $env:windir 'System32\IntegratedServicesRegionPolicySet.json'),
    [string]$EeaCode = 'IE'
)
$ErrorActionPreference = 'Stop'

function Get-EeaState {
    param($Policy, [string]$Code)
    if ($Policy.PSObject.Properties.Name -contains 'conditions' -and $Policy.conditions -and
        $Policy.conditions.PSObject.Properties.Name -contains 'region') {
        $r = $Policy.conditions.region
        if ($r.PSObject.Properties.Name -contains 'enabled'  -and $r.enabled  -contains $Code) { return 'enabled' }
        if ($r.PSObject.Properties.Name -contains 'disabled' -and $r.disabled -contains $Code) { return 'disabled' }
    }
    return $Policy.defaultState
}

$json = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8) | ConvertFrom-Json
foreach ($p in $json.policies) {
    $eea  = Get-EeaState -Policy $p -Code $EeaCode
    $mark = if ($eea -ne $p.defaultState) { '✦' } else { '' }
    '| {0} | `{1}` | `{2}` | {3} |' -f $p.'$comment', $p.defaultState, $eea, $mark
}
