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
FILECAT_RECOVERY_LIVE=G: FILECAT_RECOVERY_LIVE_SERIAL=<disk serial> FILECAT_RECOVERY_LIVE_BYTES=<Get-Disk byte capacity> FILECAT_RECOVERY_LIVE_EVIDENCE=<absolute owned folder on another disk> dotnet test tests/FileCat.Platform.Windows.Tests --filter LiveDriveRecoveryTests --logger "console;verbosity=detailed"
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

An image of the stick in this state (7.8 GB, SHA-256 `42eb936a…52cb00`) keeps the scenario:
`FILECAT_RECOVERY_LIVE_IMAGE=<image>` runs the same checks on it, with the same results (the free-space search takes
1.4 s from a local disk).

## A real drive with known contents (destructive, 2026-09-28)

`LiveDriveScenarioTests` (opt-in: `FILECAT_RECOVERY_LIVE_DESTRUCTIVE=1` on top of the shared physical-device guard) formats the
stick, writes a 300 MiB file that stays (so what follows lies beyond cluster 65,535), a folder of 300 files with long
names and a subfolder (a listing of many clusters among their data), three large files, and files around a deleted
gap; Windows deletes them (`Directory.Delete`, `File.Delete`), and the volume's cache is flushed. FileCat scans the
drive through the helper's read protocol and every recovered byte is compared with what was written.

The physical harnesses now require the exact authorized serial and physical byte capacity, plus an absolute evidence
folder marked `.filecat-owned` on another backing disk. `LiveUsbGuard` records the device instance, volume GUID,
partition bounds and disk identity; it rejects boot/system targets, ambiguous extents and source disks backing the
user profile, application binaries, temporary files or evidence. Identity is rechecked before each format/delete
phase and before source access. Formatting and file mutations address the pinned volume GUID. The destructive
harness retains the generated files' sizes, roles and expected SHA-256 hashes outside the source. These guard
checks do not qualify an installed helper, native UI, or a source-device zero-write result. The opt-in preflight
`LiveUsbGuardPreflightTests` checks identity/topology only and opens no raw reader.

The generated-fixture checker compares every byte outside the exact union of declared missing ranges; a gap no
longer excludes its whole 4 KiB block. A Recoverable claim requires the complete original bytes with no missing
range; partial claims must retain the known file length and match every claimed byte. Thirteen controlled cases
cover adjacent corruption, truncated prefixes, overlapping gaps and invalid ranges. The original checker fails ten
of these cases; the corrected checker passes all thirteen. Stronger physical rerun evidence is recorded separately
from the earlier component runs and does not replace installed-helper or zero-source-write qualification.
The destructive scenarios also retain each observed item's state, read length, SHA-256 and exact missing ranges
off-source, so complete recoveries can be independently compared with the generated expected-file manifest.

| File system (Windows format) | Scan | Deleted files back exactly | Space reused by a later file |
|---|---|---|---|
| FAT32, 4 KiB clusters | 0.9 s | 325 of 325 | *Overwritten*; its bytes not passed off |
| exFAT, 32 KiB clusters | 0.1 s | 325 of 325 | *Overwritten* |
| NTFS, 4 KiB clusters | < 0.1 s | 325 of 325 | not listed: its MFT record was reused too |

Windows' FAT and exFAT allocators did not fill the deleted gap here, so the "fragmented" file was stored in one piece;
FAT's honest *Partly lost* for files in pieces is covered by the fixture images from the Linux driver.

## Lost partitions (D-46, automated, 2026-09-29)

`PartitionSearchTests` damage the driver-made fixture disks the way it happens on real disks and check what comes back:

| Damage | Found |
|---|---|
| Every MBR entry deleted | Both volumes (FAT16 at 1 MiB, exFAT right after it), whole file systems, deleted files byte for byte |
| GPT header at the start erased | Both volumes from the backup GPT at the end |
| Both GPT headers erased | Both volumes by the quick search, with a warning about the table |
| First sector of an NTFS, FAT32, or exFAT volume erased | The volume from its backup boot sector (NTFS's last sector, FAT32's copy at sector 6, exFAT's backup boot region), existing and deleted files byte for byte |
| No table, and an NTFS volume at 1 MiB with its first sector erased | Nothing by the quick search; the deep search finds it by its backup boot sector, with progress to 100% |
| exFAT partition deleted and a newer partition over most of it | The lost volume, its files under the newer partition Uncertain, naming it |
| Empty disk, 8 MiB of random bytes, intact MBR and GPT disks with the deep search | Nothing more than what is there |

The recovery fuzzing (21,000 mutated images) runs through the same search on every image.

## Drives on Linux and macOS (D-47, 2026-09-29)

`UnixDeviceTests`: the D-Bus Hello and OpenDevice arguments byte for byte against the specification; UDisks2 object
path escaping; a sysfs tree and mountinfo (escaped spaces, a FUSE ntfs-3g mount found by its source); a diskutil
property list; whole-sector reads of a raw device giving the same bytes at 200 random ranges. On Linux (WSL, and CI)
and macOS (CI): a descriptor passed over a real socket pair arrives open; on Linux the system bus answers calls and
errors, and UDisks2 (where it runs) refuses an unknown drive with a reason. End to end, the FAT16 fixture attached as a
loop device (WSL `/dev/loop0`; CI's Linux runner) or a raw disk image (CI's macOS runner, `/dev/rdiskN`) is read
through the same source as a drive, and its deleted `report.txt` comes back byte for byte. The polkit and authopen
prompts themselves need a person at a desktop (pending below).

## Pending (manual)

- D-47's approval prompts on a desktop: polkit through UDisks2 (GNOME, KDE) and authopen on macOS, approved and declined.
- Elevated read of a real drive in the installed build: consent prompt, a scan of a secondary USB drive, and recovery
  to another disk (refused onto the same physical disk).
