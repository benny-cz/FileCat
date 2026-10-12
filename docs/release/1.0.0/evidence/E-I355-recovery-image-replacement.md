# E-I355 — refresh recovery image sessions after source replacement

Baseline **2dda4a89ed4dbee9125f7913d6f73a6feb98437e /1610 raw Git blobs**, with one identical twenty-case control overlay. The final proposal adds two product overlays and one native-capability lifetime fixture overlay. A regular recovery image could be replaced by an independently created image with the same length and modification time while fresh listings and content continued to use the original retained source.

Image-session keys now include stable source identity, explicitly supplied by the application's platform operations. The portable default uses Linux/macOS device/inode identity. If identity is unavailable, each content/listing request reopens and rescans the image instead of reusing its source. Pending/completed free-space and disk-search modes are preserved across those fresh scans. Existing readers retain their original bytes through the existing image leases. Device keys and device-opening paths are unchanged.

The twenty paired cases cover unchanged/replaced images, content, catalogues, existing leases, free-space/disk searches, and known/unavailable identities. Across the host, Ubuntu, Mac and background Windows guest, **forty original failures and forty healthy controls become eighty passes without skips**. The independent reader reconstructs both complete **32768-byte FAT12 images and every 4096-byte member**, verifies actual distinct native source identities and the rename transition, unchanged source observations, preserved unchanged-image search modes, exclusive closure and per-case owned-root restoration.

**7034 selected affected records /177 exact skips** preserve all 6906 relevant committed Core predecessors and add 128 existing archive-replacement passes. Native skip reasons are compared exactly; each archive-case skip's differing per-run scratch-root stdout is separately checked for restoration and preserved raw. The selected **348-case headless App** inventory preserves 336 passes/twelve exact skips. Independent native postchecks rehash **1161 files**, confirm no owned workers/temps and closed Windows transport listeners. Three host scratch roots/fifteen files are archived, verified and restored.

The existing cache-sharing image-lifetime fixture now supplies actual stable file IDs, with every assertion unchanged. Preparation syntax/missing-script errors remain recorded. Independent-reader v1 used literal escaped bytes and stopped before output; v2 then compared run-specific restoration stdout as part of a skip reason and stopped before output. V3 corrects only the independent reader, separately verifies that stdout and compares the actual skip reasons exactly; no producer test is rerun or result reclassified.

This preliminary correction covers deterministic regular-image replacement. It does not establish atomic path/open-handle binding, arbitrary in-place mutation safety, complete freshness of advisory metadata when identity is unavailable, physical device entry reuse/topology or candidate qualification. Extra rescanning without identity is a deliberate correctness cost. I06/I106 remain open and I106/I110 physical-source validation stays on HOLD. No host UI, physical source or persistent settings changed.

Subsequent [I404](E-I404-held-image-unknown-identity.md) tightens the held-reader behavior when identity is unavailable: an opened image with a changed native token now refuses the read. Thus the original deterministic replacement cases remain historical evidence, while an ambiguous renamed old reader may return its original bytes **or fail closed**; it must never return replacement bytes.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, payloads, commands, failures/skips and restoration.

| File | SHA256 |
|---|---|
| `recovery-image-replacement-safety-20261011-v1/independent-final-v1.json` | `e3739d8c3b115ccfdd4bb43fd2b5ca63fe14da9b9ff81c41c372cef615abda35` |
| `recovery-image-replacement-safety-20261011-v1/retained-tool-sources-v1.json` | `27c998afa911db77a26e24fe2c2619b04a3ef744d980ad05dc1b4ec2455aa17e` |
