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
| Huge hex (first page ≤250 ms, seek p95 ≤100 ms) | `HexBenchmark` (`FILECAT_HEX_BENCH`, `6df923b`, added here) | a 4 TiB sparse file with data islands and a 2 GiB file of real data, through the viewer's and the editor's readers: first page on a first and a second open, 300 random seeks (anywhere, inside the data, dense) by nearest rank | **yes**, both budgets | cold cache (after a reboot or a cache reset) not done: the dense file was just written |
| Large copy (≤10% over CopyFile2) | `LargeFileCopyBenchmark` (`FILECAT_LARGECOPY_BENCH=<GiB per file>`, `_DIR`, `5abf5b2`, added here) | four files copied by CopyFile2 alone and by a copy job in five pairs of alternating order, the baseline with the job's profile (unbuffered over 256 MiB); the job's copy and rename calls timed, so its time outside the copy engine is reported; a strict (read-back) copy reported | **yes**, the median against 10% | ReFS block cloning and SMB server-side copy (§9: account for them) not covered; on a busy disk the pairs spread wider than the budget either way |
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
| `HexBenchmark` (added) | passed: first page at most 52.5 ms on a first open (the editor's protected open of the 4 TiB sparse file) and at most 1.0 ms on a second; seek p95 0.07 ms (viewer) and 1.33 ms (editor) in the sparse file's holes, 0.27 and 1.14 ms inside its data, 0.03 and 0.63 ms in the dense file; max 3.33 ms |
| `LargeFileCopyBenchmark` (added; 4 × 1 GiB) | see below |
| `ArchiveBenchmark` (273 MiB payload) | passed: ZIP list 72 ms, first 64 KiB of a 64 MiB member 41 ms cold and 2 ms warm, extract 31.3 s against `ZipFile` 12.4 s (2.52×, budget 4×), update scratch 132 MiB; TAR.GZ extract 1.22× `TarFile`, last member's first 64 KiB 1,040 ms cold and 418 ms warm (reported); 7z extract 27.7 s against native 7-Zip 15.3 s (1.81×, budget 5×); ISO first page 179 ms cold, 0 ms warm |

The window's own benchmark (`--benchmark`) was not run: it opens FileCat's window on the owner's desktop.

### Large copies (2026-10-01 and 02)

| Run | Volume | Median overhead (5 pairs) | Pairs | Notes |
|---|---|---|---|---|
| 1 | C: (system NVMe, GIGABYTE GP-GSM2NE3100TNTD) | 51.3% (failed) | −42.7% to +336.7% | **not a valid comparison**: the baseline was a buffered CopyFile2 (default flags) against the job's unbuffered copies. It returned in 1.24 to 8.80 s for 4 GiB (up to 3,308 MiB/s: the write cache, not the disk) while the job took 5.05 to 6.36 s. The baseline was then given the job's profile |
| 2 | C: | −15.9% | −44.7% to +362.1% | the job took 4.2 to 26.4 s, CopyFile2 4.6 to 7.6 s |
| 3 | C: | −1.5% | −29.9% to +48.5% | from the second pair on, most files took 3.6 to 9.1 s per GiB on either side, CopyFile2 alone included, after the two runs before had written about 110 GiB to that disk: the disk slowed, not the job |
| 4 | E: (Samsung 990 PRO) | 19.6% (failed) | −6.8% to +31.9% | CopyFile2 0.33 to 0.91 s per GiB; the job's calls were not yet timed, so where its extra time went is not known |
| 5 | E: | 5.1% | +0.5% to +19.8% | the job's copy-engine calls took 0.29 to 0.97 s per GiB, CopyFile2's alone 0.48 to 0.81 s; renames under 1 ms |
| 6 | E: | −0.3% | −16.2% to +2.3% | the job outside the copy engine: 13, 13, 12, 12 and 18 ms per 4 GiB job (0.4–0.6%), 6 to 12 ms of it before its first copy began; strict copy 7.40 s against about 2.9 s |

From runs 5 and 6: the job's own work (starting, the journal, staging, publishing) costs 12 to 18 ms per job of four
files. Inside the copy engine, the job's calls use the same flags as the baseline's and add a progress callback and a
staged name; their times spread as widely as the baseline's own, on both disks, so with five pairs on this machine the
graded median can land on either side of 10% (run 4). An isolated reference machine (§21.2) is where the budget is to be
graded.

## What is needed before acceptance

1. A ready-for-input measurement (an injected event answered by a usable panel), validated with a positive and a
   negative control, and a launcher for warm and cold launches with a documented cold-start method.
2. Input latency from the OS input event to the presented frame (or a documented equivalent the owner accepts), and
   physical autorepeat, per §9's definition.
3. A harness for the content-cache row (huge hex and large copies have one now); grading for the rows that only
   print; a cold-cache method for the hex and first-rows rows; the large-copy row's ReFS block cloning and SMB
   server-side copy cases.
4. The reference machine of §21.2, isolated from other work.
