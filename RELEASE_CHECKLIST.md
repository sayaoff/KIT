# KIT Alpha 0.2.1 Foundation test checklist

## Clean Windows test

- Install the Foundation build for the current user without administrator privileges.
- Confirm the Start menu shortcut and optional desktop shortcut use the KIT icon.
- Confirm the first manual launch immediately shows a populated Home screen.
- Select the expected `cs2.exe`, create a Kit, save it, and make it active.
- Add one executable to the App Library and reuse it in two different Kits.
- Change that executable path in Settings and confirm both Kits use the new path.
- Start once with existing Alpha 0.1 data and confirm Kits are preserved and `%LOCALAPPDATA%\KIT\kits.v1.backup.json` is created.
- Verify Clean Mode and Launch Apps using disposable test applications.
- Close CS2 and verify Restore plus a completed session record.
- Enable Windows startup, reboot, and confirm KIT starts only in the tray.
- Manually launch KIT and confirm the existing window opens without a second resident process.
- Use the tray Exit command and confirm the process ends.
- Uninstall KIT and confirm the Windows startup entry is removed.
- Confirm `%LOCALAPPDATA%\KIT` remains available for user-controlled backup or deletion.

## Release artifacts

- Build on Windows with `powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1`.
- Confirm the installer version and SHA-256 checksum.
- Scan the installer with Microsoft Defender.
- Add screenshots, known limitations, and the changelog to the GitHub release.
- Confirm the PolyForm Shield required notice names the intended legal copyright holder before making the repository public.
- Enable GitHub private vulnerability reporting.
