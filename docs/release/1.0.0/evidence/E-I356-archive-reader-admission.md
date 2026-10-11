# E-I356 — retain archive indexes through reader acquisition

Baseline **2dda4a89ed4dbee9125f7913d6f73a6feb98437e /1610 raw Git blobs**, with one identical sixteen-case control overlay. ZIP/TAR content lookup released its cache lock before finding and extracting the member. A concurrent unavailable-identity request could retire that index before the first caller copied bytes or acquired a progressive lease, raising “The archive was closed; open it again.”

Both providers now keep the cache lock through lookup and extraction/lease acquisition. Nested paths resolve before locking, preserving cross-provider spool/cache ordering. Unavailable identities still prevent source reuse; stable sharing, member safety checks, retained readers and bounded eviction remain.

Controls cover ZIP/TAR, 64/5000 entries, two/eight callers, known/unavailable identity capability and eight actual concurrent-thread rounds per case. Across the host, Ubuntu, Mac and background Windows guest, **64 final cases pass without skips**, with **2560 complete 4096-byte reads independently reconstructed** and every observed native source handle closed. Source wrappers remain held through release; no forced GC or product hook is used. Original closed-index errors and healthy observations remain.

The final source includes I355's four qualified overlays plus this control and two product overlays: **seven declared overlays**. **7114 affected records /177 exact skips** retain all7034 I355 predecessors and add eighty passing recovery-image controls. The **377-case headless App** inventory retains all348 prior results/twelve exact skips and adds29 unchanged edit-preparation passes. Independent postchecks rehash **1161 native files**, confirm no guest workers/temps and closed listeners. Six host scratch roots/twelve files are archived, verified and restored.

[Original I354 CI](E-CI-archive-source-replacement.md) failed a Windows eight-edit ZIP assertion with seven sessions. All29 unchanged edit-preparation cases pass in thirteen isolated runs (377 observations), which does not prove the original cause. This independently reproduced reader-admission defect is fixed without changing that test, its assertions or timing limits. The initial repeat harness's wrong expected count, first probe's missing namespace import and an unexecuted publication-preparation tool syntax error remain separate.

Wider native/leased/resource scope, atomic source binding, physical-source HOLD and candidate qualification remain. No workstation UI, physical source or persistent setting changes occurred.

## Committed follow-up and nested boundary — 2026-10-11

[Exact committed qualification](E-I355-I356-native-qualification.md) now verifies04f6f18/1616 raw blobs/no overlays. [I357](E-I357-nested-reader-admission.md) separately reproduces and corrects the remaining outer-index lifetime gap during nested-member copying. The original paired evidence and wider limits remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, payloads, commands, failures/skips and restoration.

| File | SHA256 |
|---|---|
| `archive-reader-admission-safety-20261011-v1/independent-final-v1.json` | `0671ff42f913047fff1b4d0fbda77898b40fb176d5721aa572773eb29c79a548` |
| `archive-reader-admission-safety-20261011-v1/retained-tool-sources-v1.json` | `0de58f9fd14bee6c3915dfe79386bf9fe5f835b39cbf847aec5c564dd6b85980` |
