# E-CI-I318 — original committed Mac topology refresh CI

2026-10-10 CEST. Original push **38066949468 attempt1** at exact **55737cb2306d713c02e36979471be4a6f9bddfd2** succeeds on all four required Windows x64/Windows ARM64/Ubuntu/macOS lanes. No rerun or workflow change. Independently verified **27 digest archives**, extracted members, **14 TRX** and **31,502 results** contain **30,503 passes, 999 explicit skips and zero failures**.

The new actual Mac mount-replacement regression passes on the hosted Mac; Windows x64/ARM64 and Ubuntu record its three explicit native-only skips. Every **31,498 predecessor outcome/message** and all **996 predecessor skips** remain. All **1465 raw canonical blobs**, **92 locked restore graphs** and four clean **SDK10.0.401** build receipts verify. The [committed local/native evidence](E-I318-native-qualification.md) remains separately scoped.

This closes this preliminary hosted follow-up only. Wider I106 source/topology races, physical-source attribution and installed-candidate qualification remain. Earlier adverse CI records keep their original results.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact sources, artifacts, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i318-ci-20261010-v1/independent-ci-final-v1.json` | `db091861512c4fdb16d5df36dee66393816a986a32562c1a8c4ef79b6798d6ad` |
| `i318-ci-20261010-v1/actual-available-observations-v1.json` | `dde30b16248371a0fd8509cd19bef6e7cde0e31df9eda900bad3709a59db8a0e` |
| `i318-ci-20261010-v1/watch-final-v1.json` | `e187924474cb1c7cce4e15e34413d6f293873b2f7cda7d866899392b59960f1a` |
| `i318-public-20261010-v1/i318-main-push-v1.json` | `d1202ab4a0452e04fa15ac9ee2aaf038081f38f3f50448f2dde6859ef66aa6b7` |
