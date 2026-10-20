[繁體中文](../RELEASING.md) · **简体中文** · [English](../en/RELEASING.md)

# 发布流程

发布全自动，由 [`.github/workflows/release.yml`](../../.github/workflows/release.yml) 负责。用户只会下载一个文件：`EU-Overlord.cmd`。

## 发布新版本

1. 把 `src\EUOverlord.ps1` 中的 `$Script:Version` 改成新版本号（例如 `'1.2.3'`）。
2. 在 `CHANGELOG.md` 最上方加上 `## v1.2.3 — 日期` 段落（三种语言）。
3. 打 tag 并推送：

   ```bash
   git tag -a v1.2.3 -m "v1.2.3"
   git push origin v1.2.3
   ```

推送 tag 后，GitHub Actions 会在 Windows 上：

1. 验证 tag 格式（`^v\d+\.\d+\.\d+$`）；
2. 用 Windows PowerShell 5.1 和 PowerShell 7 对**该 tag 的代码**运行 `tests\Test-Engine.ps1`（测试会顺便构建单文件并实际运行一次）；
3. 确认 `$Script:Version` 与 tag 一致；
4. 用 [`tools/Build-SingleFile.ps1`](../../tools/Build-SingleFile.ps1) 构建 `EU-Overlord.cmd`；
5. 以 `CHANGELOG.md` 中对应段落作为说明，创建 GitHub Release 并上传 `EU-Overlord.cmd`。

任何一步失败就停止，不会发出半成品。附件名称始终是 `EU-Overlord.cmd`，所以这个链接始终指向最新版：
`https://github.com/PZW152/one-click-eu-overlord/releases/latest/download/EU-Overlord.cmd`

## 重建已有版本的附件

Actions → **Release** → *Run workflow*，输入已有的 tag（v1.2.0 以后）。Release 已存在时只替换附件，不改说明文字。

## 本地构建

```powershell
powershell -NoProfile -File tools\Build-SingleFile.ps1   # 输出到 dist\EU-Overlord.cmd
```

## 更新策略对照表

Windows 新版本增减策略时，在该电脑上运行 `tools\New-FeatureTable.ps1 > rows.md`，把生成的行粘贴到三份 `FEATURES.md` 中并更新条数。

## 安全设计

- 默认权限只有 `contents: read`；只有发布 job 有 `contents: write`，而且 token 只交给最后的发布步骤。
- 只用官方 `actions/checkout`，固定在完整 commit SHA（v4.3.0）；没有第三方 Action。
- tag 名称通过环境变量传入并先验证，不直接插入脚本。
