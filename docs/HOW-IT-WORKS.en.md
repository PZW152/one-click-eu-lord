# How it works

[繁體中文](HOW-IT-WORKS.md) · [简体中文](HOW-IT-WORKS.zh-CN.md) · **English**

Everything below was verified on Windows 11 build 26300. Another build may ship a
different policy file; the tool reads the one on your machine rather than assuming.

---

## 1. Two things decide whether you get EU treatment

```
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion
    A DWORD holding a GeoID. Ireland is 68.

C:\Windows\System32\IntegratedServicesRegionPolicySet.json
    The policy set itself. 84 entries on build 26300, 79 distinct GUIDs.
```

The JSON looks like this:

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

Read that entry as: *off everywhere, except in these 36 countries, where it is on.*

## 2. How Windows resolves a policy

`SystemSettings.DataModel.dll` does roughly this:

1. Read `DeviceRegion`.
2. If it is absent, call `GetUserGeoID(GEOCLASS_NATION)` and cache the answer back into `DeviceRegion`.
3. Convert the GeoID to an ISO-3166 alpha-2 code with `GetGeoInfoW`.
4. Look the policy up. If a region condition names that country, use it. Otherwise use `defaultState`.

Step 4 is the whole opportunity: **a policy with no region condition always falls
through to `defaultState`.**

## 3. Two ways in, and why this tool uses both

**Change the region.** Set `DeviceRegion` to 68 and Windows works out the EU answers
by itself. Simple — and it takes the EU's losses along with its gains, with no way to
pick. This is the trick you find in forum posts, and it is why v1 of this tool was
wrong.

**Rewrite the policy file.** Set each policy's `defaultState` to the state you want
and delete its `conditions`. The outcome no longer depends on the region at all, and
every policy can be decided separately.

v2 does both. The rewrite is what makes the superset possible; the region is still set
because some EU behaviour lives outside this file — Edge's own conduct, for one — and
reads the region directly.

## 4. The rule

In `src/EULord/Core/PolicyFile.cs`:

```csharp
internal static string Decide(PolicyDecision d)
{
    // Nothing to choose between: Ireland and the rest of the world agree.
    if (!d.Differs) return d.GlobalState;

    // Never classified, so leave it exactly as Windows shipped it.
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

and `AutoStateFor` is the superset in three lines:

```csharp
case PolicyClass.Benefit: return eeaState;     // the gain: take it
case PolicyClass.Loss:    return globalState;  // the loss: refuse it
default:                  return globalState;  // taste: the user's call, off by default
```

The classification lives in `src/EULord/Core/Catalog.cs` — 68 entries, each with a
stable `Id`, a class, and descriptions in three languages. `docs/POLICIES.md` is
generated from it, so the documentation cannot disagree with the code.

An unclassified policy that differs is **left untouched** and reported, in the status
readout, in the log, and as a CI warning. Guessing could hand a user a regression,
which is the one thing the superset promise forbids.

## 5. Every rewrite starts from a pristine source

Our output has no region conditions. That is also how we recognise it: a file that
still has them came from Windows, a file that does not came from us.

Two backups are kept in `C:\ProgramData\OneClickEULord\backups\`:

| File | Role |
|---|---|
| `…original.json` | The first policy file ever seen on this machine. Written once, never overwritten. |
| `…pristine.json` | The newest policy file Windows has shipped. Replaced whenever its hash changes. |

Every rewrite is built from the pristine source, never from a file we already wrote.
Without this, the second run would have no region conditions to read, so the EU state
would be unknowable, and the user's per-policy overrides would have nothing to switch
back to. It is also what keeps the result identical across repeated runs — the
scheduled task would otherwise churn a system file on every trigger.

## 6. Keeping it after updates

Windows restores this file during servicing, so a one-off rewrite is undone by the
next cumulative update. The scheduled task `OneClickEULord-Persist` runs as SYSTEM on
three triggers:

| Trigger | Why |
|---|---|
| At logon | Covers every reboot |
| Daily | Covers anything that quietly reset the file in between |
| `Microsoft-Windows-WindowsUpdateClient` event 19 | "Installation successful" — puts it back minutes after an update, not at the next sign-in |

It runs `--reapply`, which does nothing at all unless the superset was applied on this
machine and something has actually drifted. No service, no driver, nothing resident.

Task state is read from the task's own XML under `System32\Tasks` rather than by
parsing `schtasks` output, which is localized — on a zh-TW machine the field names
come back in Chinese.

## 7. UCPD, and why the driver is never disabled

Windows 11's UserChoice Protection Driver blocks writes to `DeviceRegion` by matching
the **image name** of the writing process. Its deny list includes `reg.exe`,
`powershell.exe`, `cmd.exe`, `regedit.exe`, `rundll32.exe` and `mshta.exe`.

`EULord.exe` is not on that list, so the ordinary registry write is expected to work.
v1 had to shell out and was blocked. The fallback, used only if a read-back shows the
write did not take, copies `reg.exe` to a temporary name, writes that one value, and
deletes the copy immediately.

The driver is never disabled, never unloaded, and nothing else on the system is
touched.

## 8. Writing a System32 file without leaving a mark

v1 ran `takeown /f` and `icacls /grant *S-1-5-32-544:F` on the policy file. Both turned
out to be mistakes:

1. **It was unnecessary.** The shipped DACL already grants `BUILTIN\Administrators`
   full control — `(A;;FA;;;BA)` — so an elevated process can simply write the file.
2. **It was not reversible.** Ownership cannot be handed back afterwards:
   `icacls <file> /setowner "NT SERVICE\TrustedInstaller"` fails with error 1307,
   *This security ID may not be assigned as the owner of this object*. v1 therefore
   permanently changed the owner and permissions of a Windows system file.

v2 writes the file directly, in place, so its owner and DACL are untouched. If a write
is ever denied, it enables `SeRestorePrivilege` — the privilege backup software uses,
which administrators already hold — and opens the file with backup semantics. That
bypasses the DACL check for one handle without modifying anything, and leaves nothing
behind either way.

## 9. Encodings, each one a bug that happened

| Rule | What goes wrong otherwise |
|---|---|
| The policy file is UTF-8 **without** a BOM | PowerShell 5.1's `Set-Content -Encoding UTF8` adds one; v1 shipped a BOM into a Windows system file |
| `.cs` sources are UTF-8 **with** a BOM | `csc.exe` otherwise decodes them with the ANSI code page, and on a default zh-TW install (cp950) every Chinese string becomes mojibake |
| `.ps1` files use CRLF | A release once shipped LF scripts because `.gitattributes` only converts on a fresh checkout |
| Timestamps use `InvariantCulture` | zh-TW defaults to the Minguo calendar, so 2026 was logged as `115` |
| Language detection prefers the user's language list | `Get-UICulture` was `en-US` on a machine whose first preferred language was `zh-Hant-TW`, so v1 showed a Chinese user English |

`tools/Check-Encoding.ps1` enforces the first three in CI, and the last two are
covered by tests.

## 10. Verifying, honestly

Writing a setting is not the same as the feature appearing — Microsoft rolls these out
in waves. So the report has two tiers:

**Settled** — things we prove by reading them back: the region value, the policy file
being our rewrite, every intended policy state actually present on disk, the task being
registered. If one of these fails, the tool failed.

**Observed** — things Windows has to act on: whether the Store reports itself
removable, whether Edge's uninstall entry allows removal. These can legitimately lag,
so a negative result is *not yet*, with the likely reason, never a failure and never a
success.

Anything that cannot be measured is reported as *cannot tell* rather than guessed at.
Whether the "turn off web results" switch has appeared in Settings is not something
Windows exposes, so the tool reports the policy state behind it and says which of the
two it measured.

## 11. Removing apps

One rule, from the person this was built for: *go through the app's own uninstaller, or
risk deleting components the system needs.*

So there are exactly two routes, both Microsoft's:

- **Appx/MSIX** — `Remove-AppxPackage`, the documented removal API.
- **Registered** — the command the app itself wrote into its `Uninstall` registry key,
  run as written. On this machine Edge's is `MsiExec.exe /X{…}`, not the
  `setup.exe --uninstall` you would expect, which is exactly why it is read rather
  than assumed.

What it never does: delete a file or folder belonging to an app, touch WinSxS, strip a
system component, force-remove something with no uninstaller, or fall back to deleting
by hand when an uninstaller fails. An app with no legitimate route is shown as not
removable, with the reason.

It is not reachable from the command line. Removing software needs a person confirming
each app, so an unattended run can never do it.

## 12. Deliberate omissions

- No telemetry, no analytics, no phone-home. The only network request is the update
  check, and only when you press the button.
- No attempt to work around domain or MDM policy. If a managed machine overrides what
  we write, the tool reports that and stops.
- No general app manager. Only Microsoft's own inbox apps, because only those are what
  the EU policies are about.
- No bundled runtime. .NET Framework 4.8 is in Windows 11, which is why a complete
  build is about 165 KB.
