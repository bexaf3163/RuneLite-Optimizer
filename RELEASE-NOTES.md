# 0.2.0

- Rebuilt dark minimalist UI with collapsed capture details and advanced HD editor.
- Added official SHA-256 pinned PresentMon 2.6.0 session capture, PID/swap-chain isolation and QPC-aligned foreground filtering.
- Added present FPS, 1% low, p95/p99, long interval counters, live chart and complete-pass report/comparison.
- Added manual populated-city benchmark labels and 120-second minimum comparison gates.
- Added Standard GPU / 117 HD target-60 and high-cap-999 profiles.
- Added search/edit/preview for all 100 visible settings in a pinned official HD configuration snapshot, with compiled enum/range validation.
- Retained offline-only file writes, DPAPI backups and conflict-safe restoration.

Important: ETW access was denied in the local test environment; live RuneLite capture, populated-city performance, actual HD installation and stable 60 FPS have not been verified. ETW may require Performance Log Users or explicitly running as administrator. HD itself is installed through RuneLite Plugin Hub; city/cap labels are manual. This release provides measurement/configuration tools, not a verified automatic performance gain.
