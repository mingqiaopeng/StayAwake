# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- `AutoStart.ExePath` leaked a `Process` handle on every call. The path is
  re-read on every autostart validation, every menu toggle and on every
  self-test, so this leaked steadily over a long-running session.
- `Native.LastWin32Error()` read the Win32 error code through a
  self-declared `GetLastError` P/Invoke marked `SetLastError = true`. The CLR
  saves and restores the last-error slot around such an import, so the value
  being read was being overwritten by the very call that read it. Now uses
  `Marshal.GetLastWin32Error()`.
- The self-test leaked a `Process` handle while reading the session id.
- Typing an interval value in the GUI wrote the configuration file once per
  keystroke. "300" produced three atomic file replacements, and the
  intermediate values (3, 30) were written even though they were never what
  the user wanted. The engine still applies changes immediately; persistence
  now happens once, when the field loses focus.
- Opening the settings panel from the tray menu did not restore it from a
  minimized state, so the click appeared to do nothing.
- `build.cmd` failed on systems without the 64-bit .NET Framework directory.
  It now falls back to the 32-bit compiler, which produces identical output
  for `/platform:anycpu`.

### Changed

- Removed the `GetLastInputInfoEx` code path. That export does not exist in
  `user32.dll` — only `GetLastInputInfo` has ever existed. The code carried a
  full "64-bit timebase first, fall back to 32-bit" mechanism around it that
  always ended in an `EntryPointNotFoundException` and took the fallback
  branch.
- Removed the unreachable `TrayState.Standby` and `TrayState.Dark` states and
  the unused `SA_ICON_BLACK.png` resource. `TrayState.Dark` mapped to a pure
  black icon, which is invisible on a dark taskbar — the opposite of what its
  name promised. `SA_ICON_NAVY.png` is kept: it is the window / taskbar icon.
- Renamed `KeepAwakeOptions.NudgeKey` to `NudgeVirtualKey`. The old name
  collided with the `KeepAwakeEngine.NudgeKey` method.
- Extracted the duplicated magic numbers `0.96` and `10` into the named
  constants `BreathRatio` and `InkAlphaThreshold`.

### Added

- `--selftest` diagnostic report, covering embedded resources, icon rendering,
  screen saver read/write round-trip, power requests, idle detection, input
  injection, autostart registration and the configuration file.
- Logging to `%LOCALAPPDATA%\StayAwake\stayawake.log`, with rotation.
- Named constants and comments for the non-obvious WinForms and P/Invoke
  pitfalls the code has to work around.

## [1.1.0]

### Added

- Settings panel reachable from the tray menu and by double-clicking the tray
  icon, kept in sync with the tray menu in both directions.
- Autostart validation: the registered command line is resolved and compared
  against the running executable, so a moved or renamed copy is reported
  instead of silently launching a stale version.

### Fixed

- The autostart checkbox no longer recurses infinitely when the registry
  cannot be written.
- The tray icon is rebuilt only on state change and the previous icon is
  explicitly disposed, which stopped a steady GDI handle leak.
- Tooltip and menu updates are throttled instead of being rewritten on every
  tick.

## [1.0.0]

- Initial release.
