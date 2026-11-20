# Filesystem details implementation receipt — 17 November 2026

Implementation is in progress on `main`, starting at `68a435dc2a1d6122f5961f867bcb653b0e6968aa`. The plans and unrelated untracked files were already present. No commit has been made; HEAD alone does not identify the implementation. This receipt extends the existing plans' ledgers and does not close their complete release gates.

## Implemented foundation

- F1: cached-only reparse taxonomy, bounded typed link/WOF/app-alias/WSL parsing, neutral unknown reparse presentation, independent cloud pin/residency/synchronization text and neutral cloud icons. Operational Link/Offline flags and traversal/admission policies remain unchanged.
- F2: `IPlatform.FileRecords` remains the composition seam. Typed summary/full observations are detached from native ownership. Windows uses one no-follow metadata handle; Linux pins with `O_PATH` and uses descriptor statx/readlinkat; macOS uses a separate minimal no-follow getattrlist mask without directory entry count. Returned masks and scalar bounds qualify values. Allocation scopes and native directory link counts remain explicit. Legacy permission, historical inode-flag and extent-placement wording was corrected.
- F3: the command is File-system details, preserving its ID and shortcut. The new route never invokes unrestricted legacy `Read`. Summary/full tiers retain paired observations, reject stale completion, retain failures with original observation times and omit unqualified acquisitions before reading. Copy/Save capture successful text and cancel pending publication on refresh/tier/close. Report toolbar and spoken controls reflow. Stored-target actions validate filesystem namespaces and re-read the source before explicit FileCat navigation. Find supplementary data opens the captured original in a new main-workspace tab.
- F4: tag storage occupies bytes 44–47 of the unchanged 48-byte spill record; mapped scans and Update retain it. The streaming Windows Basic/large-fetch enumerator is implemented behind an internal qualification gate, currently **off**. Existing enumeration therefore continues with honest unknown-tag presentation.
- NTFS WP1 started: portable C/E action and platform contracts, ordinary unnamed-stream path grammar, immutable bounded source capture, disjoint outcome counts and the storage-specific terminal reducer. Native review ownership, scheduler/job/journal integration and mutations remain to be implemented. No compression or EFS mutation action is enabled by this foundation.

## Verification completed

The pinned SDK is .NET 10.0.101. Core, App and Windows-test project builds passed with zero warnings/errors. The VSTest host connection timed out in the sandbox; directly executing the same xUnit v3 assemblies works. A zero-test filter experiment is not acceptance evidence.

Local focused Core execution: **110 passed, zero failed/skipped**. Included ReparsePresentationTests, CloudFilesTests, CloudPlaceholderTests, FileRecordTests, EntryStoreTests, FileDetailsTests, FileDetailsOwnershipTests, StorageContractTests, AttributeLinkTests, FileListExportTests, TreeCompareTests, SyncTests and TruthfulOutcomeTests. The ownership fakes cover a blocked native owner, replacement of 50 pending reads, joint process/device admission and idempotent permit retirement.

Local focused App execution: **68 passed, zero failed/skipped**. Included FileDetailsWindowTests, FileDetailsFindScopeTests, ReportWindowTests, ReportWindowOwnershipTests, FormattersTests, FileListNamePresentationTests, FindWindowTests, AttributeColorTests and OperationScopeTests. The new fixtures cover stale full completion, cached paired switches with no new acquisitions, summary refresh discarding full data, failed-refresh retention, the real narrow toolbar and Find's independent result/tab scope.

Local Windows native metadata/enumeration execution: **2 passed**, with the separately opt-in performance method skipped in the ordinary invocation. Its prepared performance invocations were then run explicitly. Native mutation/key suites were not run on the workstation.

After the user supplied disposable environments, further native work moved to them:

- Ubuntu VM: x64, Ubuntu 24.04/kernel 7.0.0-43; pinned SDK installed from Microsoft's release metadata with SHA-512 verification. Focused native/parser/contract/ownership run: **45 passed, zero failed/skipped**. Includes a real FIFO with no writer, demonstrating that summary acquisition does not wait for a payload open.
- macOS: Apple silicon, Darwin 27.2.0; existing pinned SDK. Core and App builds passed with zero warnings/errors. Core run: **44 passed, one Linux-only FIFO test skipped**. Focused details/report/formatting UI run: **32 passed, zero failed/skipped**. Regular file/directory/symlink identity and packed metadata were exercised. macOS target-payload reading and provider cases remain unqualified.
- Windows VM: x64 Windows build 26340, .NET SDK 10.0.403/runtime 10.0.14 executing the host-built pinned-SDK binaries. Windows Update stopped/disabled and Defender real-time monitoring confirmed off; its protected service could not be stopped. Selected native metadata/enumeration run: **2 passed**, with the opt-in performance method skipped. This qualifies those basic NTFS fixture cases on the guest, not compression/EFS mutation.

## Windows enumeration measurements

Before the VM preference was supplied, disposable zero-byte directories were prepared on the workstation's NTFS test volume. Both were removed afterward. Five alternating-order runs returned exactly 10,000 and 1,000,000 entries for both implementations. Names/attributes/times/kinds were compared separately on a 1,302-entry fixture, including hidden/system and Unicode names. Native batches remain at most 512 entries; x64 `EntryData` is 56 bytes. Source inspection shows one directory search cursor and no per-child opens.

| Fixture | Existing route, ms, five runs | Native route, ms, five runs |
|---|---|---|
| 10k | 16.59, 5.70, 5.61, 5.40, 5.29 | 7.11, 5.29, 5.18, 5.28, 5.42 |
| 1M | 596.37, 573.28, 560.70, 507.22, 521.52 | 598.89, 592.95, 529.07, 530.12, 519.18 |

These are first/warm observations, **not certified cold-cache runs**. Cumulative managed allocations increased by approximately 56 MiB per million-entry pass for either route. They do not establish process peak memory. The N-ENUM gate remains open for cold-cache, broader errors/providers and enabled-target qualification.

Native enumerator source SHA-256: `153A09B599B71CC3490060C2CDA6C49930CBB1C258CE3A48693B15138582AED4`. The platform binary observed after those timing runs was `FFB36C3A43402BDE41F74115032518EA3E31DBEE3D138504E5B787B2721E0A41`. Later builds contain additional details changes and have different hashes.

## Remaining work and EFS direction

Continue F1–F3 qualification and platform/provider/UI cases; finish F4 qualification/propagation before enabling it. The [supplementary checkpoint](2026-11-17-supplementary.md) records the F5 inventory/category/action work and initial F6 ownership/freshness integration. Complete their remaining native, output and collector gates and F6 optional columns/search-mask validation. F7 probes remain individually scoped and may honestly report unsupported until qualified.

Continue NTFS WP1–5 ownership, native traversal, atomic call admission, durable evidence/recovery, review UI and qualification. The user explicitly requested EFS implementation alongside the other features. That is part of the active objective: investigate a supported replacement protocol and implement the required key/output safeguards. Do not treat the plan's earlier deferral as permission to omit that work, or relabel the rejected protocol as safe.

The supported [EncryptFileW contract](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-encryptfilew) still takes a path, requires exclusive access and can decompress input. Microsoft's [EFS overview](https://learn.microsoft.com/en-us/windows/win32/fileio/file-encryption) also excludes most TxF operations. No supported replacement for the plan's exact binding/conflict invariant has yet been established in this implementation. This is an unresolved engineering gate, not a claim that successful stress tests prove safety.

Use the isolated guest/Mac checkouts named `FileCat-native-20261117-01a1e6d0`. Guest setup and transfer scripts are task scratch in `.tmp`, not product code. Do not print private keys or remove personal certificates. Native mutation tests belong to deliberately prepared disposable fixtures in these environments.

## Cloud-root presentation in list and picture modes

List rows, picture tiles, focused inspection and accessible descriptions now use the same registered cloud-root snapshot. The root context is available to a freshly opened picture view and updates with the active tab. Presentation distinguishes a managed file from evidence of its current residency and keeps the parent navigation row outside the cloud description. Places uses the same snapshot.

The Windows x64 VM passed all 29 selected file-list, accessibility and marked-item tests, including the two cached-root cases. The simulated Windows shell supplied the root once. No per-row discovery was required. The SHA-256 of `native-cloud-root-qualified-x64.trx` is `F38DD11A2F51FCAA810DF67ECF98B9E9281D3F8ED6C131137A8A264EC99EE2C5`. Apple silicon passed 27 corresponding App cases, with two Windows-only skips, and all 15 cloud and reparse taxonomy cases covering pin hints, observed socket tags and unknown surrogate and non-surrogate tags.

A recognized Cloud Files tag on a reparse entry now supplies managed status to the list, picture grid, focused inspection and screen-reader description, even when its sync root is absent from the cached list. Pin intent, local content and synchronization retain their own evidence. A tag without the reparse attribute and an unrelated app-alias tag supply no cloud evidence.

The Windows VM passed all 13 presentation and accessibility cases, including the new tag-only row. Its TRX SHA-256 is `A6ADF9150898DE7518145A1BC7BD94A4C5B28651658EFE828DD8ACE9E4812CBB`. Apple silicon passed the corresponding ten portable App cases, with three Windows-only skips, and all 16 Core cloud and reparse taxonomy cases.

### Picture-tile reparse captions

Picture tiles now show the cached reparse type below the filename, using the same classification as the accessible row description. The fixture compares unavailable and unknown tags, both surrogate variants, ambiguous mount points, WSL links, app aliases, WOF, Cloud Files and Unix-domain sockets. A file with the existing Offline flag still shows the download warning on its tile. The caption uses listing data.

The Windows VM passed all 24 selected picture-grid, file-list and accessibility cases. The SHA-256 of `picture-reparse-windows-r1.trx` is `4D1CE9FFEB0A3076BC365D82A8DC24071AA32BC0490099C008BEF249F4D59EE6`. Apple silicon passed all 12 portable cases, with 12 Windows-only skips.

## Observation admission after a failed worker start

The scheduler starts missing worker capacity before adding the request to the device queue. A failed start retires the work item and releases the shared observation permit. The regression injects a failure at thread startup, reacquires all four device permits, then sends another request through the same scheduler and device. The retry runs with a valid permit. The rejected request never reaches the later worker.

The ownership and scheduler selection passed 32 cases each on the Ubuntu x64 VM and Apple silicon, with zero failures or skips. The receipt SHA-256 is `43069CBE57877BEA93F7AFAA3CF8DC0FB78695A2BD344DDBEAA4725A1DC8D4AB` for `owned-enqueue-ubuntu-r1.trx` and `89331F3F0377D8178A671AF5AB8A457B5CFE3DE7566F708B0F5ACA753421E271` for `owned-enqueue-macos-r1.trx`.

A follow-up held the only device worker, canceled 12 queued observation requests, and then shut down with another observation queued. A new request after shutdown also canceled. The original worker finished after release, none of the queued readers entered, and all four device permits were available again. The expanded selection passed 33 cases on each host with zero failures or skips. The SHA-256 is `35E0110DCF07B5511D1360877248AD4591097B5D123FC2534AFDA13BBB5019D0` for `owned-queue-ubuntu-r2.trx` and `CBC681E4FDF0E4EA08FA6AFFAFBA2A4973B52F0EBCF486DAFF0130BE46ADE648` for `owned-queue-macos-r2.trx`.

### Shutdown during an already running observation

The regression held a real file handle in the only device worker while the scheduler shut down. The handle stayed open and a fifth device permit remained blocked until the read returned. The first run delivered the canceled read's stale result. The shared observation API now checks cancellation again after the producer finishes, including synchronous reads that reuse the worker's existing admission. The fixed test observed cancellation, confirmed that the file handle had closed and acquired the released permit.

Apple silicon and Ubuntu x64 each passed all 43 focused Core cases covering details ownership, metadata freshness, supplementary inventory and Find. The Mac also passed eight related application cases, with two platform-specific skips. Under `.tmp/filecat-efs-live-166b6897-r1/observation-shutdown-3d56a544-r2`, `ownership-mac-before-fix.trx` has SHA-256 `FF1FB17950909D8F38E7B5C9B54A2D61E86AE25B1E2096D38D20993057B7D6CB`, `ownership-mac-r2.trx` has `D1B959B2ADFED948E78E0A6C6A5D90755310544BCA3B6FE67CDCA510B779DE81`, and `ownership-ubuntu-r2.trx` has `3F347A55F83B17D37E9CE456129EDBCAC72515D4395F215D6F957C2606EB73E5`. The App receipt `observation-app-mac.trx` has `6A03A64DC07FE53C47F79585C3353060A64FDF3100BBD37C347A471EDE62EA61`.

## A replacement file at the observed path

The App fixture moved a real file aside after the summary completed and wrote a second file at the same path, with the same length and exact modification time. The full report showed the replacement's native identity and the changed-object note. Returning to the summary showed the same replacement identity, and reopening the cached full report returned identical text. Both payloads stayed out of the report and kept their exact contents. A separate blocked-read fixture reused a fake identity and logical size across revisions. Switching tiers canceled the pending publication and preserved the displayed observation until the next read completed.

Both cases passed on Windows x64 NTFS, Apple silicon APFS and Ubuntu x64 tmpfs. The focused selections passed 28 cases with six skips on Windows and 31 cases with three skips on each Unix host. Under `.tmp/filecat-efs-live-166b6897-r1/details-scope-b244e99a`, the SHA-256 values are `405B955A89F3A81ED964804D86BAE0514098B9F7128CBD177CF8693D0BB4140A` for `details-scope-windows-r1.trx`, `EC8248CC1F9FCF7A7F025BC9976C61A5C0095E3DC33C4494F48B21995CA2E6BD` for `details-scope-mac.trx` and `0C8EF11914623E54FDC95749037F3E5FF9D86B3CAA663007210415B2D723D90B` for `details-scope-ubuntu-r2.trx`.

## Recorded Unix permissions and execution attributes

Summary and Full Details describe the observed other-user write and set-ID bits on ordinary files and the sticky bit on directories. The set-ID text names mount and execution rules that can suppress privilege changes. The sticky-bit text names the entry owner, the directory owner and an appropriately privileged process, subject to other access checks. Both tiers render these facts from the same mode field and leave ACL, capability and effective-access acquisition to qualified routes.

The native fixture set mode `6753` on an owned file and `1777` on an owned directory. Apple silicon and Ubuntu x64 verified the exact reported bits and wording, including the directory-owner exception. Both also ran the existing legacy permissions regression. Ubuntu repeated the three ext inode flag cases with the storage field labeled **File compression** and the historical bits described in the recorded flags. Ubuntu passed 69 focused Core cases with 13 platform or extra-fixture skips. The Mac passed 63 with 19 skips. Under `.tmp/filecat-efs-live-166b6897-r1/unix-recorded-access-95ccc85a-r1`, `unix-recorded-access-ubuntu-r1.trx` has SHA-256 `4D6D313E94546E78AD5304AAB46E21985578CC030A03745B5F1F765B1534E023` and `unix-recorded-access-mac.trx` has `854994A56852ED6457D3AEC3228713EB84DD501CA14D12A8CC064DA7FCD69817`.
