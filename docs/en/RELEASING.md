[繁體中文](../RELEASING.md) · [简体中文](../zh-CN/RELEASING.md) · **English**

# Releasing

Releases are automated by [`.github/workflows/release.yml`](../../.github/workflows/release.yml).

## Cutting a release

1. Add a `## vX.Y.Z — date` section (all three languages) at the top of `CHANGELOG.md`. **Without it the release fails.**
2. Tag and push:

   ```bash
   git tag -a v1.2.3 -m "v1.2.3"
   git push origin v1.2.3
   ```

GitHub Actions then:

1. validates the tag (`^v\d+\.\d+\.\d+$`);
2. runs `tests\Test-Engine.ps1` on Windows, under Windows PowerShell 5.1 and PowerShell 7, against **that tag's code**;
   **nothing is published unless the tests pass**;
3. packages what users need (`src/`, `launchers/`, `docs/`, `tools/`, the three READMEs, `CHANGELOG.md`, `LICENSE`,
   `SECURITY.md`), without `.git`, CI config or tests;
4. forces `.cmd` / `.bat` / `.ps1` to **CRLF** and checks that every `.ps1` starts with a **UTF-8 BOM**;
5. creates `one-click-eu-overlord-vX.Y.Z.zip` and the GitHub Release, using the matching `CHANGELOG.md` section as notes.

If any step fails, the release stops and nothing half-built is left behind.

## Rebuilding an existing release asset

Actions → **Release** → *Run workflow*, and enter an existing tag. If the release exists, only the zip is replaced; the
notes are left alone. Only tags from v1.1.0 on are supported; earlier ones use the old layout and names.

## Updating the policy reference

When a Windows build adds or removes policies, run this on that machine:

```powershell
powershell -NoProfile -File tools\New-FeatureTable.ps1 > rows.md
```

Paste the rows into the tables in `docs/FEATURES.md`, `docs/zh-CN/FEATURES.md` and `docs/en/FEATURES.md`, and update
the counts.

## Security notes

- Default permissions are `contents: read`; only the publishing job gets `contents: write`.
- Only the first-party `actions/checkout` is used, pinned to a full commit SHA (v4.3.0). Everything else uses tools
  built into the runner; there are no third-party actions.
- The tag name is passed through an environment variable and validated, never interpolated into a script.
