# Retained edit inputs in the legacy ZIP path

The native edit-store test factory can now commit its owned snapshot through the existing ZIP job
backend. Default application composition still uses the legacy store. This work does not register
new write profiles or qualify the full shared publication boundary.

## Implementation

`c0495960` gives the parent-only edit-input capability readonly retained-file observations and views.
A legacy ZIP request is restricted to the originating ZIP session, archive/member, one replacement
and its exact captured source label. It cannot turn that copy into a move, another source, another
archive/member or a native-profile request without the corresponding native review.

`ZipUpdateExecutor` admits captured metadata directly and reads through the retained stream. It
acquires no ordinary source pathname lease and never reopens that full path for input bytes.
Directory/name checks remain relative to retained native handles. The executor hashes all replacement
bytes while writing, checks the complete decoded output against the same length/SHA-256 and reviews
the retained input before publication. Missing or duplicate current members are refused, preserving
the archive for a separate review. A later editor save stays outside this commit.

The existing ZIP publication, outer metadata and origin-mark behavior is preserved. That compatibility
protocol is not a new native publication qualification. Save copy, SFTP retained-input integration,
discard/adoption, Unix cleanup and default factory wiring remain required.

## Regression found by the complete application inventory

Windows and Mac each exposed three `OperationHistoryRetirementTests` failures; Ubuntu exposed the same
three plus four Linux picture-fixture failures. The history defect was real: cancellation is deferred
while admission observers finish, so an observer's immediate Remove was lost before the job became
terminal. `6f7c6acb` retains that explicit removal intent and applies it after cancellation and source
ownership cleanup, preserving registration-before-finish ordering. Existing history assertions were
not changed; two Core cases additionally check owner, add/remove/finish ordering and unchanged files.

Ubuntu's picture fixture inherited a private SDK root whose serviced runtime is a symlink to the
loaded runtime installation. The sandbox admitted the loaded installation while the child apphost
searched the other SDK root. The actual production launch already clears that environment and sets
the verified runtime root. `feb9105e` matches that setup in the test and adds early-exit diagnostics.
An isolated guest probe reproduced exit 131 under the inherited root, then observed four threads,
two seccomp filters and the expected framed empty-input refusal with the production environment.
No sandbox permission was broadened.

## Live evidence and limits

Candidate `feb9105e267b06f837b4d86916d633b3f71c7426`, cohort `awe-storage-v16`, has passed these focused
gates on all three actual platforms while its complete Core/application inventories are still running:

| Gate | Windows | Mac | Ubuntu |
|---|---:|---:|---:|
| Retained legacy ZIP cases | 18 | 18 | 18 |
| Job retirement ownership | 29 | 29 | 29 |
| Operation history retirement | 18 | 18 | 18 |
| Linux picture boundary | Platform-specific | Platform-specific | 12 |

The ZIP cases cover empty/multi-buffer payloads, exact readback, outer origin and existing member/archive
comments/attributes, late editor saves, explicit rebase, stale targets, changed/replaced/disposed inputs,
request redirection/move refusal, inconsistent stream data and missing/duplicate members. A filesystem
hook throws if the ordinary source pathname lease is used. The first Windows cohort (`v15`) passed
14 cases and hit four fixture sharing denials: its browsing index was still open when it attempted
an independent in-place archive edit. The corrected fixture releases that index first.

The completed failed inventories and first ZIP iteration remain in the
[progress checkpoint](edit-capture-recovery-progress.json). Current `v16` results still need final
sealing, exact compiler/binary verification and complete-suite review before a completed checkpoint.

The full Core comparison retains five explicit case migrations from `c68ec836`: oversized metadata
now remains visible for read-only recovery; unchanged-byte version-1 checks became part of the
version-1/2 matrix; the future-version case moved from 3 to 4 when schema 3 was implemented. Every
other prior case/pass must remain, and all five mapped successors must exist exactly once. This is
an exact name/contract mapping, not a filter excluding tests. The new full inventory requires 153
additional cases over `awe-v3` and records all missing cases or lost prior passes.

Complete the remaining native edit ownership/integration, actual editor/desktop/accessibility,
resource, fuzz, RID/package/CI and final independent-review gates. This component's focused results
do not enable the native-store factory or complete the overall archive-writing plan.
