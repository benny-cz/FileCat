# Bounded Linux extent details

The Linux metadata-only full record can collect the main-stream extent map of a pinned regular file. It reopens the live `O_PATH` descriptor through the process descriptor directory on procfs, verifies the inode and kind, and issues `FIEMAP` with synchronization disabled. The summary does not open a mapping descriptor. Symlinks and special files stay outside the collector. [Linux kernel FIEMAP documentation](https://docs.kernel.org/filesystems/fiemap.html). [Linux procfs descriptor semantics](https://man7.org/linux/man-pages/man5/proc_pid_fd.5.html).

The collector validates cursor progress, counts and offsets and stops at 256 requests or 100,000 records. A later error retains the validated prefix and reports a partial inventory. Completion describes the returned map. A complete record can still show **Physical location unavailable** and its raw flags. Sharing appears only as evidence reported by returned records.

Ubuntu on ext4 with kernel 7.0.0 passed all 26 focused tests. The native fixture produced over 512 sparse extent records, then replaced the original pathname with a FIFO. The still-held inode returned a complete map, and inspection of the new FIFO omitted the collector. macOS ARM64 passed 24 tests and skipped the two Linux-specific fixtures. The guests retain `.tmp/fiemap-details/results.xml`. Other Linux filesystems and architectures, Windows hard-link names and Windows allocated byte ranges still need their own evidence.

## Btrfs shared and encoded extents

Ubuntu x64 passed a separate fixture on a private read-only Btrfs loop image. `filefrag` independently reported the same shared extent for a source file and its reflink, an unshared extent for a byte-for-byte ordinary copy, and the encoded flag on all six extents of a zstd-compressed file. FileCat reported sharing only for the source and reflink. It kept the raw flags and reported the physical location as unavailable for the encoded extents. The summary omitted the extent inventory, and the full record remained metadata-only. Checksums for all four fixture files matched after inspection.

The result is `extents.trx` under `~/FileCatValidation/filecat-btrfs-extents-984b6f47-r1` on the Ubuntu guest. The compression policy and containing subvolume ID have their own [Btrfs fixture](2026-11-18-linux-btrfs-properties.md).
