# RuneLite Optimizer 0.5.2 — open OSRS Path and load both plugins normally

- Launch now opens the selected OSRS Path dashboard normally after RuneLite telemetry is ready. Reusing a connected client also opens/focuses OSRS Path through its normal single-instance startup.
- Removed the late Java-agent/EDT plugin attachment. Our independent entry point registers the original OSRS Path bridge and optimizer telemetry together via RuneLite's standard builtin plugin loading, then starts RuneLite normally.
- The installed OSRS Path launcher file is read unchanged. An optimizer-owned, process-local adapter preserves its JVM flags, bridge/dependency classpath and game arguments, adding only our JAR, nonce and entry point. OSRS Path source/binaries/plugin are never edited or replaced. No global environment settings are changed.
- Portable launcher resources remain alive while the optimizer module is running, rather than allowing the portable wrapper to clean up during client startup.
- Fresh authenticated telemetry is still required before reporting success. An existing RuneLite without our module must be closed before relaunch.

Close the previous RuneLite client, install this single EXE, and use Launch game. Leave time for initial loading; Play/login and all gameplay remain manual.

Validation: packaged entry-point registration with both an isolated bridge fixture and the actual available OSRS Path bridge JAR; Node adapter preserves original flags/classpath/arguments and rejects unsupported invocation; all existing safety/preset/telemetry, single-EXE and updater tests passed. Tests did not log in, change game settings or modify OSRS Path files. Full live-game telemetry and dashboard launch in the user's desktop session are still to be verified.
