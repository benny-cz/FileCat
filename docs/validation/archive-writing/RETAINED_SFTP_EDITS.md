# Retained native edit uploads to SFTP

`b3ef4f13` connects native edit snapshots to the SFTP upload executor. The default application still
uses the legacy edit store. This component does not register the native store or qualify archive profiles.

## Implementation

An internal, in-process `IRetainedEditUpload` capability is attached to the commit request. It retains
the actual native snapshot and authorizes one Copy to the original session's connection profile,
absolute parent folder and filename. Source substitution, another profile/folder/name, multiple sources,
a move, local destination or missing approved destination revision/SHA-256 is refused before dispatch.
The store also checks that the copy belongs to the supplied session record.

SFTP obtains metadata from the retained observation and reads bounded views. It does not use the ordinary
local-path counting, link inspection, source lease or byte-open routes. Every attempt hashes the bytes
actually consumed and requires the captured length/SHA-256, including the end-of-content probe. An
interrupted attempt starts again with a fresh retained view; it cannot seek past unverified input.
Complete remote-stage readback is mandatory for these edits even when the ordinary transfer verification
choice is Native. The held snapshot is reviewed before publication and again before replacement or the
non-atomic recovery-backup mutations. SFTP preserves its existing destination revision/hash rechecks,
backup, recovery and cancellation behavior. Its remote check-and-rename protocol is not an atomic
server-side compare-and-swap.

The 23 native-file cases cover empty/multi-buffer edits, later editor saves, changed/replaced/disposed
captures, request redirection/move refusal, metadata-preserving destination changes, changes during
upload, inconsistent source streams, corrupted server bytes, cancel, reconnect/retry and non-atomic
replacement. Guarded filesystem methods fail if the captured source uses an ordinary pathname route.

## Evidence and corrections

The first Mac/Ubuntu cohort, `awe-storage-v17`, passed the data/publication assertions in every case but
failed 13 final cleanup assertions: returning a lease correctly leaves an idle channel in the reusable
connection pool. The fixture now disposes the connection owner before requiring zero channels. All raw
failures remain preserved, with compiler-source/PDB/DLL bindings, in the
[progress checkpoint](edit-capture-recovery-progress.json). Windows did not run this known fixture iteration.

The prior `awe-storage-v16` complete inventories are preserved too:

| Platform | Core passed / skipped / failed | App passed / skipped / failed |
|---|---:|---:|
| Windows | 8,089 / 594 / 0 | 2,810 / 118 / 1 |
| Mac | 8,471 / 207 / 0 | 2,739 / 190 / 0 |
| Ubuntu | 8,493 / 185 / 0 | 2,741 / 188 / 0 |

The Windows App failure checks the selected-file text after the reopened checksum modal reports IsOpen.
Waiting for the required text within the fixture's existing bound did not fix it on Windows. The
`d503e641` diagnostic records both displayed text and the retained report: Defender refuses the
deliberately deceptive executable filename with a native virus-policy error, and FileCat correctly
shows that the file could not be read. Candidate `07f45d8c` keeps that executable case, requires the
actual native `0x800700E1` refusal before accepting its unreadable outcome, and adds a data-file variant
that must be intact. Both variants require an escaped title/status/details and reopening the actual
retained result. The checksum UI inventory expands from 12 to 13 cases. All prior Core cases/passes are
preserved after the five explicit schema/contract name migrations, with exactly 153 additional cases.

Candidate `55bb1a22`, `awe-storage-v18`, passes all 23 retained SFTP cases on all three platforms.
Mac/Ubuntu additionally pass the checksum UI gate (11 passed, one platform-specific skip) and continue
their connected native storage/edit regressions and complete Core/Remote/App inventories. Windows
stopped at the repeated checksum assertion; its failed sealed evidence and the `v19` diagnosis are
preserved. Candidate `07f45d8c`, `awe-storage-v20`, passes the Windows retained SFTP gate (23) and
expanded checksum UI gate (12 passed, one platform-specific skip); its full inventories remain active.
Mac/Ubuntu must finish and preserve `v18` before receiving that test expansion. Final
qualification remains pending.

## Remaining qualification

Actual native-store application composition, real editor/SFTP desktop/accessibility acceptance,
native Save copy, safe discard/adoption, Unix cleanup, restart recovery actions/UI, retained-disk and
global resource bounds, remaining archive worker/confinement/fuzz/RID/package/CI gates and the final
independent review remain required. Passing this component cannot enable the native-store factory.
