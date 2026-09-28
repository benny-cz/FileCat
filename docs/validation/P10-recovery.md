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

## A real drive: a USB stick that held Windows setup (2026-09-28)

An 8 GB USB stick (FAT32, 4 KiB clusters, 1.9 million clusters), onto which a Windows installation medium had been
written, then everything deleted in Windows. `LiveDriveRecoveryTests` (Windows integration tests, opt-in) scans it
through the helper's read protocol, recovers every signed program and library to another disk, and checks each by its
Authenticode signature, which verifies only over the exact bytes that were signed. The test refuses any drive but a USB
disk with the serial number given.

```
FILECAT_RECOVERY_LIVE=G: FILECAT_RECOVERY_LIVE_SERIAL=<disk serial> dotnet test tests/FileCat.Platform.Windows.Tests --filter LiveDriveRecoveryTests --logger "console;verbosity=detailed"
```

The first run found a truthfulness bug: of 58 programs FileCat called recoverable, 23 were other data. Windows erases
the upper half of a deleted FAT32 entry's first cluster number, so every file beyond cluster 65,535 was read from the
wrong place. FileCat now weighs each place the lower half allows: right where the item listed before it ends or right
before the one listed after it starts (the half that is left confirms either), a folder's "." and ".." entries, and
the data's own signature. What none of these settles is shown as **Uncertain**, with the reason, and F5 says so for
each such file. A deleted folder's listing is also followed into its next cluster where the files written meanwhile end.

| Run | Deleted files found | States | Signed programs recovered | Signatures |
|---|---|---|---|---|
| Before | 143 | 142 Recoverable, 1 Partly lost | 58 (28.5 MiB) | 31 valid, 23 not even a program, 4 unsigned (catalog-signed .mui) |
| After | 431 | 427 Recoverable, 4 Uncertain | 153 (35.7 MiB) | 101 valid, 2 valid but test-signed, 50 unsigned (.mui), **none altered** |
| After, free space searched | 1,030 (5.3 GiB) | 1,024 Recoverable, 5 Uncertain, 1 Partly lost | 296 (109.8 MiB) | 236 valid, 2 valid but test-signed, 58 unsigned (.mui), **none altered** |

The quick scan took 2 s, and recovering the 153 files 1.4 s, through the helper's pipe. Unit tests build FAT32
volumes in memory with the same deletions (`ErasedFatStartTests`).

The `sources` folder's listing goes on in two clusters near the end of the volume, which nothing FAT keeps points to:
the quick scan lists 59 of its items and says its list may go on. Searching free space (Find deleted files again,
inside the view) read all 7.4 GiB in 341 s (21.8 MB/s, the stick's speed through the helper's pipe) and found them,
placed by their subfolders' ".." entries: 205 items, among them both parts of the split install image (placed by
their WIM signature). The first part is honestly *Partly lost*: read as one run from its start it would go past the end
of the volume, so it was stored in pieces. The search asks more of a cluster than the quick scan does (every entry
marked deleted, every short entry dated): before that, x64 machine code spelled a few "listings".

## Pending (manual)

- Elevated read of a real drive in the installed build: consent prompt, a scan of a secondary USB drive, and recovery
  to another disk (refused onto the same physical disk).
