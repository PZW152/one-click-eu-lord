# 原理

**繁體中文** · [简体中文](HOW-IT-WORKS.zh-CN.md) · [English](HOW-IT-WORKS.en.md)

以下全部在 Windows 11 build 26300 上實機驗證。別的版本可能附不一樣的策略檔；
工具讀的是你機器上那一份，不是憑記憶假設。

---

## 1. 決定歐盟待遇的是兩樣東西

```
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion
    一個 DWORD，存 GeoID。愛爾蘭是 68。

C:\Windows\System32\IntegratedServicesRegionPolicySet.json
    策略集本體。build 26300 上有 84 筆、79 個唯一 GUID。
```

JSON 長這樣：

```json
{
  "$schema": "schemas/IntegratedServicesRegionPolicySet.Schema.1.1.0.json",
  "version": "1.0",
  "policies": [
    {
      "$comment": "Edge is uninstallable.",
      "guid": "{1bca278a-5d11-4acf-ad2f-f9ab6d7f93a6}",
      "defaultState": "disabled",
      "conditions": { "region": { "enabled": ["AT", "BE", "…", "IE", "…"] } }
    }
  ]
}
```

這一筆的意思是：**全世界關，但這 36 個國家開。**

## 2. Windows 怎麼判讀一條策略

`SystemSettings.DataModel.dll` 大致做這幾件事：

1. 讀 `DeviceRegion`。
2. 讀不到就呼叫 `GetUserGeoID(GEOCLASS_NATION)`，並把答案**寫回** `DeviceRegion` 快取起來。
3. 用 `GetGeoInfoW` 把 GeoID 轉成 ISO-3166 兩碼國碼。
4. 查策略。地區條件有提到這個國家就用那個狀態，否則用 `defaultState`。

第 4 步就是全部的機會所在：**一條沒有地區條件的策略，永遠落回 `defaultState`。**

## 3. 兩種切入點，為什麼兩個都要做

**只改地區。** 把 `DeviceRegion` 設成 68，Windows 自己就會算出歐盟的答案。做法簡單 ——
但它會把歐盟的損失連同好處一起收下，而且沒得挑。這就是論壇上流傳的做法，也正是這個工具
v1 版做錯的地方。

**改寫策略檔。** 把每一條的 `defaultState` 設成你要的狀態，並刪掉 `conditions`。
結果就完全與地區無關，而且每一條都能分別決定。

v2 兩個都做。改寫策略檔是超集得以成立的原因；地區還是要設，因為有些歐盟行為不在這個檔案裡
（例如 Edge 自己的行為），它們直接去讀地區。

## 4. 那條規則

在 `src/EULord/Core/PolicyFile.cs`：

```csharp
internal static string Decide(PolicyDecision d)
{
    // 沒得選：愛爾蘭跟全世界的狀態一樣。
    if (!d.Differs) return d.GlobalState;

    // 從來沒分類過，就維持 Windows 出廠的樣子。
    if (d.Entry == null) return d.GlobalState;

    switch (d.Override)
    {
        case PolicyOverride.Eea:    return d.EeaState;
        case PolicyOverride.Global: return d.GlobalState;
        default:
            return PolicyDecision.AutoStateFor(d.Entry.Class, d.GlobalState, d.EeaState);
    }
}
```

而 `AutoStateFor` 就是三行的超集：

```csharp
case PolicyClass.Benefit: return eeaState;     // 好處：收下
case PolicyClass.Loss:    return globalState;  // 損失：拒收
default:                  return globalState;  // 見仁見智：交給使用者，預設不動
```

分類表在 `src/EULord/Core/Catalog.cs` —— 68 筆，每筆有穩定的 `Id`、分類，以及三種語言的說明。
`docs/POLICIES.md` 是由它產生的，所以文件不可能跟程式碼講不一樣的話。

有差異但**未分類**的策略會被**原樣保留**，並且在狀態列、日誌和 CI 警告裡列出來。
用猜的可能害使用者功能倒退，而那正是超集承諾唯一不能破的底線。

## 5. 每次改寫都從乾淨的原始檔重建

我們寫出去的檔案沒有地區條件。這同時也是辨識方法：還留著地區條件的是 Windows 給的，
沒有的就是我們寫的。

`C:\ProgramData\OneClickEULord\backups\` 裡保存兩份：

| 檔案 | 用途 |
|---|---|
| `…original.json` | 這台機器上**最早**看過的那一份。寫一次，永不覆蓋。 |
| `…pristine.json` | Windows **最新**提供的那一份。雜湊變了就更新。 |

每次改寫都從 pristine 重建，絕不在我們已經改過的檔案上疊加。沒有這一步的話，
第二次執行時就沒有地區條件可讀，歐盟狀態會變成未知，使用者的逐條覆寫也會無處可退回。
這也是讓重複執行結果完全相同的原因 —— 否則排程每次觸發都會去攪動一個系統檔案。

## 6. 更新之後怎麼留住

Windows 會在維護期間還原這個檔案，所以只改一次，下一個累積更新就會把它蓋回去。
排程工作 `OneClickEULord-Persist` 以 SYSTEM 身分執行，有三個觸發條件：

| 觸發 | 為什麼 |
|---|---|
| 使用者登入 | 涵蓋每一次重開機 |
| 每天一次 | 涵蓋中間被悄悄重設的情況 |
| `Microsoft-Windows-WindowsUpdateClient` 事件 19 | 「安裝成功」—— 更新後幾分鐘內補回，不必等到下次登入 |

它跑的是 `--reapply`：除非這台機器套用過超集、而且確實跑掉了，否則什麼都不做。
不裝服務、不裝驅動、不常駐。

排程狀態是直接讀 `System32\Tasks` 底下那份工作自己的 XML，而不是去解析 `schtasks` 的輸出 ——
後者會被在地化，在繁中機器上欄位名稱會變成中文。

## 7. UCPD，以及為什麼不停用驅動

Windows 11 的 UserChoice 保護驅動程式會依照寫入行程的**映像名稱**阻擋對 `DeviceRegion` 的寫入。
它的拒絕清單包含 `reg.exe`、`powershell.exe`、`cmd.exe`、`regedit.exe`、`rundll32.exe`、`mshta.exe`。

`EULord.exe` 不在那份清單上，所以一般的登錄寫入預期就會成功；v1 因為必須呼叫外部程式才被擋。
只有在讀回驗證發現沒寫進去時，才會啟用備援：把 `reg.exe` 複製成一個暫時的名字，
只寫這**一個**值，然後立刻刪掉複本。

驅動程式從頭到尾不會被停用、不會被卸載，系統其他地方也完全不碰。

## 8. 寫 System32 的檔案而不留痕跡

v1 對策略檔執行了 `takeown /f` 和 `icacls /grant *S-1-5-32-544:F`。結果這兩件事都是錯的：

1. **根本不需要。** 原廠的 DACL 本來就給 `BUILTIN\Administrators` 完全控制 ——
   `(A;;FA;;;BA)` —— 提升權限的行程直接寫就可以了。
2. **而且不可逆。** 擁有權事後要不回來：
   `icacls <file> /setowner "NT SERVICE\TrustedInstaller"` 會以錯誤 1307 失敗，
   *「此安全性識別碼無法被指派為這個物件的擁有者」*。所以 v1 **永久**改掉了一個
   Windows 系統檔案的擁有者和權限。

v2 是就地直接寫入，擁有者與 DACL 完全不動。萬一真的被拒絕，就啟用 `SeRestorePrivilege`
（備份軟體用的那個權限，系統管理員本來就持有），以 backup semantics 開啟檔案。
這只讓那一個控制代碼略過 DACL 檢查，不修改任何東西，兩條路都不會留下痕跡。

## 9. 編碼，每一條都是踩過的雷

| 規則 | 不遵守會怎樣 |
|---|---|
| 策略檔是**無 BOM** 的 UTF-8 | PowerShell 5.1 的 `Set-Content -Encoding UTF8` 會加 BOM；v1 把 BOM 寫進了系統檔案 |
| `.cs` 原始碼是**含 BOM** 的 UTF-8 | 否則 `csc.exe` 會用 ANSI 代碼頁解碼，在預設的繁中機器（cp950）上所有中文都變亂碼 |
| `.ps1` 用 CRLF | 曾經發佈過 LF 結尾的腳本，因為 `.gitattributes` 只在全新 checkout 時轉換 |
| 時間戳用 `InvariantCulture` | 繁中預設是民國曆，2026 被記成 `115` |
| 語言偵測優先看使用者的語言清單 | 有台機器 `Get-UICulture` 是 `en-US`，但第一偏好語言是 `zh-Hant-TW`，v1 就給中文使用者看了英文 |

前三條由 `tools/Check-Encoding.ps1` 在 CI 把關，後兩條有測試覆蓋。

## 10. 誠實的驗證

寫進設定不等於功能就會出現 —— 微軟是分批推送的。所以報告分兩層：

**已確定** —— 我們讀回來就能證明的：地區值、策略檔是不是我們改寫的版本、
計畫中每一條狀態是不是真的在磁碟上、排程有沒有註冊。這幾項只要有一項失敗，就是工具失敗了。

**觀察到的** —— 要 Windows 自己動作的：Store 有沒有回報自己可移除、Edge 的解除安裝項目允不允許移除。
這些本來就可能延遲，所以結果是「尚未生效」並附上可能原因，不會算失敗，也絕不會算成功。

量不到的就回報「無法判斷」，不用猜的。「關閉網頁搜尋結果」那個開關有沒有出現在設定裡，
Windows 並沒有提供給我們查，所以工具報告的是它背後的策略狀態，並且講清楚量到的是哪一個。

## 11. 卸載 App

只有一條規則，來自這個工具的使用者本人：**卸載 app 要走 app 自己的卸載程序，不然怕刪掉系統必要的組件。**

所以只有兩條路，兩條都是微軟自己的：

- **Appx／MSIX** —— `Remove-AppxPackage`，官方文件記載的移除 API。
- **已註冊的解除安裝程式** —— App 自己寫進 `Uninstall` 登錄機碼的那個命令，原樣執行。
  這台機器上 Edge 的是 `MsiExec.exe /X{…}`，而不是一般預期的 `setup.exe --uninstall` ——
  這正是為什麼要去讀它，而不是假設。

絕對不做的事：刪除屬於 App 的檔案或資料夾、動 WinSxS、拔系統元件、
強制移除沒有卸載程式的東西，或在卸載失敗後退回手動刪除。找不到合法途徑的就標示為不可卸並說明原因。

命令列**不提供**這個功能。移除軟體需要有人逐一確認，所以無人值守的執行永遠做不到。

## 12. 刻意不做的事

- 沒有遙測、沒有分析、不回傳任何東西。唯一的網路請求是檢查更新，而且只在你按下按鈕時。
- 不嘗試繞過網域或 MDM 原則。受管理的機器如果蓋掉我們寫的值，工具就據實回報然後停手。
- 不做通用的 App 管理器。只處理微軟自家的內建 App，因為歐盟策略講的就是這些。
- 不夾帶 runtime。.NET Framework 4.8 是 Windows 11 內建的，所以完整編譯出來只有約 165 KB。
