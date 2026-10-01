# E-DPI — Source review of the destructive-path inventory (plan §8.3, step 6)

Static review of the code behind each destructive-path row (DPI) of the release plan, at the commits named, with a test
for every defect before its fix. Rows reviewed earlier: P03 (I19), P15 (I15), P16 (I40). Preliminary: a review by the
executing agent, not the independent re-audit the plan requires before closure.

| Row | Reviewed | Outcome |
|---|---|---|
| P01 local/native/stream copy and move | `TransferExecutor` (staged and direct copies, publish, `DeleteMovedSource`, folder moves), `StreamTransferExecutor` | **I48**: a move across volumes checked its source unchanged but not that the copy was still at the destination before deleting the source; an antivirus quarantine or sync client taking the new file away left the source the only copy, and it was deleted (reproduced: gone from both places). Fixed `e72e3fc`. Folders are removed only when empty (`Directory.Delete(path, recursive: false)`); identity checks keep a source reached through a link. |
| P02 recycle/trash/permanent delete | `UnixTrash` (Linux, macOS), the share case of E-V08-S1 | No defect found: items move only within their own volume (renamed, never copied); a trash folder owned by someone else or replaced by a link is refused; trash folders are private; the `.trashinfo` file reserves the name by exclusive creation first. On a share without a Recycle Bin nothing is deleted without the agreed permanent deletion (lab). |
| P04 journal/staging/rename reconciliation | `JobJournal`, `JournalRecovery`, the hex-save journal, edit sessions | **I44**: a second FileCat on the same profile showed a running job as interrupted on Linux and macOS. Fixed `e399276`. The hex-save journal is held exclusively for the whole save and re-read before recovery; edit-session commits refuse a target that changed; staged leftovers are matched by the job's own prefix. |
| P08 ZIP rebuild/member edit | `ZipUpdateExecutor` | No defect found: the baseline is checked before and after the rebuild, the source is read with writers excluded, the rebuilt file is verified before `File.Replace` swaps it in (keeping security and the download mark), a failed or stopped rebuild deletes only its own temporary file, which the journal names for recovery. Residual: between releasing the read lock and the replace, a writer could slip in unseen (milliseconds). |
| P09 remote publish/delete/move/resume | `SftpJobs`, both channels, real servers (E-V08-L1, E-V08-L2) | **I49**: a move from a server deleted each copied folder whole, with whatever appeared there or changed after it was copied; a move to a server deleted a local source that changed during the upload. Fixed `e72e3fc`. Also from this row's lab work: I33, I35, I36, I46, I47. |
| P10 sync/results/working sets/bulk rename/links | `SyncPlanner.BuildRequests`, the delete, recycle and copy executors for Synchronize's jobs | **I50**: removals and replacements acted on whatever was at the path when the job ran; a target edited while the plan was reviewed was deleted or overwritten. Fixed `99145cf`: each goes only while the item is as compared. A folder's own time proved unreliable for that on NTFS (listed late; unchanged within a clock tick; blind to changes deeper in): since `efc128f` a folder goes only while all it holds is as compared. Bulk rename's journaled chains were reviewed with P04. |
| P11 attributes/times/recursive Unix chmod | `AttributesExecutor` | **I51**: a link chosen itself had read-only set through it on Linux and macOS (a chmod that follows links), changing its target. Fixed `65a76f8`. Recursion skips links; permissions were already refused for links; times are set on the link itself (checked on Windows and macOS). |
| P12 ADS/EA/xattr deletion/export | `WindowsHiddenData`, `UnixHiddenData`, the delete command of the hidden-data view | No defect found: deletion asks first (naming the download mark's role when it is among them); Linux and macOS read and remove extended attributes without following links (`l*xattr`, `XATTR_NOFOLLOW`); Windows opens a file's EAs on the item itself. Residual: on Windows a file symbolic link's streams are listed and deleted through the link, on its target — consistently, so the view acts on what it shows. |
| P14 recovery outputs and FileCat's write roots | `RecoveryProvider.CheckTransferDestination`, the recovery entry flow | No defect found: from a drive or disk, recovered files go only to another physical disk, and when that cannot be told, nowhere; scanning the system drive, or the disk holding FileCat's own state, says that Windows and FileCat keep writing there. |

## Tests added with the fixes

- I48: `TruthfulOutcomeTests.A_move_keeps_its_source_when_the_copy_is_gone_before_the_source_would_go` — before the fix the
  source was deleted ("Could not find file … report.txt"), after it the source stays and the job says why.
- I49: `SftpJobTests.A_move_from_the_server_deletes_only_what_it_copied_as_it_was_copied` (before: a file that appeared
  during the move was deleted, `NullReferenceException` reading it back) and
  `SftpJobTests.A_move_to_the_server_keeps_a_source_that_changed_during_the_upload` (before: the changed source was
  deleted); `RemoteLabTests.A_tree_moved_off_the_server_arrives_and_only_then_leaves_it` passes on OpenSSH, vsftpd and
  ProFTPD (the version re-check matches on real servers).
- I50: `SyncTests.Mirror_removes_a_target_item_only_while_it_is_as_compared` (before: "Could not find file … x.txt",
  deleted permanently) and `SyncTests.Mirror_replaces_a_target_file_only_while_it_is_as_compared` (before: overwritten
  by "left d").
- I50, folders (`efc128f`): `SyncTests.Mirror_removes_a_folder_only_while_all_it_holds_is_as_compared` (a change two
  levels down keeps the folder); the two CI-flaky cases now pass 40 of 40 runs on E: (10 and 5 failures in 40 before).
- I51: `AttributeLinkTests.Changing_a_links_time_or_read_only_never_changes_what_it_points_to` (before, on macOS: the
  target became read-only).

## Not reviewed in this pass

P05–P07 (hex saves beyond their journal, Registry, privileged work) and P13 (MTP: its checks need the phone, PPL-03).
