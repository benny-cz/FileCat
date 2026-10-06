# E-I168 — duplicate comparison outlives Find demand

**Remediated preliminarily in 28d002f5a9efeeaa55b2baccb31789f2a98e2406.** Closing Find now cancels duplicate comparison as well as searching. Stop and Close also prevent further file-identity calls and discard a result once an already-active synchronous identity call returns.

## Proved defect and correction

The original Find Closed handler canceled only `_cts`; duplicate comparison uses a separate `_grouping` cancellation source. Independently, `DuplicateFinder` checked cancellation before an identity group but not between its individual file-identity calls. A held first call could therefore resume after Stop, continue over the remaining files and publish a group. Close discarded the result through `_closed` but still performed those remaining calls.

Three durable App controls run the actual Find duplicate-comparison task over three distinct owned ordinary files with identical bytes. The real checker hashes their content; a controlled identity dependency holds its first synchronous call. The test then closes Find, clicks its Stop button, or allows normal completion. Close/Stop returns while the held call is active; release lets the task drain. These are headless component controls, not native desktop input or a real blocked filesystem provider.

Baseline 9b45b13268a090eae9702ef6a2ae71bef54a3e7f's full identity is retained in the command receipt. Its two adverse controls fail: both make three calls after release; Stop also publishes one group. The normal completion control passes with three calls and one group. All cases record unchanged file hashes.

The fix calls existing `Stop()` on Find closure and checks cancellation immediately before and after every identity call. It leaves an already-running synchronous call to return normally, then schedules no further identity calls and publishes no canceled result. It does not claim to interrupt a held native call.

## Revalidation and actual provenance

Working and fresh committed-source runs each pass all thirteen affected App tests and seventeen core search/criteria tests, without skips. All three new controls pass: Close/Stop make only the initial call and produce zero groups; normal completion makes three calls and one group. Existing duplicate grouping, independent-file identity, aliases, search criteria and archive search controls remain in the affected suites.

The clean locked run uses Windows SDK 10.0.401 and a fresh export of 1,059 canonical Git blobs from 28d002f. The archive and every exported blob are checked before/after execution and against Git SHA-1/mode. The actual clean App-test `FileCat.dll` SHA-256 is 59253b5723b89e9085789510af97bab8d7efa541187d3dd8228b088eaae30959. The independent seal reconciles all raw result attributes, three structured control observations per App run, 33 retained files and 505 actual test-payload files across baseline/working/clean outputs. These are finite development payloads, not selected release artifacts.

The original [CI 37545177481 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37545177481) at 28d002f is pending at this local seal. It must be reconciled before claiming four-platform evidence. The full prior I167 CI remains sealed at a0a8ece.

The first baseline preflight compared canonical LF blobs against Windows CRLF checkout bytes and stopped before running tests. Its script/input snapshots are retained; the corrected check compares normalized source text while preserving actual raw input hashes. Existing unrelated compiler warnings remain. No physical source, persistent machine setting, contract/candidate or publication changed. Broader I06 lifetimes/materialized workloads, native frames, adverse provider behavior and final candidate remain open.

Private `FileCatReleaseEvidence/find-comparison-lifetime-20261007-v1`:

| Path | SHA-256 |
|---|---|
| baseline-v2/command.json | 5f2cf684175c5a983b500f46567fc591edabe8e6c0365ec4ad35b2d005dd857c |
| baseline-v2/results/baseline.trx | c68bcc37da982f64911e93733f8912a8ceccf5700bce5ce27158164a66ef4e35 |
| fixed-v3/FileCat.App.Tests-command.json | 529169df8fb7db93cfc5896428f6e3bc90d99c04f8420f46a2222a9a47e3d845 |
| fixed-v3/results/FileCat.App.Tests.trx | fa6a226feb1a36b9dde0547c67fbe5bbb871bf8a52ed12fe6ea114848a4137eb |
| fixed-v3/FileCat.Core.Tests-command.json | 727fc14e2df23c50efab2129ab05bc3f0fde3fa0991ea284d329925cd2cb26a7 |
| fixed-v3/results/FileCat.Core.Tests.trx | 3e8cd5fc13e4b4953b037471f7828195eafd4971cefebf4e58405e4b9ec0db52 |
| clean-v4/command.json | 09a8005a00307a2327967ccd7fe22d4eeb54ad6008bc9832ed597bc2145ec7e5 |
| clean-v4/results/FileCat.App.Tests.trx | b4c38bc6df789a965cebcb8c41528c8a591b0970c584834424dbdababf122026 |
| clean-v4/results/FileCat.Core.Tests.trx | cbd3a0f805ed4afb4a5e429b3fd353c72f6ec778ab45b6b5750ea9d9cff305f5 |
| independent-find-comparison-v5.json | ccd75f83ab0be848679e52d0b4fd8b6c6f3775fadb6e7cf0287cb7f63a8be138 |
