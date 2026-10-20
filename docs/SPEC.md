# One-Click EU Lord v2 — 規格（決策記錄）

> 這份文件是 v2 重寫的唯一依據。對話上下文會被壓縮，所以**所有決策都必須寫在這裡**。
> 寫程式前先讀這份，不要憑記憶。需求已於 2026-10-20 透過選擇題逐項確認完畢。

## 1. 專案身分

| 項目 | 值 |
|------|-----|
| 中文名 | 一鍵轉生歐爺（歐爺＝歐洲老爺） |
| 英文名 | One-Click EU Lord |
| 倉庫 | `PZW152/one-click-eu-lord`（由 `one-click-eu-grandpa` 改名，舊網址自動轉址） |
| 重來方式 | **同倉庫重寫為 v2**：保留 star／連結／v1.0.0–v1.2.0 的 Release 歷史，原始碼整個換掉 |
| 版本 | v2.0.0 起算 |
| 授權 | MIT |

舊版身分（升級時要能接管）：

- v1.0.x：`C:\ProgramData\OneClickEUGrandpa\`、排程 `OneClickEUGrandpa-Persist`
- v1.1–1.2：`C:\ProgramData\OneClickEUOverlord\`、排程 `OneClickEUOverlord-Persist`

## 2. 目標

讓任何一台**普通安裝完的 Windows 11**，一鍵取得愛爾蘭（EU＋EEA / DMA）政策的**所有好處**，並且**永久不會**因為系統更新或其他原因被刷回去。

## 3. 核心原則：超集（這是整個專案的重點）

歐盟版不只「多東西」，也**拿掉**了一些功能。所以：

| 分類 | 定義 | 處理方式 |
|------|------|----------|
| **好處 Benefit** | 歐盟版的狀態對使用者是增益 | **採用歐盟版** |
| **損失 Loss** | 歐盟版的狀態移除了原本有的功能 | **保留原本的**（不收歐盟版） |
| **見仁見智 Choice** | 看人喜好，或微軟未說明用途 | **預設保留原本的**，由使用者逐項勾選 |

> **v1 的錯誤（絕對不要重犯）**：v1 把「超集」實作成「完全照抄歐盟版」，於是連朗讀程式腳本擴充、Xbox 全螢幕、時鐘行事曆快速動作等**損失**一起收下，README 還寫了「不會替你關掉任何功能」這種**錯誤保證**。

## 4. 已確認的決策（12 項，全部來自使用者選擇）

| # | 決策 | 內容 |
|---|------|------|
| 1 | 重來範圍 | 同倉庫重寫為 v2 |
| 2 | 好處範圍 | 超集＋自訂 |
| 3 | 交付形式 | **單一 exe 可執行檔**，圖形介面 |
| 4 | 設計風格 | **瑞士國際主義設計**，強調色為**紅色**（見 §6） |
| 5 | 英文名 | One-Click EU Lord |
| 6 | GUI 結構 | **單頁＋進階展開**（狀態＋一顆主按鈕；進階設定、還原與移除收在可展開區） |
| 7 | 持久化 | **排程（登入＋每天）＋ Windows 更新完成後立即觸發**；不常駐服務 |
| 8 | 控制粒度 | **全 68 條都可調**：預設照超集自動處理，進階頁列出全部並可逐條手動覆寫 |
| 9 | 語言 | **三語自動**：繁體中文／简体中文／English，依 Windows 語言設定自動選，可手動切換 |
| 10 | 見仁見智的呈現 | **分成兩組勾選清單**：A「關掉這些功能」（改策略）、B「幫我卸載這些 App」（代勞執行，見 §9） |
| 11 | 地區 | **預設愛爾蘭（GeoID 68）**，進階頁可改其他 EEA 國家 |
| 12 | 命令列 | **支援完整參數**：雙擊開 GUI；帶參數可無介面執行（套用／還原／查狀態），供進階者、IT 批量部署、排程使用 |

## 5. 技術基礎（v1 實機驗證，可直接複用）

### 5.1 歐盟待遇由兩處決定

```
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion   ← GeoID 快取，愛爾蘭 = 68 (0x44)
C:\Windows\System32\IntegratedServicesRegionPolicySet.json                  ← 地區策略；本機 84 筆、79 個唯一 GUID
```

評估流程（`SystemSettings.DataModel.dll`）：讀 `DeviceRegion` → 讀不到則 `GetUserGeoID` 並快取回去 → `GetGeoInfoW` 轉兩碼國碼 → 查策略檔，命中地區條件就用該狀態，否則用 `defaultState`。

### 5.2 兩種切入點

- **只改地區**：Windows 自己算出歐盟狀態。但這樣會**連損失一起收下**，所以 v2 的超集**必須改策略檔**。
- **改策略檔**：把每條的 `defaultState` 設成目標狀態並**移除 region 條件**，就與地區無關，且能逐條控制。

v2 **兩者都做**：改策略檔達成逐條控制，同時把地區設成愛爾蘭，以覆蓋策略檔以外的歐盟行為（例如 Edge 本身的行為）。

### 5.3 必須遵守的格式細節（踩過的雷）

- 策略檔原檔是**無 BOM 的 UTF-8** → 改寫必須保持無 BOM。
- 策略檔的 `$schema`、`version` 欄位要保留；84 筆全部保留。
- 每次改寫都要從 **Windows 自己的原檔**重建，不可在改過的檔案上疊加。保存兩份備份：
  - `*.original.json`：**最早**看過的那一份（歷史）
  - `*.pristine.json`：**最新**由 Windows 提供的那一份（每次改寫的基礎）
  - 判斷「這是 Windows 的原檔」的方法：**還留著 region 條件**（我們寫出去的一定沒有）
- 日誌時間必須用 `InvariantCulture`，否則繁中系統會印出民國年（`115-10-20`）。
- 檔案若含中文：`.ps1` 必須是**含 BOM 的 UTF-8**；`.cmd` 必須**無 BOM ＋ CRLF**。（v2 是 exe，但工具腳本仍適用。）

### 5.4 UCPD

Windows 11 的 UserChoice Protection Driver 按**程式映像名稱**阻擋 `reg.exe`／`powershell.exe`／`cmd.exe`／`regedit.exe` 寫入受保護的鍵（含 `DeviceRegion`）。

做法：先用一般 API 寫入並**讀回驗證**；被擋才把 `reg.exe` 複製到暫存目錄**改名**後寫入這**一個值**，用完立刻刪除。**不停用整個驅動**。
v2 是自己的 exe，映像名稱不在拒絕清單上，**可能可以直接寫入**——要實測確認，仍保留改名備援。

### 5.5 技術選型（單一 exe 的關鍵）

- **C# WPF，介面以程式碼建構（不用 XAML）**，目標 **.NET Framework 4.8**。
- 理由：.NET Framework 4.8 是 **Windows 11 內建**，所以能編出**約 100KB、零依賴、不需安裝任何 runtime** 的單一 exe。若用 .NET 8 自包含，單檔會是 ~150MB，或要求使用者先裝 runtime。
- 不用 XAML 是因為 XAML 編譯需要 MSBuild；純程式碼建構 UI 可以直接用 Windows 內建的 `csc.exe` 編譯，CI 也單純。
- 需引用：`PresentationFramework.dll`、`PresentationCore.dll`、`WindowsBase.dll`、`System.Xaml.dll`。
- 內嵌 `app.manifest`（`requireAdministrator`）＋ 透過 `csc /win32manifest:` 加入，雙擊即跳 UAC。

## 6. 設計風格：瑞士國際主義

> **寫 UI 前先載入 `frontend-design` skill**（使用者明確要求善用 Claude 的設計能力）。

- 嚴格網格、模組化排版；齊左、右不齊（flush left, ragged right）
- 無襯線字體（Helvetica 系；Windows 上用 Arial／Segoe UI 等對應），**字級與字重**建立層級
- 配色：黑、白、灰 ＋ **單一強調色＝紅色**（朱紅）
- 大量留白；**不用**漸層、陰影、圓角裝飾、擬物
- 資訊以表格式對齊呈現（標籤／值兩欄）
- 大寫字母的標題、細水平分隔線是常見語彙

## 7. 策略分類表

**68 條「歐盟與全球不同」的策略**已逐條分類並附三語說明，目前存放在 v1 引擎的 `$Script:Catalog`：

```
src/EUOverlord.ps1   ← 搬移到 C# 前不要刪除；這是分類表的唯一來源
```

每筆欄位：`Guid`、`Id`（穩定識別字）、`Cat`（Benefit／Loss／Choice）、`Tw`／`Cn`／`En`（說明「啟用時」的行為）。

搬到 C# 時要保持 `Id` 不變（狀態檔與使用者設定靠它對應）。

未分類的策略（未來 Windows 更新新增的）處理方式：**超集模式維持原樣**，並在狀態與日誌中列出數量，提醒補分類。

## 8. 驗證與診斷（決策 #13）

寫入設定 ≠ 功能立刻出現（微軟分批 A/B 推送）。所以套用後要**實際驗證**並在失敗時**給診斷建議**：

- 檢查 Edge／Store 是否真的變成可卸（查註冊的卸載資訊／Appx 的可移除屬性）
- 檢查網頁搜尋的關閉開關是否出現
- 檢查 `DeviceRegion` 是否真的寫進去（讀回驗證）
- 檢查策略檔是否為我們寫出的版本（無 region 條件）
- 每項顯示 已生效／尚未生效／無法判斷；未生效時說明可能原因與下一步
  （常見原因：還沒重開機、微軟還沒推到這台、MDM／網域原則覆蓋、UCPD 阻擋）

## 9. 代勞卸載（決策 #10 的 B 組）

### 9.1 使用者的硬性約束

> **卸載 App 必須走 App 自己的卸載程序，不能硬刪，避免刪掉系統必要的組件。**

實作規則：

- **Store／MSIX App**（Copilot、手機連結、Widgets、Xbox、Teams…）：用官方 Appx 移除 API／`Remove-AppxPackage` 等價呼叫。
- **Edge**：呼叫它**自己註冊的卸載程式**（Edge 安裝目錄下的 `setup.exe --uninstall`，或 Uninstall 登錄項裡的命令），不自己刪檔。
- **OneDrive**：呼叫其註冊的卸載程式。
- **絕對不做**：手動刪檔案、刪資料夾、動 WinSxS、刪系統元件、強制移除沒有卸載程式的東西。
- 找不到合法卸載途徑的項目：**不提供**該選項，或明確標示為不可卸並說明原因。
- 卸載失敗：原樣回報結束碼與原因，**不退回硬刪**。

### 9.2 清單範圍

**歐盟政策解鎖的 ＋ 微軟自家內建 App**，逐項勾選：Edge、Microsoft Store、Copilot、手機連結、Widgets、Xbox、Teams、OneDrive、StartExperiencesApp 等。
不做成通用 App 管理器（不列出第三方已安裝程式）。

### 9.3 安全與可逆

- **逐一確認**：每個 App 卸載前分別確認，不是一次確認全部。
- **記錄**卸載了什麼、何時、結果。
- **提供「重新安裝」**：Store App 從 Microsoft Store 裝回；Edge 從官方下載頁。按鈕要能從記錄裡知道該裝回哪些。

## 10. 檔案與資料位置

```
C:\ProgramData\OneClickEULord\
├─ EULord.exe      ← 自己的複本，供排程使用（所以使用者手上那份可隨意移動或刪除）
├─ state.json      ← 模式、地區、原本地區、逐條覆寫、已卸載清單、未分類清單
├─ backups\
│  ├─ IntegratedServicesRegionPolicySet.original.json
│  └─ IntegratedServicesRegionPolicySet.pristine.json
└─ logs\
   └─ eulord.log   ← 一律英文、公曆時間，方便回報問題
```

- **還原**：從備份復原策略檔＋把地區改回原值＋移除排程（**保留備份**）
- **完全移除**：還原後再刪掉整個資料夾（**要明確確認**）
- 不在系統其他地方散落檔案
- **更新**：預設不連網；GUI 有「檢查更新」按鈕，按下才去查 GitHub 最新 Release

## 11. 持久化細節（決策 #7）

排程工作 `OneClickEULord-Persist`，以 SYSTEM／最高權限執行 `EULord.exe`（ProgramData 的複本）的無介面重新套用：

- 觸發一：**使用者登入**
- 觸發二：**每天**一次
- 觸發三：**Windows 更新完成事件**（訂閱事件記錄，例如 `Microsoft-Windows-WindowsUpdateClient` 的安裝完成事件），更新後立刻補回
- 不安裝常駐服務

## 12. 品質要求（來自 quality-absolutism skill）

- 全生命週期：安裝／使用／更新／遷移／復原／卸載都要可靠
- 集中、可追蹤的檔案管理（§10），不靠猜檔名做清理
- 卸載預設保留使用者資料，另提供明確的完整清除
- 穩定身分與可靠更新；內部重構不應讓系統把它當成新產品
- 以測試作為證據，誠實區分「已實作」「已測試」「未驗證」
- 回報要誠實：測試失敗就說失敗，跳過的步驟要講

## 13. 測試與 CI

- 保留 v1 的做法：測試**不需系統管理員權限、不碰系統**（所有路徑導向暫存沙盒）
- 每次推送在 GitHub 的 Windows runner 上編譯 exe ＋ 跑測試；發佈前必須通過
- 發佈流程：推 tag → 驗證版號與 tag 一致 → 編譯 → 測試 → 以 CHANGELOG 對應段落作為說明建立 Release，附件固定名稱 `EULord.exe`（讓 `releases/latest/download/EULord.exe` 永遠指向最新版）

## 14. 尚未決定（實作中若遇到需再問）

- 若 `DeviceRegion` 或策略檔由公司 MDM／網域原則管理，除了回報之外要不要提供任何繞法（傾向：只回報，不繞）
- GUI 的視窗尺寸與是否可調整
- 是否需要「匯出／匯入設定」給 IT 批量部署
