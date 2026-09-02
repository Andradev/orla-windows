# Contributing

Thank you for helping improve Orla. Participation is governed by the [Code of Conduct](CODE_OF_CONDUCT.md).

## Scope

Orla should remain small, responsive, reversible, and native-first. Changes must not require a service, WebView, process injection, administrator rights, Windows file modification, or undocumented Explorer patching.

Explorer owns taskbar behavior in the default mode. Proposals that move window switching back into custom code need a clear reliability benefit and should remain optional.

Open a discussion before a broad interface or architecture change.

## Development setup

1. Use Windows 10/11 x64, PowerShell 5.1+, and .NET Framework 4.8.
2. Fork the repository and create a short branch from `main`.
3. Keep each change focused and reviewable.
4. Do not commit user paths, network/company names, tokens, logs, `settings.ini`, or non-reproducible binaries.

## Verification

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Verify-Repository.ps1
```

Follow the relevant [acceptance matrix](docs/TESTING.md). Changes to installation or taskbar integration must verify first install, update, startup, exit, recovery, and uninstall. Resident changes must include CPU and memory observations.

## Pull requests

Describe:

- the user problem and expected behavior;
- what changed and why;
- test hardware, Windows build, display count, and DPI when relevant;
- CPU/memory impact for resident behavior;
- sanitized screenshots for visual changes.

Do not mix unrelated refactors. A green build is required but does not replace functional verification.
