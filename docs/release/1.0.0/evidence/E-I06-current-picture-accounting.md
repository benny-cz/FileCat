# E-I06 — current F3 and QuickView picture accounting

2026-10-10 CEST, exact **d24a45784d9ccac17d5e9fbf3b7534da2cbda34e** /**1386 canonical blobs**, unchanged committed product assemblies/apphost/native DLLs from the no-overlay exact Find build. Two standalone package-free observers reference these assemblies; FileCat is not rebuilt. All selected product copies, observer source/build/deployment pins, empty package graphs and real worker layout are independently checked. Windows host background processes use real Skia with headless drawing disabled and isolated owned state; no desktop input or visible workstation window is used.

The original probes remain at their older producers. This fresh run repeats one 256² F3 viewer, eight 4096² F3 viewers and four QuickView panes, preserving actual source identities rather than attributing old memory measurements to the new head.

| Case | Locked buffers at full load | Private baseline MiB | Private loaded MiB | Maximum private sampled MiB | Private after close/collection MiB |
|---|---|---|---|---|---|
| current-small | 256 KiB | 24.52 | 38.48 | 41.31 | 37.00 |
| current-eight | 524288 KiB | 25.75 | 631.95 | 634.20 | 80.07 |

Both F3 cases exercise hide/restore/close: hiding eight pictures leaves their **512 MiB** buffers owned while effective visible-picture bytes become zero; restoring reuses the same bitmaps, and closing releases all picture/page charges and disposes all nine sources exactly once outside active reads. Retained buffers differ from pixels actually displayed.

QuickView passes **24 decodes**, five replacement rounds, malformed/text fallbacks and detach: four retained 256² previews use **1 MiB**, all 24 retired bitmaps reject locking, all 32 sources dispose exactly once and final pixel/page/reader/admission charges are zero. Private memory is 26.03 MiB initially, at most 49.93 MiB sampled and 34.59 MiB after close. All source PNG/text/header hashes remain unchanged.

The independent reader checks complete encoded PNG patterns/CRCs/bounded inflation for three distinct fixtures and six wrong-dimension/color refusals. All 33 actual decoded framebuffer identities have correct dimensions/strides and expected center BGRA; this samples decoded pixels and does not assert every decoded pixel. All four attempt roots have no owned process remaining. Five owned temporary files are archived/rechecked and removed.

Three original observer failures remain: repository lock-guard inheritance before app code, unavailable native-library lookup before app setup, and an incomplete worker apphost/runtime payload. Corrected standalone project boundaries, explicit exact native DLL placement and the exact FileCat worker apphost/deps/runtime configuration are declared; no product dependency lock, assembly or budget changes.

Locked buffers, managed heap, page charges, private bytes and working set retain separate raw snapshots. These background offscreen runs do not measure transient worker allocation peaks, native compositor frames/input, exclusive reference throughput or exact-candidate behavior. The approved 64 MiB page target is not extended to pictures, and no arbitrary aggregate picture cap is introduced. I06 stays open for remaining simultaneous consumer/worker/frame/platform/reference/human/candidate qualification. Physical-source HOLD and human GO remain.


## Selected immutable receipts

Private FileCatReleaseEvidence paths unless absolute. Nested records preserve exact sources, original commands, payloads, raw adverse attempts and owned restoration.

| File | SHA256 |
|---|---|
| `i06-current-picture-accounting-20261010-v4/seal-current-picture-v1.py` | `0f9463ad620df6369d652c87c6bd95c9802e3e08378f3feaa209c9cd7b7c0e89` |
| `i06-current-picture-accounting-20261010-v4/independent-current-picture-final-v1.json` | `524ce4b0529abb4a7ffca0abf0da53494306e9c9b4fb579bc5a1505ded5a42ff` |
| `E:/FileCat/artifacts/release-evidence/i06-current-picture-accounting-20261010-v4/batch-final-v1.json` | `aba0580061b330c6d6240d93fb9b0f5caa9a452b7188fd6c26e359c77475438d` |
| `E:/FileCat/artifacts/release-evidence/i06-current-picture-accounting-20261010-v4/f3-inputs.json` | `19b7d678a83cd7437dda6aafa70150b3407f6ab8acbd05164cc7d0e8b73ebf91` |
| `E:/FileCat/artifacts/release-evidence/i06-current-picture-accounting-20261010-v4/quickview-inputs.json` | `c9b3c546ee9b9aea3732463ca97da53154689a27316162cbebd6da70e03a1e00` |
| `E:/FileCat/artifacts/release-evidence/i06-current-picture-accounting-20261010-v4/f3-small-run/command.json` | `ff069b3571ae59c06e6083f93e63cdb67dbf8a1b5943f759c063d87f8fe36a1e` |
| `E:/FileCat/artifacts/release-evidence/i06-current-picture-accounting-20261010-v4/f3-eight-run/command.json` | `0064f86a11aa4208fae9b3219cbc09b38365ff74f2a2d91aa9bdfb1d9636f54d` |
| `E:/FileCat/artifacts/release-evidence/i06-current-picture-accounting-20261010-v4/quickview-four-run/command.json` | `f615331ea614f6fb3eb4e9076886041f3402d6f04727b1b810eaf5aa240c0f25` |
| `E:/FileCat/artifacts/release-evidence/i06-current-picture-accounting-20261010-v4/f3-small/result.json` | `84730cacbfa3f05d902e6365bfe0aef6034e968bc746f4167455dd05b939025b` |
| `E:/FileCat/artifacts/release-evidence/i06-current-picture-accounting-20261010-v4/f3-eight/result.json` | `6c77ceac5686c686ab3100b24ff3d47d512101f7c5bb3f4699502fba1f1baa97` |
| `E:/FileCat/artifacts/release-evidence/i06-current-picture-accounting-20261010-v4/quickview-four/result.json` | `7ce1a00d4f28d2af156f18d289a27161f525a1870df028d8785a243e12070e5c` |
| `E:/FileCat/artifacts/release-evidence/i06-current-picture-accounting-20261010-v4/owned-temporary-files-v1.zip` | `eebf1af035377b77329f31d52ea025033dcdef21a9ef0d11fe152cf19af2bcfa` |
| `E:/FileCat/artifacts/release-evidence/i06-current-picture-accounting-20261010-v1/f3-build/command.json` | `b1735f6f4ab814e6505cf78a9a2c803dd267dc2f8c86e2736d85c00f1d7af314` |
| `E:/FileCat/artifacts/release-evidence/i06-current-picture-accounting-20261010-v2/f3-small-run/command.json` | `7ea04e7927a34cec871af590beb7c7896afb20ba979e24e0d6d9858a599d503e` |
| `E:/FileCat/artifacts/release-evidence/i06-current-picture-accounting-20261010-v3/f3-small-run/command.json` | `465427dbd184bb09597ae0c6efe6f357d9035b3f873d7c0d7f90b89d7a696eab` |
