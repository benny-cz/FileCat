# Windows native listing on x64 — 21 November 2026

Source commit `164b1f18d` makes `WindowsPlatform` list local folders through the native tag-bearing enumeration (F4). It does so only when both the process and Windows are x64. Windows on Arm keeps the managed route, because N-ENUM still has an open ARM64 SMB case. The native route carries each entry's reparse tag from the directory query itself. Unsupported API errors still fall back to the managed route before anything is published.

## Parity on the Windows 11 x64 VM

**Environment:** Windows 11 Pro Insider build 26340.9843 x64 VM, medium integrity, .NET 10.0.15 runtime.

**Binaries:** test DLL `79A1BF2F…` and platform DLL `B6254882…`, built from commit `9c07d5b16`, which includes the switch. Each case lists the same folder through `LocalFileSystemProvider` and through `WindowsFileSystemProvider { UseNativeEnumeration = true }`.

| Case | Result |
|---|---|
| Owned FAT32 (4 KiB), exFAT (32 KiB), ReFS Dev Drive (4 KiB) and NTFS volume without a Cloud Files filter instance (4 KiB). Each held 600 files, a hidden read-only file and a nested folder. | Same names, kinds, attributes, flags, sizes and modification times. |
| A folder whose listing is denied to the current user | Both routes throw `UnauthorizedAccessException` before publishing any entry. |
| The `Documents and Settings` compatibility junction at medium integrity | Both refuse with `LinkListingRefusedException` naming the same link and target. |
| The system drive root | Same names, kinds and attributes; reparse tags are present on the native route. |
| `a`, `A`, `ß` and `SS` in a case-sensitive folder, and a 255-unit Unicode name | Listed exactly, with the same metadata. |
| Container detection, and empty and missing folders | Same results. |

**Result:** 71 total, 55 passed, 15 skipped. The skips are prepared-fixture variables for other classes. The one failure was the Cloud Files open instrument, described below. See the [TRX](receipts/2026-11-21-windows-native-listing-x64/parity-and-volumes.trx) (`9d1933b5…`) and [run log](receipts/2026-11-21-windows-native-listing-x64/parity-and-volumes-run.log).

The same parity classes passed on the Windows 11 x64 workstation for the cases that need no prepared volume ([details gap receipt](2026-11-21-details-gap-fixtures.md#native-fixtures)).

## No per-entry opens

The earlier fixture registered a temporary Cloud Files provider and counted the open notifications for its placeholders while the test process listed them. It failed on the CI runners (build 26100, x64 and ARM64) and on the VM: the provider never saw even the control open. Two Windows behaviours explain this:

1. The provider was not told about its own process's opens.
2. A process that is not the provider sees another provider's placeholders disguised as ordinary files.

Commit `13434210a` moves the listing and each control open into child copies of the test assembly. On the VM:

- The child ran in process placeholder compatibility mode 1 (disguise). Both routes reported tag `0x00000000` and attributes `0x00400020` (archive and recall-on-data-access) for all 40 placeholders.
- Listing through both routes caused no open notification and no data request.
- Neither metadata-only control open (no-follow or ordinary) was reported to the provider. A data-access open without a read was reported.

The runtime proof therefore covers data-access opens only. Metadata-only opens cannot be observed through Cloud Files on this build. The test skips with that reason instead of passing, and the "no per-entry opens" claim for metadata opens rests on the source: the listing uses one directory query handle and opens no child. See the [TRX](receipts/2026-11-21-windows-native-listing-x64/cloud-cross-process.trx) (`7725703e…`) and [run log](receipts/2026-11-21-windows-native-listing-x64/cloud-cross-process-run.log).

## Finding: Cloud Files placeholders are disguised for FileCat

FileCat is never the provider of a sync root, and neither FileCat nor its manifest selects a placeholder compatibility mode. On the tested build it therefore runs in mode 1, like the child above. In that mode, OneDrive, Dropbox and other providers' placeholders appear to FileCat as ordinary files:

- no reparse attribute;
- tag 0;
- the cloud attributes, such as recall-on-data-access, stay visible.

The earlier Cloud Files fixtures created their placeholders in the test process itself, so they saw the exposed view. The cloud placeholder labels in listings and the cloud sections of File-system details have no evidence in FileCat's actual mode. Showing them needs FileCat to expose placeholders to its metadata reads, and the operations that would then see the exposed tags must be qualified.

Compression safety does not depend on this. From a child process in mode 1, the review refused the sync root as `cloud-sync-root` and the pinned placeholder as `cloud-entry` for both actions. A recursive Compress job over the parent changed the parent and its ordinary sibling, skipped the root, and caused no data request (same TRX).

## Limits

- x64 only. ARM64 stays on the managed route until N-ENUM's ARM64 SMB case closes.
- The runtime no-open proof covers data-access opens only, as described above.
- Cloud Files subtypes are not shown for other providers' placeholders while FileCat runs in the default placeholder compatibility mode.
