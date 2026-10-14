# E-CI-I461 — exact pushed-source Windows origin-reader qualification

Exact source `a74d25e37684f6f90b8fb98855fde432a2724dd0` ran in [GitHub Actions 38504313692](https://github.com/benny-cz/FileCat/actions/runs/38504313692). Producer policy, Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26 succeeded; four tag/manual-only jobs skipped. Its 27 server-digested ZIPs contain 2,762 members and fourteen TRX with **33,347 passes/1,191 explicit skips/zero failures**. This qualifies pushed source, not a release candidate.

Independent comparison with preceding green I460 run 38503936399 preserves all **34,532** prior test name/outcome/message/skip rows. The only six new records are three `WindowsFileOperationsTests` origin-reader and extraction controls on both Windows lanes; all six pass. The exact-parent locked/oversized ADS and extraction failures and corrected local Windows controls remain in [E-I461](E-I461-windows-origin-reader.md).

Private archive `V:\FileCat\artifacts\release-evidence\ci-i461-20261014-v1` preserves exact API responses, server digests, ZIPs and member/TRX inventories. Independent rehash passes all **35** files: manifest SHA-256 `3c48ccff54eabe6d85ca56a955bf9803b8b110a934fc5c86b0e1c5efe4bfacf3`, audit SHA-256 `70381efbc14e568999f7a013cc5d223e9becdfd1dc56e22a9f5707d77bc6851e`. Separate two-file comparison archive `ci-i461-comparison-20261014-v1` rehashes under manifest SHA-256 `184dfb2c4a3fed6d71314e7ec556737bbe37e2106f29d5b4349b15ebe49865bf`.

Concurrent ADS replacement, installed UI, physical I106/I110 HOLD, candidate qualification and explicit human GO remain separate.
