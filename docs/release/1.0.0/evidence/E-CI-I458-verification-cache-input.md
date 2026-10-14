# E-CI-I458 — exact pushed-source verification-cache input qualification

Exact source `98094631653e45e9751149130842f3a1fb3ef5dc` ran in [GitHub Actions 38502369521](https://github.com/benny-cz/FileCat/actions/runs/38502369521). Producer policy, Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26 succeeded; four tag/manual-only jobs skipped. Its 27 server-digested ZIPs contain 2,762 members and fourteen TRX with **33,331 passes/1,189 explicit skips/zero failures**. This is pushed-source qualification, not a release candidate.

Independent comparison with preceding green I457 run 38500660565 preserves all **34,508** earlier test name/outcome/message/skip rows. The only twelve new records are three `VerificationCacheRetentionTests` controls on each of four platform lanes: **ten passes** and **two explicit Windows FIFO skips**. The exact-parent 167.8 MB allocation/FIFO timeout and corrected native cases remain in [E-I458](E-I458-verification-cache-input-bound.md).

Private archive `V:\FileCat\artifacts\release-evidence\ci-i458-20261014-v1` preserves exact API responses, server digests, ZIPs and member/TRX inventories. Independent rehash passes all **35** files: manifest SHA-256 `00bb4c69838fd26f9b83b6ad67af67f6ffd763183fa2cec35669411352284841`, audit SHA-256 `fe8fd2d8eec533a68002359c9a10286d42378110ad195f543c09f031b3ab53f0`. Separate two-file comparison archive `ci-i458-comparison-20261014-v1` rehashes under manifest SHA-256 `c1a01fa310f6190f307065d6f015681a3fb5299675393ba45b7a9fcdb541ff6a`.

Later I459 source, whole-process memory, installed UI, physical I106/I110 HOLD, candidate qualification and explicit human GO remain separate.
