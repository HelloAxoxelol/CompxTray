# Changelog

## Unreleased

- macOS and Linux preview (`cross/`, .NET 10 with Avalonia and HidSharp): battery level in the menu bar or tray with low battery notifications, start at login, and a read-only settings window. Not yet tested on a real Mac or Linux PC.
- `compxtray-cli` for macOS and Linux: battery, settings, diagnostics and device listing, read-only.
- Linux package with an installer and a udev rule for access to the receiver without root. macOS packages for Apple silicon and Intel as an ad-hoc signed `CompxTray.app`.
- The protocol code is split from the Windows HID transport (`src/Device/WinHid.cs`), so both builds share it.
- The log now records every change in battery level or charging state and every voltage change of 10 mV or more, not only when the mouse wakes up.

## 1.0.0 - 2026-10-24

First public release.

- Battery level in the tray, with colour by level, charging and asleep states, and low battery warnings.
- Settings window with polling rate (plus measurement), DPI stages, lift-off, motion sync, debounce and sleep timer.
- Every write is verified by reading it back. The original settings are backed up and can be restored.
- Writes are limited to verified models. The Svive Flow 7 (`373B:11B3`) is verified.
- Demo mode (`--demo`) and screenshot export (`--screenshot`).
- The active DPI stage follows the DPI button live while the DPI tab is open.
- Diagnostics in `tools/` for mapping new mice, plus a guided hardware test (`verify-writes.bat`) that restores the original settings afterwards.
- Every setting is verified on a Svive Flow 7, including persistence after power off.
- Releases include a SHA-256 checksum.
- Project website in English and Swedish (`docs/`, GitHub Pages).
