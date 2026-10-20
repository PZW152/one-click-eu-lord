# Security

## Reporting a problem

Open a [security advisory](https://github.com/PZW152/one-click-eu-lord/security/advisories/new)
on this repository, or a normal issue if the problem is not sensitive. There is no
guaranteed response time: this is a personal open-source project.

## What this tool is allowed to do, and what it refuses to

It needs administrator rights, so it is worth being precise about the boundaries.

### It changes exactly four things

| What | Where |
|---|---|
| The region policy set | `C:\Windows\System32\IntegratedServicesRegionPolicySet.json` |
| The cached device region | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion` |
| One scheduled task | `OneClickEULord-Persist` |
| Its own data folder | `C:\ProgramData\OneClickEULord\` |

Everything it creates is in that one folder, so uninstalling never has to guess at
filenames or scan for leftovers.

### It does not weaken the system

- **No ownership or permission changes.** v1 ran `takeown` and `icacls` on the policy
  file, which permanently changed a Windows system file's owner and DACL and could not
  be undone (`icacls /setowner "NT SERVICE\TrustedInstaller"` fails with 1307). v2
  writes the file in place — the shipped DACL already grants Administrators full
  control — and if a write is ever denied it enables `SeRestorePrivilege` for a single
  handle instead, modifying nothing.
- **UCPD stays loaded and enabled.** The UserChoice Protection Driver is never
  disabled, unloaded or patched. If it blocks the one `DeviceRegion` write, the
  fallback copies `reg.exe` to a temporary name, writes that one value, and deletes the
  copy. Nothing else is affected.
- **No service, no driver, no resident process.** The scheduled task runs, re-applies
  and exits.
- **No Defender exclusions, no policy bypasses, no tampering with security settings.**

### It does not delete software by hand

Apps are removed only through `Remove-AppxPackage` or the uninstall command the app
registered itself, run as written. No file deletion, no WinSxS, no force-removal of
something without an uninstaller, and no fallback to deleting files when an
uninstaller fails. An app with no legitimate route is shown as not removable.

Uninstalling is not available from the command line, so an unattended run can never
remove software. Each app is confirmed separately in the window.

### It does not work around management policy

If a domain or MDM policy overrides what the tool writes, verification reports that
and the tool stops. There is deliberately no bypass.

## Privacy

- No telemetry, no analytics, no crash reporting, no identifiers.
- The only network request is **Check for updates**, which reads the latest release
  tag from the GitHub API and sends nothing but a `User-Agent`. It happens when you
  press the button and never on launch.
- The log is local, English, and records what was changed — never credentials, tokens
  or personal data.

## Supply chain

- One file, no dependencies. Built with the C# compiler inside Windows; no NuGet
  packages, no vendored code.
- Every GitHub Action is pinned to a full commit SHA, verified against the GitHub API
  rather than copied from memory.
- Workflows default to `permissions: contents: read`; only the publishing job gets
  `contents: write`.
- Tag names reach workflows through the environment and are regex-validated before use,
  never interpolated into a script body.
- Each release's notes carry the asset's SHA-256 so a download can be checked:

  ```powershell
  Get-FileHash .\EULord.exe -Algorithm SHA256
  ```

## Risks you should weigh before using it

Honest list, because the tool asks for administrator rights:

1. **It edits a Windows system file.** That file is backed up first, twice, and
   restore puts it back — but Microsoft does not support editing it, and a future
   Windows version could change its format. The tool refuses to write rather than
   guess if it cannot parse what it finds.
2. **It tells Windows your device region is Ireland.** Region-dependent behaviour
   elsewhere in Windows and in Microsoft apps may change accordingly. Restore puts the
   old value back, or removes the value entirely if there was not one before.
3. **The unsigned executable will warn you.** There is no code-signing certificate, so
   SmartScreen will flag it. Build it yourself from source if that matters — it is one
   command and needs nothing installed.
4. **Uninstalling Microsoft Store is hard to undo.** It has no one-click reinstall;
   the tool says so next to the checkbox rather than after the fact.
