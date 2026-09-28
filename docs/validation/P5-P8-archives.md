# P5 and P8: archive scan, first member, extraction, and scratch

Status: **executed 2026-09-28** on the TV-01 developer machine (Ryzen 9 5900X, NVMe, Windows 11, .NET 10). These are
the measurements the plan asks for at P5's exit ("measure first member, scan cost, extraction overhead, scratch use")
and P8's (per-slice performance and resources). The numbers describe that machine; real-time antivirus scanning of
20,000 new files makes single runs vary by up to 2×.

```
FILECAT_ARCHIVE_BENCH=1 [FILECAT_ARCHIVE_BENCH_MB=64] [FILECAT_ARCHIVE_BENCH_FORMATS=zip,tgz,7z,iso] \
  dotnet test tests/FileCat.Core.Tests --filter ArchiveBenchmark --logger "console;verbosity=detailed"
```

Payload: 20,000 notes of about 1 KiB in 200 folders and four 64 MiB files (half random, half text), 273 MiB in all.
Every extraction is an ordinary F5 job: staged names, journal, conflict checks, and download marks.

## Results

| Format | Scan (whole tree) | First 64 KiB of a large member, cold / warm | F5 of everything | Baseline | Scratch |
|---|---|---|---|---|---|
| ZIP, 132 MiB | 59 ms | 62 ms / 2 ms | 23.8 s | `ZipFile.ExtractToDirectory` 10.0 s (2.4×) | none |
| TAR.GZ, 138 MiB | 474 ms (reads the whole stream) | 848 ms / 367 ms (last member) | 21.9 s | `TarFile.ExtractToDirectory` 18.0 s (1.2×) | none |
| 7z solid, 128 MiB | 128 ms | 377 ms / 167 ms (last member) | 21.3 s | native 7-Zip 12.0 s (1.8×) | none |
| ISO/Joliet, 298 MiB | 183 ms | 141 ms / 0 ms (read in place) | – | – | none |

- **Pack** (Alt+F5, verified): 7.6 s against `ZipFile.CreateFromDirectory` 5.8 s.
- **Update** (add one file to the 132 MiB archive): 3.5 s. It needs scratch equal to the archive, beside it.
- **Cold** first-member times include reading the archive's structure. In solid and compressed-TAR archives a member
  deep inside costs decompressing everything before it; that is inherent to the formats.

**Budgets** asserted by the benchmark: extraction within 4× of the in-box extractors (5× of native 7-Zip), a warm first
page within 250 ms (plan §21.2, huge hex open), no scratch during extraction, and update scratch no larger than the
archive.

## What the measurements found and changed

The first run extracted the 20,000 members in about 127 s in every format, 7–12× the baselines. Fixed:

1. **Two disk flushes per file.** Extraction wrote the staging folder and the publish intent to the journal durably
   for every file. The staging folder is now recorded once per folder, and new items are group-committed like local
   copies (plan §9.3). 127 s → 98 s for ZIP, 38 s for TAR.GZ.
2. **'~' in staged names.** .NET treats any path with '~' as a possible 8.3 short name and expands it, which costs
   about 0.2 ms per file call on Windows. Staged files are now named `.fc-…` (leftovers of `.~fc-…` are still recovered).
   ZIP 98 s → 23 s.
3. **Whole members unpacked before the first byte.** F3 on a large member waited for all of it, F5 showed no progress
   and could not be canceled during that time, and each large member was written twice (spool, then destination).
   Members over 32 MiB are now decompressed as they are read (`ProgressiveContent`): copies decompress straight into
   their files, viewers keep what they read in a private spool, and files in disc images or plain TARs are read in place.
4. **Damage passed off as data.** .NET does not verify a ZIP member's CRC. FileCat now does, for every extraction and
   view, as do the checks that a member does not end short of its declared size.

Found on the way, beyond archives:

- Writing the download mark (Zone.Identifier) stamped files as modified now. Files extracted from downloaded archives,
  and downloads, lost their own times. The mark now keeps the file's time.
- Opening a file on a phone listed its whole folder again, so copying many photos was quadratic in the folder's size.
  Remembered object IDs are now confirmed with one property read. 1,000 files copy off a motorola edge 60 pro in 30 s.
- Archive members and recovered deleted files were found by searching their folder's list for every item; each is now
  a dictionary lookup.
