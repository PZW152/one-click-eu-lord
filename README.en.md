[繁體中文](README.md) · [简体中文](README.zh-CN.md) · **English**

# One-Click EU Overlord · 一鍵轉生歐盟爺

> *"EU Overlord"* (歐盟爺, short for 歐盟大老爺) is a joke: as far as Windows is concerned, users in the EU are the
> overlords nobody dares to upset. They get to uninstall Edge, turn off Bing, and see far fewer ads and nags.
>
> This tool gives any Windows 11 machine the full **EEA + DMA** (European Economic Area / Digital Markets Act)
> user-benefit set in one click, and keeps it there across **Windows updates and reboots**.

---

## What the EU overlords get

To comply with the EU **Digital Markets Act**, Microsoft ships a more user-respecting behavior set to Windows 11 in the
**European Economic Area**, for example:

- ✅ **Uninstall Microsoft Edge**, without other apps nagging you to reinstall it
- ✅ **Uninstall the Microsoft Store and some built-in apps**
- ✅ **Turn off Bing / web search in Windows Search**
- ✅ **Third-party search providers** can appear in Windows Search
- ✅ **Widgets**: turn off the Microsoft News and ads feed and limit data sharing
- ✅ **A non-Edge default browser** also takes over `.svg / .xml / .mht / ftp` and similar types
- ✅ **Far fewer promotions**, full-screen upsell pages and Edge marketing campaigns

Two things decide all of this:

```
C:\Windows\System32\IntegratedServicesRegionPolicySet.json         ← 84 region policies
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion   ← the device region Windows believes in
```

**One-Click EU Overlord** switches your machine into the EEA state (the **superset** of those benefits) and installs a
scheduled task that puts it back if an update reverts it.

> **About "superset"**: the tool mirrors the EEA policy set **exactly**, because that bundle is Microsoft-defined and
> internally consistent. Every switch that gives EEA users a *choice* is turned on; the tool never force-disables a
> feature for you. Whether you actually remove Edge or Bing is up to you. See the [policy reference](docs/en/FEATURES.md)
> for every policy.

---

## Two one-click modes

| Mode | Launcher | What it does | Footprint | Survives updates |
|------|----------|--------------|-----------|------------------|
| **Gentle** (start here) | `1-Apply-Gentle.cmd` | Sets `DeviceRegion` to an EEA country (Ireland by default) so **Windows itself** computes the EEA states | Minimal: one registry value | Kept by the persistence task |
| **Hardcore** | `2-Apply-Hardcore.cmd` | Gentle **plus** rewrites the policy file so every policy evaluates to its EEA state for **every region** | Moderate: rewrites a protected system file (backed up first) | Holds even if the region is reset |

Both modes:

1. **Back up** the original registry value and policy file to `C:\ProgramData\OneClickEUOverlord\backups\`;
2. Install a **SYSTEM scheduled task** that re-applies the state **at every logon and daily**;
3. Record their state in `state.json`, so **Restore** can put things back exactly.

> **About UCPD**: Windows 11's UCPD driver blocks `reg.exe` / `powershell.exe` from writing `DeviceRegion`. The tool
> first writes normally and reads the value back; only if that was blocked does it write this **one value** with a
> **renamed copy of `reg.exe`** in a temp folder, which it deletes right after. It does **not** disable the UCPD
> driver for the rest of the system.

---

## How to use

1. Download the latest `one-click-eu-overlord-vX.Y.Z.zip` from [Releases](https://github.com/PZW152/one-click-eu-overlord/releases/latest) and extract it.
2. Open the `launchers\` folder and double-click the launcher you want:

   | Launcher | Does |
   |----------|------|
   | `1-Apply-Gentle.cmd` | One-click Gentle mode |
   | `2-Apply-Hardcore.cmd` | One-click Hardcore mode |
   | `3-Restore.cmd` | Restore region and policy file, remove the task (backups kept) |
   | `4-Status.cmd` | Show the current state |
   | `5-Uninstall.cmd` | Restore, then delete all of the tool's data |

3. Accept the UAC prompt (Administrator rights are required).
4. **Sign out or restart** for the full effect.
5. To remove Edge afterwards: *Settings → Apps → Installed apps → Microsoft Edge → Uninstall*.

On-screen text follows the language you ranked **first** in *Settings → Time & language → Language & region*:
**Traditional Chinese, Simplified Chinese or English**.

### Command line

```powershell
# In an elevated PowerShell
.\src\EUOverlord.ps1 -Action Apply -Mode Hardcore -Region Germany
.\src\EUOverlord.ps1 -Action Status -Language en
.\src\EUOverlord.ps1 -Action Restore
.\src\EUOverlord.ps1 -Action Uninstall -FullClean -Force
```

| Parameter | Values |
|-----------|--------|
| `-Action` | `Apply` / `Restore` / `Status` / `Uninstall` (`Reapply` is used by the scheduled task) |
| `-Mode` | `Gentle` (default) / `Hardcore` |
| `-Region` | `Ireland` (default) / `Germany` / `France` / `Netherlands` / `Spain` / `Italy` / `Sweden` / `Poland` / `Finland` / `Austria` |
| `-Language` | `auto` (default) / `zh-TW` / `zh-CN` / `en` |
| `-FullClean` | With `Uninstall`: also delete backups and logs |
| `-Force` | Skip the confirmation prompts for Hardcore and full clean |

### Upgrading from One-Click EU Grandpa (v1.0.x)

Run **Apply** once with the new version. It moves `C:\ProgramData\OneClickEUGrandpa\` to the new location, keeps your
original backups, and replaces the old scheduled task.

---

## Risks and caveats

- This is **not** a Microsoft-supported configuration; it is region/feature customization of your own machine. Use at your own risk.
- Try **Gentle** first. Hardcore rewrites a system file (backed up and restorable).
- Some builds (reports mention 25H2) restore the policy file often during cumulative updates, which is why the persistence task exists.
- If `DeviceRegion` is managed by MDM or domain policy, you may need Safe Mode or your administrator.
- Microsoft rolls features out in A/B stages, so switching to the EEA does not guarantee every item appears at once.
- The tool collects and uploads nothing. Every action is logged to `C:\ProgramData\OneClickEUOverlord\logs\euoverlord.log` (in English, ready to attach to an issue).

## More documentation

- [Policy reference](docs/en/FEATURES.md): all 84 policies, global default vs. EEA state
- [How it works](docs/en/HOW-IT-WORKS.md): region detection, both modes, UCPD, persistence, file locations
- [Releasing](docs/en/RELEASING.md): for maintainers
- [Changelog](CHANGELOG.md) · [Security & disclaimer](SECURITY.md)

## Development

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\Test-Engine.ps1
```

The tests need no Administrator rights and never touch the system: every path points into a temp sandbox.
GitHub Actions runs them on Windows on every push.

## License

[MIT](LICENSE). No warranty.
