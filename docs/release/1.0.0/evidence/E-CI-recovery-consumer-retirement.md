# E-CI-I326 — original recovery-reader ownership CI qualifies

2026-10-10 CEST. Original push **38078628506 attempt1**, exact **24658e4cf99f6fcffced8f010b40cfb69b5f2c63**. All four required Windows x64/Windows ARM64/Ubuntu/macOS lanes succeed, including ARM64 package drawing and installer compilation. Independent collection verifies **27 digest archives/fourteen TRX/31,746 records: 30,739 passes, 1007 explicit skips, zero failures**.

All **56 added recovery-reader controls** pass across four lanes. Every **31,690 previous green outcome/message**, including all **1007 exact predecessor skips**, remains unchanged; no new skip is introduced. All **1493 canonical raw source blobs**, **92 restore graphs** and **four clean SDK10.0.401 build receipts** verify. Original job-progress observations show the ARM64 lane still executing its package-start/drawing step before completing; no failure or hang was inferred from that intermediate state.

The [exact I326 native qualification](E-I326-native-qualification.md) and [earlier broker/Mac green run](E-CI-broker-admission-mac-oracle.md) retain their own identities. Older failed CI runs remain failed with unavailable inventories preserved; this green run does not prove their hidden historical causes. Current [I327 committed/native qualification](E-I327-native-qualification.md) and its original CI run are separate. No workflow mutation, rerun, candidate, freeze or stable publication occurs; broader release gates remain open.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested receipts preserve raw sources, commands, complete actual outcomes/skips and native byte/restoration checks.

| File | SHA256 |
|---|---|
| `i326-ci-20261010-v1/independent-ci-final-v1.json` | `678f09a1e90b8034eb522a14b6b162bb3fc873dc4c7b15cd735112315d4242f4` |
| `i326-ci-20261010-v1/actual-available-observations-v1.json` | `fe980baacade28701511c3ef3a45ab7539324397d189cba352ceaff49434294b` |
