# E-CI-I321 — original reselection CI retains two failures

2026-10-10 CEST. Original push **38070958521 attempt1** at **1b2229cf90b2c51894f308c01902587668c89783** fails on Mac and Windows ARM64; Windows x64 and Ubuntu pass. No rerun or workflow mutation. **20 actually available digest archives**, **nine TRX** and **23,012 records** retain **22,347 passes, 663 explicit skips and two failures**. All **100 current added topology/reselection controls pass**: 72 I320 plus 28 I321. All **1477 canonical raw blobs**, **92 locked restore graphs** and four clean SDK10.0.401 receipts verify.

The existing Mac mount test again receives an unknown target classification. Windows ARM64's existing User_cancellation_still_ends_an_owned_child fails its four-second input fence. Those are exactly the two changed available predecessor outcomes; every other available predecessor outcome/message/skip remains. Five downstream inventories, covering **8614 records from the previous complete green run**, are unavailable, not passed or skipped. The initial collector expected at least 21 artifacts and stopped before download; corrected collector v2 preserves all twenty actually available artifacts. The adverse reader explicitly rejects qualification.

[Exact I321](E-I321-native-qualification.md) remains successful at its own identity. [I323](E-I323-smb-cancellation-startup.md) separately reproduces cancellation setup sensitivity with a controlled six-second startup and retains its corrected finite results; the historical ARM64 cause remains unproven. The subsequent 156b723 Mac job supplies partial digest-verified diagnostics: image disks are identified as Disk Image, then a non-mount path query fails before backing replies are recorded. It supplies no whole-run acceptance and does not establish the original classification cause. I323 corrects that collector path without changing the safety refusal or assertions. Broader native/hosted/candidate and Critical topology gates remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact sources, artifacts, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i321-ci-20261010-v1/independent-adverse-ci-final-v3.json` | `4686c463419515b2f0f600476c37a7d572240b16106b914dcc7c49baf1227379` |
| `i321-ci-20261010-v1/actual-available-observations-v2.json` | `2e3a117434beadf16632d4e4ab115f6fdfce994571a661f2be4d0c33dd9492ec` |
| `i321-ci-20261010-v1/watch-final-v1.json` | `8a39451a8218049b2e466566c0b4f08dae46d5a994fd53d706bb33cf83bb7095` |
| `i322-ci-20261010-v1/mac-artifact-diagnostic-v1/actual-partial-mac-v1.json` | `ed0470a56704547c7d7a726ce66ecabcfb0fd09217e7351772ea47f89b94b527` |
