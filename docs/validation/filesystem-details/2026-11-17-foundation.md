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
