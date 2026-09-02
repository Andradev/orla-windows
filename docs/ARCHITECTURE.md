# Architecture

Orla is one x64 WPF/.NET Framework executable. It has no runtime packages, resident service, WebView, process injection, or Windows file modification.

## Design boundary

Orla 2 separates shell behavior from visual ownership:

- Explorer owns the Windows taskbar, application grouping, focus, previews, jump lists, badges, and notifications.
- Orla owns its top bars, quick controls, optional Fluent Search bridge, and the legacy visual dock.
- documented Windows APIs coordinate work areas, auto-hide, system state, and recovery.

The native taskbar does not expose a supported API for replacing its internal Windows 11 visual tree or converting it into an arbitrary floating surface. Orla does not patch or inject into Explorer to cross that boundary.

## Components

- `SelfInstaller`: per-user installation, update, startup registration, Installed Apps registration, and uninstall from the same executable.
- `ShellController`: lifecycle, displays, integration mode, Fluent Search, and Windows-key handling.
- `TopBarWindow`: one Win32 AppBar-backed top bar per display.
- `TaskbarController`: captures the user's original taskbar state and applies native auto-hide or legacy-dock hiding reversibly.
- `QuickPanelWindow`: a transient flyout created only while open.
- `SystemStatusMonitor`: one shared source for network, audio, battery, Bluetooth, and quick-action state.
- `BrightnessService`: transient WMI and DDC/CI brightness control.
- `RadioService`: Wi-Fi and Bluetooth state through `Windows.Devices.Radios`.
- `DockWindow`: the optional legacy floating dock.
- `WindowCatalog` and `ForegroundWindowTracker`: legacy-dock window discovery and event-driven focus state.
- `BareWindowsKeyHook`: distinguishes a standalone `Win` press from native `Win+...` combinations.
- `ShellSettings`: a small UTF-8 INI file in the user's local profile.
- `Loc`: display language from Windows and date/time formatting from the regional profile.

## Startup and installation

Double-clicking a downloaded `Orla.exe` offers a per-user installation. The executable copies itself to `%LOCALAPPDATA%\Orla\Orla.exe`, writes the current-user startup and uninstall entries, and launches the installed copy. No elevation is requested.

At sign-in, Explorer starts Orla with `--startup`. Orla loads `settings.ini`, recovers any original taskbar state left by an interrupted previous session, applies the selected taskbar mode, and creates one top bar per display.

An external copy with the same content redirects to the installed copy. A different version offers an in-place update. `--portable` bypasses redirection for development.

## Taskbar integration modes

### Native

`TaskbarMode=Native` is the default. Orla leaves every `Shell_TrayWnd` and `Shell_SecondaryTrayWnd` visible and uses the documented `ABM_SETSTATE` message to request native auto-hide when enabled. Explorer remains the sole owner of task buttons and their behavior.

The previous taskbar state is written to `taskbar-state.txt` before Orla changes it. The value is restored on normal exit, uninstall, `--restore`, or the next launch after an interrupted session.

### OrlaDock

`TaskbarMode=OrlaDock` creates one `DockWindow` per display and hides the native taskbars while Orla runs. It is retained for existing users and visual experimentation, but it is not the reliability baseline.

## Top bars and multi-monitor behavior

Each connected `Screen.DeviceName` receives an independent top bar. `ABM_NEW`, `ABM_QUERYPOS`, and `ABM_SETPOS` reserve the top work area, while `ABN_POSCHANGED` keeps it synchronized with display and taskbar changes. Display add/remove events rebuild only Orla-owned windows and then reapply the configured taskbar mode.

The quick panel opens on the top bar's display and is disposed completely when closed. Status services are shared across displays to avoid duplicate polling and callbacks.

## Fluent Search

Each search button carries its display name; a standalone Windows-key press uses the display containing the cursor. Requests are serialized and sent through Fluent Search's local named pipe. Orla positions only the still-hidden search window and stops controlling it once Fluent Search begins its own animation.

If Fluent Search is absent, Orla does not install the keyboard hook and the Windows key remains native.

## Performance model

- focus, network availability, and volume are event-driven;
- battery, Bluetooth, Wi-Fi strength, and quick-action state share one ten-second timer;
- DDC/CI and WMI discovery exists only while Quick Panel is open;
- native mode creates no dock windows, application catalog timer, dock hit testing, or dock animation workload;
- software WPF rendering keeps integrated-GPU memory behavior predictable on the original target hardware.

## Recovery guarantees

Orla-owned AppBars exist only for the process lifetime and send `ABM_REMOVE` before closing. `TaskbarController.RestoreAll` shows every native taskbar and restores the captured taskbar state. The self-uninstaller invokes that recovery before stopping another installed process or deleting files.
