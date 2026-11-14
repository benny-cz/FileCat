# Retained edit-session storage checkpoint

This component is listed as of 13 November 2026 (`fda3f03c`, with the eighteenth review's fixes in `a4606689`):
ordinary startup composes this store on win-x64 (NTFS), linux-x64 (ext4) and osx-arm64 (APFS), and keeps the legacy
store everywhere else, including where FileCat's own data folder is on another file system or cannot be held privately.
The platform tests still drive the native factory explicitly for the cases a listed route cannot reach. What stays open
is the owner's: release signing and the signed build's own evidence ([route decision](RELEASE_ROUTE_DECISION.md),
[status](IMPLEMENTATION_STATUS.md)).

One asymmetry inside this store is recorded rather than removed: an archive member's download mark goes onto the private
copy through the folder FileCat holds, while a server file's edit, a legacy-ZIP member's edit, a legacy commit copy and a
Save copy write it by the copy's pathname. The [status](IMPLEMENTATION_STATUS.md) entry of 13 November states why that is
accepted (the folder is FileCat's own and private, the write carries no user bytes and is checked, and the mark is read
back through the held folder by what depends on it), what the residual is, and what making the four symmetric would
take.

`03ec2201` adds retained native directory authority and bounded visible recovery records. `df4ec125`
fixes the Darwin ARM64 variadic creation-mode ABI and adds exclusive new-record publication.
`6b7a80a3` connects private native session creation, an interrupted-creation marker, persisted directory
identities, exclusive immutable state generations, stale/concurrent-save rejection and complete bounded
revision-chain review. `c68ec836` adds held working-file reads with fresh native name/version checks,
legitimate atomic editor replacement, private Windows ACL inheritance and native storage schema 3.
Existing schema 1/2 records remain readable without rewriting them merely by listing.

`b35d5992` fixes Ubuntu's normal editor replacement under umask 0002, preserves schema 3 after
verified archive acknowledgement, keeps unreadable interrupted-creation markers visible, and bounds
native modification-time conversion. The Unix adapter tests cover all ten format/password combinations.

The initial `session.json` stays intact. New state records claim the next fixed generation without
replacement and link to the prior record's SHA-256. Every generation is reviewed, with limits of
4,096 revisions and 32 MiB of aggregate metadata. An unknown version, missing generation, damaged
ancestor, stale caller or uncertain publication retains the working folder for review. These hashes
detect inconsistent state; they are not authentication against a process with equivalent user authority.

On Windows, new directories have protected owner/SYSTEM ACLs whose permissions inherit to normal
editor-created files. File admission accepts only those two principals, a single regular non-reparse
file and the expected retained parent namespace. On Unix, directories remain creator-owned 0700;
normal editor replacement may produce ordinary read/write permission bits through 0666 within that
private namespace; the retained 0700 directory prevents other users from traversing to the file. Working
admission checks owner, type, mode, link count, native metadata/version and the current relative name.

## Evidence

The [checkpoint](edit-storage-checkpoint.json) preserves thirteen verified bundles, 219,524,260 bytes,
including every initial failure. The static verifier binds Git source bytes, compiler snapshots,
DLL/PDB checksums, test results and final process censuses:

```powershell
python -B -X utf8 eng/validation/archive-writing/verify_edit_storage_checkpoint.py
python -B -X utf8 eng/validation/archive-writing/verify_edit_storage_checkpoint.py --platform mac --folder awe-storage-v5
```

| Focused gate, `b35d5992` / v5 | Windows | Mac | Ubuntu |
|---|---:|---:|---:|
| Bounded record/recovery parser | 19 | 19 | 19 |
| Native private-directory primitives | 16 | 16 | 16 |
| Native session storage and working reads | 14 | 19 | 19 |
| Connected edit/session/password Core selection | 100 passed / 10 Unix-only skipped | 110 | 110 |
| Connected application edit selection | 190 | 190 | 190 |

The focused packets bind 22 changed C# inputs and 15 DLL/PDB pairs on Windows, and 20 inputs and
12 pairs on each Unix platform. They are not complete Core or native desktop acceptance runs. Every
final census has zero owned instances and the Mac preserves wake helper 36765. Windows v5 was
initially refused before source application by the sharing check when another tester resumed. That
refusal is preserved; the identical frozen packet later completed successfully when the VM was idle.

The initial Windows lane used an inaccessible administrator-profile fixture; subsequent test roots
use the standard account's own temporary directory. Darwin's native creation mode required the ARM64
variadic ABI correction. Remaining early failures covered exact invalid-Unicode exception shape and
unnecessary Windows directory DELETE access. Mac `awe-storage-v3` passed all native gates but exposed
two old assertions that expected oversized records to be hidden; the corrected assertions require
visible readonly recovery and refusal of Save/Discard.

Ubuntu v4 passed 13/14 storage cases and rejected the normal atomic editor save: its inherited umask
0002 creates a group-writable replacement. The actual v5 recipe passes that case and explicit 0640,
0664 and 0666 variants while still refusing a working directory changed from 0700 to 0755. Both Unix
platforms also pass interrupted-marker substitution and all ten native-store archive acknowledgement
cases. This validation does not qualify native snapshot cleanup or archive publication.

## Remaining implementation and qualification

The [snapshot follow-up](ARCHIVE_EDIT_SNAPSHOTS.md) implements retained native capture and queued readonly
source views and passes its focused three-platform matrix. Exact snapshot cleanup, native Save copy
publication, ZIP/remote integration, native discard/new-child reconciliation, old-record adoption and
application composition are still required. The legacy path-based cleanup does not establish these gates.
Do not enable this factory or new write profiles before those operations are connected and qualified.

Follow-up `cc5ea3a9` makes repeated commit-copy disposal return before touching a reused pathname and
adds a replacement-preservation regression. It is outside this frozen v5 evidence; all ten commit-copy
regressions now pass on all three platforms in the snapshot follow-up. Its selector explicitly includes
`EditCommitCopyTests`, which the `EditSession` filter alone does not select.

Complete directory/record/artifact substitution, interrupted creation/publication/cleanup, editor
concurrency, origin/metadata, cancellation and restart cases on all three platforms. Then rerun complete
regressions, actual native editor/UI/accessibility acceptance, Unix mutation/confinement and the
remaining fuzz/resource/RID/package gates before the independent final review. Optional XZ/ISO work
retains its existing plan priority.

## What a discard reviews, and by whose rules (`dcf19885`)

A discard reviews two kinds of object by two different rules. FileCat's own records - the anchor, each revision, the
creation marker, a capture's record and receipt, a saved copy's intent - must be exactly as FileCat wrote them, down to
their permissions: a record whose permissions changed is no longer one FileCat can speak for. The user's own bytes -
their working copy, a name an interrupted removal set aside, a retained commit copy - are reviewed as every other review
of them reads them: an ordinary file this user owns, with no second link, inside the private folder whose own privacy
(0700 on Unix, a protected owner-and-system list on Windows) is reviewed on every operation. Their permission bits are
the editor's to set: a replacement carries the folder's inherited permissions, an ordinary umask leaves group and other
read bits, and a script being edited may be made executable.

Until `dcf19885` the discard read the user's bytes by the records' rule, so an edit whose editor had saved it by
replacing the file - which most editors do - could not be discarded at all: the confirmation was answered with "Archive
artifact permissions are no longer private", and the only way out was to remove the folder by hand.

A session's folder and a retained copy's folder may both hold what FileCat left behind unfinished: the stage of a record
it was publishing (a failed publication keeps its stage on purpose) and a name an interrupted exact removal set aside.
Both are reviewed and removed with what they belong to, each through a handle bound to what was reviewed, up to 64 of
them per folder; a folder holding more is kept whole and says how many it holds. A retained copy whose payload - or
whose whole payload folder - a stopped removal already took is still removable: what is left is that copy's own records,
which are FileCat's. A saved copy that still refers to a payload that is gone keeps that one copy's own removal from going ahead
(`dcf19885`), while the whole edit's discard takes it with the record that refers to it (`3c377dab`, below).

A retained copy that has lost its own record is FileCat's own leftover as well (`3c377dab`). Its folder is
made before that record is published and its removal takes the record first, so a crash or a failure in either window
leaves the folder empty, holding only its payload folder, or holding only its receipt. Nothing in it can be bound to a
state of the edit without that record, so nothing can be recovered from it; it is reviewed as FileCat's own, with the
one file its payload folder may hold reviewed as the copy FileCat put there, and removed with the edit. A saved copy's
own record goes the same way as the copy it refers to, as it already did when that copy's payload was there - with the
payload gone nothing can resume from it, and the file it was writing is in the user's own folder, untouched either way.
Anything else in such a folder is still kept and named as it is everywhere else.
