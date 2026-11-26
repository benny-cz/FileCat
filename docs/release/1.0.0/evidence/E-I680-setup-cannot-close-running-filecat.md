# E-I680 — Setup cannot close a running FileCat: an upgrade with FileCat open aborts when silent and stalls when interactive

**Found:** 2026-11-26, on the installed `v1.0.0-rc.5` in the Windows 11 25H2 qualification VM, while running I644's check "an upgrade or uninstall while an administrator window is open" ([E-I644](E-I644-administrator-window-environment-profiler.md)). **Severity:** Medium: an upgrade path. Nothing is lost or damaged: Setup installs nothing and FileCat keeps running. **Disposition:** Must fix for 1.0.0, under the owner's rule of 2026-11-24 that every known issue is fixed.

## What happens

rc.5's x64 Setup ran over the installed rc.5 with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`, started in the test account's session as a user would start it (`batch44x`, `batch44y`). Setup's log:
- "RestartManager found an application using one of our files: FileCat", and the same for `FileCat.ShellHost` and, with an administrator window open, for "FileCat administrator window" (the launcher).
- "Shutting down applications using our files." Then "Some applications could not be shut down."
- "Defaulting to Abort for suppressed message box (Abort/Retry/Ignore): Setup was unable to automatically close all applications…" Then "User canceled the installation process." Setup exits with code 5.

Every FileCat process was still running afterwards. It happened in each case tried:

| FileCat processes open during Setup | Setup's exit code |
|---|---|
| An administrator window and its launcher | 5 |
| One ordinary window with its icon helper | 5 |
| One ordinary window, its icon helper ended first | 5 |

An interactive Setup reaches the same message box: the user must close FileCat and choose Retry. A silent Setup, for example a managed deployment, installs nothing.

## Why

FileCat's main window has windowless helper processes: `FileCat.exe --storage-cloud-query` always, and `FileCat.ShellHost.exe --serve` while icons are shown. In the test account's own session, as Setup sees them (`batch44w`, `RmGetList` over the installed files):

| Process | Restart Manager's type |
|---|---|
| `FileCat.exe` (the window) | 1, a main window |
| `FileCat.exe --storage-cloud-query` | 0, an unknown application |
| `FileCat.ShellHost.exe --serve` | 0, an unknown application |

Restart Manager can only shut down an unknown application by force. `RmShutdown` without `RmForceShutdown`, which Inno Setup's `CloseApplications=yes` requests, returned 351 (`ERROR_FAIL_SHUTDOWN`) after 4.3 s, and closed nothing. With `RmForceShutdown`, what `CloseApplications=force` requests, it returned 0 and all three processes ended within 4.4 s.

## Uninstalling with FileCat open

rc.5's uninstaller ran silently (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`) with one ordinary FileCat window open (`batch44u`). It exited with code 0 and removed the uninstall entry, but:
- FileCat kept running: the window, `--storage-cloud-query` and `--serve`.
- 83 files stayed in `C:\Program Files\FileCat`. The log says "Failed to delete the file; it may be in use (5)" for them.
- The log has no Restart Manager lines. Inno Setup's uninstaller does not use Restart Manager, so `CloseApplications` does not reach it.

The scenario then ended FileCat and installed rc.5 again (Setup exit code 0), so the VM was as before.

## Fix

`eng/installer/FileCat.iss`, in two parts:
- **Setup:** `CloseApplications=force`. Setup then asks FileCat's window to close, as Restart Manager does for every window, and ends the windowless helpers, which cannot be asked. The helpers hold no state of their own. A main window that does not close in time, for example one with a copy running, is ended too. Its journal then recovers the interrupted operation, as when FileCat is killed (batch7c kills FileCat mid-copy, and its interrupted operations come back).
- **The uninstaller:** before it removes files, its `[Code]` closes the FileCat processes that run from the installation's folder. It finds them through Windows Management Instrumentation: `FileCat*.exe` processes whose executable is directly in that folder. Other programs there, and FileCat copies in other folders, are left alone. It asks their windows to close (`taskkill` without `/F`), as the close button would, and waits up to 10 seconds. A silent uninstall then ends what is left by force. An interactive one first offers Check again, so that the user can answer FileCat's own question about running operations or unsaved changes, or End FileCat.

CI compiled the x64 installer only on tags. Every full run now compiles it and keeps the unsigned 0.0.0 Setup for a week, so that the fix can be tried in the VM before a candidate is tagged.

The changes are in the installer only (PR #68). They reach users in a candidate after rc.5, whose own Setup must show, in the VM:
- an upgrade with an ordinary window open finishing silently, with FileCat closed;
- the same with an administrator window open;
- an uninstall with FileCat open, silent and interactive, leaving no FileCat process running and none of its files.

## Records

The scenarios are in the Release Evidence Package's harness (`batch44x`, `batch44y`, `batch44w`, `batch44u`, `Invoke-FqI644Rest.ps1`). They lower UAC's consent prompt in the VM for their run, as batch28 does, and restored it each time (`ConsentPromptBehaviorAdmin` back to 5).
