// Trilingual text.
//
// Every string is declared once with all three translations, so a missing
// translation is a compile error rather than a runtime blank. The implicit
// conversion to string lets WPF consume these directly (btn.Content = S.Apply).
//
// Language detection prefers the user's own language list over Get-UICulture:
// on this machine the UI culture is en-US while the first preferred language is
// zh-Hant-TW, and v1 shipped English to a Traditional Chinese user because of it.
//
// The log file is deliberately NOT localized -- see Log.cs.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace EULord.Core
{
    internal enum Lang { ZhHant, ZhHans, En }

    /// <summary>One piece of user-facing text in all three languages.</summary>
    internal sealed class L
    {
        private readonly string _tw, _cn, _en;
        public L(string tw, string cn, string en) { _tw = tw; _cn = cn; _en = en; }

        public string For(Lang lang)
        {
            switch (lang)
            {
                case Lang.ZhHant: return _tw;
                case Lang.ZhHans: return _cn;
                default: return _en;
            }
        }

        public override string ToString() { return For(S.Current); }
        public static implicit operator string(L s) { return s.ToString(); }

        /// <summary>English, always -- for the log and for machine-readable output.</summary>
        public string Invariant { get { return _en; } }
    }

    internal static class S
    {
        public static Lang Current = Lang.En;

        /// <summary>Picks a language from Windows settings. Explicit user choice wins.</summary>
        public static Lang Detect()
        {
            foreach (string tag in PreferredLanguageTags())
            {
                Lang guess;
                if (TryMatch(tag, out guess)) return guess;
            }
            return Lang.En;
        }

        internal static bool TryMatch(string tag, out Lang lang)
        {
            lang = Lang.En;
            if (string.IsNullOrEmpty(tag)) return false;
            string t = tag.Replace('_', '-').ToLowerInvariant();

            if (!t.StartsWith("zh", StringComparison.Ordinal)) return false;

            // Script subtag is decisive when present.
            if (t.Contains("hant")) { lang = Lang.ZhHant; return true; }
            if (t.Contains("hans")) { lang = Lang.ZhHans; return true; }

            // Otherwise fall back to the region: TW/HK/MO are Traditional.
            if (t.EndsWith("-tw", StringComparison.Ordinal) ||
                t.EndsWith("-hk", StringComparison.Ordinal) ||
                t.EndsWith("-mo", StringComparison.Ordinal)) { lang = Lang.ZhHant; return true; }

            lang = Lang.ZhHans;
            return true;
        }

        private static IEnumerable<string> PreferredLanguageTags()
        {
            // The user's ordered preferred-language list, which is what Windows
            // actually shows UI in. Available through MUI without PowerShell.
            foreach (string tag in Native.UserPreferredUILanguages()) yield return tag;

            yield return CultureInfo.CurrentUICulture.Name;
            yield return CultureInfo.CurrentCulture.Name;
        }

        // -- window chrome ----------------------------------------------------
        public static readonly L AppName = new L("一鍵轉生歐爺", "一键转生欧爷", "One-Click EU Lord");
        public static readonly L AppNameRoman = new L("ONE-CLICK EU LORD", "ONE-CLICK EU LORD", "一鍵轉生歐爺");
        public static readonly L Tagline = new L(
            "取得愛爾蘭歐盟政策的所有好處，不收它的損失",
            "获得爱尔兰欧盟政策的所有好处，不收它的损失",
            "Every gain from Ireland's EU policy. None of its losses.");

        // -- status -----------------------------------------------------------
        public static readonly L StatusApplied = new L("已套用", "已应用", "Applied");
        public static readonly L StatusPartial = new L("部分套用", "部分应用", "Partly applied");
        public static readonly L StatusNotApplied = new L("尚未套用", "尚未应用", "Not applied");
        public static readonly L StatusUnknown = new L("無法判斷", "无法判断", "Cannot tell");

        public static readonly L BenefitsActive = new L("好處生效中", "好处生效中", "benefits active");
        public static readonly L AppliedSummary = new L(
            "{0} 項好處已寫入，{1} 項損失保留原樣。部分項目要重新開機才會出現。",
            "{0} 项好处已写入，{1} 项损失保持原样。部分项目要重启才会出现。",
            "{0} gains are in place and the {1} losses were left alone. Some only appear after a restart.");
        public static readonly L NotAppliedSummary = new L(
            "這台電腦目前是一般的 Windows 11。套用後取得愛爾蘭歐盟版的好處，原本的功能一項不少。",
            "这台电脑目前是普通的 Windows 11。应用后获得爱尔兰欧盟版的好处，原本的功能一项不少。",
            "An ordinary Windows 11 so far. Applying takes Ireland's EU gains without giving up anything you have.");
        public static readonly L LegendGains = new L("好處，全部採用", "好处，全部采用", "gains, all taken");
        public static readonly L LegendTaste = new L("見仁見智，已選 {0}", "见仁见智，已选 {0}", "matters of taste, {0} picked");
        public static readonly L LegendLosses = new L("損失，保留", "损失，保留", "losses, kept");
        public static readonly L OfTotal = new L("共", "共", "of");

        public static readonly L RowRegion = new L("地區", "地区", "Region");
        public static readonly L RowPolicyFile = new L("策略檔", "策略文件", "Policy file");
        public static readonly L RowPersistence = new L("持久化", "持久化", "Persistence");
        public static readonly L RowChoices = new L("見仁見智", "见仁见智", "Choices");
        public static readonly L RowUnclassified = new L("未分類", "未分类", "Unclassified");

        public static readonly L PolicyOurs = new L("已改寫（我們的版本）", "已改写（我们的版本）", "Rewritten by this tool");
        public static readonly L PolicyPristine = new L("Windows 原始檔", "Windows 原始文件", "Windows original");
        public static readonly L PolicyMissing = new L("找不到", "找不到", "Not found");
        public static readonly L TaskReady = new L("排程已就緒", "计划任务已就绪", "Scheduled task ready");
        public static readonly L TaskAbsent = new L("未安裝", "未安装", "Not installed");
        public static readonly L TaskDisabled = new L("已安裝但停用", "已安装但禁用", "Installed but disabled");
        public static readonly L TaskStale = new L("指向舊版，需要重新安裝", "指向旧版，需要重新安装", "Points at an old build; reinstall");
        public static readonly L NoneSelected = new L("未選取任何項目", "未选择任何项目", "None selected");
        public static readonly L NSelected = new L("已選 {0} 項", "已选 {0} 项", "{0} selected");
        public static readonly L NOverridden = new L("{0} 項手動覆寫", "{0} 项手动覆写", "{0} overridden by hand");
        public static readonly L NPolicies = new L("{0} 條策略", "{0} 条策略", "{0} policies");
        public static readonly L NUnclassified = new L(
            "未分類 {0} 條，維持原樣",
            "未分类 {0} 条，保持原样",
            "{0} unclassified, left untouched");
        public static readonly L AllClassified = new L("全部已分類", "全部已分类", "All classified");

        // -- main action ------------------------------------------------------
        public static readonly L ApplySuperset = new L("套用超集", "应用超集", "Apply superset");
        public static readonly L ReapplySuperset = new L("重新套用", "重新应用", "Re-apply");
        public static readonly L Working = new L("執行中…", "执行中…", "Working…");
        public static readonly L ApplyHint = new L(
            "採用全部 {0} 項好處，不動 {1} 項損失，{2} 項見仁見智交給你決定。",
            "采用全部 {0} 项好处，不动 {1} 项损失，{2} 项见仁见智交给你决定。",
            "Takes all {0} gains, leaves the {1} losses alone, and lets you decide the {2} matters of taste.");
        public static readonly L NeedsRestart = new L(
            "部分項目要重新開機或重新登入才會出現。",
            "部分项目要重启或重新登录才会出现。",
            "Some items only appear after a restart or a new sign-in.");

        // -- sections ---------------------------------------------------------
        public static readonly L SecChoices = new L("見仁見智的功能", "见仁见智的功能", "Matters of taste");
        public static readonly L SecChoicesLead = new L(
            "這些既不算好處也不算損失。全部預設保留 Windows 原本的行為；勾選才改成歐盟版。",
            "这些既不算好处也不算损失。全部默认保留 Windows 原本的行为；勾选才改成欧盟版。",
            "Neither gains nor losses. All of them keep Windows' own behaviour unless you tick them.");
        public static readonly L SecApps = new L("幫我卸載 App", "帮我卸载 App", "Uninstall apps for me");
        public static readonly L SecAppsLead = new L(
            "一律呼叫 App 自己註冊的卸載程式，絕不手動刪檔。逐一確認。",
            "一律调用 App 自己注册的卸载程序，绝不手动删文件。逐一确认。",
            "Always runs the app's own registered uninstaller. Never deletes files by hand. Confirmed one at a time.");
        public static readonly L SecAdvanced = new L("進階", "高级", "Advanced");
        public static readonly L SecAdvancedLead = new L(
            "逐條覆寫全部 {0} 條策略、改用其他 EEA 國家、查看驗證結果。",
            "逐条覆写全部 {0} 条策略、改用其他 EEA 国家、查看验证结果。",
            "Override any of the {0} policies individually, pick another EEA country, read the verification report.");
        public static readonly L SecRestore = new L("還原與移除", "还原与移除", "Restore and remove");
        public static readonly L SecChoicesSummary = new L(
            "預設保留 Windows 原本的樣子，勾選才改成歐盟版。已選 {0} / {1} 項",
            "默认保留 Windows 原本的样子，勾选才改成欧盟版。已选 {0} / {1} 项",
            "Kept as Windows has them unless you tick one. {0} of {1} picked");
        public static readonly L SecAppsSummary = new L(
            "一律呼叫 App 自己的解除安裝程式，逐一確認",
            "一律调用 App 自己的卸载程序，逐一确认",
            "Always the app's own uninstaller, confirmed one at a time");
        public static readonly L RestoreSummary = new L(
            "原檔備份 2 份，隨時可以還原",
            "原文件备份 2 份，随时可以还原",
            "Two backups kept; restore whenever you like");
        public static readonly L PendingApply = new L("尚未套用", "尚未应用", "not applied yet");
        public static readonly L NoBackupShort = new L("無備份", "无备份", "No backup");

        // -- choices / policy table ------------------------------------------
        public static readonly L ColPolicy = new L("策略", "策略", "POLICY");
        public static readonly L ColClass = new L("分類", "分类", "CLASS");
        public static readonly L ColGlobal = new L("全球", "全球", "GLOBAL");
        public static readonly L ColEea = new L("歐盟", "欧盟", "EEA");
        public static readonly L ColTarget = new L("將設為", "将设为", "WILL BE");
        public static readonly L ClassBenefit = new L("好處", "好处", "Gain");
        public static readonly L ClassLoss = new L("損失", "损失", "Loss");
        public static readonly L ClassChoice = new L("見仁見智", "见仁见智", "Taste");
        public static readonly L StateEnabled = new L("開", "开", "On");
        public static readonly L StateDisabled = new L("關", "关", "Off");
        public static readonly L OverrideAuto = new L("自動", "自动", "Auto");
        public static readonly L OverrideEea = new L("歐盟版", "欧盟版", "EEA");
        public static readonly L OverrideGlobal = new L("原本的", "原本的", "Global");
        public static readonly L FilterAll = new L("全部", "全部", "All");
        public static readonly L TickTurnsOn = new L("勾選後：開啟", "勾选后：开启", "Ticking turns this on");
        public static readonly L TickTurnsOff = new L("勾選後：關閉", "勾选后：关闭", "Ticking turns this off");
        public static readonly L SearchPolicies = new L("搜尋策略…", "搜索策略…", "Search policies…");

        // -- region -----------------------------------------------------------
        public static readonly L RegionLabel = new L("裝置地區", "设备区域", "Device region");
        public static readonly L RegionHint = new L(
            "愛爾蘭同時受歐盟與 EEA 規範，好處最完整。改成其他 EEA 國家不會減少策略，只影響地區相關的行為。",
            "爱尔兰同时受欧盟与 EEA 规范，好处最完整。改成其他 EEA 国家不会减少策略，只影响地区相关的行为。",
            "Ireland is covered by both the EU and the EEA, so it unlocks the most. Another EEA country changes only region-dependent behaviour, never the policy set.");
        public static readonly L RegionOriginal = new L("原本的地區", "原本的区域", "Original region");

        // -- apps -------------------------------------------------------------
        public static readonly L AppUninstall = new L("卸載", "卸载", "Uninstall");
        public static readonly L AppReinstall = new L("重新安裝", "重新安装", "Reinstall");
        public static readonly L AppNotInstalled = new L("未安裝", "未安装", "Not installed");
        public static readonly L AppRemoved = new L("已卸載", "已卸载", "Uninstalled");
        public static readonly L AppNotRemovable = new L("無法卸載", "无法卸载", "Cannot be uninstalled");
        public static readonly L AppNoUninstaller = new L(
            "找不到官方卸載程式",
            "找不到官方卸载程序",
            "No registered uninstaller");
        public static readonly L AppSystemProtected = new L(
            "Windows 標記為不可移除",
            "Windows 标记为不可移除",
            "Windows marks it non-removable");
        public static readonly L AppNeedsPolicyFirst = new L(
            "先套用超集，這個 App 才會變成可卸載",
            "先应用超集，这个 App 才会变成可卸载",
            "Apply the superset first to make this app uninstallable");
        public static readonly L AppConfirm = new L(
            "要卸載「{0}」嗎？將執行它自己註冊的卸載程式。",
            "要卸载“{0}”吗？将执行它自己注册的卸载程序。",
            "Uninstall {0}? This runs the uninstaller the app itself registered.");
        public static readonly L AppConfirmTitle = new L("逐一確認", "逐一确认", "One at a time");
        public static readonly L AppUninstallerWindow = new L(
            "微軟自己的卸載畫面可能會跳出來，請在上面確認。",
            "微软自己的卸载界面可能会弹出，请在上面确认。",
            "Microsoft's own uninstaller may appear. Confirm it there.");
        public static readonly L AppRemovedAt = new L("{0} 卸載", "{0} 卸载", "Uninstalled {0}");

        // -- restore ----------------------------------------------------------
        public static readonly L RestoreTitle = new L("還原 Windows 原本的設定", "还原 Windows 原本的设置", "Restore Windows' own settings");
        public static readonly L RestoreLead = new L(
            "把策略檔換回備份、地區改回原值、移除排程。備份會留著。已卸載的 App 不會自己裝回來。",
            "把策略文件换回备份、区域改回原值、移除计划任务。备份会留着。已卸载的 App 不会自己装回来。",
            "Puts the backed-up policy file back, returns the region to its old value and removes the scheduled task. Backups are kept. Uninstalled apps do not come back by themselves.");
        public static readonly L RestoreButton = new L("還原", "还原", "Restore");
        public static readonly L PurgeTitle = new L("完全移除", "完全移除", "Remove everything");
        public static readonly L PurgeLead = new L(
            "先還原，再刪掉 {0}（含備份與日誌）。此動作無法復原。",
            "先还原，再删除 {0}（含备份与日志）。此操作无法撤销。",
            "Restores first, then deletes {0}, backups and logs included. This cannot be undone.");
        public static readonly L PurgeButton = new L("還原並刪除全部資料", "还原并删除全部数据", "Restore and delete all data");
        public static readonly L PurgeConfirm = new L(
            "確定要刪掉 {0} 嗎？備份一旦刪除就無法再還原。",
            "确定要删除 {0} 吗？备份一旦删除就无法再还原。",
            "Delete {0}? Once the backups are gone, restoring is no longer possible.");
        public static readonly L Cancel = new L("取消", "取消", "Cancel");
        public static readonly L Confirm = new L("確定", "确定", "Confirm");
        public static readonly L Close = new L("關閉", "关闭", "Close");
        public static readonly L Minimise = new L("最小化", "最小化", "Minimise");
        public static readonly L Maximise = new L("最大化", "最大化", "Maximise");
        public static readonly L ThemeFollowSystem = new L("跟隨系統", "跟随系统", "Match Windows");

        // -- verification -----------------------------------------------------
        public static readonly L VerifyTitle = new L("驗證結果", "验证结果", "Verification");
        public static readonly L VerifyPass = new L("已生效", "已生效", "In effect");
        public static readonly L VerifyPending = new L("尚未生效", "尚未生效", "Not yet in effect");
        public static readonly L VerifyUnknown = new L("無法判斷", "无法判断", "Cannot tell");
        public static readonly L VerifyRegion = new L("裝置地區已寫入", "设备区域已写入", "Device region written");
        public static readonly L VerifyPolicy = new L("策略檔已改寫", "策略文件已改写", "Policy file rewritten");
        public static readonly L VerifyTask = new L("排程可在更新後補回", "计划任务可在更新后补回", "Scheduled task will re-apply after updates");
        public static readonly L VerifyEdge = new L("Edge 可卸載", "Edge 可卸载", "Edge is uninstallable");
        public static readonly L VerifyStore = new L("Microsoft Store 可卸載", "Microsoft Store 可卸载", "Microsoft Store is uninstallable");
        public static readonly L VerifyWebSearch = new L("可關閉網頁搜尋的開關", "可关闭网页搜索的开关", "Web search can be turned off");

        public static readonly L DiagRestart = new L(
            "重新開機後再看一次。",
            "重启后再看一次。",
            "Check again after a restart.");
        public static readonly L DiagRollout = new L(
            "微軟分批推送這項功能，這台機器可能還沒輪到。設定已經寫好了。",
            "微软分批推送这项功能，这台机器可能还没轮到。设置已经写好了。",
            "Microsoft rolls this out in waves and may not have reached this machine yet. The setting itself is in place.");
        public static readonly L DiagManaged = new L(
            "這台機器由網域或 MDM 管理，公司原則會蓋掉我們寫的值。本工具只回報，不繞過管理原則。",
            "这台机器由域或 MDM 管理，公司策略会覆盖我们写的值。本工具只报告，不绕过管理策略。",
            "This machine is managed by a domain or MDM, whose policy overrides what we write. This tool reports that and does not work around management policy.");
        public static readonly L DiagUcpd = new L(
            "寫入被 UserChoice 保護驅動程式阻擋，已改用備援方式重試。",
            "写入被 UserChoice 保护驱动程序阻挡，已改用备用方式重试。",
            "The write was blocked by the UserChoice Protection Driver; the fallback path was used instead.");
        public static readonly L DiagNotApplied = new L(
            "還沒套用過。按上面那顆按鈕。",
            "还没应用过。按上面那颗按钮。",
            "Nothing has been applied yet. Use the button above.");

        // -- results ----------------------------------------------------------
        public static readonly L DoneApplied = new L("完成", "完成", "Done");
        public static readonly L DoneRestored = new L("已還原", "已还原", "Restored");
        public static readonly L DonePurged = new L("已完全移除", "已完全移除", "Everything removed");
        public static readonly L Failed = new L("失敗", "失败", "Failed");
        public static readonly L NeedAdmin = new L(
            "需要系統管理員權限才能修改系統設定。",
            "需要管理员权限才能修改系统设置。",
            "Changing system settings needs administrator rights.");
        public static readonly L NoBackup = new L(
            "找不到備份，無法還原。",
            "找不到备份，无法还原。",
            "No backup found, so there is nothing to restore.");
        public static readonly L OpenLog = new L("開啟日誌", "打开日志", "Open log");
        public static readonly L OpenFolder = new L("開啟資料夾", "打开文件夹", "Open folder");
        public static readonly L CheckUpdate = new L("檢查更新", "检查更新", "Check for updates");
        public static readonly L UpdateLatest = new L("已是最新版", "已是最新版", "Up to date");
        public static readonly L UpdateAvailable = new L("有新版本 {0}", "有新版本 {0}", "Version {0} is available");
        public static readonly L UpdateFailed = new L("查不到更新資訊", "查不到更新信息", "Could not reach the update info");
        public static readonly L NoNetworkByDefault = new L(
            "本工具平常不連網，只有按下這顆按鈕才會查 GitHub。",
            "本工具平常不联网，只有按下这颗按钮才会查 GitHub。",
            "This tool never goes online unless you press this button, which checks GitHub.");
    }
}
