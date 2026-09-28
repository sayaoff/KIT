# Changelog

## 0.2.1-alpha.1 — Foundation

- Added a shared App Library for executables used by Kits.
- Changed Kits to reference library entries instead of duplicating executable paths.
- Added path updates and safe removal of library entries across every Kit that uses them.
- Added automatic migration from the Alpha 0.1 Kit format with a one-time `kits.v1.backup.json` backup.
- Added coverage for legacy migration and shared application references.

## 0.1.0-alpha.1 — Release candidate

- Added manual `cs2.exe` configuration with exact-path process tracking.
- Added Kits, Vanilla Kit, Clean Mode, Launch Apps, and automatic Restore.
- Added crash recovery for interrupted sessions.
- Added session history and CPU/RAM metrics.
- Added tray lifecycle, single-instance activation, and optional Windows startup.
- Added Russian and English interfaces with Dark/Light and Calm/Aggressive appearance modes.
- Added first-run guidance, a Home dashboard, and direct access to local diagnostics.

KIT does not inject into the game, read game memory, modify game files, automate game input, or interact with anti-cheat software.
