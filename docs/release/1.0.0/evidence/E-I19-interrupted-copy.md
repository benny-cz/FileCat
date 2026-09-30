# E-I19 — Interrupted-copy cleanup and Run again (plan V03-PARTIAL, DPI P03)

Classification: preliminary automated evidence on development builds (physical Windows 11 Insider host; CI hosted
runners). Not final qualification.

## E-I19-R1 — reproduction on the baseline

- **Core, source `4f6b062`** (new test file only, before any source change; Debug, physical host, 2026-09-30):
  `FileCat.Core.Tests.dll -class FileCat.Core.Tests.InterruptedCopyRecoveryTests` — 4 of 4 failed:
  - `A_complete_copy_from_a_second_source_folder_is_never_taken_for_a_partial_one`: `Assert.Empty() Failure: Collection
    was not empty` (a complete copy offered for deletion).
  - `A_partial_copy_from_any_source_folder_is_found`: expected `["one.txt", "two.txt"]`, actual `["one.txt"]` (a copy cut
    short from the second source folder not found).
  - `A_copy_changed_after_the_interruption_is_not_offered_for_deletion`: collection not empty (the user's edit offered
    for deletion).
  - `A_complete_copy_whose_source_changed_since_is_not_offered_for_deletion`: collection not empty.
- **App, source `4f6b062`** in a separate git worktree with only `InterruptedOperationUiTests.cs` added: the Run again
  dialog read "First, 2 partial files left by the interruption will be deleted." with no file names (the user-edited file
  counted as one). A throwaway variant of the test (not committed) confirmed the dialog: afterwards `edited.txt` read
  "as copied" — the user's edit "the user's own edit" had been deleted and the file copied again.

## E-I19-V1 — the fix (`f87ad32`)

- Targeted: the 7 `InterruptedCopyRecoveryTests` cases and `InterruptedOperationUiTests` pass (Debug, physical host).
- Affected regression, full solution Debug, `dotnet test FileCat.slnx -c Debug --no-build --logger "trx;LogFileName=i19.trx"
  --blame-hang-timeout 4m`, exit 0: Core 507 passed / 32 skipped, Platform.Windows 87/15, Remote 38/5, App 155/4.
  TRX SHA-256: Core `f37c997484c5511ba55b36cb9d459e8b25859191c6458533f6b9f511dcd14adc`, Platform.Windows
  `1492f07ed85dc708e748377b7ddfcfd90848e2dcd5972f078b60ea8ab6fc99a3`, Remote
  `62362a4a17b1a4e3a15ee75fb04e39248c9af198598a7dd7a201cf5b3fdaa5d6`, App
  `bfc60240498c8cc46b43e102fedd0fbeb19410a67b70a509db258a428822ebba`.
- CI run [36754000317](https://github.com/benny-cz/FileCat/actions/runs/36754000317) on `f87ad32`, from the job logs:
  Windows x64 Core 506/33, Platform.Windows 87/15 (+ FAT lane 1/0), Remote 38/5, App 155/4; Ubuntu Core 515/19 (+ device
  9, secrets 4, network 5, FAT 1), Remote 42/1, App 150/9 (+ WebKit 1); macOS Core 514/20 (+ device 8/1), Remote 42/1,
  App 150/9. Windows ARM64: Core 506/33, Platform.Windows 86 passed / **1 failed** — I20, unrelated to this change.
- CI run [36756346845](https://github.com/benny-cz/FileCat/actions/runs/36756346845) on `45efc09`: all four lanes green.
- Limitations: interruptions are simulated by removing the journal's end record after a completed job and truncating
  or editing files; a real process kill during a direct copy (V03-KILL) is still to be run on the final candidate.
