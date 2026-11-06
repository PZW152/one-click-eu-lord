# Releasing

## Portable release

```powershell
.\\tools\\Test.ps1
.\\tools\\Build.ps1 -Profile release
```

Artifacts:

- `dist\\EULord.exe`
- `dist\\EULord.exe.sha256`
- `dist\\EULord-portable.zip` (created by CI/release workflow)

Use MSVC + Windows SDK in CI. The local GNU toolchain needs WinLibs `mingw64\\bin` on `PATH`.

## Version

Update all of:

- `Cargo.toml` workspace version and `crates/eulord-app/build.rs` FileVersion/ProductVersion
- `packaging/AppxManifest.xml` Identity Version and `packaging/OneClickEULord.appinstaller` Version/MainPackage Version
- `CHANGELOG.md`

## MSIX

1. Obtain a trusted code-signing certificate (PFX).
2. Set `Publisher` in `packaging/AppxManifest.xml` to the certificate subject, e.g. `CN=Your Name`.
3. Build and pack:

```powershell
.\\tools\\Pack-Msix.ps1 -Architecture x64 -PfxPath .\\signing.pfx -PfxPassword '<password>'
```

4. Upload both `dist\\OneClickEULord.msix` and `dist\\OneClickEULord.appinstaller`.

`OneClickEULord.appinstaller` points at the stable `releases/latest/download` URLs, so App Installer can update installed MSIX builds. The app's “check updates” button opens the GitHub release page.

Without a certificate, do not publish the MSIX; publish the portable ZIP instead.

## CI secrets

- `MSIX_PFX_BASE64`: base64 of the PFX file.
- `MSIX_PFX_PASSWORD`: PFX password.

The release workflow only builds MSIX when `MSIX_PFX_BASE64` is non-empty. It never embeds the certificate in the repository.

## Tagging

```powershell
git tag v3.1.0
git push origin v3.1.0
```

The workflow runs tests, builds the portable artifact, optionally builds/signs MSIX, and creates the GitHub release.

