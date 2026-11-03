# 更新記錄 · Changelog

## v3.0.0 — 2026-11-04

**繁體中文**

- 以純 Rust 完整重寫；移除 C#／WPF、PowerShell 執行路徑與 WebView。
- GUI 改為 `egui`／`eframe`，復古未來主義 TUI 風格但完全可用滑鼠操作。
- 68 條政策維持 40 好處／7 損失／21 見仁見智；未分類政策保持 Windows 原樣，不再移除其 `conditions`。
- 套用改為交易式流程：備份、還原點、原子替換、地區、排程、驗證、關鍵失敗自動 rollback。
- 排程工作改為直接使用 Task Scheduler COM，支援登入、每日與 Windows Update 觸發。
- 新增完整 CLI：`status`、`plan`、`apply`、`reapply`、`restore`、`purge`、`verify`、`export`、`import`，支援穩定 JSON 與離開碼。
- **軟件解除安裝模組已完整移除。** EU 解鎖頁只顯示政策資訊，實際解除安裝請使用 Windows 設定。
- 新增 on-demand UAC、`asInvoker` GUI、資料夾 ACL 收緊、本機 UI 偏好備援。
- 新增 Windows 資源圖示、版本資訊、CI、可攜版打包與 MSIX/App Installer 管線。
- 已知外部依賴：MSIX 需要受信任的程式碼簽章憑證；repo 不附憑證。

**简体中文**

- 使用纯 Rust 完整重写；移除 C#/WPF、PowerShell 执行路径和 WebView。
- GUI 改用 `egui`/`eframe`，复古未来主义 TUI 风格，但完全支持鼠标。
- 68 条政策保持 40 好处/7 损失/21 见仁见智；未分类政策保持原样，不再移除 conditions。
- 套用改为事务式：备份、还原点、原子替换、地区、计划任务、验证、关键失败自动回滚。
- 计划任务改用 Task Scheduler COM，支持登录、每日与 Windows Update 触发。
- 新增完整 CLI 与稳定 JSON/退出码。
- **软件卸载模块已完全移除。** 解锁页只显示政策信息。
- 新增按需 UAC、asInvoker GUI、ACL 收紧、本地 UI 偏好备用。
- 新增图标、版本信息、CI、可携带版和 MSIX/App Installer 管线。
- 外部依赖：MSIX 需要受信任代码签名证书。

**English**

- Full pure-Rust rewrite; removed C#/WPF, PowerShell execution paths and WebView.
- GUI moved to `egui`/`eframe`: retro-futurist TUI styling, fully mouse-operable.
- Policy superset stays 40 benefit / 7 loss / 21 choice; unknown policies are left untouched, including their `conditions`.
- Transactional apply: backup, restore point, atomic replace, region, scheduled task, verify, automatic rollback on critical failure.
- Scheduled task uses Task Scheduler COM directly with logon, daily and Windows Update triggers.
- Full CLI with stable JSON and exit codes.
- **Software-uninstall module removed entirely.** The EU Unlocks page is read-only policy information.
- On-demand UAC, `asInvoker` GUI, restrictive data-folder ACL, local UI preference fallback.
- Embedded Windows icon/version resource, CI, portable packaging and MSIX/App Installer pipeline.
- External prerequisite: MSIX requires a trusted code-signing certificate; none is bundled.

## Older versions

v2 and v1 were C#/PowerShell implementations. Their source and release notes are available in git history.

