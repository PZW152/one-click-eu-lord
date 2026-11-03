# Security

## Reporting

Open a [security advisory](https://github.com/PZW152/one-click-eu-lord/security/advisories/new) for sensitive reports, otherwise a normal issue. This is a personal open-source project; no guaranteed response time.

## What v3 changes

Elevated apply touches only:

| Item | Location |
|---|---|
| Region policy set | `C:\\Windows\\System32\\IntegratedServicesRegionPolicySet.json` |
| Device region | `HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Control Panel\\DeviceRegion` |
| Scheduled task | `OneClickEULord-Persist` |
| Tool data | `C:\\ProgramData\\OneClickEULord\\` |

The policy file is backed up before replacement. Replacements are atomic (`ReplaceFileW`, fallback `MoveFileExW`). A journal, restore point attempt, verification pass and automatic rollback on critical failure are part of the apply transaction.

## Privilege model

- The GUI starts as `asInvoker`; it does not request admin at launch.
- Apply/restore/purge launch the same executable as an elevated worker via `ShellExecuteExW(runas)`.
- The worker receives a temporary JSON state handoff and removes it after use.
- The scheduled task runs as local `SYSTEM` with `HighestAvailable`, has three triggers (logon, daily, Windows Update event), and exits after re-applying.

## Hardening

- When elevated, the data directory DACL is set to protected: SYSTEM and Administrators full control, Users read/execute.
- Standard users may keep UI preferences in `%LOCALAPPDATA%\\OneClickEULord\\state.json`; system state remains in the protected ProgramData directory.
- The policy writer never grants ownership or modifies the DACL of Windows system files.
- No service, driver, resident process, Defender exclusion, firewall change, or background telemetry.

## Explicit non-goals

- **No software uninstall.** There is no Appx/registry uninstall execution path. The EU Unlocks page only explains which policy entries unlock uninstallability; removal itself is done by Windows Settings → Apps.
- No UCPD bypass, no renamed `reg.exe`, no patching of security components.
- No Microsoft account, activation, license, or Store entitlement changes.

## Release integrity

- Portable releases publish `EULord.exe` and a SHA-256 file.
- MSIX releases must be signed with a trusted certificate whose subject matches the AppxManifest Publisher. The repository does not contain a signing certificate.
- Unknown/unclassified policy entries are left exactly as Windows shipped them; review the plan before applying on a new Windows build.

