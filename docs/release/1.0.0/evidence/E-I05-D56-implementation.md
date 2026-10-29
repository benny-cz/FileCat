# E-I05-D56-IMPL — the remaining D-56 record fields implemented (DEC-05)

**Engineering for DEC-05 (2026-10-28: implement every advertised D-56 field), on `main` at `dc5c2f72` (2026-10-29). Preliminary component and native evidence only: not hosted CI, candidate qualification or release GO.**

The [field audit](E-I05-D56-field-audit.md) found nine work items. Three branches implemented them in parallel, and the release session reviewed each diff and merged it:
- `claude/d56-mac`: `35b76eaa`, merged at `3e646df5`.
- `claude/d56-ext4`: `f152ef40` and `1d231102`, merged at `e376263b`.
- `claude/d56-win`: `effab038`, `1b109b28`, `e74d004b` and `83edfc9d`, merged at `dc5c2f72`.

## What each work item became

| Audit work item (rows) | Now | Code |
|---|---|---|
| 1. `$ObjId` and `$Reparse` indexes (row 4) | As administrator on NTFS: `$ObjId` and `$Reparse` are found by name in `$Extend`'s `$I30`. Their `$O` and `$R` indexes are read (root, blocks after fixups, `$BITMAP`; 1 MiB at a time, at most 64 MiB per index, and the report says when the cap stopped the scan). The item's entries are shown and compared with what FSCTL_GET_OBJECT_ID and FSCTL_GET_REPARSE_POINT report: matches, MISSING, EXTRA or DIFFERENT, with a warning. Entries for its record left behind in the indexes' unused space are listed. | `src/FileCat.Core/Records/NtfsViewIndex.cs`, `src/FileCat.Platform.Windows/WindowsFileRecords.Indexes.cs` |
| 2. ext4 inode checksum and `i_version` (rows 14, 15) | As root on ext2/3/4: the item's inode as the inode table holds it. The parser handles 32/64-byte descriptors and `meta_bg`. Its crc32c checksum is computed as the kernel's `ext4_inode_csum` does, 16 bits on 128-byte inodes. The superblock and group-descriptor checksums are checked too. Also shown: `i_version`, and each field beside statx, FS_IOC_GETVERSION and FS_IOC_GETFLAGS. An on-disk copy that lags the live one is called "not yet written", not damage. The device is used only if it is the block device the volume is mounted from, checked before and after opening, and it is read through the page cache. | `src/FileCat.Core/Records/Ext4Inode.cs`, `src/FileCat.Core/Records/UnixFileRecords.Ext4.cs` |
| 3. EFS users and recovery agents (row 11) | Who can decrypt an encrypted file or folder, and its recovery agents: account, SID and certificate thumbprint, through `QueryUsersOnEncryptedFile`/`QueryRecoveryAgentsOnEncryptedFile`. No administrator rights needed. A network file's server is not asked. | `src/FileCat.Platform.Windows/WindowsFileRecords.Efs.cs` |
| 4. FAT/exFAT entries on macOS (row 18) | As root: the same directory-entry section Windows and Linux give. It is read from `/dev/rdisk…` in whole sectors, using the block size from `DKIOCGETBLOCKSIZE`. Accented names now match: macOS gives them decomposed (NFD), while FAT and exFAT keep them composed (NFC); an exact match still wins. | `src/FileCat.Core/Records/UnixFileRecords.MacVolumes.cs`, `src/FileCat.Core/Records/FatEntries.cs` |
| 5. Runs of every non-resident attribute (row 8) | As administrator: one table per non-resident attribute (VCN, LCN, clusters, position), at most 200 runs each, with sparse and compressed runs named. Each piece is checked against its header's VCN range, and every run against the volume's size. `ReadRuns` now skips runs before VCN 0, past the value or off the volume, which also protects the existing `$LogFile`, `$I30` and `$SDS` reads. | `src/FileCat.Platform.Windows/WindowsFileRecords.Runs.cs` |
| 6. Cloud placeholders (row 10) | A Cloud Files placeholder decoded with `CfGetPlaceholderInfo` (standard class) and `CfGetSyncRootInfoByHandle`: data on this device, validated, modified, pin state, in-sync state, file IDs, sync root, provider and its identity bytes. The existing attributes-only handle is used, and `cldapi.dll` is loaded from System32 only. | `src/FileCat.Platform.Windows/WindowsFileRecords.Cloud.cs`, `src/FileCat.Core/Records/CloudPlaceholderInfo.cs` |
| 7. The non-administrator notice (row 21) | It names everything the administrator-only parts add. A folder's notice adds `$I30` slack, and the ReFS notice adds the journal's size. | `WindowsFileRecords.cs` |
| 8. HFS+ journal summary (row 19) | As root on HFS+: volume header, journal info block and journal header in either byte order. Shown: where the journal lies, transactions waiting to be replayed, sequence number, and xnu's header checksum. Without root, whether the volume is journaled (`MNT_JOURNALED`). | `src/FileCat.Core/Records/HfsJournal.cs` |
| 9. The P10-helper sentence (row 20) | Reworded. The record reads raw volumes with its own read-only parsers inside FileCat, not through P10's helper, so those parts need FileCat run as administrator or root. | `docs/design/FILECAT_PRODUCT_ARCHITECTURE_AND_IMPLEMENTATION_PLAN.md` §16.1 |

## Claims reconciled

- **Updated to describe what the record now reads:**
  - `docs/CAPABILITIES.md`'s File-system records row;
  - the architecture plan's §16.1 paragraph and its D-56 decision row, now "done on all three systems";
  - `docs/IMPLEMENTATION_STATUS.md`;
  - the File-system record command's own description.
- **Audio/video row:** the plan's viewer table now says metadata through the Info inspector and no player in 1.0, instead of "Preview".
- **Test prerequisites:** the readiness plan's invocation map lists the new variables.

## Verification

| Machine | Run by | Result |
|---|---|---|
| This Windows 11 x64 host, not elevated | release session, merged tree | Solution Release build: 0 warnings, 0 errors. Core record tests: 83, 0 failed, 2 skipped (other platforms). Platform.Windows record tests: 19, 0 failed, 10 skipped (administrator or opt-in). |
| Win11 VM (Insider 26300), High integrity, `FILECAT_TEST_EFS=1`, `FILECAT_TEST_CLOUD_FILES=1` | release session, merged `dc5c2f72` | `WindowsFileRecordsTests` and `FatDriveRecordTests`: 19, 0 failed, 1 skipped (no FAT32 drive attached). |
| Same VM | Windows agent | 18/18 record tests, plus 29 Core parser tests, as summarised in the bullets below. |
| Ubuntu 24.04.5 VM | release session, merged tree | 72 as user (2 skipped). 34 as root on a 512 MiB loop-mounted ext4 volume made as in CI (1 skipped, the non-root test); real inodes showed matching checksums and their `i_version`. |
| Same VM | ext4 agent | 394 inodes in six layouts (4 KiB/256-byte, 1 KiB/128-byte, no metadata_csum, meta_bg, 512-byte inodes with csum_seed after `tune2fs -U`, ext2) equal to `debugfs -n stat`. Every checksum matched and `e2fsck -fn` was clean. A changed byte gave "does not match", as debugfs said. Root runs on the loop volume and the VM's own `/dev/sda2` agreed with debugfs. |
| macOS 27.0.1 arm64 (M1) | release session, merged tree | 43 as user (3 skipped). 43 as root with fresh FAT32, exFAT and journaled HFS+ images (2 skipped): real entries with matching checksums, and a journal with 48.5 KiB waiting. Images detached and deleted. |
| Same Mac | macOS agent | Journal location and size equal to `diskutil`. Start and end equal to `fsck_hfs -n -f -d` on a copy taken while mounted, and "journal is empty" after a clean detach. Kernel-written headers are used as checksum known-answer tests. |

On the VM, the Windows agent's tests showed:
- the junction's `$R` entry (tag 0xA0000003) matched what Windows reports;
- a file's `$O` entry matched FSCTL_CREATE_OR_GET_OBJECT_ID's IDs;
- removed entries showed up as left behind;
- a named stream's and a folder's `$I30` runs were listed, and the unnamed `$DATA` runs equal FSCTL_GET_RETRIEVAL_POINTERS;
- the EFS thumbprint equals the user's EFS certificate, and the file's times and attributes were unchanged;
- the Cloud placeholder test's provider saw no fetch, and the placeholder stayed unhydrated with its pin and sync states unchanged.

## Not covered or not verified

- **Hosted CI** has not run these tests yet. That includes the new ext4 root step on Ubuntu and the Windows administrator, EFS and Cloud Files tests, which run by themselves under `GITHUB_ACTIONS=true`. Whether Windows Server runners support the Cloud Files API is unverified.
- **Windows non-administrator accounts.** EFS and Cloud Files were not run under one: the VM has administrator accounts only. FileCat's non-administrator path was exercised with `AssumeNotPrivileged`.
- **Synthetic or untested data only:**
  - Intel Macs (outside PSD-MAC);
  - HFSX, the HFS wrapper, big-endian and external journals;
  - ext4 bigalloc, huge_file, Hurd, inline data and sparse_super2;
  - attribute runs split across extension records.
- **Not checked at all:** group descriptors' older crc16 (`gdt_csum`), and ext4 mounted through FUSE.
- **Noted, unchanged:** macOS's exFAT driver writes local times with the UTC offset's sign flipped. The instant is right, and FileCat shows the bytes as stored.
