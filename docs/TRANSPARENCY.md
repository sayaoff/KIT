# Trust and transparency

KIT publishes its source so users can inspect what the Windows build does and build it themselves. The repository is source-available under the PolyForm Shield License 1.0.0; it is not OSI open source.

## What KIT accesses

- The exact `cs2.exe` path explicitly selected by the user.
- Standard Windows process information needed to detect that executable and read its CPU/RAM usage.
- Applications explicitly selected for Clean Mode or Launch Apps.
- `%LOCALAPPDATA%\KIT` for configuration, Kits, recovery state, session history, and diagnostics.
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, only when the user enables Windows startup.

## What KIT does not do

- No game injection, hooks, overlays, memory reading, or game-file modification.
- No input automation or anti-cheat interaction.
- No telemetry, analytics, accounts, cloud synchronization, or network communication.
- No arbitrary scripts, plugins, or Workshop content.
- No permanent administrator rights. The installer is per-user.

## Verify a release

1. Download the source for the same release tag.
2. Review the release commit and `CHANGELOG.md`.
3. Build on Windows with `.\scripts\build-release.ps1`.
4. Compare the published file list and inspect the generated installer.
5. Compare release SHA-256 values before running a downloaded artifact.
6. Scan the installer with Microsoft Defender or another trusted scanner.

An unsigned Alpha may trigger Microsoft SmartScreen because the binary has not built a signing reputation. A warning alone is not proof of malware, but users should verify the source, release origin, and SHA-256 value. Code signing is planned before a broader stable release.
