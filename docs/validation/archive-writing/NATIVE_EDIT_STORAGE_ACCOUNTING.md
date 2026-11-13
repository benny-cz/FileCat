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
[route decision](RELEASE_ROUTE_DECISION.md) and the [status](IMPLEMENTATION_STATUS.md). The accounting below is what that store applies in
production, so its budgets and its running-copy charges are now ordinary behaviour on a listed route, not test-only.
