# Building KIT

This document is for developers and users who want to audit the published Windows binaries. Regular users should download the latest package from GitHub Releases.

## Requirements

- Windows 10 or 11
- .NET 10 SDK
- Inno Setup 6 only when building the installer

## Build and test

```powershell
git clone https://github.com/sayaoff/KIT.git
cd KIT
dotnet build KIT.sln
dotnet run --project tests\KIT.Core.Tests\KIT.Core.Tests.csproj
dotnet run --project src\KIT.App\KIT.App.csproj
```

## Release build

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
```

The script builds and tests the solution, creates a self-contained `win-x64` portable package, prints its SHA-256 value, and builds a per-user installer when Inno Setup 6 is available.

## Project structure

- `KIT.Core` — Kits, session lifecycle, Actions/Restore, and recovery.
- `KIT.Infrastructure.Windows` — process observation and Windows actions.
- `KIT.Data` — local JSON/JSONL persistence.
- `KIT.App` — WPF interface and system tray integration.
- `KIT.Core.Tests` — self-contained checks for core scenarios.

## Local data

KIT stores user data in `%LOCALAPPDATA%\KIT`:

- `configuration.json` — selected `cs2.exe`;
- `kits.json` — Kits and the Active Kit;
- `active-session.json` — crash-recovery state;
- `preferences.json` — language, appearance, and Windows startup preference;
- `activity.jsonl` — events and warnings;
- `sessions.jsonl` — completed sessions and CPU/RAM metrics;
- `startup.log` — window startup diagnostics.
