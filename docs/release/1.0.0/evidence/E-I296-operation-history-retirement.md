# E-I296 — operations history follows manager retirement

2026-10-10 CEST. Original producer **55a01ec8397540d4b94650fbc2918b159577fa9e**, with declared history fixture/two product overlays in complete 1375-blob exports. The final combined export also declares both I297 Network test overlays. Preliminary I06/V03/V12 scope.

The manager removes finished jobs when trimming history, clearing finished work or removing one job. The operations center did not learn those removals, so its rows, map, selection and job/source graphs could remain for the app lifetime. An earlier JobAdded observer can also remove a finished job before the center queues its add; that late add recreated the retired row.

JobManager now publishes actual history removals outside its lock. The center retires the corresponding row/map/obsolete selection on its dispatcher, updates the summary and refuses late adds for finished jobs already absent from manager history. The default history limit remains **50**, and its existing trim-on-submit behavior remains. No new budget or active-job removal is introduced.

Eighteen public-API controls use 1/128/4096 frozen entries and an owned 32 KiB patterned file with a complete known hash. They exercise KeepFinished=1, the existing default 50, direct manager Remove/ClearFinished, an earlier JobAdded observer and retained-history positives. The UI dispatcher is deliberately held until notifications queue. All queued witnesses remain live and no executor or visual context starts. **Fifteen original failures/three retained-history positives become eighteen passes**: all fifteen retired row/job/source graphs collect while their center stays held; all three retained histories and every queued witness remain live. An initial fifteen-case reproduction retains its twelve failures/three positives separately.

All **63 affected App controls**, including four native Network-place checks, **full Core 3470/64 exact skips** and **full App 1427/25 exact skips** pass. The full suites overlap. The earlier I296-only full App run remains failed (one Network-place assertion; [I297](E-I297-network-return-fixture.md)); its historical cause is not inferred. All **3534** preceding Core and **1432** preceding App outcome/message records and every exact skip remain; the only Core display-label adaptation is the independently matched PE test method with its export-local assembly path. App no-build payload bytes remain unchanged. Owned temporary files are archived, rechecked and removed where unlocked; any locks retain their actual receipts.

These are explicit managed reachability controls, not whole-process/native/frame memory, native input/incidence, throughput, human/reference or candidate qualification. Exact committed/original hosted follow-ups remain. I06 stays open; physical-source HOLD and human GO remain.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute. Nested receipts retain complete commands, raw outputs and source identities.

| File | SHA256 |
|---|---|
| `i06-history-row-retirement-20261010-v2/OperationHistoryRetirementTests.cs` | `8c474a9084b1b4a72789f57d2d95c75da5784eabc42368d39d1a49733f0e7fcc` |
| `i06-history-row-retirement-20261010-v2/JobManager.cs` | `47e054ba32160216de7018538edf9f9f7ad4cfa431a6d849e84aaf410e59ff04` |
| `i06-history-row-retirement-20261010-v2/OperationCenterViewModel.cs` | `647436353bf62c40dd5f80c6233028c1c9aadbf50cca8580d97d729aceaeabc4` |
| `E:/FileCat/artifacts/release-evidence/i06-history-row-retirement-20261010-v1/original/app-controls/command.json` | `d4c2e48b2a413ca8b6e87fe373a3d5121cda6ed607df39aa12aaa8a5cb5fbbaf` |
| `E:/FileCat/artifacts/release-evidence/i06-history-row-retirement-20261010-v2/original/app-controls/command.json` | `755b0d50535222877f71163c9580d17c4861810c7f937df10916f952ad8e6424` |
| `E:/FileCat/artifacts/release-evidence/i06-history-network-retirement-20261010-v1/fixed/app-controls/command.json` | `737da4d69c387045f2cdef41bf93749700f7376df270662dd77178ae26ec1cd2` |
| `E:/FileCat/artifacts/release-evidence/i06-history-network-retirement-20261010-v1/fixed/operations-app-controls/command.json` | `6ed74efa66a33a3ecf786e1064824dcee548fadf8d594117cbb4d6e45f380cb3` |
| `E:/FileCat/artifacts/release-evidence/i06-history-network-retirement-20261010-v1/fixed/full-core/command.json` | `40f2e148335ea09a3ebd60fc05a2f8a133cdd63f26f2687f32f69dbd90663836` |
| `E:/FileCat/artifacts/release-evidence/i06-history-network-retirement-20261010-v1/fixed/full-app/command.json` | `0b21dd0c7e71c45ed684d32f6e8f3ac47c2a60d713f9f7a5106ff50606b2d565` |
| `i06-history-network-retirement-20261010-v1/seal-history-network-v1.py` | `ee6768a42a798ae76999c22aff122e1421e2b223117a41d43a75144ccebff46d` |
| `i06-history-network-retirement-20261010-v1/independent-history-network-final-v1.json` | `ad6518ec26460fe433cbeef6536394370373f6930cf3a0bfa8c29bbe7d1b1dc6` |
| `E:/FileCat/artifacts/release-evidence/i06-history-network-retirement-20261010-v1/owned-temporary-files-v1.zip` | `c42777bc66d1418ec8c182fa510f4c93969a38e0fb3131af3aab76f515b91381` |
