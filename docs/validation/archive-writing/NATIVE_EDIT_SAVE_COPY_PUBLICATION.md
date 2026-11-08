# Native saved edit publication

The in-progress native component connects [saved-copy preparation](NATIVE_EDIT_SAVE_COPY_PREPARATION.md) to
exclusive ordinary-file publication and bounded immutable recovery receipts. It borrows the retained snapshot
and native private destination stage; it does not turn an ordinary file into a codec-verified archive.

One preparation gets one publication owner. The native stage issues a live name review that binds its exact
object, bytes, full observed metadata, origin and target leaf. Reviews cannot be used with another stage, replayed
after a claim, or used after a later review supersedes them. Windows uses the retained handle's no-replace rename;
Linux/macOS use their existing native exclusive name recipe, retaining post-syscall reconciliation and its
filesystem qualification limits. The complete private ACL/mode, exact timestamp and opaque origin remain checked.
An existing target is retained, and any uncertain rename/name change grants no rollback or deletion authority.

Before attempting the name claim, the coordinator writes a fresh native receipt, flushes/seals it, completely reads
it back and checks the retained object again. A separate immutable outcome receipt records either a confirmed
publication or a need for review. Both records bind one nonce, source/output identities, destination parent,
private/public names, exact length/hash, metadata/origin hashes and timestamp. Each record is limited to 64 KiB;
destination-bound space accounting reserves both. They contain no password, archive contents or source pathname.
The strict reader rejects unknown versions/fields, duplicate/missing fields, invalid scopes, malformed names,
inexact timestamps and oversized/truncated input. Deserialization never creates native mutation authority.

Once the rename may have occurred, reconciliation and outcome recording run without cancellation. A positive
native observation remains positive if a later receipt fails; the result carries the error and whether the outcome
receipt exists. Final receipt/target reviews can add a recovery warning without manufacturing an uncommitted
result. Failures before name claim keep the private stage and any receipt. Disposing result/receipt owners closes
handles and never deletes published, private, substituted or uncertain files.

The receipt-specific native operation flushes its sealed retained file and checks the parent/name again.
On Unix it also fsyncs the held directory before publication, ordering the new receipt name before the rename.
Windows uses its retained-file flush. Actual process-crash and power-loss/filesystem qualification remain separate
requirements; a returned flush syscall does not establish those gates.

The native matrix has 25 cases for bytes/empty/multi-buffer files, exact origin/time, late editor saves,
collisions/late appearance, changed sources/stages/directories, substituted records/targets, cancellation before
and after name claim, receipt creation/flush failure/corrupt readback, post-rename errors and live-review replay/redirection.
User-selected names follow native limits: long multibyte Windows names and ordinary Unix punctuation do not
inherit the stricter extraction/private-session naming restrictions. Their native publication is included.
Twenty strict receipt cases run on each platform. The initial `v24` runs preserve 19 Windows / 16 Ubuntu fixture
failures: ordinary pathname assertion readers used incompatible Windows sharing, and Ubuntu's default umask made
the test destination group writable. The strict record20 gate passes on both. The fixture correction creates new
Unix test directories as 0700 and uses explicitly shared read-only assertion readers while native owners remain
held. One additional Unix case explicitly requires the original group-write refusal. Product code is unchanged.
The corrected `v25` packet passes publication25 Windows / 26 Ubuntu and record20 on both, plus every connected
focused gate. Exact raw bundles/compiler-source/DLL/PDB bindings are preserved and verified with zero owned
instances. All 26 Windows / 22 Ubuntu product DLL/PDB files are byte-identical to the failed `v24` candidate;
the correction changes fixture code only. It requires the same 26 native cases on Mac after the previous full batch finishes.
Complete inventories are explicitly not run for these changed runtime bytes; the preceding `v23` complete
qualification remains attached to its own candidate. Mac receives the corrected candidate after its current full run.

Native store registration and the `EditSessionStore.PublishSaveCopy` guard remain unchanged. Existing-target
replacement, native receipt discovery/linking to session recovery, fresh restart actions, exact cleanup and crash/
durability qualification, job/UI integration and the broader plan remain required before enabling the workflow.
