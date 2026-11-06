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
