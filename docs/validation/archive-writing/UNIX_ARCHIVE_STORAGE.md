# Native Unix archive storage checkpoint, 5 November 2026

Status: **unregistered development primitives; no new profile enabled**. This adds private parent-owned files and
reviewed no-replace rename operations for native ext4/APFS lanes. It is not a complete publication/recovery protocol,
exact-object cleanup implementation, release/package qualification or completion of an AW packet. The
[implementation ledger](IMPLEMENTATION_STATUS.md) and [plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md)
retain the full remaining scope.

## Implemented behavior

`UnixArchiveDirectory` pins the chosen directory through one-component no-follow ancestor opens, records its identity
and observed filesystem, and rechecks namespace/volume before creation, admission and namespace mutation. Input opens
are descriptor-relative, no-follow and nonblocking; the opened object must be a bounded regular file before any payload
read. FIFO/device/directory/link inputs cannot become a data stream. These Unix handles do not claim kernel sharing
exclusion of unrelated writers or renames. Child leases retain the parent handle after its owner is disposed.

Stage, manifest and provenance names are nonce/random scoped, exclusively claimed and never reused on collision.
Linux creates with openat O_CREAT|O_EXCL, mode 0600. A POSIX default ACL cannot grant effective named/group access beyond
the zero group mask. Mac uses public openx_np with an explicit empty no-inherit ACL and mode 0600 in the creation call;
it performs no post-create chmod/ACL repair. The native inherited-ACL fixture has a normal control file inheriting an
everyone-read ACL, while the private artifact admits no access-granting ACL entry and stays 0600.

Darwin's public openx_np takes a full path, not a directory descriptor. Its final component is no-follow/exclusive;
the parent is pinned/rechecked and the created descriptor must match the relative child of that exact held directory
before a writable stream is returned. Namespace anomalies retain the created artifact for review; this lease writes
no archive data before those admission checks. This does not claim
an atomic held-parent create against equivalent-privilege namespace races. The observed openx_np rejects
O_NOFOLLOW_ANY with EINVAL despite that flag existing in the SDK; a native four-variant probe identified it, and the
route retains the explicit ancestor/relative-child checks instead of claiming that flag was installed.

Admission/review requires private ownership, mode 0600 and exactly one link for artifacts. Private state is checked
again before writes and mutation. Streams expose no native handle and check position/seek/length/write bounds before
effects. The local cap is 16 open child leases; global job lease accounting remains separate. Stages use the physical
policy ceiling, while manifest/provenance leases have the metadata ceiling, with tighter caller limits mandatory.

Seal irreversibly disables the lease's writes before cancellation/flush/review. It checks file fsync and, on Mac,
F_FULLFSYNC; errors retain evidence. Complete versions stream SHA-256 and compare full held outer metadata before and
after hashing. The [metadata contract](UNIX_OUTER_METADATA.md) covers raw origin/ACL and precise native fields.
Both manifest and provenance codecs roundtrip through actual private files, rather than only MemoryStreams.

RenameNoReplace requires a sealed private artifact, the expected full object version and matching metadata. It reviews
the source name/parent, metadata/hash/metadata, flushes the file, and uses renameat2(RENAME_NOREPLACE) on Linux or
renameatx_np(RENAME_EXCL) on Mac. An existing or late destination is never deleted or overwritten. After the syscall it
records the new known name immediately, then reviews the actual destination identity, content and preserved fields
(ctime is expected to change on rename), and checks directory fsync. Post-syscall uncertainty is NeedsReview; it does
not trigger pathname rollback or cleanup. A native pathname rename is not conditional compare-and-swap on a held inode,
and these primitives do not promise absence of transient visibility under hostile namespace races.

Recovery artifact opens require a matching explicit identity and private state; the stream stays logically read-only
while its native descriptor permits the flush needed by a reviewed rename. This is not authorization to resume an
interrupted member plan. Dispose never deletes files. DeleteReviewed **always refuses** with PublicationUnavailable:
no qualified exact-object Unix unlink operation is implemented, and an identity precheck plus unlinkat would leave a
wrong-object deletion race. Required cleanup/recovery actions must remain unavailable/retained until a reviewed
protocol establishes their authority. No UI or production registry advertises these primitives as a finished writer.

## Filesystem admission correction

Linux statfs magic 0xEF53 is shared by ext2/ext3/ext4. The previous unregistered worker bundle guard used it as an ext4
test. `UnixArchiveVolume` now binds the held descriptor's statx mount ID to the exact kernel mountinfo filesystem type,
requires an actual procfs-backed bounded mountinfo read, checks mount identity again and refuses read-only/unknown
routes. Only exact ext4 is admitted in this development lane. Pure parser cases retain ext2/ext3/xfs as distinct types
and refuse missing, duplicate, malformed or read-only evidence. Procfs cannot be mistaken for a destination disk.

Mac uses the installed public 64-bit statfs layout, requires exact apfs, MNT_LOCAL and a writable mount. Other
filesystems/RIDs are unqualified. Successful file/directory sync calls are observations, not a universal crash or
power-loss guarantee. Directory ancestry, mount/update races and package qualification still require the full matrix.
The Linux worker bundle uses this same stronger mount-bound check before consuming archive input.

## Native evidence

Final six-file source overlay over `5c9bf33b`: `unix-archive-storage-source-v4.tar.gz`, SHA-256
`E4EE3109BE91A5F603938323B9DBF093458B98B36E01BBA1AD4EC3C8714E9336`.
It contains the four Unix storage/native/volume files, the Linux isolation change and the storage tests. Source builds
with SDK 10.0.101 completed with zero warnings/errors. Guest sources contain the preceding recorded checkpoints;
their baseline Git HEAD alone is not the tested source. Windows/Ubuntu runtime: serviced .NET 10.0.14. The Mac runtime
version was not independently captured; see the [later correction](STAGE_CREATION_RESERVATION.md).

| Final lane | Passed | Skipped | Failed | Scope |
|---|---:|---:|---:|---|
| Ubuntu x64 / ext4 storage + affected Unix observations | 86 | 10 | 0 | 96 total |
| Mac ARM64 / APFS storage + affected Unix observations | 88 | 8 | 0 | 96 total |
| Windows 11 x64 medium-integrity compatibility | 56 | 40 | 0 | 96 total; native Unix cases skipped |
| Ubuntu native Linux worker regression | 40 | 1 | 0 | 41 total; newly published worker with the mount-bound guard |

The source adds 32 cases. Native storage cases cover private creation/reopening, flush/seal and canceled-seal state,
bounded IO before effects, native manifest/provenance sidecars, late target no-replace, complete source versions,
permission/content/name drift, renamed parent refusal, retained child lifetime, regular/no-follow/nonblocking opens,
hardlink refusal for private artifacts, local capacity/scope bounds, default/inheritable ACLs and unknown-volume refusal.
The independent C metadata observer is not opted into this filter replay and skips; its separate successful native
results are recorded in the preceding metadata checkpoint.

TRX: `unix-archive-storage-ubuntu-v4.trx`, `unix-archive-storage-mac-v4.trx`, `unix-archive-storage-vm4.trx`.
The 28-file Windows managed-binary overlay SHA-256 is
`83935074776F7A1501AE35233F10900A4588004E3CB921FC4150AADC4CB9A8D9`; actual medium integrity is separately recorded.
Initial Mac creation failures are retained in v1 logs/TRX and are not counted as passes. Raw evidence is retained in
the worktree's ignored `.tmp/test-results/archive-writing` and the task-owned guest validation directories.

The tracked Linux containment runner also passed the full 41-case worker lane after publishing the final source and
protecting only its owned 207-entry bundle. Healthy 64 MiB proxy work, actual TAR assessment, native authority/resource
denials, parent death, inherited-descriptor exclusion and reaping still pass with mount-bound admission. Final bundle
Core.dll SHA-256: `36F9E64CE3027481CCA987794124319B6D1511CBCBDB658594A39295599A0137`;
WorkerProbe.dll: `8A9D0524B3DCAEA9AC75DDB59A4CE002D67345BF1FF8B56B5284B7B163E35ACF`;
apphost: `69E0308BAB17EBC10507E849116006939A58C92279B5DEEFBC1CCEB74B693143`.
TRX: `linux-containment-20261105T083017Z-d21c2105-a459-4cf1-8ced-7bb179632c11.trx`.
Raw log: `storage-worker-containment.log`. Final Ubuntu census found no FileCat/test process or owned nonce controller.
Mac test processes exited and its until-reboot caffeinate assertions remained active; Windows final census is retained.
No other tester's instance was killed.

Still required: complete security/xattr admission and promised metadata/origin application, durable journal intents
and reconciliation, reviewed replacement/retention/rollback, exact cleanup or truthful retained-artifact workflows,
global budgets, process/crash/alias/race/fault/resource matrix, production workers/registry/UI, package/RID qualification
and the final independent whole-goal review. No all-platform write acceptance or absence of bugs is claimed.

Public interface review used the current Mac SDK's sys/fcntl.h, sys/acl.h, sys/mount.h and sys/stdio.h, the installed
Linux UAPI, its [filesystem magic definitions](https://raw.githubusercontent.com/torvalds/linux/v7.0/include/uapi/linux/magic.h),
the kernel's [proc filesystem documentation](https://docs.kernel.org/filesystems/proc.html), and native calls on the
actual filesystems. No third-party implementation/header was copied or vendored,
and no new runtime package or helper dependency was added.
