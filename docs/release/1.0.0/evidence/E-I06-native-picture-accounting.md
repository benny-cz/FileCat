# E-I06 — native Windows picture ownership and separate process accounts

2026-10-10 CEST. The unchanged **d24a45784d9ccac17d5e9fbf3b7534da2cbda34e** product build and **1386 canonical Git blobs** verify independently. The standalone, package-free observer references 32 exact build assemblies and uses Avalonia’s actual Win32 platform in VMware Admin console session 1 on Windows Insider 26300 / **.NET 10.0.9**. Only that guest executes the observer. Product assemblies are neither rebuilt nor modified. Host UI and the VM console are unused.

Two cases exercise production ViewerWindow decode, mode hiding, picture restoration and close. The small case opens one 256² picture; the large case opens eight simultaneous 4096² pictures. The reader checks complete encoded PNG structure, CRCs and known solid color, with four wrong-dimension/color refusals. Actual Skia framebuffer dimensions, row strides and center pixels agree; this is not an assertion that every decoded pixel or OS-presented frame was inspected. At each checkpoint, every visible native window has a nonzero platform handle and completes a requested product compositor batch.

| Case | Viewers | Locked picture bytes MiB | Native compositor completions | Parent samples | Direct-child observations | Sampled parent max private MiB | Sampled individual worker max private MiB |
|---|---|---|---|---|---|---|---|
| small | 1 | 0.25 | 4 | 35 | 36 | 76.64 | 4.43 |
| eight | 8 | 512.00 | 200 | 358 | 394 | 746.42 | 202.19 |

Hiding all picture controls retains their original buffers: **512 MiB** remains owned in the eight-viewer case while the visible-picture charge is zero. Restoration reuses the same bitmap objects. Closing each viewer releases its buffer; all nine closed bitmaps reject locking, all nine sources dispose exactly once outside a read, and final pixel/page/reader charges are zero. The source files remain unchanged and no owned product process remains after either case. Hidden bitmap retention is measured behavior; no new aggregate cap or acceptance decision is inferred.

The reader keeps locked pixel bytes, shared pages, managed bytes, checkpoint private/working-set counters and independent parent/child process samples separate. It observes one/eight distinct picture-worker PIDs, plus console-host children. Largest parent-sample gaps are 531.64/246.92 ms. The individual worker maxima in the table are finite observations, not complete worker peaks or a simultaneous family total; startup/exit can fall between samples. [Combined main-window/QuickView/F3 cases](E-I06-native-combined-accounting.md) now pass at their separate .NET 10.0.12 identity. Native/GPU allocation, actual presentation timestamps, physical input, wider combined-consumer workflows, other platforms, exclusive reference hardware, human and exact-candidate acceptance remain. I06 stays open.

The first independent reader rejected ten legitimate empty state-directory ZIP entries because it compared every member with a file-only inventory. Its executed bytes and observed failure are preserved. The corrected reader separately verifies every file and the exact ten zero-length directory entries. No guest test was rerun to replace a result. Product and observer hashes verify before and after; there are no persistent guest setting changes or physical-source operations.


## Selected immutable receipts

Private FileCatReleaseEvidence paths unless absolute; nested receipts preserve complete exports, original raw results and transport.

| File | SHA256 |
|---|---|
| `i06-native-picture-20261010-v1/independent-native-picture-final-v1.json` | `7a17ed110dadad38a234d93af1eddb6b449afc1bd37d97d0cddc59aff53380ee` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/seal-native-picture.py` | `8aeed613af1d41a64f81547d875790d79daf1a284d0f9db0c552817458866b93` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/seal-native-picture-v2.py` | `82e744074fbd25069e195aa3be51992d97356eaed3263b42f90148103d6c6eac` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/run-native.py` | `b12967b13b39331bcd0771a180ab731ac9bee40d36c341075c072dd285b8c4e6` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/native-run.ps1` | `cc1357e06e83d8bb8ceea52f60f0213ba73108334b23ad3523a45a8e3ae0457f` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/manifest.json` | `b27c5122dd05eb00199d930ff24df0009c144ce311377b74822f0e970d986119` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/outputs.zip` | `eca29cfea77eb234711ee633c72dff7970820f2147ff9fa8931ebaf4d0bdbb1b` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/retrieved/native-result.json` | `fe8a0a65720f913e8ea4c0f58ffbe0bd0038658a012df29eb7331a3c50660d06` |
| `i06-native-picture-20261010-v1/transport-final-v1.json` | `19e8af94c50442fa4479be2fe68e6ff3f91b090535d3205377e37a96da338610` |
| `i06-native-picture-20261010-v1/preparation-v1.json` | `b280342de9f778676b2b68f53c00045e63e8b6fdf53ea9414430cda784f7cd6c` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/observer-source/Program.cs` | `8f6a5ad81a35fa25d75cc605d6b7715128fee5cd9a8ababa7d2b1bab529b5035` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/observer-source/Probe.csproj` | `8bdcb8dfbacf6e1a3835535442ba3259b37b2433036ce4583c11842b0ed0fe2b` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/retrieved/small-command.json` | `e3085923387901f864466d03b30c6642fdd90581fd1cc64f25e0dc5d9024e424` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/retrieved/small/result.json` | `3ebe558fb7ebd0400e2c210480c5425f8d5cf204eb51f5d1e519d7eb890111ef` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/retrieved/small-samples.json` | `089369235b9913da7a2bd1bc7aad167e8c890eac048b3dccd8392fa5399416d3` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/retrieved/small-children.json` | `b7d93b1440490f6a250425daca41235ce8e393b61a6a6b3a9dfb2f324d3add01` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/retrieved/eight-command.json` | `cb9a23e26c4917a63d84ea6bfafbcfa2a6861e48c10589dce53d07ceed5a98b9` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/retrieved/eight/result.json` | `d253f355013613df17d262235c2f1c7f91c368e13b80a0f618696fe35667240e` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/retrieved/eight-samples.json` | `d9808c9f9e116c516e43913c04137277f91cc0ee4d735ee3ea437261eb67f335` |
| `E:/FileCat/artifacts/release-evidence/i06-native-picture-20261010-v1/retrieved/eight-children.json` | `276be2fbbfe2357e6460a2fdd820284c259c838e53b0687b10899167d5090cde` |
