# Testing

## Repository verification

On Windows 10/11 x64 with .NET Framework 4.8:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Verify-Repository.ps1
```

The verifier builds into a unique temporary directory, checks the executable version, validates accessible SVG XML, checks local Markdown links, and runs `git diff --check` in native Windows environments.

## Native-mode acceptance matrix

- start with no `settings.ini` and confirm `TaskbarMode=Native` is created;
- migrate a v1 settings file and confirm it receives format 3 and native mode;
- confirm Windows owns task buttons, grouping, previews, jump lists, badges, and notifications;
- activate, minimize, restore, pin, unpin, and reorder apps through the native taskbar;
- test Teams/WebView, packaged apps, elevated apps, and apps with multiple windows;
- reveal the native taskbar from any point on the bottom edge;
- exit and run `--restore`, confirming the user's original auto-hide state returns;
- sign out/restart and confirm no custom-dock/native-taskbar race occurs.

## Orla-owned surfaces

- validate one top bar on every display and correct AppBar work areas;
- check display language plus independent 12/24-hour and regional date formatting;
- open/close Quick Panel repeatedly by button, outside click, and `Esc`;
- toggle Wi-Fi, Bluetooth, and mute, then confirm the state is reread from Windows;
- test integrated brightness and DDC/CI when hardware supports it;
- open/close the hidden-icons flyout twice in succession;
- confirm `Win+Shift+S`, `Win+E`, `Win+D`, and other combinations remain native.

## Installation and update

- double-click a downloaded executable with no prior installation;
- decline and accept the confirmation separately;
- verify `%LOCALAPPDATA%\Orla\Orla.exe`, the startup entry, and Installed Apps metadata;
- run the same downloaded file again and confirm it redirects without prompting;
- run a different version and confirm the update prompt and atomic `.incoming` replacement;
- uninstall through Installed Apps and through `--uninstall --silent`;
- repeat uninstall with `--keep-settings`.

## Multi-monitor

- test identical and mixed DPI scales;
- maximize windows on each display and check the top reserved work area;
- disconnect/reconnect a display and restart the session;
- verify the native taskbar behavior selected in Windows for multiple displays.

## Performance

```powershell
.\tools\Measure-Performance.ps1 -ProcessName Orla -DurationSeconds 600
```

Record hardware, Windows build, Orla mode, display count, DPI, duration, and workload. Compare native and legacy modes separately; isolated results are not universal guarantees.

## Release checklist

1. Update assembly version, `CHANGELOG.md`, `CITATION.cff`, and `dist\release-notes-vX.Y.Z.md`.
2. Run repository verification and the applicable acceptance matrix.
3. Verify installation, update, uninstall, and taskbar restoration on a real Windows session.
4. Create an annotated semantic-version tag.
5. Let the release workflow rebuild and publish `Orla.exe` plus SHA-256.
6. Download both assets and verify the checksum before marking the release stable.
