**繁體中文** · [简体中文](zh-CN/RELEASING.md) · [English](en/RELEASING.md)

# 發佈流程

發佈全自動，由 [`.github/workflows/release.yml`](../.github/workflows/release.yml) 負責。

## 發新版

1. 在 `CHANGELOG.md` 最上方加上 `## vX.Y.Z — 日期` 段落（三種語言）。**沒有這段，發佈會失敗。**
2. 打 tag 並推送：

   ```bash
   git tag -a v1.2.3 -m "v1.2.3"
   git push origin v1.2.3
   ```

推送 tag 後，GitHub Actions 會：

1. 驗證 tag 格式（`^v\d+\.\d+\.\d+$`）；
2. 在 Windows 上，以 Windows PowerShell 5.1 與 PowerShell 7 對**該 tag 的程式碼**執行 `tests\Test-Engine.ps1`；**測試沒過就不發佈**；
3. 打包要給使用者的內容（`src/`、`launchers/`、`docs/`、`tools/`、三份 README、`CHANGELOG.md`、`LICENSE`、`SECURITY.md`），不含 `.git`、CI 設定與測試；
4. 把 `.cmd` / `.bat` / `.ps1` 強制轉成 **CRLF**，並檢查每個 `.ps1` 都以 **UTF-8 BOM** 開頭；
5. 用 `CHANGELOG.md` 裡對應版本的段落當作說明，建立 `one-click-eu-overlord-vX.Y.Z.zip` 與 GitHub Release。

任一步驟失敗，整個發佈就停止，不會留下半成品。

## 重建既有版本的附件

Actions → **Release** → *Run workflow*，輸入既有的 tag。Release 已存在時只替換 zip，不動說明文字。
只支援 v1.1.0 之後的 tag；更早的版本是舊的目錄結構與名稱。

## 更新策略對照表

Windows 新組建增減策略時，在該機器上執行：

```powershell
powershell -NoProfile -File tools\New-FeatureTable.ps1 > rows.md
```

把產生的列貼進 `docs/FEATURES.md`、`docs/zh-CN/FEATURES.md`、`docs/en/FEATURES.md` 的表格，並更新條數。

## 安全設計

- 預設權限只有 `contents: read`；只有發佈那一步有 `contents: write`。
- 只用官方 `actions/checkout`，釘在完整 commit SHA（v4.3.0）；其餘全用 runner 內建工具，沒有第三方 Action。
- tag 名稱經由環境變數傳入並先驗證，不直接插進腳本，避免注入。
