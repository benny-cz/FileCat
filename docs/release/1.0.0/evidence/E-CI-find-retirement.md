# E-CI-FIND — original committed Find ownership CI

2026-10-10 CEST. Source **0aec7a7b4c68d3dd142e0d34c23d0e2fdd6f68f6**; original [run 38032069975, attempt 1](https://github.com/benny-cz/FileCat/actions/runs/38032069975) remains **failed**. Windows ARM64, Ubuntu and macOS ARM64 pass; Windows x64 fails while constructing the Git partial-clone fixture, before FileCat's no-fetch assertion. No rerun, tag, freeze, candidate or publisher is invoked.

Independent collection checks **26 available official digest archives /2736 members**, original logs and **14 actual TRX inventories /30,972 records: 29,979 passes, 992 explicit skips, one failure**. All **120 new Find ownership records pass**: thirty in every App lane, with no new skips. Every other **30,851 predecessor outcome/message and exact skip** remains. The sole changed predecessor is `GitLazyFetchTests.Automatic_badges_do_not_fetch_missing_partial_clone_objects`: Passed becomes Failed in Windows x64. Windows x64's downstream Inno Setup provenance is unavailable; it is not invented from the successful ARM lane.

The raw error contains only the clone progress line and fixture assertion at line 117. The ten-second fixture kill path and a genuine Git error were indistinguishable. A setup deadline is plausible, but the original cause is **unproven**. [I307](E-I307-git-fixture-command-lifetime.md) qualifies a separate controlled old-cutoff reproduction and correction; it does not relabel this failed run.

All **92 restore graphs** match canonical lock versions/content hashes. Four clean build receipts identify exact source and **SDK 10.0.401**. All **1415 raw canonical Git blobs** are independently rechecked. The first failure collector completed downloads, then refused an incorrect expected TRX relative path; its immutable reader/refusal and actual official path are retained. The corrected reader reuses and rehashes the same raw downloads without rerunning tests. Hosted results do not replace native interaction, reference hardware, required people or exact-candidate acceptance.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested records retain exact source, commands, payloads, original failures/skips, independent observations and restoration.

| File | SHA256 |
|---|---|
| `i306-ci-20261010-v1/independent-ci-failed-final-v1.json` | `9aecd8f4916d94805b20477681eaf5cfe06e8405e044d3a2654038dbe3c88eb1` |
| `i306-ci-20261010-v1/independent-find-ci-failed-final-v2.json` | `42fe51bb8af11f0e065e270efd4913f3e0c8aada6aa2db44bfc7769c26be30b8` |
| `i306-ci-20261010-v1/watch-final-v1.json` | `f7d419bc089baf021bacfb41be0ae89cd334638dd86a895a19602a0489112b98` |
| `E:/FileCat/artifacts/release-evidence/i306-ci-20261010-v1/collect-failed-v1.py` | `62f6c7483381def84353e220442fb63e0ee034024557ab9669e6ad8ecc0c882a` |
| `E:/FileCat/artifacts/release-evidence/i306-ci-20261010-v1/collect-failed-v2.py` | `0f860c557dbfa281326a594540e78f1ede3581c3a6e89db0e7adfe8454ce7f6f` |
| `E:/FileCat/artifacts/release-evidence/i306-ci-20261010-v1/seal-failed-v1.py` | `a51fa3715edfa37d14389d8b7b59e05d53a02e155eb9117debaa66fc44030297` |
| `i306-ci-20261010-v1/failed-reader-v1-refusal.json` | `3bba4b4ae31eb769c56dfcfc03ff4e131b8f980a6a391e82820dda7e69e23d0b` |
