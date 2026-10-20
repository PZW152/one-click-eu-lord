**繁體中文** · [简体中文](README.zh-CN.md) · [English](README.en.md)

# 一鍵轉生歐盟爺

讓你的 Windows 11 享有歐盟用戶（歐盟大老爺）才有的待遇：**可以解除安裝 Edge、關掉 Bing 搜尋、少看一堆廣告推銷**。之後系統更新也不會把它改回去。

## 三步就好

1. **[⬇ 下載 EU-Overlord.cmd](https://github.com/PZW152/one-click-eu-overlord/releases/latest/download/EU-Overlord.cmd)**
2. **雙擊**它，Windows 問要不要允許時按「**是**」
3. **直接按 Enter**，再重新開機

想變回原樣？再雙擊一次，輸入 **3**。

> **跳出警告怎麼辦？** 這個檔案沒有花錢買數位簽章，所以瀏覽器和 Windows 會提醒你：
> - 瀏覽器說檔案可能有害 → 選「**保留**」（Edge 要先點「…」）
> - 藍色的「Windows 已保護您的電腦」→ 按「**其他資訊**」→「**仍要執行**」
> - 「開啟檔案 - 安全性警告」→ 按「**執行**」
>
> 它是純文字檔，任何人都能用記事本打開檢查；原始碼就在 [src/EUOverlord.ps1](src/EUOverlord.ps1)。

---

## 轉生後有什麼好處

- ✅ 可以**解除安裝 Microsoft Edge**，其他程式也不會一直叫你裝回來
- ✅ 可以**解除安裝 Microsoft Store** 和部分內建 App
- ✅ 可以**關掉 Windows 搜尋裡的 Bing 網頁結果**
- ✅ **小工具**可以關掉新聞和廣告資訊流
- ✅ 換成別的瀏覽器當預設時，**所有連結類型一起換**
- ✅ **少很多推銷**：全螢幕廣告頁、硬塞 Edge 的活動都會消失

工具只是把這些「選擇權」打開，**不會**替你刪任何東西——要不要真的卸 Edge，你自己決定。

<details>
<summary><b>選單裡的 1、2、3、4 差在哪？</b></summary>

| 選項 | 做什麼 | 適合誰 |
|------|--------|--------|
| **1 一鍵轉生**（推薦） | 只把 Windows 認定的「裝置地區」改成愛爾蘭，讓 Windows 自己開放歐盟功能 | 所有人 |
| **2 徹底轉生** | 另外改寫系統的地區策略檔，就算地區被改回去也照樣有效 | 1 之後功能還是沒出現的人 |
| **3 還原** | 把地區和策略檔恢復原狀、移除自動保護（備份保留） | 想變回去的人 |
| **4 完全移除** | 還原，並刪除本工具留在電腦上的所有資料 | 不想留任何痕跡的人 |

1 和 2 都會安裝一個「自動保護」排程：每次登入和每天檢查一次，系統更新把設定改回去時自動補回。
</details>

<details>
<summary><b>它到底改了我電腦的什麼？安全嗎？</b></summary>

- **一個登錄值**：`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion`（Windows 判斷你在哪個國家）。
- **一個系統檔**（只有選 2 才改）：`C:\Windows\System32\IntegratedServicesRegionPolicySet.json`，84 條地區功能開關。
- **一個排程工作**：`OneClickEUOverlord-Persist`。
- **一個資料夾**：`C:\ProgramData\OneClickEUOverlord\`，放原始檔備份、狀態和日誌。

動手前都會先備份，選 3 或 4 就能完整復原。不蒐集、不上傳任何資料。
Windows 11 的 UCPD 防護會擋住修改地區的寫入，工具只針對這**一個值**繞過，不會關掉整台電腦的防護。

這**不是**微軟支援的設定，請自負風險。詳細說明見[運作原理](docs/HOW-IT-WORKS.md)和[安全性與免責聲明](SECURITY.md)。
</details>

<details>
<summary><b>選了 1，重開機後還是不能卸 Edge？</b></summary>

- 微軟會分批推送功能，有些電腦要等幾天或下一次更新。
- 可以再雙擊一次改選 **2 徹底轉生**。
- 公司電腦若由 IT 管理（MDM／網域），設定可能被公司原則蓋掉，請洽 IT。
</details>

<details>
<summary><b>命令列用法（進階）</b></summary>

`EU-Overlord.cmd` 和 `src\EUOverlord.ps1` 接受相同參數；不給參數就是選單。以系統管理員身分執行：

```powershell
.\EU-Overlord.cmd -Action Apply -Mode Hardcore -Region Germany
.\EU-Overlord.cmd -Action Status -Language zh-TW
.\EU-Overlord.cmd -Action Restore
.\EU-Overlord.cmd -Action Uninstall -FullClean -Force
```

| 參數 | 可用值 |
|------|--------|
| `-Action` | `Menu`（預設）/ `Apply` / `Restore` / `Status` / `Uninstall`（`Reapply` 供排程使用） |
| `-Mode` | `Gentle`（預設）/ `Hardcore` |
| `-Region` | `Ireland`（預設）/ `Germany` / `France` / `Netherlands` / `Spain` / `Italy` / `Sweden` / `Poland` / `Finland` / `Austria` |
| `-Language` | `auto`（預設，依你在 Windows 語言設定排第一的語言）/ `zh-TW` / `zh-CN` / `en` |
| `-FullClean` | 搭配 `Uninstall`：一併刪除備份與日誌 |
| `-Force` | 略過徹底模式與完整清除的確認提問 |
</details>

<details>
<summary><b>從舊版（One-Click EU Grandpa）升級</b></summary>

直接用新版選 1～4 任一項即可。工具會把 `C:\ProgramData\OneClickEUGrandpa\` 搬到新位置、保留原本的備份，並換掉舊的排程工作。
</details>

## 更多文件

[策略對照表](docs/FEATURES.md) · [運作原理](docs/HOW-IT-WORKS.md) · [發佈流程](docs/RELEASING.md) · [更新記錄](CHANGELOG.md) · [安全性與免責聲明](SECURITY.md)

開發者：`powershell -NoProfile -ExecutionPolicy Bypass -File tests\Test-Engine.ps1` 執行測試（不需系統管理員權限、不會動到系統）。

授權：[MIT](LICENSE)，無任何擔保。
