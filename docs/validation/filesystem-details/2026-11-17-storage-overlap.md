# Storage overlap planning and bounded identity ledger — 2026-11-17

This continues the [bound history checkpoint](2026-11-17-storage-history.md) on `main`, HEAD `68a435dc2a1d6122f5961f867bcb653b0e6968aa`, with uncommitted changes. WP2 remains partial. These execution components are implemented and tested, but the executor still refuses jobs and WindowsPlatform still supplies NoStorageTransforms. EFS remains required by the active goal.

## Implemented

The held traversal contract now exposes its admitted identity and a separately revalidated root-to-parent ancestor chain. The Windows implementation derives that chain from its existing held native ancestors; it adds no pathname-based ancestry inference or filesystem handles. It preserves the current native allowance, pin lifetime and 64-level bound.

StorageExecutionPlan maps every original selected source to its unique live pin with a 64-bit source mask. Recursive planning opens one root traversal at a time and chooses the nearest simultaneously pinned selected-directory ancestor. Source order, duplicate selections and textual path prefixes do not change that proof. Each probe closes before the next opens. Selected-items-only scope neither inspects ancestry nor prunes roots. A covered selected root remains an obligation: the executor must actually reach its live pinned object before reporting it complete.

Changed root identity, duplicate/self/foreign-volume ancestry, over-depth chains and inconsistent inventory counts are refused. Denial remains a typed incomplete root while independent roots retain their mappings. Roots may move between independent probes; a resulting ancestry cycle is recorded as churn, with affected mappings retained and no arbitrary traversal order. Cancellation retires the probe without releasing the job's original review pins.

StorageVisitLedger uses fixed hash buckets and stable record-array chunks. It stores compact canonical native IDs, bounded ordinal opaque IDs, verified-volume distinctions, owner-source masks, final object outcomes and historical subtree completeness. It retains no descendant path field or native owner. Each observation consumes the one-million-observation limit, including duplicates. Chunk/string/volume capacity is charged before admission against the 64 MiB retained-data allowance. This is conservative owner-capacity accounting; it is not a claim that the entire process heap stays within 64 MiB.

A live duplicate is distinguished from a closed-ID rediscovery. A closed ID never receives a new admission receipt or turns its previous outcome into fresh AlreadySatisfied/complete-subtree evidence. Settlement and retirement are one-shot. The executor must handle duplicate attribution/churn explicitly; an entry's historical completion flag is not current filesystem authority.

The initial million-ID trial exposed a poor bucket distribution for sequential GUID-shaped directory IDs. That trial was not accepted as verification. The affected Ubuntu/Mac processes were identified and stopped; the Windows task and host were inspected and confirmed terminal before replacement. The hash now mixes both complete 64-bit ID halves, volume and any opaque ordinal ID before selecting a bucket. Final runs below use that correction. An unrelated Windows guest analysis process was identified and left running.

## Verification

Core and Windows platform builds completed with zero warnings/errors. The final focused Core selection discovers 256 cases: 255 passed/one existing mount-fixture skip on Windows; 254 passed/two existing platform/fixture skips on Ubuntu and macOS. Zero failures. The selection includes reverse-order nested selections, duplicate original mappings, no lexical ancestry proof, selected-only scope, invalid/changed/cyclic ancestry, access denial, cancellation ownership, closed-ID behavior, cloned volume serials, ordinal opaque identities, count/byte limits, and one million native-style IDs followed by a refused admission. Selection times were approximately 8.2 seconds on Windows, 5.1 seconds on Ubuntu and 7.5 seconds on macOS; these are whole-selection timings, not a standalone benchmark.

The normal-user Windows 11 x64 NTFS selection passed 31 native cases with zero failures/skips. Its new overlap fixture captures a child first and its parent twice, verifies the held ancestry and every source mask, then checks that bytes and compression defaults were unchanged. Earlier held compression/traversal, cursor, private history and retention cases remain green. This does not qualify ARM64 or every serviced Windows build.

Transport archive SHA-256 values retained in `.tmp`:

| Artifact | SHA-256 |
|---|---|
| filecat-storage-overlap-source.tar.gz | `0AC928FDA1C98603FEA16358D0D337A2E367841B9376CC2DA46D028E235709F5` |
| filecat-storage-overlap-core-binaries.tar.gz | `942789716BA2E5700C56845C63C9031789B016178B08D575AF86A0BAE42E6BCC` |
| filecat-storage-overlap-native-patch.tar.gz | `483F945F6A23C0F301659E9FC4717D743A0B891C147525CE62B200AEB81F8B5D` |

Markers: FILECAT_STORAGE_OVERLAP_FINISHED on Ubuntu/Mac; FILECAT_STORAGE_CORE_FINISHED and FILECAT_STORAGE_REVIEW_NATIVE_FINISHED on Windows. The final Windows logs record medium integrity.

## Remaining acceptance

Connect these components to the nonrecursive preorder executor and durable per-call runner. Preserve successful directory outcomes separately from later enumeration failure; account covered-but-unreached roots, ambiguous closed-ID rediscovery and unknown omitted scope; continue independent siblings after per-child access/open failures; stop the whole job on uncertainty, persistence faults or global limits. Implement bounded complete reason counts/no-call samples, generic counter projection, retries/decisions and final root summaries before enabling execution.

Finish the command/review and Operations/report/startup integration, full ownership/fault/provider/performance gates, and EFS binding/key/output work. T-TRAV.C and T-LIMIT.C remain partial. The companion filesystem roadmap still has unfinished packages and native acceptance work.
