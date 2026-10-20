[繁體中文](../HOW-IT-WORKS.md) · [简体中文](../zh-CN/HOW-IT-WORKS.md) · **English**

# How it works

## 0. The single file and the menu

The `EU-Overlord.cmd` users download is produced by [`tools\Build-SingleFile.ps1`](../../tools/Build-SingleFile.ps1):
a short, pure-ASCII batch header followed by `src\EUOverlord.ps1`, unchanged.

On a double-click:

1. The batch header writes the engine (everything after the marker line) to `%TEMP%\EUOverlord-<random>.ps1` as UTF-8
   with BOM, runs it with Windows PowerShell, and deletes it afterwards.
2. With no arguments the engine opens the **menu**: it shows the current status, and plain Enter means option 1.
3. Without Administrator rights the engine reopens itself elevated through UAC; if you decline, it says that nothing
   was changed.
4. After applying or undoing, it asks whether to restart now (default: no).
5. Without an interactive console (called from a script, input redirected) the menu refuses and exits with 2, so
   "Enter = 1" can never be triggered by accident.

With arguments it is a command-line tool, for example `EU-Overlord.cmd -Action Status`.
File format: UTF-8 without BOM (a BOM breaks `@echo off`), CRLF line endings, pure-ASCII batch header. The tests check
each of these and run the file once.

## 1. How Windows decides whether you are in the EEA

Region policies are evaluated by `SystemSettings.DataModel.dll`, roughly like this:

1. Read `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion` (a DWORD holding a GeoID).
2. If that fails, call `GetUserGeoID(GEOCLASS_NATION)` and **cache** the result back into that value.
3. Convert the GeoID to a two-letter country code (`IE`, `DE`, ...) with `GetGeoInfoW`.
4. Read `C:\Windows\System32\IntegratedServicesRegionPolicySet.json`. For each policy, a matching region condition
   decides the state; otherwise `defaultState` applies.

So there are two ways to become an EU overlord: change the **region cache** (steps 1-3 see the EEA), or change the
**policy file** (step 4 outputs EEA states directly). The tool does both.

## 2. Gentle mode

Writes an EEA country's GeoID to `DeviceRegion` (Ireland = 68 / `0x44` by default), so Windows **itself** computes the
EEA state of all 84 policies. One value, clean, easy to undo. If the value is already correct, it is left alone.

## 3. Hardcore mode

Gentle, plus a rewrite of the policy file. For **every** policy:

- work out the state it would have inside the EEA (using `IE`);
- set `defaultState` to that state and **remove the region conditions**.

The policies then output their EEA state whatever `DeviceRegion` says. The transform is **generic and not tied to any
GUID**, so it keeps working when Microsoft adds policies.

Details:

- The original is backed up to `C:\ProgramData\OneClickEUOverlord\backups\IntegratedServicesRegionPolicySet.original.json`.
  Re-applying **keeps the first backup**; it is never replaced by an already-rewritten file.
- The shipped file is **UTF-8 without a BOM**, and the tool writes it back without one. (Windows PowerShell 5.1's
  `Set-Content -Encoding UTF8` would add a BOM, so the file is written through .NET instead.)

## 4. Getting past UCPD (for the DeviceRegion value only)

Windows 11's **UserChoice Protection Driver (UCPD)** matches process image names and blocks tools such as `reg.exe`,
`powershell.exe`, `cmd.exe` and `regedit.exe` from writing protected keys, `DeviceRegion` included.

`Set-DeviceRegionValue`:

1. writes through the normal API, then **reads the value back**;
2. if the value did not change (UCPD blocked it), copies `reg.exe` to `%TEMP%` under a **new name**, writes this one
   value with it, and **deletes the copy**.

This does not disable UCPD for the rest of the system. In unusual setups (MDM-managed, stricter UCPD rules) you may need
Safe Mode, or to pick an EEA country during Windows setup (OOBE).

## 5. Persistence

Scheduled task `OneClickEUOverlord-Persist` (runs as SYSTEM):

- triggers: **at every logon** and **daily at 03:00**;
- action: `-Action Reapply`, which reads `state.json` and re-applies your mode and region.

The script is copied to `C:\ProgramData\OneClickEUOverlord\EUOverlord.ps1`, so persistence keeps working after you
delete the download folder.

## 6. Languages

- On-screen text: Traditional Chinese, Simplified Chinese, English. `-Language auto` uses the language you ranked
  **first** in Windows language settings, then the display language (some PCs still display English although the user
  ranks Chinese first).
- The console is switched to UTF-8 output, so Simplified Chinese also displays on a default Traditional Chinese (cp950)
  console.
- The log is always **English** with Gregorian timestamps (a zh-TW system would otherwise print the Minguo year), so it
  can be attached to an issue as-is.
- `src\EUOverlord.ps1` must be saved as **UTF-8 with BOM**: Windows PowerShell 5.1 reads BOM-less files with the ANSI
  code page and garbles the Chinese text. Both the tests and the release workflow check this.

## 7. Files and data

```
C:\ProgramData\OneClickEUOverlord\
├─ EUOverlord.ps1                     ← script copy used by the scheduled task
├─ state.json                         ← current mode / region / original region
├─ backups\
│  └─ IntegratedServicesRegionPolicySet.original.json
└─ logs\
   └─ euoverlord.log
```

**Restore** puts things back from the backups; **Uninstall -FullClean** deletes the whole folder. The tool leaves no
files anywhere else.

### Migrating from v1.0.x

v1.0.x was called One-Click EU Grandpa, kept its data in `C:\ProgramData\OneClickEUGrandpa\` and used the task
`OneClickEUGrandpa-Persist`. Before `Apply`, `Restore` or `Uninstall`, the new version:

1. removes the old task;
2. moves the old folder to the new location (keeping the original backups and original region) and deletes the old
   script copy;
3. if both folders exist, keeps the new one, leaves the old one untouched, and says so in the log.

`Status` stays read-only and only reports that an old install was found.

## 8. References

- Rafael Rivera — *Device Region and Integrated Services Region Policy in Windows*
- Pureinfotech, gHacks, Winaero, XDA — articles on IntegratedServicesRegionPolicySet.json
- Christoph Kolbicz, Binary Ninja — analysis of the UCPD driver
- Microsoft EU Policy Blog, Windows Insider Blog — DMA compliance changes

> These are mostly third-party and community analyses; Microsoft does not support editing this file. Actual behavior
> depends on the Windows build on your machine.
