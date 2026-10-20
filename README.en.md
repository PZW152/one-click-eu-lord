[繁體中文](README.md) · [简体中文](README.zh-CN.md) · **English**

# One-Click EU Overlord

Give your Windows 11 the treatment only EU users ("EU overlords") get: **uninstall Edge, turn off Bing in Search, and
see far fewer ads and nags**. Windows updates won't quietly undo it.

## Three steps

1. **[⬇ Download EU-Overlord.cmd](https://github.com/PZW152/one-click-eu-overlord/releases/latest/download/EU-Overlord.cmd)**
2. **Double-click** it and click **Yes** when Windows asks for permission
3. **Press Enter**, then restart

Want to go back? Double-click it again and type **3**.

> **Seeing a warning?** The file has no paid code-signing certificate, so your browser and Windows will be cautious:
> - Browser says the file may be harmful → choose **Keep** (in Edge, click "…" first)
> - Blue "Windows protected your PC" → **More info** → **Run anyway**
> - "Open File - Security Warning" → **Run**
>
> It is a plain text file anyone can open in Notepad; the source is [src/EUOverlord.ps1](src/EUOverlord.ps1).

---

## What you get

- ✅ **Uninstall Microsoft Edge**, without other apps nagging you to reinstall it
- ✅ **Uninstall the Microsoft Store** and some built-in apps
- ✅ **Turn off Bing web results** in Windows Search
- ✅ **Widgets**: turn off the news and ads feed
- ✅ A new default browser takes over **every link type**
- ✅ **Far fewer promotions**: full-screen upsells and Edge campaigns go away

The tool only unlocks these *choices*; it **doesn't** remove anything for you. Whether you actually uninstall Edge is
your call.

<details>
<summary><b>What's the difference between menu options 1, 2, 3 and 4?</b></summary>

| Option | What it does | For |
|--------|--------------|-----|
| **1 Become an EU overlord** (recommended) | Sets the device region Windows believes in to Ireland, so Windows unlocks the EU features itself | Everyone |
| **2 Hardcore** | Also rewrites the system's region policy file, so it works even if the region is changed back | People who still miss features after 1 |
| **3 Undo** | Restores the region and policy file and removes the auto-protection (backups kept) | Going back |
| **4 Remove completely** | Undo, then delete everything the tool left on the PC | Leaving no trace |

Options 1 and 2 install an auto-protection task that checks at every logon and once a day, and puts the settings back
if an update changed them.
</details>

<details>
<summary><b>What exactly does it change? Is it safe?</b></summary>

- **One registry value**: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion` (the country
  Windows thinks you are in).
- **One system file** (option 2 only): `C:\Windows\System32\IntegratedServicesRegionPolicySet.json`, 84 region
  feature switches.
- **One scheduled task**: `OneClickEUOverlord-Persist`.
- **One folder**: `C:\ProgramData\OneClickEUOverlord\`, holding backups of the originals, state and logs.

Everything is backed up first, and options 3 and 4 restore it completely. Nothing is collected or uploaded.
Windows 11's UCPD protection blocks writes to the region value; the tool works around it for **that one value only**
and leaves the protection on for everything else.

This is **not** a Microsoft-supported configuration; use at your own risk. Details in [How it works](docs/en/HOW-IT-WORKS.md)
and [Security & disclaimer](SECURITY.md).
</details>

<details>
<summary><b>I chose 1 and restarted, but still can't uninstall Edge</b></summary>

- Microsoft rolls features out in stages; some PCs need a few days or the next update.
- Double-click again and choose **2 Hardcore**.
- On a work PC managed by IT (MDM / domain), company policy may override the setting; ask your IT team.
</details>

<details>
<summary><b>Command line (advanced)</b></summary>

`EU-Overlord.cmd` and `src\EUOverlord.ps1` take the same parameters; with none, you get the menu. Run as Administrator:

```powershell
.\EU-Overlord.cmd -Action Apply -Mode Hardcore -Region Germany
.\EU-Overlord.cmd -Action Status -Language en
.\EU-Overlord.cmd -Action Restore
.\EU-Overlord.cmd -Action Uninstall -FullClean -Force
```

| Parameter | Values |
|-----------|--------|
| `-Action` | `Menu` (default) / `Apply` / `Restore` / `Status` / `Uninstall` (`Reapply` is used by the scheduled task) |
| `-Mode` | `Gentle` (default) / `Hardcore` |
| `-Region` | `Ireland` (default) / `Germany` / `France` / `Netherlands` / `Spain` / `Italy` / `Sweden` / `Poland` / `Finland` / `Austria` |
| `-Language` | `auto` (default: the first language in your Windows language settings) / `zh-TW` / `zh-CN` / `en` |
| `-FullClean` | With `Uninstall`: also delete backups and logs |
| `-Force` | Skip the confirmation prompts for Hardcore and full clean |
</details>

<details>
<summary><b>Upgrading from One-Click EU Grandpa</b></summary>

Pick any of options 1-4 in the new version. It moves `C:\ProgramData\OneClickEUGrandpa\` to the new location, keeps
your original backups, and replaces the old scheduled task.
</details>

## More documentation

[Policy reference](docs/en/FEATURES.md) · [How it works](docs/en/HOW-IT-WORKS.md) · [Releasing](docs/en/RELEASING.md) · [Changelog](CHANGELOG.md) · [Security & disclaimer](SECURITY.md)

Developers: `powershell -NoProfile -ExecutionPolicy Bypass -File tests\Test-Engine.ps1` runs the tests (no
Administrator rights needed, nothing on the system is touched).

License: [MIT](LICENSE). No warranty.
