# E-CI-I455 — exact pushed-source Linux sysfs input qualification

Exact source `4f60275c8bec06ec7b1f00411725553d4411a1c4` ran in [GitHub Actions 38497992834](https://github.com/benny-cz/FileCat/actions/runs/38497992834). Producer policy, Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26 succeeded; four tag/manual-only jobs skipped. Its 27 server-digested ZIPs contain **2,762 members** and fourteen TRX with **33,307 passes/1,185 explicit skips/zero failures**. This is hosted source qualification, not a release candidate.

Independent comparison with preceding green I454 run 38496881117 retains all **34,484** previous test result/name/message/skip rows. Only eight new `UnixDeviceTests` rows appear: the oversized sysfs-value control passes on all four lanes; the FIFO control passes on Mac/Ubuntu and explicitly skips on Windows x64/ARM64. The exact-parent 16.85 MiB allocation failure, Ubuntu eight-second FIFO timeout, corrected native cases and real sysfs probe remain separately recorded in [E-I455](E-I455-sysfs-opened-input-bound.md).

Private archive `V:\FileCat\artifacts\release-evidence\ci-i455-20261014-v1` retains exact API responses, server digests, all ZIPs and member/TRX inventories. Independent rehash passes all **35** files: manifest SHA-256 `03a920685b45bf178fe29b90400e49cbb8fb23682f048349334ec5c41e3c67fb`, audit SHA-256 `c4953089095b7189b2b16a8051a402c279e5f629b8b94ed83b200d66aa8a817a`. The separate two-file comparison archive `ci-i455-comparison-20261014-v1` rehashes under manifest SHA-256 `7682f47dd2b60b9071d13b84f03121646029e6825d950d97e04eed08e3246c60`.

Later I456–I458 source, whole-process memory, installed UI, candidate qualification and the I106/I110 physical-source HOLD remain separate. Stable publication still requires explicit human GO.
