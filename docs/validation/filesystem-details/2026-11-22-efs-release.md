# EFS release on Windows — 22 November 2026

The owner chose to pursue the EFS release with the scope proposed in the [EFS release audit](2026-11-22-efs-release-audit.md). This receipt records the release composition, a setup defect found on the way, and the runs that closed the audit's concrete gaps.

## Release composition

- **Composition:** `WindowsStorageTransforms.CreateReleaseWithEfs` composes the EFS job candidate for the App on a release host: a native x64 or native Arm64 Windows 11 client process at normal integrity. Setup files go under FileCat's private local state. The key backup wizard and parented key comparisons run through UI-thread callbacks. `AppServices` uses it for the shipped Windows platform.
- **Editions:** the EFS edition gate is unchanged: Pro, Enterprise and Education, with their N variants. On other editions both EFS commands stay unadvertised, compression stays available, and an EFS request is refused with "Changing EFS protection in FileCat is qualified on Windows 11 Pro, Enterprise and Education; this Windows edition is not."
- **Drives:** EFS review goes through the same drive admission as compression. Remote, removable, hot-plug and non-NTFS drives are refused. The remote refusal now names both features.
- **Outputs:** each E-OUT route keeps its decided behavior: preserve EFS, ask before losing it, or refuse before the first payload. The audit's open notes record how far the evidence reaches, not missing behavior.

## Setup defect: a drive without a Cloud Files filter

On the Windows on Arm VM, the Cloud Files filter was loaded but attached to no drive, as on a fresh Windows without an active sync provider. EFS setup then refused with `efs-setup-managed`, "The setup namespace is cloud-managed or unavailable", and the encryption job failed before any native call ([first attempt](receipts/2026-11-22-efs-release/arm64-qualification-canceled-key-first-attempt.json)). The setup namespace check used the strict membership query, which refuses whenever no filter instance answers.

Setup now uses the rule compression already uses for objects reached by its held walk. The application directory is the leaf of the held, no-follow walk, and the setup directory is opened from it without following reparse points. A drive where no filter answers is admitted only when no sync root of any account is registered on it, and neither directory may be reparse-backed.

`Setup_runs_on_a_drive_where_no_Cloud_Files_filter_answers_and_no_sync_root_is_registered` runs only where the filter does not answer. On the Arm VM the membership query returned `0x80070001` and setup matched the current key ([TRX](receipts/2026-11-22-efs-release/arm64-setup-no-cloud-filter.trx), [log](receipts/2026-11-22-efs-release/arm64-setup-no-cloud-filter.log)). On the x64 VM the filter answers, so the test skipped and the ordinary setup tests passed.

## Runs

**Machines:** the Windows 11 Pro on Arm VM from the [screen-reader receipt](2026-11-22-windows-arm64-screen-readers.md) (build 26300) and the Windows 11 Pro Insider x64 VMware VM (build 26340). Every run was in the signed-in desktop at medium integrity. Each VM got a fresh software EFS key as its current key before the key-backup runs ([Arm64](receipts/2026-11-22-efs-release/arm64-fixture-key.json), [x64](receipts/2026-11-22-efs-release/x64-fixture-key.json)).

**Builds:** self-contained Release builds of this change. Arm64: `FileCat.dll` `FA3486A9…`, `FileCat.Platform.Windows.dll` `D0D30A8E…`. x64: `B914ADFB…` and `BF9BAABA…`. The final commit also changes the wording of the remote-drive refusal.

The release-build runs use [`win-nvda-storage-review.ps1`](../../../tests/FileCat.NativeAcceptance/win-nvda-storage-review.ps1) with its new `-ReleaseBuild` switch, which launches the shipped `FileCat.exe` with its default composition instead of the qualification host. The completed-backup mode has the project's wizard driver enter a random throwaway password for the exported fixture PFX, verifies the PFX against the reviewed key and deletes it.

| Run | Windows on Arm | Windows x64 |
|---|---|---|
| Release build with NVDA: EFS and compression reviews, acknowledgments and focus return | [Passed](receipts/2026-11-22-efs-release/arm64-release-default.json) | [Passed](receipts/2026-11-22-efs-release/x64-release-default.json) |
| Release build: setup, key backup through Windows' wizard, Encrypt, then Decrypt through the FileCat command, with both streams verified | [Passed](receipts/2026-11-22-efs-release/arm64-release-completed-key.json) | [Passed](receipts/2026-11-22-efs-release/x64-release-completed-key.json) |
| Release build: backup of a selected encrypted file's key, after a canceled wizard left no file | [Passed](receipts/2026-11-22-efs-release/arm64-release-selected-file.json) | [Passed](receipts/2026-11-22-efs-release/x64-release-selected-file.json) |
| Release build: a canceled backup wizard returns focus and changes nothing | [Passed](receipts/2026-11-22-efs-release/arm64-release-canceled-key.json) | [Passed](receipts/2026-11-22-efs-release/x64-release-canceled-key.json) |
| Qualification host after the setup fix: canceled and completed backup | [Passed](receipts/2026-11-22-efs-release/arm64-qualification-canceled-key.json), [passed](receipts/2026-11-22-efs-release/arm64-qualification-completed-key.json) | Earlier x64 receipts |
| Qualification host: selected-file backup | [Passed](receipts/2026-11-22-efs-release/arm64-qualification-selected-file.json) | Earlier x64 receipts |
| TPM-backed keys: silent comparison and parented comparison with Microsoft Platform Crypto Provider | 2 of 2 passed: `MatchedAvailable`, export policy None; the parented case matched and rejected a different key ([TRX](receipts/2026-11-22-efs-release/arm64-tpm-keys.trx)) | Earlier x64 receipts |
| EFS setup tests | The no-filter test, 1 of 1 | 13 total, 10 passed, 3 skipped: the first-use and key-rotation opt-ins, and the no-filter test ([TRX](receipts/2026-11-22-efs-release/x64-platform-efs-setup.trx)) |
| Release composition tests | On Windows 11 Home, 3 of 3 passed (below) | 2 passed; the Home-only refusal test skipped on Pro ([TRX](receipts/2026-11-22-efs-release/x64-platform-composition.trx)) |
| Native EFS classes with only the EFS, TPM and parented-window opt-ins (`WindowsEfsJobTests`, `WindowsEfsKeyTests`, `WindowsEfsPathTests`, `WindowsEfsSetupTests`, `WindowsEfsCopyTests`, `WindowsEfsReplacementTests`); account, USB, smart-card, rotation and wizard fixtures skip | 111 total, 53 passed, 1 failed, 57 skipped ([TRX](receipts/2026-11-22-efs-release/arm64-efs-regression.trx), [log](receipts/2026-11-22-efs-release/arm64-efs-regression.log)). The failure was the guest, not FileCat: the recursive job test creates a directory symbolic link, and the limited token had no symlink privilege with Developer Mode off. With Developer Mode on, it passed, including setup below an encrypted root on the drive without a Cloud Files filter ([TRX](receipts/2026-11-22-efs-release/arm64-efs-recursive-rerun.trx)) | 111 total, 70 passed, 0 failed, 41 skipped ([TRX](receipts/2026-11-22-efs-release/x64-efs-regression.trx), [log](receipts/2026-11-22-efs-release/x64-efs-regression.log)) |
| App default composition and private storage history | On Windows 11 Home, the composition test passed (below); history ran on Windows on Arm in the [compression receipt](2026-11-22-compression-release-arm64.md) | 1 of 1 and 3 of 3 passed ([composition](receipts/2026-11-22-efs-release/x64-app-composition.trx), [history](receipts/2026-11-22-efs-release/x64-app-history.trx)) |

NVDA read the setup dialog with the invoking account and "Windows may create or enroll an encryption key. That key remains if you cancel later. Setup uses a private empty file and leaves your selected items alone." It read both encryption acknowledgments and the decryption warning: "I understand the selected files will lose EFS protection. Other protections, such as permissions, still apply."

## Windows Home

A second disposable VM on the test Mac ran Windows 11 Home on Arm (EditionID `Core`, build 26300.9457), installed from the same Microsoft image with Microsoft's generic Home setup key. The test lane ran in its signed-in desktop at medium integrity ([host record](receipts/2026-11-22-efs-release/home-host.txt)).

- **Windows itself:** `cipher /e` on a probe file reported "The request is not supported.", and so did `File.Encrypt` ([record](receipts/2026-11-22-efs-release/home-windows-efs.txt)).
- **Release composition:** 3 of 3 passed, none skipped ([TRX](receipts/2026-11-22-efs-release/home-platform-composition.trx)). Home is a release host for compression. The App composition keeps compression available and refuses both EFS actions with `efs-build` and the edition reason. `Without_a_qualified_EFS_edition_EFS_review_is_refused_before_any_object_is_opened` ran: Encrypt and Decrypt reviews were refused before any job, journal or setup file existed, and the file stayed clear.
- **App:** `The_default_composition_offers_compression_and_EFS_only_where_qualified` passed: neither EFS command was advertised, and the reason was the edition reason ([TRX](receipts/2026-11-22-efs-release/home-app-composition.trx)).

## Shared rows: the EFS side

The C release mapped each shared row's acceptance items to tests and native runs. The EFS side uses the same scheduler, journals, traversal, history and refresh code, so those items carry over. Each row below adds the EFS-specific evidence.

| Row | EFS-specific evidence |
|---|---|
| T-UI | Separate action and scope acknowledgments, the decryption warning, setup and backup decisions: [EFS review controls](2026-11-18-efs-review-controls.md); NVDA on x64 ([storage review](2026-11-18-ntfs-storage-review-nvda.md), [key export](2026-11-19-efs-key-export-nvda.md)) and on Arm64 (this receipt and the [screen-reader receipt](2026-11-22-windows-arm64-screen-readers.md)); the release-build runs above |
| T-CAP | Native status and held-stream admission ([native binding](2026-11-17-efs-native-binding.md)); stream overflow refusal ([late streams](2026-11-18-efs-late-streams.md)); the edition gate (composition tests above, Windows Home) |
| T-TRAV | Held recursive jobs and reversed overlapping roots ([recursive jobs](2026-11-18-efs-recursive-jobs.md)); native Arm64 selected and recursive jobs ([volume matrix](2026-11-18-ntfs-compression-volume-matrix.md#durable-storage-job-qualification)) |
| T-OUT | Held native return and post-state ([native binding](2026-11-17-efs-native-binding.md)); selected-job no-op and refusal outcomes ([selected jobs](2026-11-18-efs-selected-jobs.md)); no-call removal on an EFS file on both architectures |
| T-LIFE | Stop during the admitted call ([native binding](2026-11-17-efs-native-binding.md)); recovery cancellation and key drift ([recursive jobs](2026-11-18-efs-recursive-jobs.md)) |
| T-REC | Operations recovery presentation and retained acknowledgment ([recovery journal](2026-11-18-efs-recovery-journal.md)); the history startup test for Encrypt and Decrypt on both architectures |
| T-LIMIT | Refusal before mutation with 1,025 native streams ([late streams](2026-11-18-efs-late-streams.md)) |
| T-REFRESH | Captured-origin cleanup after Stop and uncertainty, and the native decrypt origin refresh ([storage command](2026-11-17-storage-command.md)) |
| T-REG | Operations recovery presentation ([recovery journal](2026-11-18-efs-recovery-journal.md)); both EFS commands sit in File → Inspect beside NTFS compression and appear in the palette only where advertised |
| N-ID | N-E2's 21-case pathname and stream suite on both architectures ([native binding](2026-11-17-efs-native-binding.md#new-hard-link-and-alternate-local-path-spellings)) |

## Limits

- The hardware key ran on x64 only ([hardware-key receipt](2026-11-22-efs-hardware-key.md)). The only Arm64 machine available is the test Mac, and the owner's YubiKey cannot be attached to a Windows guest there, so smart-card keys on Windows on Arm are not qualified. On Arm64 the software, TPM and managed-recovery keys ran. A card key that FileCat cannot compare without a prompt is unresolved, and an unresolved key blocks new encryption rather than being assumed.
- One VM per architecture, with NVDA as the screen reader.
- EFS uses Windows' pathname calls under the approved [concurrency boundary](../../design/FILECAT_EFS_PATH_API_CONCURRENCY_AMENDMENT_PROPOSAL.md): FileCat holds the items and blocks competing renames and deletions, but another program can still convert a held item into a link during the call. The review says so and requires an acknowledgment.
- Windows chooses the key when each file is encrypted. If that key changes during a job, the job stops for review; the current file may already be encrypted.
