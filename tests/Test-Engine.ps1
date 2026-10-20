<#
.SYNOPSIS
    Self-contained tests for src/EUOverlord.ps1. No admin rights, no Pester.

.DESCRIPTION
    Loads the engine's constants and functions WITHOUT running its entry point,
    redirects every path into a temp sandbox, and checks:
      * the EEA transform and its no-BOM output,
      * restore from backup (byte-identical),
      * localized text in all three languages and the English-only log file,
      * migration of a v1.0.x ("EU Grandpa") data folder.
    Nothing outside the sandbox is touched. Exit code 0 = all passed.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tests\Test-Engine.ps1
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo    = Split-Path -Parent $PSScriptRoot
$engine  = Join-Path $repo 'src\EUOverlord.ps1'
$fixture = Join-Path $PSScriptRoot 'fixtures\policy-sample.json'

$script:Failures = 0
function Assert {
    param([bool]$Condition, [string]$Name)
    if ($Condition) { Write-Host "  PASS  $Name" -ForegroundColor Green }
    else { Write-Host "  FAIL  $Name" -ForegroundColor Red; $script:Failures++ }
}

# --- Encoding guard: Windows PowerShell 5.1 needs the BOM to read the CJK text.
$head = [IO.File]::ReadAllBytes($engine)[0..2]
Assert ($head[0] -eq 0xEF -and $head[1] -eq 0xBB -and $head[2] -eq 0xBF) 'engine is UTF-8 with BOM'

$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($engine, [ref]$tokens, [ref]$errors)
Assert ($errors.Count -eq 0) 'engine parses without errors'

# --- Load constants, message tables and functions, but not the entry point.
$defs = $ast.EndBlock.Statements | Where-Object {
    $_ -is [Management.Automation.Language.FunctionDefinitionAst] -or
    ($_ -is [Management.Automation.Language.AssignmentStatementAst] -and $_.Left.Extent.Text -like '$Script:*')
}
$Language = 'en'
foreach ($d in $defs) { . ([scriptblock]::Create($d.Extent.Text)) }

# --- Sandbox every path the engine uses.
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('euo-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sandbox | Out-Null
$Script:DataDir       = Join-Path $sandbox 'OneClickEUOverlord'
$Script:BackupDir     = Join-Path $Script:DataDir 'backups'
$Script:LogDir        = Join-Path $Script:DataDir 'logs'
$Script:StateFile     = Join-Path $Script:DataDir 'state.json'
$Script:LegacyDataDir = Join-Path $sandbox 'OneClickEUGrandpa'
$Script:LegacyTaskName = 'OneClickEUOverlord-TestOnly-NoSuchTask'
$Script:PolicyFile    = Join-Path $sandbox 'IntegratedServicesRegionPolicySet.json'
$Script:Lang          = 'en'
function Grant-AdminWrite { param([string]$Path) }   # no ACL changes in tests

try {
    Write-Host 'i18n'
    foreach ($lang in 'en', 'zh-TW', 'zh-CN') {
        $missing = @($Script:Msg['en'].Keys | Where-Object { -not $Script:Msg[$lang].ContainsKey($_) })
        Assert ($missing.Count -eq 0) "$lang has every message key ($($missing -join ', '))"
    }
    Assert ((T -Key ApplyDone -Arg @('Gentle', 'Ireland') -Lang en) -eq 'Apply complete. Mode=Gentle, Region=Ireland. Sign out or restart for the full effect.') 'English text with arguments'
    Assert ((T -Key Ireland -Lang zh-TW) -eq '愛爾蘭') 'Traditional Chinese region name'
    Assert ((T -Key Ireland -Lang zh-CN) -eq '爱尔兰') 'Simplified Chinese region name'
    Assert ((T -Key TaskState -Arg @('Ready') -Lang zh-CN) -eq '已安装（Ready）') 'Simplified Chinese with arguments'
    Assert ((Resolve-Language -Requested 'zh-CN') -eq 'zh-CN') 'explicit -Language wins'
    $map = @{ 'zh-Hant-TW' = 'zh-TW'; 'zh-TW' = 'zh-TW'; 'zh-HK' = 'zh-TW'; 'zh-MO' = 'zh-TW'
              'zh-Hans-CN' = 'zh-CN'; 'zh-CN' = 'zh-CN'; 'zh-SG' = 'zh-CN'; 'en-US' = 'en'; 'de-DE' = 'en' }
    $wrong = @($map.Keys | Where-Object { (ConvertTo-SupportedLanguage $_) -ne $map[$_] })
    Assert ($wrong.Count -eq 0) "language tags map correctly ($($wrong -join ', '))"
    Assert ($null -eq (ConvertTo-SupportedLanguage '')) 'empty language tag is ignored'

    Write-Host 'logging'
    $Script:Lang = 'zh-TW'
    Write-Log -Key TaskRemoved -Level OK 6>$null
    $Script:Lang = 'en'
    $logText = Get-Content -LiteralPath (Join-Path $Script:LogDir 'euoverlord.log') -Raw -Encoding UTF8
    Assert ($logText -match '\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] \[OK\] Persistence task removed\.') 'log file is English with a Gregorian timestamp, even when the console is zh-TW'

    Write-Host 'hardcore transform'
    Copy-Item -LiteralPath $fixture -Destination $Script:PolicyFile
    $original = [IO.File]::ReadAllBytes($Script:PolicyFile)
    Set-PolicyFileEea -EeaCode 'IE' 6>$null
    $bytes = [IO.File]::ReadAllBytes($Script:PolicyFile)
    Assert (-not ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB)) 'rewritten policy file has no BOM'
    $out = [IO.File]::ReadAllText($Script:PolicyFile) | ConvertFrom-Json
    $states = @($out.policies | ForEach-Object { $_.defaultState })
    Assert (($states -join ',') -eq 'enabled,disabled,disabled,enabled') 'each policy takes its EEA state'
    Assert (@($out.policies | Where-Object { $_.PSObject.Properties.Name -contains 'conditions' }).Count -eq 0) 'region conditions are removed'
    Assert ($out.'$schema' -eq 'schemas/IntegratedServicesRegionPolicySet.Schema.1.1.0.json') '$schema is preserved'
    Assert (Test-Path -LiteralPath (Join-Path $Script:BackupDir 'IntegratedServicesRegionPolicySet.original.json')) 'original is backed up'

    Set-PolicyFileEea -EeaCode 'IE' 6>$null   # second run must keep the FIRST backup
    $backup = [IO.File]::ReadAllBytes((Join-Path $Script:BackupDir 'IntegratedServicesRegionPolicySet.original.json'))
    Assert ([Convert]::ToBase64String($backup) -eq [Convert]::ToBase64String($original)) 're-applying keeps the first, untouched backup'

    Restore-PolicyFile 6>$null
    $restored = [IO.File]::ReadAllBytes($Script:PolicyFile)
    Assert ([Convert]::ToBase64String($restored) -eq [Convert]::ToBase64String($original)) 'restore is byte-identical'

    # The real Windows file, when present: transform a COPY and check it still parses.
    $real = Join-Path $env:windir 'System32\IntegratedServicesRegionPolicySet.json'
    if (Test-Path -LiteralPath $real) {
        Remove-Item -LiteralPath $Script:BackupDir -Recurse -Force
        Copy-Item -LiteralPath $real -Destination $Script:PolicyFile -Force
        $count = @(([IO.File]::ReadAllText($real) | ConvertFrom-Json).policies).Count
        Set-PolicyFileEea -EeaCode 'IE' 6>$null
        $rt = [IO.File]::ReadAllText($Script:PolicyFile) | ConvertFrom-Json
        Assert (@($rt.policies).Count -eq $count) "real policy file copy: all $count policies survive the transform"
    } else {
        Write-Host '  SKIP  real policy file not present on this machine'
    }

    Write-Host 'legacy migration'
    Remove-Item -LiteralPath $Script:DataDir -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $Script:LegacyDataDir 'backups') -Force | Out-Null
    '{"mode":"Gentle","region":"Ireland","geoId":68,"isoCode":"IE","originalRegion":237}' |
        Set-Content -LiteralPath (Join-Path $Script:LegacyDataDir 'state.json') -Encoding UTF8
    'old' | Set-Content -LiteralPath (Join-Path $Script:LegacyDataDir 'EUGrandpa.ps1')
    Invoke-LegacyMigration 6>$null
    Assert (-not (Test-Path -LiteralPath $Script:LegacyDataDir)) 'old folder is moved away'
    Assert ((Read-State).originalRegion -eq 237) 'state (original region) survives migration'
    Assert (-not (Test-Path -LiteralPath (Join-Path $Script:DataDir 'EUGrandpa.ps1'))) 'old script copy is removed'

    New-Item -ItemType Directory -Path $Script:LegacyDataDir | Out-Null
    Invoke-LegacyMigration 6>$null
    Assert (Test-Path -LiteralPath $Script:LegacyDataDir) 'when both folders exist, the old one is left untouched'
}
finally {
    Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:Failures -gt 0) { Write-Host "$script:Failures test(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All tests passed.' -ForegroundColor Green
