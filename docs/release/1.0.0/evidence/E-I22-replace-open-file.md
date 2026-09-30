# E-I22 — a replace refused because FileCat itself, or another program, still had the file open

Issue: [I22](../FILECAT_1_0_RELEASE_ISSUES.md#i22--replacing-a-file-that-is-open-failed-on-windows-with-a-misleading-access-denied-and-a-closed-comparison-kept-its-files-open).

## E-I22-D1 — the intermittent test failure that led here

`DirectoryDiffTests.Synchronize_previews_every_step_and_runs_only_the_chosen_ones_as_jobs` failed twice at the same
assertion — after Synchronize, the target's `changed.txt` still read "old" instead of "newer", while the new file beside
it had been copied:

- E-X01 run W1 (lent Windows 11 VM, unelevated, `be6ca25`, the whole App suite running), App TRX
  `9953ee6db49d35ce7029d121cc9544b6338c8aa69bc00210af5f8a293648cc5b`;
- CI run 36759824994 on `552aa62`, Windows ARM64 (23 s: both of the test's wait loops ran out).

Run on its own it passed 195 times (5 and 40 at `47c27b9`, 150 at `5c54181`, E-X01 W2 and W3); both failures happened
with the whole suite running in parallel.

## E-I22-M1 — mechanism (source and experiments)

1. The test compares `changed.txt` with its namesake (Enter in the directory comparison), which opens a content
   comparison holding both files through `FileContentSource`: read handles that share reading, writing and deletion
   (plan §9.5, "viewers never block other programs"). The test closes that window at once.
2. `CompareWindow.ReleaseWhenIdle` disposed the files only when `_loading` finished — the comparison's `async` method,
   whose last part runs on the window's (UI) thread. The test runs on that thread, so the files stayed open until it next
   yielded.
3. On Windows, replacing a file that another handle holds open fails even when that handle shares deletion. Probe on the
   host (Windows 11 26220, NTFS; script `i22-posix-rename-probe.ps1`
   `3953ef89fd94c9d5533514bc058336b7fb1784134e9dc5417e7ad24452d486a3`, output `i22-posix-rename-probe.txt`
   `12793488a0def26b8db268b934670997d2194eb00e41021e1a291f570863f2b5`):

   | Target | `MoveFileEx(REPLACE_EXISTING)` | `FileRenameInfoEx`, replace + POSIX semantics |
   |---|---|---|
   | not open | replaced | replaced |
   | open, sharing read, write and delete (FileCat's viewer) | error 5 (access denied) | **replaced**; the open handle still reads the old content |
   | open, sharing read and write only | error 5 | error 32 (sharing violation) |
   | open, sharing read only | error 5 | error 32 |
   | read-only attribute | error 5 | error 5 |

4. The Synchronize job "replace older files" publishes its staged copy with `MoveFileEx(REPLACE_EXISTING)`
   (`WindowsFileOperations.Move`). Error 5 is classified "access": no quiet retries, and the job asks "Could not replace
   the existing item: Access is denied. If the destination is a protected folder, Windows Controlled Folder Access may be
   blocking FileCat." In the test nobody answers, so the job waits and the file stays "old".

## E-I22-R1 — reproductions (physical host, working tree at `5c54181` plus the new tests)

- `CompareWindowTests.Closing_releases_the_files_while_the_windows_thread_is_busy` (new): opens a comparison of two real
  files, closes it, and keeps the window's thread busy. Unchanged code: **failed**, "Still open 10 s after the window
  closed".
- `OpenTargetReplaceTests.A_file_open_in_a_viewer_is_replaced` (new, Windows): a copy with Replace onto a file held open
  by a `FileContentSource`, as FileCat's own viewer (F3) holds it. Unchanged code: **failed**, the job asked "Could not
  replace the existing item: Access is denied. If the destination is a protected folder, Windows Controlled Folder
  Access may be blocking FileCat. (access)" and the file kept its old content.

## E-I22-V1 — fix `63d5fc4` and verification

- **Changes:** `CompareWindow` counts its load's reading among the runs the contents wait for and disposes them when the
  readers stop, without waiting for the UI thread. `WindowsFileOperations.Move`, when `MoveFileEx(REPLACE_EXISTING)` is
  refused with access denied, retries once as a POSIX-semantics rename of the file (`SetFileInformationByHandle`,
  `FileRenameInfoEx`, replace-if-exists; the source opened for deletion only, as itself if a link, write-through); a
  sharing violation from that attempt is reported as a sharing violation ("in use", with the job's quiet retries), any
  other refusal keeps the first error; folders, read-only files and file systems without such renames keep the classic
  behavior.
- **Tests:** `CompareWindowTests.Closing_releases_the_files_while_the_windows_thread_is_busy`;
  `OpenTargetReplaceTests.A_file_open_in_a_viewer_is_replaced_and_the_viewer_keeps_what_it_read` (replaced, no question,
  the open handle still reads "old", no staged file left) and
  `…A_file_held_without_sharing_deletion_is_reported_in_use_and_kept` (one question, class `sharing`, "in use", the file
  unchanged, no staged file left). On the unchanged code in a clean worktree at `5c54181` both Windows tests **fail**
  (the first asks "Access is denied … Controlled Folder Access", the second reports class `access`); on the fix they pass.
- **Regression:** host, elevated, all four suites (run `run-i22a`): Core 543, Remote 43, Platform.Windows 110, App 160 —
  0 failed. Lent Windows 11 VM, unelevated, `d40e510` (E-X01 W6): Core 544, Platform.Windows 110, Remote 43, App 160 —
  0 failed, and the whole App suite 15 more times — 0 failed. CI run 36773433835 (`d40e510`, which contains `63d5fc4`):
  all four lanes green.
- **Note on the flaky test itself:** the baseline did not fail in 195 isolated runs or 15 whole-suite runs at `5c54181`
  (E-X01 W2, W3, W5), so repeated runs cannot show the race gone; the deterministic tests above carry the verification.

## Limitations

- The two CI/VM failures were not captured with job diagnostics (added in `64ed037`, after both); the mechanism is
  established by the reproductions above, and consistent with the failures (the replace step alone missing; 23 s).
- The probe covers NTFS on one Windows build. Other file systems (FAT, exFAT, ReFS, SMB shares) may not support
  POSIX-semantics renames; the remediation must keep the classic behavior there.
