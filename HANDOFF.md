# 0.5.0 handoff — 2026-10-15

OSRS Path is read-only: never modify its repo, launcher, package, plugin or settings. This project owns its telemetry agent and data directory only.

New implementation: AutoTune.cs contains recommended profiles, quality ladder, per-phase acceptance, encrypted transaction/recovery and offline graphics writes. Guided.cs coordinates rounds, renderer identity, restart handshake, conditions and account-free reports. SessionWindow.cs has three preset buttons and a separate measured search. Standard mode explicitly cannot claim 60 FPS. Starter presets are for the detected i5-1135G7 / Iris Xe and are unmeasured.

Only the three flags runelite.gpuplugin, runelite.hdplugin and fpscontrol.limitFps are newly allowed outside renderer groups. GPU/HD switching was explicitly requested by the user. Do not broaden this to other plugins. Offline writes use the existing Changes transaction and DPAPI backup; nullable Patch values remove originally absent settings for restoration. Both renderer configuration proxies are observed, including inactive installed HD. Missing HD requires Plugin Hub installation by the user.

Protocol 2 adds the local active profile path and available renderers; the private path is not included in JSON reports. Matching profile path plus expected graphics is required after a new client generation. New generation during a measured phase aborts. Encrypted active-tune.bin supports restoring after an interrupted process; Original/Expected/Pending handles uncertain write completion. Changes to unrelated properties survive selective restore. Cloud sync may override disk changes and will fail the expected-settings handshake.

The launcher helper reads an existing installed app.asar module unchanged. Never assign process.resourcesPath: Electron makes it read-only. Inspect-only module loading succeeded on the available unpacked OSRS Path, ASAR hash unchanged, no game launch requested. Java agent waits for the original bridge plugin and loads its own plugin with PluginManager, rather than replacing ExternalPluginManager's builtin list.

Acceptance is conservative: every location/action must pass, not an average. Four locations x three phases per round. Search tries high to low quality, then repeats at 72 FPS. This is a finite preset search, not global optimization. Crowd tolerance is max(10 players, 50% of baseline). Changing time/weather/background load is not fully controlled.

Run test.ps1. It includes original file-safety and telemetry tests; preset/renderer exclusivity, candidate selection, missing/slow/stalled/crowd rejection, restart profile guard and unset restore; companion token/Origin/input gates; portable and updater replacement/rollback; GUI renders at both sizes. Do not label synthetic fixtures as live-game measurements. Full game startup with the telemetry agent, automated graphics close/relaunch and real FPS are still not verified.

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
