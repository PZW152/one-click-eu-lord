**繁體中文** · [简体中文](#简体中文) · [English](#english)

# 安全性與免責聲明

## 免責聲明

- 本專案對 Windows 進行**非官方、非 Microsoft 支援**的區域與功能自訂。
- 使用本工具代表你了解並接受相關風險；作者不對任何資料遺失、系統不穩或保固問題負責。
- 本工具**只應用於你本人擁有或獲授權管理的電腦**。

## 本工具會動到什麼

| 資源 | 用途 | 可還原 |
|------|------|--------|
| `HKLM\...\Control Panel\DeviceRegion`（登錄 DWORD） | 切換 Windows 認定的裝置地區 | ✅ 還原為原值 |
| `C:\Windows\System32\IntegratedServicesRegionPolicySet.json` | 只有徹底模式會改；強制 EEA 策略 | ✅ 從備份還原 |
| 排程工作 `OneClickEUOverlord-Persist` | 每次登入／每天重新套用 | ✅ 還原時移除 |
| `C:\ProgramData\OneClickEUOverlord\` | 備份、狀態、日誌 | ✅ `-FullClean` 刪除 |

本工具**不會**蒐集或上傳任何資料、安裝背景服務、永久停用 Windows 安全驅動（UCPD），或修改與地區無關的系統設定。

## 關於 UCPD

Windows 11 的 UserChoice Protection Driver（UCPD）以映像名稱比對，阻擋 `reg.exe`、`powershell.exe` 等工具寫入 `DeviceRegion`。本工具先嘗試正常寫入並讀回驗證；若被攔截，才用暫存目錄裡一份**改名的 `reg.exe` 複本**寫入這**一個值**，然後立刻刪除複本。整台機器的 UCPD **不會**被停用。若你不想用這個方法，可以在 Windows 初始設定（OOBE）時直接選擇 EEA 國家。

## 回報問題

請透過 GitHub Issues 回報，可附上 `C:\ProgramData\OneClickEUOverlord\logs\euoverlord.log`（英文、不含個資）。請勿貼上密碼、金鑰或個人資料。

---

<a id="简体中文"></a>

# 安全性与免责声明

## 免责声明

- 本项目对 Windows 进行**非官方、非 Microsoft 支持**的区域与功能自定义。
- 使用本工具即表示你了解并接受相关风险；作者不对任何数据丢失、系统不稳定或保修问题负责。
- 本工具**只应用于你本人拥有或获授权管理的电脑**。

## 本工具会改动什么

| 资源 | 用途 | 可恢复 |
|------|------|--------|
| `HKLM\...\Control Panel\DeviceRegion`（注册表 DWORD） | 切换 Windows 认定的设备区域 | ✅ 恢复为原值 |
| `C:\Windows\System32\IntegratedServicesRegionPolicySet.json` | 只有彻底模式会改；强制 EEA 策略 | ✅ 从备份恢复 |
| 计划任务 `OneClickEUOverlord-Persist` | 每次登录／每天重新应用 | ✅ 恢复时删除 |
| `C:\ProgramData\OneClickEUOverlord\` | 备份、状态、日志 | ✅ `-FullClean` 删除 |

本工具**不会**收集或上传任何数据、安装后台服务、永久禁用 Windows 安全驱动（UCPD），或修改与区域无关的系统设置。

## 关于 UCPD

Windows 11 的 UserChoice Protection Driver（UCPD）按映像名称匹配，阻止 `reg.exe`、`powershell.exe` 等工具写入 `DeviceRegion`。本工具先尝试正常写入并读回验证；若被拦截，才用临时目录中一份**重命名的 `reg.exe` 副本**写入这**一个值**，然后立即删除副本。整台电脑的 UCPD **不会**被禁用。如果你不想用这个方法，可以在 Windows 初始设置（OOBE）时直接选择 EEA 国家。

## 反馈问题

请通过 GitHub Issues 反馈，可附上 `C:\ProgramData\OneClickEUOverlord\logs\euoverlord.log`（英文、不含个人信息）。请勿粘贴密码、密钥或个人数据。

---

<a id="english"></a>

# Security & disclaimer

## Disclaimer

- This project performs an **unofficial, not Microsoft-supported** region and feature customization of Windows.
- By using it you accept the risks; the authors are not liable for data loss, instability or warranty issues.
- Use it **only on machines you own or are authorized to administer**.

## What the tool touches

| Resource | Purpose | Reversible |
|----------|---------|------------|
| `HKLM\...\Control Panel\DeviceRegion` (registry DWORD) | Switches the device region Windows uses | ✅ restored to the original value |
| `C:\Windows\System32\IntegratedServicesRegionPolicySet.json` | Hardcore mode only; forces the EEA policies | ✅ restored from backup |
| Scheduled task `OneClickEUOverlord-Persist` | Re-applies at logon and daily | ✅ removed on Restore |
| `C:\ProgramData\OneClickEUOverlord\` | Backups, state, logs | ✅ deleted with `-FullClean` |

The tool does **not** collect or upload any data, install background services, permanently disable a Windows security
driver (UCPD), or change system settings unrelated to the region.

## About UCPD

Windows 11's UserChoice Protection Driver (UCPD) matches process image names and blocks tools such as `reg.exe` and
`powershell.exe` from writing `DeviceRegion`. The tool first writes normally and reads the value back. Only if that was
blocked does it write this **one value** with a **renamed copy of `reg.exe`** in a temp folder, deleting the copy right
after. UCPD stays enabled for the rest of the system. If you prefer not to use this, pick an EEA country during Windows
setup (OOBE) instead.

## Reporting issues

Please use GitHub Issues. You can attach `C:\ProgramData\OneClickEUOverlord\logs\euoverlord.log` (English, no personal
data). Never paste passwords, keys or personal data.
