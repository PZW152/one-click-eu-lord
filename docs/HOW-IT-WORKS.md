# 運作原理 / How it works

## 1. Windows 怎麼判斷「你在不在 EEA」

Windows 的功能開關由 `SystemSettings.DataModel.dll` 評估，流程大致是：

1. 讀取登錄值
   `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion`（DWORD，存 GeoID）。
2. 若讀不到，呼叫 `GetUserGeoID(GEOCLASS_NATION)`，再把結果**快取**回上面那個登錄值。
3. 用 `GetGeoInfoW` 把 GeoID 轉成兩碼國家代碼（如 `IE`、`DE`）。
4. 讀取 `C:\Windows\System32\IntegratedServicesRegionPolicySet.json`，對每條策略：
   - 若某地區條件命中 → 用該條件的狀態；
   - 否則 → 用 `defaultState`。

因此「切換到 EEA」有兩個切入點：改**地區快取**（讓第 1~3 步看到 EEA），或改**策略檔**（讓第 4 步直接輸出 EEA 狀態）。本工具兩者都做。

## 2. 溫和模式 (Gentle)

只把 `DeviceRegion` 寫成一個 EEA 國家的 GeoID（預設愛爾蘭 = 68 / `0x44`）。
Windows 隨後會**自己**把 84 條策略算成 EEA 狀態。優點：只動一個值、乾淨、易還原。

## 3. 徹底模式 (Hardcore)

在溫和模式之上，直接改寫策略檔：對**每一條**策略
- 先算出它在 EEA（以 `IE` 為基準）會是什麼狀態；
- 把 `defaultState` 設成該狀態，並**移除 region 條件**。

這樣一來，無論 `DeviceRegion` 是什麼，策略都輸出 EEA 狀態。即使日後地區被重設，好處仍在。
這個轉換是**通用、不綁 GUID** 的——微軟新增策略時規則依然適用。原始檔會先備份到
`C:\ProgramData\OneClickEUGrandpa\backups\IntegratedServicesRegionPolicySet.original.json`。

## 4. UCPD 繞過（僅針對 DeviceRegion 單一值）

Windows 11 的 **UserChoice Protection Driver (UCPD)** 會以映像名稱比對，阻擋
`reg.exe / powershell.exe / cmd.exe / regedit.exe` 等 Microsoft 簽章工具寫入受保護鍵（含 `DeviceRegion`）。

本工具的 `Set-DeviceRegionValue`：
1. 先用一般 API 寫入，再**讀回驗證**；
2. 若值沒變（被 UCPD 擋），複製一份 `reg.exe` 到 `%TEMP%` 並**改名**，用它寫入這一個值，然後**刪除複本**。

此法不停用整台機器的 UCPD，是針對單一值的最小侵入繞過。若環境特殊（如 MDM 管理、UCPD 嚴格），
可能需要在安全模式套用，或在 OOBE 階段直接選 EEA 國家。

## 5. 持久化 (Persistence)

安裝一個排程工作 `OneClickEUGrandpa-Persist`（SYSTEM 權限）：
- 觸發：**每次登入** + **每天 03:00**；
- 動作：以 `-Action Reapply` 讀取 `state.json`，重新套用你選的模式與地區。

這是為了對抗部分組建（如 25H2）累積更新頻繁還原策略檔的行為。工具腳本會被複製到
`C:\ProgramData\OneClickEUGrandpa\EUGrandpa.ps1`，因此即使你刪掉下載的資料夾，持久化仍可運作。

## 6. 檔案與資料位置（集中、可追蹤）

```
C:\ProgramData\OneClickEUGrandpa\
├─ EUGrandpa.ps1                      ← 供排程使用的腳本複本
├─ state.json                         ← 目前模式／地區／原始地區
├─ backups\
│  └─ IntegratedServicesRegionPolicySet.original.json
└─ logs\
   └─ eugrandpa.log
```

所有動作集中於此，`還原` 從備份復原、`完整解除安裝 -FullClean` 整個刪除。
不散落登錄或系統各處，符合「可追蹤、可還原」原則。

## 7. 參考來源 / References

- Rafael Rivera — *Device Region and Integrated Services Region Policy in Windows*
- Pureinfotech / gHacks / Winaero — IntegratedServicesRegionPolicySet.json 介紹
- Christoph Kolbicz / Binary Ninja — UCPD 驅動分析
- Microsoft EU Policy Blog / Windows Insider — DMA 合規變更

> 這些為第三方與社群分析；微軟未將編輯此檔列為支援設定。實際行為以你機器上的組建為準。
