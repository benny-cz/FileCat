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
