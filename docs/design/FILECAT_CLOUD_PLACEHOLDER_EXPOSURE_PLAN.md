# Cloud Files placeholder exposure: implementation plan

**Status, 21 November 2026:** implemented and qualified. X-LIST, X-DETAILS, X-POLICY, X-THREAD and X-NOEXPORT passed on the Windows 11 x64 VM, and X-ARM64 passed on the hosted `windows-11-arm` runner ([receipt](../validation/filesystem-details/2026-11-21-windows-cloud-exposure.md)). This plan closes the Cloud Files items left open in the [File-system details plan](FILECAT_FILESYSTEM_DETAILS_AND_FEATURES_PLAN.md): F-TYPE and N-PROVIDER.

## 1. Problem

FileCat is never the provider of a Cloud Files sync root, and it does not choose a placeholder compatibility mode. Windows therefore runs it in mode 1 (`PHCM_DISGUISE_PLACEHOLDER`). In that mode, another provider's placeholders (OneDrive, Dropbox, iCloud) look like ordinary files and folders to every FileCat call:

- no `FILE_ATTRIBUTE_REPARSE_POINT`;
- reparse tag 0;
- the cloud attributes (recall-on-data-access, recall-on-open, pinned and unpinned, offline) stay visible.

The [native listing receipt](../validation/filesystem-details/2026-11-21-windows-native-listing-x64.md#finding-cloud-files-placeholders-are-disguised-for-filecat) records this with a cross-process fixture. Consequences today:

- **Listings:** listings never show Cloud Files subtypes, although the native tag-bearing route (F4) exists to show them.
- **Details:** File-system details shows only the cloud state it derives from the attributes (`WindowsFileRecords.Details.cs`). Its Cloud Files section (placeholder state, pin state, on-disk size, sync-root provider) never appears: `WindowsFileRecords.Cloud` requests it only when the handle reports the reparse attribute and a cloud tag.
- **Evidence:** every earlier Cloud Files fixture ran in the provider's own process, which sees the exposed view, so it qualified a view production never has.
- **Unchanged:** operations behave as if placeholders were ordinary files, which is what they should keep doing. NTFS compression already refuses placeholders through the sync-root query and the cloud attributes.

## 2. Decisions

| Decision | Contract |
|---|---|
| Expose only for metadata reads, per thread | Use `RtlSetThreadPlaceholderCompatibilityMode(PHCM_EXPOSE_PLACEHOLDERS)` only around the synchronous native calls that produce presentation metadata. Never set the process mode. Operations, searches, transfers, archive packing and the shell keep the disguised view they have today. |
| Presentation gains the tag; policy flags do not change | When an exposed listing entry carries a cloud tag, keep `NativeReparseTag` and the native `Attributes`, reparse bit included, for presentation. Compute `Flags` exactly as the disguised view would: from the attributes without the bits Windows hides from a process that is not the provider (reparse point, sparse and offline; a placeholder only in the cloud lists as `0x00401620` exposed and `0x00400020` disguised on Windows 11 x64), so never `EntryFlags.Link`. `LocalFileSystemProvider.MapFlags` marks every reparse point as a link, and the listing-driven consumers (compare, sync, sizing, export, transfer admission, Find descent) follow that flag. Every reader of a listing entry's reparse attribute is presentation: `EntryPresentation` (kind and link labels; a cloud tag is never a link), `CloudFiles.StateOf`, the attribute letters (`P`, not `L`) and the picture labels. A search refresh keeps a tag only while fresh attributes agree with the listed ones; a disguised refresh disagrees, so the tag is dropped rather than kept stale. |
| Restore on the same thread, every time | A scope helper saves the thread's previous mode and restores it in `finally` on the same thread. It never spans an `await`. Pool threads must leave with the mode they arrived with. |
| No new acquisition | Exposure changes what existing metadata calls report, not which calls are made. No new per-entry opens, no data reads and no hydration. The details reader keeps its metadata-only handle and its existing Cloud Files queries. |
| Native route only | The managed listing route (`LocalFileSystemProvider`, .NET enumeration) stays disguised. Windows on Arm keeps the managed route until N-ENUM's ARM64 SMB case closes, so it shows no Cloud Files subtypes; that is truthful, as today. |

## 3. Source seams

- **Native listing:** `WindowsFileSystemProvider.Enumeration.cs` (`EnumerateWithFindApi`). It runs `FindFirstFileExW` → `FindNextFileW` → `FindClose` synchronously on the calling thread and builds `EntryData` with `NativeReparseTag` from the find data and `Flags = MapFlags(attributes)`. The whole loop runs inside one exposure scope; the managed fallback runs outside it. For a cloud tag it keeps the tag and the attributes and strips the hidden bits before `MapFlags`.
- **Details reader:** `WindowsFileRecords` (summary, full and the file record with its `Cloud` section). Each read runs synchronously inside one exposure scope, so the handle queries (`FileAttributeTagInfo`, the reparse payload, `CfGetPlaceholderInfo`, the sync-root query) see the placeholder. The report's operational facts (kind, link classification) keep treating a cloud placeholder as an ordinary file or folder: `FileDetailsTargets.IsOrdinaryWindowsEntry` accepts a cloud tag for target navigation and the Hard links column.
- **Columns:** the Reparse type column reads the same snapshot, so it gains the cloud subtype automatically.
- **Unchanged consumers:** the operation-time attribute reads in `Executors.cs` (for example line 547, which treats the reparse attribute as a link while copying), `SearchEngine.cs`, `ClipboardVirtualStaging.cs`, the storage traversal, `ArchiveAppContainer.cs` and `SftpJobs.cs`. They keep running in the disguised process mode. This is why process-wide exposure is rejected: it would turn OneDrive folders into links while copying.

## 4. Work packages

1. **Scope helper** (`Platform.Windows`): `PlaceholderView.Expose<T>(Func<T>)` over `RtlQueryThreadPlaceholderCompatibilityMode` and `RtlSetThreadPlaceholderCompatibilityMode`. It is a no-op on Windows versions without the export, where the view stays disguised and truthful.
2. **Native listing:** wrap the find loop. Map cloud-tagged entries to the disguised `Attributes` and `Flags` and keep the tag. Unknown and non-cloud tags behave exactly as today.
3. **Details:** wrap the reader's handle queries. Show the Cloud Files section and subtype for other providers' placeholders. Keep the link classification and target navigation unchanged for cloud tags.
4. **Presentation:** confirm that list, picture, speech and letters use the existing cloud labels (F-TYPE). Add none.

## 5. Gates

All native gates use the cross-process harness from `WindowsFileRecordsTests.CloudEnumeration.cs` and `CloudStorageProcess.cs`. The test process serves a sync root; a child copy of the test assembly does the FileCat work in the default mode 1, as production does.

| ID | Fixture | Expected |
|---|---|---|
| X-LIST | A child lists the sync root through the native route | Every placeholder carries its cloud tag. `Attributes` are the disguised listing's plus the reparse bit and any other bit the disguise hides; `Flags` equal the disguised listing's, with no Link flag. Listing causes no data request and no data-access open. |
| X-DETAILS | A child reads the summary and full report of a placeholder and its folder | Cloud Files section present: placeholder state, pin state, on-disk size and sync-root provider. Kind and link classification as for an ordinary file or folder. No fetch callback. |
| X-POLICY | The F-OPS corpus with exposed cloud entries; plus native copy, move, delete, Find and NTFS compression from a child, with exposure active only in the listing | Every operational decision equals the disguised run's. Compression still refuses as `cloud-sync-root` and `cloud-entry`. The F-OPS corpus already holds the exposed shape (cloud tag, reparse attribute, no Link flag) beside the disguised one, and each decision follows the Link flag alone. |
| X-THREAD | A pool thread runs an exposed listing, then a disguised query | The thread's mode after the scope equals its mode before, including on exceptions and cancellation. The process mode never changes. |
| X-NOEXPORT | The scope helper with the export missing (fake) | Disguised view, no exception, no presentation claim. |
| X-ARM64 | CI on `windows-11-arm` | The managed route shows no subtype. The details reader's exposed path passes X-DETAILS through the ARM64 qualification workflow. |

Close F-TYPE's Cloud Files item and the Windows part of N-PROVIDER when X-LIST, X-DETAILS, X-POLICY and X-THREAD pass on the Windows 11 x64 VM and X-ARM64 passes on the hosted runner.

## 6. Risks

- **An OS change to exposure semantics:** the gates run on each Windows build that is qualified.
- **A provider that reacts to exposed metadata queries:** the no-fetch assertion and the fetch counter detect a data request. Metadata-only queries stay as they are today.
- **Mode leaks on pool threads:** X-THREAD, and the helper's same-thread restore.
- **Presentation-policy drift:** a future change that derives policy from `NativeReparseTag` for cloud entries would reopen this plan's X-POLICY gate.
