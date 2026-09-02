# Changelog

Notable Orla changes are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and public releases follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed

- made the Explorer-owned Windows taskbar the default reliability path;
- retained the original floating dock as optional `OrlaDock` legacy mode;
- moved repository documentation and public installer messages to English;
- bumped the application to the 2.0 release line.

### Added

- one-file per-user install, in-place update, startup registration, Installed Apps registration, and uninstall;
- runtime taskbar-mode switching from the Orla context menu;
- crash-safe recovery of the taskbar state captured by a previous session.

## [1.2.10] - 2026-08-28

### Fixed

- deterministic second click for auxiliary windows, WebViews, and minimized HWNDs;
- focus indicator aligned with the actual active application;
- dock reveal starting within 100 ms and completing in 160 ms.

## [1.2.9] - 2026-08-28

### Fixed

- stable click area and reliable Microsoft Teams activation;
- short directional motion for activate and minimize actions.

## [1.2.8] - 2026-08-28

### Changed

- simplified Quick Panel header.

## [1.2.6] - 2026-08-28

### Added

- compact controls for Wi-Fi, Bluetooth, Energy saver, Night light, volume, and brightness;
- vector icons and state synchronized with Windows.

## [1.2.0] - 2026-08-28

### Added

- shared network, audio, and battery monitoring;
- localization based on Windows display language and regional format.

## [1.1.0] - 2026-08-27

### Added

- first Orla-branded multi-monitor top bar and dock;
- optional Fluent Search integration and reversible taskbar restoration.

[Unreleased]: https://github.com/Andradev/orla-windows/compare/v1.2.10...HEAD
[1.2.10]: https://github.com/Andradev/orla-windows/releases/tag/v1.2.10
[1.2.9]: https://github.com/Andradev/orla-windows/releases/tag/v1.2.9
[1.2.8]: https://github.com/Andradev/orla-windows/releases/tag/v1.2.8
[1.2.6]: https://github.com/Andradev/orla-windows/releases/tag/v1.2.6
[1.2.0]: https://github.com/Andradev/orla-windows/releases/tag/v1.2.0
[1.1.0]: https://github.com/Andradev/orla-windows/releases/tag/v1.1.0
