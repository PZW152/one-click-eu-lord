[繁體中文](README.md) · **简体中文** · [English](README.en.md)

# 一键转生欧盟爷

让你的 Windows 11 享有欧盟用户（欧盟大老爷）才有的待遇：**可以卸载 Edge、关掉 Bing 搜索、少看一堆广告推销**。之后系统更新也不会把它改回去。

## 三步就好

1. **[⬇ 下载 EU-Overlord.cmd](https://github.com/PZW152/one-click-eu-overlord/releases/latest/download/EU-Overlord.cmd)**
2. **双击**它，Windows 问是否允许时点“**是**”
3. **直接按 Enter**，然后重启

想变回原样？再双击一次，输入 **3**。

> **弹出警告怎么办？** 这个文件没有花钱买数字签名，所以浏览器和 Windows 会提醒你：
> - 浏览器说文件可能有害 → 选“**保留**”（Edge 要先点“…”）
> - 蓝色的“Windows 已保护你的电脑”→ 点“**更多信息**”→“**仍要运行**”
> - “打开文件 - 安全警告”→ 点“**运行**”
>
> 它是纯文本文件，任何人都能用记事本打开检查；源代码就在 [src/EUOverlord.ps1](src/EUOverlord.ps1)。

---

## 转生后有什么好处

- ✅ 可以**卸载 Microsoft Edge**，其他程序也不会一直让你装回来
- ✅ 可以**卸载 Microsoft Store** 和部分内置应用
- ✅ 可以**关掉 Windows 搜索中的 Bing 网页结果**
- ✅ **小组件**可以关掉新闻和广告信息流
- ✅ 换成别的浏览器当默认时，**所有链接类型一起换**
- ✅ **少很多推销**：全屏广告页、硬塞 Edge 的活动都会消失

工具只是把这些“选择权”打开，**不会**替你删除任何东西——要不要真的卸载 Edge，由你决定。

<details>
<summary><b>菜单里的 1、2、3、4 有什么区别？</b></summary>

| 选项 | 做什么 | 适合谁 |
|------|--------|--------|
| **1 一键转生**（推荐） | 只把 Windows 认定的“设备区域”改成爱尔兰，让 Windows 自己开放欧盟功能 | 所有人 |
| **2 彻底转生** | 另外改写系统的区域策略文件，即使区域被改回去也照样有效 | 选 1 之后功能还是没出现的人 |
| **3 恢复** | 把区域和策略文件恢复原状、移除自动保护（保留备份） | 想变回去的人 |
| **4 完全移除** | 恢复，并删除本工具留在电脑上的所有数据 | 不想留任何痕迹的人 |

1 和 2 都会安装一个“自动保护”计划任务：每次登录和每天检查一次，系统更新把设置改回去时自动补回。
</details>

<details>
<summary><b>它到底改了我电脑的什么？安全吗？</b></summary>

- **一个注册表值**：`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion`（Windows 判断你在哪个国家）。
- **一个系统文件**（只有选 2 才改）：`C:\Windows\System32\IntegratedServicesRegionPolicySet.json`，84 条区域功能开关。
- **一个计划任务**：`OneClickEUOverlord-Persist`。
- **一个文件夹**：`C:\ProgramData\OneClickEUOverlord\`，存放原始文件备份、状态和日志。

动手前都会先备份，选 3 或 4 就能完整恢复。不收集、不上传任何数据。
Windows 11 的 UCPD 防护会拦截修改区域的写入，工具只针对这**一个值**绕过，不会关闭整台电脑的防护。

这**不是**微软支持的配置，请自行承担风险。详细说明见[工作原理](docs/zh-CN/HOW-IT-WORKS.md)和[安全性与免责声明](SECURITY.md)。
</details>

<details>
<summary><b>选了 1，重启后还是不能卸载 Edge？</b></summary>

- 微软会分批推送功能，有些电脑要等几天或下一次更新。
- 可以再双击一次改选 **2 彻底转生**。
- 公司电脑如果由 IT 管理（MDM／域），设置可能被公司策略覆盖，请联系 IT。
</details>

<details>
<summary><b>命令行用法（进阶）</b></summary>

`EU-Overlord.cmd` 和 `src\EUOverlord.ps1` 接受相同参数；不带参数就是菜单。以管理员身份运行：

```powershell
.\EU-Overlord.cmd -Action Apply -Mode Hardcore -Region Germany
.\EU-Overlord.cmd -Action Status -Language zh-CN
.\EU-Overlord.cmd -Action Restore
.\EU-Overlord.cmd -Action Uninstall -FullClean -Force
```

| 参数 | 可用值 |
|------|--------|
| `-Action` | `Menu`（默认）/ `Apply` / `Restore` / `Status` / `Uninstall`（`Reapply` 供计划任务使用） |
| `-Mode` | `Gentle`（默认）/ `Hardcore` |
| `-Region` | `Ireland`（默认）/ `Germany` / `France` / `Netherlands` / `Spain` / `Italy` / `Sweden` / `Poland` / `Finland` / `Austria` |
| `-Language` | `auto`（默认，按你在 Windows 语言设置中排第一的语言）/ `zh-TW` / `zh-CN` / `en` |
| `-FullClean` | 配合 `Uninstall`：一并删除备份和日志 |
| `-Force` | 跳过彻底模式和完全清除的确认提问 |
</details>

<details>
<summary><b>从旧版（One-Click EU Grandpa）升级</b></summary>

直接用新版选 1～4 任一项即可。工具会把 `C:\ProgramData\OneClickEUGrandpa\` 移到新位置、保留原来的备份，并替换旧的计划任务。
</details>

## 更多文档

[策略对照表](docs/zh-CN/FEATURES.md) · [工作原理](docs/zh-CN/HOW-IT-WORKS.md) · [发布流程](docs/zh-CN/RELEASING.md) · [更新日志](CHANGELOG.md) · [安全性与免责声明](SECURITY.md)

开发者：`powershell -NoProfile -ExecutionPolicy Bypass -File tests\Test-Engine.ps1` 运行测试（无需管理员权限、不会改动系统）。

许可证：[MIT](LICENSE)，不提供任何担保。
