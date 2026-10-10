# E-I06 — combined native listings, panel QuickViews and F3 ownership

2026-10-10 CEST. Two combined native Windows guest cases pass at unchanged **d24a45784d9ccac17d5e9fbf3b7534da2cbda34e**, with all **1386 canonical Git blobs**, product build outputs and transported assemblies independently verified. The private observer references 33 exact assemblies in a package-free project. It uses production MainWindow/MainViewModel, the Windows platform adapter, product styles/Classic theme and four actual panel QuickView controls. Shell pictures and animations are disabled for the declared fixture. Actual Win32 windows run in VMware Admin console session 1 on Insider 26300; no workstation UI or VM console is operated.

Each panel contains a production synthetic listing plus four owned content entries, using actual listing spill/index paths. All four QuickViews decode, replace pictures three times, show malformed/text fallback and detach. One/eight simultaneous F3 viewers hide, restore their original bitmap objects and close. Each visible native window has a nonzero HWND and completes a requested product compositor batch at every checkpoint.

| Case | Rows per panel / total | F3 viewers / locked MiB | QuickView decodes / sources | Compositor completions | Parent samples / child observations | Sampled parent / individual worker max private MiB |
|---|---|---|---|---|---|---|
| small | 100004 / 400016 | 1 / 0.25 | 16 / 24 | 21 | 78 / 93 | 220.67 / 10.18 |
| eight | 1000004 / 4000016 | 8 / 512.00 | 16 / 24 | 119 | 281 / 354 | 1029.39 / 202.67 |

Across both cases, **32 QuickView decodes, 48 QuickView sources and nine F3 sources** retire as observed. All sources dispose exactly once outside a read; all 32 replaced QuickView and nine closed F3 bitmaps reject locking. Four visible QuickViews retain 1 MiB in total while the larger F3 case retains **512 MiB**, including while hidden. Final tracked pixel/page/reader/external-index charges are zero and no owned product process remains. The larger listing profile spills 392,000,976 bytes. These charges do not assert that every managed object or native allocation was reclaimed.

The reader checks four complete encoded PNG fixtures, including CRC/size/color, and rejects eight wrong-size/color controls. It independently compares all 41 actual decoded framebuffer dimensions, row strides and center pixels; it does not inspect every decoded pixel or OS-presented frame. Owned content files remain unchanged. Finite parent/child samples keep managed bytes, locked pixels, shared pages, spill/index, private bytes and working set separate. Largest parent sample gaps are 436.14/431.90 ms. Observed worker PIDs do not establish complete worker census, peaks or simultaneous family totals. Product compositor completion is distinct from external input or OS presentation.

## Runtime and retained adverse attempts

The earlier listing/picture cases retain their observed system **.NET 10.0.9** identities. A later fresh guest diagnostic found only system runtimes through 9.0.20; the intervening cause is unproven. This batch uses a pinned, isolated **.NET 10.0.12** runtime under the owned v3 test root, inherited by picture workers and checked file-by-file before and after. Its retained ZIP provenance is declared; no new upstream-download chain is claimed. No system runtime, registry, firewall or persistent guest setting is changed. The temporary allowlisted HTTP server on the private VMware adapter closed before workload execution.

Original attempts remain adverse: v1 lacked an exact MVVM observer reference; both v2 cases failed before managed product startup because the system runtime was unavailable; the first v3 runtime transfer timed out after 180 seconds, leaving a guest-computed 24,710,000-byte partial whose hash independently matches the exact archive prefix. That partial remains in the guest; its raw bytes were not retrieved. A new WebClient transfer fetched the complete pinned archive. Both v3 workload observers disposed tabs before main-window state saving; both v4 workloads completed their result checks but failed during a duplicate main-window close in finally after tab disposal. V5 corrects observer close ownership and both processes exit normally. No preceding failure is rewritten as a pass or assigned to FileCat without evidence.

I06 remains open. Wider consumer/error/cancellation ownership, native/GPU/file-mapping allocation, other platforms, exclusive reference hardware, actual input/presentation, required people and exact-candidate qualification remain. No new aggregate picture cap or acceptance decision is invented. Physical-source HOLD, contract/freeze gates and human GO remain unchanged.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain exact sources, payloads, commands, raw results and adverse attempts.

| File | SHA256 |
|---|---|
| `i06-native-combined-20261010-v5/independent-combined-final-v1.json` | `45f719ceda4038dcfc58a24532b4f5851c4a2cf519eb07d60942898066be53c3` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/seal-combined.py` | `e334513d085e54d2676f4b7e029186ab81df853cee68598be9894a175e5cd8e4` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/run-native.py` | `a936729c2e091f3d9c30e5798b2e92e9894e3fcc149967847558aef1d3480835` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/native-run.ps1` | `d149107814f743a6dd49a19ee2489d2c7c30c9380465e9db082c387838a95888` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/manifest.json` | `34a24787b375b2426937a01d59118d44d86fe90cd8f3fc1dcd0b356b599e1bb9` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/outputs.zip` | `ebc3835ccb4238b3ec4955547a8c2791dad89f1e5d1e60da4e387ae24562a95e` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/retrieved/native-result.json` | `0e3a9029aaa3b7b76e5dab73b0430df502a2157a97cfba9c5165c8d66b28d6d3` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/observer-source/Program.cs` | `d5b18b1821af1ac317b81a48bce48244ddb4194853ac3b3fb76aef872d3c6274` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/observer-source/Probe.csproj` | `bba006faece9e6c0ceab0fcb768d615fb6c990c4bf1e525bdb3f8d43051930fc` |
| `i06-native-combined-20261010-v5/preparation-v1.json` | `9a93ca6c93b2705bd19e6a93d0fcdb49e566a77535d04b48d6fce7993e80eff6` |
| `i06-native-combined-20261010-v5/transport-final-v1.json` | `693aae0b9db8100c8929f005b027645d41b805b19fc076f259fccc0aab209cb2` |
| `i06-native-combined-20261010-v2/runtime-diagnostic.json` | `e171243c1bc2517f3152b675aae1eade42f69ffc218176c6655ff518c93dfb29` |
| `E:/FileCat/artifacts/vm/dotnet-runtime-10.0.12-win-x64-private.zip` | `d3a5d78d5ff2efe8337924ab984eed2e43fa3be25bf6a41e010c4900826c8753` |
| `i06-native-combined-20261010-v3/private-runtime-v2.json` | `157a4e665bc9f71d1930d9cc370a75ee6dca83b5c3d0a32c62f4a6bc8d1de1a8` |
| `i06-native-combined-20261010-v3/private-runtime-server-v2.json` | `6b64e8a0fb7b135ce3a7866aad1fe0d97fa25fc721ef709a0b34fa2218b3b304` |
| `i06-native-combined-20261010-v3/private-runtime-transfer-command.json` | `6472acb449d4d7f5f7e1f9d0e0b1d17f7f1da29abd8d89590f115fd42ec74ec2` |
| `i06-native-combined-20261010-v3/runtime-check-v1-v1.json` | `98f20a8b3cac30ca2447036d2a7d479a6d8b441526fddac14670f1f4603bfa8f` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v3/runtime-transfer-v2.py` | `3fede9a80c82d102bc13435af8573cb2947b20e6cf59f7d4987d3573c80d8d5d` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/retrieved/small/result.json` | `eba8db21263871b20ec90dd85d420e8c2eb14ed0f3e50605e4834a84433a2d36` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/retrieved/small-command.json` | `6839aa68ee4ce9709b88bde430b9a2219c4c3eadf4bc4415e2702db90de26c71` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/retrieved/small-samples.json` | `a7038ef2f385c317201e351dc5fc697c08ee4be9b32f58bc4e7fd3b0dc69b21f` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/retrieved/small-children.json` | `a87b632047f003be51b7802fdc24198d4be8d7cda0083dd6c8bca2adb4b2e2f4` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/retrieved/eight/result.json` | `f49c8c4bc02f878a63e0ed727760bfbfabfd74db40745f673bab295e6651f1d8` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/retrieved/eight-command.json` | `2b6f834f9fcecdc8431e65caaf29bd90673e2a372fffe57cbf6d327fecde3b8a` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/retrieved/eight-samples.json` | `b07934a57170c6ca8c05a286a256926084489dbd12e05844de2c01926f1bd1a2` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v5/retrieved/eight-children.json` | `6d6a0eb0a7ddebc01182adb661da8160962b34636b87407480fe1106765f5dd1` |
| `i06-native-combined-20261010-v1/preparation-v1.json` | `9a23380b706b2f9c866b560bc0a380348d516943a14ff97452c6386e78e2da55` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v2/retrieved/small-command.json` | `e54bb17dbf8b2fe2f165f093aa9664470394e2dbbf976f7120819ebd161ca361` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v2/retrieved/small-stderr.txt` | `e1cbc21977d42ee0eb15cbbdd0d498eb14bb7b16d06e59307f2b6e79e7f65559` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v2/retrieved/eight-command.json` | `b36e1c5ff92b649033f3f649dddc123e838d3eb779402a89592877a387c9f9ca` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v2/retrieved/eight-stderr.txt` | `e1cbc21977d42ee0eb15cbbdd0d498eb14bb7b16d06e59307f2b6e79e7f65559` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v3/retrieved/small-command.json` | `2fed56290e9f0f7ed35b977a49ffca4ba191909a69e4371a109819440c8d08b7` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v3/retrieved/small-stderr.txt` | `194d0a1b5fae2036e3c281ee3742cfc6cab332467e198ad0588e5c388a25db3a` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v3/retrieved/small/snapshots.json` | `3774ce817e119ce41715f062f3f2ad10d204752e23853511cb0faa602e51a8ae` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v3/retrieved/eight-command.json` | `34676c98b200e263f24c055c7533277b07d2ab589db588db66c2d272b38a0e51` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v3/retrieved/eight-stderr.txt` | `8cc1359a3bd738a0d344b021f3a46229c4821f31d95eaf6a14f82d1254139153` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v3/retrieved/eight/snapshots.json` | `dc35627e9c1341d89378b7555ab60fedf352b2f890b948c04eddc078ba356915` |
| `i06-native-combined-20261010-v4/transport-final-v1.json` | `9d9e25d942a9c722a58d9cd299da36c6c0bac2e9dd91478e46af779b8e0e3f3b` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v4/retrieved/small-command.json` | `525137b7e717530587023ccb11393857150a94b78db4ec3010cb40a29d05c924` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v4/retrieved/small-stderr.txt` | `98e403d8a1edfdb23156ad554190957d1e62bcc82f9494f5991ce0b5eacf65a0` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v4/retrieved/small/result.json` | `040962a4ad13d231cdb593158ed6a589bd01b6c0c7f8bccb95d561b62449449c` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v4/retrieved/eight-command.json` | `96664a31476d831d2f74781483e895123f0c1f1e8325d26d251e2794be559f14` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v4/retrieved/eight-stderr.txt` | `98e403d8a1edfdb23156ad554190957d1e62bcc82f9494f5991ce0b5eacf65a0` |
| `E:/FileCat/artifacts/release-evidence/i06-native-combined-20261010-v4/retrieved/eight/result.json` | `9d55d234e89ab89acd465ecfee41dcfff384db7602722fe06a469a54d6a893f3` |
