# FCSEC-056 / RAR-SEC-003: chained RAR filter work — 29 October 2026

## Source and fixture

Baseline: `be40902e`, Release .NET 10.0.14 on Windows x64. The relevant RAR/archive source has no change from `3bb3d995` through this HEAD. The [exact probe source](E-FCSEC-20261029-RAR-filter-work-probe.cs.txt) was a temporary test in `FileCat.Core.Tests` using its existing `Rar3PpmdTestStream` and RAR 4 block writer. It generated **owned** RAR 2.9 archives with a checksummed file header and a zero-filled member with the correct CRC-32. Each filter record named the same zero-based start and the same output length; the first installed the standard RAR 3 E8 program, and subsequent records reused that slot. Each E8 pass scanned the block but left the zero data unchanged. The PPMd encoder compressed the repetitive records and zero-run symbols into the packed byte counts below. Fixtures were deleted by the probe. No shipped archive or user data was modified.

The exact public-route case queued 8,192 filters on one 4 MiB block, the maximum count `RarWindow.AddFilter` admits. The test enumerated the archive with `ArchiveProvider.EnumerateAsync`, then called `ArchiveProvider.OpenContent` for its one file. It returned content with a valid size and CRC after approximately 24 seconds. The smaller controls used `RarReader.Open` and read the member to EOF. The zero-filter control and each filtered case decoded the same data. Command after placing the linked probe source in the test project: `dotnet test tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RarFilterAuditProbeTests --logger "console;verbosity=detailed"`. The sandboxed MSBuild project-reference evaluation stopped without diagnostics, so the owned-fixture run used approved outside-sandbox execution. One initial probe version failed to compile due an `int`/`uint` argument and an async test warning; the corrected run below passed. The temporary test file was then removed from the project.

```text
Test run for E:\FileCat\tests\FileCat.Core.Tests\bin\Release\net10.0\FileCat.Core.Tests.dll (.NETCoreApp,Version=v10.0)
  Passed FileCat.Core.Tests.RarFilterAuditProbeTests.Bounded_filter_work_measurement [24 s]
  Standard Output Messages:
 filters=0 packed=19 decoded=1048576 elapsedMs=43
 filters=64 packed=91 decoded=1048576 elapsedMs=65
 filters=256 packed=92 decoded=1048576 elapsedMs=201
 filters=1024 packed=95 decoded=1048576 elapsedMs=573
 filters=8192 packed=128 decoded=4194304 elapsedMs=23767
Test Run Successful.
Total tests: 1
```

## Interpretation and limit

The 8,192-filter case is **High availability**: about 128 bytes of packed member data induce roughly 32 GiB of repeated in-process block scans before one 4 MiB member is returned. This is work, not a measured memory allocation or an out-of-bounds read. `RarWindow.ApplyFilters` executes every queued transform before `ReadOutput` returns, while `ArchiveIndex.Extract` applies `MemberLimits` only after a read returns. `MemberLimits` also starts its expansion-ratio check above 64 MiB, so this 4 MiB case is admitted. `ArchiveProvider.OpenContent` uses `CancellationToken.None`; no UI, process crash, repeated-request test or other platform was exercised. The elapsed times are host measurements, not fixed cross-machine guarantees.

The earlier RAR-SEC-001/002 findings and their corrected evidence remain separate: this route uses one complete member, no solid predecessor and no special volume file. The shared archive-open change was source-reviewed for TAR, single-stream compression, 7z and disc images. The RAR 3/7z shared PPMd model was also inspected for its order, memory and context-walk limits; no new 7z PPMd adverse fixture was run. This probe does not prove a similar filter-work issue in those formats.
