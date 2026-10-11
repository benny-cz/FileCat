# E-I362 — keep a nested spool present through reader admission and use

Baseline **d9889ce780ab0e6d8e7528cd056ffbc877963766 / 1,631 raw Git blobs**, with a controlled test overlay. A real reader completes the outer-member copy, then waits on the existing inner cache lock. Four later nested members evict that private file before reader admission. Both mixed ZIP/TAR directions fail with `FileNotFoundException`, with and without source identities. The host and three native baselines each retain **four passes and four failures**. All four healthy cases and every retry independently reproduce the complete expected payload.

The correction acquires a private-source lease while the spool cache still owns the file. Eviction returns the cache reference; admitted indexes and progressive readers retain their reference until retirement. ZIP recursion keeps the outer source leased while copying. The built-in cross-provider route uses the same lease contract. Cache bounds and lock order remain; final returns use reference counts without acquiring a provider lock. The compatibility bare-path callback remains available and is not claimed to supply ownership.

All **112 final combined controls pass** across host, Windows guest, Ubuntu and Mac:32 admission, eight source-close and72 lazy-reopen controls. All36 lazy refusals/108 complete reads qualify on the final proposal as well as the separate earlier producer. The independent reader decodes every complete outer and inner archive and verifies **64 full 4,096-byte reads**, source preservation, observed eviction order, real blocked reader state and final handle/root retirement. Host affected coverage is **422 passes / 18 explicit skips**. The three native inventories select 40 complete affected classes: **1,605 records, 1,528 passes and 77 explicit skips**. Three existing reflection fixtures adapt their cache value type and cleanup; every original assertion is unchanged. The new schedule/assertions are unchanged between proposals; its cache-value adapter changes are declared.

The separately sealed App followup builds the same product proposal and passes all **114 unchanged archive-consumer cases**, retaining their previous original-CI outcomes. This is headless host App evidence, not native UI evidence. Independent postchecks verify **1,161 distinct native files**, zero owned workers/scratch/listeners, and seven restored host roots/ten files across the complete proposal chain. Latest independent postchecks rehash1,161 distinct native files and restore three new host roots/four files.

A further controlled post-close probe rejects the first lease draft: its return expression creates a lease before the copying source disposes. A disposal error can abandon that lease. Four host/native baseline failures retain the observed pathname leak after cache eviction. The final correction closes the copying source inside the holder cleanup scope before publishing the entry or handing out a lease; all eight final source-close controls pass with the original error object and closed native source preserved. All final native controllers exit0.

The first proposal's missing-namespace compilation failure remains with its actual source tree and command outputs. The Mac fixed wrapper exits 1 because it still expects one command after both commands pass; its result and count error remain, and fresh postchecks verify actual bytes and retirement without a test rerun. The unused first Windows fixed controller draft is retained separately. Wider progressive formats, legacy callbacks, cross-provider topologies, whole-process/native resources, atomic and physical/candidate qualification remain. **I106/I110 physical-source HOLD is unchanged.** Exact committed and original-push qualification of this correction follow separately.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records preserve exact source, commands, payloads, original failures/skips and restoration.

| File | SHA256 |
|---|---|
| `spool-admission-qualification-20261011-v1/independent-final-v1.json` | `b490f7feb7012008fc673eb3ac9e2dd49a114a0c95e957673685165b5c409360` |
| `spool-admission-qualification-20261011-v1/retained-tool-sources-v1.json` | `3c609f72ab191d6e437cdf3da36da07b55dafb5e8882f1bc25d9ee2e8449e4a6` |
| `spool-admission-affected-app-20261011-v1/independent-final-v1.json` | `dd923a760b253d45679b272ef768a1166eec4e5afc3662603bc3b336356ac8d5` |
| `spool-admission-affected-app-20261011-v1/retained-tool-sources-v1.json` | `7af6bfe740fbd06b4d403bd74572a9b96bb730d99bd1e5087f1210bb854a8444` |
| `spool-admission-qualification-20261011-v2/independent-final-v2.json` | `726f2191581a01d665c7ac904e2cf3e18f77c57f070f72bd0631b24a8cbfd69d` |
| `spool-admission-qualification-20261011-v2/retained-tool-sources-v2.json` | `b2dd3f41ad44f4d567239bcc3654e7f68aeb4c595836b761244e158f08b25487` |
