# 更新記錄 · 更新日志 · Changelog

每個版本依序提供繁體中文、简体中文、English。發佈流程會把對應版本的段落當成 Release 說明。
Each version lists Traditional Chinese, Simplified Chinese and English. The release workflow uses the matching section
as the release notes.

## v2.0.0 — 2026-10-21

完整重寫。v1 的 PowerShell 腳本全部移除，換成單一的 `EULord.exe`。
Complete rewrite. The v1 PowerShell scripts are gone, replaced by a single `EULord.exe`.

**繁體中文**

- **修正 v1 的核心錯誤。** v1 把「超集」實作成「完全照抄歐盟版」，於是連歐盟版**拿掉**的功能也一起收下 —— 朗讀程式的腳本擴充、Xbox 全螢幕體驗、時鐘與行事曆的快速動作等等。README 當時還寫了「工具不會替你關掉任何功能」，那是錯的。現在 68 條差異逐條分類成 **好處 40／損失 7／見仁見智 21**：好處採用歐盟版，損失**保留原本的**，見仁見智預設不動、由你勾選。
- **修正說明文件的反向錯誤。** 舊的 `docs/FEATURES.md` 把「網頁搜尋需要 Edge」那一條寫反了。新的 [docs/POLICIES.md](docs/POLICIES.md) 由 `tools/New-PolicyTable.ps1` 從程式碼產生，不可能再跟實作講不一樣的話。
- **單一 exe，圖形介面。** 約 182 KB、零依賴、不需安裝任何 runtime（.NET Framework 4.8 是 Windows 11 內建的）。
- **瑞士國際主義設計，「色面疊壓」。** 層級來自兩塊平塗色面在 12 欄網格上錯位疊壓，不靠陰影；套用時上層色面沿網格往左推一欄。**深淺兩套主題**，跟隨 Windows 設定自動切換。**標題列自己畫**（透過 WindowChrome，所以 Aero Snap、拖到頂端最大化、工作列縮圖都還在）。強調色是低飽和的氧化紅 `#B3402E`，一個畫面只用一次。字體全用 Arial —— 瑞士風就是 Helvetica 那一派，而 Arial 是 Windows 上與它等寬的那一款，不必內嵌 600 KB 的字體。
- 所有數值出自 `design/tokens.json`，`tools/New-Theme.ps1` 產生程式用的 `Theme.cs`，CI 會重新產生並比對，設計稿和程式不可能走樣。
- **68 條全部可逐條覆寫**，進階頁列出全部，可強制「歐盟版／原本的／自動」。
- **代勞卸載 App**：Edge、Store、Copilot、手機連結、小工具、Xbox、Teams、OneDrive 等。一律走 App 自己註冊的卸載程式，**絕不手動刪檔**；逐一確認；已卸載的有「重新安裝」。命令列刻意不提供這個功能。
- **套用後實際驗證**並給診斷建議，分成「已確定」與「觀察到的」兩層，量不到的就說無法判斷。
- **更新後立即補回**：排程多了 Windows Update 事件 19 的觸發，不必等到下次登入。
- **不再動系統檔案的權限。** v1 的 `takeown` + `icacls` 其實沒必要（原廠 DACL 已給 Administrators 完全控制），而且不可逆（擁有權要不回 TrustedInstaller，錯誤 1307）。v2 就地寫入，必要時才用 `SeRestorePrivilege`，兩條路都不留痕跡。
- **還原更正確**：記錄 `DeviceRegion` 原本是否存在，還原時該刪就刪，而不是寫回一個猜的值。
- 三語自動偵測、可手動切換；日誌一律英文、公曆時間。
- **寫完策略檔會讀回來比對**，對不上就立刻把 Windows 原檔放回去再回報失敗，不會留下一台被這支工具弄壞的機器。
- 可靠性排查（實際拿真機和六種壞掉的策略檔跑出來的，不是讀程式碼讀出來的）：日誌不再寫進 stdout，`--status --json` 現在是乾淨可解析的 JSON；`schtasks` 和備援寫入不再有讀管道順序造成的卡死；解除安裝命令只有空白時不再崩；本機沒有 `DeviceRegion` 時不再沿用 v1 記的舊值去還原一個從來不存在的地區；空的策略檔不再被誤判成「已經是我們改寫的」。
- 332 項測試，不需系統管理員、不碰系統；CI 另外檢查編碼、版號一致性、主題與設計 token 是否一致，以及分類表對 runner 上那個 Windows 版本的涵蓋率。

**简体中文**

- **修正 v1 的核心错误。** v1 把「超集」实现成「完全照抄欧盟版」，于是连欧盟版**拿掉**的功能也一起收下 —— 讲述人的脚本扩展、Xbox 全屏体验、时钟与日历的快速操作等等。README 当时还写了「工具不会替你关掉任何功能」，那是错的。现在 68 条差异逐条分类成 **好处 40／损失 7／见仁见智 21**：好处采用欧盟版，损失**保留原本的**，见仁见智默认不动、由你勾选。
- **修正文档的反向错误。** 旧的 `docs/FEATURES.md` 把「网页搜索需要 Edge」那一条写反了。新的 [docs/POLICIES.md](docs/POLICIES.md) 由 `tools/New-PolicyTable.ps1` 从代码生成，不可能再跟实现讲不一样的话。
- **单一 exe，图形界面。** 约 182 KB、零依赖、不需安装任何 runtime（.NET Framework 4.8 是 Windows 11 内置的）。
- **瑞士国际主义设计，「色面叠压」。** 层级来自两块平涂色面在 12 栏网格上错位叠压，不靠阴影；应用时上层色面沿网格往左推一栏。**深浅两套主题**，跟随 Windows 设置自动切换。**标题栏自己画**（通过 WindowChrome，所以 Aero Snap、拖到顶端最大化、任务栏缩略图都还在）。强调色是低饱和的氧化红 `#B3402E`，一个画面只用一次。字体全用 Arial —— 瑞士风就是 Helvetica 那一派，而 Arial 是 Windows 上与它等宽的那一款，不必内嵌 600 KB 的字体。
- 所有数值出自 `design/tokens.json`，`tools/New-Theme.ps1` 生成程序用的 `Theme.cs`，CI 会重新生成并比对，设计稿和程序不可能走样。
- **68 条全部可逐条覆写**，高级页列出全部，可强制「欧盟版／原本的／自动」。
- **帮忙卸载 App**：Edge、Store、Copilot、手机连接、小组件、Xbox、Teams、OneDrive 等。一律走 App 自己注册的卸载程序，**绝不手动删文件**；逐一确认；已卸载的有「重新安装」。命令行刻意不提供这个功能。
- **应用后实际验证**并给诊断建议，分成「已确定」与「观察到的」两层，量不到的就说无法判断。
- **更新后立即补回**：计划任务多了 Windows Update 事件 19 的触发，不必等到下次登录。
- **不再动系统文件的权限。** v1 的 `takeown` + `icacls` 其实没必要（出厂 DACL 已给 Administrators 完全控制），而且不可逆（所有权要不回 TrustedInstaller，错误 1307）。v2 就地写入，必要时才用 `SeRestorePrivilege`，两条路都不留痕迹。
- **还原更正确**：记录 `DeviceRegion` 原本是否存在，还原时该删就删，而不是写回一个猜的值。
- 三语自动检测、可手动切换；日志一律英文、公历时间。
- **写完策略文件会读回来比对**，对不上就立刻把 Windows 原文件放回去再报告失败，不会留下一台被这个工具弄坏的机器。
- 可靠性排查（实际拿真机和六种坏掉的策略文件跑出来的，不是读代码读出来的）：日志不再写进 stdout，`--status --json` 现在是干净可解析的 JSON；`schtasks` 和备用写入不再有读管道顺序造成的卡死；卸载命令只有空白时不再崩；本机没有 `DeviceRegion` 时不再沿用 v1 记的旧值去还原一个从来不存在的区域；空的策略文件不再被误判成「已经是我们改写的」。
- 332 项测试，不需管理员、不碰系统；CI 另外检查编码、版本号一致性、主题与设计 token 是否一致，以及分类表对 runner 上那个 Windows 版本的覆盖率。

**English**

- **Fixes the mistake at the centre of v1.** v1 implemented "superset" as *copying the
  EEA set verbatim*, which silently accepted the features the EU build **removes** —
  Narrator's scripting extensions, the Xbox full-screen experience, the Clock and
  Calendar quick actions and more. Its README claimed the tool never turned a feature
  off, which was false. All 68 differences are now classified individually as
  **40 gains / 7 losses / 21 matters of taste**: gains take the EU state, losses
  **keep Windows' own**, and matters of taste stay off until you tick them.
- **Fixes a documentation row that was stated backwards.** The old `docs/FEATURES.md`
  had "Edge is required for web search" the wrong way round. The new
  [docs/POLICIES.md](docs/POLICIES.md) is generated from the code by
  `tools/New-PolicyTable.ps1`, so it cannot disagree with the implementation again.
- **One exe with a GUI.** About 182 KB, no dependencies, no runtime to install
  (.NET Framework 4.8 ships inside Windows 11).
- **Swiss International Typographic Style, built on overlapping colour planes.**
  Depth comes from two flat planes crossing on a 12-column grid rather than from
  shadow, and applying slides the upper plane one column left. **Both themes
  ship** and the window follows the Windows light/dark setting. **The title bar
  is drawn by the app** through WindowChrome, so Aero Snap, drag-to-maximise and
  the taskbar thumbnail all still work. The accent is a muted iron-oxide
  `#B3402E`, used once per view. Arial throughout: Swiss is Helvetica's style and
  Arial is the Helvetica-metric face already on every Windows, so nothing is
  embedded.
- Every value comes from `design/tokens.json` through `tools/New-Theme.ps1`, and
  CI regenerates and compares, so the design system and the window cannot drift.
- **All 68 policies are individually overridable** — force EU, global or automatic
  from the advanced table.
- **Uninstalling Microsoft's apps**: Edge, Store, Copilot, Phone Link, Widgets, Xbox,
  Teams, OneDrive and more. Always through the app's own registered uninstaller,
  **never by deleting files**; confirmed one at a time; with a Reinstall link
  afterwards. Deliberately unavailable from the command line.
- **Real verification after applying**, with diagnostics, split into *settled* and
  *observed* so a slow Microsoft rollout is never reported as either success or
  failure. Anything unmeasurable is reported as such.
- **Re-applies right after an update** — the scheduled task now also triggers on
  `Microsoft-Windows-WindowsUpdateClient` event 19, rather than waiting for the next
  sign-in.
- **No longer changes system file permissions.** v1's `takeown` + `icacls` was both
  unnecessary (the shipped DACL already grants Administrators full control) and
  irreversible (ownership cannot be returned to TrustedInstaller, error 1307). v2
  writes in place and falls back to `SeRestorePrivilege` only if denied, leaving no
  trace either way.
- **More correct restore**: it records whether `DeviceRegion` existed at all, and
  deletes the value on restore if it did not, instead of writing back a guess.
- Three languages, auto-detected and switchable; the log stays English with Gregorian
  dates.
- **The policy file is read back after writing** and checked against what was
  meant to be written. If it does not match, the Windows original goes back
  immediately and the run reports failure, so a machine is never left holding a
  policy file this tool damaged.
- A reliability pass, found by running the real command line against the real
  machine and against six damaged or foreign policy files rather than by reading
  the code: log output no longer goes to stdout, so `--status --json` is now
  parseable; two child-process calls that read one pipe to the end before the
  other no longer deadlock; an uninstall command of only whitespace no longer
  throws; a machine with no `DeviceRegion` no longer inherits a stale v1 record
  and restores a region it never had; and an empty policy file is no longer
  mistaken for our own rewrite.
- 332 tests that need no administrator rights and touch nothing on the machine. CI
  additionally checks encodings, version consistency, that the theme matches the
  design tokens, and the catalog's coverage of the Windows build on the runner.

### Upgrading from v1

Nothing to do: run the new exe and press the button. It adopts the original region
v1 recorded so restore still works, and removes the v1.0–v1.2 scheduled tasks, which
would otherwise keep re-applying v1's behaviour. The old `C:\ProgramData\OneClickEUGrandpa\`
and `C:\ProgramData\OneClickEUOverlord\` folders are left alone rather than deleted.

## v1.2.0 — 2026-10-20

**繁體中文**

- **只要下載一個檔案**：[EU-Overlord.cmd](https://github.com/PZW152/one-click-eu-overlord/releases/latest/download/EU-Overlord.cmd)。不用解壓縮、不用找資料夾，雙擊就能用。原本的 `launchers\` 資料夾和 zip 已移除。
- **雙擊後是選單**：自動顯示目前狀態，直接按 Enter＝推薦的一鍵轉生；要還原就再雙擊一次選 3。會自己要求系統管理員權限，做完會問要不要重新開機。
- README 精簡成「下載 → 雙擊 → 按 Enter」三步驟，並說明遇到瀏覽器或 Windows 警告時怎麼做；進階內容收進折疊區。
- 修正：日誌檔應為英文，但「套用完成」那行會混入主控台語言的模式與地區名稱（例如 `Mode=溫和`）。
- 測試增加到 40 項，包含選單狀態判斷，以及實際建出並執行單一檔案。

**简体中文**

- **只需下载一个文件**：[EU-Overlord.cmd](https://github.com/PZW152/one-click-eu-overlord/releases/latest/download/EU-Overlord.cmd)。不用解压、不用找文件夹，双击就能用。原来的 `launchers\` 文件夹和 zip 已删除。
- **双击后是菜单**：自动显示当前状态，直接按 Enter＝推荐的一键转生；要恢复就再双击一次选 3。会自己请求管理员权限，完成后会问是否重启。
- README 精简成“下载 → 双击 → 按 Enter”三步，并说明遇到浏览器或 Windows 警告时怎么办；进阶内容收进折叠区。
- 修复：日志文件应为英文，但“应用完成”那一行会混入控制台语言的模式和地区名称（例如 `Mode=溫和`）。
- 测试增加到 40 项，包括菜单状态判断，以及实际构建并运行单文件。

**English**

- **One file to download**: [EU-Overlord.cmd](https://github.com/PZW152/one-click-eu-overlord/releases/latest/download/EU-Overlord.cmd).
  No unzipping, no folders: double-click it. The `launchers\` folder and the zip are gone.
- **A menu on double-click**: it shows the current status, and plain Enter means the recommended option; to undo,
  double-click again and choose 3. It asks for Administrator rights itself and offers to restart when done.
- The README is now three steps (download → double-click → Enter), explains what to do about browser and Windows
  warnings, and folds the advanced material away.
- Fix: the log file is meant to be English, but the "Apply complete" line picked up mode and region names in the
  console language (e.g. `Mode=溫和`).
- 40 tests, now including the menu status logic and building and running the single file.

## v1.1.0 — 2026-10-20

**繁體中文**

- 專案改名為 **One-Click EU Overlord**（中文名不變；「歐盟爺」＝歐盟大老爺）。倉庫改為 `one-click-eu-overlord`，舊網址會自動轉址。
- 新增**簡體中文與英文**介面：自動依 Windows 語言設定中排第一位的語言選擇，也可用 `-Language zh-TW | zh-CN | en` 指定。日誌固定為英文。
- 文件提供三種語言：`README.md`、`README.zh-CN.md`、`README.en.md` 與 `docs/`。
- 啟動檔改成語言中立的名稱：`1-Apply-Gentle.cmd` … `5-Uninstall.cmd`。
- **自動遷移舊版**：從 `C:\ProgramData\OneClickEUGrandpa\` 搬到 `C:\ProgramData\OneClickEUOverlord\`，保留原始備份，並換掉舊排程。
- 修正：徹底模式在 Windows PowerShell 5.1 下會替系統策略檔加上 BOM（原檔沒有 BOM）。現在維持無 BOM。
- `DeviceRegion` 已是目標值時不再嘗試寫入，日誌也不再出現誤導的「寫入失敗」。
- 主控台改用 UTF-8 輸出，簡體字在繁中主控台也能正常顯示。
- 新增免系統管理員權限的測試（`tests\Test-Engine.ps1`），GitHub Actions 每次推送都在 Windows 上執行；發佈前也必須先通過。

**简体中文**

- 项目改名为 **One-Click EU Overlord**（中文名不变；“欧盟爷”＝欧盟大老爷）。仓库改为 `one-click-eu-overlord`，旧网址会自动跳转。
- 新增**简体中文和英文**界面：自动按 Windows 语言设置中排第一位的语言选择，也可用 `-Language zh-TW | zh-CN | en` 指定。日志固定为英文。
- 文档提供三种语言：`README.md`、`README.zh-CN.md`、`README.en.md` 和 `docs/`。
- 启动文件改为与语言无关的名称：`1-Apply-Gentle.cmd` … `5-Uninstall.cmd`。
- **自动迁移旧版**：从 `C:\ProgramData\OneClickEUGrandpa\` 移到 `C:\ProgramData\OneClickEUOverlord\`，保留原始备份，并替换旧计划任务。
- 修复：彻底模式在 Windows PowerShell 5.1 下会给系统策略文件加上 BOM（原文件没有 BOM）。现在保持无 BOM。
- `DeviceRegion` 已是目标值时不再尝试写入，日志也不再出现误导性的“写入失败”。
- 控制台改用 UTF-8 输出，简体字在繁中控制台上也能正常显示。
- 新增无需管理员权限的测试（`tests\Test-Engine.ps1`），GitHub Actions 每次推送都在 Windows 上运行；发布前也必须先通过。

**English**

- Renamed to **One-Click EU Overlord** (the Chinese name is unchanged; 歐盟爺 = 歐盟大老爺, "the EU overlord"). The
  repository is now `one-click-eu-overlord`; old URLs redirect.
- **Simplified Chinese and English** interface: chosen from the first language in your Windows language settings, or
  set with `-Language zh-TW | zh-CN | en`. The log file stays in English.
- Documentation in all three languages: `README.md`, `README.zh-CN.md`, `README.en.md` and `docs/`.
- Launchers now have language-neutral names: `1-Apply-Gentle.cmd` … `5-Uninstall.cmd`.
- **Automatic migration** from `C:\ProgramData\OneClickEUGrandpa\` to `C:\ProgramData\OneClickEUOverlord\`, keeping the
  original backups and replacing the old scheduled task.
- Fix: under Windows PowerShell 5.1, Hardcore mode added a BOM to the system policy file, which ships without one. It
  now stays BOM-less.
- No write attempt when `DeviceRegion` already has the target value, so the log no longer shows a misleading failure.
- UTF-8 console output, so Simplified Chinese also displays on a Traditional Chinese console.
- Tests that need no Administrator rights (`tests\Test-Engine.ps1`), run on Windows by GitHub Actions on every push and
  required before a release is published.

## v1.0.1 — 2026-10-20

- 繁中：`EUGrandpa.ps1` 改存為含 BOM 的 UTF-8，修正非 UTF-8 碼頁（如繁中 cp950）上的亂碼。
- 简中：`EUGrandpa.ps1` 改存为带 BOM 的 UTF-8，修复非 UTF-8 代码页（如繁中 cp950）上的乱码。
- English: `EUGrandpa.ps1` saved as UTF-8 with BOM, fixing garbled text on non-UTF-8 code pages such as cp950.

## v1.0.0 — 2026-10-20

- 繁中：首個版本，提供溫和／徹底兩種一鍵模式、持久化排程與完整還原。
- 简中：首个版本，提供温和／彻底两种一键模式、持久化计划任务和完整恢复。
- English: first release with Gentle and Hardcore one-click modes, a persistence task and full restore.
