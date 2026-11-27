# E-I688 — FileCat refused to pack browser downloads: it could not read the download mark browsers end with a NUL

**Found:** 2026-11-27, by the owner's GA PC checklist on `v1.0.0-rc.9`, step G7 line 5 ([checklist](../FILECAT_1_0_RC3_GA_PC_CHECKLIST.md)). **Severity:** Medium. On Windows no browser-downloaded file could be packed or moved into an archive, and no downloaded archive could be updated. Nothing was written or lost: FileCat refused. Its message blamed the archive, which did not exist yet. **Disposition:** Must fix for 1.0.0. The owner chose rc.10 with the fix ("Fix in rc.10", 2026-11-27).

## What happened

In G7 the owner packed three downloaded files in `C:\test`, each with its download mark (the `Zone.Identifier` stream), into a new `test.7z`. FileCat reported:

"Create 7z archive “test.7z”: failed — This 7z archive contains metadata FileCat cannot preserve when saving. It is read-only here. You can still extract supported files with F5 (test.7z)."

The operation report (captured 11:38:15 UTC) names `C:\test\test.7z`, the requested action ArchiveUpdate, and no item done.

## Why

- **The marks:** browsers end the mark they write with a NUL: `[ZoneTransfer]`, `ZoneId=3`, `ReferrerUrl=…`, `HostUrl=…`, then a NUL. On the release session's host, 41 of 41 downloaded files that carry a mark end that way, PDFs included.
- **FileCat's reader:** `ArchiveWindowsOriginZone.Parse`, which every archive write uses to carry the mark over (creation, update, move, content restoration), refused any control character. So it refused every browser download's mark, and the write stopped with the generic text for metadata it cannot keep.
- **What Windows reads:** Windows' own reader (`PersistentZoneIdentifier`) reads these marks as their zone, 3. With a NUL inside and a second `ZoneId=0` after it, Windows reads zone 0, so a NUL that does not end the mark is still ambiguous.

## The fix

`911fe5f5` on `fix/i688-download-mark-nul`, for rc.10:
- NULs at the end of a mark end it. A NUL anywhere else is still refused.
- A refused mark now says what it is: "FileCat cannot read the download mark (Zone.Identifier) on a selected file or on this archive. It keeps such marks on the archives it writes, so it stopped."

## Checks

- **Tests, at standard rights (medium integrity) on the release session's host, at `23f39bbe`:**
  - Core 180 passed, 1 skipped (Unix error codes). Among them, marks that end in one or more NULs (CRLF, LF and UTF-16) keep their zone and every byte; NULs elsewhere are refused with the new text; a new archive from browser-marked downloads gets their zone.
  - Windows 81 passed: Windows' own reader is the oracle for three browser shapes, and FileCat raises each exactly.
  - App 107 passed, 59 skipped (other platforms and opt-in lanes). The standard-user native creation lane, whose mark is now a browser's, is opt-in and did not run.
- **The owner's build:** on the owner's own build of the branch at `23f39bbe`, packing downloaded files works ("I688 fixed", 2026-11-27).
- **Still to come:** rc.10's tagged run, and G7 line 5 again on rc.10.
