**繁體中文** · [简体中文](zh-CN/HOW-IT-WORKS.md) · [English](en/HOW-IT-WORKS.md)

# 運作原理

## 0. 單一檔案與選單

使用者下載的 `EU-Overlord.cmd` 由 [`tools\Build-SingleFile.ps1`](../tools/Build-SingleFile.ps1) 產生：前面是一小段純 ASCII 的批次檔，後面原封不動接上 `src\EUOverlord.ps1`。

雙擊時：

1. 批次檔把分隔線之後的引擎寫成 `%TEMP%\EUOverlord-<亂數>.ps1`（含 BOM 的 UTF-8），用 Windows PowerShell 執行，結束後刪除。
2. 沒有參數時，引擎開啟**選單**：顯示目前狀態，直接按 Enter＝選項 1。
3. 沒有系統管理員權限時，引擎透過 UAC 以系統管理員身分開新視窗；若你拒絕，會說明「沒有做任何變更」。
4. 套用或還原後，會問要不要立刻重新開機（預設不要）。
5. 沒有互動式主控台時（例如被腳本呼叫、輸入被導向），選單拒絕執行並回傳 2，避免「Enter＝1」被誤觸發。

加上參數就是命令列工具，例如 `EU-Overlord.cmd -Action Status`。
檔案格式：無 BOM 的 UTF-8（BOM 會讓 `@echo off` 失效）、CRLF 換行、批次檔部分純 ASCII。測試會逐項檢查，並實際執行一次。

## 1. Windows 怎麼判斷「你在不在 EEA」

地區策略由 `SystemSettings.DataModel.dll` 評估，流程大致是：

1. 讀取登錄值 `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion`（DWORD，存 GeoID）。
2. 讀不到時呼叫 `GetUserGeoID(GEOCLASS_NATION)`，再把結果**快取**回上面那個登錄值。
3. 用 `GetGeoInfoW` 把 GeoID 轉成兩碼國家代碼（如 `IE`、`DE`）。
4. 讀取 `C:\Windows\System32\IntegratedServicesRegionPolicySet.json`，對每條策略：地區條件命中就用該條件的狀態，否則用 `defaultState`。

所以「變成歐盟大老爺」有兩個切入點：改**地區快取**（讓第 1～3 步看到 EEA），或改**策略檔**（讓第 4 步直接輸出 EEA 狀態）。本工具兩者都做。

## 2. 溫和模式

只把 `DeviceRegion` 寫成一個 EEA 國家的 GeoID（預設愛爾蘭 = 68 / `0x44`），讓 Windows **自己**把 84 條策略算成 EEA 狀態。只動一個值、乾淨、好還原。值本來就正確時，工具不會重寫它。

## 3. 徹底模式

在溫和模式之上直接改寫策略檔。對**每一條**策略：

- 算出它在 EEA（以 `IE` 為基準）會是什麼狀態；
- 把 `defaultState` 設成該狀態，並**移除 region 條件**。

這樣無論 `DeviceRegion` 是什麼，策略都輸出 EEA 狀態。轉換是**通用、不綁 GUID** 的，微軟新增策略時規則依然適用。

細節：

- 原始檔先備份到 `C:\ProgramData\OneClickEUOverlord\backups\IntegratedServicesRegionPolicySet.original.json`。重複套用時**保留第一份**備份，不會被改寫過的版本蓋掉。
- 系統原檔是**沒有 BOM 的 UTF-8**，工具寫回時也維持無 BOM（Windows PowerShell 5.1 的 `Set-Content -Encoding UTF8` 會自動加 BOM，所以改用 .NET 直接寫入）。

## 4. UCPD 繞過（只針對 DeviceRegion 這一個值）

Windows 11 的 **UserChoice Protection Driver（UCPD）** 以映像名稱比對，阻擋 `reg.exe`、`powershell.exe`、`cmd.exe`、`regedit.exe` 等工具寫入受保護的鍵（包含 `DeviceRegion`）。

`Set-DeviceRegionValue` 的做法：

1. 先用一般 API 寫入，再**讀回驗證**；
2. 若值沒變（被 UCPD 攔截），把 `reg.exe` 複製到 `%TEMP%` 並**改名**，用它寫入這一個值，然後**刪除複本**。

此法不停用整台機器的 UCPD。若環境特殊（MDM 管理、UCPD 規則更嚴），可能需要在安全模式下套用，或在初始設定（OOBE）時直接選 EEA 國家。

## 5. 持久化

排程工作 `OneClickEUOverlord-Persist`（SYSTEM 權限）：

- 觸發：**每次登入**＋**每天 03:00**；
- 動作：以 `-Action Reapply` 讀取 `state.json`，重新套用你選的模式與地區。

腳本會複製到 `C:\ProgramData\OneClickEUOverlord\EUOverlord.ps1`，所以刪掉下載的資料夾後持久化仍然有效。

## 6. 多語言

- 畫面文字：繁體中文、简体中文、English。`-Language auto` 會先看你在 Windows「語言」設定裡**排第一位**的語言，再看顯示語言（有些電腦的顯示語言仍是英文，但使用者把中文排第一）。
- 主控台改用 UTF-8 輸出，簡體字在預設繁中（cp950）主控台上也能正常顯示。
- 日誌一律寫**英文**，時間用西元曆（避免繁中系統顯示民國年），方便直接附在問題回報裡。
- `src\EUOverlord.ps1` 必須保存為**含 BOM 的 UTF-8**：Windows PowerShell 5.1 讀無 BOM 檔案時會用系統 ANSI 碼頁，中文會變亂碼。測試與發佈流程都會檢查。

## 7. 檔案與資料位置

```
C:\ProgramData\OneClickEUOverlord\
├─ EUOverlord.ps1                     ← 給排程使用的腳本複本
├─ state.json                         ← 目前模式／地區／原本地區
├─ backups\
│  └─ IntegratedServicesRegionPolicySet.original.json
└─ logs\
   └─ euoverlord.log
```

「還原」從備份復原；「完整解除安裝」（`-FullClean`）整個刪除。本工具不在系統其他地方散落檔案。

### 從 v1.0.x 遷移

v1.0.x 叫 One-Click EU Grandpa，資料在 `C:\ProgramData\OneClickEUGrandpa\`，排程叫 `OneClickEUGrandpa-Persist`。
新版在 `Apply` / `Restore` / `Uninstall` 前會：

1. 移除舊排程；
2. 把舊資料夾整個搬到新位置（保留原始備份與原本地區），並刪掉舊腳本複本；
3. 如果新舊資料夾同時存在，保留新的、不動舊的，並在日誌中說明。

`Status` 保持唯讀，只會提示偵測到舊版。

## 8. 參考來源

- Rafael Rivera — *Device Region and Integrated Services Region Policy in Windows*
- Pureinfotech、gHacks、Winaero、XDA — IntegratedServicesRegionPolicySet.json 介紹
- Christoph Kolbicz、Binary Ninja — UCPD 驅動分析
- Microsoft EU Policy Blog、Windows Insider Blog — DMA 合規變更

> 以上多為第三方與社群分析；微軟沒有把編輯此檔列為支援的設定。實際行為以你機器上的組建為準。
