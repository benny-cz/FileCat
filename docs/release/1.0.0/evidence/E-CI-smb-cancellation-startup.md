# E-CI-I323 — original cancellation CI failure retained

2026-10-10 CEST. Original push **38074627662 attempt1**, source **c0677d9329d278294ad2fc31249772691f24d1b9**. Windows x64, Windows ARM64 and Ubuntu required lanes pass; the Mac lane fails the existing mount-classification fixture with `ArgumentNullException` /parameter collection. All **124 current added topology/reselection/image/cancellation records pass**. This includes the newly delayed cancellation startup control on all four lanes.

Independent collection verifies **24 digest archives/twelve TRX/27,482 actual records: 26,607 passes, 874 explicit skips, one failure**, **1484 canonical source blobs**, **92 restore graphs** and **four clean SDK10.0.401 receipts**. Every other available predecessor outcome/message and exact skip remains. Two downstream Mac inventories are unavailable (4168 previous green records); no current outcome is assigned to them. Qualification is refused after sealing these actual observations. No rerun, failure erasure or publication occurs.

The raw Mac query evidence and independent known/unknown native oracle correction are in [I325](E-I325-mac-backing-fixture.md). This actual run reports unavailable backing; earlier hidden-query CI causes remain unproven. [Exact I323](E-I323-native-qualification.md) and [committed I324/I325](E-I324-I325-native-qualification.md) are separate producers. This failed historical run remains failed after subsequent corrections.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain exact source, overlays, payload/runtime bytes, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i323-ci-20261010-v1/independent-adverse-ci-final-v1.json` | `210171bafe55a0dfbebdf4d3a3b7f2db1a4c32cb940193ec5df0b8cf29ca398f` |
| `i323-ci-20261010-v1/actual-available-observations-v1.json` | `b4175213ddcbdfd1795dc87db939ab5f81d75c7023eefae4664109ec31166401` |
