# E-CI-I457 — exact pushed-source Unix job-journal FIFO qualification

Exact source `f30f0603f7835230fe34206578b5b3d74757179a` ran in [GitHub Actions 38500660565](https://github.com/benny-cz/FileCat/actions/runs/38500660565). Producer policy, Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26 succeeded; four tag/manual-only jobs skipped. Its 27 server-digested ZIPs have 2,762 members and fourteen TRX with **33,321 passes/1,187 explicit skips/zero failures**. This qualifies pushed source, not a release candidate.

Independent comparison with preceding green I456 run 38499165489 retains all **34,504** prior test name/outcome/message/skip rows. Exactly four records add: the `JobJournalInputTests` FIFO control passes on Mac and Ubuntu and explicitly skips on Windows x64 and ARM64. The exact-parent Ubuntu timeout and native lock/cleanup observations remain in [E-I457](E-I457-job-journal-fifo-open.md).

Private archive `V:\FileCat\artifacts\release-evidence\ci-i457-20261014-v1` retains API responses, server digests, ZIPs and member/TRX inventories. Independent rehash passes all **35** files: manifest SHA-256 `23199f7bf71ddac449cda0953bec76942ffce9eae578fa0e7d38d10ec0a5369f`, audit SHA-256 `9cdc0d48a1e1cd731440a28c153f1a8acec5a633dc6af3133db164a07d9dae3f`. The separate two-file `ci-i457-comparison-20261014-v1` archive rehashes under manifest SHA-256 `bb18014561e4e9fdd87727570f02650dc39df37afb3a47f3c2dfcc628735ac09`.

Later I458/I459 source, whole-process memory, installed UI, physical I106/I110 HOLD, candidate qualification and explicit human GO remain separate.
