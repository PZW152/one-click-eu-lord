<#
.SYNOPSIS
    Checks that the version number agrees everywhere it appears.

.DESCRIPTION
    Four places have to say the same thing, or a release ships claiming to be a
    version it is not:

      Build.Version in src\EULord\Core\Engine.cs       what the exe reports and logs
      AssemblyVersion/FileVersion in AssemblyInfo.cs   what the file's Properties show
      assemblyIdentity in src\EULord\app.manifest      what the side-by-side loader sees
      the newest "## vX.Y.Z" in CHANGELOG.md           what the release notes come from

    With -Tag, the tag being released must match them too.

.PARAMETER Tag
    A release tag such as v2.0.0. Omit when running on an ordinary push.
#>
[CmdletBinding()]
param([string]$Tag)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$problems = New-Object System.Collections.Generic.List[string]

# --- the version the exe reports --------------------------------------------
$engine = [IO.File]::ReadAllText((Join-Path $repoRoot 'src\EULord\Core\Engine.cs'), [Text.Encoding]::UTF8)
$match = [regex]::Match($engine, 'public\s+const\s+string\s+Version\s*=\s*"(\d+\.\d+\.\d+)"')
if (-not $match.Success) { throw 'No Build.Version found in src\EULord\Core\Engine.cs' }
$version = $match.Groups[1].Value

# --- the version Windows sees ------------------------------------------------
$manifest = [IO.File]::ReadAllText((Join-Path $repoRoot 'src\EULord\app.manifest'), [Text.Encoding]::UTF8)
$manifestMatch = [regex]::Match($manifest, 'assemblyIdentity\s+version="(\d+\.\d+\.\d+)\.\d+"')
if (-not $manifestMatch.Success) { throw 'No assemblyIdentity version found in src\EULord\app.manifest' }
if ($manifestMatch.Groups[1].Value -ne $version) {
    $problems.Add("app.manifest says $($manifestMatch.Groups[1].Value) but Build.Version says $version")
}

# --- the version Windows shows in the file's Properties ----------------------
$assemblyInfoPath = Join-Path $repoRoot 'src\EULord\AssemblyInfo.cs'
if (-not (Test-Path -LiteralPath $assemblyInfoPath)) { throw "Missing $assemblyInfoPath" }
$assemblyInfo = [IO.File]::ReadAllText($assemblyInfoPath, [Text.Encoding]::UTF8)

foreach ($attribute in 'AssemblyVersion', 'AssemblyFileVersion') {
    $m = [regex]::Match($assemblyInfo, [regex]::Escape($attribute) + '\("(\d+\.\d+\.\d+)\.\d+"\)')
    if (-not $m.Success) { throw "No $attribute found in src\EULord\AssemblyInfo.cs" }
    if ($m.Groups[1].Value -ne $version) {
        $problems.Add("$attribute says $($m.Groups[1].Value) but Build.Version says $version")
    }
}

$informational = [regex]::Match($assemblyInfo, 'AssemblyInformationalVersion\("([^"]+)"\)')
if ($informational.Success -and $informational.Groups[1].Value -ne $version) {
    $problems.Add("AssemblyInformationalVersion says $($informational.Groups[1].Value) but Build.Version says $version")
}

# --- the newest entry in the changelog ---------------------------------------
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
$changelog = [IO.File]::ReadAllText($changelogPath, [Text.Encoding]::UTF8)
$headings = @([regex]::Matches($changelog, '(?m)^##\s+v(\d+\.\d+\.\d+)') | ForEach-Object { $_.Groups[1].Value })
if ($headings.Count -eq 0) { throw 'CHANGELOG.md has no "## vX.Y.Z" heading' }
if ($headings[0] -ne $version) {
    $problems.Add("the newest CHANGELOG heading is v$($headings[0]) but Build.Version says $version")
}

# --- the section has something in it -----------------------------------------
$section = [regex]::Match($changelog, "(?ms)^##\s+v$([regex]::Escape($version))\b.*?\n(.*?)(?=^##\s|\z)")
if (-not $section.Success -or [string]::IsNullOrWhiteSpace($section.Groups[1].Value)) {
    $problems.Add("CHANGELOG.md has no release notes under v$version")
}

# --- the tag, when releasing --------------------------------------------------
if ($Tag) {
    if ($Tag -notmatch '^v\d+\.\d+\.\d+$') { $problems.Add("the tag '$Tag' does not look like v1.2.3") }
    elseif ($Tag -ne "v$version") { $problems.Add("the tag is $Tag but the build says v$version") }
}

Write-Host ("Build.Version      {0}" -f $version)
Write-Host ("AssemblyInfo       {0}" -f
    [regex]::Match($assemblyInfo, 'AssemblyFileVersion\("([^"]+)"\)').Groups[1].Value)
Write-Host ("app.manifest       {0}" -f $manifestMatch.Groups[1].Value)
Write-Host ("CHANGELOG newest   v{0}" -f $headings[0])
if ($Tag) { Write-Host ("tag                {0}" -f $Tag) }

if ($problems.Count -gt 0) {
    Write-Host ''
    foreach ($problem in $problems) { Write-Host "  $problem" }
    throw "$($problems.Count) version mismatch(es)"
}

Write-Host 'ok'
