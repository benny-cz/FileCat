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

## Limits

- This qualifies Windows 11 x64 on an Insider build only. ARM64 needs its own results.
- Physical device loss stays unqualified. Hot-plug transports are now refused rather than admitted.
- The no-filter rule depends on the registered sync-root list and on reparse tags of sync-root directories. Providers that register no sync root and leave no Cloud Files reparse point are not detected; the plan already states that cloud detection is not exhaustive.
- The renames in the ancestor-swap cases were refused by Windows, so these runs did not reach the replaced-ancestor branch. The fixtures retain that branch's assertions.
