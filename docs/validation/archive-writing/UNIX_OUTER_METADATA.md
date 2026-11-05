# Held Unix archive outer metadata, 5 November 2026

Status: **development observations only; no new profile enabled**. This extends the held origin reader with the native
permission/metadata evidence needed by future publication and reconciliation. It does not create, publish, chmod,
chown, change ACLs/flags/origin, or delete any archive. The [implementation ledger](IMPLEMENTATION_STATUS.md) retains
the full outstanding scope and [plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md).

## Observation contract

`ArchiveUnixOuterMetadata.Capture` takes an already-held regular-file SafeFileHandle. It retains that descriptor across
all native calls, opens no pathname, refuses devices/directories and excessive physical length, and checks cancellation.
Disposal cannot recycle the descriptor during a capture. A renamed, replaced, linked or unlinked pathname cannot
retarget the observations.

The immutable snapshot records native device/inode identity, length, owner/group IDs, all 12 permission/set-ID/sticky
mode bits, link count, full nanosecond mtime/ctime, optional explicitly marked birth time, native inode flags, raw ACL
evidence, and the existing raw typed origin snapshot. Linux additionally records the statx attributes and their
supported mask. Ctime is change evidence, not a promised preserved timestamp. Atime is excluded from the snapshot.

Linux x64/ARM64 uses the fixed statx layout with mandatory returned-field mask checks; optional birth time stays absent
when unsupported. `FS_IOC_GETFLAGS` must succeed; unsupported filesystems do not silently produce zero flags. The access
ACL comes from held-fd `system.posix_acl_access`; only ENODATA denotes absence. Mac uses the supported 64-bit-inode fstat
layout and `acl_get_fd_np(ACL_TYPE_EXTENDED)`, bounded `acl_size` and canonical external `acl_copy_ext` bytes, including
ACL-level flags. On the actual held-file Mac API, ENOENT denotes absence; denied/unsupported/IO failures stay unavailable.
The native calls/layouts are admitted only on x64/ARM64; native evidence below covers Linux x64 and Mac ARM64 only.

ACL data is copied/frozen and capped at 128 KiB before managed value allocation. Present data and explicit absence
have distinct representations. A failed ACL, flags or origin read refuses capture with PublicationUnavailable.
Each observation reads stat/flags before and after ACL/origin capture, and two complete observations must have the
same canonical SHA-256. Detected changes refuse with SourceChanged. Matching observations are not an atomic snapshot
against an equivalent-privilege concurrent writer; publication must reread the actual retired/published objects.

The schema-1 digest has a fixed domain/version, platform, explicit little-endian fixed-width fields, timestamp/ACL
presence markers, length-delimited ACL bytes, and the typed origin digest. A separately encoded binary golden fixture
and field-change cases cover every named observation. No culture-dependent dates, SID/user lookups, ACL sorting,
normalization or origin URL interpretation occurs.

This is a named subset, not a complete Unix backup-security descriptor or authorization to discard other metadata.
Arbitrary xattrs, Linux security labels/resource policy, Finder/resource-fork state, complete filesystem admission,
promised metadata application, durable private stages/sidecars, publication/recovery integration and global evidence
budgets still require implementation/qualification before profile enablement.

## Independent native observer and failed iterations

The FileCat-owned `eng/validation/archive-writing/unix-metadata-observer.c` is a development test driver, not a shipped
helper or runtime dependency. It uses each machine's public C headers and native structure members independently of
the managed buffer offsets. It opens only the supplied owned regular fixture and emits bounded numeric metadata;
it reads no payload and mutates nothing. The opt-in test compares every stat/flag field, including actual birth-time
presence/precision, group, link count and Linux attribute masks, and rereads the held snapshot for stability.

Mac's global Xcode selection pointed to Apple clang 15, which could not link the installed 27.0 SDK. Qualification
explicitly selects `/Library/Developer/CommandLineTools/usr/bin/clang` 21.0.0 and its matching SDK without changing
global Xcode settings. Ubuntu initially had no C compiler; GCC was explicitly announced and installed only in the
user-authorized disposable validation VM. These test tools never become FileCat product dependencies. GCC/driver
versions and raw setup output are retained with the evidence.

The first native run found two issues: Mac's no-ACL result was incorrectly treated as an unavailable ACL, and the
Linux test incorrectly used .NET's creation-time API as an exact native birth-time oracle. The former now recognizes
only the observed native ENOENT absence result; permission-denial cases remain unavailable. The latter now checks the
raw birth timestamp against the independent C statx observer; it does not replace precise native evidence with the
managed ctime/mtime fallback. Initial failed TRX/logs are retained and are not counted as passes.

## Evidence

Final source overlay over `2ff48386`: production reader, tests and C observer in
`archive-unix-metadata-source-v3.tar.gz`, SHA-256
`D53EEC29EBAEADBFE4416A7E07C9A5F7909DB87623120F545D1FADA44E8F484E`.
Windows host source build: SDK 10.0.101, zero warnings/errors. The source overlay is applied over all previously
recorded guest source checkpoints; the guest baseline Git HEAD alone is not presented as the tested source.

The affected filter selects ArchiveUnixOuterMetadata, ArchiveUnixOrigin, UnixMark, ArchiveOrigin and
ArchiveWindowsOrigin classes. Native cases cover unchanged observations/precision, mode/mtime/raw origin changes,
hard-link counts, path replacement/symlink/unlink without retargeting, Linux POSIX access ACL changes at identical
mode/mtime, Mac extended ACL changes at identical mode/mtime, absent/present-mark permission denials, native nodump
flags, device/directory refusal, cancellation/disposed handles, and the independent C field observer. Portable
constructor/digest cases exercise frozen binary data, every field, presence markers and invalid/bounded observations.

| Final environment | Passed | Skipped | Failed | Scope |
|---|---:|---:|---:|---|
| Windows 11 x64 VM, medium integrity | 192 | 36 | 0 | 228 total; native Unix cases skipped |
| Mac ARM64 / APFS | 221 | 7 | 0 | 228 total; independent C observer executed |
| Ubuntu x64 / ext4 | 218 | 10 | 0 | 228 total; independent C observer executed after an idle window |

Windows final TRX: `archive-unix-metadata-vm2.trx`; managed 28-file overlay SHA-256
`2581EAD765CF7EBC803D064923C9B4E4D897784B6B3122CAD22293147C893627`.
Mac final TRX: `archive-unix-metadata-mac-v2.trx`; source built natively using SDK 10.0.101. Its runtime version was not
independently captured; see the [later runtime inventory correction](STAGE_CREATION_RESERVATION.md).
Ubuntu final TRX: `archive-unix-metadata-ubuntu-v2.trx`; the same source built natively using SDK 10.0.101 and runtime
10.0.14; native observer built with GCC 13.3.0. Testing was postponed while other FileCat instances ran, then began
only after an idle check. Both Unix observer comparison cases passed, not skipped.
The source adds 46 tests; the filter has 228 total cases. Raw logs, TRX and launch facts are retained under the ignored
worktree `.tmp/test-results/archive-writing` and task-owned guest validation directories. These are not GUI or
installed-package qualification, and no format or AW packet is completed by this checkpoint.

| Native artifact | SHA-256 |
|---|---|
| Mac Core.dll | `7C1D02412CFCEB26A4BE4E0CB8CA7449DBC37B6522CE55F27641D64773C10AA7` |
| Mac Core.Tests.dll | `012CAE7DF2B3CDD7764594F3B164D04209AE0772F9F0DBC6F48E593FAD58EF96` |
| Mac C observer | `5BA0FCA43B1184E00AFB9A75BD612FA495286D91A7380A8685019A6FECB6F8FC` |
| Ubuntu Core.dll | `67A9A2B22F2E714A9EB93D8BF9FE4CCC7A0A98236619E6963C7C5BFE20068601` |
| Ubuntu Core.Tests.dll | `B4364EAD7597996CE8F9A4B496B3773EC5233461FA413C3B820479D065B80BF3` |
| Ubuntu C observer | `7969F20F145AC721D79F120A03BEE85998995650D5D3E376263D3FD8E997F17E` |

Final Unix process censuses found no FileCat, worker, dotnet or testhost instance left by this task. The Mac's temporary
until-reboot caffeinate assertions were retained. The Windows final census is recorded separately; no other tester's
FileCat instance was killed to obtain these results.

## Interface references

Public interface review used the Mac's installed `sys/stat.h` and `sys/acl.h`, Apple's
[held ACL manual](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man3/acl_get_fd.3.html),
and the Linux native statx/ioctl/xattr interfaces as defined by its installed headers and exercised by the independent
C observer. No third-party implementation/header source was copied or vendored; the code uses FileCat-owned wrappers
and development driver code. Native absence/denial behavior is measured separately from an API's older documentation.
