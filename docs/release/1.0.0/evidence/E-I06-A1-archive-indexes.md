# E-I06-A1 — indexes of earlier archives within a limit

I06's last clause ("some operations materialize child lists/payloads … measure reachable aggregate workloads") and V12's
million-entry listings, for archives. Companion to E-I06-P1 (page caches).

## What was there

Each archive provider (`ZipProvider` for ZIP, `ArchiveProvider` for TAR, 7z, RAR, ISO and the rest) keeps an index of
every archive it opened, so that going back into one is quick: the whole member list as nodes by folder, and for ZIP
also .NET's entry for every member of the central directory. Each kept the last **eight**, by count only, and a cache
hit did not count as use (the eight were the eight opened last, not used last).

**Measured** (`ArchiveIndexScaleTests`, `FILECAT_ARCHIVE_SCALE`; empty members with 30-character names in a thousand
folders, managed memory after a full collection, this host):

| Members | ZIP: size, opened in, index holds | TAR: size, opened in, index holds |
|---|---|---|
| 200,000 | 25 MiB, 0.45 s, **114 MiB** (598 B a member) | 292 MiB, 2.45 s, **59 MiB** (312 B) |
| 1,000,000 (each provider's member limit) | 129 MiB, 2.18–2.20 s, **557 MiB** (584 B) | 1,464 MiB, 10.4–10.5 s, **291 MiB** (305 B) |

Eight such ZIPs and eight such TARs opened one after another would have stayed in memory together: about 4.4 GiB and
2.3 GiB.

## The fix (`6b37c41`, `319a38c`)

- Each index estimates its size as it is built: ZIP 460 bytes a member of the central directory plus 4 a name
  character, TAR and the others 185 plus 4. For the million-member archives above: **553 MiB** and **290 MiB**
  estimated, against 557 and 291 measured.
- A provider keeps the indexes of earlier archives while they total at most 256 MiB (`RetainedIndexLimitBytes`), eight
  at most as before; past that the least recently used go first, and listing an archive again counts as use. The two
  used last stay whatever their size, as the two panels may be showing them (the first version, `6b37c41`, exempted
  only the index just opened: two large archives in the two panels would have pushed each other out at every switch;
  corrected in `319a38c` before anything was recorded on it).
- Letting an index go does not break a viewer reading one of its members: an index lent to a viewer (a member larger
  than 32 MiB, read as it is decompressed) closes when the viewer is done, as before.

## How it was checked

| Check | Result |
|---|---|
| `ArchiveIndexBudgetTests`, ZIP and TAR: three archives of 5,000, 6,000 and 5,500 members, room for two | the first two kept; after the first is listed again, the third pushes out the second (the least recently used), not the first; the kept estimates add up to the two kept |
| The same with a limit below one index | the two used last are kept, the one before goes |
| A 35 MiB member being read while its archive's index is let go (limit of one byte, two other archives opened) | read to the end, byte for byte |
| Negative control: eviction by count only, as before | all three tests fail (on both versions) |
| Negative control: a cache hit not counted as use | both ordering tests fail |
| Core suite / App suite | 738 / 220, 0 failed |

## Not covered here

- The two archives used last can each be as large as the member limit allows (a million members: 557 MiB for a ZIP);
  panels may show them, so they stay. A more compact index (not keeping .NET's entries beside FileCat's nodes) is not
  attempted.
- A third archive opened while two large ones are kept pushes out the older of them; its index is read again on
  return (2.2 s for a million-member ZIP here).
- The limit is per provider (two providers, so up to 512 MiB of earlier archives' indexes together, the order of the
  plan's shared index budget for listings); it is not in the settings file.
- Other memory that grows with what is open: pictures the quick view decodes (one at a time), icon caches: V12.
