[繁體中文](../RELEASING.md) · [简体中文](../zh-CN/RELEASING.md) · **English**

# Releasing

Releases are automated by [`.github/workflows/release.yml`](../../.github/workflows/release.yml). Users download a
single file: `EU-Overlord.cmd`.

## Cutting a release

1. Set `$Script:Version` in `src\EUOverlord.ps1` to the new version (for example `'1.2.3'`).
2. Add a `## v1.2.3 — date` section (all three languages) at the top of `CHANGELOG.md`.
3. Tag and push:

   ```bash
   git tag -a v1.2.3 -m "v1.2.3"
   git push origin v1.2.3
   ```

GitHub Actions then, on Windows:

1. validates the tag (`^v\d+\.\d+\.\d+$`);
2. runs `tests\Test-Engine.ps1` against **that tag's code** under Windows PowerShell 5.1 and PowerShell 7 (the tests
   also build the single file and run it once);
3. checks that `$Script:Version` matches the tag;
4. builds `EU-Overlord.cmd` with [`tools/Build-SingleFile.ps1`](../../tools/Build-SingleFile.ps1);
5. creates the GitHub Release with the matching `CHANGELOG.md` section as notes and uploads `EU-Overlord.cmd`.

Any failure stops the release; nothing half-built is published. The asset is always named `EU-Overlord.cmd`, so this
link always serves the newest version:
`https://github.com/PZW152/one-click-eu-overlord/releases/latest/download/EU-Overlord.cmd`

## Rebuilding an existing release asset

Actions → **Release** → *Run workflow*, and enter an existing tag (v1.2.0 or later). If the release exists, only the
asset is replaced; the notes are left alone.

## Building locally

```powershell
powershell -NoProfile -File tools\Build-SingleFile.ps1   # writes dist\EU-Overlord.cmd
```

## Updating the policy reference

When a Windows build adds or removes policies, run `tools\New-FeatureTable.ps1 > rows.md` on that machine, paste the
rows into the three `FEATURES.md` files and update the counts.

## Security notes

- Default permissions are `contents: read`; only the release job gets `contents: write`, and the token is handed only
  to the final publishing step.
- Only the first-party `actions/checkout` is used, pinned to a full commit SHA (v4.3.0); no third-party actions.
- The tag name is passed through an environment variable and validated, never interpolated into a script.
