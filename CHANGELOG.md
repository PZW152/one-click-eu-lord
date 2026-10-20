# 更新記錄 · 更新日志 · Changelog

每個版本依序提供繁體中文、简体中文、English。發佈流程會把對應版本的段落當成 Release 說明。
Each version lists Traditional Chinese, Simplified Chinese and English. The release workflow uses the matching section
as the release notes.

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
