# E-CI-I456 — exact pushed-source interrupted-job journal bounds

Exact source `83efd217a4723fdfe3cfc7b9c8dd33962fbade3d` ran in [GitHub Actions 38499165489](https://github.com/benny-cz/FileCat/actions/runs/38499165489). Producer policy, Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26 succeeded; four tag/manual-only jobs skipped. Its 27 server-digested ZIPs contain **2,762 members** and fourteen TRX with **33,319 passes/1,185 explicit skips/zero failures**. This is hosted source qualification, not a release candidate.

Independent comparison with preceding green I455 run 38497992834 retains all **34,492** previous test result/name/message/skip rows. Only the three new `JobJournalInputTests` controls appear, each passing once on all four platform lanes (twelve new passes). The exact-parent oversized-line allocations, malformed-journal retention control and corrected native cases remain separately recorded in [E-I456](E-I456-job-journal-line-bound.md).

Private archive `V:\FileCat\artifacts\release-evidence\ci-i456-20261014-v1` retains exact API responses, server digests, all ZIPs and member/TRX inventories. Independent rehash passes all **35** files: manifest SHA-256 `840a5a908efa06cb2659dcb71c924028c147fca009c53c3bee83758557769f65`, audit SHA-256 `faebb0f09ff3792031b7669332aa2d876dd3118bda9ea37677967d08bdb5ee7e`. The separate two-file comparison archive `ci-i456-comparison-20261014-v1` rehashes under manifest SHA-256 `f3353d6be731a996b20281accf2164d5aafd2181ccc5e41c758ae2e2687fd729`.

Later I457/I458 source, whole-process memory, installed UI, candidate qualification and the I106/I110 physical-source HOLD remain separate. Stable publication still requires explicit human GO.
