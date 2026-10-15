# Handoff — 0.1.0

Independent Windows WinForms/.NET Framework utility, built with the Windows csc compiler. No OSRS Path source or installation was edited.

## Implemented

- Asynchronous diagnostic report: CPU, RAM with native fallback, display adapter with Display API fallback, WMI driver version if accessible, approximately one-second RuneLite process CPU/memory snapshot.
- Standard GPU-only presets; seven-key allowlist, user-selected properties file and explicit confirmation.
- Client/Java offline guard, stale-preview guard, encrypted CurrentUser DPAPI original, atomic replacement, exact-hash restore conflict rejection.
- Manual entered-FPS comparison, no claim of automatic game telemetry.
- Russian dark UI with three tabs and scrolling for smaller profile views.
- GitHub Actions build/test/artifact/release workflow. Releases use VERSION, never overwrite an existing release.

## Local verification (October 15, 2026)

- Windows compiler build succeeded.
- 23 checks passed, including full apply/restore against isolated synthetic files, restore/stale-preview conflict preservation, UTF-8/unrelated byte preservation, duplicate keys, idempotence, rejected complex properties syntax, DPAPI/metadata roundtrip and exclusion of the optimizer itself from game detection.
- Rendered all three tabs at 1080x790 and 800x620. This is an offscreen rendering smoke check, not a full live interactive walkthrough or all-DPI validation.
- Runtime diagnosis succeeded. CPU, native RAM fallback and display-adapter fallback were available; WMI was unavailable in the execution environment. No detected live RuneLite client, so no in-game FPS comparison or active-account setting persistence is claimed.
- No real RuneLite configuration, OSRS Path data, driver, registry value, service, process priority or network tuning was changed.

## Important limitations

No automatic FPS/frametime, GPU load, thermal, VRAM, OpenGL capability or monitor refresh-rate telemetry. No causal bottleneck classification. GPU target is a cap/goal, not a guaranteed result. HD is not configured. File selection is manual; active/cloud profile may overwrite local settings, so the user must verify values in the client after launch.

Properties parsing is deliberately conservative: unsupported continuations/escaped names/mixed endings fail closed. Restoring refuses any intervening file changes, including unrelated changes. Hidden Java clients can be ambiguous; writes also refuse any java/javaw processes. Backups remain encrypted on the same Windows account; do not publish them.

## Next useful work

Validated profile detection and client-specific integration, optional explicit HD profiles from verified source, actual FPS/frametime telemetry via a supported companion integration, live button/file-picker and display-scaling QA, and measured same-scene before/after comparison. Do not add generic registry cleaners or unsupported performance promises.
