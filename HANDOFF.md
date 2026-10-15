# Handoff — 0.3.0 — 2026-10-15

Independent from OSRS Path. No changes to its repo, launcher, plugin or user settings.

## 0.3.0 changes

- Portable distribution is one EXE. PresentMon, catalogue, manual, optimization guide and third-party notices are embedded resources. PresentMon extracts only when needed to a versioned CurrentUser runtime cache and is SHA-256 checked. No adjacent assets are required.
- Dashboard uses rounded dark cards/buttons, one capture toggle, compact controls and collapsed advanced capture settings. Update preferences/status and embedded manuals are on a separate fourth page. Removed controls are disposed during navigation.
- Automatic startup check/download from the exact repository's stable releases. HTTPS, exact source URL/asset name, version, size, SHA-256 digest and EXE FileVersion are checked. No downgrade/prerelease installation. Offline errors remain on the update page without blocking the application. No automatic restart during a capture.
- Restart/install is explicit. A separate helper waits for the original PID/start time, checks the original target still matches itself, atomically replaces that EXE, and launches it. Immediate launch failure triggers restoration. Crash after a successful process start is not detected as an installation failure. Cached helper copies remain in the user's update cache.
- Preferences contain only the automatic-check boolean. No game settings are changed by updates. Old 0.1/0.2 builds require one manual download of 0.3.0.
- Build embeds all assets and validates VERSION against FileVersion. CI publishes EXE and checksum, no ZIP. License texts remain accessible inside the app.

## 0.3.0 verification

- Existing safety, frame metrics and all 100 HD setting tests pass.
- Embedded tool/catalogue/licenses verified, extraction tested in isolated fixtures, damaged fixture cache repaired.
- Update metadata rejects alternate source, absent/invalid digest, old/equal versions and prereleases. Isolated replacement detects tampering/conflicts.
- Real separate-helper fixture test waits for parent, replaces a copied EXE with a distinct valid binary, restarts its self-tests, then verifies rollback when a non-runnable candidate fails to launch. Original installed EXE remains unchanged. Helper suppresses invisible OS loader error dialogs.
- Standalone test copies only the EXE to an otherwise empty folder; self-tests and all UI/catalogue renders succeed without adjacent dependencies. Four pages inspected at both sizes.
- Download against the published single-EXE release is checked after publication. Existing ETW/RuneLite limitations below still apply; packaging/update tests do not establish in-game FPS gains.

## Implemented

- Replaced old manual-FPS tabs with dark session/settings/system navigation, draggable/resizable window chrome. Capture details and full HD editor collapse by default.
- Pinned/hash-verified PresentMon 2.6.0: exact PID, unique session, cleanup targets only this instance's ETW session. No DLL injection or game launch changes.
- Present interval FPS, mean-worst-1% FPS, p50/p95/p99, budget share, long interval counts, no outlier trimming, separate swap chains, QPC-aligned focus history sampled every 100ms, no-data state, 1M frame limit, sanitized export.
- Auto attach, manual PID, stop disables auto attach; new game process starts a new session. Manual scene/uncapped declarations; minimum 120-second baseline and comparison. Swap-chain addresses may change on game restart.
- GPU/HD 60 and 999-FPS profiles; all 100 visible HD fields searchable/editable with immutable generated bool/enum/range rules. Hidden/migration fields excluded. Source pin documented in notices.
- Offline-only byte-preserving changes, encrypted DPAPI backup, stale-preview guard and conflict-safe restore retained. No renderer activation flags written.

## Verified locally

- Build, original 23 safety checks and isolated apply/restore transactions passed.
- Deterministic telemetry tests passed: quoted CSV, actual legacy header casing, QPC, PID isolation, exact statistics, invalid values, stalls, focus crossings, all 100 HD fields, enum/range boundaries.
- UI navigation retains declarations. Sufficient baseline accepted, empty candidate rejected. 100 HD controls. Top/bottom and expanded catalogue renders at 1120x840 and 800x660 inspected.
- Official PresentMon hash verified; --help confirms flags. Source CsvOutput.cpp at v2.6.0 confirms legacy casing, TimeInSeconds and QPCTime columns.
- One-second ETW probe failed **Access denied** under current user. No automatic elevation attempted. This is not a successful live FPS test.
- Hardware: i5-1135G7, Intel Iris Xe, 15.8 GB RAM. WMI unavailable in execution context; native fallback used. No RuneLite client found in current diagnostic.

## Not verified / not claimed

- Live RuneLite/OpenGL capture and correct game swap chain; actual cap state; HD installation/runtime version; profile persistence through account sync.
- No city walkthrough, FPS improvement or stable-60 guarantee. Scene/world/player count/camera/resolution are not read automatically.
- No GPU load/temperature/VRAM, display latency, network telemetry or proven CPU/GPU bottleneck.
- Focus has 100ms resolution; sub-100ms transitions may be missed. QPC joins prevent buffered frames being classified only by receive-time focus.
- Reports require explicit export before exit. Baseline remains in memory. HD schema is a pinned source snapshot, not inferred installed-version metadata.

## Next live verification

Obtain normal ETW permission or explicitly start optimizer as administrator. Launch RuneLite through the user's normal OSRS Path workflow. Identify actual game swap chain. Run identical 120-second GE/Varrock/Falador/Lumbridge scenes at 999 and 60. Check stop/reconnect/background/loading, profile persistence in each renderer, and safe rollback. Record real measurements before claiming gains.

## Maintainer map

- Program.cs: protected settings transaction engine, diagnostics, entry and safety tests.
- Telemetry.cs: profiles, CSV, focus history, statistics, capture lifecycle and tests.
- SessionWindow.cs: UI, benchmark controls, comparison, advanced editor and UI tests.
- HdRules.cs / hd-settings.tsv: generated by tools/update-hd-catalog.mjs from pinned official source. Review generated changes and run tests when updating the pin.
- tools/prepare.ps1: fixed binary download/hash gate, no runtime download. Notices must accompany packaging.
- Portable.cs: embedded resources, version identity, protected tool extraction and resource tests.
- Updates.cs: release validation/download, preferences, helper transaction/restart/rollback and isolated tests. Preserve update CLI protocol when changing versions.
- ModernControls.cs: rounded controls and shared geometry. Manuals/notices are embedded by build.ps1.
