# One-Click EU Lord v3 — Final Decision Record

> Status: implemented in branch `refactor/rust-v3` (v3.0.0).

## 1. User decisions

| # | Decision |
|---|---|
| 1 | Full v3 rewrite: core, Windows integration, GUI, CLI, CI |
| 2 | Preserve feature parity and harden; **no software uninstall**; other policy-level removes/disables are in scope |
| 3 | Retro-futurist 1970s/80s TUI-styled **real GUI**; mouse operation is first-class |
| 4 | Sidebar information architecture |
| 5 | Color/CTA delegated to implementation |
| 6 | First-run diff review; one-click thereafter |
| 7 | Strict pure Rust; no C#/PowerShell/WebView runtime |
| 8 | `egui`/`eframe` |
| 9 | Remove software-uninstall module; still explain what EU policy unlocks |
| 10 | Windows 11 22H2+ x64/arm64 + runtime compatibility gate |
| 11 | GUI `asInvoker`; on-demand UAC |
| 12 | Simple mode by default; expert searchable list of all entries |
| 13 | Custom title bar |
| 14 | Density 4/10; motion 2/10 |
| 15 | Scheduled task: logon + daily + Windows Update |
| 16 | Journal + restore point + automatic rollback on critical failure |
| 17 | Signed MSIX + auto-update; signing certificate still an external prerequisite |
| 18 | Full CLI: plan/apply/verify/repair/restore/status/export/import + stable JSON + tests |

## 2. Architecture

```text
crates/
  eulord-core/       domain: catalog, planner, policy parser, state, i18n, paths, compat
  eulord-windows/    Windows API implementation: registry, files, ACL, UAC, restore point, Task Scheduler
  eulord-app/        eframe GUI, CLI, transactional engine
```

- `eulord-core` has no Windows API dependency and is unit-testable.
- `eulord-windows` is the only crate that links the `windows` crate.
- `eulord-app` is one binary: no subcommand/args starts the GUI; a subcommand starts the CLI.
- The GUI uses `asInvoker`; the elevated worker is the same executable invoked through `ShellExecuteExW(runas)` with a temporary state handoff.

## 3. Policy invariants

- Catalog: 68 entries — 40 Benefit, 7 Loss, 21 Choice.
- Benefit: take the EEA state.
- Loss: keep the global state.
- Choice: keep global unless the state override is `Eea`.
- Unknown/unclassified policy: leave the object exactly as Windows shipped it. Never rewrite it and never strip its `conditions`.
- `apply_target` removes the region condition only from catalogued policies whose target is being rewritten.
- Duplicate GUIDs in the policy file are processed once.
- Region target defaults to Ireland (`IE`, GeoID 68); the CLI can override it.

## 4. Transactional apply

1. Validate the plan and target policy file.
2. Back up the original and update the pristine snapshot.
3. Attempt a Windows restore point (non-fatal warning).
4. Install/update the executable copy in ProgramData.
5. Atomically replace the policy JSON (`ReplaceFileW`, fallback `MoveFileExW`).
6. Write the device region and read it back.
7. Install the `OneClickEULord-Persist` scheduled task with logon, daily and Windows Update triggers.
8. Save state and run verification.
9. On critical failure, roll back policy, region, and task using the journal/backups.

Every step emits a `StepUpdate`; the GUI renders progress and a transmission log.

## 5. UI design

- Palette: near-black green `#040A08`, panel `#091611`, phosphor ink `#D6FFE2`, amber `#FFB454`, cyan `#65E3C3`, green `#7EF09A`, red `#FF6B5E`.
- Monospace only; 1px borders; zero corner radius; bracketed labels; CRT scanlines and a subtle scanning beam.
- Sidebar: Overview / Policy Matrix / EU Unlocks / Maintenance / About.
- Custom title bar with drag, minimize, maximize, close, and language switches.
- Simple mode lists the 21 choices; expert mode exposes all 68 with `AUTO / EU / GLOBAL`.
- No software-uninstall control anywhere.
- Minimum window target 820×540 logical; default 960×600; DPI-aware.

## 6. CLI contract

Commands: `status`, `plan`, `apply`, `repair`, `reapply`, `restore`, `purge`, `verify`, `export`, `import`.

- Stable JSON is available via `--json` for `status`, `plan`, `apply`, `restore`, `purge`, `verify`.
- Exit codes: 0 success, 1 error, 2 confirmation required, 3 elevation required, 4 partial/verify failure.
- `purge` requires `--yes`.
- `apply`, `restore`, `purge` require elevation.

## 7. Compatibility and verification

- Minimum build: 22621 (Windows 11 22H2).
- Compatibility matrix: 22631, 26100, 26300. Only 26300 was exercised in this environment; destructive apply/restore was not run on the host.
- Unknown builds are marked unknown; they are not silently treated as supported.
- Tests: `eulord-core` unit tests, CLI parse/contract coverage, planner/unknown-policy tests, state migrations.
- Host verification is non-destructive: no apply/restore/purge is run in CI or tests.

## 8. Packaging and open external dependency

- Portable release: `dist/EULord.exe` + SHA-256.
- MSIX template: `packaging/AppxManifest.xml`, assets, and `packaging/OneClickEULord.appinstaller`.
- `tools/Pack-Msix.ps1` packs and optionally signs with a PFX provided by the release operator.
- **No code-signing certificate is bundled.** A trusted certificate, matching Publisher subject, is required before MSIX can be installed or auto-updated.
- App Installer provides the MSIX update path; the in-app “check updates” button opens the GitHub release page.

## 9. Non-goals

- Software uninstallation.
- UCPD bypass or renamed `reg.exe`.
- Microsoft account, activation, license, or Store entitlement changes.
- Kernel drivers, services, or background network telemetry.

## 10. Known limitations

- arm64 must be built for the arm64 target and repackaged; the checked-in MSIX template is architecture-substituted by the pack script.
- Unknown policies remain untouched, so new Windows builds may need a catalog update.
- The signing certificate is not available in this repository.
- Destructive host tests are intentionally excluded; use a VM for end-to-end apply/restore testing.

