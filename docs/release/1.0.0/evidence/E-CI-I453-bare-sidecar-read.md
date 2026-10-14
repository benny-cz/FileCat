# E-CI-I453 — exact pushed-source bare-sidecar reread qualification

Exact source `8ec88623f8bb92913a17b765cbb9ab4648718ad5` ran in [GitHub Actions 38495928351](https://github.com/benny-cz/FileCat/actions/runs/38495928351). Producer policy, Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26 succeeded; four tag/manual-only jobs skipped. Its 27 server-digested ZIPs contain **2,762 members** and fourteen TRX with **33,297 passes/1,179 explicit skips/zero failures**. This is hosted source qualification, not a release candidate.

An independent comparison with preceding green I452 run 38494871494 retains all **34,472** previous test result/name/message/skip rows. Only the new `ChecksumManifestBoundedInputTests.Bare_checksum_fallback_does_not_materialize_an_oversized_file` row appears, passing once on each of four platform lanes. The original I453 16.8 MiB allocation failure and corrected native controls remain separately recorded in [E-I453](E-I453-bare-sidecar-second-read.md).

Private archive `V:\FileCat\artifacts\release-evidence\ci-i453-20261014-v1` retains exact API responses, server digests, all ZIPs and member/TRX inventories. Independent rehash passes all **35** files: manifest SHA-256 `d83dc8b8e8b5a574a9cded28de118f2afebe847634f3f0bd496f81e503c377a3`, audit SHA-256 `7bc685640af13565671d46c236f81e83a4ae13b509b70b0dd02739aa2ff14a5e`. The separate two-file comparison archive `ci-i453-comparison-20261014-v1` rehashes under manifest SHA-256 `c2a1e73e8e9f25c88cee5e72d9474ca4d6607453c12e362388bec7ef74bab028`.

Later I454/I455/I456 source, whole-process memory, installed UI, candidate qualification and the I106/I110 physical-source HOLD remain separate. Stable publication still requires explicit human GO.
