# P10: recovery scan, preview, and read-broker measurements

Status: **automated parts executed 2026-09-28**. The elevated read of a real drive in the installed build is still
manual (a release gate). The numbers describe the machines named below. They are not guarantees (plan §21).

## Scan and preview benchmark (plan P10: "benchmark scan/preview")

`eng/make-recovery-bench.sh` builds two 512 MiB images with the real Linux drivers (ntfs-3g, vfat). Each holds
20,000 small notes in 200 folders and 200 photos of 256 KiB, and half of everything is deleted. The workflow
"Recovery fixtures" (manual) builds them on a GitHub `ubuntu-latest` runner and runs `RecoveryBenchmark` right away.

```
sudo eng/make-recovery-bench.sh /tmp/bench
FILECAT_RECOVERY_BENCH=/tmp/bench dotnet test tests/FileCat.Core.Tests --filter RecoveryBenchmark --logger "console;verbosity=detailed"
```

| Image (run 36428985808) | Deleted files found | Scan | Scan result in memory | First 64 KiB of a deleted photo | Whole photo (256 KiB) |
|---|---|---|---|---|---|
| NTFS, 4 KiB clusters | 10,100 | 67 ms | about 4.1 MiB | 0.1 ms | 0.1 ms |
| FAT32 | 10,100 | 84 ms | about 4.1 MiB | 3.5 ms | 0.1 ms |

The images had just been written, so the operating system's cache held them: the numbers measure FileCat's engines,
not the medium. On a physical drive the medium's speed and the broker below come on top. A scan reads only file system
structures (the MFT, or FAT tables and directories), so its cost grows with the number of entries, not the volume size.

**Budgets** in the test: a scan under 30 s and a first preview under 1 s. They are generous for shared CI machines and
still catch a regression of an order of magnitude.

## TV-09: the read broker's cost

`DeviceReadTests` (Windows integration tests) scans the fixture images five times and reads their files whole,
once directly and once through the `ReadDevice` plan of the ADR-14 helper over its private pipe.

| Run | Direct | Through the helper's pipe |
|---|---|---|
| Typical (developer machine, Ryzen 9 5900X, NVMe) | 4 ms | 8–9 ms |

About 2× on cached reads, which is small next to a real drive's own latency. ADR-08 keeps the split: parsing never
runs elevated, and the helper reads bounded, sector-aligned ranges of one device and never writes.

## Correctness evidence (automated)

- Fixture images from the real drivers (`eng/make-recovery-fixtures.sh`): FAT12, FAT16, FAT32, exFAT, NTFS (with
  compression), and MBR and GPT disks. Recovered bytes are compared with what was written; bytes a file lost are
  declared as lost and never passed off as data.
- Fuzzing: 1,400 damaged images on every test run (200 per fixture), 21,000 in a deeper run
  (`FILECAT_FUZZ_ROUNDS=3000`). Damage becomes a report, never an unexpected exception.
- Helper tests: exact reads at any offset and size, a scan through the helper equal to a scan of the file, a read
  plan naming one device only, unknown requests ending the session, and destinations judged by physical disk.

## Pending (manual)

- Elevated read of a real drive in the installed build: consent prompt, a scan of a secondary USB drive, and recovery
  to another disk (refused onto the same physical disk).
