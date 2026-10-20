# 免責聲明與安全性 / Disclaimer & Security

## 免責聲明 / Disclaimer

- 本專案對你的 Windows 進行**非官方、非 Microsoft 支援**的區域與功能自訂。
- 使用本工具代表你了解並接受相關風險，作者不對任何資料遺失、系統不穩或保固問題負責。
- 本工具**只應用於你本人擁有或獲授權管理的電腦**。
- This project performs an **unofficial, not-Microsoft-supported** region/feature customization of Windows. Use at your own risk, only on machines you own or are authorized to administer.

## 本工具會動到什麼 / What it touches

| 資源 | 用途 | 可還原 |
|------|------|--------|
| `HKLM\...\Control Panel\DeviceRegion` (登錄 DWORD) | 切換 Windows 看到的裝置地區 | ✅ 還原為原值 |
| `C:\Windows\System32\IntegratedServicesRegionPolicySet.json` | 徹底模式才改；強制 EEA 策略 | ✅ 從備份還原 |
| 排程工作 `OneClickEUGrandpa-Persist` | 每次登入／每天重新套用 | ✅ 解除時移除 |
| `C:\ProgramData\OneClickEUGrandpa\` | 備份、狀態、日誌 | ✅ `-FullClean` 刪除 |

本工具**不會**：蒐集或上傳任何個人資料、安裝背景服務、永久停用 Windows 安全驅動（UCPD），或修改與區域無關的系統設定。

## UCPD 相關 / About UCPD

Windows 11 的 UserChoice Protection Driver（UCPD）會以「映像名稱」比對來阻擋 `reg.exe`、`powershell.exe` 等工具寫入 `DeviceRegion`。本工具的作法是：

1. 先嘗試正常寫入並讀回驗證；
2. 若被擋，才用暫存目錄裡一份**改名的 `reg.exe` 複本**寫入這**單一個值**，然後立刻刪除複本。

此作法**不**停用整台機器的 UCPD，屬於針對單一值的最小侵入繞過。若你偏好完全不繞過，可在 OOBE（初始設定）階段選擇 EEA 國家，或自行在安全模式修改。

## 回報問題 / Reporting

請透過 GitHub Issues 回報。請勿在 Issue 中貼上任何密碼、金鑰或個資。
Please report via GitHub Issues. Do not paste passwords, keys, or personal data.
