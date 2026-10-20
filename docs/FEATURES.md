**繁體中文** · [简体中文](zh-CN/FEATURES.md) · [English](en/FEATURES.md)

# 策略對照表

下表列出 `IntegratedServicesRegionPolicySet.json` 中的所有策略、它們的**全球預設狀態**，以及**歐洲經濟區（EEA）狀態**。
徹底模式會把每條策略對所有地區都設成「EEA 狀態」那一欄。`✦` 代表 EEA 與全球預設不同，也就是歐盟大老爺真正多拿到的差異。

> 本表由 [`tools/New-FeatureTable.ps1`](../tools/New-FeatureTable.ps1) 從實機上的策略檔（Schema 1.1.0）產生，共 84 條、73 條差異。
> 不同 Windows 組建的條目會有增減；策略說明保留微軟原文，方便對照。

| 策略（微軟原文說明） | 全球預設 | EEA 狀態 | 差異 |
|---|---|---|---|
| Edge is uninstallable. | `disabled` | `enabled` | ✦ |
| User can disable web search. | `disabled` | `enabled` | ✦ |
| Narrator scripting extensions are enabled. | `enabled` | `disabled` | ✦ |
| File Explorer Search is third party extensible. | `disabled` | `disabled` |  |
| First party File Explorer Search is allowed. | `enabled` | `disabled` | ✦ |
| SetAppAsDefault Public API exception for setting Edge as default | `disabled` | `disabled` |  |
| Default Apps settings extra types, one click pdf, pinning | `disabled` | `enabled` | ✦ |
| Third party search providers show in search. | `disabled` | `enabled` | ✦ |
| Third party search highlight content in search zero input. | `disabled` | `enabled` | ✦ |
| First party search highlight content in search zero input. | `enabled` | `enabled` |  |
| V2: Third parties can customize the Taskbar Gleam. | `disabled` | `enabled` | ✦ |
| Third parties can customize the Taskbar Gleam. | `disabled` | `disabled` |  |
| V2: First party Taskbar Gleam customization is shown. | `enabled` | `enabled` |  |
| First party Taskbar Gleam customization is shown. | `enabled` | `disabled` | ✦ |
| Xbox full screen experience. | `enabled` | `disabled` | ✦ |
| Search MRU shows third party provider. | `disabled` | `enabled` | ✦ |
| Edge is required for web search. | `disabled` | `enabled` | ✦ |
| System components are differentiated from apps. | `disabled` | `enabled` | ✦ |
| Backup options are restricted. | `disabled` | `enabled` | ✦ |
| Show files from the MS Office MRU redommendation provider. | `enabled` | `disabled` | ✦ |
| Third party feed is shown in Widgets. | `disabled` | `enabled` | ✦ |
| Third party Widgets are shown in Widgets feed. | `enabled` | `disabled` | ✦ |
| First party Widget feed can be disabled. | `disabled` | `enabled` | ✦ |
| Use default web protocol for Widget links. | `disabled` | `enabled` | ✦ |
| Restrict Widgets data sharing. | `disabled` | `enabled` | ✦ |
| Restrict third-party Widgets data sharing. | `disabled` | `enabled` | ✦ |
| UTC events should be tagged restricted. | `disabled` | `enabled` | ✦ |
| XBox performance fit data sharing. | `enabled` | `disabled` | ✦ |
| Shared ODD consent. | `enabled` | `disabled` | ✦ |
| Windows CoPilot | `enabled` | `disabled` | ✦ |
| Automatic app sign-in | `enabled` | `disabled` | ✦ |
| Notifications on the taskbar for Widgets. | `enabled` | `disabled` | ✦ |
| Show recent web searches in search zero input. | `enabled` | `disabled` | ✦ |
| Show website items in start recommendations. | `enabled` | `disabled` | ✦ |
| Widget sign-in is restricted to default account. | `disabled` | `enabled` | ✦ |
| Widget third-party taskbar badges are enabled. | `enabled` | `disabled` | ✦ |
| Widgets should be restricted to static recommendations. | `disabled` | `enabled` | ✦ |
| First-party Widgets have optimized frame and taskbar integration. | `enabled` | `disabled` | ✦ |
| News & Interest settings are shown. | `enabled` | `disabled` | ✦ |
| Full screen user setup promotional surfaces are allowed. | `enabled` | `disabled` | ✦ |
| Individual promotional pages within larger user setup flows are allowed. | `enabled` | `disabled` | ✦ |
| Windows and first-party apps are allowed to access web browser user profile IDs. | `enabled` | `disabled` | ✦ |
| Promotion and direct launch of Edge instead of the default browser from campaigns is allowed | `enabled` | `disabled` | ✦ |
| Campaign segment targeting is allowed | `enabled` | `disabled` | ✦ |
| Widgets is shown as a Status app on Lock Screen | `enabled` | `disabled` | ✦ |
| Third party apps can be expedited via UO infrastructure | `disabled` | `enabled` | ✦ |
| Windows Copilot chat provider extensibility | `disabled` | `enabled` | ✦ |
| Full screen user setup promotional surfaces are allowed. | `enabled` | `disabled` | ✦ |
| Individual promotional pages within larger user setup flows are allowed. | `enabled` | `disabled` | ✦ |
| Windows and first-party apps are allowed to access web browser user profile IDs. | `enabled` | `disabled` | ✦ |
| Promotion and direct launch of Edge instead of the default browser from campaigns is allowed | `enabled` | `disabled` | ✦ |
| Campaign segment targeting is allowed | `enabled` | `disabled` | ✦ |
| Windows Copilot can use foreground window OCR results to add context | `enabled` | `disabled` | ✦ |
| Show Copilot on taskbar by default when definitive answer is not received from Bing on user eligibility | `enabled` | `enabled` |  |
| Replace tailored experience with personalized offers | `disabled` | `enabled` | ✦ |
| Windows Copilot PWA is pre-pinned during LCU upgrades. EEA (European Economic Area) countries are excluded from this | `enabled` | `disabled` | ✦ |
| WinAct Clock and Calendar action sources are unavailable in the EEA until DMA compliant. | `enabled` | `disabled` | ✦ |
| Copilot PWA is available on Windows in these countries | `enabled` | `enabled` |  |
| OneDrive KFM offer is rate-limited on the device | `disabled` | `enabled` | ✦ |
| A9 Optional Component is disabled in some regions | `enabled` | `disabled` | ✦ |
| OneDrive Resume feature based on device region | `enabled` | `disabled` | ✦ |
| Windows Account Sync Consent is applicable | `disabled` | `enabled` | ✦ |
| Widgets On Lock feature based on device region | `disabled` | `enabled` | ✦ |
| Make StartExperiencesApp uninstallable | `disabled` | `enabled` | ✦ |
| Manage Start Menu's Phone Link companion | `enabled` | `disabled` | ✦ |
| Show modified UX layout, such as unweighted options, for Privacy-related settings | `disabled` | `enabled` | ✦ |
| Manage Vega search in Windows Search Box | `enabled` | `disabled` | ✦ |
| File Explorer graph connected content shows in File Explorer Home and details pane | `enabled` | `disabled` | ✦ |
| Manage Recall Data Provision Dialog | `disabled` | `enabled` | ✦ |
| Recommended actions show in File Explorer and Desktop | `enabled` | `disabled` | ✦ |
| C2D extensible entity actions should be displayed in curated order | `enabled` | `disabled` | ✦ |
| Settings Agent is disabled in some regions | `enabled` | `enabled` |  |
| Windows feature is disabled in regions due to language support | `enabled` | `enabled` |  |
| Microsoft Store has specific options available in some regions, such as ability to uninstall | `disabled` | `enabled` | ✦ |
| ShareWindowCommandSource API participation unrestricted to all share providers in DMA region | `disabled` | `enabled` | ✦ |
| SearchV2 on Taskbar feature | `enabled` | `disabled` | ✦ |
| Restrict Windows Search Box Client Data Exposure to Bing Server in Non-DMA Regions Only | `enabled` | `disabled` | ✦ |
| Unique region specific behavior for Copilot Key | `disabled` | `disabled` |  |
| Restrict Windows Usage And Quality Insights Service features in DMA Regions | `enabled` | `disabled` | ✦ |
| Copilot hardware key has region-specific behavior in EEA (European Economic Area) regions. | `disabled` | `enabled` | ✦ |
| FilesAgent MCP Server is available. | `disabled` | `disabled` |  |
| Windows Backup for Organizations is restricted in privacy-sensitive regions (EEA, UK, South Korea, Brazil, Canada, Vietnam, China). | `enabled` | `disabled` | ✦ |
| Microsoft Products upsell experiences in Windows Settings via Bing | `enabled` | `disabled` | ✦ |
| EEA Region check for default behavior tampering in Registry | `disabled` | `enabled` | ✦ |

## 重點好處

| 策略 | EEA 狀態 | 對你的意義 |
|------|----------|------------|
| Edge is uninstallable | `enabled` | 可以解除安裝 Edge |
| Microsoft Store … ability to uninstall | `enabled` | 可以解除安裝 Microsoft Store |
| User can disable web search | `enabled` | 可以關閉搜尋中的網頁／Bing 結果 |
| Edge is required for web search | `disabled` | 網頁搜尋不再綁定 Edge |
| Third party search providers show in search | `enabled` | 第三方搜尋可以出現 |
| First party Widget feed can be disabled | `enabled` | 可以關掉小工具的新聞與廣告資訊流 |
| Full screen user setup promotional surfaces are allowed | `disabled` | 移除全螢幕推銷頁 |
| Promotion and direct launch of Edge … from campaigns | `disabled` | 不再用行銷活動把 Edge 塞給你 |
| Microsoft Products upsell experiences in Windows Settings via Bing | `disabled` | 「設定」裡不再塞升級推銷 |
| SetAppAsDefault Public API exception for setting Edge as default | `disabled` | 在台灣／中國等地，Edge 不能再透過例外 API 把自己設成預設瀏覽器 |

## 設計理念

目標是「**好處的超集**」：凡是 EEA 給你**選擇權**的（可以卸、可以關、可以換），一律讓它對你可用。
工具只是把這些**選項**打開，**不會**替你把功能硬關掉——要不要真的卸 Edge、關 Bing，由你決定。
徹底模式採用與 EEA **完全一致**的整包策略，因為那是微軟官方定義、彼此協調的一組，比逐條主觀挑選更穩定、更可預期。
