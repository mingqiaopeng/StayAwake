# StayAwake

Keep Windows from locking the screen or falling asleep while you are busy.

One portable executable. No installer, no runtime dependencies, no admin rights.

**Windows 10/11 · .NET Framework 4.x · ~95 KB · MIT**

[中文文档](README.zh-CN.md)

<!-- Uncomment once the repository is published and the path is known:
[![Build](https://github.com/OWNER/REPO/actions/workflows/build.yml/badge.svg)](https://github.com/OWNER/REPO/actions/workflows/build.yml)
-->

---

## The problem

Windows decides on its own when to lock the screen or put the machine to
sleep. During a long render, a download, a remote session or a meeting, that
can be exactly the wrong moment.

Two different mechanisms are needed, because two different parts of Windows
are involved:

| What you want to prevent | Who controls it | How |
|---|---|---|
| Sleep, display-off | The power manager | `SetThreadExecutionState` |
| Screen lock | A separate group policy | Real input events |

`SetThreadExecutionState` on its own **does not stop the lock screen**. To
keep the screen unlocked you have to actually generate input.

---

## What it does

- Holds off sleep and display-off
- Optionally disables the screen saver, and restores your original setting on exit
- Optionally injects mouse movement and a keypress to defeat the lock screen
- Waits until you are genuinely idle, so it never fights with you
- Reports every failure instead of failing silently

---

## Quick start

1. Download `StayAwake.exe`
2. Double-click it

That is the whole installation. It lands in the notification area and starts
working. No window, no setup wizard, no elevation prompt.

---

## Tray menu

```
Status: guarding (12 nudges · next in 27s)
──────────────────────────────
Stop guarding                    �?command, bold, no checkmark
Settings�?                       �?also opened by double-clicking the icon
──────────────────────────────
�?Prevent sleep   �?Prevent display off   �?Disable screen saver
──────────────────────────────
�?Simulate mouse   �?Simulate keyboard (F24)   �?Only when idle
──────────────────────────────
�?Start at login
──────────────────────────────
Exit
```

Command items and checkboxes are deliberately different shapes. "Stop
guarding" is an *action*, not a state �?the state is already spelled out in
the text, and a checkmark on a verb misleads people into thinking they are
toggling a setting.

The tray icon is rebuilt only when the state actually changes, and the old
icon is explicitly disposed �?`NotifyIcon` does not take ownership of the
icon you assign it.

---

## Command line

| Command | Effect |
|---|---|
| `StayAwake.exe` | **Default**: tray icon, guards immediately, no window |
| `StayAwake.exe --gui` | Open the graphical window |
| `StayAwake.exe --no-autostart` | Stay in the tray but do not guard yet |
| `StayAwake.exe --silent` | No window, no tray icon (for Task Scheduler) |
| `StayAwake.exe --selftest` | Run diagnostics, print the report, exit |
| `StayAwake.exe --verbose` | Enable debug-level logging |

`--silent` additionally accepts:

| Flag | Effect |
|---|---|
| `--interval N` | Override the interval, 5�?600 seconds |
| `--no-mouse` | Do not simulate the mouse |
| `--no-key` | Do not simulate the keyboard |
| `--always` | Inject even while you are actively using the machine |

---

## How the lock screen is defeated

`SetThreadExecutionState(ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)` holds off
sleep and display-off. The lock screen is controlled separately and is not
affected by it, so StayAwake also injects input events:

- **Mouse** �?moves 1 pixel relative, then straight back. No visible movement.
- **Keyboard** �?presses **F24**. Almost nothing binds F24, so it has no
  side effects.

By default this only happens when you are genuinely idle, detected with
`GetLastInputInfo`. While you are typing, **not a single event is injected**.

The mouse nudge is *relative*, so the pointer never drifts toward the edge of
the screen.

---

## Configuration

`%APPDATA%\StayAwake\settings.ini`

```ini
Sleep=1
Display=1
Saver=1
SimMouse=1
SimKey=1
OnlyIdle=1
Interval=30
```

Writes are atomic (write a temp file, then replace), so a crash mid-write
cannot leave a truncated configuration behind.

---

## Start at login

StayAwake writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, so
**no administrator rights are required**.

The registered command line is **validated against the running executable**.
Move or rename the exe and the tray tells you that autostart now points
somewhere else, instead of quietly launching a stale copy at next boot.

---

## Logging

`%LOCALAPPDATA%\StayAwake\stayawake.log`

```
[INF] Config loaded | sleep=True display=True saver=False mouse=True key=True idleOnly=True interval=30s
[WRN] SendInput submitted only 0/2 events: Access denied (0x5) (UIPI if the foreground window runs elevated; security software may also block it)
```

The log exists because every failure mode used to look like the same
indirect symptom ("the counter stopped going up"). Power request failures,
`SendInput` being blocked by UIPI, an unwritable registry �?all of them are
now recorded with a concrete reason.

---

## Building from source

Requires only the .NET Framework 4.x compiler that ships with Windows itself.
No Visual Studio, no NuGet, no SDK download.

```cmd
build.cmd
```

Produces `StayAwake.exe` next to the script. `build.cmd /clean` removes
build artifacts first.

<details>
<summary>The compiler invocation</summary>

```cmd
"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" ^
  /nologo /target:winexe /platform:anycpu /optimize+ ^
  /warnaserror+ ^
  /win32manifest:app.manifest ^
  /out:StayAwake.exe ^
  /reference:System.dll /reference:System.Core.dll ^
  /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
  /resource:SA_ICON_GREY.png,StayAwake.SA_ICON_GREY.png ^
  /resource:SA_ICON_LIGHT.png,StayAwake.SA_ICON_LIGHT.png ^
  /resource:SA_ICON_RED.png,StayAwake.SA_ICON_RED.png ^
  /resource:SA_ICON_NAVY.png,StayAwake.SA_ICON_NAVY.png ^
  StayAwake.cs Log.cs UiKit.cs AutoStart.cs TrayIconFactory.cs ^
  TrayController.cs PanelForm.cs MainForm.cs Program.cs
```

</details>

A few things worth knowing:

- `/warnaserror+` �?the build is expected to produce **zero warnings**
- `/win32manifest:app.manifest` is **not optional** �?it declares PerMonitorV2
  DPI awareness. Without it the tray icon is blurry on scaled displays
- `build.cmd` **must stay pure ASCII**: `cmd.exe` reads batch files using the
  OEM code page, so non-ASCII comments turn into garbage commands
- The `SA_ICON_*.png` files are **sources**, not artifacts. They are embedded
  into the executable via `/resource`

---

## File layout

| File | Contents |
|---|---|
| `StayAwake.cs` | Win32 interop, options object, engine |
| `Log.cs` | Rotating file log |
| `UiKit.cs` | Palette, font cache, control factory |
| `AutoStart.cs` | Autostart registry entry, atomic settings persistence |
| `TrayIconFactory.cs` | Embedded resource loading, alpha trim, supersampling, multi-size ICO |
| `TrayController.cs` | Tray presence, menu, commit hub |
| `PanelForm.cs` | Settings panel (tray mode) |
| `MainForm.cs` | Graphical window (`--gui`) |
| `Program.cs` | Entry point, mode dispatch, `--selftest` |
| `app.manifest` | PerMonitorV2 DPI awareness |
| `SA_ICON_*.png` | Icon sources, 200×200 RGBA |

---

## Design notes

These are the non-obvious details this project has to get right. They are
written down because every one of them fails *quietly* �?the code compiles,
looks correct, and simply does not work.

**WinForms checkboxes need `CheckOnClick = true`.** With it `false`, clicking
does not change `Checked`, `CheckedChanged` never fires, and the menu item
does nothing at all �?while still looking perfectly fine in review.

**Writing `Checked` inside `CheckedChanged` recurses.** The setter fires the
event again, so a failed write turns into "write failed �?roll back �?write
failed �?roll back" until the stack overflows and takes the process with it.
Roll back by setting the checkbox behind a re-entrancy guard, and never by
calling the write function again.

**`ShowImageMargin` and `ShowCheckMargin` must be `true`**, or the checkmark
column is not drawn at all and `Checked = true` shows nothing whatsoever.

**`NotifyIcon.Text` is a system call.** Every assignment is a
`Shell_NotifyIcon(NIM_MODIFY)`. Writing it unconditionally on every tick
burns real CPU while nothing on screen changes. The same goes for setting a
menu item's `Text`, which forces a redraw.

**`NotifyIcon.Icon` does not take ownership.** The icon you assign is never
disposed by the control, so replacing it without disposing the old one leaks
a GDI handle every single time.

**`Control.Paint` does not fire for `Label` or `Panel`.** They default to
`ControlStyles.Opaque` and are not `UserPaint`, so a custom `Paint` handler
attached to them runs exactly zero times. You have to call
`SetStyle(ControlStyles.UserPaint, true)` yourself.

**A `Label` with a default position silently stacks.** Three group headings
that forgot to pass their `y` coordinate all land at `(0, 0)` and overlap
precisely, leaving only the last one visible. `UiKit.MakeLabel` therefore
takes `x` and `y` as *required* parameters with no defaults, so this class of
bug cannot be written at all.

**Never read the Win32 last-error code through a `SetLastError` import.** The
CLR saves and restores the last-error slot around such an import, so a
self-declared `GetLastError` marked `SetLastError = true` returns a value that
was just overwritten by the call reading it. Use
`Marshal.GetLastWin32Error()`.

**A failed call is not the same as an unavailable API.** `GetLastInputInfo`
returning `false` means *this call* failed, not that the API is missing.
Clearing the "is it available" flag on a `false` return disables the fallback
path permanently.

**Idle-detection failure must be "unknown", not "zero".** Zero idle
milliseconds means "the user just moved the mouse", so a failure path that
returns 0 makes "only when idle" skip *every* round �?the simulation stops
permanently and never says why. Failure returns `-1` and the caller decides
what to do.

**Renew the power request on the interval, not every second.** The request
expires; a one-second tick is wasted work.

**`SetThreadExecutionState` cannot stop the lock screen.** It only covers
sleep and display-off. Pretending otherwise produces a program that looks
like it works and still locks the screen.

---

## Diagnostics

`StayAwake.exe --selftest` checks the embedded resources, icon rendering for
every state, the screen saver read/write round-trip, power requests, idle
detection, input injection, the autostart registry entry, and the settings
file.

If the process has no console, the report is written to a file and its path
is shown in a dialog.

```
[Screen saver]
  [ OK ] Current state          enabled
  [FAIL] Write back original     SPI_SETSCREENSAVEACTIVE(0x0011) returned false
```

A screen saver failure is frequently the *machine's* state rather than a bug:
MSDN documents that the call fails with `ERROR_OPERATION_IN_PROGRESS` when
the machine is already locked or in a power-saving state. Distinguishing
"the environment did this" from "the code is broken" is exactly what the
self-test is for.

---

## Resource usage

Measured over 9 minutes in tray mode, sampling every 20 seconds:

| Metric | Start | End |
|---|---|---|
| GDI objects | 14 | 14 |
| USER objects | 12 | 13 |
| Working set | 30.7 MB | 31.6 MB |
| **CPU total** | 0.2 s | **0.4 s / 9 min** (�?.04% of one core) |

The two paths most likely to leak were stress-tested separately and showed
no growth: rebuilding the tray icon 1000 times held GDI at 7, and toggling
the settings panel 150 times showed a flat line from the 25th iteration
onwards (a one-time warm-up, not a linear leak).

---

## Known limitations

- The user interface and the log messages are **Chinese only**. English
  localization is not implemented; contributions are welcome.
- The lock screen is defeated by generating input events. That works against
  the ordinary lock screen, but not against a screen lock enforced by a
  third-party agent or by a BitLocker PIN prompt.
- If the process is killed from Task Manager, the screen saver state cannot be
  restored �?the process never gets to run its cleanup code. Logout, shutdown
  and normal exit are all covered.
- Manual locking (Win+L) and manual sleep are **not** prevented. Only the
  automatic ones are.

---

## License

MIT �?see [LICENSE](LICENSE).

Icon source: [Icons8](https://icons8.com), the verified_account series.
