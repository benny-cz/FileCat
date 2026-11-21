# Storage volume admission, identity and process-kill qualification — 21 November 2026

This checkpoint covers NTFS compression (C) admission changes and the native fixtures added for the open N-ID.C, N-C2 and T-REC.C items. The EFS release gates are unchanged.

## Admission changes

Source commit `437861ef2` changes three admission rules.

**Volumes without a Cloud Files filter.** Windows attaches the Cloud Files minifilter (`CldFlt`) only to volumes that need it. On the Windows 11 workstation used for development, `fltmc instances -f CldFlt` listed only `C:`; four other fixed NTFS drives had no instance. The test VM showed the same pattern: `C:` and a shadow copy only. On such a volume `CfGetSyncRootInfoByHandle` returns `0x80070001` (`ERROR_INVALID_FUNCTION`). Under the previous rule this refused every storage change on every fixed NTFS drive except the system drive (`cloud-query-80070001`).

The held-object query still decides whenever the filter answers. The new rule applies only when no filter answered, and only to an object reached by the held no-follow walk or held traversal:

- That walk has already refused every component that was reparse-backed or carried a cloud attribute.
- Each registered sync root's own directory is a Cloud Files reparse point. On the workstation, the Dropbox, iCloud Drive and OneDrive roots carry tags `0x9000F01A`, `0x9000301A` and `0x9000701A`.
- The object is admitted only when no registered sync root of any account (`HKLM\…\SyncRootManager\*\UserSyncRoots`) resolves to the same volume GUID.

The following cases still refuse:

- If the registrations cannot be read: `cloud-registrations-unknown`.
- If a registered root is on the same volume: `cloud-sync-root-volume`.
- Callers without a held walk keep the strict rule: the details range collectors, private evidence directories and the EFS setup namespace.

Registration paths that resolve to no mounted volume are skipped. Such a root cannot hold an object reached through a mounted path, and its own reparse tag stops the walk if the volume is mounted elsewhere. The resolved volume set is reused for at most two seconds.

**Hot-plug transports.** The removable-media characteristic did not cover disks that report fixed media. The workstation's USB-attached SSD reports bus type USB and fixed media. The adapter now reads `STORAGE_DEVICE_DESCRIPTOR` from the volume device without data access:

- USB, IEEE 1394, SD and MMC transports, and any removable medium, are refused as `hot-plug-unqualified`.
- An unknown or future bus type, or a failed query, is refused as `device-attachment-unknown`.
- File-backed virtual disks, Storage Spaces, SCSI, SAS, SATA, ATA, RAID, NVMe and iSCSI remain fixed.

This keeps physical hot-plug device loss outside the enabled scope instead of requiring a physical unplug fixture.

**Emulated x64.** `IsQualifiedHost` now also requires the OS architecture to be x64, so an x64 process emulated on Arm64 is not the qualified x64 target.

## Native evidence

**Environment:** Windows 11 Pro Insider build 26340.9843 (26H2), x64 VM; Windows Update and Defender real-time protection were disabled for the run.

**Test process:** medium integrity, lowered by `eng/validation/Enter-MediumIntegrityTestProcess.ps1`, using the .NET 10.0.15 runtime.

**Fixtures:** each runner creates its own fixtures and removes them afterwards. The run logs list volume IDs, allocation units, bus types (`File Backed Virtual`) and the `CldFlt` instances.

The TRX files and run logs are stored under [receipts/2026-11-21-storage-admission-x64](receipts/2026-11-21-storage-admission-x64/).

| Run | Binaries | Result |
|---|---|---|
| Regression and no-filter volume | test DLL `A31C46DF…`, platform DLL `F3424B26…` (commit `437861ef2`) | 141 total, 77 passed, 0 failed, 64 opt-in skips. TRX SHA-256 `ea920ace…b50e644e`. |
| Identity and volumes | test DLL `AAF5758D…`, platform DLL `F3424B26…` | 50 total, 44 passed, 0 failed, 6 opt-in skips. TRX SHA-256 `dd9d2387…b7d18e6906`. |
| Process kill | test DLL `19B3924C…`, platform DLL `B21B4DDA…` (commit `01cc8df1d`) | 4 total, 3 passed, 0 failed; the skip is the child half. TRX SHA-256 `a4465bb2…a8ab4`. |

The regression run covered the existing compression classes:

- `WindowsStorageInspectionTests`, `WindowsStorageExecutorTests`, `WindowsStorageTraversalTests`, `WindowsStorageSelectionTests`
- `WindowsCompressionNativeTests`, `WindowsStorageAbiTests`, `WindowsStorageReviewEvidenceTests`
- `StorageDirectoryCursorTests`, `WindowsSparseTransformTests`

Its skips are the EFS, device-loss, full-volume, scale and other separately prepared fixtures.

### Admission fixtures

- **No-filter volume:** on a fresh 4 KiB VHDX with no `CldFlt` instance, the raw query returned `0x80070001`. The volume was absent from the registered-root volumes and classified Fixed. A reviewed Compress and Remove compression each changed one file and preserved its bytes.
- **Deterministic cases:** these cover the full membership matrix, the bus-type classification and the distinct reasons. Explicit filter answers never read registrations; a missing held walk keeps refusing; cancellation precedes the registry read.
- **Hot-plug disk on the workstation:** a separate run ran this fixture against a folder on the workstation's USB-attached SSD.
  - The process was the elevated development shell, and the run was review-only.
  - The volume device reported bus type USB with fixed media. Both actions were refused as `hot-plug-unqualified` at binding.
  - No job or journal directory was created. No TRX was retained for this host run.

### N-ID.C and N-C2 fixtures

| Case | Observed |
|---|---|
| Drive root, `\\?\C:\`, volume GUID root | Refused at capture (`source-path`) before any native open; no job, no evidence file. |
| Drive-letter alias of a folder (`DefineDosDevice`) | `volume-binding` for both actions; file unchanged. |
| Junction as selected item, as ancestor, as discovered child | `reparse-entry`, `ancestor-excluded`, and skipped in a recursive job (2 changed, 1 skipped); the target and its file were not compressed. |
| Volume mount point (VHDX mounted in a folder) | Selected mount folder `reparse-entry`, path through it `ancestor-excluded`. The same file through `\\?\Volume{GUID}\` was admitted with the mounted volume's binding. A reviewed Compress and removal each changed it; the outer mount folder stayed uncompressed. |
| Parent renamed after review (pin held) | Rename refused by Windows (blocked by the held objects); the job changed the reviewed object. |
| Ancestor renamed during a recursive job | Rename refused (`0x80070005`); the job completed with four changes on the held objects. |
| Junction set in place on the reviewed empty folder after review | Job CompletedWithIssues, `reparse-entry`, zero changes; the junction target and its file untouched. |
| Paths over 400 characters | Recursive job changed five objects, payload intact. |
| Case-sensitive folder with `a` and `A` | Only the selected `a` was compressed. |
| 1,025 named streams | Main data compressed without a stream inventory; sampled streams intact. |
| WOF-backed file (`compact /exe:xpress4k`) | Refused for both actions; external backing and payload unchanged. |
| Sparse read-only file | Compress refused `read-only-file`; Remove compression completed as already satisfied with no change; read-only, sparse and both written ranges preserved. |
| Folder without listing access inside a recursive scope | CompletedWithIssues: parent and sibling changed, the folder failed (`observation-failed`), discovery `UnknownRemainder`, its child untouched. A fully denied selected folder was refused at review as PermissionOrPolicy. |
| Allocation units 512, 1,024 and 2,048 bytes (VHD, 512-byte sectors) | Compress and removal round trips; the named stream kept its bytes. |

### Process kill (T-REC.C)

A child copy of the test assembly ran a reviewed two-file Compress job and terminated itself with `TerminateProcess` (exit code −1) at one of three points:

- before the first SET;
- after the first SET returned;
- before the second SET.

The first and last points are after durable intent. A qualification-only hook, null in production, marks the point between durable intent and the SET.

A fresh manager over the same private evidence then showed:

- one Compress history row, with review pending, not ended and `IsInterrupted` true;
- the last call (1 or 2) recorded without a result and the effect uncertain;
- no automatic job.

Each file was compressed exactly as the native calls had left it, and its bytes were unchanged. Reading the review again changed nothing.

### Protected tree, cloud breadth and reason matrix (T-CAP.C, N-ID.C)

A fourth run used test DLL `22C364A0…` and platform DLL `3AC69810…` (source of commit `8304044a0`). It also set `FILECAT_TEST_CLOUD_FILES=1`. Results: [admission-matrix.trx](receipts/2026-11-21-storage-admission-x64/admission-matrix.trx), 21 total, 20 passed, 1 skip (hot-plug); [protected-and-cloud.trx](receipts/2026-11-21-storage-admission-x64/protected-and-cloud.trx), 3 of 3 passed.

- **Protected tree:** a recursive Compress over a folder holding a fake active-Windows anchor (with `System32\inside.dll`) and folders named `Windows`, `Program Files` and `ProgramData` completed with issues. Seven objects changed. One was skipped (`windows-tree`, FileCatBoundary, not an error) and its subtree was not entered or changed. The name lookalikes were ordinary. With the anchor placed on the no-filter VHDX, a file inside it was refused there while the same `C:` file was admitted.
- **Cloud Files:** a temporary sync root of the test's own provider (which serves no data) held a pinned 5,000-byte cloud-only placeholder, beside an ordinary sibling.
  - The selected root was refused (`reparse-entry`) and the placeholder was refused as `ancestor-excluded`, for both actions.
  - A recursive job over the parent changed the parent and the sibling and skipped the root.
  - The provider received no data request at any step, and the placeholder stayed pinned with zero bytes on disk. The existing placeholder-details fixture passed in the same run.
- **Reason matrix:** a deterministic test drives every physical refusal branch and checks its kind and precedence. The branches are entry changed or deleting, volume root, Windows tree, System, reparse, the four cloud attributes (the queried EA bit is not one), link counts 0 and 2, sparse directory, read-only volume, unknown or oversized allocation unit (not applied to EFS), and missing compression or EFS volume support. The test also covers the unknown-format and compression-with-EFS state refusals.

### Compression enable boundary (N-C2)

The opt-in fixture `Reviewed_compression_at_the_enable_limit_compresses_a_dense_NTFS_stream` ran with test DLL `140D2101…` and platform DLL `36C5ED22…`; the source and fixture are in commit `1364188bf`.

**Fixture volume:** an owned 76 GiB expandable VHDX holding a fixed 4 KiB NTFS volume labelled `FCATDENSE`. No Cloud Files filter instance was attached before or after the run.

**File:** an uncompressed dense main stream of exactly 30,000,000,000 bytes, the largest admitted length, with a named stream.

**Result:** the reviewed Compress job ran from 11:09:37 to 11:28:04 UTC and completed. The durable call was observed with main data not compressed before and LZNT1 after.

**Preserved:**

- the held identity `907A2D527A2D35FE:2800…0100…`
- the full-stream SHA-256 `E6A54535…C54EAF`
- the named stream's bytes and its original compression format

Allocation fell to 11,249,999,872 bytes. The small preflight case passed in the same run. The first attempt failed before any change because the fixture kept its evidence directory on the filter-less volume; commit `1364188bf` fixed that.

**Evidence** (2 of 2 passed):

- [TRX](receipts/2026-11-21-storage-admission-x64/dense-enable-boundary.trx), SHA-256 `54aa9e23…7027ed`
- [run log](receipts/2026-11-21-storage-admission-x64/dense-enable-boundary-run.log)
- [seeding progress](receipts/2026-11-21-storage-admission-x64/dense-enable-boundary-progress.log)

## Limits

- This qualifies Windows 11 x64 on an Insider build only. ARM64 needs its own results.
- Physical device loss stays unqualified. Hot-plug transports are now refused rather than admitted.
- The no-filter rule depends on the registered sync-root list and on reparse tags of sync-root directories. Providers that register no sync root and leave no Cloud Files reparse point are not detected; the plan already states that cloud detection is not exhaustive.
- The renames in the ancestor-swap cases were refused by Windows, so these runs did not reach the replaced-ancestor branch. The fixtures retain that branch's assertions.
