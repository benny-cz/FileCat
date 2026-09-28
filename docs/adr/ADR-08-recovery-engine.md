# ADR-08: Recovery engine and privilege split

**Status:** Decided for disk images (P10, 2026-09-28). Devices follow through the read-only broker described below.

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
  adopts the wrong children (they go to "Orphans"). Compressed and EFS-encrypted content is listed but not recovered.
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

**Devices (next).** Raw volumes and disks need administrator rights on Windows. They will be read through a narrow broker:
started for one device identity, with a read-ranges verb only (no write verb exists), bounded request sizes, and no
parsing: parsing stays in FileCat, unelevated. Recovering from a device additionally requires a destination on another
physical disk, because writing to the source can overwrite exactly what is being recovered.

## Consequences

- Disk images (raw `.img`/`.dd`/`.bin`, fixed `.vhd`) open with Commands → Find deleted files in a disk image. Dynamic
  VHDs and VHDX images are refused with a conversion hint.
- ext4 and APFS stay research tracks, as the plan says; no undelete is promised for them.
- Fragmented FAT files are the known weak spot: FAT keeps no record of their pieces once deleted. The state says "Partly
  lost" and names the bytes, instead of guessing.

## Validation

`eng/make-recovery-fixtures.sh` builds the images on a disposable runner with the Linux kernel's vfat and exfat drivers
and ntfs-3g (workflow "Recovery fixtures"). Tests cover recoverable, partly lost, overwritten, fragmented, resident,
nested-folder, Unicode, and long-name cases on FAT12/16/32, exFAT, and NTFS, plus MBR and GPT disks; the job path; the
UI; and fuzzing. TV-09 (broker overhead) is measured when devices arrive.
