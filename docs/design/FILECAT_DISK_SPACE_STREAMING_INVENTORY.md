# Indexed inventory persistence checkpoint

This documents the implemented full-dataset **writer**, not qualification of streaming import, public indexed persistence, or the complete disk-space plan.

`AnalysisDatasetLease.SaveNewAsync` retains the immutable revision through actual work and cleanup. It writes every object, path, fact and sampled issue from the indexed tables, not the bounded presentation preview. Native captured path IDs and parent IDs remain unchanged; object/fact/root wire IDs use the existing schema-1 ordinal mapping. General imported path-ID mapping remains unfinished.

Two process-wide save reservations cover suspended jobs and actual cleanup. Each admitted Background turn prepares at most 512 records, emits at most 256 KiB, and yields after 20 ms between record operations. One pending JSON record is limited to 1 MiB and drains in chunks of at most 64 KiB. Source and destination use the captured paired device admission; a third device is refused before destination creation. A nonresponding device cancels subsequent work. Existing total inventory byte/record limits apply. This is bounded application work, not a guarantee that a native filesystem call completes within 20 ms.

The destination factory is trusted application configuration. Native private storage performs create-new publication; conflicts preserve existing bytes. Cancellation retains the reservation and resources until actual native work returns. A failure after publication is explicitly uncertain and must never trigger an automatic second save. No serialized record restores source, action, helper or content authority.

## Schema-1 coverage extension

Coverage may contain the optional field `qualification` with exactly this four-element tuple:

```json
"qualification": [true, "9", true, false]
```

| Position | Meaning | Type |
|---|---|---|
| 0 | Enumeration counts are known | Boolean |
| 1 | Number of eligible directories | Canonical unsigned decimal string |
| 2 | Stale-location samples are complete | Boolean |
| 3 | Some staleness has no recorded location | Boolean |

The reader accepts the exact legacy coverage fields or those fields plus this tuple. Other fields, wrong tuple lengths/types, contradictory counts, invalid stale references and false complete-sample claims are refused even with a valid trailer hash. Legacy records retain their previous validation. Older strict readers safely reject the additional field; there is no silent downgrade or overwrite.

Qualified data preserves unknown enumeration and incomplete/unlocated staleness. The window displays persistent notices for these conditions. Imported data remains imported and inert, with its taint preserved. Streaming import, arbitrary imported IDs, indexed catalog/UI adoption, large-resource qualification and the final independent review remain required.

The all-platform full-save controls and exact source/binary provenance are recorded in [the r462 manifest](../validation/disk-space/source-manifest-r462.json). Own-window visual-tree renders do not qualify compositor, physical input, accessibility, packaging or the complete UX.

## Original path-ID mapping stage

`AnalysisInventoryPathMap` is the implemented identity stage for the forthcoming streaming reader. It stores original ID/parent pairs in file order and builds an exact external index from unsigned original IDs to storage rows. Original IDs may be nonconsecutive, descend numerically and include `UInt64.MaxValue`. The virtual parent sentinel remains zero. Storage rows are separate from observation identity.

The stage rejects zero or duplicate IDs, self-parenting, missing parents and parents that occur later in the file. It checks parent **file order**, rather than comparing the numeric IDs. Parent-before-child validation prevents cycles. This layer does not validate root membership, entry kinds, depth, facts, coverage, trailer or aggregates; the complete reader must validate these before publishing an inventory.

The trusted upstream caller supplies already extracted identity pairs on captured device admission. Input and validation turns handle at most 512 pairs and yield after 20 ms between records. External sorting uses its existing bounded runs and merge spool; no inventory-sized dictionary is built. Two process-wide build reservations span suspended construction and actual cleanup. Immutable queries retain both native tables after workspace-owner closure and close through the actual I/O lifetime wrapper. Unknown IDs remain absent rather than being interpreted as row numbers.

The r471 native fixture exercises 5,201 nonconsecutive IDs, including maximum/low values and parents numerically larger than children, through a forced external merge. It queries after workspace-owner closure and verifies that anonymous files and reservations are gone after actual map closure on Windows, Mac and Ubuntu. Additional controls cover duplicate/missing/forward/self references, empty input, partial native-write failure and noncooperative canceled input. This stage is not yet adopted by an indexed inventory reader or explorer; complete streaming import and original-ID preservation through that workflow remain unfinished. [The r474 manifest](../validation/disk-space/source-manifest-r474.json) records the exact sources, tests and scope.

## Shared typed-record decoder

`AnalysisInventoryFormat.RecordDecoder` is now used by the existing bounded-memory snapshot reader. The forthcoming streaming reader can reuse it after bounded framing, rather than maintaining a second schema implementation. It accepts one already framed record without LF, validates strict UTF-8/JSON/typed fields, record order, container byte/record/kind limits and the mandatory counts/SHA trailer. It owns constant-sized container state and no stream or earlier payload collection. Typed values own their data independently of the caller's reusable input buffer.

The caller must confirm actual EOF and call `Complete()` before publishing any result. A trailer alone is insufficient. Any read/admission/cancellation failure retires the decoder; it cannot resume from partly consumed state. Retained-data admission runs before typed payload allocation; a streaming caller must reserve its bounded decode/staging job before using the decoder. The current snapshot loader preserves its shared retained-memory accounting, complete cross-record relationship/coverage validation and all-metric recomputation. Historical records remain inert. This extraction does not implement bounded asynchronous framing, scratch-table staging, original-ID graph adoption or a complete indexed importer. [The r481 evidence](../validation/disk-space/source-manifest-r481.json) records the cross-platform controls.

## Incremental framing and private staging

`RecordFramer.Step` performs at most one64 KiB source read and keeps a partial record across turns. Its reusable line buffer is bounded by1 MiB; actual bytes, delimiter, cancellation and terminal-failure state are checked independently. The existing synchronous snapshot reader uses `Next` over the same framing logic. The private staging pipeline calls individual steps through captured Background I/O and decodes completed records off device workers.

`StagePrivateAsync` takes trusted private-input configuration and an already prepared scratch workspace. It refuses a third device before invoking the input factory. The source is opened read-only through an owned native file/directory pair and closes before handoff. Original object/path/fact/issue JSON is packed into authenticated64 KiB blocks; fixed36-byte locators carry logical offsets/lengths and original path/parent IDs. Headers remain bounded in memory. Private byte/session/profile limits still apply and can refuse the stage; an8 GiB input allowance does not promise that its expanded staging/indexes fit the independent4 GiB scratch ceiling.

Two process-wide import reservations hold construction buffers through actual cancellation and cleanup. Construction reserves16 MiB of the shared import budget; a retained stage owns8 MiB covering its header and table directories/caches. Two record-assembly jobs bound concurrent read buffers. Data ownership spans actual queued/running I/O and decode work; scratch queries stop depending on the input device after its native close. Original-ID maps read only the fixed locator table and retain their own files after stage closure.

This stage validates exact schema, record order, counts, hash and real EOF. It deliberately has no public inventory/snapshot/explorer interface or runtime source authority. A consumer must still validate cross-record root/kind/depth/identity/fact/issue/coverage relationships and recomputed metrics, retain original IDs through graph and view APIs, and only then publish a loaded inventory. General external-file input, shared pending/CPU/device-alias/resource integration, complete indexed adoption and large-workload qualification remain required. Native private-file controls on Windows, Mac and Ubuntu and their limitations are recorded in [the r492 manifest](../validation/disk-space/source-manifest-r492.json).

## Validated staged dataset adoption

`AnalysisInventoryStage.BuildDatasetAsync` now retains captured staged data through original-ID map construction, bounded scratch-record assembly, typed graph append/index validation, supplied coverage checks and all four checked summaries. Only that complete chain returns an `AnalysisDataset`. The schema/trailer stage itself remains distinct from a loaded dataset. Parent order is storage-row order; original IDs are nonzero opaque UInt64 values. Graph `ReadPath(id)` resolves identity, while sequential scans and path-valued secondary indexes use `ReadPathRow(row)`. Native dense captures keep a computed direct-row fast path. Duplicate original IDs and absent/forward parents cannot publish a dataset. Original directory/issue/stale references survive related queries and whole saves.

Qualified coverage retains explicit enumeration/sample uncertainty and checks eligible directories, totals, measurement-state counts and actual stale events. Legacy zero enumeration is normalized to unknown when eligible directories exist. Each import recomputes every metric before publication; checked overflow is invalid inventory data. Two actual-lifetime constructions reserve24 MiB each from shared import memory, in addition to retained8 MiB stage reservations. Cancellation and caller disposal keep the source files until actual I/O returns, with all temporary graph/map owners closed on failure. This reservation is not a claim that the complete global memory/CPU/pending/native-alias qualification is finished.

Native private-file import/filter/save/reopen probes on all three platforms retain original IDs through5,201 paths and a1 MiB source record, preserve source bytes and leave no scratch reservation/files after actual closure. General external-file guarding, catalog/explorer UI adoption, public registration, global resource qualification and1M/10M workloads remain unfinished. See [r505 evidence](../validation/disk-space/source-manifest-r505.json).
