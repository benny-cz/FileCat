# E-CI-I459 — exact pushed-source desktop-icon configuration qualification

Exact source `56355754b5b7a3166686105f4a0baea99b404cd3` ran in [GitHub Actions 38503209241](https://github.com/benny-cz/FileCat/actions/runs/38503209241). Producer policy, Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26 succeeded; four tag/manual-only jobs skipped. Its 27 server-digested ZIPs contain 2,762 members and fourteen TRX with **33,337 passes/1,191 explicit skips/zero failures**. This qualifies pushed source, not a release candidate.

Independent comparison with preceding green I458 run 38502369521 preserves all **34,520** prior test name/outcome/message/skip rows. The only eight new records are two `FreedesktopIconsTests` controls on each of four platform lanes: **six passes** and **two explicit Windows FIFO skips**. The exact-parent oversized-allocation/FIFO timeout, corrected Mac/Ubuntu cases and retained transient native helper-fixture failure remain in [E-I459](E-I459-desktop-icon-input-bound.md).

Private archive `V:\FileCat\artifacts\release-evidence\ci-i459-20261014-v1` preserves exact API responses, server digests, ZIPs and member/TRX inventories. Independent rehash passes all **35** files: manifest SHA-256 `9e519eb17df49679076179c0abcaa541ffa6e6866072486350980546539619f8`, audit SHA-256 `4fcefceb029be405dda5bb97c5f14ca7a42c65354bbe62f2fc8c519ea62b0f86`. Separate two-file comparison archive `ci-i459-comparison-20261014-v1` rehashes under manifest SHA-256 `a8f407d8b300443184b284eadfb52207e1fec6269435c9372ab2a9ecdefc6efd`.

Whole-process memory, installed UI, physical I106/I110 HOLD, candidate qualification and explicit human GO remain separate.
