**繁體中文** · [简体中文](zh-CN/RELEASING.md) · [English](en/RELEASING.md)

# 發佈流程

發佈全自動，由 [`.github/workflows/release.yml`](../.github/workflows/release.yml) 負責。使用者只會下載一個檔案：`EU-Overlord.cmd`。

## 發新版

1. 把 `src\EUOverlord.ps1` 裡的 `$Script:Version` 改成新版本號（例如 `'1.2.3'`）。
2. 在 `CHANGELOG.md` 最上方加上 `## v1.2.3 — 日期` 段落（三種語言）。
3. 打 tag 並推送：

   ```bash
   git tag -a v1.2.3 -m "v1.2.3"
   git push origin v1.2.3
   ```

推送 tag 後，GitHub Actions 會在 Windows 上：

1. 驗證 tag 格式（`^v\d+\.\d+\.\d+$`）；
2. 以 Windows PowerShell 5.1 與 PowerShell 7 對**該 tag 的程式碼**執行 `tests\Test-Engine.ps1`（測試會順便建出單一檔案並實際執行一次）；
3. 確認 `$Script:Version` 和 tag 一致；
4. 用 [`tools/Build-SingleFile.ps1`](../tools/Build-SingleFile.ps1) 建出 `EU-Overlord.cmd`；
5. 以 `CHANGELOG.md` 對應段落當說明，建立 GitHub Release 並上傳 `EU-Overlord.cmd`。

任一步驟失敗就停止，不會發出半成品。附件名稱永遠是 `EU-Overlord.cmd`，所以這個連結永遠指向最新版：
`https://github.com/PZW152/one-click-eu-overlord/releases/latest/download/EU-Overlord.cmd`

## 重建既有版本的附件

Actions → **Release** → *Run workflow*，輸入既有的 tag（v1.2.0 以後）。Release 已存在時只替換附件，不動說明文字。

## 本機建置

```powershell
powershell -NoProfile -File tools\Build-SingleFile.ps1   # 輸出到 dist\EU-Overlord.cmd
```

## 更新策略對照表

Windows 新組建增減策略時，在該機器上執行 `tools\New-FeatureTable.ps1 > rows.md`，把產生的列貼進三份 `FEATURES.md` 並更新條數。

## 安全設計

- 預設權限只有 `contents: read`；只有發佈 job 有 `contents: write`，而且 token 只交給最後的發佈步驟。
- 只用官方 `actions/checkout`，釘在完整 commit SHA（v4.3.0）；沒有第三方 Action。
- tag 名稱經由環境變數傳入並先驗證，不直接插進腳本。
