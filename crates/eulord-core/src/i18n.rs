use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum Lang {
    ZhHant,
    ZhHans,
    #[default]
    En,
}

impl Lang {
    pub fn code(self) -> &'static str {
        match self {
            Self::ZhHant => "zh-Hant",
            Self::ZhHans => "zh-Hans",
            Self::En => "en",
        }
    }

    pub fn short(self) -> &'static str {
        match self {
            Self::ZhHant => "繁中",
            Self::ZhHans => "简中",
            Self::En => "EN",
        }
    }

    pub fn from_locale(locale: &str) -> Self {
        let lower = locale.to_ascii_lowercase();
        if lower.starts_with("zh-tw")
            || lower.starts_with("zh-hk")
            || lower.starts_with("zh-mo")
            || lower.starts_with("zh-hant")
        {
            Self::ZhHant
        } else if lower.starts_with("zh") {
            Self::ZhHans
        } else {
            Self::En
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum LangPref {
    #[default]
    #[serde(rename = "auto")]
    Auto,
    #[serde(rename = "zh-Hant")]
    ZhHant,
    #[serde(rename = "zh-Hans")]
    ZhHans,
    #[serde(rename = "en")]
    En,
}

impl LangPref {
    pub fn resolve(self, locale: &str) -> Lang {
        match self {
            Self::Auto => Lang::from_locale(locale),
            Self::ZhHant => Lang::ZhHant,
            Self::ZhHans => Lang::ZhHans,
            Self::En => Lang::En,
        }
    }

    pub fn label(self) -> &'static str {
        match self {
            Self::Auto => "AUTO",
            Self::ZhHant => "繁中",
            Self::ZhHans => "简中",
            Self::En => "EN",
        }
    }
}

const TABLE: &[(&str, &str, &str, &str)] = &[
    (
        "app_name",
        "一鍵轉生歐爺",
        "一键转生欧爷",
        "One-Click EU Lord",
    ),
    (
        "tagline",
        "EU / EEA 政策超集控制台",
        "EU / EEA 政策超集控制台",
        "EU / EEA policy superset console",
    ),
    ("nav_overview", "總覽", "总览", "OVERVIEW"),
    ("nav_policies", "政策矩陣", "政策矩阵", "POLICY MATRIX"),
    ("nav_unlocks", "EU 解鎖", "EU 解锁", "EU UNLOCKS"),
    ("nav_maintenance", "維護", "维护", "MAINTENANCE"),
    ("nav_about", "關於", "关于", "ABOUT"),
    (
        "status_not_configured",
        "尚未設定",
        "尚未配置",
        "NOT CONFIGURED",
    ),
    ("status_active", "已生效", "已生效", "ACTIVE"),
    ("status_partial", "部分生效", "部分生效", "PARTIAL"),
    ("status_unknown", "無法判斷", "无法判断", "UNKNOWN"),
    ("status_ready", "系統就緒", "系统就绪", "SYSTEM READY"),
    ("status_region", "地區", "地区", "REGION"),
    ("status_policy", "政策檔", "政策文件", "POLICY FILE"),
    ("status_persistence", "持久化", "持久化", "PERSISTENCE"),
    ("status_integrity", "完整性", "完整性", "INTEGRITY"),
    ("status_admin", "管理員", "管理员", "ADMIN"),
    ("status_standard", "標準權限", "标准权限", "STANDARD"),
    ("status_elevated", "已提權", "已提权", "ELEVATED"),
    (
        "status_task_ready",
        "排程就緒",
        "计划任务就绪",
        "TASK READY",
    ),
    ("status_task_absent", "未安裝", "未安装", "NOT INSTALLED"),
    ("status_task_disabled", "已停用", "已停用", "DISABLED"),
    ("status_task_stale", "需重建", "需重建", "STALE"),
    ("status_task_unknown", "無法讀取", "无法读取", "UNKNOWN"),
    (
        "status_original",
        "Windows 原檔",
        "Windows 原文件",
        "WINDOWS ORIGINAL",
    ),
    ("status_ours", "已改寫", "已改写", "REWRITTEN"),
    ("status_unreadable", "無法讀取", "无法读取", "UNREADABLE"),
    ("status_missing", "檔案缺失", "文件缺失", "MISSING"),
    ("apply_superset", "套用超集", "应用超集", "APPLY SUPERSET"),
    ("reapply", "重新套用", "重新应用", "REAPPLY"),
    ("review_changes", "檢視變更", "查看变更", "REVIEW CHANGES"),
    ("confirm_apply", "確認套用", "确认应用", "CONFIRM APPLY"),
    ("cancel", "取消", "取消", "CANCEL"),
    ("close", "關閉", "关闭", "CLOSE"),
    ("continue", "繼續", "继续", "CONTINUE"),
    ("back", "返回", "返回", "BACK"),
    (
        "apply_intro",
        "這會把歐盟／EEA 的增益寫入系統，並保留歐盟版拿掉的功能。",
        "这会把欧盟／EEA 的增益写入系统，并保留欧盟版拿掉的功能。",
        "This writes the EU/EEA gains into Windows and keeps the features the EU build removes.",
    ),
    (
        "apply_no_uninstall",
        "本工具不安裝或解除安裝任何軟件；只調整政策與系統設定。",
        "本工具不安装或卸载任何软件；只调整政策与系统设置。",
        "This tool never installs or uninstalls software. It only changes policy and system settings.",
    ),
    (
        "review_title",
        "套用前確認",
        "应用前确认",
        "PRE-FLIGHT REVIEW",
    ),
    (
        "review_body",
        "以下是這次會寫入的變更。未知政策會保持 Windows 原樣。",
        "以下是这次会写入的变更。未知政策会保持 Windows 原样。",
        "These are the changes that will be written. Unknown policies stay exactly as Windows shipped them.",
    ),
    ("review_gains", "套用增益", "应用增益", "GAINS TAKEN"),
    ("review_kept", "保留原功能", "保留原功能", "FEATURES KEPT"),
    ("review_unknown", "未知政策", "未知政策", "UNKNOWN POLICIES"),
    ("review_changed", "實際變更", "实际变更", "FILES CHANGED"),
    (
        "review_restart",
        "部分功能需要重新啟動或登入後才會出現。",
        "部分功能需要重新启动或登录后才会出现。",
        "Some features only appear after a restart or sign-in.",
    ),
    ("applying", "套用中…", "应用中…", "APPLYING…"),
    ("apply_done", "套用完成", "应用完成", "APPLY COMPLETE"),
    (
        "apply_partial",
        "部分完成",
        "部分完成",
        "PARTIALLY COMPLETE",
    ),
    ("apply_failed", "套用失敗", "应用失败", "APPLY FAILED"),
    ("apply_cancelled", "已取消", "已取消", "CANCELLED"),
    ("apply_idle", "待命", "待命", "STANDBY"),
    ("step_backup", "建立備份", "建立备份", "BACKUP"),
    ("step_policy", "寫入政策檔", "写入政策文件", "POLICY WRITE"),
    ("step_region", "設定地區", "设置地区", "REGION"),
    ("step_task", "安裝持久化", "安装持久化", "PERSISTENCE"),
    ("step_verify", "驗證", "验证", "VERIFY"),
    ("step_rollback", "回滾", "回滚", "ROLLBACK"),
    ("benefits", "增益", "增益", "GAINS"),
    ("losses", "保留", "保留", "KEPT"),
    ("choices", "見仁見智", "见仁见智", "TASTE"),
    ("class_benefit", "增益", "增益", "Gain"),
    ("class_loss", "保留", "保留", "Keep"),
    ("class_choice", "見仁見智", "见仁见智", "Taste"),
    ("unclassified", "未分類", "未分类", "UNCLASSIFIED"),
    ("selected", "已選", "已选", "SELECTED"),
    ("auto", "自動", "自动", "AUTO"),
    ("eea", "EU 版", "EU 版", "EU"),
    ("global", "全球版", "全球版", "GLOBAL"),
    ("current", "目前", "当前", "CURRENT"),
    ("target", "目標", "目标", "TARGET"),
    ("source", "來源", "来源", "SOURCE"),
    ("changed", "已變更", "已变更", "CHANGED"),
    ("unchanged", "未變更", "未变更", "UNCHANGED"),
    (
        "search_policies",
        "搜尋政策…",
        "搜索政策…",
        "SEARCH POLICIES…",
    ),
    ("filter_all", "全部", "全部", "ALL"),
    ("filter_benefits", "增益", "增益", "GAINS"),
    ("filter_losses", "保留", "保留", "KEPT"),
    ("filter_choices", "見仁見智", "见仁见智", "TASTE"),
    ("expert_mode", "專家模式", "专家模式", "EXPERT MODE"),
    ("simple_mode", "簡單模式", "简单模式", "SIMPLE MODE"),
    (
        "expert_hint",
        "專家模式可逐條覆寫 68 條政策；一般使用不需要打開。",
        "专家模式可逐条覆写 68 条政策；一般使用不需要打开。",
        "Expert mode lets you override all 68 policies. Most people never need it.",
    ),
    (
        "simple_hint",
        "預設只顯示需要你決定的項目；增益與保留由超集規則自動處理。",
        "默认只显示需要你决定的项目；增益与保留由超集规则自动处理。",
        "Only decisions that need you are shown. Gains and keeps are handled by the superset rule.",
    ),
    (
        "policy_matrix_hint",
        "增益會採用 EU 狀態；保留會維持全球狀態；見仁見智預設不變。",
        "增益会采用 EU 状态；保留会维持全球状态；见仁见智默认不变。",
        "Gains take the EU state; keeps retain the global state; taste stays unchanged by default.",
    ),
    ("unlocks_title", "EU 解鎖清單", "EU 解锁清单", "EU UNLOCKS"),
    (
        "unlocks_body",
        "這些是歐盟／EEA 政策解鎖的能力。軟件解除安裝不在本工具範圍內；需要時請到 Windows「設定 → 應用程式」。",
        "这些是欧盟／EEA 政策解锁的能力。软件卸载不在本工具范围内；需要时请到 Windows“设置 → 应用”。",
        "These are capabilities unlocked by EU/EEA policy. Software uninstall is out of scope; use Windows Settings → Apps when needed.",
    ),
    (
        "unlocks_software",
        "可解除安裝（由 Windows 設定處理）",
        "可卸载（由 Windows 设置处理）",
        "UNINSTALLABLE VIA WINDOWS SETTINGS",
    ),
    (
        "unlocks_feature",
        "可關閉或限制的系統行為",
        "可关闭或限制的系统行为",
        "SYSTEM BEHAVIOURS THAT CAN BE DISABLED OR LIMITED",
    ),
    (
        "open_windows_apps",
        "開啟 Windows 應用程式設定",
        "打开 Windows 应用设置",
        "OPEN WINDOWS APPS SETTINGS",
    ),
    (
        "maintenance_title",
        "維護與復原",
        "维护与恢复",
        "MAINTENANCE & RECOVERY",
    ),
    ("backup_status", "備份狀態", "备份状态", "BACKUP STATUS"),
    ("backup_none", "尚無備份", "尚无备份", "NO BACKUP"),
    ("backup_ready", "備份可用", "备份可用", "BACKUP READY"),
    ("restore", "還原", "还原", "RESTORE"),
    (
        "restore_confirm",
        "要還原政策檔、地區與排程嗎？備份會保留。",
        "要还原政策文件、地区与计划任务吗？备份会保留。",
        "Restore the policy file, region and scheduled task? Backups are kept.",
    ),
    ("purge", "完全移除", "完全移除", "PURGE"),
    (
        "purge_confirm",
        "這會刪除本工具的所有資料與備份。輸入 PURGE 以確認。",
        "这会删除本工具的所有数据与备份。输入 PURGE 以确认。",
        "This deletes all tool data and backups. Type PURGE to confirm.",
    ),
    ("verify", "驗證", "验证", "VERIFY"),
    ("export", "匯出設定", "导出设置", "EXPORT"),
    ("import", "匯入設定", "导入设置", "IMPORT"),
    ("open_logs", "開啟日誌", "打开日志", "OPEN LOGS"),
    ("open_folder", "開啟資料夾", "打开文件夹", "OPEN FOLDER"),
    ("check_updates", "檢查更新", "检查更新", "CHECK UPDATES"),
    (
        "update_latest",
        "已是最新版本",
        "已是最新版本",
        "UP TO DATE",
    ),
    (
        "update_available",
        "有新版本可用",
        "有新版本可用",
        "UPDATE AVAILABLE",
    ),
    (
        "update_failed",
        "無法檢查更新",
        "无法检查更新",
        "UPDATE CHECK FAILED",
    ),
    ("compatibility", "相容性", "兼容性", "COMPATIBILITY"),
    (
        "compat_tested",
        "已驗證的 Windows build",
        "已验证的 Windows build",
        "TESTED WINDOWS BUILD",
    ),
    (
        "compat_known",
        "已知但未完整驗證的 build",
        "已知但未完整验证的 build",
        "KNOWN BUT NOT FULLY TESTED",
    ),
    (
        "compat_unknown",
        "未知 build；未知政策會保持原樣",
        "未知 build；未知政策会保持原样",
        "UNKNOWN BUILD; UNKNOWN POLICIES ARE LEFT ALONE",
    ),
    (
        "compat_unsupported",
        "不支援此 Windows 版本",
        "不支持此 Windows 版本",
        "UNSUPPORTED WINDOWS VERSION",
    ),
    (
        "about_title",
        "關於這個版本",
        "关于这个版本",
        "ABOUT THIS BUILD",
    ),
    (
        "about_body",
        "v3 是純 Rust 重寫。介面是復古未來主義 TUI 風格的 GUI；系統操作直接呼叫 Windows API。",
        "v3 是纯 Rust 重写。界面是复古未来主义 TUI 风格的 GUI；系统操作直接调用 Windows API。",
        "v3 is a pure-Rust rewrite. The UI is a retro-futurist TUI-styled GUI; system operations call Windows APIs directly.",
    ),
    (
        "about_license",
        "MIT 授權 · 與 Microsoft 無關",
        "MIT 授权 · 与 Microsoft 无关",
        "MIT licensed · not affiliated with Microsoft",
    ),
    (
        "about_repo",
        "開啟 GitHub 專案",
        "打开 GitHub 项目",
        "OPEN GITHUB REPOSITORY",
    ),
    (
        "first_run_title",
        "歡迎，歐爺。",
        "欢迎，欧爷。",
        "WELCOME, EU LORD.",
    ),
    (
        "first_run_body",
        "先掃描這台機器，確認 Windows build、政策檔與相容性。",
        "先扫描这台机器，确认 Windows build、政策文件与兼容性。",
        "First scan this machine for the Windows build, policy file and compatibility.",
    ),
    ("first_run_scan", "開始掃描", "开始扫描", "BEGIN SCAN"),
    ("first_run_skip", "直接進入", "直接进入", "SKIP"),
    (
        "admin_required",
        "這個動作需要管理員權限；系統會顯示 UAC 提示。",
        "这个动作需要管理员权限；系统会显示 UAC 提示。",
        "This action needs administrator rights. Windows will show a UAC prompt.",
    ),
    (
        "not_admin_hint",
        "檢視狀態不需要管理員；套用、還原與修復才需要提權。",
        "查看状态不需要管理员；应用、还原与修复才需要提权。",
        "Viewing status does not need admin. Apply, restore and repair do.",
    ),
    ("error", "錯誤", "错误", "ERROR"),
    ("warning", "警告", "警告", "WARNING"),
    ("ok", "確定", "确定", "OK"),
    ("yes", "是", "是", "YES"),
    ("no", "否", "否", "NO"),
    ("language", "語言", "语言", "LANGUAGE"),
    ("crt_effect", "CRT 掃描線", "CRT 扫描线", "CRT SCANLINES"),
    ("theme", "主題", "主题", "THEME"),
    (
        "footer_hint",
        "滑鼠可操作 · Esc 取消或關閉 · CRT 掃描線可在「關於」切換",
        "鼠标可操作 · Esc 取消或关闭 · CRT 扫描线可在“关于”切换",
        "Mouse-ready · Esc cancels/closes · CRT toggle in About",
    ),
    ("not_available", "不適用", "不适用", "N/A"),
    ("unknown", "未知", "未知", "UNKNOWN"),
    ("none", "無", "无", "NONE"),
    ("of", "／", "／", "OF"),
    ("version", "版本", "版本", "VERSION"),
    ("build", "組建", "构建", "BUILD"),
    ("applied_at", "套用時間", "应用时间", "APPLIED"),
    ("never", "從未", "从未", "NEVER"),
    ("loading", "讀取中…", "读取中…", "LOADING…"),
    ("retry", "重試", "重试", "RETRY"),
    ("details", "詳情", "详情", "DETAILS"),
    (
        "apply_requires_review",
        "請先檢視變更。",
        "请先查看变更。",
        "Review the changes first.",
    ),
    (
        "plan_failed",
        "無法建立計畫；請查看日誌。",
        "无法建立计划；请查看日志。",
        "Could not build a plan. Check the logs.",
    ),
    (
        "unsupported_apply",
        "此 Windows build 尚未驗證；仍可檢視與匯出，但預設不寫入。",
        "此 Windows build 尚未验证；仍可查看与导出，但默认不写入。",
        "This Windows build is untested. Viewing and exporting work, but writing is disabled by default.",
    ),
    ("proceed_anyway", "仍要繼續", "仍要继续", "PROCEED ANYWAY"),
    (
        "no_changes",
        "目前沒有任何需要寫入的變更。",
        "当前没有任何需要写入的变更。",
        "There are no changes to write.",
    ),
    (
        "unknown_policies_notice",
        "有未知政策；它們會保持 Windows 原樣。",
        "有未知政策；它们会保持 Windows 原样。",
        "Unknown policies are present and will be left exactly as Windows shipped them.",
    ),
    (
        "software_uninstall_note",
        "軟件解除安裝已刻意移除；這裡只顯示政策解鎖資訊。",
        "软件卸载已刻意移除；这里只显示政策解锁信息。",
        "Software uninstall is intentionally not implemented. This page only shows policy unlock information.",
    ),
];

pub fn tr(lang: Lang, key: &'static str) -> &'static str {
    for (candidate, tw, cn, en) in TABLE {
        if *candidate == key {
            return match lang {
                Lang::ZhHant => tw,
                Lang::ZhHans => cn,
                Lang::En => en,
            };
        }
    }
    key
}
