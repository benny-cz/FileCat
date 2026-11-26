# E-I682 — On Linux, FileCat started without a usable X display crashed with a core dump instead of saying why

**Found:** 2026-11-26, by the release session while qualifying `v1.0.0-rc.7` on the Ubuntu VM ([rc.7](E-RC7-TAG-20261126.md#i682-found-on-rc7)). **Severity:** Low. A start that cannot succeed ended in a .NET stack trace and an abort, instead of a message. A core dump and Ubuntu's crash report and dialog followed. Nothing is read or written. **Disposition:** Must fix for 1.0.0, under the owner's rule of 2026-11-24 that every known issue is fixed.

## What happens

On Linux, FileCat opens its windows through X11: Avalonia's X11 platform, through XWayland on a Wayland desktop. When FileCat cannot open the X display, `AvaloniaX11Platform.Initialize` throws `System.Exception: XOpenDisplay failed`. That happens in two ways:
- **No display is set:** over SSH, in a text console, or after `su -` or `pkexec`, which drop `DISPLAY`.
- **The X server refuses the connection:** another account without the session's X authority.

`Program.Main` did not catch the exception. .NET printed "Unhandled exception" with the stack trace and aborted.

On the Ubuntu 24.04.5 VM, with rc.7's installed package:
- **The trigger:** FileCat was started from an SSH shell, as a control for [I681](E-I681-linux-census-zombie.md)'s check, with `DISPLAY=:0` and an empty `XAUTHORITY`. The session had copied the environment from gnome-shell's process, which holds no X authority.
- **The crash:** it ended by `SIGABRT`.
- **Ubuntu's response:** `/var/crash/_opt_filecat_FileCat.1000.crash` appeared ("Signal: 6, SignalName: SIGABRT"), and `apport-gtk` opened a crash dialog on the desktop.
- **Reproduced** with no display variables at all: exit status 134, and on stderr:

```
Unhandled exception. System.Exception: XOpenDisplay failed
   at Avalonia.X11.AvaloniaX11Platform.Initialize(X11PlatformOptions options)
   at Avalonia.AppBuilder.SetupUnsafe()
   at Avalonia.ClassicDesktopStyleApplicationLifetimeExtensions.StartWithClassicDesktopLifetime(AppBuilder builder, String[] args, Action`1 lifetimeBuilder)
   at FileCat.App.Program.Main(String[] args) in /home/runner/work/FileCat/FileCat/src/FileCat.App/Program.cs:line 65
```

The session deleted both crash reports its runs had made and ended the two crash dialogs they had opened. It changed nothing else on the VM.

**Not affected:** `--version` and FileCat's helper modes, which open no window and were tested without a display (the packages' smoke tests). Windows and macOS do not reach this path.

## Fix

[PR #72](https://github.com/benny-cz/FileCat/pull/72) (`src/FileCat.App/Program.cs`). `Main` now catches only Avalonia's `XOpenDisplay failed` exception, prints why on stderr, and exits with 1:
- **No display:** "FileCat could not open a window: no X display is set (DISPLAY). Start FileCat in a desktop session; on Wayland it uses XWayland."
- **A refused display:** "FileCat could not open a window on the X display ":0": the X server did not accept the connection. Start FileCat in your desktop session. Another account, such as root, also needs that session's X authority (XAUTHORITY)."

Any other exception still propagates.

**Tests** (`NoDisplayStartupTests`):
- **The message helper:** only that exact exception gets a message, and the message names the display.
- **On Linux, the app itself:** `FileCat.dll`, run by the test runtime's own host, started with no `DISPLAY`, and with `DISPLAY=:987`, which no X server answers. Each must end with exit 1 and the message, with no "Unhandled exception".

**Checked on the Ubuntu VM:**
- **With the fix:** the branch's App tests, built on the host and run with .NET 10.0.14, pass all three, and no crash report appears.
- **Without the fix:** rc.7's package, started the same way, ends with exit 134 and "Unhandled exception". That fails both of the test's assertions.

The unfixed build was not run through the test again, since every abort would open another crash dialog on the VM.

## The owner's decision and the merge (2026-11-26)

- **The owner chose rc.8:** asked to cut rc.8 with the fix or take rc.7 to GO with I682 open, the owner answered "Yes, make rc.8".
- **PR #72 merged** into main as `6b1c3419` after its full-matrix run 45823749293 passed all 19 jobs. In that run's Ubuntu App tests, both Linux cases ran and passed: no display, and `:987`.
- **What follows:** the fix reaches users in rc.8. On rc.8's installed package, the session repeats the start without a usable display, three ways.
