# E-CI-I454 — exact pushed-source Unix verification FIFO qualification

Exact source `f0c7c4013b5208cd42ecb51ab20f99b8b747d3c6` ran in [GitHub Actions 38496881117](https://github.com/benny-cz/FileCat/actions/runs/38496881117). Producer policy, Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26 succeeded; four tag/manual-only jobs skipped. Its 27 server-digested ZIPs contain **2,762 members** and fourteen TRX with **33,301 passes/1,183 explicit skips/zero failures**. This is hosted source qualification, not a release candidate.

Independent comparison with preceding green I453 run 38495928351 retains all **34,476** previous test result/name/message/skip rows. Only eight new checksum FIFO rows appear: two Unix controls pass on each Mac/Ubuntu lane, and the same two controls explicitly skip on each Windows lane. The original eight-second Mac FIFO timeout and corrected native cases remain separately recorded in [E-I454](E-I454-verification-fifo-open.md).

Private archive `V:\FileCat\artifacts\release-evidence\ci-i454-20261014-v1` retains exact API responses, server digests, all ZIPs and member/TRX inventories. Independent rehash passes all **35** files: manifest SHA-256 `69f0d1fabc819efbfb3e588b87d9b736ca0077d5c47d09ced0c20bbd5dc77553`, audit SHA-256 `8352e9fb9f4647d56db49b9b43239ce275d674749b913b668021f6f6e1cb7301`. The separate two-file comparison archive `ci-i454-comparison-20261014-v1` rehashes under manifest SHA-256 `c4d7e46ed897302c8b16b701c2af248ac5d67564e93e9bc16be5aa1be018e7c2`.

Later I455–I457 source, whole-process memory, installed UI, candidate qualification and the I106/I110 physical-source HOLD remain separate. Stable publication still requires explicit human GO.
