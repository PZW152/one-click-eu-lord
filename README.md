# 一鍵轉生歐盟爺 · One-Click EU Grandpa

> 在任何一台裝好的 Windows 11 上，一鍵取得「歐盟經濟區（EEA）＋ DMA（數位市場法）」版本的**所有使用者好處**，
> 並且讓它**不會因為系統更新或重開機被偷偷刷回去**。
>
> One click to unlock the full **Windows 11 EEA / DMA** user-benefit set on any machine,
> and keep it unlocked across Windows updates and reboots.

繁體中文說明在前，English below.

---

## 這是什麼 / What it is

微軟為了遵守歐盟《數位市場法》(DMA)，在**歐洲經濟區 (EEA)** 的 Windows 11 上開放了一整組「對使用者比較好」的行為，例如：

- ✅ **可以解除安裝 Microsoft Edge**（而且解除後其他程式不會一直叫你裝回來）
- ✅ **可以解除安裝 Microsoft Store、部分系統 App**
- ✅ **可以關閉 Windows 搜尋裡的 Bing／網頁搜尋**，搜尋框不再偷偷把資料送去 Bing
- ✅ **第三方搜尋提供者**可以出現在系統搜尋裡
- ✅ **小工具 (Widgets)** 可以關掉 Microsoft News 與廣告資訊流、限制資料分享
- ✅ **設定非 Edge 的預設瀏覽器**時，連 `.svg / .xml / .mht / ftp` 等類型一起接管，並詢問是否釘到工作列
- ✅ **大幅減少促銷、全螢幕推銷頁、把 Edge 塞給你的行銷活動**
- ✅ 把「系統元件」與一般 App 區分、限制帳號同步強迫同意等隱私面向的調整

這些開關全部由這個檔案決定：

```
C:\Windows\System32\IntegratedServicesRegionPolicySet.json
```

以及一個登錄值（Windows 判斷「你在不在 EEA」用的快取）：

```
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion
```

**一鍵轉生歐盟爺**就是把你的機器切換到「歐盟爺」狀態：拿到上面**整包好處的超集**，並用一個排程工作確保更新後自動補回來。

> 關於「超集」：本工具採用 **與 EEA 完全一致** 的策略集——這正是微軟官方定義、彼此協調的一整包。
> 凡是 EEA 版「給你選擇權」的開關都會被打開（例如可以關網頁搜尋、可以卸 Edge）。
> 工具**不會**幫你把那些好處硬關掉；它只是讓這些選項對你**可用**。要不要真的卸 Edge、關 Bing，決定權在你。

---

## 兩種一鍵模式 / Two one-click modes

| 模式 | 檔案 | 動作 | 侵入性 | 更新後是否保留 |
|------|------|------|--------|----------------|
| **溫和 Gentle**（推薦先用） | `1-一鍵溫和模式 Apply-Gentle.cmd` | 只改 `DeviceRegion` 登錄值為 EEA 國家（預設愛爾蘭），讓 **Windows 自己**算出 EEA 狀態 | 極低（只動一個登錄值） | 由持久化排程確保 |
| **徹底 Hardcore** | `2-一鍵徹底模式 Apply-Hardcore.cmd` | 溫和模式 **＋** 直接改寫 `IntegratedServicesRegionPolicySet.json`，讓每條策略對**所有地區**都評估成 EEA 狀態 | 中（改寫一個受保護的系統檔，已先備份） | 即使地區被重設也仍生效，再加持久化排程 |

兩種模式都會：
1. 先**備份**原始登錄值與原始策略檔到 `C:\ProgramData\OneClickEUGrandpa\backups\`；
2. 安裝一個 **SYSTEM 權限排程工作**，在**每次登入 + 每天**自動重新套用，對抗累積更新把設定刷回；
3. 把狀態記錄在 `state.json`，讓「還原」能誠實地復原。

> 為什麼要繞過：Windows 11 的 **UCPD** 驅動會攔截 `reg.exe / powershell.exe` 對 `DeviceRegion` 的寫入。
> 本工具先嘗試正常寫入，若被擋，才用「暫存目錄中改名的 `reg.exe` 複本」寫這**單一個值**後立即刪除——
> 它**不會**關閉整台機器的 UCPD 安全驅動，是最小侵入的作法。

---

## 怎麼用 / How to use

1. 下載整個資料夾（或 `git clone`）。
2. 打開 `launchers\` 資料夾。
3. 想要低風險 → 雙擊 **`1-一鍵溫和模式 Apply-Gentle.cmd`**；想要最徹底 → 雙擊 **`2-一鍵徹底模式 Apply-Hardcore.cmd`**。
4. 跳出 UAC 時按「是」（需要系統管理員權限）。
5. **登出或重開機**讓效果完整生效。
6. 之後要卸 Edge：到「設定 → 應用程式 → 已安裝的應用程式 → Microsoft Edge → 解除安裝」。

隨時可以：
- 雙擊 **`4-查看狀態 Status.cmd`** 看目前狀態；
- 雙擊 **`3-還原 Restore.cmd`** 把地區與策略檔還原、移除排程（保留備份）；
- 雙擊 **`5-完整解除安裝 Uninstall.cmd`** 還原並刪除本工具的所有資料。

### 進階：命令列參數

```powershell
# 以系統管理員開 PowerShell
.\src\EUGrandpa.ps1 -Action Apply -Mode Hardcore -Region Germany
.\src\EUGrandpa.ps1 -Action Status
.\src\EUGrandpa.ps1 -Action Restore
.\src\EUGrandpa.ps1 -Action Uninstall -FullClean -Force
```

可選 `-Region`：`Ireland`(預設) / `Germany` / `France` / `Netherlands` / `Spain` / `Italy` / `Sweden` / `Poland` / `Finland` / `Austria`。

---

## 風險與注意事項 / Risks & caveats

- 這**不是** Microsoft 支援的設定，屬於對你自己機器的區域／功能自訂。請自負風險。
- 建議第一次先用**溫和模式**。徹底模式會改寫系統檔（已備份、可還原）。
- 在部分版本（如 25H2）累積更新可能頻繁還原該 JSON——這正是本工具裝**持久化排程**的原因。
- 若 `DeviceRegion` 由公司 MDM/網域原則管理，可能需要在安全模式或由管理員處理。
- 功能是微軟分批 A/B 推送的，即使切成 EEA 也不保證每一項立刻出現。
- 本工具不蒐集、不上傳任何資料；所有動作都寫在 `C:\ProgramData\OneClickEUGrandpa\logs\`。

完整的技術細節與每條策略對照見 [`docs/FEATURES.md`](docs/FEATURES.md) 與 [`docs/HOW-IT-WORKS.md`](docs/HOW-IT-WORKS.md)。

---

## English summary

To comply with the EU **Digital Markets Act**, Microsoft ships a more user-respecting behavior set to Windows 11 in the **European Economic Area**: you can uninstall Edge / Store, disable Bing web search in Windows Search, turn off the Widgets news & ad feed, set a real default browser for all file types, and get far fewer Edge-promotion nags.

All of this is gated by `C:\Windows\System32\IntegratedServicesRegionPolicySet.json` plus the `DeviceRegion` registry value. **One-Click EU Grandpa** flips your machine into that EEA state — the full superset of benefits — and installs a scheduled task so a Windows update can't quietly revert it.

- **Gentle** = set `DeviceRegion` to an EEA country; Windows computes the EEA states itself. Minimal, one registry value.
- **Hardcore** = Gentle **plus** rewriting the policy JSON so every policy evaluates to its EEA state for every region (original backed up, fully restorable).

Everything is backed up and reversible (`Restore` / `Uninstall`). Run the launchers in `launchers\` as Administrator. Not a Microsoft-supported configuration; use at your own risk.

---

## 授權 / License

[MIT](LICENSE). 無任何擔保 / No warranty. 見 [`SECURITY.md`](SECURITY.md) 的免責聲明。
