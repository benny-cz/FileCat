# E-I670 — An administrator window or a root FileCat checked for updates at every start, even with the check off

2026-11-25. Found by the documentation review for rc.3 (PR #53), which made the user documents say what rc.3 does.

## Finding

Since I645 and I655, an administrator window (Windows) and a FileCat running as root (Linux, macOS) keep out of the
saved state of the profile they find: they load the default settings and save none (`ProcessAuthority`). The default
turns the automatic update check on, and the daily gate's last-check time is never saved. So each such start asked the
GitHub releases API for FileCat's newest release, with FileCat's version in the User-Agent, even where the user had
turned the check off in Settings → Privacy. SECURITY.md counts "network requests while the update check is off" as an
in-scope privacy defect.

## Fix

`UpdateCheck.RunScheduledAsync` makes no automatic check in an administrator window or a root FileCat
(`ProcessAuthority.IsElevatedOrRoot`): such a process cannot see whether the user turned the check off, and the ordinary
window the user starts from checks as their setting says. Help → Check for updates still asks GitHub when the user
chooses it there.

## Tests

- `UpdateCheckTests.An_administrator_window_or_root_never_checks_at_startup`: with the process taken as elevated or root
  and default settings, the startup check makes no request and saves nothing; the control, an ordinary process with the
  same settings, checks once and records when. A stand-in replaces the network request (`UpdateCheck.CheckForTests`), so
  no request leaves the test host. Without the fix the case fails (one check in the elevated case).
- `UpdateCheckTests`, `UpdateDialogTests`, `SettingsDialogTests` and `RootProfileStateUiTests` at medium integrity on
  Windows: 33 tests, 0 failed, 8 skipped (they need a real elevated or root host).

## Documents

PRIVACY.md and ADR-15 say so (PR #53): an administrator window or a root FileCat never checks automatically.
