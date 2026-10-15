# RuneLite Optimizer maintenance

This is an independent project. Do not change OSRS Path or its launch flow.

The user's launch fix authorizes a process-local optimizer adapter around the unchanged installed launcher: add our JAR and select our entry point, which registers the original bridge and optimizer together in RuneLite's normal builtin startup. Never edit OSRS Path files/classes, transform bytecode, or persist injected environment variables. Open the original dashboard normally after the bridge is ready to avoid racing its game autostart.

- Keep the application portable and usable without administrator access.
- Never change real game settings during tests. Use fixtures.
- Read only required diagnostic fields; never export credentials, raw user configuration, process command lines, account IDs, or game logs.
- GPU changes must be explicitly previewed and confirmed, use an allowlist, require the client closed, and preserve unrelated bytes.
- The offline file editor always requires the client closed. The separately approved guided benchmark may use its own companion's ConfigManager API for allowlisted graphics only, with encrypted original values and profile/conflict guards. Never send gameplay inputs.
- Never edit, rebuild, replace or publish any OSRS Path source, binaries, plugin or configuration. Its installed launcher may only be invoked unchanged. Keep all optimizer assets and state under this independent project and its own application-data folder.
- Backups may contain sensitive configuration: encrypt them with Windows CurrentUser DPAPI, keep them out of source control, and fail closed on restore conflicts.
- Unknown hardware/telemetry must remain unknown. Do not invent FPS, temperatures or measured performance improvements.
- Standard GPU and 117 HD settings are different. The user explicitly authorized switching the GPU/117 HD graphics plugins and disabling the global FPS Control cap for the three renderer modes. Keep this offline-only, allowlisted, previewed and reversible. Never change other plugins or OSRS Path settings.
- Run `test.ps1` after changes. Inspect UI renders at both sizes when layout changes.
- Keep README limitations and handoff evidence accurate.
- Ship a single EXE with embedded capture tool, catalogue, manuals and licenses. Never require adjacent files for normal operation.
- Updates must use this repository's stable GitHub release, exact asset identity, SHA-256 and version checks. Never restart an active session without user action. Keep the updater helper protocol compatible and test replacement/rollback on isolated copies.
