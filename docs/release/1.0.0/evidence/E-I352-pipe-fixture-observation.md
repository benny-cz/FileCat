# E-I352 — remove an unnecessary async dependency from the pipe fixture

Original CI at **69cd563** retains a Windows ARM64 five-second timeout waiting for the regular-image recovery helper session to close. The fixture served synchronous protocol operations on a dedicated long-running thread but created an asynchronous server pipe.

An isolated control invokes the actual private fixture factory, reads all **4096 known bytes**, then deliberately holds the only process-local thread-pool worker. The original fixture's server task stays incomplete through the observation interval; the one-flag synchronous-pipe proposal completes while the blocker remains held. Both return the exact close reason and unchanged bytes after release. **Four paired host/native observations** verify this dependency. The three existing regular-image pipe tests also pass in both native versions: **six passes**, without changing their five-second deadlines, assertions or product protocol. Physical-device methods are explicitly excluded.

The background Windows guest uses high RID 12288/session 0 and pinned .NET 10.0.12. Independent checks verify **535 native files**, absent owned processes/temps and closed listeners. Owned host scratch restoration is separately sealed with [I353](E-I353-hidden-data-native-fixture.md).

The scheduler dependency is reproduced; the exact cause of the original ARM64 timeout remains unproven. The original CI failure, first log retrieval's terminal-escape refusal and reader timing quantization failure remain adverse. No retry, timeout extension, new skip, weaker assertion, physical-source access or host UI is introduced. A new original committed CI run still has to qualify the final batch.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, payloads, commands, original failures/skips and restoration.

| File | SHA256 |
|---|---|
| `pipe-fixture-safety-20261011-v1/independent-final-v1.json` | `bd1f16bbcde689f63cb167fa577be34d3d94035dbc3a3745486c1264185c8142` |
| `pipe-fixture-safety-20261011-v1/retained-tool-sources-v1.json` | `732f92e3471de4501ce99a7bd4ae986a33d5c43ead358a02a6f77db820cdfbdc` |
