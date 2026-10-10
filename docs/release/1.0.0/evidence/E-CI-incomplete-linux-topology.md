# E-CI-I314 — original topology CI and retained ARM64 failure

2026-10-10 CEST. Original push **38060584377 attempt 1** at exact **e0bdadec256369b11089b500108689e9a980d85e** finishes **failed**. Windows x64, Ubuntu and macOS lanes succeed; Windows ARM64 fails in App tests. No rerun or workflow/skip mutation occurs.

All **25 available artifact archives** match retained GitHub byte counts and SHA256 digests, and every extracted member is independently rehashed. Fourteen raw TRX inventories contain **31,298 records: 30,301 passes, 996 explicit skips, one failure**. All **32 new topology records pass**. The failed case is `PanelRetirementLifetimeTests.Removing_a_quick_view_panel_retires_its_source_subscription_and_reader(hiddenFirst: True, binary: True, repetitions: 12)`: the expected `PagedReader` is null at line 51. Its actual message and stack remain immutable.

Every other **31,265 predecessor outcome/message** and all **996 exact skips** remain. All **92 locked restore graphs**, four clean **SDK 10.0.401** build receipts and **1452 canonical raw blobs** verify. ARM64 Inno Setup provenance and package screenshot archives are unavailable because later steps did not execute; they are never counted as passing.

[I316](E-I316-panel-preview-readiness.md) independently reproduces and corrects a same-key preview readiness defect in the test fixture. That controlled result does not prove the original hosted cause. Original failed CI remains failed; corrected committed/hosted follow-up requires its own identity. No candidate, hardware, native UI or publication acceptance is supplied.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact source, commands, original failures, skips and native restoration.

| File | SHA256 |
|---|---|
| `i314-ci-20261010-v1/independent-adverse-ci-final-v1.json` | `dee8cd4f4dabae02b0fa23c11a993581d3f8b06ce39816302b910e1173b4ce33` |
| `i314-ci-20261010-v1/actual-available-observations-v1.json` | `efeb29334d2cb0ae68a9ee18109ae5ebd98890a5b141a8770ec508008a5e8f19` |
| `i314-ci-20261010-v1/watch-final-v1.json` | `a7a8c1d9611be14b8fb397f84c5ae1a8a12d7fa534bcf6c3d9153e361da2a79f` |
| `V:/FileCat/artifacts/release-evidence/i314-ci-20261010-v1/collect-v1.py` | `45260dbea72b811c4ab71975058643f99cfaa37df66a1b0569fa47a2427cab88` |
