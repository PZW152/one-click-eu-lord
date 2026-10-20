[繁體中文](../RELEASING.md) · **简体中文** · [English](../en/RELEASING.md)

# 发布流程

发布全自动，由 [`.github/workflows/release.yml`](../../.github/workflows/release.yml) 负责。

## 发布新版本

1. 在 `CHANGELOG.md` 最上方加上 `## vX.Y.Z — 日期` 段落（三种语言）。**没有这段，发布会失败。**
2. 打 tag 并推送：

   ```bash
   git tag -a v1.2.3 -m "v1.2.3"
   git push origin v1.2.3
   ```

推送 tag 后，GitHub Actions 会：

1. 验证 tag 格式（`^v\d+\.\d+\.\d+$`）；
2. 在 Windows 上，用 Windows PowerShell 5.1 和 PowerShell 7 对**该 tag 的代码**运行 `tests\Test-Engine.ps1`；**测试不通过就不发布**；
3. 打包给用户的内容（`src/`、`launchers/`、`docs/`、`tools/`、三份 README、`CHANGELOG.md`、`LICENSE`、`SECURITY.md`），不含 `.git`、CI 配置和测试；
4. 把 `.cmd` / `.bat` / `.ps1` 强制转为 **CRLF**，并检查每个 `.ps1` 都以 **UTF-8 BOM** 开头；
5. 用 `CHANGELOG.md` 中对应版本的段落作为说明，创建 `one-click-eu-overlord-vX.Y.Z.zip` 和 GitHub Release。

任何一步失败，整个发布就停止，不会留下半成品。

## 重建已有版本的附件

Actions → **Release** → *Run workflow*，输入已有的 tag。Release 已存在时只替换 zip，不改说明文字。
只支持 v1.1.0 之后的 tag；更早的版本是旧的目录结构和名称。

## 更新策略对照表

Windows 新版本增减策略时，在该电脑上运行：

```powershell
powershell -NoProfile -File tools\New-FeatureTable.ps1 > rows.md
```

把生成的行粘贴到 `docs/FEATURES.md`、`docs/zh-CN/FEATURES.md`、`docs/en/FEATURES.md` 的表格中，并更新条数。

## 安全设计

- 默认权限只有 `contents: read`；只有发布那一步有 `contents: write`。
- 只用官方 `actions/checkout`，固定在完整 commit SHA（v4.3.0）；其余全部使用 runner 自带工具，没有第三方 Action。
- tag 名称通过环境变量传入并先验证，不直接插入脚本，避免注入。
