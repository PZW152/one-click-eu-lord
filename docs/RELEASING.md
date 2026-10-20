# Releasing

For maintainers. This document exists once, in English, so there is no translation
to drift out of date.

## Before tagging

```powershell
.\tools\Format-Sources.ps1   # UTF-8 BOM on .cs, CRLF on .ps1
.\tools\Build.ps1            # dist\EULord.exe
.\tools\Test.ps1             # the suite
.\tools\Check-Encoding.ps1
.\tools\Check-Catalog.ps1    # warns about policies Windows added since the catalog
.\tools\Check-Version.ps1
```

If `Check-Catalog.ps1` reports unclassified policies, decide **Benefit**, **Loss** or
**Choice** for each and add them to `src/EULord/Core/Catalog.cs` before releasing.
Leaving them unclassified is safe — the engine will not touch them — but it means the
superset is incomplete, so it is a release-blocking decision, not a release-blocking
bug.

After changing the catalog, regenerate the table:

```powershell
.\tools\New-PolicyTable.ps1   # rewrites docs\POLICIES.md
```

## The three places the version lives

`Check-Version.ps1` fails unless all three agree:

| Where | What |
|---|---|
| `src/EULord/Core/Engine.cs` | `Build.Version` — what the exe reports and logs |
| `src/EULord/app.manifest` | `assemblyIdentity version` — what Windows sees, as `X.Y.Z.0` |
| `CHANGELOG.md` | the newest `## vX.Y.Z` heading, which becomes the release notes |

## Tagging

The GitHub account has commit email privacy on, so commits **and annotated tags** must
use the noreply address or the push is rejected with `GH007`:

```bash
git -c user.email=200456337+PZW152@users.noreply.github.com -c user.name=PZW152 \
    tag -a v2.0.0 -m "v2.0.0"
git push origin v2.0.0
```

Pushing the tag runs `.github/workflows/release.yml`, which checks out that exact
commit, re-runs every check, builds, and publishes the release with the CHANGELOG
section as its notes plus the asset's SHA-256.

The asset name never changes, so this link always serves the newest build:

```
https://github.com/PZW152/one-click-eu-lord/releases/latest/download/EULord.exe
```

`workflow_dispatch` can rebuild the asset for an existing tag without re-tagging.

## Pinned actions

Third-party actions are pinned to full commit SHAs. Verify a SHA against the GitHub
API before changing it — never copy one from memory:

```bash
gh api repos/actions/checkout/tags --jq '.[] | "\(.name) \(.commit.sha)"'
```

| Action | Version | SHA |
|---|---|---|
| `actions/checkout` | v4.3.0 | `08eba0b27e820071cde6df949e0beb9ba4906955` |
| `actions/upload-artifact` | v4.6.2 | `ea165f8d65b6e75b540449e92b4886f43607fa02` |

## Versioning

Semantic versioning, with one project-specific rule: **a policy `Id` in
`Catalog.cs` is a public contract.** `state.json` and users' saved overrides key off
it, and a CLI script may pass it to `--choice` or `--override`. Renaming one silently
discards that user's setting, so treat it as a breaking change and give it a major
bump, or keep the old Id working.

The same goes for `C:\ProgramData\OneClickEULord\`, the task name
`OneClickEULord-Persist`, and the `state.json` schema. The loader migrates older
schemas (see `StateStore.FromJson`); add a migration rather than bumping the folder.

## Legacy identifiers

v1 shipped under two earlier names. The tool still adopts their recorded original
region so an upgraded machine can be restored, and deletes their scheduled tasks,
which would otherwise re-apply v1's behaviour and fight this one:

| Version | Folder | Task |
|---|---|---|
| v1.0.x | `C:\ProgramData\OneClickEUGrandpa\` | `OneClickEUGrandpa-Persist` |
| v1.1–1.2 | `C:\ProgramData\OneClickEUOverlord\` | `OneClickEUOverlord-Persist` |

Those folders are left in place rather than deleted; only their tasks are removed.

## Rendering the screenshots

`docs/images/*.png` come from the UI snapshot tool, which renders the real window
off-screen so a layout regression shows up as a picture:

```powershell
.\tools\Test.ps1    # builds dist\EULord.Tests.exe
.\dist\EULord.Tests.exe . --render dist\ui
Copy-Item dist\ui\ui-zh-Hant-collapsed.png docs\images\screenshot-zh-Hant.png -Force
Copy-Item dist\ui\ui-zh-Hans-collapsed.png docs\images\screenshot-zh-Hans.png -Force
Copy-Item dist\ui\ui-en-collapsed.png      docs\images\screenshot-en.png      -Force
```
