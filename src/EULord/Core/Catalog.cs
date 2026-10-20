// The heart of the superset principle.
//
// Every policy whose Ireland (EU+EEA) state differs from the global default is
// classified once, by hand:
//
//   Benefit  the EEA state is a gain          -> the superset takes the EEA state
//   Loss     the EEA state removes something  -> the superset keeps the global state
//   Choice   a matter of taste, or Microsoft  -> the superset keeps the global state
//            never documented what it does       unless the user asks for the EEA one
//
// v1 got this wrong: it copied the EEA set verbatim and so silently accepted the
// EEA build's losses too. Classification is what makes v2 a superset rather than
// a clone.
//
// Descriptions say what the policy does when ENABLED, not what we set it to.
// Ids are a stable contract: state.json and the user's per-policy overrides key
// off them, so an Id must never be renamed once released.
//
// Verified against C:\Windows\System32\IntegratedServicesRegionPolicySet.json on
// Windows 11 build 26300: 84 entries, 79 unique GUIDs, 72 of which list IE, and
// exactly these 68 differ from their default. The engine always reads the real EEA
// state from the live file -- it is never baked in here.

using System;
using System.Collections.Generic;
using C = EULord.Core.PolicyClass;

namespace EULord.Core
{
    internal enum PolicyClass { Benefit, Loss, Choice }

    internal sealed class CatalogEntry
    {
        public readonly string Guid;
        public readonly string Id;
        public readonly PolicyClass Class;
        private readonly string _tw, _cn, _en;

        public CatalogEntry(string guid, string id, PolicyClass cls, string tw, string cn, string en)
        {
            Guid = guid; Id = id; Class = cls; _tw = tw; _cn = cn; _en = en;
        }

        /// <summary>What this policy does when enabled, in the requested language.</summary>
        public string Describe(Lang lang)
        {
            switch (lang)
            {
                case Lang.ZhHant: return _tw;
                case Lang.ZhHans: return _cn;
                default: return _en;
            }
        }
    }

    internal static class Catalog
    {
        private static CatalogEntry E(string guid, string id, PolicyClass cls, string tw, string cn, string en)
        {
            return new CatalogEntry(guid, id, cls, tw, cn, en);
        }

        private static readonly CatalogEntry[] _all =
        {
            E("{1bca278a-5d11-4acf-ad2f-f9ab6d7f93a6}", "EdgeUninstall", C.Benefit, "可以解除安裝 Edge", "可以卸载 Edge", "Edge can be uninstalled"),
            E("{6002ce31-b807-4f82-820c-2b92e716ab76}", "WebSearchOff", C.Benefit, "可以關閉搜尋中的網頁結果", "可以关闭搜索中的网页结果", "Web results in Search can be turned off"),
            E("{6033b294-79ff-47eb-bbde-97b4c6479217}", "NarratorScripts", C.Loss, "朗讀程式的腳本擴充", "讲述人的脚本扩展", "Narrator scripting extensions"),
            E("{ad2d9176-ef16-42de-ba5e-5f6e5a07abca}", "ExplorerMsSearch", C.Choice, "檔案總管使用微軟自家的搜尋", "文件资源管理器使用微软自家的搜索", "Microsoft's own search in File Explorer"),
            E("{8e779eeb-377c-4373-9e80-56e164fdca7e}", "DefaultAppsPlus", C.Benefit, "設預設瀏覽器時一併接管更多類型、一鍵設定 PDF、釘選", "设默认浏览器时一并接管更多类型、一键设置 PDF、固定", "Setting a default browser covers more types, one-click PDF, pinning"),
            E("{f2775604-739c-4cd2-8463-eb040f7e8bf7}", "ThirdPartySearch", C.Benefit, "第三方搜尋提供者可出現在搜尋中", "第三方搜索提供商可出现在搜索中", "Third-party search providers appear in Search"),
            E("{e462341d-48bf-4a7f-9585-5a4e5b90a079}", "ThirdPartyHighlights", C.Benefit, "搜尋首頁可顯示第三方的精選內容", "搜索首页可显示第三方的精选内容", "Third-party highlights on the Search start page"),
            E("{7de951fb-a68f-4cb0-b67b-ae6fbdec83ed}", "ThirdPartyGleam", C.Benefit, "第三方可自訂搜尋框圖示", "第三方可自定义搜索框图标", "Third parties can customize the search box icon"),
            E("{61bf5046-c5db-4cd3-b6bf-929e5b421a6a}", "MsGleam", C.Benefit, "搜尋框顯示微軟的推廣圖示", "搜索框显示微软的推广图标", "Microsoft's promotional icons in the search box"),
            E("{cde299a6-4bb6-4623-ae47-cbf055b70873}", "XboxFullScreen", C.Loss, "Xbox 全螢幕體驗", "Xbox 全屏体验", "Xbox full-screen experience"),
            E("{407a6a2a-4c34-4945-bd24-cd194635abae}", "ThirdPartyRecent", C.Benefit, "最近搜尋中顯示第三方提供者", "最近搜索中显示第三方提供商", "Third-party providers in recent searches"),
            E("{5436f3c3-803e-447d-9bd2-b533888df09f}", "WebSearchNeedsEdge", C.Choice, "網頁搜尋需要 Edge（微軟未公開細節）", "网页搜索需要 Edge（微软未公开细节）", "Web search requires Edge (not documented)"),
            E("{b162c44a-5d51-4fb4-aadd-6b8bf3ea4648}", "SystemComponents", C.Benefit, "系統元件與一般 App 分開列出", "系统组件与普通应用分开列出", "System components listed separately from apps"),
            E("{4492387d-5ea5-4e72-9ca0-69067d19502c}", "BackupRestricted", C.Choice, "限制 Windows 備份的選項", "限制 Windows 备份的选项", "Restricts Windows Backup options"),
            E("{c053478a-fd9a-4bbf-a794-4ebef4bbf764}", "OfficeRecent", C.Choice, "推薦清單顯示 Office 最近的檔案", "推荐列表显示 Office 最近的文件", "Office recent files in recommendations"),
            E("{16d2b50e-fa7c-4bb1-ab17-01d766530b3b}", "WidgetsThirdPartyFeed", C.Benefit, "小工具可使用第三方資訊流", "小组件可使用第三方信息流", "Third-party feeds in Widgets"),
            E("{93f5099b-feeb-42c1-984b-c34851e100aa}", "WidgetsThirdPartyInFeed", C.Loss, "第三方小工具出現在小工具資訊流中", "第三方小组件出现在小组件信息流中", "Third-party widgets in the Widgets feed"),
            E("{92ca7dcf-f422-4a4b-892e-7883a3ca8d70}", "WidgetsFeedOff", C.Benefit, "可以關掉微軟的小工具資訊流", "可以关掉微软的小组件信息流", "Microsoft's Widgets feed can be turned off"),
            E("{3d0c862c-bfd8-4350-af2f-4c42884cc3a8}", "WidgetsDefaultBrowser", C.Benefit, "小工具的連結用預設瀏覽器開啟", "小组件的链接用默认浏览器打开", "Widget links open in the default browser"),
            E("{0dcb52b1-6b3f-4e95-8049-bf2281ae2eda}", "WidgetsDataLimit", C.Benefit, "限制小工具的資料分享", "限制小组件的数据共享", "Limits Widgets data sharing"),
            E("{4323bb73-d394-4c3a-b9df-224ab359844f}", "WidgetsThirdPartyDataLimit", C.Benefit, "限制第三方小工具的資料分享", "限制第三方小组件的数据共享", "Limits third-party Widgets data sharing"),
            E("{dd5d7e7c-3ad7-4e38-b11b-a566f1510d00}", "TelemetryRestricted", C.Benefit, "診斷事件標記為受限", "诊断事件标记为受限", "Diagnostic events tagged as restricted"),
            E("{cd69e9e4-3778-4418-ba1d-bf0116eeeebe}", "XboxPerfData", C.Choice, "分享資料給 Xbox「效能適配」功能", "向 Xbox“性能适配”功能共享数据", "Data sharing for Xbox \"performance fit\""),
            E("{1f5403a8-5d44-40b9-a002-dda7ce7b0d01}", "SharedDiagConsent", C.Benefit, "選用診斷資料的同意在各處共用", "可选诊断数据的同意在各处共用", "Optional diagnostic data consent is shared"),
            E("{4ac54d32-0799-405f-9bf4-1fe094cd859c}", "Copilot", C.Choice, "Windows Copilot", "Windows Copilot", "Windows Copilot"),
            E("{1d290cdb-499c-4d42-938a-9b8dceffe998}", "AutoAppSignIn", C.Choice, "App 自動登入", "应用自动登录", "Automatic app sign-in"),
            E("{37bae007-d2ac-41e8-b1bb-848f04e7858f}", "WidgetsTaskbarNotify", C.Benefit, "工作列上的小工具通知", "任务栏上的小组件通知", "Widgets notifications on the taskbar"),
            E("{3ffae667-623e-408e-bb30-9f670707c346}", "RecentWebSearches", C.Benefit, "搜尋首頁顯示最近的網頁搜尋", "搜索首页显示最近的网页搜索", "Recent web searches on the Search start page"),
            E("{1ee2eff2-27f6-4103-b593-6e7d656a54df}", "StartWebsites", C.Benefit, "「開始」推薦中顯示網站", "“开始”推荐中显示网站", "Websites in Start recommendations"),
            E("{1b4085de-aab1-43f6-9385-c5774b824be9}", "WidgetsDefaultAccount", C.Benefit, "小工具只能用預設帳號登入", "小组件只能用默认账户登录", "Widgets sign-in limited to the default account"),
            E("{0446522c-1359-4a68-9e11-e7e46459b2e7}", "WidgetsBadges", C.Benefit, "工作列上的第三方小工具徽章", "任务栏上的第三方小组件徽章", "Third-party Widgets badges on the taskbar"),
            E("{263054fb-c39e-41d5-b712-23f340806ca8}", "WidgetsStatic", C.Benefit, "小工具只顯示固定推薦，不做個人化", "小组件只显示固定推荐，不做个性化", "Widgets limited to static, non-personalized recommendations"),
            E("{9f39df42-5d54-46fc-aa50-9ae3b1ecd663}", "WidgetsMsFrame", C.Choice, "微軟小工具的最佳化外框與工作列整合", "微软小组件的优化外框与任务栏整合", "Optimized frame and taskbar integration for Microsoft widgets"),
            E("{3b6022db-defd-4b4a-b6cf-6004dd657f3c}", "NewsSettings", C.Loss, "顯示「新聞與興趣」的設定", "显示“资讯和兴趣”的设置", "News & Interests settings are shown"),
            E("{b5113273-5a79-4488-a7b4-0a4fc5d5b194}", "SetupPromoFull", C.Benefit, "設定流程中的全螢幕推銷頁", "设置流程中的全屏推销页", "Full-screen promotions during setup"),
            E("{75b09d11-2e0d-4029-bd88-b91ec9a229bb}", "SetupPromoPages", C.Benefit, "設定流程中的推銷頁面", "设置流程中的推销页面", "Promotional pages inside setup flows"),
            E("{874ed11b-2ab7-4f1e-b275-6032092e3dda}", "BrowserProfileIds", C.Benefit, "微軟 App 可讀取瀏覽器的個人檔案 ID", "微软应用可读取浏览器的个人资料 ID", "Microsoft apps can read browser profile IDs"),
            E("{2bf706de-6dbb-4692-b7ef-84d80c47e927}", "EdgeCampaigns", C.Benefit, "行銷活動推廣 Edge、改用 Edge 開啟", "营销活动推广 Edge、改用 Edge 打开", "Campaigns promote Edge and open it instead of your default browser"),
            E("{36996754-e327-483a-902f-523e2ba03239}", "CampaignTargeting", C.Benefit, "行銷活動的分群鎖定", "营销活动的分群定向", "Campaign segment targeting"),
            E("{f7281e17-d7eb-4224-89d0-8aa12a403410}", "WidgetsLockStatus", C.Choice, "鎖定畫面以狀態方式顯示小工具", "锁屏以状态方式显示小组件", "Widgets as a status app on the lock screen"),
            E("{0d8903d8-84f5-4048-af3f-547476d83c3c}", "ThirdPartyUpdates", C.Benefit, "第三方 App 可用系統的加速更新管道", "第三方应用可用系统的加速更新渠道", "Third-party apps can use the expedited update channel"),
            E("{a9aacd21-327a-4747-adc1-9d8f1d0f6704}", "CopilotProviders", C.Benefit, "Copilot 聊天可接第三方提供者", "Copilot 聊天可接入第三方提供商", "Third-party chat providers for Copilot"),
            E("{8fae6629-123f-46dd-8c34-042f654b554e}", "CopilotScreenContext", C.Choice, "Copilot 讀取前景視窗的文字當作上下文", "Copilot 读取前台窗口的文字作为上下文", "Copilot reads the foreground window (OCR) for context"),
            E("{b59b2b22-db72-4e2b-867a-4bfc53290abb}", "PersonalizedOffers", C.Choice, "以「個人化優惠」取代「量身打造的體驗」", "以“个性化优惠”取代“量身定制的体验”", "Replace \"tailored experiences\" with \"personalized offers\""),
            E("{fc5f578d-597b-47ee-8e0e-051cf79fb2c5}", "CopilotAutoPin", C.Benefit, "系統更新時自動把 Copilot 釘到工作列", "系统更新时自动把 Copilot 固定到任务栏", "Copilot pinned automatically during updates"),
            E("{139e6c8f-e543-4dfe-803f-2fdd26b31477}", "ClockCalendarActions", C.Loss, "時鐘與行事曆的快速動作", "时钟与日历的快速操作", "Clock and Calendar action sources"),
            E("{e32f3cd4-da4c-4c60-a302-05deb33c5254}", "OneDriveOfferLimit", C.Benefit, "限制 OneDrive 資料夾備份推銷的頻率", "限制 OneDrive 文件夹备份推销的频率", "Rate-limits the OneDrive folder-backup offer"),
            E("{d935674a-c417-4804-8b01-48d746af7a9e}", "A9Component", C.Loss, "「A9」選用元件（微軟未公開細節）", "“A9”可选组件（微软未公开细节）", "\"A9\" optional component (not documented)"),
            E("{e2961891-e5ef-436a-b009-7801281c3fbf}", "OneDriveResume", C.Choice, "OneDrive 接續工作", "OneDrive 继续工作", "OneDrive Resume"),
            E("{c8e6c136-c6d1-419c-919a-4f8935662914}", "AccountSyncConsent", C.Benefit, "帳號同步前先徵求同意", "账户同步前先征求同意", "Asks for consent before account sync"),
            E("{608ba944-62eb-4912-a873-127c8a68b98c}", "WidgetsOnLock", C.Choice, "新版鎖定畫面小工具", "新版锁屏小组件", "New lock screen widgets"),
            E("{c74bb959-f763-4b9b-9b14-6b312df2c937}", "StartExperiencesUninstall", C.Benefit, "可以解除安裝 StartExperiencesApp", "可以卸载 StartExperiencesApp", "StartExperiencesApp can be uninstalled"),
            E("{9679c1a3-b7b3-4789-87ea-5578f78fe900}", "PhoneLinkStart", C.Choice, "「開始」裡的手機連結", "“开始”里的手机连接", "Phone Link companion in Start"),
            E("{dbfadcb8-0302-4de0-87be-4dfbb71950f9}", "PrivacyNeutralUi", C.Benefit, "隱私設定使用中立、不偏向的選項排版", "隐私设置使用中立、不偏向的选项排版", "Neutral, unweighted layout for privacy choices"),
            E("{b1a7c9e2-3d4f-4a6b-8c9d-1a2b3c4d5e6f}", "VegaSearch", C.Choice, "搜尋框的「Vega」搜尋（微軟未公開細節）", "搜索框的“Vega”搜索（微软未公开细节）", "\"Vega\" search in the search box (not documented)"),
            E("{e7a1f3c4-9b2d-4f8a-8c3e-5d7f6a9b1c2e}", "ExplorerCloudContent", C.Choice, "檔案總管首頁與詳細資料窗格顯示雲端關聯內容", "文件资源管理器主页与详细信息窗格显示云端关联内容", "Cloud-connected content in File Explorer Home and details"),
            E("{12c807e4-a5a4-4965-b980-531c108eb39e}", "RecallConsent", C.Benefit, "Recall 提供資料前先詢問", "Recall 提供数据前先询问", "Recall asks before providing data"),
            E("{f3a8c2d4-5b7e-4c9a-8f3d-1e2b3c4d5e6f}", "RecommendedActions", C.Choice, "檔案總管與桌面的建議動作", "文件资源管理器与桌面的建议操作", "Recommended actions in File Explorer and on the desktop"),
            E("{d3bb16fc-705c-483a-8c4b-4f0e914389c4}", "CuratedActionOrder", C.Benefit, "動作清單依微軟安排的順序排列", "操作列表按微软安排的顺序排列", "Action lists follow Microsoft's curated order"),
            E("{9a453b66-5ea7-4322-9aba-b054e914cc67}", "StoreUninstall", C.Benefit, "可以解除安裝 Microsoft Store", "可以卸载 Microsoft Store", "Microsoft Store can be uninstalled"),
            E("{b30e775c-9f50-4f5c-8a3e-ab8c78d439e0}", "ShareOpen", C.Benefit, "分享視窗開放給所有分享提供者", "分享窗口开放给所有分享提供商", "Share window open to all share providers"),
            E("{983e7839-8ee8-4705-81f0-962b8966b7d7}", "SearchV2", C.Choice, "工作列的新版搜尋", "任务栏的新版搜索", "New search experience on the taskbar"),
            E("{b7f4a1d2-3c8e-4f9a-9b6e-2f1d8a7c4e91}", "SearchBingDataLimit", C.Loss, "限制搜尋框傳給 Bing 的資料", "限制搜索框传给 Bing 的数据", "Limits search box data sent to Bing"),
            E("{b744cc5a-2ca0-4408-bf38-a565eb58f56e}", "UsageInsightsLimit", C.Choice, "限制「使用方式與品質洞察」服務（微軟命名不清）", "限制“使用方式与质量洞察”服务（微软命名不清）", "Limits the Usage and Quality Insights service (unclear naming)"),
            E("{fab7a223-1bc0-4f83-93d7-a31f0ce31173}", "CopilotKey", C.Benefit, "Copilot 鍵採用歐盟版行為", "Copilot 键采用欧盟版行为", "EEA behavior for the Copilot key"),
            E("{07fd8bc5-2e71-4290-8cdf-d61d8ecfe184}", "OrgBackupRestricted", C.Choice, "限制組織版 Windows 備份（微軟命名不清）", "限制组织版 Windows 备份（微软命名不清）", "Restricts Windows Backup for Organizations (unclear naming)"),
            E("{2dd61904-1adf-4a7e-8bce-35a6b913527d}", "SettingsUpsell", C.Benefit, "「設定」裡透過 Bing 推銷微軟產品", "“设置”里通过 Bing 推销微软产品", "Microsoft product upsells in Settings via Bing"),
            E("{f2c18e9d-7b4a-4e32-9c61-3a8d5f0b92c7}", "DefaultsTamperCheck", C.Choice, "歐盟地區檢查預設值是否在登錄中被竄改", "欧盟地区检查默认值是否在注册表中被篡改", "EEA check for tampering with defaults in the registry")
        };

        private static readonly Dictionary<string, CatalogEntry> _byGuid = BuildIndex(e => e.Guid);
        private static readonly Dictionary<string, CatalogEntry> _byId = BuildIndex(e => e.Id);

        private static Dictionary<string, CatalogEntry> BuildIndex(Func<CatalogEntry, string> key)
        {
            var map = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (CatalogEntry e in _all) map[key(e)] = e;
            return map;
        }

        public static IList<CatalogEntry> All { get { return Array.AsReadOnly(_all); } }
        public static int Count { get { return _all.Length; } }

        /// <summary>The catalog entry for a policy GUID, or null when the policy is unclassified.</summary>
        public static CatalogEntry ByGuid(string guid)
        {
            CatalogEntry e;
            return guid != null && _byGuid.TryGetValue(guid, out e) ? e : null;
        }

        public static CatalogEntry ById(string id)
        {
            CatalogEntry e;
            return id != null && _byId.TryGetValue(id, out e) ? e : null;
        }

        public static IEnumerable<CatalogEntry> OfClass(PolicyClass cls)
        {
            foreach (CatalogEntry e in _all) if (e.Class == cls) yield return e;
        }

        public static int CountOfClass(PolicyClass cls)
        {
            int n = 0;
            foreach (CatalogEntry e in _all) if (e.Class == cls) n++;
            return n;
        }
    }
}
