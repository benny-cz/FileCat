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

## Fix

[PR #46](https://github.com/benny-cz/FileCat/pull/46) (branch `fix/i655-root-profile-state`, from `rc3/integration-3` `3a76703d8`), as the owner decided (variant A). The code is commits `6aae2e26f`, `afa92fa11` and `0ad955e66`; the tests are `1185ff79f`, `4cd2b2a00`, `39cea1b0f` and `961aed11b`. Not merged, and in no candidate yet.

### What changed

**One decision.** `ProcessAuthority.IsElevatedOrRoot` (`src/FileCat.Core/State/ProcessAuthority.cs`) is FileCat's one "elevated or root" decision:
- Windows: a high-integrity token (`WindowsTokenAuthority.IsHighIntegrity`), as before;
- Linux and macOS: an effective user ID of 0 (`Environment.IsPrivilegedProcess`), whichever HOME the process has.

`IShellServices.IsElevated` (R-M12) returns it on every platform. The two checks that already covered root (`FileSystemOperations.cs:409`, `ReviewedPermissionOperation.cs:196`) use it too. Tests can assume it either way for their own asynchronous flow (`ProcessAuthority.Assume`, internal); other tests keep the real answer.

**Root keeps out of saved state, as an administrator window does.** The 45 guards marked "Extended to root" below use the decision. A FileCat running as root, with the caller's HOME or root's own:
- loads the default settings, history, workspaces and working sets, shows Settings read-only, and saves none of them;
- keeps no log and no crash report, and its diagnostics export copies none;
- lists, reads and writes no edit sessions, hex save journals or verification records;
- scans, prunes and offers for review no job journals, storage history or interrupted renames;
- trusts and saves no host keys or FTPS certificates of the profile;
- runs no Git status, no OpenPGP check with the profile's GnuPG configuration, and no SSH terminal;
- checks no file beside a checksum file by itself, keeps the archive members it shows in memory (at most 64 MiB), opens no nested archive and stages no drag;
- moves no folder back by Undo.

**Root starts no program that the profile chooses.** Windows' shell services already refused an administrator window file associations and terminals. `PortableShellServices` now refuses root the same: xdg-open and open, Linux's Reveal (xdg-open picks the file manager too), and terminals. Their associations, `$TERMINAL` and the shell's start-up files are found through HOME and `XDG_CONFIG_HOME`, which `sudo -E` leaves as the caller's. macOS's Reveal (Finder) still works.

**Nothing of root's in a profile.** A root FileCat on Linux and macOS keeps what it writes while it runs in a folder of its own (`RootSession`, `AppPaths.UsesRootSession`). That covers the journals of its own operations, temporary copies, the scratch of large listings, its instance files and its empty settings folders.
- The folder is `/tmp/filecat-root-<128 random bits>` (`/private/tmp` on macOS). It is made 0700 when FileCat starts, and used only if it is then a real folder of the effective user's own. It is removed when FileCat exits. It is never under `TMPDIR`, which `sudo -E` keeps as the caller's.
- `AppPaths.Resolve` gives root that folder as if FileCat had been started with `--data` there (`AppPaths.DataRoot`). Root therefore makes and changes nothing in the caller's profile or in a portable copy's data folder. It never forwards its arguments to the caller's running FileCat or takes over that FileCat's instance files.
- With `--data` root keeps the folder given. With a test root (`overrideRoot`) only the listing scratch and hex originals move to the session folder.
- An administrator window keeps its profile's folders, where Windows gives the account rights to what it makes.

**Wording.** Messages that said "as administrator" say "as root" on Linux and macOS, and "Start it (or FileCat) as your usual user" instead of "Reopen it without elevation". Windows' text is unchanged. This covers:
- the start-up notice, which says root started with the default settings and saves nothing;
- the save-failure notice, Settings' read-only reason, the diagnostics preview and the crash notice;
- edit sessions, interrupted renames, hex recovery, folder Undo and drag export;
- nested archives, members of unknown size, OpenPGP, host keys and FTPS certificates;
- the SSH terminal, R-M12's editor and tool gates, the Verified column ("not checked (root)") and the shell's refusals.

**Root helper modes.** None of them needed the profile state that root now refuses:
- `--filecat-recovery-process-census` (pkexec) is dispatched before any profile is resolved and reads only `/proc`;
- the Linux permission broker is the separate C service (`src/FileCat.Permissions.Linux`), not FileCat's .NET code;
- the picture worker (`--picture-worker`) also runs before any profile is resolved, and drops root through `SUDO_UID` (`UnixWorkerIdentity.Drop`) as before;
- the archive workers refuse root as before (`UnixArchiveJobBackend.cs:29`, `MacArchiveProcess.cs:73`).

**Found on macOS.** The record assumed that a root FileCat's profile lies under HOME on macOS too. It does not: .NET finds Application Support through the account's own home. A root FileCat started with the runner's HOME used `/var/root/Library/Application Support/FileCat`, root's own profile ([run 45466925421](https://github.com/benny-cz/FileCat/actions/runs/45466925421), macOS job). On Linux it used the caller's `~/.config/FileCat` and `~/.local/share/FileCat`, as the record says. On macOS HOME still reaches root through OpenSSH's known hosts, GnuPG, Git and terminals, which the extended guards cover; and under variant A root keeps nothing in its own profile either.

### Audit

`WindowsTokenAuthority.IsHighIntegrity` had 84 uses in 57 files of `src/` at the base, besides the property itself:

| Class | Uses | What happened |
|---|---|---|
| Extended to root | 45 | They protect saved state or other input that the ordinary account can change; they now use the one decision. |
| Windows side, same check | 4 | Windows-only code whose root counterpart was extended elsewhere (named in the table); same answer on Windows. |
| Root already; same check | 2 | They covered root through `Environment.IsPrivilegedProcess`; now the one decision. |
| Windows-only path, unchanged | 11 | They protect input that the ordinary account can change, on paths only Windows has; the Unix counterpart, where there is one, already refuses root (named in the table). |
| Windows token or UAC, unchanged | 22 | UAC broker requests, administrator transfers, EFS, NTFS storage, Windows security reads, the change journal. |

| Use at the base (`src/…`) | What it keeps from an elevated process | Class | Notes |
|---|---|---|---|
| `FileCat.App/App.axaml.cs:175` | crash notice: "persisted diagnostics are omitted" | Extended to root | `App.CrashNotice`; names root |
| `FileCat.App/Services/AppServices.cs:625` | save-failure notice | Extended to root | `AppServices.ElevatedSaveNotice`; names root |
| `FileCat.App/Services/DiagnosticsBundle.cs:18` | export preview: logs omitted | Extended to root | names root |
| `FileCat.App/Services/DiagnosticsBundle.cs:55` | export copies no profile log (a link there) | Extended to root |  |
| `FileCat.App/Services/GitStatusReader.cs:150` | Git runs its and HOME's configured programs | Extended to root |  |
| `FileCat.App/ViewModels/MainViewModel.Sftp.cs:511` | SSH terminal runs profile SSH and shell configuration | Extended to root | `SshTerminalRefusal`; names root |
| `FileCat.App/ViewModels/TabViewModel.Metadata.cs:86` | Verified column: no implicit check | Extended to root | says "not checked (root)" |
| `FileCat.App/ViewModels/TabViewModel.Verification.cs:61` | row check: no implicit check | Extended to root | `TabViewModel.ElevatedOn` |
| `FileCat.App/Views/SettingsDialog.cs:484` | read-only Settings: the reason | Extended to root | `SettingsDialog.ElevatedReadOnlyReason` |
| `FileCat.Archives/ArchiveProvider.cs:434` | nested archive spooled to the profile's temp | Extended to root | names root |
| `FileCat.Archives/ArchiveProvider.cs:872` | member of unknown size spooled to temp | Extended to root | names root |
| `FileCat.Core/Archives/ZipProvider.cs:182` | nested ZIP spooled to the profile's temp | Extended to root | names root |
| `FileCat.Core/Content/ProgressiveContent.cs:124` | member kept in memory, 64 MiB | Extended to root |  |
| `FileCat.Core/Content/ProgressiveContent.cs:224` | no disk-spool check without a disk spool | Extended to root |  |
| `FileCat.Core/Content/ProgressiveContent.cs:333` | member spooled to the profile's temp | Extended to root |  |
| `FileCat.Core/Diagnostics/Diagnostics.cs:57` | log file in the profile | Extended to root |  |
| `FileCat.Core/Diagnostics/Diagnostics.cs:124` | crash report in the profile | Extended to root |  |
| `FileCat.Core/Edit/EditSessions.cs:273` | edit sessions listed from the profile | Extended to root |  |
| `FileCat.Core/Edit/EditSessions.cs:1252` | edit sessions read or written | Extended to root | names root |
| `FileCat.Core/Jobs/InterruptedRenameReview.cs:24` | interrupted rename reviewed | Extended to root |  |
| `FileCat.Core/Jobs/InterruptedRenameReview.cs:49` | interrupted rename finished | Extended to root | names root |
| `FileCat.Core/Jobs/JobJournal.cs:789` | journals scanned, pruned and offered for review | Extended to root |  |
| `FileCat.Core/Jobs/JournalRecovery.Storage.cs:58` | storage history read | Extended to root |  |
| `FileCat.Core/Jobs/JournalRecovery.Storage.cs:139` | storage history listed | Extended to root |  |
| `FileCat.Core/Jobs/JournalRecovery.StorageCallExport.cs:13` | storage evidence exported | Extended to root |  |
| `FileCat.Core/Jobs/JournalRecovery.StorageCalls.cs:23` | storage evidence read | Extended to root |  |
| `FileCat.Core/Jobs/JournalRecovery.StorageReview.cs:362` | storage review artifacts | Extended to root |  |
| `FileCat.Core/Jobs/StorageEvidenceAccounting.cs:23` | storage history accounted | Extended to root |  |
| `FileCat.Core/Jobs/UndoRunner.cs:79` | folder moved back with what an ordinary writer put in it | Extended to root | names root |
| `FileCat.Core/Operations/DragStaging.cs:31` | drag copies staged for ordinary programs | Extended to root | names root |
| `FileCat.Core/State/JsonFileStore.cs:47` | settings, history, workspaces and other JSON state loaded | Extended to root | defaults |
| `FileCat.Core/State/JsonFileStore.cs:142` | JSON state saved | Extended to root | names root |
| `FileCat.Core/Verification/Signatures.cs:301` | gpg with GNUPGHOME or HOME's configuration | Extended to root | names root |
| `FileCat.Core/Verification/Signatures.cs:315` | gpg on content in memory | Extended to root | names root |
| `FileCat.Core/Verification/VerificationService.cs:121` | verification records written | Extended to root |  |
| `FileCat.Core/Verification/VerificationService.cs:136` | verification records read | Extended to root |  |
| `FileCat.Core/Verification/VerificationService.cs:336` | sidecars scanned on a listing refresh | Extended to root |  |
| `FileCat.Core/Verification/VerificationService.cs:363` | files checked by themselves | Extended to root |  |
| `FileCat.Core/Verification/VerificationService.cs:649` | status-line summary | Extended to root |  |
| `FileCat.Platform.Windows/HexSaveJournal.cs:48` | hex journals listed (they name a file and bytes) | Extended to root | runs on every platform |
| `FileCat.Platform.Windows/HexSaveJournal.cs:344` | hex journals read, written, applied | Extended to root | names root |
| `FileCat.Remote/Ftp/CertificateTrust.cs:32` | pinned FTPS certificates trusted | Extended to root |  |
| `FileCat.Remote/Ftp/CertificateTrust.cs:42` | FTPS certificate pinned | Extended to root | names root |
| `FileCat.Remote/Sftp/HostKeys.cs:77` | known hosts (FileCat's and HOME's .ssh) trusted | Extended to root | names root |
| `FileCat.Remote/Sftp/HostKeys.cs:110` | host key remembered | Extended to root | names root |
| `FileCat.App/Services/AppServices.cs:29` | Windows settings writer only below high integrity | Windows side, same check | root: `JsonFileStore.cs:47` |
| `FileCat.Platform.Windows/WindowsShellServices.cs:19` | per-user file associations | Windows side, same check | root: `PortableShellServices.Open`, `Reveal` |
| `FileCat.Platform.Windows/WindowsShellServices.cs:332` | terminals | Windows side, same check | root: `PortableShellServices` terminals |
| `FileCat.Platform.Windows/WindowsShellServices.cs:351` | the shell's `IsElevated` (R-M12) | Windows side, same check | root: `PortableShellServices.IsElevated` |
| `FileCat.Core/FileSystem/FileSystemOperations.cs:409` | links refused where an elevated process follows none | Root already; same check |  |
| `FileCat.Core/Permissions/ReviewedPermissionOperation.cs:196` | reviewed permission changes | Root already; same check |  |
| `FileCat.App/Services/ClipboardVirtualStaging.cs:43` | virtual clipboard files staged in the profile | Windows-only path, unchanged | Windows clipboard only |
| `FileCat.App/Services/WindowsContextMenu.cs:199` | Shell extensions registered per user | Windows-only path, unchanged | no Unix counterpart |
| `FileCat.App/ViewModels/MainViewModel.Registry.cs:394` | .reg backup in the profile | Windows-only path, unchanged | Windows Registry only |
| `FileCat.Core/State/WindowsSettingsFile.cs:200` | Windows settings writer | Windows-only path, unchanged | Windows only; root: `JsonFileStore.cs:142` |
| `FileCat.Platform.Windows/Archives/WindowsArchiveDirectory.cs:42` | archive writes in a folder an ordinary writer holds | Windows-only path, unchanged | root: `UnixArchiveJobBackend.cs:29` |
| `FileCat.Platform.Windows/Archives/WindowsArchiveJobBackend.cs:39` | archive writer (AppContainer workers) | Windows-only path, unchanged | root: `UnixArchiveJobBackend.cs:29`, `MacArchiveProcess.cs:73` |
| `FileCat.Platform.Windows/Archives/WindowsEditDirectoryLease.cs:48` | native edit storage | Windows-only path, unchanged | root: Unix lease refuses UID 0 |
| `FileCat.Platform.Windows/Archives/WindowsLegacyEditSource.cs:74` | old edit admission | Windows-only path, unchanged | root: `EditSessions.cs:1252` |
| `FileCat.Platform.Windows/Elevation/ElevationPaths.cs:60` | links above an item to delete | Windows-only path, unchanged | root: `FileSystemOperations.cs:409` |
| `FileCat.Platform.Windows/Shell/LocalIconFileLease.cs:46` | links followed for icons | Windows-only path, unchanged | Windows Shell only |
| `FileCat.Platform.Windows/Shell/LocalIconFileLease.cs:53` | links followed for transfers | Windows-only path, unchanged | root: `FileSystemOperations.cs:409` |
| `FileCat.App/Program.cs:34` | administrator link draft from the UAC broker | Windows token or UAC, unchanged |  |
| `FileCat.App/Program.cs:43` | administrator task from the UAC broker | Windows token or UAC, unchanged |  |
| `FileCat.App/Services/AdministratorTransferPreferences.cs:25` | administrator transfer store (registry) | Windows token or UAC, unchanged |  |
| `FileCat.App/Services/AppServices.cs:162` | drive read directly or through the UAC helper | Windows token or UAC, unchanged |  |
| `FileCat.App/Services/AppServices.cs:472` | NTFS storage qualification host | Windows token or UAC, unchanged |  |
| `FileCat.App/ViewModels/MainViewModel.Commands.cs:85` | EFS keys belong to the user's token | Windows token or UAC, unchanged |  |
| `FileCat.App/ViewModels/MainViewModel.PermissionInspector.cs:42` | Windows security read with the token's privileges | Windows token or UAC, unchanged | root: line 47 already |
| `FileCat.App/ViewModels/MainViewModel.Recovery.cs:308` | drive scan: administrator or UAC helper | Windows token or UAC, unchanged |  |
| `FileCat.App/ViewModels/MainViewModel.Recovery.cs:371` | drive scan text: reads itself or through the helper | Windows token or UAC, unchanged |  |
| `FileCat.App/ViewModels/MainViewModel.Recovery.cs:425` | disk scan text: reads itself or through the helper | Windows token or UAC, unchanged |  |
| `FileCat.App/ViewModels/MainViewModel.Workspaces.cs:261` | workspace transferred into an administrator window | Windows token or UAC, unchanged |  |
| `FileCat.App/ViewModels/OperationCenterViewModel.cs:166` | "Retry as administrator" (UAC) | Windows token or UAC, unchanged |  |
| `FileCat.App/Views/AppearanceTransferDialog.cs:29` | appearance transfer between sessions | Windows token or UAC, unchanged |  |
| `FileCat.Core/Analysis/AnalysisAdmission.cs:22` | retained Windows authority | Windows token or UAC, unchanged | root: line 14 already |
| `FileCat.Platform.Windows/AdministratorTransferRegistry.cs:74` | administrator transfer store (registry) | Windows token or UAC, unchanged |  |
| `FileCat.Platform.Windows/Permissions/WindowsPermissionService.Elevated.cs:44` | Windows security read with privileges | Windows token or UAC, unchanged |  |
| `FileCat.Platform.Windows/UsnJournal.cs:396` | change journal needs administrator rights | Windows token or UAC, unchanged |  |
| `FileCat.Platform.Windows/WindowsEfsKeys.cs:226` | EFS keys belong to the user's token | Windows token or UAC, unchanged |  |
| `FileCat.Platform.Windows/WindowsStorageReviewEvidence.cs:17` | NTFS storage evidence host | Windows token or UAC, unchanged |  |
| `FileCat.Platform.Windows/WindowsStorageReviewEvidence.cs:379` | NTFS storage evidence host | Windows token or UAC, unchanged |  |
| `FileCat.Platform.Windows/WindowsStorageTransforms.Efs.cs:135` | EFS as the invoking user | Windows token or UAC, unchanged |  |
| `FileCat.Platform.Windows/WindowsStorageTransforms.cs:49` | NTFS compression and EFS | Windows token or UAC, unchanged |  |

### Tests

New classes. Each case runs with the decision assumed, on every platform. In a test host started as root or elevated it also runs with the process's own decision, without the seam. The controls take the process to be an ordinary one.
- **Core `RootProfileStateTests`** (15 cases) **and `RootDiagnosticsTests`** (1):
  - JSON state, the log and crash report, edit sessions, job journals, interrupted renames;
  - the six storage-history entry points, folder Undo, drag staging;
  - verification records and implicit checks, OpenPGP (both entry points), member spooling and its 64 MiB limit;
  - nested ZIP, other archives (nested, and a member of unknown size);
  - the shell's associations and terminals, and root's session folder (Linux and macOS).
- **Remote `RootTrustStoreTests`** (2): host keys and FTPS certificates.
- **App `RootProfileStateUiTests`** (9):
  - settings, history, the log, the save-failure notice, the diagnostics preview and export, the crash notice;
  - the start-up notice, the editor and tool gates, the SSH terminal, the Verified column and row, Settings' read-only reason;
  - hex journals, Git status, Windows' shell services, the notices' wording on Linux and macOS;
  - the record's finite check with the caller's HOME. That case is gated by `FILECAT_TEST_CALLER_HOME`, because two of its steps write the account's real profile.
- `NotificationCenterTests` and `DiagnosticsExportTests` now expect root's wording on Linux and macOS.

**Before and after.** "Before" is the fix with every extended guard put back to its old check: the mutations below, all at once. The seam and the texts are kept so that the tests compile.

| Where | Decision | Before | After |
|---|---|---|---|
| Windows 11 (this host), test host at medium integrity | assumed | Core 16 of 16 cases fail, Remote 2 of 2, App 7 of 8 (the eighth checks wording only) | Core 16, Remote 2 and App 8 pass; the rest skip (the process's own decision, Linux and macOS only, the gated finite check) |
| Windows 11 (this host), elevated test host | the process's own (high integrity), and assumed | — | Core 31 of 31 pass; 2 skip (Linux and macOS only) |
| Ubuntu 24.04 VM, ordinary user | assumed | — | Core 17 and Remote 2 pass; the App class ran among the classes below |
| hosted `ubuntu-24.04`, root with the runner's HOME | the process's own (root), and assumed | Core 33 of 33 fail, Remote 4 of 4, App 12 of 13; the finite check fails at root's step and at the check step | Core 33, Remote 4 and App 13 pass; the finite check passes |
| hosted `macos-26`, root with the runner's HOME | the process's own (root), and assumed | — | Core 33, Remote 4 and App 13 pass; the finite check passes |

"Before" as root on Linux reproduced the record. Root used the caller's `~/.config/FileCat` and `~/.local/share/FileCat` and left `settings.json`, `settings.json.bak`, `history.json` and `history.json.bak` there, owned by root with mode 0600, and a `crash.log`. The ordinary user's check step then failed on them.

**Mutations.** There are 53: the 45 extended guards, the Windows side of two of them (`AppServices.cs:29`, `WindowsShellServices.cs:351`), the shell's decision and its four new refusals, and root's session folder. Each was applied alone, the tree was built, and the tests named for it were run with the seam. Every one was caught. 52 ran on this host at medium integrity. The session folder's mutation ran on the Ubuntu VM, since only Linux and macOS have that folder. Line numbers are those of `afa92fa11`.

| Test that failed | Mutations it caught (file:line in the fix) |
|---|---|
| `Saved_state_loads_as_the_defaults_and_nothing_is_saved` | `JsonFileStore.cs:48, :143` |
| `Neither_the_log_nor_a_crash_report_is_kept` | `Diagnostics.cs:61, :129` |
| `Edit_sessions_are_neither_listed_nor_written` | `EditSessions.cs:274, :1253` |
| `Job_journals_are_neither_reviewed_nor_pruned` | `JobJournal.cs:790` |
| `An_interrupted_rename_is_neither_reviewed_nor_finished` | `InterruptedRenameReview.cs:24, :49` |
| `Storage_history_is_not_read_accounted_or_exported` | `JournalRecovery.Storage.cs:58, :139`; `JournalRecovery.StorageCallExport.cs:13`; `JournalRecovery.StorageCalls.cs:23`; `JournalRecovery.StorageReview.cs:362`; `StorageEvidenceAccounting.cs:23` |
| `A_folder_is_not_moved_back_by_undo` | `UndoRunner.cs:84` |
| `Nothing_is_staged_for_dragging_out_of_an_archive` | `DragStaging.cs:38` |
| `Verification_records_are_neither_read_nor_kept` | `VerificationService.cs:122, :137` |
| `Files_beside_checksum_files_are_not_checked_by_themselves` | `VerificationService.cs:337, :364, :650` |
| `OpenPGP_signatures_are_not_checked_with_the_profile_configuration` | `Signatures.cs:301, :315` |
| `A_member_kept_in_memory_stops_at_64_MiB` | `ProgressiveContent.cs:124` |
| `A_viewed_archive_member_is_kept_in_memory_not_spooled` | `ProgressiveContent.cs:224, :333` |
| `A_nested_ZIP_is_not_spooled_to_be_opened` | `ZipProvider.cs:182` |
| `Other_archives_spool_no_nested_archive_and_show_no_member_of_unknown_size` | `ArchiveProvider.cs:434, :872` |
| `Root_starts_no_program_that_the_profile_chooses` | `IShellServices.cs:224, :85, :96, :163, :171` |
| `Pinned_certificates_in_the_profile_are_neither_trusted_nor_saved` | `CertificateTrust.cs:33, :43` |
| `Host_keys_in_the_profile_are_neither_trusted_nor_saved` | `HostKeys.cs:79, :112` |
| `Hex_save_journals_are_neither_listed_nor_read` | `HexSaveJournal.cs:52, :348` |
| `Windows_shell_associations_and_terminals_stay_refused` | `WindowsShellServices.cs:351` |
| `Settings_history_and_the_log_are_the_defaults_and_nothing_is_saved` | `App.axaml.cs:165`; `AppServices.cs:30, :625`; `DiagnosticsBundle.cs:18, :57` |
| `Git_status_is_not_read` | `GitStatusReader.cs:152` |
| `The_startup_notice_and_the_tool_gates_name_the_process_and_the_usual_user` | `MainViewModel.Sftp.cs:519` |
| `A_file_beside_a_checksum_file_says_it_was_not_checked` | `TabViewModel.Metadata.cs:86`; `TabViewModel.Verification.cs:69` |
| `Settings_open_read_only_and_say_why` | `SettingsDialog.cs:493` |
| `Root_keeps_what_it_writes_in_a_private_session_folder_outside_every_profile` | `AppPaths.cs:177` (Ubuntu VM) |

**Real root on hosted runners.** These ran on the throwaway branch `qual/i655-root`, which changed `details-arm64-qualification.yml` there only and was deleted after the runs. The tests ran as root with the runner user's HOME (`sudo -n env HOME=$HOME …`), in three steps:
1. the runner user saved a setting, a history entry and a log line in the usual profile;
2. root ran the finite-check case and the four new classes;
3. the runner user checked that it owns everything and can save a change.

| Run | Code | Tests | Jobs |
|---|---|---|---|
| [45466925421](https://github.com/benny-cz/FileCat/actions/runs/45466925421) | `0ad955e66` | `961aed11b` | Linux fix, Linux before, macOS fix |
| [45463078755](https://github.com/benny-cz/FileCat/actions/runs/45463078755) | `6aae2e26f` | `39cea1b0f` | Linux fix |
| [45463314262](https://github.com/benny-cz/FileCat/actions/runs/45463314262) | `6aae2e26f` | `39cea1b0f` | Linux before |

The final run's results:
- **Linux, fix:** every step passed. Root's own folders were outside the caller's profile, and nothing in HOME was owned by root afterwards (none before either). No session folder was left in `/tmp` after root's test hosts exited.
- **Linux, before:** root's step, all 33 Core and 4 Remote cases, and 12 of the 13 App cases failed, with the five root-owned files above. The check step failed, as the record's finite check would.
- **macOS, fix:** every step passed, and nothing new in HOME was owned by root. One file there was root's before the tests ran: a Control Center preference of the runner image.
- **Earlier runs:**
  - 45462285457 (fix) and 45462304525 (before) had one more failure, in the seed step: an assertion of the test itself, since fixed. Every root step of the fix passed.
  - 45463907009 (all jobs) failed on macOS in the root step, for the profile finding above; the test now takes the caller's folders from the seed step.

**Other classes** that touch the changed code:

- **Windows 11 (this host), test host at medium integrity**, at `1185ff79f`, with `TEMP` set to `%LOCALAPPDATA%\Temp`:
  - the whole Core suite: 12,507 cases, none failed, 1,430 skipped;
  - Remote `HostKeyTrustTests`, `CertificateTrustInputBudgetTests`, `FtpJobTests` and `RootTrustStoreTests`: 101 cases, none failed, 2 skipped;
  - the whole App suite: 4,089 cases, none failed, 313 skipped.
- **Ubuntu 24.04 VM** (kernel 7.0, an ordinary user):
  - at `1185ff79f`, 35 App classes: 509 cases, none failed, 51 skipped. They cover settings, notifications, diagnostics, recovery, interrupted work, storage history, edit sessions, the hex editor, temporary sweeps, security routes, verification, Git status, workspaces, start-up arguments and administrator actions;
  - at `afa92fa11`, 53 Core classes: 792 cases, none failed, 46 skipped. They cover state and paths, the log, edit sessions, job, archive and storage journals, interrupted work, Undo, drag staging, spooling, nested archives, verification and OpenPGP, the temporary sweeps and the shell's terminal selection;
  - at `afa92fa11`, the new Remote class: 2 cases passed, 2 skipped;
  - the whole Core suite at `1185ff79f` was stopped after 53 minutes. It had reported no failure and sat idle in a D-Bus call, most likely `SecretStoreTests`, which this VM's keyring is known to hang. That killed test host left its session folder in `/tmp`, removed by hand; every run that exited normally left none.

### Follow-up: reviewed recovery reports

The fix's author left `RecoveryReviewStore` without a guard. It keeps read-only snapshots of reviewed recovery reports in the profile's `journals/reviewed` folder. Two of its paths matter for an elevated or root FileCat:
- **Load:** the main window loaded these snapshots at start in every FileCat, an administrator window included. It now loads none when elevated or root, as `JournalRecovery.Scan` lists no journal there.
- **Save:** saving prunes old records in that folder, so an elevated save would delete in a folder the ordinary account can replace with a link (I645's pattern). Only the recovery of a listed journal saves, which an elevated or root FileCat never reaches. Save now refuses outright all the same.

New `RecoveryReviewStoreAuthorityTests` covers both. It fails without the guard ("Collection was not empty"). At medium integrity, the 11 App classes that use the store pass: 186 tests, 0 failed, 13 skipped.

**Left by design:** an administrator window writes the journals of its own jobs into the profile. They are never read back elevated (`Scan` lists none there). The ordinary window recovers an interrupted administrator job later, with ordinary rights, so no elevated step acts on a journal the ordinary account could change.

### Not verified

- **FileCat's window as root.** No root FileCat window was started on Linux or macOS. The test machines here have no passwordless sudo, and the hosted runners have no desktop session. The hosted runs drove the same code headless: the composition (`AppServices`), the start-up and Settings texts, the finite check's three steps, and the session folder's removal when the process exits. The window's own start (single-instance handling, the notice on screen) was not seen.
- **macOS's sudo defaults.** Whether macOS's sudo keeps HOME by default was not checked; the hosted runs passed HOME explicitly. The fix does not depend on it.
- **An administrator window.** No elevated FileCat window was started here. On Windows the guards give the same answers as before. The Core classes also ran in an elevated test host (high integrity); the App classes ran only with the decision assumed.
- **Two Windows mutations were not run:** `WindowsShellServices.cs:19` (file associations) and `:332` (terminals). A failing guard there would start a program or a console window on this workstation. Their tests pass, and both guards answer as before on Windows.
- **Root helper modes** were checked in the code, not run as root here. The census needs pkexec and a desktop; the picture worker's drop has its own tests, which were not rerun.
- **What root loses, by design:**
  - file associations, Linux's Reveal, terminals and the SSH terminal;
  - Git badges, archive members over 64 MiB, nested archives and drags out of archives;
  - folder Undo and saved settings.

  After a crash its session folder stays in `/tmp` (root's, 0700) until the system clears it.
- **Left as they were:**
  - `RecoveryReviewStore` (reviewed recovery reports) has no guard. An administrator window still lists the user's reports, read-only; root lists its own, which are none.
  - An administrator window still writes the journals of its own operations into the user's profile, where Windows gives the user rights to them.
  - The trusted minisign keys that an administrator window reads are still the user's; root reads its session's, so it trusts none.
- **The record's status line and the shared ledgers** are unchanged here.
