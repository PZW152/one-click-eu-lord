[繁體中文](README.md) · **简体中文** · [English](README.en.md)

# 一键转生欧盟爷 · One-Click EU Overlord

> “欧盟爷”就是**欧盟大老爷**——在 Windows 眼里，住在欧盟的用户就是得罪不起的大老爷：
> 想卸 Edge 就卸、想关 Bing 就关、广告推销少一大半。
>
> 这个工具让任何一台装好的 Windows 11，一键获得“欧洲经济区（EEA）＋ DMA（数字市场法）”版本的**全部用户福利**，
> 并且**不会因为系统更新或重启被悄悄刷回去**。

---

## 欧盟大老爷有什么好处

微软为了遵守欧盟《数字市场法》（DMA），在**欧洲经济区（EEA）**的 Windows 11 上开放了一整套对用户更友好的行为，例如：

- ✅ **可以卸载 Microsoft Edge**，卸载后其他程序也不会一直让你装回来
- ✅ **可以卸载 Microsoft Store 和部分系统应用**
- ✅ **可以关闭 Windows 搜索中的 Bing／网页搜索**
- ✅ **第三方搜索提供商**可以出现在系统搜索中
- ✅ **小组件（Widgets）**可以关闭 Microsoft News 和广告信息流，并限制数据共享
- ✅ **设置非 Edge 的默认浏览器**时，连 `.svg / .xml / .mht / ftp` 等类型一起接管
- ✅ **大幅减少推广、全屏推销页、把 Edge 塞给你的营销活动**

这些开关由两样东西决定：

```
C:\Windows\System32\IntegratedServicesRegionPolicySet.json         ← 84 条区域策略
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion   ← Windows 认定的设备区域
```

**一键转生欧盟爷**把你的电脑切换到 EEA 状态，拿到上面**整套福利的超集**，再用计划任务确保更新后自动补回。

> **关于“超集”**：本工具采用与 EEA **完全一致**的策略集——那是微软官方定义、彼此协调的一整套。
> 凡是 EEA 版“给你选择权”的开关都会打开；工具**不会**替你把功能强行关掉，
> 要不要真的卸 Edge、关 Bing，由你决定。每条策略的对照见[策略对照表](docs/zh-CN/FEATURES.md)。

---

## 两种一键模式

| 模式 | 启动文件 | 做什么 | 侵入性 | 更新后是否保留 |
|------|----------|--------|--------|----------------|
| **温和**（建议先用） | `1-Apply-Gentle.cmd` | 只把 `DeviceRegion` 改成 EEA 国家（默认爱尔兰），让 **Windows 自己**算出 EEA 状态 | 极低，只改一个注册表值 | 由持久化计划任务保证 |
| **彻底** | `2-Apply-Hardcore.cmd` | 温和模式 **＋** 改写策略文件，让每条策略对**所有地区**都是 EEA 状态 | 中，改写一个受保护的系统文件（先备份） | 即使区域被重置也仍然生效 |

两种模式都会：

1. 先把原始注册表值和原始策略文件**备份**到 `C:\ProgramData\OneClickEUOverlord\backups\`；
2. 安装 **SYSTEM 权限的计划任务**，在**每次登录＋每天**自动重新应用；
3. 把状态记录在 `state.json`，让“恢复”能准确还原。

> **关于 UCPD**：Windows 11 的 UCPD 驱动会拦截 `reg.exe` / `powershell.exe` 对 `DeviceRegion` 的写入。
> 本工具先正常写入并读回验证，若被拦截才用临时目录中**重命名的 `reg.exe` 副本**写入这**一个值**，用完立即删除。
> 它**不会**禁用整台电脑的 UCPD 安全驱动。

---

## 使用方法

1. 到 [Releases](https://github.com/PZW152/one-click-eu-overlord/releases/latest) 下载最新的 `one-click-eu-overlord-vX.Y.Z.zip` 并解压。
2. 打开 `launchers\` 文件夹，双击要用的启动文件：

   | 启动文件 | 功能 |
   |----------|------|
   | `1-Apply-Gentle.cmd` | 一键温和模式 |
   | `2-Apply-Hardcore.cmd` | 一键彻底模式 |
   | `3-Restore.cmd` | 恢复区域和策略文件、删除计划任务（保留备份） |
   | `4-Status.cmd` | 查看当前状态 |
   | `5-Uninstall.cmd` | 恢复并删除本工具的所有数据 |

3. 弹出 UAC 时点“是”（需要管理员权限）。
4. **注销或重启**让效果完全生效。
5. 之后要卸载 Edge：“设置 → 应用 → 安装的应用 → Microsoft Edge → 卸载”。

界面文字会根据你在“设置 → 时间和语言 → 语言和区域”中排在**第一位**的语言，自动选择**繁體中文、简体中文或 English**。

### 命令行参数

```powershell
# 以管理员身份打开 PowerShell
.\src\EUOverlord.ps1 -Action Apply -Mode Hardcore -Region Germany
.\src\EUOverlord.ps1 -Action Status -Language zh-CN
.\src\EUOverlord.ps1 -Action Restore
.\src\EUOverlord.ps1 -Action Uninstall -FullClean -Force
```

| 参数 | 可用值 |
|------|--------|
| `-Action` | `Apply` / `Restore` / `Status` / `Uninstall`（`Reapply` 供计划任务使用） |
| `-Mode` | `Gentle`（默认）/ `Hardcore` |
| `-Region` | `Ireland`（默认）/ `Germany` / `France` / `Netherlands` / `Spain` / `Italy` / `Sweden` / `Poland` / `Finland` / `Austria` |
| `-Language` | `auto`（默认）/ `zh-TW` / `zh-CN` / `en` |
| `-FullClean` | 配合 `Uninstall`：一并删除备份和日志 |
| `-Force` | 跳过彻底模式和完全清除的确认提问 |

### 从旧版（One-Click EU Grandpa）升级

直接运行一次新版的“应用”即可。工具会自动把 `C:\ProgramData\OneClickEUGrandpa\` 搬到新位置、保留原来的备份，并替换旧的计划任务。

---

## 风险与注意事项

- 这**不是** Microsoft 支持的配置，属于对你自己电脑的区域／功能自定义。请自行承担风险。
- 建议第一次先用**温和模式**。彻底模式会改写系统文件（已备份、可恢复）。
- 有反馈指出部分版本（如 25H2）的累积更新会频繁还原策略文件——这正是本工具安装持久化计划任务的原因。
- 如果 `DeviceRegion` 由公司 MDM／域策略管理，可能需要在安全模式下操作，或交给管理员处理。
- 微软以分批 A/B 方式推送功能，切换到 EEA 也不保证每一项立即出现。
- 本工具不收集、不上传任何数据；所有操作都记录在 `C:\ProgramData\OneClickEUOverlord\logs\euoverlord.log`（英文，方便反馈问题）。

## 更多文档

- [策略对照表](docs/zh-CN/FEATURES.md) — 84 条策略的全球默认与 EEA 状态
- [工作原理](docs/zh-CN/HOW-IT-WORKS.md) — 区域判断、两种模式、UCPD、持久化、文件位置
- [发布流程](docs/zh-CN/RELEASING.md) — 给维护者
- [更新日志](CHANGELOG.md) · [安全性与免责声明](SECURITY.md)

## 开发与测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\Test-Engine.ps1
```

测试不需要管理员权限，也不会改动系统：所有路径都指向临时沙盒。GitHub Actions 会在每次推送时在 Windows 上运行。

## 许可证

[MIT](LICENSE)，不提供任何担保。
