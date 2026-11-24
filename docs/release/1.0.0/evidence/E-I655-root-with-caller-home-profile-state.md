# I655 — A root FileCat started with the caller's home folder reads and writes the caller's saved state

**Found:** 2026-11-24 by I645's fix ([PR #33](https://github.com/benny-cz/FileCat/pull/33), its "Not verified" list), confirmed in the source of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (`7269ec61`). **Status:** open; to be fixed for rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate), as proposed below: root keeps out of the caller's saved state as an administrator window does. It is not new in rc.2.

## Observation

On Windows, an administrator window keeps out of the ordinary account's saved state, which a process at medium integrity can change:
- `JsonFileStore.Load` returns the defaults and `SaveCore` refuses (`JsonFileStore.cs:47`, `:142`). These cover the settings, history and the other JSON state.
- `AppLog.Initialize` writes no log and `AppendCrash` no crash report (`Diagnostics.cs:55`, `:97`).
- Edit sessions, job journals, storage history, interrupted renames, verification records and drag staging have their own refusals (`EditSessions.cs:273`, `:1254`; `JobJournal.cs:789`; `JournalRecovery.Storage.cs:58`, `:139`; `InterruptedRenameReview.cs:24`, `:49`; `VerificationService.cs:121`, `:136`, `:336`; `DragStaging.cs:31`).

Each of these checks only `WindowsTokenAuthority.IsHighIntegrity`, which is false on Linux and macOS. FileCat finds its profile through .NET's `ApplicationData` and `LocalApplicationData` (`AppPaths.cs:151-152`), which on Linux and macOS lie under `HOME` (on Linux also `XDG_CONFIG_HOME` and `XDG_DATA_HOME`).

So FileCat started as root with the caller's `HOME`:
- loads the caller's settings and state;
- writes its log in the caller's profile;
- keeps its edit sessions and journals there;
- reads the journals it finds there.

That happens with `sudo -E`, and with `sudo` wherever its configuration keeps `HOME`; macOS's default configuration is widely reported to, but that was not checked here.

Some root checks already exist:
- storage analysis (`AnalysisAdmission.cs:14`);
- archive workers (`MacArchiveProcess.cs:73`, `UnixArchiveJobBackend.cs:29`);
- permission changes (`ReviewedPermissionOperation.cs:196`);
- the file operations' own elevation check (`FileSystemOperations.cs:409`).

I645's fix adds root to the start-up sweeps, the page view and the folder modes, through `IShellServices.IsElevated` (R-M12).

## Why it matters

- **State that the ordinary account can write, acted on as root.** The settings name programs (Tools, the editor, the terminal), and the journals name files to roll back, clean up or recover. A program running as the caller, without root, can change both before the caller next starts FileCat with sudo. On Unix that account can already change what sudo runs through the shell's start-up files and `PATH`, so such a program gains less here than its Windows counterpart in I645.
- **Files the ordinary FileCat can no longer read.** State files are created owner-only (`JsonFileStore.cs:164`). A settings or history file that root saves in the caller's profile therefore belongs to root with mode 0600. The ordinary FileCat cannot read it afterwards, and a folder that root created there cannot take its saves.

**Not run.** Neither the test Mac nor the Ubuntu VM offers sudo without a password, so no root FileCat was started. This rests on the source.

## Remedy (proposed) and its cost

Treat root as Windows treats an administrator window wherever the code keeps the user's saved state, through the canonical "elevated or root" check (`IShellServices.IsElevated`). That means defaults with nothing saved, no persisted log, and no edit sessions or journals. A narrower variant refuses only when the profile folder belongs to another account than root, so that a root account with its own home keeps its state. Either changes product source and the root behaviour on Linux and macOS, so it reaches users only in a candidate after rc.2.

## Finite check

On Linux with `sudo -E`, or on macOS with `sudo`, after the ordinary FileCat has saved a setting:
1. The root FileCat shows the defaults and says that it saves nothing.
2. After it closes, the caller's profile holds no file or folder owned by root.
3. The ordinary FileCat's next start reads its settings and saves a change.
