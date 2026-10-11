# E-I354 — refresh archive indexes and nested spools after source replacement

Baseline **7e88ef72ac00a11b9d64f95db61d8d2862d4a27d /1606 raw Git blobs**, one identical 32-case test overlay. The final proposal adds three product and two native-capability fixture overlays. ZIP and TAR reused a retained source/index when an independently created archive replaced its path with the same length and modification time. Fresh content could come from the original file, listings stayed stale, and nested archives could return an old private spool.

Both providers now include stable source identity in index and nested-spool keys. The application explicitly supplies its platform file operations, resolving a final link target before querying identity. The portable default obtains Linux/macOS device/inode identity. An unavailable identity gives a fresh key, preventing reuse while preserving bounded ownership; it may require extra index or spool work. Native Windows code remains in its adapter. Existing readers keep their original content when an old index retires.

The paired 32 cases cover ZIP/TAR outer files, unchanged repeats, changed content/catalogues, existing reader leases, every ZIP/TAR nesting combination, and known/unavailable identity modes. Across the host, Ubuntu, Mac and background Windows guest, **80 original failures and 48 healthy cases** become **128 passes with no skips**. The independent reader reconstructs every complete 4096-byte payload, decodes the retained outer/inner archive bytes, checks distinct actual source identities and the observed rename transition, and preserves source-unchanged observations and owned-root cleanup.

**6906 selected affected records /177 exact skips** retain all 6778 relevant committed Core predecessors and their 129 exact skips. The additional 128 existing archive-case controls retain 80 passes/48 filesystem-specific skips. The selected **348-case headless App** inventory retains 336 passes/twelve exact skips. Independent native postchecks rehash **1161 files** and verify no owned test processes/temps and closed transport listeners. Six host scratch roots/29 files are archived, verified and restored.

The initial eight- and sixteen-case probes remain separately retained. The original eight-case prepared path was later extended, but its exact producer copy and recorded original hash remain independently verified; its original input record is not rewritten. A first full selected regression run retained twenty Windows cache-lifetime fixture failures: twelve concurrent-index and eight spool-eviction controls lacked native identity capability. Those two fixtures now supply actual stable IDs, preserving every assertion; all twenty pass in the final run. Their original failures, messages and selected inventory remain adverse.

This preliminary correction addresses deterministic same-metadata source replacement. It does not establish atomic path/open-handle binding, arbitrary concurrent in-place modification safety, physical topology/entry reuse, or final candidate qualification. I06/I106 remain open and I106/I110 physical-source validation remains on HOLD. No workstation UI, physical source or persistent setting changes occurred.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, payloads, commands, failures/skips and restoration.

| File | SHA256 |
|---|---|
| `archive-replacement-safety-20261011-v1/independent-final-v1.json` | `6a595e57142b859476760071a12297ada1919eeb87eec0b38e5d862502def3bd` |
| `archive-replacement-safety-20261011-v1/retained-tool-sources-v1.json` | `73aa804275ffb4d1e0ebe1e00303cfd6617bd0e752dbe10e9d011502f120452b` |
