# Integrated storage executor — 2026-11-17

This continues the [overlap checkpoint](2026-11-17-storage-overlap.md) on `main`, HEAD `68a435dc2a1d6122f5961f867bcb653b0e6968aa`, with uncommitted changes. The internal native adapter now executes compression jobs through JobManager and the durable writer in qualification fixtures. Product Windows composition still supplies the unavailable transform service; command/review/Operations integration and remaining release gates are unfinished. EFS remains required by the active goal.

## Implemented

The executor now performs a nonrecursive preorder walk through the held platform traversal. It uses verified overlap planning, preserves every original source mapping and processes each stable physical object once. A directory default is established before its contents; AlreadySatisfied directories still descend. Failed defaults exclude their subtree. Changed parents remain Changed when later enumeration fails, with incomplete scope recorded separately. Selected-only scope neither prunes roots nor opens descendant cursors.

The bounded ledger distinguishes live/closed rediscovery; it never mutates a closed-ID rediscovery again or reuses an earlier complete subtree as current coverage. Receiving scopes become incomplete and observation counts/ambiguous-duplicate totals are explicit. Covered roots not reached after an ancestor failure remain known NotProcessed roots. Unseen descendants remain unknown rather than becoming invented counters. The core walk also enforces its active-depth bound; the native traversal retains its stricter inclusive-ancestor bound.

Per-child traversal entries can report a failed open while preserving native cursor progress and independent siblings. Retry delegates remain bound to the held parent and original cursor entry. Before native call admission, sharing conflicts get at most two short automatic retries and then the existing job-scoped Retry/Skip/Stop decision. The same helper handles root opening and held mutation preparation. Admitted calls and uncertain effects are never retried through this helper.

Every admitted mutation uses the existing intent/admission/postcondition/result protocol. Stop before admission records no call; Stop after admission preserves the returned effect and its durable result. A known child opened immediately before Stop is accounted NotProcessed and closed. Uncertainty, persistence failure or a global observation/journal bound stops further mutation. Held traversal owners close before final root summaries; writer and review pins retain their existing manager-owned retirement order before terminal publication.

One accounting owner publishes disjoint global outcomes, overlapping per-root counters/statuses, full occurrence totals with bounded reason categories, and at most 16 no-call examples within a 32 KiB encoded sample budget. Category overflow remains explicit; native-call causes stay exact in their call records. Successful optional examples yield space to issue examples. Examples and omission/ambiguity counts survive the strict summary collector. A sample is never a complete per-item log. Generic item counters project from the typed snapshot, while current path/time and a durable-prefix receipt remain available for live projections.

## Verification

Core, Windows platform and App builds completed with zero warnings/errors. The final focused Core selection discovered 273 cases: Windows passed 272 with one existing mount-fixture skip; Ubuntu/macOS each passed 271 with two existing platform/fixture skips. Zero failures. Seventeen executor cases cover reversed/duplicate overlap mappings, selected-only scope, preorder defaults for both actions, no-op directory descent, changed-directory/enumeration failure, independent siblings after child failure, closed-ID rediscovery, policy skips, desired state with native error, depth/count bounds, bounded complete reason totals/examples, Stop boundaries and result/root-summary flush failures. Prior storage, scheduler, lifecycle, Undo and archive selections remain included.

Normal-user Windows 11 x64 NTFS passed 37 native cases with zero failures/skips. New full JobManager fixtures execute recursive compression and removal, preserve main-stream/ADS sentinels, include hidden files and duplicate/reversed selections, skip read-only files/reparse subtrees, preserve independent siblings after access denial, and exercise sharing-conflict Retry/Skip. A deterministic cursor fixture deletes an entry after its batch was read and verifies one failed child plus remaining admitted siblings. The private writer is closed before terminal review; the final durable history can be opened immediately.

The ACL fixture was corrected to deny data access and independently prove that denial. Restoring a previously read FileSecurity object without marking its access section modified did not restore the DACL; the fixture now writes a newly marked descriptor. Those earlier failing fixture trials are not acceptance evidence. Final runs use the corrected fixture and restored permissions.

The 150-case App recovery/Operations/Undo regression selection passed 149/one existing skip on Windows, 148/two on Ubuntu and 147/three on macOS, with zero failures. These regressions do not prove the still-unimplemented storage-specific UI.

Transport archive SHA-256 values retained in `.tmp`:

| Artifact | SHA-256 |
|---|---|
| filecat-storage-executor-qualified-source.tar.gz | `314DFB8E9F32B0560DC6B9167F14C3DD4F1D05B2661FBBCC0137B40A3B5FD925` |
| filecat-storage-executor-qualified-core-binaries.tar.gz | `E5A34B236DCBBFBE0E47FD208A5C446EA8336C2587C432C5C8715B78C2AEA7B6` |
| filecat-storage-executor-qualified-native-patch.tar.gz | `9725F2767981781B0B46E65EBE0C9BFE9D19F5EAD002F9855CE5A119B8E75F41` |
| filecat-storage-executor-final-source.tar.gz | `BB904D0BA4D91AB88F31608FF8AB8ABD9B6A67C27BD4E90620027EEF084610D4` |
| filecat-storage-executor-final-ui-patch.tar.gz | `99E2BDF932C63AA48DFC8F6F7835ED356793D9DF865CAEE8269B4FA04FA0D416` |

The qualified source/core/native archives include the final removal round-trip cases. The earlier final-source/UI archives and logs cover the same production implementation plus the expanded App regressions. Markers are FILECAT_STORAGE_EXECUTOR_QUALIFIED_FINISHED on Ubuntu/Mac Core, FILECAT_STORAGE_EXECUTOR_FINAL_FINISHED on their Core/App runs, and FILECAT_STORAGE_CORE_FINISHED / FILECAT_STORAGE_REVIEW_NATIVE_FINISHED / FILECAT_STORAGE_UI_FINISHED on Windows. Windows native/Core logs record medium integrity.

## Remaining acceptance

WP2/WP3 and T-TRAV.C/T-OUT.C/T-LIFE.C/T-REC.C/T-LIMIT.C remain partial. Finish storage command/review/origin/mark/refresh integration; storage-specific Operations, taskbar, notifications and report projections; incremental startup and immediate same-session Needs review publication; retained-pair UI; exact call-detail paging/export and acknowledgment flows. Complete additional crash/device/disk-full, architecture/OS/filesystem/provider, ownership/fault and large native-volume qualification before enabling product composition. This fixture set is not a complete Windows support matrix or the full 448-handle release proof.

EFS binding/conflict, key/setup/backup and output-route implementation/qualification remain required, alongside the companion filesystem roadmap's unfinished work. No EFS action was enabled by this compression executor work.
