# Troubleshooting

## The Windows taskbar did not return

```powershell
%LOCALAPPDATA%\Orla\Orla.exe --restore
```

If the file no longer exists, restart Windows Explorer from Task Manager or sign out and back in. Orla 2 uses the native taskbar by default, so this recovery is mainly relevant to legacy `OrlaDock` mode.

## I want the original floating dock

Right-click an Orla top bar and choose **Use the legacy Orla visual dock**, or set:

```ini
TaskbarMode=OrlaDock
```

Legacy mode is optional and cannot reproduce every private Explorer behavior. Switch back through the same menu when reliability is more important than the floating shape.

## The taskbar does not auto-hide

Confirm the following value in `%LOCALAPPDATA%\Orla\settings.ini`:

```ini
NativeTaskbarAutoHide=true
```

Orla requests auto-hide through the documented Shell AppBar API. Explorer remains responsible for the reveal animation and timing.

## Fluent Search does not open or appears on the wrong display

1. Confirm `FluentSearchPath` in `settings.ini`.
2. Confirm `BareWindowsKeyOpensFluent=true`.
3. Use the search button on the desired display.
4. If Fluent Search is starting for the first time, wait a few seconds and try once more.

Without Fluent Search, the Windows key remains native.

## An elevated application does not receive focus

Windows prevents normal processes from controlling elevated windows. Run both at the same integrity level. Do not run Orla as administrator solely to bypass this protection.

## Wi-Fi, Bluetooth, or battery appears stale

Externally initiated changes can take up to ten seconds to reach the shared slow-status refresh. Open Quick Panel to see the current cards. Radio toggles immediately reread the confirmed Windows state.

Wi-Fi shows signal strength and, when permitted, the active connection name. If privacy or policy restricts the name, signal information remains available. Systems whose firmware reports no battery show external power instead.

## External-display brightness does not change

The display must expose DDC/CI and allow brightness commands in its current picture mode. In the monitor's physical menu:

1. enable DDC/CI;
2. disable Eye Saver or similar eye-comfort modes;
3. disable automatic power saving and dynamic contrast;
4. select a standard/custom picture mode and reopen Quick Panel.

The integrated laptop panel uses WMI and does not depend on monitor DDC/CI settings.

## I edited the INI and nothing changed

Exit Orla from its context menu, edit the file, and start Orla again. Runtime mode switching is available from the context menu and is safer than editing while the process is active.

## Logs

`%LOCALAPPDATA%\Orla\Orla.log`

Remove usernames, company/network names, window titles, and other private information before sharing a log.
