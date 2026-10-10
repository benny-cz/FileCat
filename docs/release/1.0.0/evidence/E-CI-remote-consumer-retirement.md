# E-CI-I315/I316 — original committed remote ownership and readiness CI

2026-10-10 CEST. Original push **38063456193 attempt1** at exact **17035d54f56a56974a0d2d0b5bb9c70bd62f2f05** succeeds on all four required Windows x64/Windows ARM64/Ubuntu/macOS lanes. No rerun or workflow change. All **27 digest archives**, their extracted members, **14 TRX** and **31,434 results** independently verify: **30,438 passes, 996 exact skips, zero failures**. All **136 added controls** pass (32 remote ownership and two preview readiness tests per lane).

The previous ARM64 null-reader case now passes at this new identity. Every other **31,297 predecessor outcome/message** and all **996 exact skips** are retained. This does not establish the cause of the historical e0bdade failure or relabel it as passed. All **1458 raw canonical source blobs**, **92 locked restore graphs** and four clean **SDK10.0.401** build receipts verify.

This is hosted preliminary qualification alongside the [exact local/native record](E-I315-I316-native-qualification.md). Broader I06/native-input/reference/human/candidate scope remains. No frozen contract, candidate, physical-source resumption or publication is authorized.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact sources, artifacts, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i315-i316-ci-20261010-v1/independent-ci-final-v1.json` | `b620008e07f94b97a62b92710b43145680fd25f2c3a3506d3071ab43661f2b17` |
| `i315-i316-ci-20261010-v1/actual-available-observations-v1.json` | `e5e1b43bb33242e290ec2405e347fd9abab8614ab75552272cbb34b3fa23543b` |
| `i315-i316-ci-20261010-v1/watch-final-v1.json` | `ea170ef5914eb6fd64faf680795da70c9e7b4b304a6bd81e03ddad2141002fc9` |
| `i315-i316-public-20261010-v1/i315-i316-main-push-v1.json` | `ff729784c8ab1af5bc3c042e2dd4ee94cdd7a8a2688e4ef5a60ef8af821d9a69` |
