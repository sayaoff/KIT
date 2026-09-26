# Security policy

KIT is intentionally narrow: it observes the configured `cs2.exe` process from outside the game, applies user-selected desktop actions, stores data locally, and does not require permanent administrator privileges.

## Reporting a vulnerability

When this repository is public, use GitHub private vulnerability reporting if it is enabled. If it is unavailable, open an issue requesting a private contact channel without publishing exploit details, personal data, or sensitive logs.

Include the KIT version, Windows version, reproduction steps, expected behavior, actual behavior, and the smallest relevant excerpt from `%LOCALAPPDATA%\KIT\startup.log`.

Do not include the complete contents of `%LOCALAPPDATA%\KIT` without reviewing them first because executable paths and application names may reveal personal information.
