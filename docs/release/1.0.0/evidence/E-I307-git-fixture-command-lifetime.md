# E-I307 — bounded, asynchronous Git fixture setup

2026-10-10 CEST. Test-only overlay on exact **0aec7a7b4c68d3dd142e0d34c23d0e2fdd6f68f6** /1415 raw canonical blobs; no product changes. This batch also qualifies the subsequent [I305 asynchronous retirement checkpoint](E-I304-report-window-retirement.md#i305--subsequent-asynchronous-retirement-checkpoint). [Original hosted failure](E-CI-find-retirement.md) occurs during the local partial clone and retains its unproven cause.

The fixture synchronously waits ten seconds, kills the entire process tree and then reports only stderr. A deliberately successful eleven-second owned command reproduces rejection by that exact old wait/kill/assert path. It establishes the cutoff defect, without proving that the original hosted Git process reached it.

Setup now awaits process completion without blocking a test worker, permits sixty seconds separately from FileCat's unchanged eight-second badge deadline, and observes xUnit cancellation. Timeout/cancellation kills the owned tree, waits for exit and drains both output streams before returning. Every command logs its arguments, PID, budget, elapsed time, exit code, normal/deadline/cancel outcome and both streams. Nonzero exits remain failures.

All **20 GitLazy controls pass**, including four new real-process controls: success after eleven seconds, exit 7 with both exact output streams, actual timeout and cancellation with the OS-confirmed direct process gone. All affected Git controls pass; full App is **1569 passes /25 exact skips**, preserving every **1587 predecessor name/outcome/message** and adding seven passes (four Git process controls and three icon held-borrower controls). The actual local partial clone still lacks the promised tree and FileCat's no-fetch check still leaves every pack hash unchanged.

Initial original/fixed builds fail nullable analysis at two new test assertions; no tests run in those attempts. Their source, compiler logs and command exits remain immutable. The corrected fixtures use the asserted exception's nonnullable return. The first independent seal refuses a current/tested byte mismatch; a retained annotation proves CRLF-only normalization, preserves the original working bytes, and makes the checkout byte-identical to the tested copy before the corrected seal. Test results and executed inputs are unchanged. The old-cutoff reproduction, corrected controls, affected and full suites retain separate raw inventories. This is preliminary fixture remediation, not a product containment or candidate acceptance claim. Exact committed and original hosted follow-up are now sealed below; all earlier failures retain their original identities.

## Exact committed and hosted qualification

Exact **069f77225801d0e12f47c64a2b842d4ab9b7d87c** independently checks all **1421 raw canonical Git blobs** with no overlay and passes **150 affected Git/icon controls /one exact existing skip**. Every 151 validated name/outcome/message and exact skip remains. Four actual process observations preserve the eleven-second success, exit 7 and both streams, deadline and cancellation; three actual published borrowers remain usable until explicit release. Default headless pixel fidelity remains unqualified. The initial reused-temp-root driver refusal and the CI watcher's absent-directory refusal are retained; neither changed product or test evidence.

[Original hosted follow-up](E-CI-git-fixture-lifetime.md) passes four lanes with all 28 added fixture records and all 120 Find ownership records; the sole prior Windows x64 Git failure becomes Passed. Every other predecessor outcome/message/exact skip remains. The original failure cause remains unproven.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested records retain exact source, commands, payloads, original failures/skips, independent observations and restoration.

| File | SHA256 |
|---|---|
| `i307-git-fixture-20261010-v1/independent-final-v1.json` | `3b20f63ed7bd602150287b12ba35fbeb29e9ac04e7ff082ebf69d7a6e6df0bfe` |
| `E:/FileCat/artifacts/release-evidence/i307-git-fixture-20261010-v1/seal-v2.py` | `d1e1cdd6c25ff633c03c132206ed8268f441a1398f4f06829d1937990c968faa` |
| `E:/FileCat/artifacts/release-evidence/i307-git-fixture-20261010-v1/run-batch-v1.py` | `d405be4ebf1505dba5d41363f6b5339d667ecab8879c9c44551831d71148681e` |
| `E:/FileCat/artifacts/release-evidence/i307-git-fixture-20261010-v1/run-batch-v2.py` | `9b973825c151d9cb19843cbf00c2d0b0b33c3999b3d6c4716b34f5b7c3fc602e` |
| `E:/FileCat/artifacts/release-evidence/i307-git-fixture-20261010-v1/original-v2/controls/command.json` | `5dbf7b70184c7df425c4bdbc63be20f124db91788302a08781e4d0fc8accaed0` |
| `E:/FileCat/artifacts/release-evidence/i307-git-fixture-20261010-v1/fixed-v3/inputs.json` | `98a34e281c11cd17a4886e18fc83b98004a2f1327b2ab5970c8316d50ad46a98` |
| `E:/FileCat/artifacts/release-evidence/i307-git-fixture-20261010-v1/fixed-v3/controls/command.json` | `c0a04d2b1d2474fe807bbb36e0ce58056559706c2c9beaa0479dd9f3b0948b68` |
| `E:/FileCat/artifacts/release-evidence/i307-git-fixture-20261010-v1/fixed-v3/affected/command.json` | `2cc51776ff8ec0097e0d760c2db7d5dff1d687b37e88c943880db7a3249049cf` |
| `E:/FileCat/artifacts/release-evidence/i307-git-fixture-20261010-v1/fixed-v3/full-app/command.json` | `7aa8c71669537e6177ee06ae77ae7c6b6cedc123b7f99703e63b7484128c39a3` |
| `E:/FileCat/artifacts/release-evidence/i307-git-fixture-20261010-v1/fixed-v2/full-app/command.json` | `6e6b1b48f37cff1472dc052bdbf49bbff9fe84ea9ae3ec32afa508971d572383` |

## Committed follow-up receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact source, commands, raw results, failures/skips and restoration.

| File | SHA256 |
|---|---|
| `i307-git-fixture-20261010-v1/independent-committed-v1.json` | `40d92e6bd3eff58b03c05a3618581b13cf9c6882223f5482f40807664f7d29e5` |
| `E:/FileCat/artifacts/release-evidence/i307-git-fixture-20261010-v1/seal-committed-v1.py` | `b6cf4b7077795827ffba267bf0a4db36bf8416705751eebcc14053c71e90e627` |
| `E:/FileCat/artifacts/release-evidence/i307-git-fixture-20261010-v1/committed-v2/inputs.json` | `b0775d3fc3f0ee7996b6b6f82c2e1b8ba1840d20397adb242bd36730336c7f3e` |
| `E:/FileCat/artifacts/release-evidence/i307-git-fixture-20261010-v1/committed-v2/controls/command.json` | `e67cdf6cff7a022732dee411e0bdea740b44a6d0827f04d219ab805790258b18` |
| `i307-git-fixture-20261010-v1/followup-driver-refusals-v1.json` | `b6ae456febf5f014948c5e56fb9d97f984e8623cc38f1ad693cda5b7eccf58ea` |
