# Principal-comparison allocation CI probe — 17 November 2026

The [main CI run for 8ff36f3e](https://github.com/benny-cz/FileCat/actions/runs/44142902441) failed its Windows Core project because `MetadataOrderingSnapshotTests.Canonical_principal_comparison_does_not_allocate_for_each_comparison` reported **656 allocated bytes** for 10,000 comparisons against a **128-byte** limit. Ubuntu, macOS and Windows ARM64 jobs passed. The CI log establishes the allocation count, not which runtime operation allocated those bytes; this is not presented as proof of a particular JIT event.

The comparer implementation is unchanged. The test now runs its exact method in an isolated xUnit process with tiered compilation disabled for that child only. Warmup remains outside the sample; the **128-byte limit is unchanged**. The parent checks the child's exit, a single XML test with the expected method/result, and its measurement marker. Timeout/cancellation kills and reaps an owned child before returning.

A second child intentionally splits the SID on every comparison and keeps the allocated result alive. The parent requires exit 1, exactly one failed test, the allocation measurement and an `InRangeException` from the allocation budget. A missing test, unrelated failure or skipped probe cannot satisfy this negative control. This distinguishes a stable measurement from a test that no longer detects per-comparison allocation.

| Supplied test machine | Full `MetadataOrderingSnapshotTests` class | Actual comparer, 10,000 calls | Deliberate allocating control |
|---|---|---|---|
| Disposable Windows VM, normal user | **12 passed**, no failures/skips | **0 bytes**, passed | **2,960,000 bytes**, rejected |
| Disposable Ubuntu VM | **12 passed**, no failures/skips | **0 bytes**, passed | **2,960,000 bytes**, rejected |
| Mac | **12 passed**, no failures/skips | **0 bytes**, passed | **2,960,000 bytes**, rejected |

The Core test assembly built with zero warnings/errors. No tests ran on the workstation. The complete CI suite is not claimed to have passed from these focused runs; the next pushed main run must establish that result.

Inputs SHA-256: `MetadataOrderingSnapshotTests.cs` = `AB0CC6594C41E5D014BCD098B9A02AFC0B8627E635B889A171DD72CEA1F218DB`; `MetadataOrderingAllocationProbe.cs` = `838920E501FB066840F4403B45636EA2DFA1CC9E2CAB887223FF4DABA15B1771`; `.tmp/filecat-principal-allocation-core.tar.gz` = `13D7DAB75169D16DA236558A56CCEC750012110A6E30324712FF8664EF0B7701`.

Ignored local evidence: `.tmp/filecat-ci-44142902441-windows.log`, `.tmp/filecat-principal-allocation-{windows,ubuntu,mac}.xml` and `.tmp/filecat-principal-allocation-windows-results.log`. XML output records both the passing measurement and the rejected allocating control. The Windows log ends `Probe exit: 0` / `FILECAT_PRINCIPAL_ALLOCATION_FINISHED`.
