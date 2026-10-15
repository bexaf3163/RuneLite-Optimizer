# RuneLite Optimizer 0.5.1 — fix RuneLite startup

- Fixed the root cause of Java exiting before RuneLite opened: .NET Framework ZIP creation wrote backslash entry names into the companion JAR. Java could not find META-INF/MANIFEST.MF. Every JAR entry now uses forward slashes.
- Added a real JVM startup test with the packaged -javaagent JAR and tests loaded from that JAR, rather than only testing loose compiled classes.
- Launch now waits for a fresh, authenticated telemetry connection before saying RuneLite is connected. A spawned process alone is not success. Early JVM startup failure, timeout and an existing client without the optimizer module have separate messages.
- Already-connected clients are reused. Launch errors remain visible instead of being overwritten by generic polling status.
- Graphics presets and offline tuning are unchanged. OSRS Path code, package and plugin are untouched.

Download the single RuneLite-Optimizer.exe. Close any previous RuneLite client, update the optimizer and use Launch game again. The button invokes OSRS Path's existing RuneLite launcher directly; it does not open the OSRS Path dashboard itself.

Validation: packaged-agent JVM startup, companion authorization, all existing isolated safety/preset/telemetry tests, startup-failure fixtures, UI renders and portable/updater checks. Full live-game telemetry remains unverified; no gameplay or graphics changes were made during tests.
