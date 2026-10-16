# E-CI-I532 — exact pushed-source Unix worker identity matrix

[Run 38897334150](https://github.com/benny-cz/FileCat/actions/runs/38897334150) completed **success** at pushed source `7bef94da23ce8c4ce75827a7c28a9bfacc18011a`. Producer reference policy and macOS 26, Ubuntu 24.04, Windows ARM64 and Windows x64 jobs passed; four publication-only jobs skipped. This CI runs ordinary-user platform tests; the separate [I532 native record](E-I532-unix-effective-root-worker.md) exercises mixed and saved-root worker identities.

Private official archive `V:\FileCat\artifacts\release-evidence\ci-i532-root-drop-20261016-v1` retains run/job API responses and **27 server-digest-verified ZIPs, 2,779 independently hashed members and fourteen TRX**: **33,828 Passed, 1,302 NotExecuted, zero Failed**. The native Mac/Ubuntu eight-scenario instance-process artifacts each report **8/8 passed**. Audit SHA-256 is `9c2241080971d8bff4df6500da268c23b1cdd0ed18d128b285743bd40df1a06a`; file-manifest SHA-256 is `2a296018556dc4d91334c26c9e691664577c21c9119dc3f684698277f7d87519`.

The independent `ci-i532-root-drop-comparison-20261016-v1` archive rehashes every member of this run and immediately prior green `04b054c2` run 38893727259. All **35,130** duplicate-preserved test names, outcomes and failure messages remain identical; **zero rows are added or removed**. Comparison SHA-256 is `f2c15d53e91e97e4bf854bf7cedeafd6e985449886b48ee06e9da1fe2a6cd9e1`; comparison-manifest SHA-256 is `84335fb4ea9109940db1ed6b448d74608722e92429f692b8150271bef5434ccf`.

This matrix qualifies the pushed I532 source at its tested scope. It predates the separate I533 Linux file-capability fix and I534 worker-memory change, and does not establish installed UI or candidate acceptance.
