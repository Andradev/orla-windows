# Privacy

Orla is designed to work locally. It contains no telemetry, analytics, account system, cloud synchronization, advertising, or automatic report upload.

## Data read from Windows

- windows, processes, and active display for top-bar titles and the optional legacy dock;
- network state, Wi-Fi strength, and the active connection name when Windows permits access;
- volume, mute, battery, Bluetooth, Night light, and Energy saver state;
- integrated-display brightness through WMI and external-display brightness through DDC/CI;
- Windows display language and regional date/time format.

This information is used only for the local interface. A connection or device name may appear temporarily in Quick Panel but is not written by Orla.

## Data stored locally

`%LOCALAPPDATA%\Orla\settings.ini` stores preferences, the optional Fluent Search path, and legacy-dock order/favorites. Diagnostic messages remain in `%LOCALAPPDATA%\Orla\Orla.log`.

The installer writes two current-user registry entries:

- startup under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`;
- uninstall metadata under `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Orla`.

## Communication

Fluent Search integration uses a local named pipe. Orla creates no network connection of its own. Windows Settings, documentation, and GitHub links open only after an explicit user action.

## Removal

The built-in uninstaller removes the executable, startup registration, uninstall metadata, settings, and log. `--keep-settings` preserves the INI and log. Taskbar state is always restored and its temporary recovery file removed.
