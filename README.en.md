# StayAwake

Prevent Windows from automatically locking the screen or going to sleep while you're busy.

Single portable executable. No installation, no runtime dependencies, no administrator privileges required.

**Windows 10/11 · .NET Framework 4.x · ~95 KB · MIT**

[中文文档](README.md)

## Features

- Keeps your system awake by preventing sleep, display sleep, and lock screen
- Optionally simulates mouse and keyboard input to defeat lock screen
- Only simulates input when the system is truly idle (won't interfere with your work)
- Disables screensaver temporarily and restores original settings on exit
- Runs in system tray with easy access to settings
- Supports auto-start at login (no admin required)
- Silent mode for Task Scheduler
- Detailed logging for troubleshooting

## Quick Start

1. Download `StayAwake.exe` from the [latest release](https://github.com/mingqiaopeng/StayAwake/releases)
2. Double-click to run - it starts in the system tray and begins protecting immediately
3. Right-click the tray icon for menu, double-click to open settings panel

## Command Line Options

| Option | Description |
|---|---|
| (none) | Default: tray mode, start guarding immediately |
| `--gui` | Launch in window mode (MainForm) |
| `--no-autostart` | Tray mode but don't start guarding automatically |
| `--silent` | Silent mode, no window/tray (for Task Scheduler) |
| `--selftest` | Run diagnostics and exit |
| `--verbose` | Enable debug logging |
| `--interval N` | Set simulation interval in seconds (5-3600, silent mode only) |
| `--no-mouse` | Disable mouse simulation (silent mode only) |
| `--no-key` | Disable keyboard simulation (silent mode only) |
| `--always` | Simulate even when system is not idle (silent mode only) |

## How It Works

- **Prevent sleep/display off**: Uses `SetThreadExecutionState` to keep the system active
- **Prevent lock screen**: Uses `SendInput` to simulate tiny mouse movements and key presses (F24 by default), only when idle
- **Idle detection**: Uses `GetLastInputInfo` to detect real user activity

## Configuration

Settings are saved to: `%APPDATA%\StayAwake\settings.ini`

Uses atomic writes to prevent corruption.

## Logs

Logs are saved to: `%LOCALAPPDATA%\StayAwake\stayawake.log`

Automatically rotates when exceeding 512KB.

## Building from Source

Requires the .NET Framework 4.x compiler included with Windows (no Visual Studio or SDK needed).

```cmd
cd StayAwake
build.cmd
```

Output: `StayAwake.exe`. Use `build.cmd /clean` to clean build artifacts.

## Design Notes

- **Tray icon**: Rebuilt only on state changes, old icons explicitly disposed to avoid GDI leaks
- **Menu sync**: Tray menu and settings panel stay synchronized through unified commit path
- **Idle detection**: Returns "unknown" on failure rather than "zero" to avoid misjudging
- **Auto-start**: Validates registry path matches current executable to prevent launching old versions
- **Screensaver**: Strictly restores original state on exit, with fallbacks for abnormal termination

## Resource Usage

Measured over 9 minutes in tray mode: ~0.4s CPU total (~0.04%), stable GDI/USER handles.

## Known Limitations

- UI and logs are currently Chinese only
- Manual lock (Win+L) or manual sleep cannot be prevented, only automatic triggers
- Force-killing via Task Manager may prevent screensaver state restoration
- Some security software may block input simulation due to UIPI restrictions

## License

[MIT License](LICENSE)

Icons by [Icons8](https://icons8.com) - verified_account series.
