# Cloud Files placeholder exposure on Windows 11 x64 — 21 November 2026

This receipt qualifies the [placeholder exposure plan](../../design/FILECAT_CLOUD_PLACEHOLDER_EXPOSURE_PLAN.md) on the Windows 11 x64 VM. FileCat runs in Windows's default mode, which disguises another provider's Cloud Files placeholders as ordinary files ([native listing finding](2026-11-21-windows-native-listing-x64.md#finding-cloud-files-placeholders-are-disguised-for-filecat)). The change exposes them to presentation only.

## What changed

- **Scope helper:** `PlaceholderView` sets `PHCM_EXPOSE_PLACEHOLDERS` on the calling thread and restores the thread's previous mode on the same thread. Its scope is a stack-only `ref struct`, so it cannot cross an `await`. The process mode never changes. Without the ntdll export, reads keep the view the thread already has.
- **Native listing:** only `FindFirstFileExW` and `FindNextFileW` run exposed. The sink, container detection and the refused-link check run in the thread's previous view. A cloud-tagged entry keeps its tag and native attributes for presentation. Its `Flags` come from the attributes without the bits the disguise hides (reparse point, sparse and offline), so they equal the disguised listing's flags and never include `Link`.
- **Details:** the summary, the full report and the file record each run inside one exposure scope. The file record's Cloud Files section now appears for other providers' placeholders.
- **Ordinary-entry rule:** `FileDetailsTargets.IsOrdinaryWindowsEntry` accepts a cloud tag. Target navigation and the Hard links column therefore keep treating a placeholder as the ordinary file or folder that operations see.

## What Windows hides

A child process in the default mode 1 listed the same sync root twice: through the managed route (disguised) and through the native route (exposed).

| Placeholder | Exposed tag | Exposed attributes | Disguised attributes | Flags (both) |
|---|---|---|---|---|
| File only in the cloud | `0x9000401A` | `0x00401620` | `0x00400020` | Offline |
| In-sync local files | `0x9000601A` | `0x00000420` | `0x00000020` | None |
| In-sync local folder | `0x9000601A` | `0x00000410` | `0x00000010` | None |

So the disguise removes the reparse point, sparse and offline attributes (`0x1600`). Recall-on-data-access stays visible, so both views flag the cloud-only file as Offline.

## Gates on the Windows 11 x64 VM

**Environment:** Windows 11 Pro Insider build 26340.9843 x64 VM, medium integrity, `FILECAT_TEST_CLOUD_FILES=1`.

**Binaries:** test DLL `FF252CF3…` and platform DLL `D6B103DC…`, built from the commit that adds this receipt.

| Gate | Fixture | Result |
|---|---|---|
| X-LIST | `Another_process_describes_placeholders_as_cloud_files_and_operates_on_them_as_ordinary_ones`, and the listing child of `Listing_a_sync_root_opens_none_of_its_placeholders_on_either_route` (40 placeholders) | Passed. Every placeholder carries its cloud tag. With the hidden bits removed, its attributes equal the disguised listing's, and its flags equal the disguised flags exactly. `EntryPresentation` labels each one "Cloud-managed item" and never a link; the disguised entry has no label. The listing thread leaves with the mode it had, and `File.GetAttributes` on that thread sees the disguised view. No data request. |
| X-DETAILS | The same child reads the summary, the full report and the file record of the cloud-only file, an in-sync file and the in-sync folder | Passed. Each summary reports its cloud tag and a Cloud observation, with the target not inspected and the entry an ordinary navigation target. Each file record has the Cloud Files section: provider `FileCat exposure test 1.0 · idle`, in sync, pin state and sync root. The files also show "On this device": none of 5,000 bytes for the cloud-only file, all of 10 for the in-sync one. The folder shows no such field. No data request. |
| X-POLICY | The child copies, moves, deletes and finds, using `ItemRef.FromEntry` on the exposed listing's own entries | Passed. The copy holds the file's bytes, and the folder's contents were copied (entered, not skipped as a link). A plain folder came out. The moved placeholder stayed a placeholder in its new folder. The deleted one is gone. Find descended the placeholder folder. The cloud-only file stayed dehydrated, and the provider received no data request. Compression refusal from another process stays covered by `Storage_changes_from_another_process_refuse_the_sync_root_and_its_placeholder` (passed in the same run). The deterministic F-OPS corpus already includes the exposed shape (cloud tag, reparse attribute, no Link flag) beside the disguised one, and each decision follows the Link flag alone. |
| X-THREAD | `PlaceholderViewTests` | Passed. The mode is restored after success, an exception, nesting and cancellation; across 64 parallel pool work items; while another thread and the process mode stay unchanged. |
| X-NOEXPORT | `PlaceholderViewTests.Without_the_export_the_read_keeps_the_view_the_thread_has` | Passed. |

**Result:** 65 total, 46 passed, 0 failed, 19 skipped. Eighteen skips are prepared-fixture variables, the three child entry points, the administrator-only test and the EFS opt-in. The last is the refused control described below. See the [TRX](receipts/2026-11-21-windows-cloud-exposure/cloud-exposure.trx) (`b6fc2ce3…`) and [run log](receipts/2026-11-21-windows-cloud-exposure/cloud-exposure-run.log). The Core rules (cloud tags as ordinary navigation targets, Hard links shown for placeholders, reparse type "Cloud-managed item") passed in `FileDetailsTests` and `FileDetailsFieldsTests` on the workstation (64 tests, 1 Unix-only skip).

## The open-notification control is now refused

`Listing_a_sync_root_opens_none_of_its_placeholders_on_either_route` uses a data-access open from another process as its positive control. At 16:42 on the same VM, the provider was told about that open ([native listing receipt](2026-11-21-windows-native-listing-x64.md#no-per-entry-opens)). In this run, Windows refused it with `ERROR_CLOUD_FILE_ACCESS_DENIED` (395). A build of `f01ff2930`, which has no exposure code, gets the same refusal when the test runs alone ([TRX](receipts/2026-11-21-windows-cloud-exposure/control-refusal-before-change.trx), `695417cb…`). So the refusal comes from the VM's state, not from this change. No Cloud Files event or app block on the VM explains it.

The fixture now records a refused control as unobservable and skips with that reason; it does not fail. The listing itself still caused no open notification and no data request. The "no per-entry opens" claim keeps resting on the earlier observation and on the source: the listing uses one directory query handle and opens no child.

## Not covered here

- **Windows on Arm:** production keeps the managed route there, so listings show no Cloud Files subtype. Details are exposed there too; X-ARM64 runs these fixtures through the [ARM64 qualification workflow](../../../.github/workflows/details-arm64-qualification.yml).
- **Real providers:** OneDrive, Dropbox and iCloud placeholders were not read here. The fixture's provider uses the same Cloud Files API.
