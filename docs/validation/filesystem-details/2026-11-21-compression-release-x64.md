# NTFS compression release on Windows 11 x64 — 21 November 2026

The Windows release build now offers NTFS compression: **Compress** and **Remove compression** for main file data and folder defaults. It does so on Windows 11 x64 in a process at normal integrity. This receipt records:

- the release composition;
- the fixtures added for the C gates that were still Partial;
- the runs on the Windows 11 x64 VM.

ARM64 is not enabled and EFS is not offered. The [volume admission receipt](2026-11-21-storage-volume-admission.md) covers the admission rules, the native identity cases, process kills and the 30,000,000,000-byte enable boundary.

## Release composition

Source commit `8ffd0c1a6` makes `WindowsPlatform` compose `WindowsStorageTransforms.CreateRelease()`. The adapter is composed only when the private storage evidence is qualified: a Windows 11 client, an x64 process on x64 Windows, and normal integrity. Every other host gets `NoStorageTransforms` with one reason. The command stays visible there, disabled with that reason, in the menu, Find and the command palette.

| Host | Reason code | Text |
|---|---|---|
| Administrator (high-integrity) process | `elevated-process` | Open FileCat as your usual Windows user to change NTFS compression; an administrator window does not change it. |
| Windows on Arm | `arm64-unqualified` | NTFS compression changes are not yet qualified on Windows on Arm. |
| Windows Server or Windows 10 | `windows-version` | NTFS compression changes in FileCat require Windows 11 (not Windows Server or Windows 10). |
| Any other host | `target-unqualified` | NTFS compression changes are not qualified on this system. |

The released adapter refuses both EFS actions (`efs-unqualified`). Neither EFS command is advertised, and the command palette offers neither.

## Fixtures added for the open C items

| Gate | Fixtures (commit) |
|---|---|
| T-LIFE.C | An actual worker handoff failure enters no worker, retires its owner once and publishes Failed; a handoff failure for one batch entry finalizes only that claim. Stop while a launch claim is held resolves it once. A throwing Running observer in a multi-entry batch starts each claim once. A JobAdded observer that cancels retires the unstarted owner once. Paused unstarted requests may be overtaken with a truthful wait state; an active or paused owner is never bypassed. Unrelated local and remote jobs wait only for the global storage reservation (`94164190f`). Pause after call admission neither denies nor steals the admitted call and writes no intent until Resume; intent-flush and no-call pairing faults admit no call and stay pending review (`2b240d189`). Exit during an admitted call waits for that call and starts no other (`3e4c1e4a9`). |
| T-REC.C | Crash temporaries from an earlier process are reconciled once before the next save. Leftovers that cannot be removed or proven free stay charged and reported (`e2ad224f8`). The static interruption check applies the collector to stray, Failed and Canceled end records (`af196eed2`). Interrupted-operation actions refuse storage history without touching its journal (`3e4c1e4a9`). A history review shows a fresh, read-only observation of each recorded object apart from the history (`5f7593981`). Process kills at three call phases are in the [admission receipt](2026-11-21-storage-volume-admission.md#process-kill-t-recc). |
| T-LIMIT.C | Repeated native failures exhaust the record budget with a truthful incomplete outcome. Fresh passes spend no records on a completed or excluded prefix (`70a07d43a`). The byte limit keeps fixed root and closure capacity and reserves a whole call before each intent. History slices end before 64 candidates when the 1 MiB header slice fills, and an oversized first record is a bounded damaged row (`af196eed2`). A full native owner, a full pin review, a history cursor and a live report use separate bounded lanes, 256 + 188 + 1 + 3 handles (`7907d0be9`). |
| T-TRAV.C | A partially processed directory moved to another root leaves both roots incomplete. Churned cursor entries and objects on another volume are not mutated. Sharing conflicts retry twice and then ask one job-scoped decision; a sharing failure inside an admitted call is uncertain and never retried (`70a07d43a`). Native directory sequences on the VM cover Stop after a folder default changes and a no-op directory whose default changes before descent. A directory that changes after its enumeration began is not promoted (`3e4c1e4a9`). |
| T-CAP.C | A compression job completes without consulting the EFS, key or stream services its adapter also offers (`94164190f`, `70a07d43a`). The reason matrix, protected tree and cloud breadth are in the [admission receipt](2026-11-21-storage-volume-admission.md#protected-tree-cloud-breadth-and-reason-matrix-t-capc-n-idc). |
| T-UI.C | Retry while root inspection is blocked keeps one check in flight and only the latest request. Stale and capability-refused roots expand the disclosure with their reason and block submission (`21fe528b9`). Find refuses storage while its search is still running, and closing Find cancels its review before or after the actionable milestone. The blocker escape selects the running operation in the owner window. An initial listing invalidation closes the review and drains a late native pin without submitting (`449142be0`). |
| T-REFRESH.C | Completion refreshes the captured result tab, keeps incomplete marks and forgets observations. It unmarks only when navigation and mark intent still match. Closing the origin tab after the actionable review keeps the job and cleans no other tab. Find coalesces a burst of storage completions into one refresh (`449142be0`). |
| T-REG.C | The review has fixed wording, exactly two actions and no EFS controls (`21fe528b9`). Escape cancels the actionable review and returns focus to the file list (`70fe0c508`). Compression adds one More FileCat actions entry without changing primary menus, the toolbar or the key bar (`449142be0`). The released composition and the unsupported-build reason are covered in Find and the palette (`8ffd0c1a6`). |

## Runs

**Environment:** the Windows 11 Pro Insider build 26340.9843 x64 VM from the admission receipt, with Windows Update and Defender real-time protection disabled. Test processes ran at medium integrity, lowered by `eng/validation/Enter-MediumIntegrityTestProcess.ps1`, on the .NET 10.0.15 runtime. Each lane checked that no other test process was running.

**Source:** commit `9c07d5b16` for all runs except the rerun, which used `7f493e217` (the two test fixes below).

| Run | Classes | Result | Evidence |
|---|---|---|---|
| Native storage regression | `WindowsStorageTraversalTests`, `WindowsStorageExecutorTests`, `WindowsStorageAdmissionTests`, `WindowsStorageCrashTests`. Test DLL `79A1BF2F…`, platform DLL `B6254882…` | 77 total, 66 passed, 0 failed, 11 skipped | [TRX](receipts/2026-11-21-compression-release-x64/storage-native-regression.trx) `946492ea…`, [log](receipts/2026-11-21-compression-release-x64/storage-native-regression-run.log) |
| Core storage machinery | 19 classes: scheduler claims, call admission and evidence, contract, current observation, evidence accounting, cursor and envelope, execution plan, executor, history discovery, journal, native allowance, call runner, resource lanes, acknowledgment, ownership, selection review, visit ledger. Test DLL `F931807B…`, Core DLL `B138527D…` | 213 total, 213 passed | [TRX](receipts/2026-11-21-compression-release-x64/core-storage.trx) `30939b74…`, [log](receipts/2026-11-21-compression-release-x64/core-storage-run.log) |
| App storage, dialogs and regression | 16 classes: storage command integration, compression dialog, storage operations, dialog buttons, Escape audit, EFS backup dialog, history startup, native storage commands, metadata preparation, permissions, attribute colours, operation scope and report, interrupted operations, accessibility, command search. Test DLL `30537653…` | 221 total, 206 passed, 1 failed, 14 skipped | [TRX](receipts/2026-11-21-compression-release-x64/app-storage-ui.trx) `1d33fbf5…`, [log](receipts/2026-11-21-compression-release-x64/app-storage-ui-run.log) |
| Rerun after the test fixes | `StorageCommandIntegrationTests`. Test DLL `F1D20435…` | 45 total, 45 passed | [TRX](receipts/2026-11-21-compression-release-x64/app-command-integration-rerun.trx) `4a613810…`, [log](receipts/2026-11-21-compression-release-x64/rerun-run.log) |
| Native App commands, `FILECAT_TEST_STORAGE_UI=1` (`7f493e217`) | `StorageCommandNativeTests`, real `WindowsStorageTransforms` through the App command and job. Test DLL `F1D20435…` | 12 total, 5 passed, 7 EFS variants skipped. Passed: completion marks guarded across refresh, marks and navigation; recursive refresh of parent and descendant tabs that keeps other marks; a scoped Find refresh that keeps later marks. | [TRX](receipts/2026-11-21-compression-release-x64/app-native-commands.trx) `c46b7cc0…`, [log](receipts/2026-11-21-compression-release-x64/app-native-commands-run.log) |
| Cloud Files from another process (`13434210a`) | Storage review and a recursive Compress job run from a child process while the test serves the sync root, plus the provider-process fixture. Test DLL `04A59434…` | Both passed. From the child (placeholder mode 1), the root was refused as `cloud-sync-root` and the pinned placeholder as `cloud-entry`. The recursive job changed 2 and skipped 1, and the provider saw no data request. | [TRX](receipts/2026-11-21-compression-release-x64/cloud-cross-process.trx) `7725703e…` |
| Ubuntu 24.04.5 x64 and macOS 27.2.1 Apple silicon (`7f493e217`) | `FileDetailsWindowTests`, `StorageCommandIntegrationTests`, `StorageOperationsUiTests` | 85 total, 0 failed, 6 skipped on each | [Ubuntu TRX](receipts/2026-11-21-compression-release-x64/app-ubuntu.trx) `61dce38b…`, [Mac TRX](receipts/2026-11-21-compression-release-x64/app-mac.trx) `4ad76753…` |

**Skips:**

- Native storage regression: the prepared-fixture variants (full volume, WOF, small units, mount folder, no-filter volume, hot-plug), the virtual-disk device-loss fixtures, the crash child half and the EFS fixture. These ran separately in the admission receipt.
- App: 12 skips are the native command fixtures, which need `FILECAT_TEST_STORAGE_UI=1`. They ran in their own lane below. The rest are one POSIX-permissions test and one EFS fixture.

### Release build with NVDA (N-UI.C)

The published self-contained win-x64 build ran with `tests/FileCat.NativeAcceptance/win-nvda-compression-release.ps1` from commit `13434210a`:

- `FileCat.dll` `235C47E5…`, `FileCat.Platform.Windows.dll` `F00C8CC1…`;
- portable NVDA 2026.3.1 in session 1;
- the owned window at 640 × 420 DIP and 96 DPI;
- keyboard input only, at medium integrity.

**Fixture:** a folder holding a file with a named stream and a read-only file, plus an unselected sibling.

**Result:** passed. See the [result](receipts/2026-11-21-compression-release-x64/nvda-release-result.json) (`03ad3651…`), [NVDA speech](receipts/2026-11-21-compression-release-x64/nvda-release-speech.log) and [run log](receipts/2026-11-21-compression-release-x64/nvda-release-run.log).

- **No EFS:** the main window and both reviews exposed no EFS control. Searching the command palette for "EFS protection" and "Back up EFS key" offered neither.
- **Review wording:** each opening showed the current-state notice and the alternate-stream sentence, started with **Selected items only**, and kept the footer disabled until an action was chosen.
- **Speech:** NVDA announced the dialog, the focused **Compress** or **Remove compression** radio, its checked state and the inspection result. It also announced the **Selected folders and their existing contents** radio and its checked state.
- **Compress:** the result read "2 changed, 0 already satisfied, 1 skipped, 0 failed, 0 uncertain, 0 not processed. The requested scope was not fully completed." The folder and its writable file were compressed. The read-only file kept its attribute and stayed uncompressed, and the sibling was untouched.
- **Remove compression:** the result read "2 changed, 1 already satisfied, 0 skipped, 0 failed, 0 uncertain, 0 not processed. The requested scope was fully checked." All payload hashes and the named stream were unchanged.
- **No progress figures:** neither result nor its operation text contained a percentage, rate or time estimate.
- **Focus return:** after each review, FileCat returned keyboard focus to the reviewed folder by itself, and NVDA announced it.

**Observation:** focus returned while the completion refresh was replacing the listing. NVDA first described the row as "unavailable. This item belongs to a previous listing and cannot be acted on." It then read the result and announced the refreshed row: "folder-release, not selected, folder, …, 2 of 3". The next command acted on the refreshed row. The stale description is brief extra speech, not a wrong target.

A first attempt failed in the runner, not in FileCat. The dialog opens with focus already on the first action, so that radio's name was announced before the runner began listening. Choosing it then produced only "checked". The runner now hears the focused radio's name and its checked state separately.

### Findings during the runs

- **The release-composition test never saw the shipped adapter.** It opened the main window on the headless test platform, which registers no Windows platform. On an elevated host both sides of its comparison were false, so it passed by accident. At normal integrity on the VM, compression was qualified but the test window had no adapter, and the test failed. Commit `7f493e217` composes `WindowsPlatform` for that window and reports the availability reason on failure. The rerun passed with compression offered and both EFS commands absent.
- **Cloud Files placeholders look different to FileCat.** The earlier cloud fixtures ran in the provider's own process, which sees placeholders as reparse points. FileCat runs in Windows's default mode, which disguises them as ordinary files with cloud attributes. Compression still refuses them, via the sync-root query and the cloud attributes, as the cross-process run shows. See the [native listing receipt](2026-11-21-windows-native-listing-x64.md#finding-cloud-files-placeholders-are-disguised-for-filecat).

## Limits

- Windows 11 x64 only, on an Insider build. ARM64 has its own Partial results and stays disabled.
- Compression changes run one at a time under the global storage reservation, with two durable flushes per change. FileCat does not freeze names: a renamed or replaced item is refused or reported, never followed, and no alias exclusion is claimed.
- The optional categories in the [admission receipt](2026-11-21-storage-volume-admission.md#limits) stay refused: removable and hot-plug devices, large sparse removal, and the other refused locations.
