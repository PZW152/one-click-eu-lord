# 原理

[繁體中文](HOW-IT-WORKS.md) · **简体中文** · [English](HOW-IT-WORKS.en.md)

以下全部在 Windows 11 build 26300 上实机验证。别的版本可能附不一样的策略文件；
工具读的是你机器上那一份，不是凭记忆假设。

---

## 1. 决定欧盟待遇的是两样东西

```
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion
    一个 DWORD，存 GeoID。爱尔兰是 68。

C:\Windows\System32\IntegratedServicesRegionPolicySet.json
    策略集本体。build 26300 上有 84 条、79 个唯一 GUID。
```

JSON 长这样：

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

这一条的意思是：**全世界关，但这 36 个国家开。**

## 2. Windows 怎么判读一条策略

`SystemSettings.DataModel.dll` 大致做这几件事：

1. 读 `DeviceRegion`。
2. 读不到就调用 `GetUserGeoID(GEOCLASS_NATION)`，并把答案**写回** `DeviceRegion` 缓存起来。
3. 用 `GetGeoInfoW` 把 GeoID 转成 ISO-3166 两位国家代码。
4. 查策略。区域条件里有这个国家就用那个状态，否则用 `defaultState`。

第 4 步就是全部的机会所在：**一条没有区域条件的策略，永远落回 `defaultState`。**

## 3. 两种切入点，为什么两个都要做

**只改区域。** 把 `DeviceRegion` 设成 68，Windows 自己就会算出欧盟的答案。做法简单 ——
但它会把欧盟的损失连同好处一起收下，而且没得挑。这就是论坛上流传的做法，也正是这个工具
v1 版做错的地方。

**改写策略文件。** 把每一条的 `defaultState` 设成你要的状态，并删掉 `conditions`。
结果就完全与区域无关，而且每一条都能分别决定。

v2 两个都做。改写策略文件是超集得以成立的原因；区域还是要设，因为有些欧盟行为不在这个文件里
（例如 Edge 自己的行为），它们直接去读区域。

## 4. 那条规则

在 `src/EULord/Core/PolicyFile.cs`：

```csharp
internal static string Decide(PolicyDecision d)
{
    // 没得选：爱尔兰跟全世界的状态一样。
    if (!d.Differs) return d.GlobalState;

    // 从来没分类过，就保持 Windows 出厂的样子。
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
case PolicyClass.Benefit: return eeaState;     // 好处：收下
case PolicyClass.Loss:    return globalState;  // 损失：拒收
default:                  return globalState;  // 见仁见智：交给用户，默认不动
```

分类表在 `src/EULord/Core/Catalog.cs` —— 68 条，每条有稳定的 `Id`、分类，以及三种语言的说明。
`docs/POLICIES.md` 是由它生成的，所以文档不可能跟代码讲不一样的话。

有差异但**未分类**的策略会被**原样保留**，并且在状态栏、日志和 CI 警告里列出来。
用猜的可能害用户功能倒退，而那正是超集承诺唯一不能破的底线。

## 5. 每次改写都从干净的原始文件重建

我们写出去的文件没有区域条件。这同时也是识别方法：还留着区域条件的是 Windows 给的，
没有的就是我们写的。

`C:\ProgramData\OneClickEULord\backups\` 里保存两份：

| 文件 | 用途 |
|---|---|
| `…original.json` | 这台机器上**最早**见过的那一份。写一次，永不覆盖。 |
| `…pristine.json` | Windows **最新**提供的那一份。哈希变了就更新。 |

每次改写都从 pristine 重建，绝不在我们已经改过的文件上叠加。没有这一步的话，
第二次执行时就没有区域条件可读，欧盟状态会变成未知，用户的逐条覆写也会无处可退回。
这也是让重复执行结果完全相同的原因 —— 否则计划任务每次触发都会去搅动一个系统文件。

## 6. 更新之后怎么留住

Windows 会在维护期间还原这个文件，所以只改一次，下一个累积更新就会把它盖回去。
计划任务 `OneClickEULord-Persist` 以 SYSTEM 身份执行，有三个触发条件：

| 触发 | 为什么 |
|---|---|
| 用户登录 | 覆盖每一次重启 |
| 每天一次 | 覆盖中间被悄悄重置的情况 |
| `Microsoft-Windows-WindowsUpdateClient` 事件 19 | 「安装成功」—— 更新后几分钟内补回，不必等到下次登录 |

它跑的是 `--reapply`：除非这台机器应用过超集、而且确实跑掉了，否则什么都不做。
不装服务、不装驱动、不常驻。

计划任务状态是直接读 `System32\Tasks` 下面那份任务自己的 XML，而不是去解析 `schtasks` 的输出 ——
后者会被本地化，在中文机器上字段名会变成中文。

## 7. UCPD，以及为什么不禁用驱动

Windows 11 的 UserChoice 保护驱动程序会按照写入进程的**映像名称**阻止对 `DeviceRegion` 的写入。
它的拒绝列表包含 `reg.exe`、`powershell.exe`、`cmd.exe`、`regedit.exe`、`rundll32.exe`、`mshta.exe`。

`EULord.exe` 不在那份列表上，所以一般的注册表写入预期就会成功；v1 因为必须调用外部程序才被挡。
只有在读回验证发现没写进去时，才会启用备用方案：把 `reg.exe` 复制成一个临时的名字，
只写这**一个**值，然后立刻删掉副本。

驱动程序从头到尾不会被禁用、不会被卸载，系统其他地方也完全不碰。

## 8. 写 System32 的文件而不留痕迹

v1 对策略文件执行了 `takeown /f` 和 `icacls /grant *S-1-5-32-544:F`。结果这两件事都是错的：

1. **根本不需要。** 出厂的 DACL 本来就给 `BUILTIN\Administrators` 完全控制 ——
   `(A;;FA;;;BA)` —— 提升权限的进程直接写就可以了。
2. **而且不可逆。** 所有权事后要不回来：
   `icacls <file> /setowner "NT SERVICE\TrustedInstaller"` 会以错误 1307 失败，
   *「此安全 ID 不能被指派为该对象的所有者」*。所以 v1 **永久**改掉了一个
   Windows 系统文件的所有者和权限。

v2 是就地直接写入，所有者与 DACL 完全不动。万一真的被拒绝，就启用 `SeRestorePrivilege`
（备份软件用的那个特权，管理员本来就持有），以 backup semantics 打开文件。
这只让那一个句柄跳过 DACL 检查，不修改任何东西，两条路都不会留下痕迹。

## 9. 编码，每一条都是踩过的坑

| 规则 | 不遵守会怎样 |
|---|---|
| 策略文件是**无 BOM** 的 UTF-8 | PowerShell 5.1 的 `Set-Content -Encoding UTF8` 会加 BOM；v1 把 BOM 写进了系统文件 |
| `.cs` 源码是**带 BOM** 的 UTF-8 | 否则 `csc.exe` 会用 ANSI 代码页解码，在默认的繁中机器（cp950）上所有中文都变乱码 |
| `.ps1` 用 CRLF | 曾经发布过 LF 结尾的脚本，因为 `.gitattributes` 只在全新 checkout 时转换 |
| 时间戳用 `InvariantCulture` | 繁中默认是民国纪年，2026 被记成 `115` |
| 语言检测优先看用户的语言列表 | 有台机器 `Get-UICulture` 是 `en-US`，但第一偏好语言是 `zh-Hant-TW`，v1 就给中文用户看了英文 |

前三条由 `tools/Check-Encoding.ps1` 在 CI 把关，后两条有测试覆盖。

## 10. 诚实的验证

写进设置不等于功能就会出现 —— 微软是分批推送的。所以报告分两层：

**已确定** —— 我们读回来就能证明的：区域值、策略文件是不是我们改写的版本、
计划中每一条状态是不是真的在磁盘上、计划任务有没有注册。这几项只要有一项失败，就是工具失败了。

**观察到的** —— 要 Windows 自己动作的：Store 有没有报告自己可移除、Edge 的卸载项允不允许移除。
这些本来就可能延迟，所以结果是「尚未生效」并附上可能原因，不会算失败，也绝不会算成功。

量不到的就报告「无法判断」，不用猜的。「关闭网页搜索结果」那个开关有没有出现在设置里，
Windows 并没有提供给我们查，所以工具报告的是它背后的策略状态，并且讲清楚量到的是哪一个。

## 11. 卸载 App

只有一条规则，来自这个工具的用户本人：**卸载 app 要走 app 自己的卸载程序，不然怕删掉系统必要的组件。**

所以只有两条路，两条都是微软自己的：

- **Appx／MSIX** —— `Remove-AppxPackage`，官方文档记载的移除 API。
- **已注册的卸载程序** —— App 自己写进 `Uninstall` 注册表项的那个命令，原样执行。
  这台机器上 Edge 的是 `MsiExec.exe /X{…}`，而不是一般预期的 `setup.exe --uninstall` ——
  这正是为什么要去读它，而不是假设。

绝对不做的事：删除属于 App 的文件或文件夹、动 WinSxS、拔系统组件、
强制移除没有卸载程序的东西，或在卸载失败后退回手动删除。找不到合法途径的就标示为不可卸并说明原因。

命令行**不提供**这个功能。移除软件需要有人逐一确认，所以无人值守的执行永远做不到。

## 12. 刻意不做的事

- 没有遥测、没有分析、不回传任何东西。唯一的网络请求是检查更新，而且只在你按下按钮时。
- 不尝试绕过域或 MDM 策略。受管理的机器如果覆盖我们写的值，工具就如实报告然后停手。
- 不做通用的 App 管理器。只处理微软自家的内置 App，因为欧盟策略讲的就是这些。
- 不夹带 runtime。.NET Framework 4.8 是 Windows 11 内置的，所以完整编译出来只有约 165 KB。
