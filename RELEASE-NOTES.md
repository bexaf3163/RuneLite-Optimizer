# RuneLite Optimizer 0.5.0 — three graphics modes and offline autotuning

Download RuneLite-Optimizer.exe directly. No archive or installer.

- Three one-click starter presets for Mark's i5-1135G7 / Iris Xe: Standard, 117 HD and GPU. No FPS capture is required to apply a preset; these are unmeasured recommendations, not benchmark claims.
- GPU: distance 40, map loading +1, MSAA 2x, filtering 4x, cap 72. HD: distance 30, details 25, 100% resolution, fast 1024 shadows, textures/normal mapping and a few lights, no MSAA/POM. Software mode disables GPU/HD and retains its roughly 50 FPS ceiling.
- GPU and HD are mutually exclusive. The separate FPS Control global cap is disabled; unrelated plugins and OSRS Path remain untouched. HD must already be installed through Plugin Hub.
- Apply flow asks you to close RuneLite, waits for it and other Java processes to exit, writes an allowlisted graphics transaction with encrypted backup, then invokes the existing unchanged OSRS Path launcher.
- Separate optional autotune compares the baseline and up to four quality levels, automatically chooses the first passing preset, and repeats all four locations for verification at cap 72. Uses 1% low, p99, stall and crowd comparability checks; failure remains explicit.
- Recoverable encrypted state, profile/conflict/path checks and selective original-value restoration after closing the game. No gameplay input or force-killing the game.
- Fixed an installed-launcher compatibility bug: Electron resourcesPath is read-only. Inspect-only loading now succeeds on the available installed/unpacked app without changing its ASAR or launching the game.

After updating, close the previous RuneLite client and launch it from the new optimizer to load telemetry protocol 2. A portable launcher wrapper may be incompatible; choose an installed/unpacked EXE.

VALIDATION: automated safety, synthetic metrics/preset/search/restart guards, companion authorization, updater replacement/rollback, single-EXE execution and five-page UI renders. A complete live-game benchmark and graphics restart cycle have NOT been verified. 60+ FPS and zero future stalls are not guaranteed. ETW permission is needed only for frame capture. OSRS Path source, package and plugin were not modified.
