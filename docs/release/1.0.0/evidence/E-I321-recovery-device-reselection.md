# E-I321 — explicit device reselection keeps the previous reader and scan

2026-10-10 CEST. **Potential Critical deleted-data safety; remediated preliminarily.** After a device was scanned, ForDevice updated the selected name/size but left its cached reader and scan active. A fresh explicit choice after the application's safety checks therefore reused the old source even when the newly chosen device had another size or label. Its chosen-size check was bypassed by the cache hit. A current path's topology cannot by itself establish the retained reader's backing after replacement.

Unchanged **399a829 product plus seven test controls** reproduces **five failures and two healthy repeat/rescan controls** on Windows, Ubuntu UID1000 and macOS UID501. The recording reader opens only two owned regular FAT12 images. Four reselection cases cover equal/different size and equal/different display name; each receives the old A label and one retained reader, with B never opened. A fifth case with an intentionally mismatched newly chosen size also receives the old scan instead of refusal. All complete A/B image bytes remain unchanged. This demonstrates stale production selection/cache behavior, not an actual replaced device, physical write or data loss.

Fresh explicit ForDevice choices now retire and close the previous cached reader before publishing the new name/size, under the existing scan lock. The next listing opens the newly chosen source and applies its size check. Ordinary listing repeat and Forget/rescan retain the existing approved reader and do not request a second open. No filesystem member-name semantics or general cache capacity changes.

All **seven corrected controls pass on each platform**, with new B labels/readers, closure of A and refusal/closure for the intentionally wrong chosen size. All **118 affected tests pass** with the same **34 explicit skips**; every **152 predecessor outcome/message/skip** remains. All **1473 canonical raw blobs**, declared source/test overlays and unchanged other source files verify. Independent postchecks verify **172 staged and 920 reused runtime-file checks** across both Ubuntu/Mac producers, no owned payload process, fixture or temporary root. Seven fixture restorations per platform/producer verify complete unchanged bytes and all reader closures.

No actual device or source is opened; no package, account, privilege, system setting, workstation UI or VM-console changes. Exact committed/original hosted, broader active-reader/selection races, source identity/topology/lifetime and physical/candidate scopes remain. I106 remains open; I106/I110 physical-source HOLD, all owner decisions and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact sources, artifacts, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i321-recovery-reselection-20261010-v1/independent-remediation-v1.json` | `fa5c768903aac37614da9175b0b68b2a0edfec38fd438305193c1cb2573b2f3b` |
| `V:/FileCat/artifacts/release-evidence/i321-recovery-reselection-20261010-v1/baseline-v1/inputs.json` | `734299ed0b4dcda7bb326b239a945579dd68425d2ff13404c12cb32651c825ca` |
| `V:/FileCat/artifacts/release-evidence/i321-recovery-reselection-20261010-v1/fixed-v1/inputs.json` | `60015888ae25b7b72013dc1239858df272a2b95f11185491524c33a988e3b722` |
| `i321-reselection-native-baseline-20261010-v1/linux/transport-final-v1.json` | `a8f9fd85f1d97c1f55bfdf3622700e678fe485f03ee6ad096adacd5901d5b67c` |
| `i321-reselection-native-fixed-20261010-v1/linux/transport-final-v1.json` | `864d835ef34cee4108ff0acb133a6c2ab54bdbdae48503b1d3b17b8a1c624f5c` |
| `i321-recovery-reselection-20261010-v1/baseline-macos-v1/transport-final-v1.json` | `bd47aeb778a79472c51f25e86a732e11471e44615f2feb4f1f59a0bffd461994` |
| `i321-recovery-reselection-20261010-v1/fixed-macos-v1/transport-final-v1.json` | `25c6edd8d5a033d30d1dd7adca738913883fa2fbaa94d8bc73689d02854b85f7` |
| `i321-public-20261010-v1/retained-tool-sources-v1.json` | `a3a6b327ad67befae94c2b3fd2ebac884a627506c973b2be35374a3c27124c8c` |
