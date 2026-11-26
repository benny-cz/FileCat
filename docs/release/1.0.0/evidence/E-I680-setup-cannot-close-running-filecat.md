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

## Checked before the candidate, on CI's test Setup (2026-11-26)

PR #68's full CI run (`45746120969`, all 19 jobs passed) built the unsigned 0.0.0 Setup from the PR merged with main (`ea451d52`; SHA-256 `9923dbb703f9dcf3867bf2a9a293132b81c483c934ea30b6750bf68628d36786`). In the Windows 11 25H2 qualification VM, over the installed rc.5, an elevated scenario ran Setup and the uninstaller, as UAC's prompt would. The ordinary windows were started through Explorer, so they ran without elevation, as a user's do:

| Case | Result |
|---|---|
| T1 a silent upgrade with an ordinary window open | Setup exit 0, the test build installed. Restart Manager: "Shutting down applications using our files. (forced)". FileCat itself ended normally (exit code 0), and no FileCat process was left. |
| T2 the same with an administrator window open | Setup exit 0, FileCat exit code 0, no FileCat process left. |
| T3 a silent uninstall with an idle ordinary window | Uninstall exit 0. The log says "FileCat is running from this folder (three processes); asking it to close.", then "FileCat is closed." FileCat ended normally (exit code 0), with no file and no uninstall entry left. |
| T4 a silent uninstall while FileCat copies 8 GB | FileCat answered the close request with its "Operations are still running" question. Unanswered, it was ended after 10 s ("Ending FileCat", exit code 1). Uninstall exit 0, no file and no uninstall entry left. |
| T5 an interactive uninstall while FileCat copies | After the uninstaller's confirmation, FileCat asked its question. The uninstaller's box "FileCat is still running" offered Check again and End FileCat. Answering FileCat's question (Stop them and exit) ended FileCat normally (exit code 0). Check again then let the uninstall finish: "FileCat was successfully removed from your computer." Nothing was ended by force, and no file was left. |

Notes on the runs:
- T1 to T3 ran in `batch44v`. Its T4 and T5 failed in the harness, not in Setup: the 0.0.0 build's start-up update offer took the keyboard. In `batch44v2` the copy's start went unseen, because FileCat copies under a temporary name.
- `batch44v3` passed T4 but saw no question 5 s in. Its T5 looked for the uninstaller's boxes under the wrong process name: Inno 6.7's working copy is `_unins.tmp`. The diagnostics `diag44v`, `diag44v2` and `diag44v3` showed FileCat asking within 2 s of the close request in every combination: rc.5 or the test build, the offer answered through UI Automation or the keyboard, one process ID or all three, and the 64-bit or the 32-bit `taskkill` (the uninstaller is a 32-bit process).
- `batch44v4` ran T4 and T5 again and passed both. T4 looked at FileCat 3, 6 and 9 s in. The uninstaller's log did not exist yet at 6 s, and the question was there at 9 s. The uninstaller simply takes several seconds to start its working copy.
- Each run installed rc.5 again at the end. After `batch44v4` its uninstaller is `unins000.exe`. An earlier reinstall had named it `unins001.exe`, because `batch44v3`'s stopped T5 uninstaller still held `unins000.dat`.

On rc.6's own Setup the same cases run again.

## Records

The scenarios are in the Release Evidence Package's harness (`batch44x`, `batch44y`, `batch44w`, `batch44u`, `Invoke-FqI644Rest.ps1`; for the fix, `batch44v` to `batch44v4`, `diag44v` to `diag44v3` and `Invoke-FqI680Fix.ps1`). They lower UAC's consent prompt in the VM for their run, as batch28 does, and restored it each time (`ConsentPromptBehaviorAdmin` back to 5). The fix's runs left it at 5: an elevated scenario starts Setup and the uninstaller without a prompt.
