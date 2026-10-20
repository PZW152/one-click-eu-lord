**繁體中文** · [简体中文](README.zh-CN.md) · [English](README.en.md)

# 一鍵轉生歐盟爺 · One-Click EU Overlord

> 「歐盟爺」就是**歐盟大老爺**——在 Windows 眼裡，住在歐盟的使用者就是得罪不起的大老爺：
> 想卸 Edge 就卸、想關 Bing 就關、廣告推銷少一大半。
>
> 這個工具讓任何一台裝好的 Windows 11，一鍵取得「歐洲經濟區（EEA）＋ DMA（數位市場法）」版本的**所有使用者好處**，
> 並且**不會因為系統更新或重開機被偷偷刷回去**。

---

## 歐盟大老爺有什麼好處

微軟為了遵守歐盟《數位市場法》（DMA），在**歐洲經濟區（EEA）**的 Windows 11 上開放了一整組對使用者更好的行為，例如：

- ✅ **可以解除安裝 Microsoft Edge**，解除後其他程式也不會一直叫你裝回來
- ✅ **可以解除安裝 Microsoft Store 與部分系統 App**
- ✅ **可以關閉 Windows 搜尋裡的 Bing／網頁搜尋**
- ✅ **第三方搜尋提供者**可以出現在系統搜尋裡
- ✅ **小工具（Widgets）**可以關掉 Microsoft News 與廣告資訊流，並限制資料分享
- ✅ **設定非 Edge 的預設瀏覽器**時，連 `.svg / .xml / .mht / ftp` 等類型一起接管
- ✅ **大幅減少促銷、全螢幕推銷頁、把 Edge 塞給你的行銷活動**

這些開關由兩樣東西決定：

```
C:\Windows\System32\IntegratedServicesRegionPolicySet.json         ← 84 條地區策略
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion   ← Windows 認定的裝置地區
```

**一鍵轉生歐盟爺**把你的機器切換到 EEA 狀態，拿到上面**整包好處的超集**，再用排程工作確保更新後自動補回。

> **關於「超集」**：本工具採用與 EEA **完全一致**的策略集——那是微軟官方定義、彼此協調的一整包。
> 凡是 EEA 版「給你選擇權」的開關都會打開；工具**不會**替你把功能硬關掉，
> 要不要真的卸 Edge、關 Bing，決定權在你。每條策略的對照見 [策略對照表](docs/FEATURES.md)。

---

## 兩種一鍵模式

| 模式 | 啟動檔 | 做什麼 | 侵入性 | 更新後是否保留 |
|------|--------|--------|--------|----------------|
| **溫和**（建議先用） | `1-Apply-Gentle.cmd` | 只把 `DeviceRegion` 改成 EEA 國家（預設愛爾蘭），讓 **Windows 自己**算出 EEA 狀態 | 極低，只動一個登錄值 | 由持久化排程確保 |
| **徹底** | `2-Apply-Hardcore.cmd` | 溫和模式 **＋** 改寫策略檔，讓每條策略對**所有地區**都是 EEA 狀態 | 中，改寫一個受保護的系統檔（先備份） | 即使地區被重設也仍生效 |

兩種模式都會：

1. 先把原始登錄值與原始策略檔**備份**到 `C:\ProgramData\OneClickEUOverlord\backups\`；
2. 安裝 **SYSTEM 權限的排程工作**，在**每次登入＋每天**自動重新套用；
3. 把狀態記在 `state.json`，讓「還原」能準確復原。

> **關於 UCPD**：Windows 11 的 UCPD 驅動會攔截 `reg.exe` / `powershell.exe` 對 `DeviceRegion` 的寫入。
> 本工具先正常寫入並讀回驗證，若被攔才用暫存目錄裡**改名的 `reg.exe` 複本**寫這**一個值**，用完立刻刪除。
> 它**不會**停用整台機器的 UCPD 安全驅動。

---

## 使用方式

1. 到 [Releases](https://github.com/PZW152/one-click-eu-overlord/releases/latest) 下載最新的 `one-click-eu-overlord-vX.Y.Z.zip` 並解壓縮。
2. 打開 `launchers\` 資料夾，雙擊要用的啟動檔：

   | 啟動檔 | 功能 |
   |--------|------|
   | `1-Apply-Gentle.cmd` | 一鍵溫和模式 |
   | `2-Apply-Hardcore.cmd` | 一鍵徹底模式 |
   | `3-Restore.cmd` | 還原地區與策略檔、移除排程（保留備份） |
   | `4-Status.cmd` | 查看目前狀態 |
   | `5-Uninstall.cmd` | 還原並刪除本工具的所有資料 |

3. 跳出 UAC 時按「是」（需要系統管理員權限）。
4. **登出或重新開機**讓效果完整生效。
5. 之後要卸 Edge：「設定 → 應用程式 → 已安裝的應用程式 → Microsoft Edge → 解除安裝」。

畫面文字會依你在「設定 → 時間與語言 → 語言」排在**第一位**的語言自動選擇**繁體中文、简体中文或 English**。

### 命令列參數

```powershell
# 以系統管理員身分開啟 PowerShell
.\src\EUOverlord.ps1 -Action Apply -Mode Hardcore -Region Germany
.\src\EUOverlord.ps1 -Action Status -Language zh-TW
.\src\EUOverlord.ps1 -Action Restore
.\src\EUOverlord.ps1 -Action Uninstall -FullClean -Force
```

| 參數 | 可用值 |
|------|--------|
| `-Action` | `Apply` / `Restore` / `Status` / `Uninstall`（`Reapply` 供排程使用） |
| `-Mode` | `Gentle`（預設）/ `Hardcore` |
| `-Region` | `Ireland`（預設）/ `Germany` / `France` / `Netherlands` / `Spain` / `Italy` / `Sweden` / `Poland` / `Finland` / `Austria` |
| `-Language` | `auto`（預設）/ `zh-TW` / `zh-CN` / `en` |
| `-FullClean` | 搭配 `Uninstall`：一併刪除備份與日誌 |
| `-Force` | 略過徹底模式與完整清除的確認提問 |

### 從舊版（One-Click EU Grandpa）升級

直接執行一次新版的「套用」即可。工具會自動把 `C:\ProgramData\OneClickEUGrandpa\` 搬到新位置、保留原本的備份，並換掉舊的排程工作。

---

## 風險與注意事項

- 這**不是** Microsoft 支援的設定，屬於對你自己機器的區域／功能自訂。請自負風險。
- 建議第一次先用**溫和模式**。徹底模式會改寫系統檔（已備份、可還原）。
- 有回報指出部分版本（如 25H2）的累積更新會頻繁還原策略檔——這正是本工具裝持久化排程的原因。
- 若 `DeviceRegion` 由公司 MDM／網域原則管理，可能需要在安全模式下操作，或交由管理員處理。
- 微軟以分批 A/B 方式推送功能，切成 EEA 也不保證每一項立刻出現。
- 本工具不蒐集、不上傳任何資料；所有動作都記錄在 `C:\ProgramData\OneClickEUOverlord\logs\euoverlord.log`（英文，方便回報問題）。

## 更多文件

- [策略對照表](docs/FEATURES.md) — 84 條策略的全球預設與 EEA 狀態
- [運作原理](docs/HOW-IT-WORKS.md) — 地區判斷、兩種模式、UCPD、持久化、檔案位置
- [發佈流程](docs/RELEASING.md) — 給維護者
- [更新記錄](CHANGELOG.md) · [安全性與免責聲明](SECURITY.md)

## 開發與測試

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\Test-Engine.ps1
```

測試不需要系統管理員權限，也不會動到系統：所有路徑都導向暫存沙盒。GitHub Actions 會在每次推送時於 Windows 上執行。

## 授權

[MIT](LICENSE)，無任何擔保。
