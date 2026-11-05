# Native Unix origin application, 5 November 2026

Status: **unregistered parent primitive; no new write profile enabled**. This extends the
[Unix private storage checkpoint](UNIX_ARCHIVE_STORAGE.md) with held-file create-only origin copying. The
[implementation ledger](IMPLEMENTATION_STATUS.md) and [controlling plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md)
retain the full outstanding scope. This does not classify an opaque mark, derive the strongest mark over multiple
inputs, invent an archive origin URL, qualify marked-input workflows, or implement a complete publication transaction.

## Application contract

`UnixArchiveFile.ReadOrigin` uses the existing held-fd typed reader, retaining the distinction between absent, present
(including empty) and unavailable evidence. It continues to observe the held object after its pathname is moved or
replaced; a pathname is not its source authority.

`CopyOriginNoReplace` accepts only a newly created private artifact from this live transaction, after sealing and
before any successful native rename. Input/source leases, reopened recovery artifacts, unsealed/disposed leases and
renamed artifacts cannot gain this capability. Source arguments must use the native platform's origin format and
must not be unavailable. Existing marks, including empty marks, are never erased, overwritten or treated as absent.
The same post-rename capability closure now applies to the existing Windows archive primitive.

Before a present mark is copied, the parent rechecks directory/name identity and private state, requires native
absence, captures the full payload version/outer metadata and rereads metadata after hashing. The only native write
is to the held descriptor's fixed attribute: `user.xdg.origin.url` with Linux XATTR_CREATE (1), or
`com.apple.quarantine` with Darwin XATTR_CREATE (2). Native create-only semantics close the overwrite gap after an
absence observation. A late EEXIST result refuses for review. Other native set failures retain the private artifact
without publication; no privileged or pathname fallback exists.

The immutable argument already caps raw bytes at 64 KiB; values are copied exactly, including empty/binary data.
This is an evidence-preserving primitive, not approval to publish malformed or unrepresentable provenance. A trusted
coordinator must separately establish the strongest supported classification and full source-plan/evidence binding.
In particular, arbitrary opaque bytes do not become a valid URL or effective macOS quarantine verdict by copying them.

After the native operation, the method completes flush/readback even if cancellation arrives. It checks full data
SHA-256, identity, length, owner/group/mode, link count, mtime/birth time, flags, ACL bytes and native attribute masks;
only ctime's expected change and the requested origin differ. It checks the parent/name again. Any uncertainty retains
the mark/artifact without rollback or removal. Cancellation then propagates. The method does not restore mtime or
other fields over a concurrent change; a mismatch refuses instead. An absent request creates no empty attribute.

All origin preparation must finish before namespace mutation. A successful rename permanently closes this lease's
origin-write capability, even if a subsequent post-rename review fails. A failed no-replace claim does not masquerade
as a successful rename. This lifecycle flag is not durable recovery authorization; reopened leases cannot write marks.
Publication/recovery still need fresh observations of the actual published/retired objects and preservation of late
marks. Equivalent-privilege races are not claimed to be isolated by these checks.

## Validation

The change adds 13 Core native cases and one Windows native case. Native Unix coverage includes exact empty/text/
binary marks, unchanged payload and every other captured field, survival through no-replace rename, late/empty mark
retention, absence without materialization, lifecycle/format/cancellation refusal, stale-name and changed-permission
refusal, Linux unreadable evidence remaining unavailable, and a full 64 KiB APFS quarantine value without conversion.
The Windows case rejects post-rename absent/present requests and preserves the complete payload version. The existing
Windows metadata test now applies provenance before renaming, compares the prepared snapshot after rename, and
checks the capability is closed.

| Final lane | Passed | Skipped | Failed | Scope |
|---|---:|---:|---:|---|
| Mac ARM64 / APFS | 100 | 9 | 0 | 109-case Unix storage/observation/origin suite |
| Ubuntu x64 / ext4 | 98 | 11 | 0 | Same 109-case suite |
| Windows 11 x64, medium integrity | 56 | 53 | 0 | Same suite; native Unix cases skipped |
| Windows 11 native archive/related publication regressions | 121 | 2 | 0 | 123 total; includes the changed native lifecycle |

The Unix metadata C observer is not opted into this replay and skips; its separate native results remain in the
[outer-metadata checkpoint](UNIX_OUTER_METADATA.md). Windows skips the unsupported/elevation guard and the existing
unavailable FAT-drive fixture in the native lane. This is development fixture evidence, not GUI/installed-package
qualification or completion of a plan packet.

Source overlays over `1925560d`:

- Unix final replay used the six-file v2 overlay `unix-archive-origin-write-source-v2.tar.gz`, SHA-256
  `48370A04D0E7C83296E170AE78D0E14D777CC67957924D06EC243892DF6C62E6`.
- The final seven-file source overlay additionally includes the corrected Windows metadata-test ordering:
  `unix-archive-origin-write-source-v3.tar.gz`, SHA-256
  `954A01BCD5712F7898FFB6CE0BDF5DE80FDA758077C79D03827E927A743566D5`.
  Its compiled Unix production/test bytes are unchanged from v2.
- The 48-file final Windows managed-binary overlay `unix-archive-origin-write-win-v3.tar.gz` has SHA-256
  `51CD64039959444B3076E524AE111DE9CFF0CC627018BF2EA0D8EEFD108F39B8`.

Builds used SDK 10.0.101 and completed with zero warnings/errors; actual test runtime was serviced .NET 10.0.14.
TRX: `unix-archive-origin-write-mac-v2.trx`, `unix-archive-origin-write-ubuntu-v2.trx`,
`unix-archive-origin-core-vm3.trx`, `unix-archive-origin-native-vm3.trx`. Windows launch facts independently record
medium integrity 8192; this is not an ordinary UAC-filtered interactive-login qualification. Raw logs, TRX, source/
binary archives and facts are retained under the ignored worktree `.tmp/test-results/archive-writing` and task-owned
guest directories.

| Final native test artifact | SHA-256 |
|---|---|
| Mac Core.dll | `8B14EF201DBDC95CC52DB7CE7BA857E2971CF45EED6D09041544A4BC050D9CCE` |
| Mac Core.Tests.dll | `25898D00F1D115CEB35AABEB12F39256CA40A7BC8D620D27609BAB3AB641BA70` |
| Ubuntu Core.dll | `EA0779295B94E177840AA19E5708367A1B763B303F8EEBE09A77BFFDEA909044` |
| Ubuntu Core.Tests.dll | `0132367B52CAD82643A1F849754E6504DEE3F687AAF8B7B0811C165A515C0A78` |

Final process censuses found no FileCat, worker, dotnet or testhost instance left by this task. The Mac's existing
until-reboot caffeinate assertions were retained. No other tester's instance was killed.

Initial native Ubuntu testing refused a permission-revoked artifact safely but leaked a plain native-open IOException.
The operation now converts preflight IO/permission failures into a controlled NeedsReview refusal. The corresponding
case passes after the fix. The first Windows replay exposed the older metadata test's now-forbidden post-rename mark
application; the test now exercises the supported prepare-then-rename order while retaining its metadata assertions.
Initial failed evidence is retained and is not counted as a pass.

Still required: Unix classification/strongest-input aggregation and representability, complete origin/metadata
admission/application, reviewed source binding, durable sidecar/journal facts, actual publication/reconciliation/
recovery, all format/UI workflows, complete fault/crash/fuzz/resource/package qualification and the final independent
whole-goal review. New writer profiles remain disabled.

Native interface review used the installed Darwin sys/xattr.h and the public
[Linux fsetxattr manual](https://man7.org/linux/man-pages/man2/setxattr.2.html). No third-party code or dependency was
added. The product uses no new helper process, broker command or global system-policy change for this operation.
