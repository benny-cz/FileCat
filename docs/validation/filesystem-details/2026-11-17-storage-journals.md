# Storage call evidence and accounting — 2026-11-17

This continues the [held traversal checkpoint](2026-11-17-held-traversal.md) on `main`, HEAD `68a435dc2a1d6122f5961f867bcb653b0e6968aa`, with uncommitted implementation changes. WP3 is **partial**. WindowsPlatform still composes NoStorageTransforms, and StorageTransformExecutor still refuses execution. The new per-object call runner is independently exercised; full bounded traversal/accounting and the product review/recovery flow are not yet connected. EFS remains required by the active user objective.

## Implemented

- A storage-specific versioned CRC journal and one strict collector govern writer state, typed finalization and bounded reads. Required fields, known enums, exact sequence/job/call identity, pending/result order and terminal reduction are checked. Torn, duplicate, reordered, unknown-schema and malformed records latch damage. Generic end records cannot resolve storage effects.
- A pending intent is published before its write attempt. Result/summary/end transitions are validated on a separate collector state and published only after durable flush returns. A result-flush failure preserves the pending call and latches damage, even when its bytes subsequently become readable. The last published immutable `(job, sequence, endpoint, SHA-256)` prefix remains readable without the writer lock during a blocked later flush.
- Header preflight preserves all selected roots and refuses oversize metadata before owner transfer/job creation. Records are capped at 256 KiB, journals at 64 MiB/131,072 records. Each root has a reserved 64 KiB summary slot, final closure retains 1 MiB/four records, and a call reserves another 1 MiB/four records before intent. No-call objects require no per-item records.
- Atomic call admission shares the job control lock with Pause/Stop. It accepts an attached-writer receipt once and rejects foreign/stale/closed/faulted writers. I/O, observers, cancellation callbacks and pause waits run outside that lock. A native result requires an admitted call.
- StorageObjectCallRunner performs held observation/preparation, intent flush, admission, token-free admitted call and result flush. A pause denial records NotAttempted and requires fresh observation/preparation/intent after Resume. A satisfied object requests no mutation rights. Stop after admission retains the call through postcondition/result. Uncertainty, persistence faults and record limits require whole-job stop. This helper is not the complete executor.
- Recognizable storage candidates bypass legacy eager sorting/deletion. Incremental header discovery exposes at most 64 descriptors/1 MiB of header bytes per page, including damaged/empty/unknown names, case-altered prefixes and directory candidates. Exact history loads on demand. Generic close, staged/rename cleanup, source reconstruction and Copy/Move continuation refuse storage history.
- A cancellable streaming census charges journals and associated review artifacts/temporaries, reconstructs pending review-write reservations, and counts associated physical bytes against the remaining 2 MiB allowance once. Unreadable, changing, duplicate-identity or unassociated evidence prevents complete admission accounting. A process-local budget rechecks observed directories before reserving 64 MiB + 2 MiB per worker. Retirement never waits for the census lane; concurrent retirement forces re-accounting. Writer/native/root ownership retires before the reservation and terminal publication.

## Verification

Final focused Core selection: **217 discovered** per machine, **216 passed/one fixture skip on Windows**, **215 passed/two platform or fixture skips on Ubuntu and macOS**, zero failures. Windows recovery/accounting ran under a medium-integrity token (`S-1-16-8192`). Builds completed with zero warnings/errors.

The selection includes StorageJournalTests (9), StorageCallAdmissionTests (7), StorageHistoryDiscoveryTests (2), StorageEvidenceAccountingTests (6), StorageObjectCallRunnerTests (7), review/allowance/contracts and existing scheduler, engine, retirement, synchronization, Undo and archive-backend lifecycle fixtures. It covers blocked intent/result flushes, failed flushes, malformed evidence, no-op prefix/record limits, stale receipts, reentrant cancellation, restart quota, associated crash bytes, concurrent retirement and exact owner release.

The disposable Windows native selection passed **19 tests**, zero failures/skips. Besides the previous 17 adapter/ABI/cursor tests, two cases run an actual held compression lease through Core's call runner and a disk-flushed journal. Before the admitted native call, an independent read verifies its exact flushed intent and held identity. Stop before admission performs no SET and records NotAttempted; Stop after admission still changes the held object, saves the result and produces a collector-validated canceled terminal sequence with resolved effects. This does not qualify the complete traversal worker or final resource count.

Focused App Operations/Undo selection: **75 discovered**, **74 passed/one skip on Windows**, **73 passed/two skips on Ubuntu**, **72 passed/three skips on macOS**, zero failures. A host-only native move/Undo setup failed under the host execution environment; its matching disposable Windows medium-token fixture passed. App checks preceded the final collector-publication correction; the final Core/native checks include it. No App storage command is enabled.

Final transport archive SHA-256 values (artifacts retained in `.tmp`, not committed):

| Artifact | SHA-256 |
|---|---|
| filecat-storage-journal-final-source.tar.gz | `669C498E81AF2345817C8EFE258940DB1037CD330663536FAB7182F86E88C98C` |
| filecat-storage-journal-final-core-binaries.tar.gz | `72E465C2658B631189CF2B796535ED3FFD2C9112AFE63953A628E452135700EC` |
| filecat-storage-journal-final-native-binaries.tar.gz | `6F78101094F1B0475F857DDD3BAC74209071EE4E9CD7A3C3D0F933727D7A2D0C` |

Final markers: FILECAT_STORAGE_JOURNAL_FINAL_FINISHED on Ubuntu/Mac; FILECAT_STORAGE_CORE_FINISHED and FILECAT_STORAGE_INSPECTION_FINISHED on Windows. Earlier three-platform App runs carry FILECAT_STORAGE_UI_FINISHED or FILECAT_STORAGE_JOURNAL_FINISHED.

## Remaining gates

T-LIFE.C, T-REC.C and T-LIMIT.C remain partial. Implement exact-snapshot Core acknowledgment and immutable retained report binding, inactive-writer ownership through publication, crash-temp reconciliation, durable retention dependencies and journal-first pruning. Census and reads still use ordinary filesystem opens; private, object-bound evidence access and complete 188+1+3 accounting are not qualified. Measure/resolve large-history census cost and dirty cross-instance observations under N-C3. A discovery page is not the full census, and its cursor must be released before another cursor owner or user wait.

Connect bounded traversal, closed-ID rediscovery/overlap attribution and final root accounting to the call runner; publish same-session Needs review after owner retirement; integrate incremental startup discovery and storage-specific Operations/report actions; finish command/review/origin/mark/refresh UI. Complete remaining native capabilities/adversary/large-volume/stream/provider and performance gates before composing the adapter into the product. EFS identity/key/output protocol and the companion filesystem plan's outstanding gates remain required work.
