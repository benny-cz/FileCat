# Native edit storage accounting

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

Runtime qualification remains pending. Exact cleanup, recovery/UI composition, crash/durability/resource qualification
and the wider plan remain open. No store or archive write profile is enabled by this component.
