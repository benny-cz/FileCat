# E-CI-I324-I325 — original broker/Mac CI qualifies

2026-10-10 CEST. Original push **38076560415 attempt1**, exact **55deaa3a6a0c103b000c56a66874512623b928f9**. All four required Windows x64/Windows ARM64/Ubuntu/macOS lanes succeed. Independent collection and qualification verify **27 digest archives/fourteen TRX/31,690 records: 30,683 passes, 1007 explicit skips, zero failures**.

All **164 controls added since the previous green I319 run** pass: **128** four-lane topology/reselection/image/cancellation records and **36** two-lane Windows broker admission records. Every **31,526 previous green outcome/message**, including every **1007 exact predecessor skip**, remains; no new skip is introduced. All **1489 canonical raw source blobs**, **92 restore graphs** and **four clean SDK10.0.401 build receipts** verify. The actual native Mac mount oracle accepts unavailable backing only as unknown/refusal; full known-backing and owned 64 KiB controls remain independently qualified in [I325](E-I325-mac-backing-fixture.md).

Earlier I320/I321/I322/I323 CI runs retain their original failures and unavailable downstream inventories; this successful run does not reclassify them or prove their hidden historical causes. No CI rerun/workflow mutation or candidate/publication is performed. [Exact broker/Mac controls](E-I324-I325-native-qualification.md) remain a separate producer. Current I326 original CI and the [exact committed recovery controls](E-I326-native-qualification.md) have their own identities; broader release gates remain open.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested receipts preserve exact sources, commands, payload/runtime bytes, raw failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i324-i325-ci-20261010-v1/independent-ci-final-v1.json` | `ee36683945344518f007aa580be2cbb30d30978d5ff3a85f9a5e691d7af51380` |
| `i324-i325-ci-20261010-v1/actual-available-observations-v1.json` | `30a0128ff2463366ed1a3ae87ff4c51eeb1b18bbc84b4d11cfb4ded1dd01c1ba` |
