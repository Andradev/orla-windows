# Security

## Supported versions

Only the [latest stable release](https://github.com/Andradev/orla-windows/releases/latest) receives security fixes. Update older versions before investigation.

## Report a vulnerability

Use **Report a vulnerability** on the repository's [Security Advisories page](https://github.com/Andradev/orla-windows/security/advisories/new). Include the affected version, impact, minimal reproduction, and a suggested mitigation when available.

Do not publish exploitable details in an issue or discussion before a fix. The maintainer will coordinate validation and disclosure through the private advisory; timing depends on severity and reproducibility.

## Security model

- Orla runs as the current user and never requests elevation.
- Installation writes only to `%LOCALAPPDATA%` and `HKCU`.
- Native mode leaves taskbar behavior inside Explorer.
- No policy, service, driver, Windows file, or Explorer process is modified.
- The global keyboard hook exists only when standalone-`Win` Fluent Search integration is enabled and available.
- Taskbar state is captured before any change and restored on exit, uninstall, or `--restore`.
- Configuration and logs remain local.

Release binaries are unsigned. Verify the published SHA-256 or compile from source for the highest assurance.
