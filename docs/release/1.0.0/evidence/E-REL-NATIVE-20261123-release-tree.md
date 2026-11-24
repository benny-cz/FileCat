# E-REL-NATIVE-20261123 — Native Mac, Ubuntu and Windows runs of the release tree at `8a1e6763` and of the final tree `7ba3aa00`

2026-11-23 (UTC). **Release-branch validation; no candidate, tag or publication.** On the owner's request, the full regression lanes ran natively on the three test machines against the tree `release/1.0` took through PR #6, main `8a1e6763`. They are the same lanes as the r26/r36 and sync rounds.

Later syncs changed no product source relative to this tree except for the FTPS fix these runs found ([I631](E-I631-ftps-refused-data-final-reply.md)):
- PR #8 brought the release-candidate signing pipeline: workflows and `eng` scripts.
- PR #14 brought the I631 fix and moved the unlinked validation receipts out of the repository (PR #11).
- A later sync is to bring the two test-wait changes below and these records.

All three machines ran at the same time, by the owner's instruction to keep every machine busy.

## Source and tools

- **Packet:** `git archive` of main `8a1e6763d07ab0be042030d3024143edeb7cd435`. SHA-256 `2e71bf060db9031797e8560b9f775f6a255adb989cf8f97758667209fa45b565`, 1.16 GB.
- **Unix runner:** the repository's `run_unix_test_packet.sh` (SHA-256 prefix `98d43e33`). It replaced the machines' 12 November copy (`e3bf9d5f`), which was kept beside it.
- **Windows runner:** `run_windows_test_packet.ps1` (`13446ed5`). A copy of `invoke_windows_test_packet.ps1` started it, adding the runner's own `-Dotnet` parameter.
- **Windows SDK:** the VM had only a runtime-only .NET 10.0.15 install that another session had made, and no `C:\FileCatValidation\aw-claude`. SDK 10.0.101, the version `global.json` pins, was copied from the development host into `C:\FileCatValidation\sdk-10.0.101`. The zip's SHA-256 prefix is `2de1798d`; it holds the host's files unchanged, and nothing was downloaded.
- **Ubuntu SDK path:** the runner's `DOTNET_ROOT`, `~/FileCatValidation/archive-writing-42829297/dotnet`, no longer existed, and that broke both the old and new runner there. It is now a link to `~/.dotnet` (SDK 10.0.101).
- **Abandoned first attempt:** an earlier attempt on the PR #5 tree (`6611a72d`) was stopped when the owner asked for another sync, and its files were deleted.

## Lanes

- **Mac** (M1, macOS 27.2.1, 30 minutes):
  - Core in two shards, App, Rendering and Remote, all at once (`parallel:on`).
  - The published app and worker probe, the crash harness, `FILECAT_EDIT_NATIVE_REQUIRED=1`, and a 60-minute hang guard.
- **Ubuntu** (24.04.5, kernel 7.0, 4 processors, 44 minutes): the same lane.
- **Windows 11 VM** (build 26340, 8 processors, about two hours):
  - Lanes one after another: App; Core in two shards (`ChangeMonitorCadenceTests` apart); the Windows platform tests in two shards (`ArchiveAppContainerTests` apart); Remote; Rendering.
  - Native containment and TxF, and the published app and probe.

## Results

| Machine | Tests | Passed | Skipped | Failed |
|---|---|---|---|---|
| Mac | 20,198 | 19,140 | 1,057 | 1 |
| Ubuntu | 20,198 | 19,200 | 991 | 7 |
| Windows | 22,793 | 19,856 | 2,448 | 489 (487 from the lane's own setup; see below) |

On Windows, Core (12,422) and Remote (2,800) had no failures.

## Failures and their dispositions

- **Mac, 1:** `FtpIntegrationTests.FTPS_refuses_a_data_connection_presenting_another_certificate_within_the_session`.
  - After a correctly refused listing, a download of a file that was on the server reported "The file is not on the server."
  - The class passed alone (27/27), so the failure was intermittent. Its cause is a real client bug, [I631](E-I631-ftps-refused-data-final-reply.md), fixed in PR #9.
- **Ubuntu, 7:**
  - **`SecretStoreTests`:** the login keyring was locked ("Cannot create an item in a locked collection"), the known cold-boot state. It was not unlocked.
  - **`JsonFileStoreFifoTests` and `ArchiveWorkerCancellationTests`:** timing under load. Run alone, the two classes passed 25/25.
  - **Four `AnalysisRenderingTests`:** run alone, two passed. `NativeIndexedComparisonCatalogRouteUsesEntireRevisionsAndPagesBothRecordedSides` (Files and FileSize) failed again. The window still read "Comparing captured inventories … Working in the private inventory store" when the test helper's five-second wait ran out.
- **Windows, 489:**
  - **487 from the lane's own setup:** 447 `ArchiveAppContainerTests` and 40 `WindowsArchiveStorageTests` lacked `FILECAT_ARCHIVE_PROBE_ASSETS`, which the lane did not set.
  - **The re-run, with the variable set:** AppContainer passed 420 of 449 (0 failed, 29 skipped), and storage passed 259 of 260 (0 failed, 1 skipped).
  - **App, 1:** `OperationsDrawerLayoutTests` passed alone (18/18).
  - **Rendering, 1:** the native comparison wait above. In the re-run, the comparison passed and `IndexedPersistenceSavesTheWholeNativeRevisionAndRestoresPublishedSettingsWithoutMarks(grouped: True)` ran out of the same five seconds.

**The rendering waits.** A different test failed each time, always one waiting on native indexed work on a VM, while the Mac finished in time. PR #12 gives the comparison wait 30 seconds, as the cancel test in the same file already allowed. PR #13 raises the helper's default to 20 seconds, as waits on heavy work already pass. CI runs these tests only on Windows ARM64 and macOS.

## Evidence

Sealed run archives, on the development host under `E:\FileCat\.tmp\release-native-6611a72d\evidence` and on each machine. SHA-256 prefixes:

| Run | Archive | SHA-256 |
|---|---|---|
| `full-rel8a1e6763-mac` | `.tar.gz` | `d5b104869fd2ea5b` |
| `rerun1-rel8a1e6763-mac` (FtpIntegrationTests) | `.tar.gz` | `20b03814ddd87392` |
| `full-rel8a1e6763-ubuntu` | `.tar.gz` | `dbd64306a7ae7c66` |
| `rerun1-rel8a1e6763-ubuntu` (FIFO, cancellation, AnalysisRenderingTests) | `.tar.gz` | `920e78440eba3754` |
| `full-rel8a1e6763-win` | `.zip` | `17044324911d7919` |
| `rerun1-rel8a1e6763-win` (AppContainer, storage, drawer, AnalysisRenderingTests) | `.zip` | `1f25d9a641d4d428` |

## Second round: the final tree

The same lanes ran again on the tree main reached once PRs #11, #12 and #13 had merged.

- **Packet:** `git archive` of `2d07b72b`, a local merge, never pushed, of main `1ec1207f` with the heads of PRs #11, #12 and #13. SHA-256 `04e30c347b753fcc765df78a66b25f6fcd0dc4541283a6240616d936bd9522e6`, 89.5 MB; PR #11 removed the receipts that made the first packet 1.16 GB.
- **Identity:** its tree equals main `7ba3aa00`'s exactly, the merge of PR #12. `src` equals `1ec1207f`'s, so the product is the first round's plus the I631 fix.
- **Lanes:** the same runners and lanes as the first round. On Windows, `FILECAT_ARCHIVE_PROBE_ASSETS` is now set.
- **Cleanup first:** each machine's first-round run folders and packet were removed after its sealed archives were checked against the host copies.

| Machine | Tests | Passed | Skipped | Failed |
|---|---|---|---|---|
| Mac | 20,199 | 19,142 | 1,057 | 0 |
| Ubuntu | 20,199 | 19,205 | 991 | 3 |
| Windows | 22,794 | 20,317 | 2,476 | 1 |

- **Rendering:** it passed on both machines (988 passed, 10 skipped each). The longer waits of PR #12 and PR #13 held on Ubuntu, where four of these tests had run out of time in the first round.
- **Remote:** no failures on any machine. The Mac and Ubuntu ran the new I631 test, and it passed. The Windows VM has no pyftpdlib, the owned FTP fixture, so all 28 `FtpIntegrationTests` skip there, in both rounds; hosted CI's Windows x64 and ARM64 jobs run them.
- **Windows, 1 failure:** `OperationsDrawerLayoutTests.Recovery_section_stays_bounded_and_last_item_actions_are_scrollable(count: 5)`.
  - It failed at the same assertion as in the first round: after five Down presses, the last interrupted operation was not selected.
  - The class passed alone on the VM in the first round (18 of 18), and it passes in hosted CI.
  - Windows lanes run one at a time, so load does not explain it. Process state left by earlier App tests may; the cause is under investigation.
  - Every Windows suite had the same total as in the first round, apart from Remote's one new test. The 487 setup failures of the first round passed, with the probe assets set.
- **Ubuntu, 3 failures:**
  - **`SecretStoreTests`:** the locked login keyring again.
  - **`LinuxArchiveProcessTests.A_healthy_64_MiB_stream_proxy_copy_and_complete_readback_need_no_filesystem_grants`:** timed out at its 30-second limit beside four other test processes. It took 7.1 s in the first round, and run alone, the class passed 31 of 31. Its time-limited copy ran as a `Task.Run` body. [PR #15](https://github.com/benny-cz/FileCat/pull/15) runs it on its own thread under 120 seconds, as the repository's other hang guards do.
  - **`EscAuditTests.Esc_cancels_the_actionable_storage_review_and_returns_focus_to_the_file_list(keyboardOutside: True)`:** the test waited a fixed 60 ms for the review dialog to take the keyboard, then asserted that the file list no longer had it.
    - Run alone it failed again. In 10 more direct runs of the class, it failed 5 times, always in this case.
    - With PR #15, which waits up to 5 seconds for the focus to leave the list, 10 of 10 direct runs passed, as did the runner's own run (4 of 4).
    - On the Windows host at medium integrity, 10 of 10 runs passed with PR #15.
    - So the dialog does take the keyboard; on the Ubuntu VM it sometimes needed more than 60 ms.

| Run | Archive | SHA-256 |
|---|---|---|
| `full-relfinal2d07b72b-mac` | `.tar.gz` | `92eddac94041f4d7` |
| `full-relfinal2d07b72b-ubuntu` | `.tar.gz` | `c42d3efd2db9f724` |
| `rerun1-relfinal2d07b72b-ubuntu` (EscAuditTests, LinuxArchiveProcessTests) | `.tar.gz` | `0600bda4dc86a63c` |
| `rerun2-pr15-cb06d20f-ubuntu` (EscAuditTests with PR #15) | `.tar.gz` | `91ad27f11d0bb286` |
| `esc-before-after` (10 direct runs each, before and with PR #15) | `.tar.gz` | `fd1996d43b3ece9e` |
| `full-relfinal2d07b72b-win` | `.zip` | `f0dd2d1317d9e35d` |

The archives are on the development host under `E:\FileCat\.tmp\release-native-final\evidence`, and each machine keeps its own.

## Not shown

- A release candidate's qualification: no candidate exists, and nothing here is signed or installed.
- `SecretStoreTests` on Ubuntu with an unlocked keyring.
- The `MetadataPreparationTests` and inspector clipboard failures that hosted macOS CI showed the same day; see [E-REL-SYNC-20261123](E-REL-SYNC-20261123-release-branch.md).
