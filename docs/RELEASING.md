# 發佈流程 / Releasing

發佈是全自動的，由 [`.github/workflows/release.yml`](../.github/workflows/release.yml) 負責。

## 發新版

```bash
git tag v1.2.3          # 必須是 vMAJOR.MINOR.PATCH
git push origin v1.2.3
```

推送 tag 後，GitHub Actions 會：

1. 驗證 tag 格式（`^v\d+\.\d+\.\d+$`，其他格式直接拒絕）；
2. 檢出該 tag，只打包要出貨的內容（`src/`、`launchers/`、`docs/`、`README.md`、`LICENSE`、`SECURITY.md`），不含 `.git` 與 CI 設定；
3. 把 `.cmd` / `.bat` / `.ps1` 強制轉成 **CRLF** 並逐檔驗證——Windows 雙擊執行批次檔需要它；
4. 驗證 `src/EUGrandpa.ps1` 以 **UTF-8 BOM** 開頭——繁中等非 UTF-8 預設碼頁的 Windows PowerShell 5.1 靠它才能正確讀中文；
5. 建立 `one-click-eu-grandpa-vX.Y.Z.zip`，並建立（或刷新）對應的 GitHub Release，說明文字由 GitHub 自動產生。

任一檢查失敗，整個發佈就會失敗，不會留下半成品。

## 重建既有版本的附件

到 Actions → **Release** → *Run workflow*，輸入既有的 tag（例如 `v1.0.0`）。
若該 Release 已存在，只會替換 zip 附件，不會動說明文字。

## 設計重點

- 權限只有 `contents: write`。
- 只使用官方 `actions/checkout`，並釘在完整 commit SHA（v4.3.0）；其餘全用 runner 內建的 `gh` 與 `zip`，沒有第三方 Action。
- tag 名稱經由環境變數傳入，不直接插進 shell 腳本，避免注入。
