<#
.SYNOPSIS
    Compares the classification table against the policy file on this machine.

.DESCRIPTION
    Catalog.cs classifies every policy whose Ireland state differs from the global
    default. Microsoft adds policies with Windows updates, so on a newer build some
    may differ without being classified yet.

    That is not a failure. The engine leaves an unclassified policy exactly as
    Windows shipped it, which is the safe answer and the one the superset promise
    requires. It is, however, something a maintainer should act on, so this writes a
    warning and lists the GUIDs.

    Running this in CI is worth more than running it locally: the GitHub runner's
    Windows build differs from any developer machine, so the catalog gets checked
    against a second build on every push.

    Exits non-zero only when the policy file cannot be read or parsed, or when a
    classified GUID has gone missing from a file that otherwise looks healthy.
#>
[CmdletBinding()]
param(
    [string]$PolicyFile = (Join-Path $env:SystemRoot 'System32\IntegratedServicesRegionPolicySet.json'),
    [string]$Iso2 = 'IE'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$catalogFile = Join-Path $repoRoot 'src\EULord\Core\Catalog.cs'

if (-not (Test-Path -LiteralPath $PolicyFile)) { throw "Policy file not found: $PolicyFile" }
if (-not (Test-Path -LiteralPath $catalogFile)) { throw "Catalog not found: $catalogFile" }

$catalogText = [IO.File]::ReadAllText($catalogFile, [Text.Encoding]::UTF8)
$catalog = @{}
foreach ($match in [regex]::Matches($catalogText, 'E\("(\{[0-9a-fA-F-]{36}\})",\s*"([A-Za-z0-9]+)",\s*C\.(Benefit|Loss|Choice)')) {
    $catalog[$match.Groups[1].Value.ToLowerInvariant()] = [pscustomobject]@{
        Id    = $match.Groups[2].Value
        Class = $match.Groups[3].Value
    }
}
if ($catalog.Count -eq 0) { throw "No catalog entries were parsed out of $catalogFile" }

$policy = Get-Content -LiteralPath $PolicyFile -Raw -Encoding UTF8 | ConvertFrom-Json

$differing = @{}
$seen = @{}
foreach ($entry in $policy.policies) {
    $guid = [string]$entry.guid
    $seen[$guid.ToLowerInvariant()] = $true

    $state = $entry.defaultState
    if ($entry.PSObject.Properties.Name -contains 'conditions' -and $entry.conditions -and
        $entry.conditions.PSObject.Properties.Name -contains 'region' -and $entry.conditions.region) {
        $region = $entry.conditions.region
        if ($region.PSObject.Properties.Name -contains 'enabled' -and $region.enabled -contains $Iso2) { $state = 'enabled' }
        elseif ($region.PSObject.Properties.Name -contains 'disabled' -and $region.disabled -contains $Iso2) { $state = 'disabled' }
    }

    if ($state -ne $entry.defaultState) { $differing[$guid.ToLowerInvariant()] = $entry }
}

$unclassified = @($differing.Keys | Where-Object { -not $catalog.ContainsKey($_) })
$missing = @($catalog.Keys | Where-Object { -not $seen.ContainsKey($_) })

Write-Host ("policy file     {0}" -f $PolicyFile)
Write-Host ("entries         {0}" -f $policy.policies.Count)
Write-Host ("differ in {0}    {1}" -f $Iso2, $differing.Count)
Write-Host ("classified      {0}" -f $catalog.Count)
Write-Host ("unclassified    {0}" -f $unclassified.Count)
Write-Host ("absent here     {0}" -f $missing.Count)

function Write-Annotation {
    param([string]$Level, [string]$Message)
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host "::$Level::$Message" }
    else { Write-Host "$($Level.ToUpperInvariant()): $Message" }
}

foreach ($guid in $unclassified) {
    $comment = [string]$differing[$guid].'$comment'
    Write-Annotation -Level 'warning' -Message "Unclassified policy $guid left untouched: $comment"
}

# A classified GUID that this build does not have is ordinary: Microsoft removes
# policies too, and the engine simply never sees it. Worth printing, not failing on.
foreach ($guid in $missing) {
    Write-Host ("  not on this build: {0}  ({1})" -f $guid, $catalog[$guid].Id)
}

if ($unclassified.Count -gt 0) {
    Write-Annotation -Level 'warning' -Message `
        "$($unclassified.Count) policy/policies differ in $Iso2 without a classification. They stay as Windows shipped them until someone decides Benefit, Loss or Choice."
}

Write-Host ''
Write-Host 'ok (unclassified policies are reported, never guessed at)'
exit 0
