[繁體中文](../HOW-IT-WORKS.md) · **简体中文** · [English](../en/HOW-IT-WORKS.md)

# 工作原理

## 1. Windows 如何判断“你在不在 EEA”

区域策略由 `SystemSettings.DataModel.dll` 评估，流程大致是：

1. 读取注册表值 `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion`（DWORD，保存 GeoID）。
2. 读不到时调用 `GetUserGeoID(GEOCLASS_NATION)`，再把结果**缓存**回上面这个注册表值。
3. 用 `GetGeoInfoW` 把 GeoID 转成两位国家代码（如 `IE`、`DE`）。
4. 读取 `C:\Windows\System32\IntegratedServicesRegionPolicySet.json`，对每条策略：命中区域条件就用该条件的状态，否则用 `defaultState`。

所以“变成欧盟大老爷”有两个切入点：改**区域缓存**（让第 1～3 步看到 EEA），或改**策略文件**（让第 4 步直接输出 EEA 状态）。本工具两者都做。

## 2. 温和模式

只把 `DeviceRegion` 写成一个 EEA 国家的 GeoID（默认爱尔兰 = 68 / `0x44`），让 Windows **自己**把 84 条策略算成 EEA 状态。只改一个值、干净、易恢复。值本来就正确时，工具不会重写它。

## 3. 彻底模式

在温和模式之上直接改写策略文件。对**每一条**策略：

- 算出它在 EEA（以 `IE` 为基准）会是什么状态；
- 把 `defaultState` 设为该状态，并**删除 region 条件**。

这样无论 `DeviceRegion` 是什么，策略都输出 EEA 状态。转换是**通用、不绑定 GUID** 的，微软新增策略时规则依然适用。

细节：

- 原始文件先备份到 `C:\ProgramData\OneClickEUOverlord\backups\IntegratedServicesRegionPolicySet.original.json`。重复应用时**保留第一份**备份，不会被改写过的版本覆盖。
- 系统原文件是**不带 BOM 的 UTF-8**，工具写回时也保持无 BOM（Windows PowerShell 5.1 的 `Set-Content -Encoding UTF8` 会自动加 BOM，所以改用 .NET 直接写入）。

## 4. 绕过 UCPD（只针对 DeviceRegion 这一个值）

Windows 11 的 **UserChoice Protection Driver（UCPD）** 按映像名称匹配，阻止 `reg.exe`、`powershell.exe`、`cmd.exe`、`regedit.exe` 等工具写入受保护的键（包括 `DeviceRegion`）。

`Set-DeviceRegionValue` 的做法：

1. 先用常规 API 写入，再**读回验证**；
2. 如果值没变（被 UCPD 拦截），把 `reg.exe` 复制到 `%TEMP%` 并**重命名**，用它写入这一个值，然后**删除副本**。

此方法不会禁用整台电脑的 UCPD。如果环境特殊（MDM 管理、UCPD 规则更严），可能需要在安全模式下应用，或在初始设置（OOBE）时直接选择 EEA 国家。

## 5. 持久化

计划任务 `OneClickEUOverlord-Persist`（SYSTEM 权限）：

- 触发：**每次登录**＋**每天 03:00**；
- 操作：以 `-Action Reapply` 读取 `state.json`，重新应用你选的模式和地区。

脚本会复制到 `C:\ProgramData\OneClickEUOverlord\EUOverlord.ps1`，所以删除下载的文件夹后持久化仍然有效。

## 6. 多语言

- 界面文字：繁體中文、简体中文、English。`-Language auto` 会先看你在 Windows“语言”设置中**排第一位**的语言，再看显示语言（有些电脑的显示语言仍是英文，但用户把中文排在第一）。
- 控制台改用 UTF-8 输出，简体字在默认繁中（cp950）控制台上也能正常显示。
- 日志一律用**英文**，时间用公历（避免繁中系统显示民国纪年），方便直接附在问题反馈中。
- `src\EUOverlord.ps1` 必须保存为**带 BOM 的 UTF-8**：Windows PowerShell 5.1 读取无 BOM 文件时会使用系统 ANSI 代码页，中文会变成乱码。测试和发布流程都会检查。

## 7. 文件与数据位置

```
C:\ProgramData\OneClickEUOverlord\
├─ EUOverlord.ps1                     ← 供计划任务使用的脚本副本
├─ state.json                         ← 当前模式／地区／原来地区
├─ backups\
│  └─ IntegratedServicesRegionPolicySet.original.json
└─ logs\
   └─ euoverlord.log
```

“恢复”从备份还原；“完全卸载”（`-FullClean`）整个删除。本工具不会在系统其他位置散落文件。

### 从 v1.0.x 迁移

v1.0.x 名为 One-Click EU Grandpa，数据在 `C:\ProgramData\OneClickEUGrandpa\`，计划任务名为 `OneClickEUGrandpa-Persist`。
新版在 `Apply` / `Restore` / `Uninstall` 之前会：

1. 删除旧计划任务；
2. 把旧文件夹整个移到新位置（保留原始备份和原来地区），并删除旧脚本副本；
3. 如果新旧文件夹同时存在，保留新的、不动旧的，并在日志中说明。

`Status` 保持只读，只会提示检测到旧版。

## 8. 参考来源

- Rafael Rivera — *Device Region and Integrated Services Region Policy in Windows*
- Pureinfotech、gHacks、Winaero、XDA — IntegratedServicesRegionPolicySet.json 介绍
- Christoph Kolbicz、Binary Ninja — UCPD 驱动分析
- Microsoft EU Policy Blog、Windows Insider Blog — DMA 合规变更

> 以上多为第三方和社区分析；微软没有把编辑此文件列为受支持的配置。实际行为以你电脑上的版本为准。
