# E-I267 — ordinary Git badge admission fixture

Recorded 2026-10-09. **Preliminary I06 validation reliability; exact test producer `a2f6cd9109a6b3dfddad7e92e74c5875147c78f9`.** One test file changes; production, workflows, guards, budgets, assertions and skip rules remain unchanged from `c0cfb7efbcf9664ef56c258257b391ddfb5ba35a`.

Original CI **37916424327 attempt 1** at c0cfb7e has one Windows x64 failure: the ordinary absolute alternate-object-store test expects an admitted repository but receives null. Its original fifteen Git cases give fourteen passes/one failure. All 22 archive and 23 Markdown cases pass. The actual Windows native GnuPG case is NotExecuted with “No gpg here.”; hosted success is not described as executing that native body. The full original TRX, server-digest archive and metadata survive. A first reader incorrectly required the GnuPG case to pass and refuses after extracting the artifact; a fresh reader interprets those same captured bytes without replaying retrieval or tests.

Production optional badge admission may return null when any guard or finite budget refuses the directory. The original null result does not expose which guard refused it, and elapsed time does not retrospectively establish the cause. The positive fixture now permits at most three calls with a 50 ms pause between null results. It returns the first non-null value to the unchanged final assertion, so an incorrect non-null repository still fails. Every adverse/refusal test continues to make its original call without retries. Production resource limits and refusal semantics are unchanged. Five positive observation groups record every actual attempt, elapsed time, admitted root, finite maximum and these scope qualifications.

The private correction passes all fifteen focused Git cases with identical logical identities; all five actual positive observations verify and their owned roots are absent. The exact pushed producer then passes the complete App suite: **1217 passed/25 skipped**. All 1242 preceding names/outcomes and all 25 exact skip messages remain. All 1289 source Git blobs/ZIP members and the actual 141 compiled payload members recheck. The earlier complete Core 2724/64 retains c0cfb7e; earlier full Remote/Platform retain dc1a86e. This is not a claim of four full local suites at the current producer.

Current original CI **37918903995 attempt 1** at a2f6cd9 succeeds in all four required lanes. Its **25312** records retain all 25220 preceding identities/outcomes/exact skips and add 92 passing Markdown executions. The independent reader validates twenty positive Git observation groups across four lanes, alongside 88 archive, 92 Markdown, 56 staging, 16 reveal and four earlier Platform fixture observations. All 21 original server-digest artifacts, 14 TRX inventories, four toolchain receipts and 92 locked graphs retain their actual source and native command provenance. No failed original run is rerun or relabelled.

Owned restoration archives/rechecks and removes twelve regular temporary files and both exact private/full-run build roots, with zero remaining locks or link objects. No process is killed, global configuration changed, physical source opened, account touched or Mac/VM action performed. This fixture remediation does not close wider I06, native GUI or candidate qualification, physical HOLD, any of the 24 final-candidate campaigns or explicit human GO.

| Retained input | SHA-256 |
|---|---|
| Original server-digest Windows failure, 15 cases and actual native skip — `independent-original-git-ci-failure-v2.json` | `011b53b478cad66ed533ddcd92d14c89a7ddd1b864910d4c37ae2ffa050a7b22` |
| Private focused command and actual compiled payload — `command.json` | `d2d7c6e44e695f074c851dd661593df268b4427bb7c5bccbb51f5780f26c371e` |
| Independent private 15-case and five-raw-observation review — `independent-private-git-fixture-review-v1.json` | `e3c215167a3500a1a63d465a45b5b605818a25d725d7cc7cb3dd3df460c0ea8c` |
| Exact approved fixture input — `GitAlternateTests.cs` | `1e4becfa97f95d0004e73c6a8f4491c13c088036a8eec4ff157a5a6bceca9062` |
| Exact main commit and push — `git-fixture-main-push-v1.json` | `01b8554b2ff0f019e06c20d1b2a12d8b2ace24efea3de76adef63058d657f4cc` |
| Complete App command and actual 141-member payload — `command.json` | `6cce633ca2710336403ab0217e5504143a49e7614e6807092123b81a6d7a81ab` |
| Closed complete App controller — `command.json` | `885477f12c5e5621a5286d2bdcf620dbbdeccccc422515b28124c9f18c3726db` |
| Complete source and 1289 immutable Git blob copies — `source-inputs-before-v1.json` | `3165efc8598311438aeb2e33595a242b9f2b6f6468f7d35cdb6614011188635e` |
| Independent exact full App comparison and owned restoration — `independent-committed-git-fixture-final-v1.json` | `836cde4f3c05b0318e9fd2f69dc1ee659a440c3b10d7f027a7c9334d2ffde96b` |
| Owned temporary archive and two absent roots — `owned-git-fixture-restoration-v1.json` | `ce596610609b833df57d88b820144b9e9887d2cf74ccbe633c91e552711a38ff` |
| Current four-lane original CI and 20 positive admission observations — `independent-markdown-ci-final-v1.json` | `120bed3b5aaa44aae89202e6dffd4d1ac7f5730a265680eb213718c78673c821` |
| Current original CI collector — `collector-v1-command.json` | `4817b74ef91e231830e0d9e6e37457f7e180b264b8aeccee11feae763319ce95` |
