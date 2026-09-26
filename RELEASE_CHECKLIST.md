# KIT Alpha 0.1 release checklist

## Clean Windows test

- Install RC1 for the current user without administrator privileges.
- Confirm the Start menu shortcut and optional desktop shortcut use the KIT icon.
- Confirm the first manual launch immediately shows a populated Home screen.
- Select the expected `cs2.exe`, create a Kit, save it, and make it active.
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
