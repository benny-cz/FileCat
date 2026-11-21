# Linux ARM64 ext-family, Btrfs and XFS fixtures — 21 November 2026

This receipt qualifies File-system details' native ext-family, Btrfs and XFS fixtures for the linux-arm64 package. Until now these were qualified on Linux x64 only (N-LINUX). The only ARM64 Linux machines available are GitHub-hosted runners.

## Fixtures

[`eng/validation/prepare-linux-details-fixtures.sh`](../../../eng/validation/prepare-linux-details-fixtures.sh) builds every fixture on loop-mounted images, following the x64 fixtures' recipes:

- **ext4 restrictions:** fs-verity, immutable, append-only, no-dump and ordinary files, plus a link to the verity file.
- **ext4 case folding:** the same names in an ordinary and a folded directory, mounted read-only.
- **Historical ext flags:** ext2, ext3 and ext4 volumes with the recorded `c`, `s` and `u` flags on a file and a folder, mounted read-only.
- **Btrfs properties:** a nested subvolume, inherited and per-file compression policies, a denied file, a link and a FIFO.
- **Btrfs extents:** a reflinked pair, an ordinary file and a compressed file, read-only.
- **XFS extents:** reflinked files (one partly rewritten), a sparse file, a reserved range and an extended attribute, read-only.
- **FIEMAP:** on the runner's own temporary folder.

On the Ubuntu x64 VM, a dry run of the script followed by the details classes passed all eight target tests (80 tests, 0 failed). The x64 qualification itself is unchanged.

## What changed in FileCat

The first hosted run prepared every fixture correctly, but the casefold, Btrfs-properties and ext inode-flag sections were missing: the production reader gated them to x64. These queries use the generic Linux ioctl encodings and layouts, which are the same on arm64: `FS_IOC_GETFLAGS`, `BTRFS_IOC_INO_LOOKUP` and the `btrfs.compression` attribute. The reader already takes its open flags from the per-architecture table. Commit `f67d7d7da` enables the inode-flag, case-folding and Btrfs queries on ARM64. fscrypt, file capabilities and ZFS stay x64-only until they have ARM64 fixtures.

## Result

The manual [ARM64 qualification workflow](../../../.github/workflows/details-arm64-qualification.yml) passed at `f67d7d7da` in [run 44943123942](https://github.com/benny-cz/FileCat/actions/runs/44943123942), on `ubuntu-24.04-arm` (kernel 7.0.0-1017-azure, aarch64).

The Unix details classes ran 177 tests: 158 passed, 0 failed ([TRX](receipts/2026-11-21-linux-arm64-fixtures/linux-arm64-details.trx), `d29c8892…`; [fixture log](receipts/2026-11-21-linux-arm64-fixtures/fixtures.log)). The tests that passed include:

- the ext4 restrictions in Summary and Full, with no payload read;
- case folding observed on the held ext4 directory;
- the historical ext2, ext3 and ext4 flags, without claiming compression;
- Btrfs policy and containing subvolume from the held object, and the refused deletion of the compression attribute;
- Btrfs and XFS extent records: sharing, sparse ranges, unwritten extents, and the encoded extents' physical locations omitted;
- FIEMAP reopening the pinned inode after its path became a FIFO;
- the inline_data sentinel.

The 19 skips are the macOS-only cases and the fixtures that stay x64-only: fscrypt policies and key states, nested ZFS, recorded file capabilities and the xattr syscall route.
