# E-I293 — retire removed job subscriptions

2026-10-10 CEST. Correction base **f56f9de148ed65653f815a4f42194afcfcf94b8f**, with explicitly declared fixture/fix overlays in complete 1366-blob exports. I06/V03/V12 preliminary scope.

Finished jobs removed from JobManager history remained subscribed to its Changed handler. Holding a removed job therefore kept the manager and its other current request graphs reachable. Remove, ClearFinished and automatic history trimming now detach that handler when retiring the job. Active jobs and existing notifications keep their behavior; no queue capacity or memory limit changes.

Twelve actual public-API controls use owned 32 KiB patterned bytes and frozen request lists of 1/128/4096 entries. Nine retired cases exercise each removal route; three live-manager positives keep current requests intentionally reachable. The manager is configured with zero concurrent executors, and actual queued cancellation completes through its scheduler. Non-inlined setup and bounded collection observe ownership while the removed job remains deliberately held. There are no fake executors, private-state setters or source-device operations. Repeated ItemRefs are real frozen inputs; their copy execution is not claimed.

Nine original failures/three live positives become twelve passes. Every retired manager/current job/source-list graph collects after correction; all live positives remain reachable. Complete known bytes and hashes stay unchanged. [I294](E-I294-cancelled-waiter-retirement.md) covers the distinct cancelled-waiter edge in the same coherent batch.

The combined batch changes **18 original failures/six live positives to 24 passes**. All 156 affected App results pass with three exact existing skips. Unchanged compiled payloads pass overlapping full **Core 3470/64 exact skips** and **App 1398/25 exact skips**. Every 3510 preceding Core and 1423 preceding App outcome/message/exact skip remains, with one independently verified PE assembly display-path adaptation. The first restricted restore attempt exited without TRX/test results and remains separately preserved; the authorized retry provides the actual original failures.

This qualifies finite managed reachability, not aggregate/process/native/frame/throughput, ordinary UI incidence or a candidate. Exact committed controls pass below; original hosted follow-up remains. I06 stays open; physical-source HOLD and human GO remain. Owned temporary files are archived/rechecked before attempted removal; retained compiler locks remain explicitly recorded.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute. Nested receipts retain complete commands, raw outputs and source identities.

| File | SHA256 |
|---|---|
| `i06-job-retirement-20261010-v1/JobRetirementOwnershipTests.cs` | `750b8eb3eb32578a4d0d55a6f66c08dabc2f3eb248df320c75da25ac2a4006ee` |
| `i06-job-retirement-20261010-v1/JobManager.cs` | `752769b254234131b45c2a271c09f43a4c4cdba7bf024a4618eb27c1eca9a215` |
| `E:/FileCat/artifacts/release-evidence/i06-job-retirement-20261010-v1/original-v2/core-controls/command.json` | `56c51fbc6780df220efa884650159ee3574ece71291b96a02c89f83522d33fb1` |
| `E:/FileCat/artifacts/release-evidence/i06-job-retirement-20261010-v1/fixed/core-controls/command.json` | `3b3821d94d7a5494421ad22dd7747cf3f356f49d617b9170f8dab9f8a2c4892b` |
| `E:/FileCat/artifacts/release-evidence/i06-job-retirement-20261010-v1/fixed/jobs-app-controls/command.json` | `ccb0af9f00c91d655cf128da4aa7b8d02539a33a2ca877dd45151915438fae3d` |
| `E:/FileCat/artifacts/release-evidence/i06-job-retirement-20261010-v1/fixed/full-core/command.json` | `135eda9235fe76e02dd7255f4ba29e9c358cc1e33df49abde963656187a94807` |
| `E:/FileCat/artifacts/release-evidence/i06-job-retirement-20261010-v1/fixed/full-app/command.json` | `25e1dff67e437508ec4e4ba5de9b0ec8c0fc0bd6e551158a706c857949ad452e` |
| `i06-job-retirement-20261010-v1/seal-job-retirement-v1.py` | `f16099fa7265123c5d7628cfa57cc1df679d839d96c38fd4d3ab58d0b9bc9826` |
| `i06-job-retirement-20261010-v1/independent-job-retirement-final-v1.json` | `5bcaa87f2585657fe35c031ff29d2aee52ce395132aed2e08a049ac23cb417f0` |
| `E:/FileCat/artifacts/release-evidence/i06-job-retirement-20261010-v1/owned-temporary-files-v1.zip` | `0690d0396a9ac0cb57b5a7ea95271e1d419c09e058f2979e99f2cd3d786474e0` |
| `E:/FileCat/artifacts/release-evidence/i06-job-retirement-20261010-v1/original/core-controls/command.json` | `168bd0f1e5e93f28ae0821053f04b2fbf65f92b16efb4b53128457127db7fa3b` |


## Exact committed follow-up

No-overlay **20f1bdc5ce168c414fb1c9457c0f76b61c9e2059** verifies all **1371 canonical Git blobs** and passes all **24 controls**. Every private/exact outcome/message/manager/waiter/live relationship agrees apart from owned roots. All three validated overlays match the canonical committed bytes with only Git line-ending normalization permitted. One owned temporary file is archived/rechecked and removed; no compiler lock remains in this exact scope. This is not a full-suite, process-memory/native/frame/human/candidate repeat.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute. Nested receipts retain complete commands, raw outputs and source identities.

| File | SHA256 |
|---|---|
| `i293-i294-committed-20261010-v1/seal-exact-v1.py` | `92589b9602501fec77983c9f243c1a95869537f5b50cda645f9f6f64fdd629fb` |
| `i293-i294-committed-20261010-v1/independent-exact-final-v1.json` | `8ac971f68cc07d0a6dc85b30b4ebb495d64d9c666158766d017d328871b91df1` |
| `E:/FileCat/artifacts/release-evidence/i293-i294-committed-20261010-v1/committed/core-controls/command.json` | `411905e616ca6fb4193540be194fab78e6f855f328cd0293008ea377fade7207` |
| `E:/FileCat/artifacts/release-evidence/i293-i294-committed-20261010-v1/committed/inputs.json` | `bca7f4bd5dc2b71e73eb70bee80137cbcc25f85fa00fae077e3c062d8605d67c` |
| `E:/FileCat/artifacts/release-evidence/i293-i294-committed-20261010-v1/committed/owned-temporary-files-v1.zip` | `85bca5a2d8947e002bef1f1fc3303822d092b34badde232bc90da2ff1e6f067d` |


Original hosted follow-up: [38000303954 attempt 1](E-CI-job-retirement.md) passes all four lanes/30,268 results, including 96 new job controls. Every predecessor outcome/message/exact skip remains. Broader aggregate/native/frame/reference/human/candidate scope remains.
