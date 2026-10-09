# E-I294 — release cancelled job blockers

2026-10-10 CEST. Correction base **f56f9de148ed65653f815a4f42194afcfcf94b8f**, plus the declared Job.cs/JobManager.cs/fixture overlays. I06/V03/V12 preliminary scope; complete source, raw failures, payloads and suite receipts are in the [combined job ownership record](E-I293-job-manager-retirement.md).

A queued waiter records the actual earlier conflicting job in WaitingFor. Cancelling the waiter made it terminal but left that blocker reference and its reason attached. A caller holding the cancelled waiter therefore retained a removed blocker and its frozen sources. Terminal state transitions now clear WaitingFor and WaitReason before publishing completion; live waiters retain their actual blocker/reason. This does not assert that a terminal status was visibly shown in the UI.

Nine real queued/cancelled cases span Remove/ClearFinished/history trimming and 1/128/4096 frozen source entries. Three live-waiter positives remain intentionally blocked. Original nine failures/three positives become twelve passes: all removed blocker/source-list graphs collect with the waiter and manager deliberately held; all live blocker/reason relationships remain. All known owned 32 KiB bytes and hashes stay unchanged; no executor starts. The combined 24 controls and both affected full suites pass as recorded in I293. Aggregate/native/frame/reference/human/candidate scope remains, and I06 remains open.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute. Nested receipts retain complete commands, raw outputs and source identities.

| File | SHA256 |
|---|---|
| `i06-job-retirement-20261010-v1/Job.cs` | `d23a26894a81253211aac3738fb2538a8bae3b248656c966090f05f4236f7966` |
| `i06-job-retirement-20261010-v1/independent-job-retirement-final-v1.json` | `5bcaa87f2585657fe35c031ff29d2aee52ce395132aed2e08a049ac23cb417f0` |
