# Bounded Linux extent details

The Linux metadata-only full record can collect the main-stream extent map of a pinned regular file. It reopens the live `O_PATH` descriptor through the process descriptor directory on procfs, verifies the inode and kind, and issues `FIEMAP` with synchronization disabled. The summary does not open a mapping descriptor. Symlinks and special files stay outside the collector. [Linux kernel FIEMAP documentation](https://docs.kernel.org/filesystems/fiemap.html). [Linux procfs descriptor semantics](https://man7.org/linux/man-pages/man5/proc_pid_fd.5.html).

The collector validates cursor progress, counts and offsets and stops at 256 requests or 100,000 records. A later error retains the validated prefix and reports a partial inventory. Completion describes the returned map. A complete record can still show **Physical location unavailable** and its raw flags. Sharing appears only as evidence reported by returned records.

Ubuntu on ext4 with kernel 7.0.0 passed all 26 focused tests. The native fixture produced over 512 sparse extent records, then replaced the original pathname with a FIFO. The still-held inode returned a complete map, and inspection of the new FIFO omitted the collector. macOS ARM64 passed 24 tests and skipped the two Linux-specific fixtures. The guests retain `.tmp/fiemap-details/results.xml`. Other Linux filesystems and architectures, Windows hard-link names and Windows allocated byte ranges still need their own evidence.

## Btrfs shared and encoded extents

Ubuntu x64 passed a separate fixture on a private read-only Btrfs loop image. `filefrag` independently reported the same shared extent for a source file and its reflink, an unshared extent for a byte-for-byte ordinary copy, and the encoded flag on all six extents of a zstd-compressed file. FileCat reported sharing only for the source and reflink. It kept the raw flags and reported the physical location as unavailable for the encoded extents. The summary omitted the extent inventory, and the full record remained metadata-only. Checksums for all four fixture files matched after inspection.

The result is `extents.trx` under `~/FileCatValidation/filecat-btrfs-extents-984b6f47-r1` on the Ubuntu guest. The compression policy and containing subvolume ID have their own [Btrfs fixture](2026-11-18-linux-btrfs-properties.md).

## XFS sharing, sparse ranges, and unwritten allocation

Ubuntu x64 with kernel `7.0.0-43-generic` ran the same held-file collector against a read-only 4 KiB XFS loop image with reflink enabled. The fixture tools came from `xfsprogs` 6.6.0 in a private directory. `filefrag` independently observed shared extents on the source and clone. Rewriting the first 64 KiB of another clone produced a private prefix and a shared suffix. The ordinary copy had no sharing flags. [XFS block sharing](https://www.kernel.org/pub/linux/utils/fs/xfs/docs/xfs_filesystem_structure.pdf)

FileCat reported both records on the sparse file, with logical offsets 0 and 1,048,576, alongside its separately observed allocation and logical length of 1,048,577 bytes. It preserved the `UNWRITTEN` flag and showed **Physical location unavailable** for the preallocated file. A separate held xattr inventory reported the prepared name and length without a value query. The summary omitted extent collection. All six file checksums matched after the full reports, and the private mount was removed. The run passed 47 tests with eight platform or opt-in skips. Its control log and `xfs-details.trx` are under `~/FileCatValidation/filecat-xfs-details-98833205-r1`. [Kernel FIEMAP flags](https://docs.kernel.org/5.18/filesystems/fiemap.html)

## Short-page completion regression (21 November 2026)

Against base main `3db2088e7`, a deterministic collector fixture on a Windows x64 workstation (Windows 10.0.26320, .NET SDK 10.0.101, runtime 10.0.15) exposed a short, nonempty FIEMAP page without `FIEMAP_EXTENT_LAST` being marked complete. The corrected collector continues from its validated cursor until it sees the terminal flag. An empty continuation after a nonterminal page now reports a partial inventory rather than claiming completion. An initially empty map remains complete. The changed `UnixFileRecords.Fiemap.cs` SHA-256 is `73E09EC5D18713B4AD44AB50FFC7C8CA1783029716F6FC0EAF2C3D0EC5C03478`.

Validation: `dotnet build tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj --no-restore --nologo --verbosity minimal` succeeded, then `FileCat.Core.Tests.exe -class '*UnixFiemapDetailsTests' -noLogo` passed 15/15 tests. These tests supplied synthetic FIEMAP buffers; no Linux ioctl, filesystem, VM, or native qualification was run in this regression slice. The earlier Ubuntu native results above remain historical evidence for their stated builds.
