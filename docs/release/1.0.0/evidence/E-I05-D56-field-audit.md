# E-I05-D56 — audit of the advertised filesystem-record fields (DEC-05)

DEC-05 (2026-10-28) requires every filesystem-record field advertised under D-56 to be implemented before 1.0.0, rather than trimmed from the claims. A read-only audit compared the claims with the source at `275e2caf` (record code identical to main `74e8e591`). Nothing was built or run.

The claims are in the architecture plan (§16.1 at line 973 says "implemented"; the D-56 decision row at line 1634 still says "in progress"), `docs/CAPABILITIES.md` lines 55–56 and `docs/IMPLEMENTATION_STATUS.md` line 58. README does not mention the record.

## Result

| # | Claim | Status | Platform / rights |
|---|---|---|---|
| 1 | `$LogFile` restart and record pages | Implemented (`Core/Records/NtfsLog.cs`, `WindowsFileRecords.Log.cs`) | Windows NTFS, admin + raw |
| 2 | Deleted names in `$I30` index slack | Implemented (`NtfsIndex.cs`, `WindowsFileRecords.cs`) | Windows NTFS, admin + raw |
| 3 | `$Secure` security descriptors | Implemented: `$SII`→`$SDS`, hash, mirror, compared with Windows | Windows NTFS, admin + raw |
| 4 | **`$ObjId` and `$Reparse` indexes** | **Missing**: only the item's own object ID and reparse data are read, through FSCTL calls | Windows NTFS, admin + raw |
| 5 | USN journal: one file's history, maximum, allocation delta | Implemented | Windows NTFS/ReFS, admin |
| 6 | `$STANDARD_INFORMATION` beside `$FILE_NAME` times, with a warning | Implemented (it warns on creation time; other pairs are shown, not compared) | Windows NTFS, admin |
| 7 | Attributes, attribute list, resident data, record slack | Implemented (first 256 bytes; slack only when not all zero) | Windows NTFS, admin |
| 8 | Data runs | **Partial**: unnamed `$DATA` runs come from FSCTL_GET_RETRIEVAL_POINTERS; other non-resident attributes' runs are decoded but shown only as "runs from VCN n" | Windows NTFS, admin |
| 9 | Object ID birth time and network card, sparse ranges, compression, clusters, IDs, 8.3 name, hard links, SMB connection | Implemented | Windows |
| 10 | Reparse data decoded, including cloud placeholders | **Partial**: links, junctions, app execution aliases, WSL links and WOF decode; cloud placeholders show the tag name and hex | Windows |
| 11 | EFS information | **Partial**: the encrypted bit and a `$LOGGED_UTILITY_STREAM` row only; no users, recovery agents or certificates | Windows NTFS |
| 12 | Permissions in words, weak permissions flagged | Implemented | Windows, Linux, macOS |
| 13 | ext4 inode/generation, lsattr flags, birth time, extents, project ID | Implemented (statx, FS_IOC_GETFLAGS/GETVERSION/FSGETXATTR, FIEMAP) | Linux |
| 14 | **ext4 inode checksums** | **Missing** (no ext4 reader exists) | Linux, root + raw |
| 15 | **ext4 `i_version`** | **Missing** | Linux, root + raw |
| 16 | ext4 journal summary | Implemented from `/proc/fs/jbd2` and `/sys/fs/ext4` statistics (kernel statistics, not the on-disk journal) | Linux |
| 17 | APFS/HFS+ chflags flags, clones, birth time | Implemented through getattrlist | macOS |
| 18 | FAT/exFAT short names and raw time stamps | Implemented on Windows and Linux; **missing on macOS** | admin/root + raw |
| 19 | HFS+ journal summary (D-56 row wording) | **Missing**; §16.1 names only ext4's journal, so scope is ambiguous | macOS, root + raw |
| 20 | "uses the read-only engines and helper of P10" | **Partial / wording**: the record uses its own parsers and needs FileCat itself elevated; the P10 helper is not used | Windows, Linux |
| 21 | Non-admin report says first what it leaves out | **Partial**: the notice omits `$I30` slack, `$LogFile`, `$Secure` and the journal size | Windows |

## Work items, in priority order

1. `$ObjId` (`$O`) and `$Reparse` (`$R`) indexes from `$Extend` with the existing index reader; show and cross-check the item's entries (M, admin).
2. A raw ext4 inode reader (superblock → group descriptor → inode) for the crc32c inode checksum (metadata_csum seed, inode number, generation) and `i_version` (`l_i_version` + `i_version_hi`) (M, root).
3. EFS users and recovery agents (`QueryUsersOnEncryptedFile`/`QueryRecoveryAgentsOnEncryptedFile` or `$EFS`) (S).
4. FAT/exFAT entries on macOS through the existing `FatEntries` on the raw device (S, root).
5. Show the runs of every non-resident attribute (S).
6. Decode cloud placeholders (`CfGetPlaceholderInfo` or the data) (S).
7. Complete the non-admin notice (S).
8. HFS+ journal summary (volume header → JournalInfoBlock → journal header), or reword the D-56 row (S–M).
9. Reword the P10-helper sentence, or route through it (S to reword; M–L to implement).
