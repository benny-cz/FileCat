# E-I341 — local copy staging cleanup ownership

2026-10-11 CEST. Baseline **e9f15bfaef2374c9c61578c595a00f82426cb9b8 /1558 canonical raw Git blobs**, declared test/product overlays. A failed local CopyExecutor deletes a staging pathname even when its copy engine never created it. Actual CreateNew collisions and controlled I/O, access-denied and cancellation failures reproduce deletion of complete unrelated staging bytes: **four unsafe failures/eight healthy passes** per producer.

The executor now leaves failed-copy cleanup to the engine that knows whether it created the destination. PortableFileOperations already removes only its successfully created destination; Windows CopyFile2 already owns partial-copy cancellation cleanup. Successful verification/publication cleanup is unchanged. Identical baseline/fixed safety assertions become **24 local and 48 native fixed passes** across portable and actual Windows CopyFile2 routes. Complete 4097-byte source/target data, complete unrelated collision bytes, original target preservation, exact names, zero failed-copy progress and removal of owned partial copies verify independently. The Windows tests explicitly select read-back verification to exercise the staged route.

The broad paired batch retains every **9778 predecessor outcome/message and 122 exact skips**, with **1472 native file checks**. Its first six expanded native matrices hit the 180-second controller limit after controls finished; every timeout remains adverse. A fresh 600-second diagnostic controller completes the same selection without changing test thresholds. All affected tests pass, but Ubuntu fixed and Mac baseline controllers refuse cleanup because synthetic fixture files remain. Their original exit-one results stay adverse; the cause is unproven. Fresh bounded maintenance archives every complete residual file, verifies all members before removal and independently confirms absent owned roots. Original wrong-project and unintended fast-route fixture failures remain separately retained.

[Final gated validation](E-I342-staging-fixture-ordering.md) qualifies the actual test sources intended for commit: **38 local/90 native controls**, all 4060 finite predecessor results/109 exact skips and 966 native file checks. Windows measures high RID 12288/session 0, Ubuntu UID 1000 and Mac UID 501 using pinned .NET 10.0.12. No host UI, physical source or persistent setting changes. [Exact committed/native follow-up](E-I341-I342-native-qualification.md) passes 38 local/90 native controls at 1b13697/no overlays. Hosted/installed-candidate and later pathname replacement, identity/topology and atomic races remain separate. Physical-source HOLD and human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, raw failures/skips, whole bytes and restoration.

| File | SHA256 |
|---|---|
| `i341-local-staging-ownership-20261011-v1/independent-remediation-v4.json` | `131a7f3931409f8cd743418959e3dfdae0eb834a772151d14c95dfed273bc5c6` |
| `i341-local-staging-ownership-20261011-v1/retained-tool-sources-v1.json` | `71e9e9eb5cba61b5789f3f391424983fa620cfa53fd2d81a043e52df06551fc7` |
