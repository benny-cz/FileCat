# ADR-08: Recovery engine and privilege split

**Status:** Decided (P10, 2026-09-28): disk images, and drives through the read-only helper session. The elevated read on
real removable and fixed drives is a manual check of the installed build (TV-09).

## Decision

**FileCat's own engines, read-only, MIT.** NTFS, FAT12/16/32, and exFAT are read by FileCat's own code in
`FileCat.Recovery` (about 1,500 lines), written from Microsoft's published FAT and exFAT specifications and the
documented NTFS on-disk structures. No recovery library was adopted: the candidates were GPL (TestDisk/PhotoRec) or
unmaintained, and the plan forbids copying GPL code into FileCat. MBR (with extended partitions) and GPT tables are
read the same way; a file system at the start of an image needs no table.

**Evidence, not percentages.** Each deleted item gets one of four states, with its reasons in words:

| State | Meaning |
|---|---|
| Recoverable | Every byte lies where the file system recorded it, in space nothing uses now |
| Partly lost | Some of that space is used by other data now, or cannot be read; those bytes come back as zeros and are named |
| Overwritten | All of it is used by other data now: nothing is offered, because what is there is not this item |
| Name only | No usable content location survives (and folders, which have no content of their own) |

How far each file system's evidence reaches:

- **FAT** frees a deleted file's cluster chain and overwrites its first short-name byte. FileCat reads the content as one
  continuous run from the recorded first cluster and says so. The long-name checksum validates the long name, and a name
  without one shows its lost letter as `_`.
- **exFAT** clears the in-use bits of the entry set and the allocation bitmap, and keeps whether the file was contiguous.
  A fragmented file's FAT chain usually survives, so FileCat follows it when it is complete and consistent.
- **NTFS** keeps a deleted record's names, parent reference, times, and data runs until the record is reused, and small
  files keep their content in the record. Parents are matched by record and sequence number, so a reused folder never
  adopts the wrong children (they go to "Orphans"). Compressed files are decompressed with FileCat's own LZNT1 reader,
  one compression unit at a time: a unit with any cluster reused is lost as a whole, since compressed data cannot be
  read in part; sparse units are zeros by definition. EFS-encrypted content is listed but not recovered.
- Allocation is judged against the volume's own bitmap or FAT. "Free" can still hold other data that was written and
  freed since; the state says "unallocated now", which is all the evidence shows.

**The truthfulness rule, tested.** Every byte FileCat hands out as recovered must be the file's own, and every lost byte
is declared: content sources implement `IPartialContent`, the viewer shows the lost ranges above the text, and F5 finishes
with a per-file warning naming them. Tests check this byte by byte on images that real drivers produced.

**Where parsing runs.** Parsing is managed, bounds-checked, and fuzzed (21,000 damaged images per deep run): any
unexpected structure ends in a "damaged volume" report, never a crash. It runs in FileCat's process with the user's
rights, the same stance ADR-07 takes for managed archive readers, because a disk image is an ordinary file the user can
read. The source is opened read-only and shared; nothing in the engine can write, and tests confirm the image's bytes
and time stamp are unchanged after a scan and a recovery.

**Drives: a read session through the administrator helper.** Raw volumes need administrator rights on Windows. FileCat
asks the ADR-14 helper for a plan with a single `ReadDevice` step, so the same checks apply as for every elevated plan:
the installed program folder, a hashed single-use plan from the installed FileCat of the named user, and the helper's own
consent window, which names the drive and says nothing is written. After consent, the helper:

- opens only that device (a `\\?\Volume{…}` or `\\.\PhysicalDriveN`, matched exactly), for reading;
- creates one pipe with a random name that only the requesting user can open, and serves only the requesting
  process ID (a second instance or another client ends the session);
- answers three requests: describe (length, sector size), read (at most 4 MiB, rounded to whole sectors, clipped at the
  end), and close. There is no request that writes or names another device, and no parsing: the engine runs in FileCat,
  unelevated;
- exits when FileCat closes the session, disconnects, or exits.

Reread scans again through the same session, so one approval covers a drive until its recovery view is closed. Drive
locations are not restored at startup, so FileCat never asks for approval that nobody requested.

**A drive is recovered only to another disk.** Before any copy starts, the source may refuse the destination
(`ResourceProvider.CheckTransferDestination`): FileCat compares physical disk numbers
(`IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS`) and refuses a destination on the drive's disk, and also one whose disk it cannot
determine. Network shares are accepted. The scan's first message says the drive is in use: it is a snapshot, and new
writes can overwrite what is listed.

## Consequences

- Disk images (raw `.img`/`.dd`/`.bin`, fixed `.vhd`) open with Commands → Find deleted files (disk image or drive). Dynamic
  VHDs and VHDX images are refused with a conversion hint.
- ext4 and APFS stay research tracks, as the plan says; no undelete is promised for them.
- Fragmented FAT files are the known weak spot: FAT keeps no record of their pieces once deleted. The state says "Partly
  lost" and names the bytes, instead of guessing.

## Validation

`eng/make-recovery-fixtures.sh` builds the images on a disposable runner with the Linux kernel's vfat and exfat drivers
and ntfs-3g (workflow "Recovery fixtures"). Tests cover recoverable, partly lost, overwritten, fragmented, resident,
nested-folder, Unicode, and long-name cases on FAT12/16/32, exFAT, and NTFS, plus MBR and GPT disks; the job path; the
UI; and fuzzing. The read protocol runs over a real named pipe with a file standing in for the drive (exact bytes at any
offset and size, refused requests, a full scan); plan validation refuses other paths, other session names, extra fields,
and combined steps; the disk comparison is tested on the machine's own volumes; and a drive's recovery to the same disk
is refused before anything is written. TV-09's overhead: five scans with a 70 KB recovery take about 8 ms through the
helper's pipe against 4 ms directly, a small factor next to device I/O and the one approval per session.
