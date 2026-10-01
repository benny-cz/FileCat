# E-V16-H1 — performance harnesses: what each measures, asserts or only prints

Plan §9 asks, before acceptance runs, to "verify which existing benchmarks assert, merely print or self-grade each
metric" and to validate missing input, ready and harness measurements with positive and negative controls. This is
that inventory, read from the code on `5c42a91`, with preliminary numbers from this machine. The numbers are not
acceptance evidence: they come from the historical regression profile (below), not the §21.2 reference profile, and
not with §9's sampling rules (50 warm and 50 cold launches, 300 events per workload, raw samples kept).

**This machine:** AMD Ryzen 9 5900X, 12 cores / 24 threads, 64 GB, NVMe and SATA SSDs, Windows 11 10.0.26220 (an
Insider build), Debug builds of the test projects, other sessions' work running beside it.

## Per workload

| §9 workload | Harness | Measures | Asserts? | Gap before acceptance |
|---|---|---|---|---|
| Startup, two local tabs (ready ≤1 s warm, ≤3 s cold, p95) | `FileCat.exe --benchmark N` (`NativeBenchmark`) | `startup_to_first_frame_ms`: process start to the window's Opened event, once per launch | no: writes JSON | **ready for input** (usable panels answering an externally injected event) is not measured; no launcher for 50 warm and 50 cold launches; no cold-start method |
| First rows (10k ≤250 ms warm; million first batch ≤500 ms) | `NativeBenchmark`; `eng/ListingScale` | first rows and completion, N synthetic entries in up to four panels; ListingScale also peak private and managed memory | no: JSON / console | thresholds not graded; real (not synthetic) folders not covered |
| Cursor and mark input (next frame p95 ≤16.7 ms) | `NativeBenchmark` | key events raised inside the app (`RaiseEvent` on the panel) to the next `RequestAnimationFrame` callback | no | the plan's definition runs from the **OS input event** to the frame **presented**; neither end is what is measured; no physical autorepeat |
| Panel and tab switching (p95 ≤50 ms) | `NativeBenchmark` | panel switches the same way | no | as above |
| Command feedback and cancel acknowledgement (≤100 ms) | none for the window; workers: below | — | — | no harness for the window's acknowledgement |
| Scrolling (p95 ≤16.7 ms) | `NativeBenchmark` | frame intervals under continuous paging | no | each shipping theme and dynamic columns not run |
| Listing storage (512 MiB shared index) | `eng/ListingScale` | peak private and managed memory, index budget given | no | mappings and spill not reported separately |
| Content caches (64 MiB shared) | none | — | — | no aggregate accounting harness |
| Huge hex (first page ≤250 ms, seek p95 ≤100 ms) | none found | — | — | no harness |
| Large copy (≤10% over CopyFile2) | none found | — | — | no harness |
| Small copy (100,000 × 4 KiB, ≤25%) | `SmallFileCopyBenchmark` (`FILECAT_COPY_BENCH`, `_ROUNDS`) | job time against CopyFile2 alone on the same files, median overhead over rounds | no: prints; asserts only that the copy completed | ungraded; the default is 500 files, not 100,000 |
| Comparison (binary ≤2 s, text ≤10 s, aligned ≤30 s, cancel ≤250 ms) | `CompareBenchmark` (`FILECAT_COMPARE_BENCH`) | the TV-08 workloads | **yes**, all four | — |
| Search (200 MiB ≤2 s, 50,000 files ≤20 s, ≤512 MiB) | `SearchBenchmark` (`FILECAT_SEARCH_BENCH`) | the TV-08 workloads, cancel | **yes**, and cancel ≤250 ms | the Unicode option is not measured separately |
| Archives (≤4× in-box, ≤5× external, first page) | `ArchiveBenchmark` (`FILECAT_ARCHIVE_BENCH`) | ZIP, TAR.GZ, 7z, ISO: pack, list, first page, extract, update | **yes**: ZIP and ISO first page ≤250 ms warm, extract ratios, update scratch | TAR.GZ and 7z first-page costs reported, not graded (as §9 asks) |
| Remote | `RemoteBenchmark` (`FILECAT_REMOTE_BENCH`) | small and large transfers against the connection alone | **yes**: ratios | 100 ms latency and faults not added |
| Recovery (scan ≤30 s, first preview ≤1 s) | `RecoveryBenchmark` (`FILECAT_RECOVERY_BENCH=<folder>`) | scan and first preview of a fixture | **yes** | needs the fixture folder |
| Registry | `RegistryBenchmark` (`FILECAT_REGISTRY_BENCH`) | listing, cancel | cancel ≤250 ms only | limits to be recorded |
| MTP | `MtpRobustnessTests` (`FILECAT_MTP_BENCH`) | a thousand files written, listed, copied back | the device answers within 15 s after a cancel | limits to be recorded (E-V21-M1 has one run) |

## Preliminary runs here (2026-10-01)

| Benchmark | Result |
|---|---|
| `CompareBenchmark` (before and after `db2e9b4`) | passed: identical million lines 0.87–0.91 s; shifted 1.36–1.49 s; 1,000 scattered edits 1.37–1.67 s; binary 256 MiB identical 0.10 s; aligned binary 2.6 s and 4.7–4.8 s; cancel within 12–20 ms |
| `SearchBenchmark` | passed: by name over 50,000 files 95 ms; content in 50,000 small files 3.6–4.1 s (budget 20 s), 44 MiB allocated (512 MiB); 200 files of 1 MiB 48–75 ms (2 s); cancel within 13 ms (250 ms) |
| `ArchiveBenchmark` (273 MiB payload) | passed: ZIP list 72 ms, first 64 KiB of a 64 MiB member 41 ms cold and 2 ms warm, extract 31.3 s against `ZipFile` 12.4 s (2.52×, budget 4×), update scratch 132 MiB; TAR.GZ extract 1.22× `TarFile`, last member's first 64 KiB 1,040 ms cold and 418 ms warm (reported); 7z extract 27.7 s against native 7-Zip 15.3 s (1.81×, budget 5×); ISO first page 179 ms cold, 0 ms warm |

The window's own benchmark (`--benchmark`) was not run: it opens FileCat's window on the owner's desktop.

## What is needed before acceptance

1. A ready-for-input measurement (an injected event answered by a usable panel), validated with a positive and a
   negative control, and a launcher for warm and cold launches with a documented cold-start method.
2. Input latency from the OS input event to the presented frame (or a documented equivalent the owner accepts), and
   physical autorepeat, per §9's definition.
3. Harnesses for the huge-hex, large-copy and content-cache rows; grading for the rows that only print.
4. The reference machine of §21.2, isolated from other work.
