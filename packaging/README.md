# MSIX packaging

Files:

- `AppxManifest.xml`: MSIX identity and full-trust application. `__ARCH__` is substituted by `tools/Pack-Msix.ps1`.
- `assets/`: MSIX tile/store logos.
- `OneClickEULord.appinstaller`: App Installer manifest pointing at the stable GitHub release URLs.

The `Publisher` in `AppxManifest.xml` is a placeholder (`CN=PZW152`). It must exactly match the subject of the signing certificate. A package signed with a different subject will not install.

Signing is an external prerequisite; the repo does not include a certificate. See `docs/RELEASING.md`.

