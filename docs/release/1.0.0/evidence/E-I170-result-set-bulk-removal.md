# E-I170 — bulk result removal repeatedly scans and shifts the member list

**Remediated preliminarily in 22c263d4a9ef7b889f95d883893d2c40bf7f2736.** Bulk removal now compacts the result-member list once after removing selected metadata. It preserves remaining order, notes, relative folders, duplicate/missing-selection counts and existing event behavior.

## Proved latency and correction

The original `ResultSet.Remove` calls `List.Remove` for every selected member. Removing many references therefore repeatedly searches and shifts the same materialized list. `MainViewModel.RemoveFromResultSet` calls this synchronously before refreshing the tab; this source path is verified, without claiming a native desktop input/frame measurement.

The owned probe creates synthetic identities and notes, selects every other member in reverse order, removes them and independently checks all remaining identities/order/folders/notes and one completed-operation notification. It performs no filesystem operations on those references or devices. Each of six sizes (5,000 through 200,000) runs in three fresh processes for the original, working and clean committed Core images: all 54 observations and full stdout are retained. Timings cover the actual removal call, with correctness oracles outside the timed region.

The larger original measurements show the repeated work: doubling from 50,000 to 100,000 and then 200,000 members increases the median from 2.1 to 8.7 and 36 seconds. These are measured development workloads on this shared Windows host, not an exclusive reference machine, a native UI freeze reproduction or frozen V16 acceptance.

The fix removes entries from the relative-folder/note dictionaries as the selection enumerates, then compacts the member list once. A `finally` block also compacts successful partial removals if the selection enumerator fails. It preserves existing partial-removal consistency and does not introduce a temporary collection proportional to the selection.

| Members / selected | Original median | Working correction median | Clean committed median |
|---|---|---|---|
| 50,000 / 25,000 | 2,112.386 ms | 26.858 ms | 13.535 ms |
| 100,000 / 50,000 | 8,658.681 ms | 51.169 ms | 25.389 ms |
| 200,000 / 100,000 | 35,973.803 ms | 101.571 ms | 53.919 ms |

Both correction measurements are reported: working and fresh committed runs were sequential on a shared host and their times vary. No universal speedup, native frame/input target or aggregate-memory budget is inferred from these timings.

## Correctness and exact provenance

Four durable Core controls cover zero, single and 12,000-member sets with unordered/repeated/missing selections, and an enumerator that fails after two removals. They pass on the baseline as correctness controls; the performance defect is established by the separate retained measurements. They do not use a fragile CI wall-clock deadline.

Working and fresh committed runs each pass all 29 affected Core cases without skips: these four controls, the four I169 note-lifetime controls, seventeen search/criteria cases and four working-set cases. Note-reference release remains covered. The independent seal reconciles 158 retained files, 246 actual test-payload files, 24 actual probe-payload files and 82 original input copies; all raw attributes and measurements reconcile.

The clean locked SDK 10.0.401 run exports and verifies all 1,063 canonical Git blobs from 22c263d before/after execution, including archive bytes, Git SHA-1 and modes. Actual clean `FileCat.Core.dll` SHA-256: c7259115da887725f0baaddce2bcee90a730e7450c08bc1f085f0f90ec51cb14. The probe binary/config/dependencies remain byte-identical across measured payloads except the exact corrected Core image and its symbols. Windows .NET runtime 10.0.12 is recorded; these are finite development payloads, not selected release artifacts.

Original [CI 37548599663 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37548599663) at 22c263d is pending at the local seal. Its hosted correctness cases will be recorded after original inventories/artifacts are retrieved and verified; hosted performance acceptance is not claimed.

The first payload-copy preflight stopped before probe compilation on an assumption that all 82 inputs were flat. The corrected copy preserves subdirectories and verifies existing bytes; its first successful compiled probe is unchanged throughout the measurements. The first SSH push timed out; the bounded retry succeeded. Existing unrelated compiler warnings remain. No physical source, persistent machine setting, frozen contract, candidate or stable publication changed. Broader I06 materialized memory/consumer lifetimes, V13/native interaction and V16 reference acceptance remain open.

Private `FileCatReleaseEvidence/result-set-bulk-remove-20261007-v1`:

| Path | SHA-256 |
|---|---|
| probe-build-receipt.json | 7510128bea42c0bfaad8e574e561945e83c798e02e90eb3ecc1ac9ebac638057 |
| baseline-measurements-v1.json | 9fdcc9465f32e6bcaa9a1a01ae15c1a8d608471ad96abe20b7d379922bbc493a |
| baseline-wide-measurements-v3.json | d9cf09873774f08e77da4a554bc29386d62f6b3f4fb80f083fef10123e241276 |
| baseline-v1/command.json | 8e4bda45526b588f7ceb70bc71290506e58dd397086d5ba026bc68eab9c916dc |
| baseline-v1/results/baseline.trx | ac19c2a148c3e98c2d68feba19f93e3c1cf9fba143ad84cdddf2338a8db2fc24 |
| fixed-overlay-v5/command.json | d3e382fa20531b296ba771865c20853677e37817c2c61c17966484e92511b1b3 |
| fixed-overlay-v5/results/fixed.trx | 80e807ab113d809ca07035db603a9ef19e219e5a1920456cd8dac85e5221737d |
| fixed-measurements-v6.json | 3cbf7c14b55a69112a7dcb1396f4ff07153ad62c61127b75656d3083676602a8 |
| clean-committed-v8/command.json | e1fdcec83830defbbe6a657642f8627af71464037541e719042be81a829deacc |
| clean-committed-v8/results/clean.trx | b24cde1b075ab1996f4670751d47c4e12b388d727fe0cd8e0f7548243d214e76 |
| clean-measurements-v9.json | c53f2ef24a010cffdaf277d1e2f36499f4e9e913249cf3526a0a61384f22d10a |
| independent-bulk-removal-v10.json | abac467b8d33bd721da116e9eb744cbbc5cf8f4f7358270791aa78282f369929 |
