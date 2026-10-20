<#
.SYNOPSIS
    一鍵轉生歐盟爺 / 一键转生欧盟爷 / One-Click EU Overlord — core engine.
    (「歐盟爺」= 歐盟大老爺 / 欧盟大老爷 / the EU Overlord.)

.DESCRIPTION
    Unlocks the Windows 11 EEA/DMA ("European Economic Area" / Digital Markets Act)
    feature set on any machine and keeps it unlocked across updates and reboots.

      * Gentle  : sets the cached device region (HKLM\...\DeviceRegion) to an EEA
                  country so Windows itself computes the EEA policy states. Fully
                  reversible, touches only one registry value.

      * Hardcore: Gentle, PLUS rewrites C:\Windows\System32\IntegratedServicesRegionPolicySet.json
                  so every policy evaluates to its EEA/DMA state for ALL regions.
                  Survives even if the device region is later reset. Touches a
                  protected system file (backed up first).

    A scheduled task re-applies the chosen state at every logon + daily, so a
    cumulative update that reverts the registry value or the JSON cannot silently
    strip the benefits back out.

    Console output follows the Windows display language (Traditional Chinese,
    Simplified Chinese or English) unless -Language is given. The log file is
    always written in English so it can be attached to bug reports as-is.

    This is region/feature self-customization of the user's OWN machine. It is not
    a supported Microsoft configuration. Everything is backed up and reversible via
    -Action Restore / Uninstall.

.NOTES
    Project    : one-click-eu-overlord (formerly one-click-eu-grandpa)
    Requires   : Windows 10/11, elevated (Administrator), Windows PowerShell 5.1+.
    Encoding   : this file MUST stay UTF-8 with BOM (Windows PowerShell 5.1 decodes
                 BOM-less files with the ANSI code page, which garbles the CJK text).
    License    : MIT. No warranty.
#>

[CmdletBinding()]
param(
    # Menu (the default) is the interactive, double-click experience. The other
    # actions are for scripts and the scheduled task.
    [ValidateSet('Menu', 'Apply', 'Reapply', 'Restore', 'Status', 'Uninstall')]
    [string]$Action = 'Menu',

    # Gentle = region only. Hardcore = region + JSON policy override.
    [ValidateSet('Gentle', 'Hardcore')]
    [string]$Mode = 'Gentle',

    # EEA country whose region code is written. Ireland by default.
    [ValidateSet('Ireland', 'Germany', 'France', 'Netherlands', 'Spain', 'Italy', 'Sweden', 'Poland', 'Finland', 'Austria')]
    [string]$Region = 'Ireland',

    # Console language. 'auto' follows the Windows display language.
    [ValidateSet('auto', 'zh-TW', 'zh-CN', 'en')]
    [string]$Language = 'auto',

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
$Script:Version    = '1.2.0'   # must match the release tag (checked by the release workflow)
$Script:AppName    = 'OneClickEUOverlord'
$Script:DataDir    = Join-Path $env:ProgramData $Script:AppName
$Script:BackupDir  = Join-Path $Script:DataDir 'backups'
$Script:LogDir     = Join-Path $Script:DataDir 'logs'
$Script:StateFile  = Join-Path $Script:DataDir 'state.json'
$Script:SelfCopy   = Join-Path $Script:DataDir 'EUOverlord.ps1'
$Script:TaskName   = 'OneClickEUOverlord-Persist'

# Identifiers used by v1.0.x, when the project was called "One-Click EU Grandpa".
$Script:LegacyDataDir  = Join-Path $env:ProgramData 'OneClickEUGrandpa'
$Script:LegacyTaskName = 'OneClickEUGrandpa-Persist'
$Script:LegacyScript   = 'EUGrandpa.ps1'

$Script:RegPath    = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion'
$Script:RegName    = 'DeviceRegion'
$Script:PolicyFile = Join-Path $env:windir 'System32\IntegratedServicesRegionPolicySet.json'

# GeoID (decimal) for EEA countries. These are the standard Windows GEOIDs.
$Script:GeoIds = @{
    Ireland = 68; Germany = 94; France = 84; Netherlands = 176; Spain = 217
    Italy = 118; Sweden = 221; Poland = 191; Finland = 73; Austria = 14
}
# Two-letter codes used inside the JSON region lists, parallel to the GeoIds.
$Script:IsoCodes = @{
    Ireland = 'IE'; Germany = 'DE'; France = 'FR'; Netherlands = 'NL'; Spain = 'ES'
    Italy = 'IT'; Sweden = 'SE'; Poland = 'PL'; Finland = 'FI'; Austria = 'AT'
}

# ---------------------------------------------------------------------------
# Localized text
# ---------------------------------------------------------------------------
$Script:Msg = @{
    'en' = @{
        NeedAdmin          = "This tool must run as Administrator. Right-click the launcher and choose 'Run as administrator'."
        DirectWriteErr     = 'Direct DeviceRegion write failed: {0}'
        RegionAlready      = 'DeviceRegion is already {0}; nothing to change.'
        RegionSetDirect    = 'DeviceRegion set to {0} (direct write).'
        RegionBlocked      = 'Direct write was blocked (most likely by UCPD). Retrying with a renamed copy of reg.exe.'
        RenamedRegErr      = 'Renamed reg.exe fallback failed: {0}'
        RegionSetRenamed   = 'DeviceRegion set to {0} (renamed reg.exe).'
        RegionFailed       = 'Could not set DeviceRegion to {0}. UCPD may require Safe Mode, or the value is managed by MDM.'
        BackupDone         = 'Original policy file backed up to {0}'
        BackupExists       = 'An original policy backup already exists; keeping the first one.'
        PolicyRewritten    = 'Policy file rewritten to EEA ({0}) states for all regions. Policies changed: {1} / {2}.'
        PolicyRestored     = 'Policy file restored from backup.'
        PolicyNoBackup     = 'No policy backup found; leaving the current policy file unchanged.'
        TaskInstalled      = "Persistence task '{0}' installed (at logon + daily, as SYSTEM)."
        TaskRemoved        = 'Persistence task removed.'
        ApplyDone          = 'Apply complete. Mode={0}, Region={1}. Sign out or restart for the full effect.'
        NoState            = 'No saved state; nothing to re-apply.'
        ReapplyStart       = 'Re-apply triggered by the persistence task.'
        RegionRestored     = 'Device region restored to the original value ({0}).'
        RegionUnknown      = 'Original region unknown; leaving DeviceRegion unchanged. Set it in Settings > Time & language if needed.'
        RestoreDone        = 'Restore complete. The EEA state has been reverted. Restart to finish.'
        FullCleanPrompt    = 'Delete all One-Click EU Overlord data (backups and logs) in {0}? [y/N]'
        FullCleanCancelled = 'Full clean cancelled; data kept.'
        FullCleanDone      = 'All application data removed. Fully uninstalled.'
        UninstallKept      = 'Uninstalled. Backups and logs are kept in {0}. Use -FullClean to remove them.'
        HardcoreWarn       = 'HARDCORE mode rewrites a protected system file:'
        HardcoreWarn2      = 'The original is backed up and can be fully restored (Restore / Uninstall).'
        ContinuePrompt     = 'Continue? [y/N]'
        HardcoreCancelled  = 'Hardcore apply cancelled by the user.'
        MigrateDone        = 'Migrated the previous install (One-Click EU Grandpa) from {0} to {1}.'
        MigrateConflict    = 'Both {0} and the new data folder exist; keeping the new one and leaving the old folder untouched.'
        LegacyTaskRemoved  = "Removed the old persistence task '{0}'."
        StatusTitle        = 'One-Click EU Overlord - Status'
        CurRegion          = 'Current DeviceRegion'
        NotSet             = '(not set; computed from Geo)'
        AppliedMode        = 'Applied mode'
        AppliedRegion      = 'Applied region'
        OrigRegion         = 'Original region'
        AppliedAt          = 'Applied at (UTC)'
        NeverApplied       = '(never applied)'
        Task               = 'Persistence task'
        TaskState          = 'installed ({0})'
        TaskNone           = 'not installed'
        EeaActive          = 'EEA state active'
        Yes                = 'YES'
        No                 = 'no'
        LegacyFound        = 'A previous install (One-Click EU Grandpa) was found. Run Apply once to migrate it.'
        MenuTitle            = 'One-Click EU Overlord v{0}'
        MenuStateOn          = 'Status: you are already an EU overlord ({0} mode).'
        MenuStateUnprotected = 'Status: the device region is already in the EEA, but nothing protects it; a Windows update may change it back.'
        MenuStateOff         = 'Status: not an EU overlord yet.'
        MenuStateLegacy      = 'Status: an older version (One-Click EU Grandpa) is installed. Options 1-4 upgrade it automatically and keep its backups.'
        MenuOpt1             = '[1] Become an EU overlord (recommended) — changes one setting, the safest choice'
        MenuOpt2             = '[2] Hardcore — also rewrites a system policy file, the most robust'
        MenuOpt3             = '[3] Undo — put everything back the way it was'
        MenuOpt4             = "[4] Remove completely — undo, then delete all of this tool's data"
        MenuOpt0             = '[0] Exit'
        MenuPrompt           = 'Type a number and press Enter (just Enter = 1)'
        MenuInvalid          = 'There is no such option. Please try again.'
        AfterApply           = 'Done! After a restart you can uninstall Edge and other apps in Settings > Apps > Installed apps.'
        RestartPrompt        = 'A restart is needed for everything to take effect. Restart now? [y/N]'
        PressEnter           = 'Press Enter to close this window'
        RequestingAdmin      = 'Administrator permission is needed. Click "Yes" in the window that appears...'
        UacDeclined          = 'Administrator permission was not granted, so nothing was changed.'
        NotInteractive       = 'The menu needs an interactive window. For scripts, use -Action Apply, Restore, Status or Uninstall.'
        Mode_Gentle        = 'Gentle'
        Mode_Hardcore      = 'Hardcore'
        Ireland = 'Ireland'; Germany = 'Germany'; France = 'France'; Netherlands = 'Netherlands'; Spain = 'Spain'
        Italy = 'Italy'; Sweden = 'Sweden'; Poland = 'Poland'; Finland = 'Finland'; Austria = 'Austria'
    }
    'zh-TW' = @{
        NeedAdmin          = '此工具需要以系統管理員身分執行。請在啟動檔上按右鍵，選擇「以系統管理員身分執行」。'
        DirectWriteErr     = '直接寫入 DeviceRegion 失敗：{0}'
        RegionAlready      = 'DeviceRegion 已經是 {0}，無需變更。'
        RegionSetDirect    = '已將 DeviceRegion 設為 {0}（直接寫入）。'
        RegionBlocked      = '直接寫入被攔截（很可能是 UCPD），改用改名的 reg.exe 複本重試。'
        RenamedRegErr      = '改名 reg.exe 備援失敗：{0}'
        RegionSetRenamed   = '已將 DeviceRegion 設為 {0}（改名 reg.exe）。'
        RegionFailed       = '無法將 DeviceRegion 設為 {0}。可能需要在安全模式下操作，或此值由 MDM 管理。'
        BackupDone         = '已將原始策略檔備份到 {0}'
        BackupExists       = '原始策略檔備份已存在，保留最早的那一份。'
        PolicyRewritten    = '策略檔已改寫為對所有地區套用 EEA（{0}）狀態。變更策略數：{1} / {2}。'
        PolicyRestored     = '已從備份還原策略檔。'
        PolicyNoBackup     = '找不到策略檔備份，目前的策略檔維持不變。'
        TaskInstalled      = '已安裝持久化排程工作「{0}」（登入時＋每日，以 SYSTEM 執行）。'
        TaskRemoved        = '已移除持久化排程工作。'
        ApplyDone          = '套用完成。模式＝{0}，地區＝{1}。請登出或重新開機以完整生效。'
        NoState            = '沒有已儲存的狀態，無需重新套用。'
        ReapplyStart       = '持久化排程觸發重新套用。'
        RegionRestored     = '已將裝置地區還原為原本的值（{0}）。'
        RegionUnknown      = '不知道原本的地區，DeviceRegion 維持不變。如有需要，請到「設定 > 時間與語言」調整。'
        RestoreDone        = '還原完成，EEA 狀態已撤銷。請重新開機以完成。'
        FullCleanPrompt    = '要刪除 {0} 裡所有「一鍵轉生歐盟爺」的資料（備份與日誌）嗎？[y/N]'
        FullCleanCancelled = '已取消完整清除，資料保留。'
        FullCleanDone      = '已刪除所有程式資料，完整解除安裝。'
        UninstallKept      = '已解除安裝。備份與日誌保留在 {0}。加上 -FullClean 可一併刪除。'
        HardcoreWarn       = '徹底模式會改寫一個受保護的系統檔：'
        HardcoreWarn2      = '原始檔會先備份，可完整還原（Restore / Uninstall）。'
        ContinuePrompt     = '要繼續嗎？[y/N]'
        HardcoreCancelled  = '使用者取消了徹底模式。'
        MigrateDone        = '已將舊版安裝（One-Click EU Grandpa）從 {0} 遷移到 {1}。'
        MigrateConflict    = '{0} 與新的資料夾同時存在；保留新的，舊資料夾不動。'
        LegacyTaskRemoved  = '已移除舊的持久化排程工作「{0}」。'
        StatusTitle        = '一鍵轉生歐盟爺 — 狀態'
        CurRegion          = '目前 DeviceRegion'
        NotSet             = '（未設定，由 Geo 推算）'
        AppliedMode        = '套用模式'
        AppliedRegion      = '套用地區'
        OrigRegion         = '原本地區'
        AppliedAt          = '套用時間（UTC）'
        NeverApplied       = '（從未套用）'
        Task               = '持久化排程'
        TaskState          = '已安裝（{0}）'
        TaskNone           = '未安裝'
        EeaActive          = 'EEA 狀態生效'
        Yes                = '是'
        No                 = '否'
        LegacyFound        = '偵測到舊版安裝（One-Click EU Grandpa）。執行一次「套用」即可自動遷移。'
        MenuTitle            = '一鍵轉生歐盟爺 v{0}'
        MenuStateOn          = '目前狀態：已經是歐盟大老爺了（{0}模式）。'
        MenuStateUnprotected = '目前狀態：裝置地區已經是 EEA，但沒有受到保護，系統更新後可能被改回去。'
        MenuStateOff         = '目前狀態：還不是歐盟大老爺。'
        MenuStateLegacy      = '目前狀態：裝了舊版（One-Click EU Grandpa）。選 1～4 任一項都會自動升級，並保留原本的備份。'
        MenuOpt1             = '[1] 一鍵轉生（推薦）— 只改一個設定，最安全'
        MenuOpt2             = '[2] 徹底轉生 — 連系統策略檔一起改，效果最穩'
        MenuOpt3             = '[3] 還原 — 變回原本的樣子'
        MenuOpt4             = '[4] 完全移除 — 還原，並刪除本工具的所有資料'
        MenuOpt0             = '[0] 離開'
        MenuPrompt           = '請輸入數字後按 Enter（直接按 Enter＝1）'
        MenuInvalid          = '沒有這個選項，請再輸入一次。'
        AfterApply           = '完成！重新開機後，就能到「設定 → 應用程式 → 已安裝的應用程式」解除安裝 Edge 等程式。'
        RestartPrompt        = '需要重新開機才會完全生效。現在重新開機嗎？[y/N]'
        PressEnter           = '按 Enter 關閉視窗'
        RequestingAdmin      = '需要系統管理員權限，請在跳出的視窗中按「是」……'
        UacDeclined          = '沒有取得系統管理員權限，所以沒有做任何變更。'
        NotInteractive       = '選單需要在互動視窗中使用。腳本請改用 -Action Apply、Restore、Status 或 Uninstall。'
        Mode_Gentle        = '溫和'
        Mode_Hardcore      = '徹底'
        Ireland = '愛爾蘭'; Germany = '德國'; France = '法國'; Netherlands = '荷蘭'; Spain = '西班牙'
        Italy = '義大利'; Sweden = '瑞典'; Poland = '波蘭'; Finland = '芬蘭'; Austria = '奧地利'
    }
    'zh-CN' = @{
        NeedAdmin          = '此工具需要以管理员身份运行。请在启动文件上右键，选择“以管理员身份运行”。'
        DirectWriteErr     = '直接写入 DeviceRegion 失败：{0}'
        RegionAlready      = 'DeviceRegion 已经是 {0}，无需更改。'
        RegionSetDirect    = '已将 DeviceRegion 设为 {0}（直接写入）。'
        RegionBlocked      = '直接写入被拦截（很可能是 UCPD），改用重命名的 reg.exe 副本重试。'
        RenamedRegErr      = '重命名 reg.exe 备用方案失败：{0}'
        RegionSetRenamed   = '已将 DeviceRegion 设为 {0}（重命名 reg.exe）。'
        RegionFailed       = '无法将 DeviceRegion 设为 {0}。可能需要在安全模式下操作，或该值由 MDM 管理。'
        BackupDone         = '已将原始策略文件备份到 {0}'
        BackupExists       = '原始策略文件备份已存在，保留最早的那一份。'
        PolicyRewritten    = '策略文件已改写为对所有地区应用 EEA（{0}）状态。变更策略数：{1} / {2}。'
        PolicyRestored     = '已从备份恢复策略文件。'
        PolicyNoBackup     = '找不到策略文件备份，当前策略文件保持不变。'
        TaskInstalled      = '已安装持久化计划任务“{0}”（登录时＋每天，以 SYSTEM 运行）。'
        TaskRemoved        = '已删除持久化计划任务。'
        ApplyDone          = '应用完成。模式＝{0}，地区＝{1}。请注销或重启以完全生效。'
        NoState            = '没有已保存的状态，无需重新应用。'
        ReapplyStart       = '持久化计划任务触发重新应用。'
        RegionRestored     = '已将设备区域恢复为原来的值（{0}）。'
        RegionUnknown      = '不知道原来的区域，DeviceRegion 保持不变。如有需要，请到“设置 > 时间和语言”调整。'
        RestoreDone        = '恢复完成，EEA 状态已撤销。请重启以完成。'
        FullCleanPrompt    = '要删除 {0} 中所有“一键转生欧盟爷”的数据（备份和日志）吗？[y/N]'
        FullCleanCancelled = '已取消完全清除，数据保留。'
        FullCleanDone      = '已删除所有程序数据，完全卸载。'
        UninstallKept      = '已卸载。备份和日志保留在 {0}。加上 -FullClean 可一并删除。'
        HardcoreWarn       = '彻底模式会改写一个受保护的系统文件：'
        HardcoreWarn2      = '原始文件会先备份，可完整恢复（Restore / Uninstall）。'
        ContinuePrompt     = '要继续吗？[y/N]'
        HardcoreCancelled  = '用户取消了彻底模式。'
        MigrateDone        = '已将旧版安装（One-Click EU Grandpa）从 {0} 迁移到 {1}。'
        MigrateConflict    = '{0} 与新的文件夹同时存在；保留新的，旧文件夹不动。'
        LegacyTaskRemoved  = '已删除旧的持久化计划任务“{0}”。'
        StatusTitle        = '一键转生欧盟爷 — 状态'
        CurRegion          = '当前 DeviceRegion'
        NotSet             = '（未设置，由 Geo 推算）'
        AppliedMode        = '应用模式'
        AppliedRegion      = '应用地区'
        OrigRegion         = '原来地区'
        AppliedAt          = '应用时间（UTC）'
        NeverApplied       = '（从未应用）'
        Task               = '持久化计划任务'
        TaskState          = '已安装（{0}）'
        TaskNone           = '未安装'
        EeaActive          = 'EEA 状态生效'
        Yes                = '是'
        No                 = '否'
        LegacyFound        = '检测到旧版安装（One-Click EU Grandpa）。运行一次“应用”即可自动迁移。'
        MenuTitle            = '一键转生欧盟爷 v{0}'
        MenuStateOn          = '当前状态：已经是欧盟大老爷了（{0}模式）。'
        MenuStateUnprotected = '当前状态：设备区域已经是 EEA，但没有受到保护，系统更新后可能被改回去。'
        MenuStateOff         = '当前状态：还不是欧盟大老爷。'
        MenuStateLegacy      = '当前状态：装了旧版（One-Click EU Grandpa）。选 1～4 任一项都会自动升级，并保留原来的备份。'
        MenuOpt1             = '[1] 一键转生（推荐）— 只改一个设置，最安全'
        MenuOpt2             = '[2] 彻底转生 — 连系统策略文件一起改，效果最稳'
        MenuOpt3             = '[3] 恢复 — 变回原来的样子'
        MenuOpt4             = '[4] 完全移除 — 恢复，并删除本工具的所有数据'
        MenuOpt0             = '[0] 退出'
        MenuPrompt           = '请输入数字后按 Enter（直接按 Enter＝1）'
        MenuInvalid          = '没有这个选项，请重新输入。'
        AfterApply           = '完成！重启后，就能到“设置 → 应用 → 安装的应用”卸载 Edge 等程序。'
        RestartPrompt        = '需要重启才会完全生效。现在重启吗？[y/N]'
        PressEnter           = '按 Enter 关闭窗口'
        RequestingAdmin      = '需要管理员权限，请在弹出的窗口中点“是”……'
        UacDeclined          = '没有获得管理员权限，所以没有做任何更改。'
        NotInteractive       = '菜单需要在交互窗口中使用。脚本请改用 -Action Apply、Restore、Status 或 Uninstall。'
        Mode_Gentle        = '温和'
        Mode_Hardcore      = '彻底'
        Ireland = '爱尔兰'; Germany = '德国'; France = '法国'; Netherlands = '荷兰'; Spain = '西班牙'
        Italy = '意大利'; Sweden = '瑞典'; Poland = '波兰'; Finland = '芬兰'; Austria = '奥地利'
    }
}

# Maps a BCP-47 tag (zh-Hant-TW, zh-HK, zh-CN, en-US...) to a supported language.
function ConvertTo-SupportedLanguage {
    param([string]$Tag)
    if ([string]::IsNullOrEmpty($Tag)) { return $null }
    if ($Tag -like 'zh-Hant*' -or $Tag -match '^zh-(TW|HK|MO)$') { return 'zh-TW' }
    if ($Tag -like 'zh*') { return 'zh-CN' }
    return 'en'
}

<#
  'auto' uses the language the user ranked FIRST in Settings > Language, and only
  then the display language: Windows can keep showing English (no language pack,
  or an override) for someone whose first choice is Chinese.
#>
function Resolve-Language {
    param([string]$Requested)
    if ($Requested -ne 'auto') { return $Requested }
    $preferred = $null
    try { $preferred = @(Get-WinUserLanguageList)[0].LanguageTag } catch { }
    $lang = ConvertTo-SupportedLanguage $preferred
    if ($lang) { return $lang }
    $lang = ConvertTo-SupportedLanguage (Get-UICulture).Name
    if ($lang) { return $lang }
    return 'en'
}

$Script:Lang = Resolve-Language -Requested $Language

# Translate a message key. Falls back to English for a missing key.
function T {
    param([Parameter(Mandatory)][string]$Key, [object[]]$Arg = @(), [string]$Lang = $Script:Lang)
    $table = $Script:Msg[$Lang]
    $text  = if ($table.ContainsKey($Key)) { $table[$Key] } else { $Script:Msg['en'][$Key] }
    if ($Arg.Count -gt 0) { return [string]::Format([Globalization.CultureInfo]::InvariantCulture, $text, $Arg) }
    return $text
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

# Logs a message: localized on the console, English in the log file.
# -Arg values are used as-is; -ArgKey values are message keys translated per
# language, so the log file never picks up the console's language.
function Write-Log {
    param(
        [Parameter(Mandatory)][string]$Key,
        [object[]]$Arg = @(),
        [string[]]$ArgKey = @(),
        [ValidateSet('INFO', 'WARN', 'ERROR', 'OK')][string]$Level = 'INFO'
    )
    # Invariant culture: on zh-TW the thread calendar would print the Minguo year.
    $stamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture)
    $fileArgs    = @($Arg) + @($ArgKey | ForEach-Object { T -Key $_ -Lang 'en' })
    $consoleArgs = @($Arg) + @($ArgKey | ForEach-Object { T -Key $_ })
    try {
        Initialize-DataDir
        $fileLine = "[$stamp] [$Level] $(T -Key $Key -Arg $fileArgs -Lang 'en')"
        Add-Content -LiteralPath (Join-Path $Script:LogDir 'euoverlord.log') -Value $fileLine -Encoding UTF8
    } catch { }
    $color = switch ($Level) { 'ERROR' { 'Red' } 'WARN' { 'Yellow' } 'OK' { 'Green' } default { 'Gray' } }
    Write-Host "[$stamp] [$Level] $(T -Key $Key -Arg $consoleArgs)" -ForegroundColor $color
}

function Read-State {
    if (Test-Path -LiteralPath $Script:StateFile) {
        try { return Get-Content -LiteralPath $Script:StateFile -Raw -Encoding UTF8 | ConvertFrom-Json } catch { }
    }
    return $null
}

function Test-StateField {
    param($State, [string]$Name)
    return ($null -ne $State) -and ($State.PSObject.Properties.Name -contains $Name) -and ($null -ne $State.$Name)
}

function Write-StateFile {
    param([hashtable]$State)
    Initialize-DataDir
    ($State | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $Script:StateFile -Encoding UTF8
}

function Test-Yes {
    param([string]$Answer)
    return $Answer -in @('y', 'Y', 'yes', 'Yes', 'YES', '是')
}

function Get-RegionLabel {
    param([string]$RegionKey)
    if ([string]::IsNullOrEmpty($RegionKey)) { return '' }
    return (T -Key $RegionKey)
}

# ---------------------------------------------------------------------------
# Migration from v1.0.x ("One-Click EU Grandpa")
# ---------------------------------------------------------------------------
function Test-LegacyInstall {
    return (Test-Path -LiteralPath $Script:LegacyDataDir) -or
           [bool](Get-ScheduledTask -TaskName $Script:LegacyTaskName -ErrorAction SilentlyContinue)
}

<#
  Moves the old ProgramData folder (state, backups, logs) to the new location and
  removes the old scheduled task, so Restore keeps working with the ORIGINAL
  backups taken by v1.0.x. Nothing is logged until the folder has moved, because
  logging creates the new data folder and would turn every migration into a
  conflict.
#>
function Invoke-LegacyMigration {
    $taskRemoved = $false
    if (Get-ScheduledTask -TaskName $Script:LegacyTaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $Script:LegacyTaskName -Confirm:$false
        $taskRemoved = $true
    }

    $moved = $false
    $conflict = $false
    if (Test-Path -LiteralPath $Script:LegacyDataDir) {
        if (Test-Path -LiteralPath $Script:DataDir) {
            $conflict = $true
        } else {
            Move-Item -LiteralPath $Script:LegacyDataDir -Destination $Script:DataDir
            $oldCopy = Join-Path $Script:DataDir $Script:LegacyScript
            if (Test-Path -LiteralPath $oldCopy) { Remove-Item -LiteralPath $oldCopy -Force }
            $moved = $true
        }
    }

    if ($taskRemoved) { Write-Log -Key LegacyTaskRemoved -Arg @($Script:LegacyTaskName) -Level OK }
    if ($moved)       { Write-Log -Key MigrateDone -Arg @($Script:LegacyDataDir, $Script:DataDir) -Level OK }
    if ($conflict)    { Write-Log -Key MigrateConflict -Arg @($Script:LegacyDataDir) -Level WARN }
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
  (reg.exe, powershell.exe, cmd.exe...).

  We first attempt the ordinary write and verify by read-back. If the value did
  not take, we fall back to invoking a RENAMED copy of reg.exe from a temp folder
  (an image name UCPD does not match). This does NOT disable the UCPD driver for
  the rest of the system; it only changes this single value, then cleans up.
#>
function Set-DeviceRegionValue {
    param([Parameter(Mandatory)][int]$GeoId)

    if ((Get-DeviceRegionValue) -eq $GeoId) {
        Write-Log -Key RegionAlready -Arg @($GeoId) -Level OK
        return $true
    }

    # Attempt 1: ordinary API write.
    try {
        New-Item -Path $Script:RegPath -Force | Out-Null
        Set-ItemProperty -LiteralPath $Script:RegPath -Name $Script:RegName -Value $GeoId -Type DWord -ErrorAction Stop
    } catch {
        Write-Log -Key DirectWriteErr -Arg @($_.Exception.Message) -Level WARN
    }
    if ((Get-DeviceRegionValue) -eq $GeoId) {
        Write-Log -Key RegionSetDirect -Arg @($GeoId) -Level OK
        return $true
    }

    # Attempt 2: renamed reg.exe copy (UCPD name-filter bypass for this one value).
    Write-Log -Key RegionBlocked -Level WARN
    $tmp = Join-Path $env:TEMP ('euo_' + [guid]::NewGuid().ToString('N') + '.exe')
    try {
        Copy-Item -LiteralPath (Join-Path $env:windir 'System32\reg.exe') -Destination $tmp -Force
        $regKey = 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion'
        & $tmp add $regKey /v $Script:RegName /t REG_DWORD /d $GeoId /f | Out-Null
    } catch {
        Write-Log -Key RenamedRegErr -Arg @($_.Exception.Message) -Level ERROR
    } finally {
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
    }
    if ((Get-DeviceRegionValue) -eq $GeoId) {
        Write-Log -Key RegionSetRenamed -Arg @($GeoId) -Level OK
        return $true
    }
    Write-Log -Key RegionFailed -Arg @($GeoId) -Level ERROR
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
        Write-Log -Key BackupDone -Arg @($dest) -Level OK
    } else {
        Write-Log -Key BackupExists
    }
}

function Grant-AdminWrite {
    param([Parameter(Mandatory)][string]$Path)
    & takeown.exe /f $Path | Out-Null
    & icacls.exe $Path /grant '*S-1-5-32-544:F' | Out-Null   # BUILTIN\Administrators
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
    return $Policy.defaultState
}

function Set-PolicyFileEea {
    param([Parameter(Mandatory)][string]$EeaCode)

    Backup-PolicyFile
    $json = [IO.File]::ReadAllText($Script:PolicyFile, [Text.Encoding]::UTF8) | ConvertFrom-Json

    $changed = 0
    foreach ($p in $json.policies) {
        $eea = Get-EeaState -Policy $p -EeaCode $EeaCode
        if ($p.defaultState -ne $eea) { $p.defaultState = $eea; $changed++ }
        if ($p.PSObject.Properties.Name -contains 'conditions') {
            $p.PSObject.Properties.Remove('conditions')
        }
    }

    Grant-AdminWrite -Path $Script:PolicyFile
    # The shipped file is UTF-8 WITHOUT a BOM. Windows PowerShell 5.1's
    # Set-Content -Encoding UTF8 would add one, so write the bytes explicitly.
    $out = $json | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($Script:PolicyFile, $out, (New-Object Text.UTF8Encoding($false)))
    Write-Log -Key PolicyRewritten -Arg @($EeaCode, $changed, @($json.policies).Count) -Level OK
}

function Restore-PolicyFile {
    $src = Join-Path $Script:BackupDir 'IntegratedServicesRegionPolicySet.original.json'
    if (Test-Path -LiteralPath $src) {
        Grant-AdminWrite -Path $Script:PolicyFile
        Copy-Item -LiteralPath $src -Destination $Script:PolicyFile -Force
        Write-Log -Key PolicyRestored -Level OK
    } else {
        Write-Log -Key PolicyNoBackup -Level WARN
    }
}

# ---------------------------------------------------------------------------
# Persistence (scheduled task)
# ---------------------------------------------------------------------------
function Install-PersistTask {
    # Copy ourselves into ProgramData so the task survives deletion of the download folder.
    Initialize-DataDir
    Copy-Item -LiteralPath $PSCommandPath -Destination $Script:SelfCopy -Force

    $psArgs    = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$Script:SelfCopy`" -Action Reapply -Language en"
    $action    = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $psArgs
    $tLogon    = New-ScheduledTaskTrigger -AtLogOn
    $tDaily    = New-ScheduledTaskTrigger -Daily -At 3am
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable

    Register-ScheduledTask -TaskName $Script:TaskName -Action $action -Trigger @($tLogon, $tDaily) `
        -Principal $principal -Settings $settings -Force | Out-Null
    Write-Log -Key TaskInstalled -Arg @($Script:TaskName) -Level OK
}

function Remove-PersistTask {
    if (Get-ScheduledTask -TaskName $Script:TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $Script:TaskName -Confirm:$false
        Write-Log -Key TaskRemoved -Level OK
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
    $originalRegion = if (Test-StateField $existing 'originalRegion') { [int]$existing.originalRegion } else { Get-DeviceRegionValue }

    Set-DeviceRegionValue -GeoId $geo | Out-Null

    if ($Mode -eq 'Hardcore') {
        Set-PolicyFileEea -EeaCode $iso
    }

    Write-StateFile @{
        schemaVersion  = 1
        mode           = $Mode
        region         = $Region
        geoId          = $geo
        isoCode        = $iso
        originalRegion = $originalRegion
        appliedUtc     = (Get-Date).ToUniversalTime().ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    }

    if (-not $FromReapply) { Install-PersistTask }
    Write-Log -Key ApplyDone -ArgKey @("Mode_$Mode", $Region) -Level OK
}

function Invoke-Reapply {
    $state = Read-State
    if (-not ((Test-StateField $state 'mode') -and (Test-StateField $state 'region'))) {
        Write-Log -Key NoState -Level WARN
        return
    }
    Write-Log -Key ReapplyStart
    Invoke-Apply -Mode $state.mode -Region $state.region -FromReapply
}

function Invoke-Restore {
    $state = Read-State
    Remove-PersistTask
    if ((Test-StateField $state 'mode') -and $state.mode -eq 'Hardcore') { Restore-PolicyFile }
    if (Test-StateField $state 'originalRegion') {
        Set-DeviceRegionValue -GeoId ([int]$state.originalRegion) | Out-Null
        Write-Log -Key RegionRestored -Arg @($state.originalRegion) -Level OK
    } else {
        Write-Log -Key RegionUnknown -Level WARN
    }
    Write-Log -Key RestoreDone -Level OK
}

function Invoke-Uninstall {
    param([switch]$FullClean, [switch]$Force)
    Invoke-Restore
    if ($FullClean) {
        if (-not $Force) {
            if (-not (Test-Yes (Read-Host (T -Key FullCleanPrompt -Arg @($Script:DataDir))))) {
                Write-Log -Key FullCleanCancelled
                return
            }
        }
        if (Test-Path -LiteralPath $Script:DataDir) {
            Remove-Item -LiteralPath $Script:DataDir -Recurse -Force
        }
        # Logging here would recreate the folder we just deleted.
        Write-Host (T -Key FullCleanDone) -ForegroundColor Green
    } else {
        Write-Log -Key UninstallKept -Arg @($Script:DataDir)
    }
}

function Invoke-Status {
    $state = Read-State
    $cur   = Get-DeviceRegionValue
    $task  = Get-ScheduledTask -TaskName $Script:TaskName -ErrorAction SilentlyContinue

    Write-Host ''
    Write-Host ('  ' + (T StatusTitle)) -ForegroundColor Cyan
    Write-Host '  ------------------------------------------'
    Write-Host ('  {0}: {1}' -f (T CurRegion), $(if ($null -ne $cur) { "$cur" } else { T NotSet }))
    if (Test-StateField $state 'mode') {
        Write-Host ('  {0}: {1}' -f (T AppliedMode), (T -Key "Mode_$($state.mode)"))
        Write-Host ('  {0}: {1} (GeoID {2}, {3})' -f (T AppliedRegion), (Get-RegionLabel $state.region), $state.geoId, $state.isoCode)
        Write-Host ('  {0}: {1}' -f (T OrigRegion), $state.originalRegion)
        Write-Host ('  {0}: {1}' -f (T AppliedAt), $state.appliedUtc)
    } else {
        Write-Host ('  {0}: {1}' -f (T AppliedMode), (T NeverApplied))
    }
    Write-Host ('  {0}: {1}' -f (T Task), $(if ($task) { T -Key TaskState -Arg @($task.State) } else { T TaskNone }))
    $isEea = ($null -ne $cur) -and ($Script:GeoIds.Values -contains $cur)
    Write-Host ('  {0}: {1}' -f (T EeaActive), $(if ($isEea) { T Yes } else { T No })) -ForegroundColor $(if ($isEea) { 'Green' } else { 'Yellow' })
    if (Test-LegacyInstall) { Write-Host ('  ' + (T LegacyFound)) -ForegroundColor Yellow }
    Write-Host ''
}

# ---------------------------------------------------------------------------
# Interactive menu (what a double-click shows)
# ---------------------------------------------------------------------------
# Picks the one-line status shown at the top of the menu.
function Get-MenuStateKey {
    param($State, $Region, [bool]$TaskInstalled, [bool]$Legacy)
    if ($Legacy) { return 'MenuStateLegacy' }
    $isEea = ($null -ne $Region) -and ($Script:GeoIds.Values -contains [int]$Region)
    if ($isEea -and $TaskInstalled -and (Test-StateField $State 'mode')) { return 'MenuStateOn' }
    if ($isEea) { return 'MenuStateUnprotected' }
    return 'MenuStateOff'
}

function Test-Interactive {
    try { return [Environment]::UserInteractive -and -not [Console]::IsInputRedirected } catch { return $false }
}

function Confirm-Hardcore {
    Write-Host ''
    Write-Host ('  ' + (T HardcoreWarn)) -ForegroundColor Yellow
    Write-Host "  $Script:PolicyFile" -ForegroundColor Yellow
    Write-Host ('  ' + (T HardcoreWarn2)) -ForegroundColor Yellow
    if (Test-Yes (Read-Host ('  ' + (T ContinuePrompt)))) { return $true }
    Write-Log -Key HardcoreCancelled
    return $false
}

function Request-Restart {
    Write-Host ''
    if (Test-Yes (Read-Host ('  ' + (T RestartPrompt)))) { Restart-Computer -Force }
}

<#
  Relaunches this script elevated in a new window and waits for it, so a
  double-click needs no "Run as administrator". If the user declines UAC,
  nothing has been changed and we say so in their language.
#>
function Invoke-SelfElevation {
    Write-Host (T RequestingAdmin) -ForegroundColor Cyan
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Action Menu -Language $Script:Lang"
    try {
        $p = Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs -Wait -PassThru
        if ($p) { return $p.ExitCode }
        return 0
    } catch {
        Write-Host (T UacDeclined) -ForegroundColor Yellow
        [void](Read-Host (T PressEnter))
        return 1
    }
}

function Invoke-Menu {
    $state  = Read-State
    $task   = [bool](Get-ScheduledTask -TaskName $Script:TaskName -ErrorAction SilentlyContinue)
    $key    = Get-MenuStateKey -State $state -Region (Get-DeviceRegionValue) -TaskInstalled $task -Legacy (Test-LegacyInstall)
    $mode   = if (Test-StateField $state 'mode') { T -Key "Mode_$($state.mode)" } else { '' }
    $color  = switch ($key) { 'MenuStateOn' { 'Green' } 'MenuStateOff' { 'Gray' } default { 'Yellow' } }

    Write-Host ''
    Write-Host ('  ' + (T -Key MenuTitle -Arg @($Script:Version))) -ForegroundColor Cyan
    Write-Host '  =============================================='
    Write-Host ('  ' + (T -Key $key -Arg @($mode))) -ForegroundColor $color
    Write-Host ''
    foreach ($k in 'MenuOpt1', 'MenuOpt2', 'MenuOpt3', 'MenuOpt4', 'MenuOpt0') { Write-Host ('  ' + (T $k)) }
    Write-Host ''

    do {
        $choice = "$(Read-Host ('  ' + (T MenuPrompt)))".Trim()
        if ($choice -eq '') { $choice = '1' }
        $valid = $choice -in @('0', '1', '2', '3', '4')
        if (-not $valid) { Write-Host ('  ' + (T MenuInvalid)) -ForegroundColor Yellow }
    } until ($valid)
    if ($choice -eq '0') { return }
    Write-Host ''

    switch ($choice) {
        '1' {
            Invoke-LegacyMigration
            Invoke-Apply -Mode Gentle -Region $Region
            Write-Host ''; Write-Host ('  ' + (T AfterApply)) -ForegroundColor Green
            Request-Restart
        }
        '2' {
            if (Confirm-Hardcore) {
                Invoke-LegacyMigration
                Invoke-Apply -Mode Hardcore -Region $Region
                Write-Host ''; Write-Host ('  ' + (T AfterApply)) -ForegroundColor Green
                Request-Restart
            }
        }
        '3' { Invoke-LegacyMigration; Invoke-Restore; Request-Restart }
        '4' { Invoke-LegacyMigration; Invoke-Uninstall -FullClean }
    }
    Write-Host ''
    [void](Read-Host ('  ' + (T PressEnter)))
}

# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------
# CJK text needs a Unicode console; the default zh-TW code page (950) cannot show
# Simplified Chinese at all. Best effort: the console may be redirected.
try { [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false) } catch { }

if ($Action -eq 'Menu') {
    # Never let a non-interactive caller fall through to "Enter = apply".
    if (-not (Test-Interactive)) { Write-Host (T NotInteractive) -ForegroundColor Red; exit 2 }
    if (-not (Test-Admin)) { exit (Invoke-SelfElevation) }
    Invoke-Menu
    exit 0
}

if (-not (Test-Admin)) {
    Write-Host (T NeedAdmin) -ForegroundColor Red
    exit 1
}

# Status stays read-only; every other action first adopts a v1.0.x install.
if ($Action -ne 'Status') { Invoke-LegacyMigration }

switch ($Action) {
    'Apply' {
        if ($Mode -eq 'Hardcore' -and -not $Force) {
            if (-not (Confirm-Hardcore)) { exit 0 }
        }
        Invoke-Apply -Mode $Mode -Region $Region
        Invoke-Status
    }
    'Reapply'   { Invoke-Reapply }
    'Restore'   { Invoke-Restore }
    'Uninstall' { Invoke-Uninstall -FullClean:$FullClean -Force:$Force }
    'Status'    { Invoke-Status }
}
