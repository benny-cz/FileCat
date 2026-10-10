# E-I319 — case-distinct Unix recovery sources share admission and cached scans

2026-10-10 CEST. **Potential Critical deleted-data safety; remediated preliminarily.** The recovery provider used case-insensitive dictionaries for source admission/name, volume status, selected size and cached scans on every OS. Distinct Unix device paths can therefore inherit admission that followed another source's disk-safety checks, or reuse that source's reader.

Unchanged **55737cb product plus the new test overlay** reproduces **four failures and two healthy same-source controls** in Ubuntu UID1000. Typed `/dev/mapper/FC_CASE_control` and `/dev/mapper/fc_case_control` references stand for distinct sources. Without an initial scan, the unchosen name invokes the recording reader under A's admitted label. After A is scanned, the unchosen name instead receives A's cached session. When both sources are chosen, both ordering controls open only A; one also reports B's label for A. These are actual production provider/admission/cache paths. The recording reader opens only an owned 32 KiB regular FAT12 image, whose complete bytes remain unchanged. No real mapper/device, physical source or distinct-image content mixup is demonstrated.

The correction uses the existing platform safety comparer for all four source maps, preserving Windows case-insensitive behavior and treating Unix paths ordinally. Image eviction/invalidation prefixes use the same comparison so a case-distinct source cannot retire another's session. This conservatively permits separate bounded cache entries for case aliases on case-insensitive Unix volumes; it never assumes they are different disks. Filesystem member-name matching remains separate.

All **six native Ubuntu controls pass** on the corrected artifact: unchosen names are refused before the reader/cached scan, chosen names open their own readers with their own labels, and repeat/rescan keeps the same-source reader. Independent postchecks verify all **86 staged and 386 reused runtime pins**, no owned payload process or fixture/temp root, and complete regular-image cleanup. No package, account, privilege, system setting, workstation UI or VM console changes.

All **98 affected Core/App recovery tests pass**, preserving the same **29 exact affected skips**. Windows runs **two healthy same-source controls** and records **four explicit Unix-only skips** separately; those skips supply no Unix acceptance. Every **133 predecessor outcome/message/skip** across these controlled Windows/affected comparisons remains. The first private baseline compilation fails on a missing test namespace (`CS0246`) before any test; the raw failure remains and the corrected fixture is built in fresh v2 roots. Source qualification verifies all **1465 canonical raw Git blobs** and declared overlays; the subsequent parent 68d78a3 changes documentation only, with identical pre-fix product bytes.

Six permanent regression controls accompany the correction. [Exact committed/local/Ubuntu follow-up](E-I319-native-qualification.md) passes at 7b73e2b. Original hosted, broader source identity/case-volume/race/lifetime and installed-candidate follow-up remain. I106 is open; I106/I110 physical-source HOLD, all owner decisions and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact sources, artifacts, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i319-recovery-source-case-20261010-v1/independent-remediation-v1.json` | `a13baace34ccacaa027cd46728a2aab3aa28ba5d3d84fc93548ac1bb5af55fa6` |
| `V:/FileCat/artifacts/release-evidence/i319-recovery-source-case-20261010-v1/baseline-v2/inputs.json` | `3511fc608d733508e58bf8a842fdc2b26de4d7254b349d4901877aeb9afbfefb` |
| `V:/FileCat/artifacts/release-evidence/i319-recovery-source-case-20261010-v1/fixed-v2/inputs.json` | `a5bf914171004f0aeaead08418569c3e7ae719b7fce4aa996cc647396813164d` |
| `i319-recovery-case-native-baseline-20261010-v1/linux/transport-final-v1.json` | `eb2a9055df3cc83260c633ac807c4bbec347d963d8bab22ab5c1213414dcacb6` |
| `i319-recovery-case-native-fixed-20261010-v1/linux/transport-final-v1.json` | `54a8fce08e513d921cb42382416784fbc0cc19a5ed70e716547682aa04484301` |
| `i319-recovery-case-native-baseline-20261010-v1/postcheck-linux-v2/independent-final-v1.json` | `dfade79bb15d92d89514ecb34497614161ac425f39ab9889ba67230d3d27dd8b` |
| `i319-recovery-case-native-fixed-20261010-v1/postcheck-linux-v2/independent-final-v1.json` | `df22bd3f12859d4b81c063c081febf5107f050b89c1c7ac3ba6cb0d9af9ab9c5` |
| `i319-public-20261010-v1/retained-tool-sources-v1.json` | `7b883480178b0fa1c79de65c630727900bc4f745059b296329a1205bd831163c` |
