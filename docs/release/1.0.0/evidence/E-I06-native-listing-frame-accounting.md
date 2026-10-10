# E-I06 — native Windows listing, memory and compositor observations

2026-10-10 host CEST. Exact **d24a45784d9ccac17d5e9fbf3b7534da2cbda34e**, all **1386 canonical blobs** and unchanged no-overlay product build verified. The existing native benchmark runs in the VMware Windows 11 Insider 26300 guest, active Admin console session 1, through background VMware Tools. No workstation UI or VM console is used. The framework-dependent test-build payload is declared; it is not a release distribution. The guest uses **.NET 10.0.9**, distinct from the producing host’s 10.0.12. The guest wall clock differs from the host and is retained as observed; stopwatch elapsed times measure samples. No persistent guest setting changes.

Four native main-window cases complete normally. Each loads four actual listing stores, verifies the requested row counts, routes 530 discrete cursor/mark/panel events plus paging/sort/search through production handlers and awaits product compositor completion. Main-window HWND/title and console session are independently observed. Synthetic entries avoid manufacturing millions of physical files; actual listing spill/index paths are used according to configured budgets.

| Case | Rows per panel | Total rows | First rows ms | Complete ms | Sampled private maximum MiB | Spill MiB | External index MiB |
|---|---|---|---|---|---|---|---|
| normal-10000 | 10000 | 40000 | 178 | 178 | 155.61 | 0 | 0 |
| normal-100000 | 100000 | 400000 | 205 | 1174 | 184.14 | 0 | 0 |
| normal-1000000 | 1000000 | 4000000 | 897 | 6428 | 470.29 | 373.8 | 0 |
| compat-100000 | 100000 | 400000 | 244 | 1063 | 186.20 | 0 | 0 |

The private observer retains every independent process sample, its actual gaps, sampled descendant census, process handles/threads/CPU, private bytes, working set and OS peak-working-set counters. Each original product benchmark retains its own managed/private/working-set samples and latency summaries. After every case, the owned benchmark temporary root is gone and no product process under the payload remains. All transported product bytes verify before and after. Raw command/output/archive/guest identities are retained independently.

These are preliminary native-window and product-compositor observations in a nonexclusive 8-vCPU/8-GiB VM. The benchmark raises routed events inside FileCat, not externally timestamped hardware input. It exports summary latency statistics, not complete raw latency or OS-present vectors. Renderer labels state the configured preference/fallback, not proof of the selected native composition backend. Finite process/descendant samples do not attribute transient worker/native/GPU allocation peaks or prove all children were observed. The compatibility case is a separate workload observation, not a causal performance comparison. [Combined main-window/QuickView/F3 cases](E-I06-native-combined-accounting.md) now pass at their distinct private-runtime identity. Required reference-machine, physical-input/presentation, other-consumer, wider platform/human and exact-candidate qualification remain. I06 stays open; no numeric picture cap, physical-source resumption or publication is authorized.

Before any native benchmark started, the original VMware file copy timed out and two bounded guest checks failed. The retrieved guest-computed partial-file size/hash receipt records 10,752,000 bytes; that file remains as `payload-v1-partial.zip`. The authorized guest reset required the owner to log in again; the exact Admin spelling then accepted the fresh guest check. A temporary server bound only to the private VMware adapter served the pinned files only to the guest IP and was closed before tests; existing Python firewall permissions were inspected, not changed. Raw failed checks, partial-file hash, reset/login observations and new complete transport remain distinct. One bounded Mac SSH check timed out before any remote command ran; that platform resumption is queued.


## Selected immutable receipts

Private FileCatReleaseEvidence paths unless absolute; nested receipts preserve complete exports, original raw results and transport.

| File | SHA256 |
|---|---|
| `E:/FileCat/artifacts/release-evidence/i06-native-aggregate-20261010-v1/native-run.ps1` | `f63c9899d700add6699ddf48e259f3c0f3059b3de5a6acbad2fc22987e4d084e` |
| `E:/FileCat/artifacts/release-evidence/i06-native-aggregate-20261010-v1/run-native.py` | `dfedef585e91b7681563ed89d882e6c6ff8214f16b9cbb4bec386e7ee538e585` |
| `E:/FileCat/artifacts/release-evidence/i06-native-aggregate-20261010-v1/run-native-v2.py` | `ba5ffad69cdf72a7f8499a094c7a6f6d50e7db006d564df3f121d4bd69039f11` |
| `E:/FileCat/artifacts/release-evidence/i06-native-aggregate-20261010-v1/seal-native.py` | `2d6722fd10c5c5ba928135c25a72b0ecedfa182dd7ee73dbb85859849b33ff68` |
| `i06-native-aggregate-20261010-v1/independent-native-final-v1.json` | `4df0bcb63f2b60e20d3bb4e81eb6acdd1a90cd5fbbd5c48d2a5084d4990c42d4` |
| `i06-native-aggregate-20261010-v1/transport-final-v1.json` | `99edeb74bbbcd40e20c8695c7b974f7bfa6693bfc2ca5c33cebdc2c423cfa4dd` |
| `i06-native-aggregate-20261010-v1/guest-preflight.json` | `825bd8b561fe552b8e06a001c692099e0ca5f9bbc92ff25b7e2a25d86c531d6c` |
| `i06-native-aggregate-20261010-v1/transfer-check-command.json` | `f0749b0bf2783c2b77138c0c0ed5ebf1877373954811a1ca369d9a58002b5b67` |
| `i06-native-aggregate-20261010-v1/transfer-check-v2-command.json` | `835137da42266d78b9d0d9abc21e97e4c4575178de1f7b6536a029b47bd6eabe` |
| `i06-native-aggregate-20261010-v1/transfer-check-v4-v1.json` | `d074c015ac2a84da4199c220906d117e602bb7ebde6878583876c4d5fd156652` |
| `i06-native-aggregate-20261010-v1/vmrun-reset-command.json` | `b1eba7990ce12395b33f9d9f9ea0cffffeabe1ed79898be5fd81ed2ab3f43127` |
| `i06-native-aggregate-20261010-v1/guest-interactive-readiness-command.json` | `f53bc8d3a05d201808973c16394299b90e76235c405950f6cb4f27f8d24f1de3` |
| `i06-native-aggregate-20261010-v1/guest-interactive-readiness-stdout.txt` | `0ff389983086a67c82b2414e884d73f7955246fe69ad4e986c661136da8bb76b` |
| `i06-native-aggregate-20261010-v1/http-server-v2.json` | `a688755ee631c84207f0bb214ced66116198af4edba8c3a01c42c54d77cb55ff` |
| `i06-native-aggregate-20261010-v1/guest-http-transfer-v2.json` | `b11d7ac548d2af8dfc727a8406b495b1bd21baf2bd4f8f07b8de6697fab49ea0` |
| `i06-native-mac-20261010-v1/preflight-command.json` | `500ffb4f85748f0677e2e373d69e1225f9e8f6e1a4322b0bef06f86dd1290af0` |
| `i06-native-mac-20261010-v1/preflight-stderr.txt` | `4ccbc688400288e58cd5293523a6785b050593ec32473586397d765965f8ed52` |
| `E:/FileCat/artifacts/release-evidence/i06-native-aggregate-20261010-v1/manifest.json` | `58fd1b83db6efbfe5ff634761f573e7f450051729ebb5f4324f43806d5dfa9c7` |
| `E:/FileCat/artifacts/release-evidence/i06-native-aggregate-20261010-v1/payload.zip` | `fd8de2faf269e406063e7f3cbadfebaac8b7c5a9e26c8bed67528b50fbb84f1c` |
| `E:/FileCat/artifacts/release-evidence/i06-native-aggregate-20261010-v1/outputs.zip` | `14afe8d890a33d0721487d9f9225f00f46e1afe1446fd368915aaecccf2e12c0` |
| `E:/FileCat/artifacts/release-evidence/i06-native-aggregate-20261010-v1/retrieved/native-result.json` | `cc71dd2e0a482d08969246c1aeb6bda66b1f84d104a72b907fd22c6102bdcce3` |
| `E:/FileCat/artifacts/release-evidence/i06-native-aggregate-20261010-v1/retrieved/normal-10000/command.json` | `5ad41e98685ef7e79fcc972e7f82765dca7386e8b95b49bcf331dae39578e63a` |
| `E:/FileCat/artifacts/release-evidence/i06-native-aggregate-20261010-v1/retrieved/normal-100000/command.json` | `88481b82d6c2a98cf519ae59527956b9b98d0a14c2cbf2f94573b699b4247aa2` |
| `E:/FileCat/artifacts/release-evidence/i06-native-aggregate-20261010-v1/retrieved/normal-1000000/command.json` | `729f0011e7079ef091dffa7dedd14d74e239566b1537a065136577c5e4ea8562` |
| `E:/FileCat/artifacts/release-evidence/i06-native-aggregate-20261010-v1/retrieved/compat-100000/command.json` | `adaa250837ad8bf8af9d33cb98089c2029c984681efad5ad4603ce3a8e290a7d` |
