<#
.SYNOPSIS
    一鍵轉生歐盟爺 (One-Click EU Grandpa) — core engine.

.DESCRIPTION
    Unlocks the Windows 11 EEA/DMA ("European Economic Area" / Digital Markets Act)
    feature set on any machine and keeps it unlocked across updates and reboots.

    It does this with two independent, layered techniques:

      * Gentle  : sets the cached device region (HKLM\...\DeviceRegion) to an EEA
                  country so Windows itself computes the EEA policy states. Fully
                  reversible, touches only one registry value.

      * Hardcore: Gentle, PLUS rewrites C:\Windows\System32\IntegratedServicesRegionPolicySet.json
                  so every policy evaluates to its EEA/DMA state for ALL regions.
                  Survives even if the device region is later reset. Touches a
                  protected system file (backed up first).

    A scheduled task re-applies the chosen state at every startup + daily, so a
    cumulative update that reverts the registry value or the JSON cannot silently
    strip the benefits back out.

    This is region/feature self-customization of the user's OWN machine. It is not
    a supported Microsoft configuration. Everything is backed up and reversible via
    -Action Restore / Uninstall.

.NOTES
    Project    : one-click-eu-grandpa
    Requires   : Windows 10/11, elevated (Administrator).
    License    : MIT. No warranty.
#>

[CmdletBinding()]
param(
    [ValidateSet('Apply', 'Reapply', 'Restore', 'Status', 'Uninstall')]
    [string]$Action = 'Status',

    # Gentle = region only. Hardcore = region + JSON policy override.
    [ValidateSet('Gentle', 'Hardcore')]
    [string]$Mode = 'Gentle',

    # EEA country whose region code is written. Ireland by default.
    [ValidateSet('Ireland', 'Germany', 'France', 'Netherlands', 'Spain', 'Italy', 'Sweden', 'Poland', 'Finland', 'Austria')]
    [string]$Region = 'Ireland',

    # Uninstall only: also delete our own ProgramData (backups/logs/state).
    [switch]$FullClean,

    # Skip the "are you sure" prompt for Hardcore / Uninstall -FullClean.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
$Script:AppName   = 'OneClickEUGrandpa'
$Script:DataDir   = Join-Path $env:ProgramData $Script:AppName
$Script:BackupDir = Join-Path $Script:DataDir 'backups'
$Script:LogDir    = Join-Path $Script:DataDir 'logs'
$Script:StateFile = Join-Path $Script:DataDir 'state.json'
$Script:SelfCopy  = Join-Path $Script:DataDir 'EUGrandpa.ps1'
$Script:TaskName  = 'OneClickEUGrandpa-Persist'

$Script:RegPath   = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion'
$Script:RegName   = 'DeviceRegion'
$Script:PolicyFile = Join-Path $env:windir 'System32\IntegratedServicesRegionPolicySet.json'

# GeoID (decimal) for EEA countries. These are the standard Windows GEOIDs.
$Script:GeoIds = @{
    Ireland     = 68
    Germany     = 94
    France      = 84
    Netherlands = 176
    Spain       = 217
    Italy       = 118
    Sweden      = 221
    Poland      = 191
    Finland     = 73
    Austria     = 14
}
# Two-letter codes used inside the JSON region lists, parallel to the GeoIds.
$Script:IsoCodes = @{
    Ireland = 'IE'; Germany = 'DE'; France = 'FR'; Netherlands = 'NL'; Spain = 'ES'
    Italy = 'IT'; Sweden = 'SE'; Poland = 'PL'; Finland = 'FI'; Austria = 'AT'
}

# ---------------------------------------------------------------------------
# Infrastructure helpers
# ---------------------------------------------------------------------------
function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Initialize-DataDir {
    foreach ($d in @($Script:DataDir, $Script:BackupDir, $Script:LogDir)) {
        if (-not (Test-Path -LiteralPath $d)) {
            New-Item -ItemType Directory -Path $d -Force | Out-Null
        }
    }
}

function Write-Log {
    param([string]$Message, [ValidateSet('INFO', 'WARN', 'ERROR', 'OK')][string]$Level = 'INFO')
    $stamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $line  = "[$stamp] [$Level] $Message"
    try {
        Initialize-DataDir
        Add-Content -LiteralPath (Join-Path $Script:LogDir 'eugrandpa.log') -Value $line -Encoding UTF8
    } catch { }
    $color = switch ($Level) { 'ERROR' { 'Red' } 'WARN' { 'Yellow' } 'OK' { 'Green' } default { 'Gray' } }
    Write-Host $line -ForegroundColor $color
}

function Read-State {
    if (Test-Path -LiteralPath $Script:StateFile) {
        try { return Get-Content -LiteralPath $Script:StateFile -Raw -Encoding UTF8 | ConvertFrom-Json } catch { }
    }
    return $null
}

function Write-StateFile {
    param([hashtable]$State)
    Initialize-DataDir
    ($State | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $Script:StateFile -Encoding UTF8
}

# ---------------------------------------------------------------------------
# DeviceRegion read / write (handles the UCPD driver that blocks reg/powershell)
# ---------------------------------------------------------------------------
function Get-DeviceRegionValue {
    try {
        $v = Get-ItemProperty -LiteralPath $Script:RegPath -Name $Script:RegName -ErrorAction Stop
        return [int]$v.$($Script:RegName)
    } catch { return $null }
}

<#
  Windows 11's UserChoice Protection Driver (UCPD) guards the DeviceRegion value
  and denies writes from Microsoft-signed tools it recognizes by image name
  (reg.exe, powershell.exe, cmd.exe...). A direct Set-ItemProperty therefore
  "succeeds" but the value does not actually change.

  We first attempt the ordinary write and verify by read-back. If the value did
  not take, we fall back to invoking a RENAMED copy of reg.exe from a temp folder
  (an image name UCPD does not match). This is a least-privilege workaround: it
  does NOT disable the UCPD security driver for the rest of the system; it only
  bypasses the name filter for this single value, then cleans up.
#>
function Set-DeviceRegionValue {
    param([Parameter(Mandatory)][int]$GeoId)

    # Attempt 1: ordinary API write.
    try {
        New-Item -Path $Script:RegPath -Force | Out-Null
        Set-ItemProperty -LiteralPath $Script:RegPath -Name $Script:RegName -Value $GeoId -Type DWord -ErrorAction Stop
    } catch {
        Write-Log "Direct DeviceRegion write errored: $($_.Exception.Message)" WARN
    }
    if ((Get-DeviceRegionValue) -eq $GeoId) {
        Write-Log "DeviceRegion set to $GeoId via direct write." OK
        return $true
    }

    # Attempt 2: renamed reg.exe copy (UCPD name-filter bypass for this one value).
    Write-Log "Direct write blocked (likely UCPD). Retrying via renamed reg.exe." WARN
    $tmp = Join-Path $env:TEMP ("eug_" + [guid]::NewGuid().ToString('N') + ".exe")
    try {
        Copy-Item -LiteralPath (Join-Path $env:windir 'System32\reg.exe') -Destination $tmp -Force
        $regKey = 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion'
        & $tmp add $regKey /v $Script:RegName /t REG_DWORD /d $GeoId /f | Out-Null
    } catch {
        Write-Log "Renamed-reg fallback errored: $($_.Exception.Message)" ERROR
    } finally {
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
    }
    if ((Get-DeviceRegionValue) -eq $GeoId) {
        Write-Log "DeviceRegion set to $GeoId via renamed reg.exe." OK
        return $true
    }
    Write-Log "Could not set DeviceRegion to $GeoId. UCPD may require Safe Mode, or the value is MDM-managed." ERROR
    return $false
}

# ---------------------------------------------------------------------------
# JSON policy override (Hardcore mode)
# ---------------------------------------------------------------------------
function Backup-PolicyFile {
    Initialize-DataDir
    $dest = Join-Path $Script:BackupDir 'IntegratedServicesRegionPolicySet.original.json'
    if (-not (Test-Path -LiteralPath $dest)) {
        Copy-Item -LiteralPath $Script:PolicyFile -Destination $dest -Force
        Write-Log "Backed up original policy file to $dest" OK
    } else {
        Write-Log "Original policy backup already exists; keeping the first one." INFO
    }
}

function Take-Ownership {
    param([Parameter(Mandatory)][string]$Path)
    # Record nothing destructive; just make the file writable by Administrators.
    & takeown.exe /f $Path | Out-Null
    & icacls.exe $Path /grant *S-1-5-32-544:F | Out-Null   # BUILTIN\Administrators = SID S-1-5-32-544
}

<#
  Transform rule: for every policy, compute the state it WOULD evaluate to inside
  the EEA, then make it evaluate to that state for ALL regions by setting
  defaultState to the EEA state and clearing the region conditions. This is a
  generic, build-agnostic "give me the whole EEA/DMA superset" transform — it does
  not hardcode individual GUIDs, so it keeps working as Microsoft adds policies.
#>
function Get-EeaState {
    param($Policy, [string]$EeaCode)
    $ds = $Policy.defaultState
    if ($Policy.PSObject.Properties.Name -contains 'conditions' -and $Policy.conditions) {
        $cond = $Policy.conditions
        if ($cond.PSObject.Properties.Name -contains 'region' -and $cond.region) {
            $region = $cond.region
            if ($region.PSObject.Properties.Name -contains 'enabled' -and ($region.enabled -contains $EeaCode)) {
                return 'enabled'
            }
            if ($region.PSObject.Properties.Name -contains 'disabled' -and ($region.disabled -contains $EeaCode)) {
                return 'disabled'
            }
        }
    }
    # No region clause mentions the EEA → EEA sees the default.
    return $ds
}

function Set-PolicyFileEea {
    param([Parameter(Mandatory)][string]$EeaCode)

    Backup-PolicyFile
    $raw  = Get-Content -LiteralPath $Script:PolicyFile -Raw -Encoding UTF8
    $json = $raw | ConvertFrom-Json

    $changed = 0
    foreach ($p in $json.policies) {
        $eea = Get-EeaState -Policy $p -EeaCode $EeaCode
        if ($p.defaultState -ne $eea) { $p.defaultState = $eea; $changed++ }
        # Drop region conditions so the state applies everywhere.
        if ($p.PSObject.Properties.Name -contains 'conditions') {
            $p.PSObject.Properties.Remove('conditions')
        }
    }

    Take-Ownership -Path $Script:PolicyFile
    ($json | ConvertTo-Json -Depth 12) | Set-Content -LiteralPath $Script:PolicyFile -Encoding UTF8
    Write-Log "Policy file rewritten to EEA ($EeaCode) states for all regions. Policies adjusted: $changed / $($json.policies.Count)." OK
}

function Restore-PolicyFile {
    $src = Join-Path $Script:BackupDir 'IntegratedServicesRegionPolicySet.original.json'
    if (Test-Path -LiteralPath $src) {
        Take-Ownership -Path $Script:PolicyFile
        Copy-Item -LiteralPath $src -Destination $Script:PolicyFile -Force
        Write-Log "Policy file restored from backup." OK
    } else {
        Write-Log "No policy backup found; leaving current policy file as-is." WARN
    }
}

# ---------------------------------------------------------------------------
# Persistence (scheduled task)
# ---------------------------------------------------------------------------
function Install-PersistTask {
    # Copy ourselves into ProgramData so the task survives deletion of the repo folder.
    Initialize-DataDir
    Copy-Item -LiteralPath $PSCommandPath -Destination $Script:SelfCopy -Force

    $psArgs = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$Script:SelfCopy`" -Action Reapply"
    $action  = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $psArgs
    $tLogon  = New-ScheduledTaskTrigger -AtLogOn
    $tDaily  = New-ScheduledTaskTrigger -Daily -At 3am
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable

    Register-ScheduledTask -TaskName $Script:TaskName -Action $action -Trigger @($tLogon, $tDaily) `
        -Principal $principal -Settings $settings -Force | Out-Null
    Write-Log "Persistence task '$($Script:TaskName)' installed (logon + daily, SYSTEM)." OK
}

function Remove-PersistTask {
    if (Get-ScheduledTask -TaskName $Script:TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $Script:TaskName -Confirm:$false
        Write-Log "Persistence task removed." OK
    }
}

# ---------------------------------------------------------------------------
# High-level actions
# ---------------------------------------------------------------------------
function Invoke-Apply {
    param([string]$Mode, [string]$Region, [switch]$FromReapply)

    $geo = $Script:GeoIds[$Region]
    $iso = $Script:IsoCodes[$Region]

    # Preserve the very first observed original region so Restore is honest.
    $existing = Read-State
    $hasOrig  = $existing -and ($existing.PSObject.Properties.Name -contains 'originalRegion') -and ($null -ne $existing.originalRegion)
    $originalRegion = if ($hasOrig) { [int]$existing.originalRegion } else { Get-DeviceRegionValue }

    Set-DeviceRegionValue -GeoId $geo | Out-Null

    if ($Mode -eq 'Hardcore') {
        Set-PolicyFileEea -EeaCode $iso
    }

    Write-StateFile @{
        mode           = $Mode
        region         = $Region
        geoId          = $geo
        isoCode        = $iso
        originalRegion = $originalRegion
        appliedUtc     = (Get-Date).ToUniversalTime().ToString('o')
    }

    if (-not $FromReapply) { Install-PersistTask }
    Write-Log "Apply complete. Mode=$Mode Region=$Region. Sign out / restart to see the full effect." OK
}

function Invoke-Reapply {
    $state = Read-State
    if (-not $state) { Write-Log "No saved state; nothing to reapply." WARN; return }
    Write-Log "Reapply triggered (persistence)." INFO
    Invoke-Apply -Mode $state.mode -Region $state.region -FromReapply
}

function Invoke-Restore {
    $state = Read-State
    Remove-PersistTask
    $stateHasMode = $state -and ($state.PSObject.Properties.Name -contains 'mode')
    $stateHasOrig = $state -and ($state.PSObject.Properties.Name -contains 'originalRegion') -and ($null -ne $state.originalRegion)
    if ($stateHasMode -and $state.mode -eq 'Hardcore') { Restore-PolicyFile }
    if ($stateHasOrig) {
        Set-DeviceRegionValue -GeoId ([int]$state.originalRegion) | Out-Null
        Write-Log "Device region restored to original ($($state.originalRegion))." OK
    } else {
        Write-Log "Original region unknown; leaving DeviceRegion as-is. Set it in Settings > Time & language if needed." WARN
    }
    Write-Log "Restore complete. Benefits reverted. Restart to finish." OK
}

function Invoke-Uninstall {
    param([switch]$FullClean, [switch]$Force)
    Invoke-Restore
    if ($FullClean) {
        if (-not $Force) {
            $ans = Read-Host "Delete all EU Grandpa data (backups + logs) at $($Script:DataDir)? [y/N]"
            if ($ans -notin @('y', 'Y')) { Write-Log "Full clean cancelled; data kept." INFO; return }
        }
        if (Test-Path -LiteralPath $Script:DataDir) {
            Remove-Item -LiteralPath $Script:DataDir -Recurse -Force
            Write-Log "All application data removed. Fully uninstalled." OK
        }
    } else {
        Write-Log "Uninstalled. Backups/logs kept at $($Script:DataDir). Use -FullClean to remove them." INFO
    }
}

function Invoke-Status {
    $state = Read-State
    $cur   = Get-DeviceRegionValue
    $task  = Get-ScheduledTask -TaskName $Script:TaskName -ErrorAction SilentlyContinue

    Write-Host ""
    Write-Host "  一鍵轉生歐盟爺 — Status" -ForegroundColor Cyan
    Write-Host "  ------------------------------------------"
    Write-Host ("  Current DeviceRegion : {0}" -f ($(if ($cur -ne $null) { "$cur" } else { '(not set / computed from Geo)' })))
    if ($state) {
        Write-Host ("  Applied mode         : {0}" -f $state.mode)
        Write-Host ("  Applied region       : {0} (GeoID {1}, {2})" -f $state.region, $state.geoId, $state.isoCode)
        Write-Host ("  Original region       : {0}" -f $state.originalRegion)
        Write-Host ("  Applied (UTC)        : {0}" -f $state.appliedUtc)
    } else {
        Write-Host "  Applied mode         : (never applied)"
    }
    Write-Host ("  Persistence task     : {0}" -f ($(if ($task) { "installed ($($task.State))" } else { 'not installed' })))
    $eea = @('IE','DE','FR','NL','ES','IT','SE','PL','FI','AT','BE','DK')
    $isEea = ($cur -ne $null) -and ($Script:GeoIds.Values -contains $cur)
    Write-Host ("  EEA state active     : {0}" -f ($(if ($isEea) { 'YES' } else { 'no' }))) -ForegroundColor $(if ($isEea) { 'Green' } else { 'Yellow' })
    Write-Host ""
}

# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------
if (-not (Test-Admin)) {
    Write-Host "This tool must run as Administrator. Right-click the launcher and 'Run as administrator'." -ForegroundColor Red
    exit 1
}
Initialize-DataDir

switch ($Action) {
    'Apply' {
        if ($Mode -eq 'Hardcore' -and -not $Force) {
            Write-Host ""
            Write-Host "  HARDCORE mode rewrites a protected system file" -ForegroundColor Yellow
            Write-Host "  ($Script:PolicyFile)." -ForegroundColor Yellow
            Write-Host "  The original is backed up and fully restorable (Restore / Uninstall)." -ForegroundColor Yellow
            $ans = Read-Host "  Continue? [y/N]"
            if ($ans -notin @('y', 'Y')) { Write-Log "Hardcore apply cancelled by user." INFO; exit 0 }
        }
        Invoke-Apply -Mode $Mode -Region $Region
        Invoke-Status
    }
    'Reapply'   { Invoke-Reapply }
    'Restore'   { Invoke-Restore }
    'Uninstall' { Invoke-Uninstall -FullClean:$FullClean -Force:$Force }
    'Status'    { Invoke-Status }
}
