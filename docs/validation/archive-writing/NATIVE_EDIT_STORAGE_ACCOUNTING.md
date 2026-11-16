# Native edit storage accounting

## Explicit reviewed discard candidate

The connected `v35` candidate implements a separate `DiscardReviewed` path. On Windows it obtains
fresh relative native directory/file handles with DELETE authority, private security, no link traversal
and matching native identities. Ordinary directory opens still grant no removal authority. The explicit
user decision is bound to the complete working SHA-256 (or reviewed absence); editor saves between
review and deletion refuse. Initial/state records, the full bounded revision chain and creation marker
must agree with the actual session namespace before removal begins.

The preflight inventories known entries and verifies complete captures, interrupted copies and saved-copy
intents. A retained capture may bind an earlier verified state in this session's chain. Saved destinations
are never opened or removed by Discard. Unknown entries, malformed records, foreign identities, unexpected
record origin/streams and active writers retain the edit. Actual deletion reopens and pins each reviewed
object, rechecks its complete observation, deletes that exact handle and removes directories only when empty.
Native directory owners stay held throughout; recursive pathname deletion is not used by this path.

Review is bounded to 8,192 entries, 1,024 captures, 4,096 revisions, 32 MiB of record bytes and 8 GiB of
payload bytes reviewed, with the existing 1 GiB per-file bound. Current preflight/removal checks hash each
payload at most four times (at most 32 GiB of payload hashing), plus bounded native metadata reviews.
Capture metadata retains its 16 KiB bound and intents
their 64 KiB bound. Handles are used in a bounded depth-first walk, rather than retaining thousands of
file owners at once. Cancellation before mutation keeps all data; a later cancellation/failure returns an
explicit partial result so the UI cannot claim that every file remains or that Discard succeeded.

The actual confirmation action now calls this reviewed API and explains recovery-copy removal and
separately saved file preservation. Explicit test composition exercises the native store through the App;
ordinary startup still uses the legacy store. The candidate adds 39 Windows real-file cases, three Unix
unavailable-operation preservation cases and six headless UI cases. Host builds pass; guest execution is
required. Unix exact removal, recovery actions/adoption and the complete qualification gates remain open.

The subsequent resource/race follow-up sizes hash buffers to the held file length (at most 1 MiB).
Zero-byte admission-gate reviews allocate no payload buffer instead of an unused 1 MiB each time.
A 128-review allocation ceiling and an editor retaining a writable memory map after closing its original
file handle are queued as additional native cases. The latter must be tested against the actual Windows
disposition behavior; [Microsoft documents mapped-view refusal and POSIX deletion semantics](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddk/ns-ntddk-_file_disposition_information_ex).

## Connected mutation coordination

The next `v34` affected candidate extends the native matrix from 34 to 44 cases. Saved-copy intent
creation and exact live Windows capture cleanup now acquire the same native-root writer gate as
working-file creation, captures and state revisions. A fresh private root must match the actual root
retained at capture creation. Root admission precedes the snapshot lock in both paths, so a saved-copy
attempt racing disposal cannot invert their lock order. Canceled intent admission writes no intent.

Cleanup admission failures retain the files but retire every live file/record/directory owner and the
active-copy permit. One close failure cannot prevent attempts to close later owners. Saved-copy
intent uncertainty and unsupported Unix exact deletion still retain their copies. The new cases cover
waiting/cancellation, fresh-root refusal, disposal races, permit/handle retirement, unsupported native
discard and a redirected new-root parent. They require guest execution; clean host builds are not runtime evidence.

Native Discard previously fell through to recursive pathname deletion. That path is now unavailable
for native records or a native-store composition until exact native discard ownership is implemented.
The legacy composition is unchanged. This closes an unsafe fallback, not the Discard implementation gate.
New-root creation also checks the native parent's path before creating a child and the returned child's
path before writing records or working bytes. No native store/profile is enabled by these changes.

The draft native accounting component measures retained default-stream bytes and conservatively charged native
metadata through held private namespaces. It includes working files, complete/interrupted captures, state records
and other ordinary files in the admitted session/capture/file layout. Unknown directory layouts, symlinks,
unreviewable native inventories, changed observations and exhausted review bounds refuse a complete result.
No saved JSON length, prior cleanup state, pathname prefix or age can refund a byte or authorize deletion.

The reader limits the complete traversal to 8,192 namespace entries, 128 sessions/captures, 8 GiB of charged bytes
and 128 MiB of metadata. These draft edit-store limits are separate from archive job/codec budgets and require
resource tests before enabling composition. Unix measurement includes bounded ACL/extended-attribute/origin data.
Windows uses attribute/security handles, so a default-stream data sharing lock need not prevent size observation.
It counts all streams from a complete bounded native catalogue without treating unfamiliar streams as eligible
for publication/cleanup, and charges the bounded catalogue/security descriptor. Payloads are not hashed just
to measure usage. Native namespace enumeration accepts an
operation-specific count and cancellation token, enforcing the bound while accumulating names.

The connected admission now holds a root-wide cooperating-process gate while observing usage and creating
working sessions, captures or revision records. A persistent zero-length native gate uses exclusive `flock` on
Unix or `LockFileEx` on Windows; a bounded, reference-counted semaphore also serializes same-process callers.
Acquisition is cancellable with a finite deadline. Root/name/object/metadata reviews detect replacement or
tampering; release closes the owned descriptor and never removes/recreates the gate file. Native root opens
require the private namespace, and existing two-/three-/four-argument constructor shapes remain available.

Working-file creation is lazy: the writer validates its source length/revision and passes that exact length to
the native output factory before any session directory or payload is created. Admission charges actual native
data/metadata, one extra possible output payload per capture and a 4 MiB bounded intent/receipt/output-metadata
contingency, plus a future source intent entry. Releasing a live snapshot permit does not refund retained bytes.
Fresh processes recount actual native files rather than believing a serialized completed/deleted outcome.
New allocations also check the exact held root's available filesystem space, with a 64 MiB margin; unavailable
capacity remains unknown and actual writes stay finite/checked. External editor writes remain external mutations
and require fresh bounded review. No claim is made about atomic global filesystem usage or an OS disk quota.

Core/Windows-platform compilation passes. The new 34-case native matrix covers working/captured/restarted data,
misleading records, native metadata/streams, exclusive data handles, unsupported layouts, all review/admission
budgets, known insufficient capacity, cancellation, same/different-root callers and real child-process lock
contention/death/quota races. Read-only snapshots assert no observation/refusal mutation. The combined `v30`
cohort runs this gate first and then existing affected checks and complete Core/Remote/App inventories on all
three platforms, requiring 309 additions over the preserved Core baseline and no lost prior cases/passes.
The first `v30` runs preserve Mac/Ubuntu 33 passes and one fixture failure: a no-mutation pathname reader tried
to acquire its shared lock while the test intentionally held the exclusive gate. The corrected fixture releases
that test hold before readback. Windows preserves one pass/33 failures because the new gate incorrectly used
directory-inheritance ACE flags. The correction creates its owner/SYSTEM protected file ACL without inheritance
flags and keeps the original strict file-security validator. Raw/source/binary evidence is verified with zero
owned instances on all systems. `v31` reruns the same combined milestone with these corrections; no failed result
is treated as passing and no test requirement is removed.

The corrected `v31` native matrix passes all 34 cases on Mac/Ubuntu and continues into existing checks/full
inventories. Windows passes 30 and exposes four failures while counting still-live capture directories: its
ordinary protected directory open intentionally excludes delete sharing, which conflicts with the live
creator's retained DELETE handle. Inventory now has a separate read/list-only open with delete sharing and
the same identity/private-parent reviews. Mutation/ordinary namespace opens keep their previous sharing rules.
The failed Windows bundle is sealed/verified with zero owned instances; the `v32` Windows retry is queued.
Mac/Ubuntu frozen `v31` sources remain untouched while their current complete runs finish.

When this record was written runtime qualification was pending and this component enabled no store. Both have since
closed: 13 November 2026: native edit storage is listed (`fda3f03c`, with the eighteenth review's fixes in `a4606689`). On
win-x64 (NTFS), linux-x64 (ext4) and osx-arm64 (APFS) ordinary startup composes this store, so a new edit is kept in
FileCat's own identity-bound folder; everywhere else the legacy store stays, including where FileCat's own data folder
is on another file system, is reached through a link, or cannot be held privately. An edit made before the listing stays
in the legacy folder, commits through its own route, and is still discarded through the legacy store's bound removal
(`a4606689`). The routes, their evidence and the three-platform runs behind them are in the
[route decision](RELEASE_ROUTE_DECISION.md) and the [status](IMPLEMENTATION_STATUS.md). The accounting recorded here is what that store
applies in production, so its budgets and its running-copy charges are now ordinary behaviour on a listed route, not
test-only; what a copy still being made is charged is below.

## What a copy still being made is charged (`dcf19885`)

A commit's capture takes the storage gate to admit its copy and to make the objects it needs - the capture's folder, its
payload folder, its own record and the file the bytes go into - and then lets the gate go while the bytes are copied, as
a creation has since `ef5c36d1`: a large member or a slow source never holds up another edit. Publishing the receipt
takes the gate again and waits longer for it, because those bytes are already made.

While the gate is down, nothing measures that copy as it grows. Its own record (`capture.json`, version 2) says how long
it was admitted to be, and every process charges the folder by that record instead: its two folders and two files, the
payload it was admitted for, and the room a retained copy of that payload reserves (that payload again, plus the 4 MiB
contingency). That is exactly what the same capture is charged once its receipt lands, which is the point. Charged for
less, two copies were both admitted into room for one and the 8 GiB budget was then exceeded by the whole of one copy:
the next review of the store threw, so the commit that had just finished could not write down its own new state, and new
edits were refused until a retained copy was reviewed (`dcf19885`). The reserved part is kept apart from the stored
bytes, among the running charges, so a record of work already finished is still judged without it.

The free space a copy still needs is counted only while a process is really writing it: the process making the copy
holds its output handle and can say what is left of it, and no other process can. A capture a crash or a failed copy
left behind is therefore charged - the review budget still judges what the store holds, and the user is asked to review
the retained copy - but reserves no free space. Reserving it held space for bytes nobody would ever write, which refused
later edits on a volume that had room for them and said the volume was full (`dcf19885`).

The other side of that choice, accepted: two FileCat processes copying at once are each admitted without seeing the
other's outstanding bytes, so on a volume with room for only one of them the second copy fails for want of space. That
copy keeps the user's files, says so, and is offered for review, and a retry once the first copy is done succeeds -
against a refusal that was permanent, misleading and needed no second process at all. Proving a copy is still coming
across processes would need a capture to hold a marker the way a creation does, and to be read through it; that is what
closing this would take. The same missing marker has a second consequence, recorded with it in
[the sessions record](ARCHIVE_EDIT_SESSIONS.md): on macOS a second window can remove a commit copy the first is still
writing, because no platform check refuses that there.

A capture record that does not name the folder it is in - a stale one, or one made by hand - speaks for no copy being
made here, and its folder is measured as the bytes it holds instead. An earlier build's record (version 1) carries no
admitted length, so a capture of that build is measured as before.

The application's composition pin judges the file system of the folder its run gives it (`FILECAT_EDIT_NATIVE_ROOT`, or
the run's temporary folder), not FileCat's own data folder: it proves the listing's rule - a listed route composes the
native store, an unlisted one keeps the legacy store - on the disk the lane runs on. Which file system a release
composes on is the route decision's question, and is answered by each route's own evidence.

`Compose` judges the file system where the native root will be created, which is its parent: FileCat's own data folder.
A file system mounted *at* the root itself is not judged, so a user who bind-mounts another file system there before
FileCat starts would get native storage on an unlisted file system (on Windows a mount point is a reparse point, which
the private lease refuses). Mounting one over a root FileCat already holds changes nothing it does, because every
operation goes through the held descriptor rather than the path. This limit is accepted and recorded here; closing it
would mean judging the root again after it is created.
