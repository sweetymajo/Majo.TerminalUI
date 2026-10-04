# Changelog

All notable changes to `Majo.TerminalUI` are documented in this file.

## 0.0.3 - 2026-10-04

### Added

- Add `TerminalInterruptedEventArgs` to let applications continue the command loop after an interruption.

### Changed

- Change `Terminal.Interrupted` to an asynchronous event so interruption handlers can perform asynchronous work before `RunAsync()` decides whether to exit.

## 0.0.2 - 2026-10-03

### Added

- terminal initialization for Linux support.

### Changed

- Update the Sharprompt package reference.

## 0.0.1 - 2026-10-01

- Initial release.