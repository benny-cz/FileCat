# AC-59 RAR chained-filter work execution — 29 October 2026

FCSEC-056 / RAR-SEC-003 was measured at source baseline `be40902e`: 128 packed bytes queued 8,192 E8 filters over one 4 MiB block. The exact public `ArchiveProvider.OpenContent` took 23,767 ms before returning valid checksummed content. The owned baseline fixture, construction and limits are in [the probe receipt](E-FCSEC-20261029-RAR-filter-work.md). This execution used that fixture as a permanent regression test and ignored `FileCat.zip`.

## Corrected invariant

`RarWindow` now counts the byte length of **every** pending transform before running a same-start chain, including E8 transforms that leave the block unchanged. Checked accumulation refuses any chain that would take the decoder's aggregate filter work beyond 1 GiB. The counter belongs to the window, so it persists across solid members and resets only with a new decoder run. The filter loop checks the member cancellation token before each transform. Both `ReadOutput` call paths in `RarMemberStream` mark the stream broken on a filter exception; the reader cannot resume that partial decoder. Refusal uses RAR's explicit unsupported/damaged `InvalidDataException` classification.

The 1 GiB bound admits the owned 1,024-filter × 1 MiB healthy case exactly, while rejecting the 8,192-filter × 4 MiB case before its first transform. It limits cumulative transform bytes, not wall time or arbitrary work in other decompression routines. The bound is intentionally per decoder/solid run rather than per returned member.

## Finite controls and result

`RarFilterBudgetTests` builds owned checksummed RAR 2.9 archives with the test PPMd encoder and exercises **both** `RarReader.Open` and `ArchiveProvider.EnumerateAsync` → `OpenContent` for every row. Admitted cases read to EOF, verify CRC through the reader/provider and compare every decoded byte to the expected zero array. The refused case requires a damage-classified `InvalidDataException` containing `filter-work limit`; the public call returns no content, and no temp-stage file remains. The test deletes each owned fixture directory.

| Filters | Packed member | Decoded block | Attempted filter bytes | Direct reader | Public provider | Result |
|---:|---:|---:|---:|---:|---:|---|
| 0 | 19 B | 1 MiB | 0 | 42 ms | 18 ms | Byte exact, accepted |
| 64 | 91 B | 1 MiB | 64 MiB | 48 ms | 45 ms | Byte exact, accepted |
| 256 | 92 B | 1 MiB | 256 MiB | 149 ms | 149 ms | Byte exact, accepted |
| 1,024 | 95 B | 1 MiB | 1 GiB | 557 ms | 561 ms | Byte exact, accepted |
| 8,192 | 128 B | 4 MiB | 32 GiB requested | 10 ms | 10 ms | Explicit refusal before transform; no content |

Times are the corrected-source Windows x64 Release run on the same host as the baseline, not cross-machine performance guarantees. The public adverse route is well under AC-59's two-second local target. A separate `RarWindow` test exhausts the 1 GiB budget on one simulated member, then refuses a one-byte filter at the next member boundary without advancing the window read position or counter.

The selected Release regression group covered `RarReaderTests`, `ArchiveFormatTests`, `ArchiveMultipartRarTests`, `ArchiveSourceReplacementTests`, `ArchiveSourceCaseTests` and `RarFilterBudgetTests`: **259 total, 240 passed, 19 skipped, 0 failed**. These include standard RAR E8/Delta/Audio, solid RAR, 7z, TAR, single-stream compression, multipart RAR and archive source-binding controls. The skipped cases require a case-sensitive filesystem or Unix FIFO behavior unavailable on this Windows host; those passed in their earlier platform receipts and were not reopened by AC-59. The full `dotnet build FileCat.slnx -c Release --no-restore --verbosity quiet` succeeded with **0 warnings, 0 errors**.

## Disposition and stop

**FCSEC-056 is contained at the direct-reader and public-provider source boundary.** The exact previously slow member now returns an explicit, prompt refusal, and ordinary filtered and cross-format controls remain green. The installed viewer, packaged release candidate and other operating systems were not run in this execution; this receipt makes no qualification claim for them. Stop this security slice here. Reopen only on a corrected-source failing fixture, a concrete way to exceed the aggregate work limit or return partial content, a consequential cancellation failure, or an approved change to the limit. Do not repeat earlier passed security scenarios without their own consequential trigger.
