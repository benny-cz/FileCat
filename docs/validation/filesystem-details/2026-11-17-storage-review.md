# Exact storage review and private Windows evidence — 2026-11-17

This continues the [storage journal checkpoint](2026-11-17-storage-journals.md) on `main`, HEAD `68a435dc2a1d6122f5961f867bcb653b0e6968aa`, with uncommitted implementation changes. WP3 remains **partial**. Windows App composition now supplies a private native journal creator. WindowsPlatform still supplies NoStorageTransforms and StorageTransformExecutor still refuses native execution; no storage command or acknowledgment UI is enabled. EFS remains required by the user's full goal.

## Core protocol

StorageReviewedJournal binds an owned directory identity, journal name and actual held file identity to its complete byte length/SHA-256 and bounded parsed history. Complete hashing uses a fixed 64 KiB buffer and is independent of the parser's readable prefix. Damage, unknown schema and historical effect uncertainty survive acknowledgment. The opaque association for an unreadable name is distinct from a known job ID.

StorageRetainedReviewReport and StorageReviewAcknowledgment belong to Core. Acknowledgment requires the explicit typed review choice, a fresh exact snapshot and writer exclusion. It writes/flushes/publishes a new bounded report, reopens and verifies that actual report, rechecks the journal, then writes/flushes/publishes and verifies the matching acknowledgment. The acknowledgment binds the report's native identity, length and digest as well as the journal snapshot, last readable sequence/schema and uncertainty. A report reference is one validated private artifact name, never an external pathname or authority over historical user files.

An existing identical review is idempotent. A deliberately revised review gets a new immutable report/pair; old pairs retire only after the new pair is verified. Failed acknowledgment deletion keeps its report protected. The retained pair fits 1 MiB, with 2 MiB for retained artifacts plus publication overlap. The per-operation artifact inventory is capped at 4,096 entries; exceeding it refuses a new save rather than materializing an unlimited directory. Recognized unreferenced temporaries and valid orphan reports are reconciled under the same journal owner before retry. Unknown artifacts and failed deletions remain retained and charged; repeated failing retries stop before exhausting the finite allowance.

An ambiguous acknowledgment publication failure is latched in the current process and its exact owned artifact is retired where possible. Readable bytes from that failed attempt cannot clear review in that session. A later retry reconciles the known failed artifact. After restart, surviving complete pairs are independently reverified from disk; partial/temporary records never qualify. The failure registry is bounded.

PruneStorageJournal requires a fresh exact snapshot and either the shared collector's clean terminal proof or a matching report/acknowledgment pair. It removes the journal first through the owned object. A failed journal deletion does not release or delete its report dependency. These are Core/owned-evidence APIs; ordinary Scan/IsInterrupted/UI/retention integration is still pending below.

## Windows authority and ownership

WindowsStorageReviewEvidence walks local NTFS paths component by component, without following reparse points. It rolls directory handles instead of retaining an ancestor tree. It binds actual volume GUID plus native serial/file ID, refuses observed managed/unavailable/encrypted directories and unqualified volumes, and verifies the final namespace is invoking-user owned and not writable by unrelated identities. Existing SYSTEM/Administrators namespace rights are permitted; newly created evidence files have a protected DACL containing only invoking-user and SYSTEM full access.

The inactive journal owner uses exclusive sharing. Files must be ordinary, single-link, private, on the held volume and contain only their unnamed data stream; observed EAs/additional streams and unsafe metadata refuse review/mutation. Identity checks also revalidate the held file role. Artifact inventories borrow the same bounded directory cursor and compare each opened metadata object's identity/length with its cursor entry. Opens, publication and deletion use held directories/files; no path-based deletion or publication fallback is present. This is an observed-churn/object-bound contract, not a universal metadata/alias-exclusion guarantee.

The independent evidence envelope now has three concrete handle slots and retains its semaphore until every started handle actually retires. The native review path holds journal ownership plus a directory/cursor and one artifact at a time. Native tests assert at most three charged handles. The worker's private creator uses its own scratch slots, never waits for that envelope, and hands its file/charge to JobJournal. Its recognizable namespace entry is flushed before durable intent writes. Artifact file/directory flushes and held rename/delete operations preserve publication/retirement ordering.

The writer requests no DELETE access, allowing the existing shared live-prefix reader to coexist with it. Inactive review/pruning and private artifact retirement acquire their separate required rights. A real native fixture blocks a later writer flush and reads the prior exact published prefix through the independent reader.

Interop basis includes Microsoft's [NtCreateFile contract](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntcreatefile). File opens use OPEN_REPARSE_POINT and OPEN_NO_RECALL. The first native fixture rejected the latter combined with this directory-open form; directories use the already qualified metadata-relative form without that option. Neither the option nor this receipt claims absence of all provider callbacks/network activity. Remaining provider/adversary qualification stays open. Directory/file flush success below is the tested OS boundary, not a hardware power-loss guarantee.

## Verification

All builds completed with **zero warnings/errors**. Final focused Core selection: **233 discovered** on each platform, **232 passed/one missing-mount fixture skip on Windows**, **231 passed/two platform or fixture skips on Ubuntu and macOS**, zero failures. It adds StorageReviewAcknowledgmentTests (14 cases) and StorageEvidenceEnvelopeTests (2) to the previous 217-case contracts/journal/call-runner/allowance/scheduler/retirement/Undo/archive lifecycle selection.

The new Core cases cover exact complete hashes beyond damaged prefixes, unknown-name association, idempotence, revised immutable reports, stale snapshots, replaced/tampered/missing reports, unknown schema/external references, faults at each report/acknowledgment flush/publication boundary, ambiguous readable publication plus failed cleanup, bounded retry growth, journal-first pruning and delayed slot retirement. These are protocol/fault fixtures with owned in-memory evidence; they are not native disk-full or crash tests.

The disposable Windows 11 x64 NTFS **normal-user** run passed **24 native tests**, zero failures/skips. Its log confirms medium integrity (`S-1-16-8192`). Five new WindowsStorageReviewEvidenceTests cover:

- Private disk report/acknowledgment, reopen/revalidation, journal-first pruning, protected DACLs and the three-slot bound.
- Active writer, multiply linked journal and reparse-path refusal, with untouched history.
- Foreign namespace write permissions and additional-stream refusal.
- The actual manager worker's private-journal handoff and terminal ownership release, while execution remains refused and user payload stays untouched.
- A live private writer blocked in a later flush while the earlier exact durable prefix remains readable.

The earlier 19 Windows inspection/traversal/compression ABI/cursor/call-journal cases also passed under that same normal-user run. Developer-mode symlink qualification was enabled only in the disposable VM. No workstation protection setting was changed.

The focused App Operations/Undo selection discovered **75 cases**: **74 passed/one skip on Windows**, **73 passed/two skips on Ubuntu**, **72 passed/three skips on macOS**, zero failures. These runs include the updated App composition.

Final transport archive SHA-256 values (retained in `.tmp`, not committed):

| Artifact | SHA-256 |
|---|---|
| filecat-storage-review-source.tar.gz | `359E5522AADFD790FD665FACD2767A3086F87C4F6E884D39280AF8FD9FFA67B1` |
| filecat-storage-review-native-patch.tar.gz | `AFA5F7EECAFC4DAF1F09BA4BCDEDADECF5E77F06EE46BF85B38B06C1D4065B20` |
| filecat-storage-review-core-binaries.tar.gz | `5DF0A70E9E1C622A018616E67BCD71D9CE622B3B932127BCFFE7B13734A7A479` |
| filecat-storage-review-ui-patch.tar.gz | `2D776AFAC38B2AB63E89197BE6E46A70AE19088229E56EB94CFC82D5D0CF9BFB` |

Markers: FILECAT_STORAGE_REVIEW_FINISHED on Ubuntu/Mac, FILECAT_STORAGE_REVIEW_NATIVE_FINISHED for Windows native, FILECAT_STORAGE_CORE_FINISHED and FILECAT_STORAGE_UI_FINISHED for Windows Core/App.

## Remaining acceptance

Integrate exact acknowledgment into every recovery/closure/query/census/retention caller and the existing Operations UI. The current census still uses ordinary filesystem opens and does not yet reduce reservations for verified acknowledgments or recognize validated retained pairs after journal pruning; do not expose the new prune operation through product retention before those routes agree. Finish standalone retained-report loading, durable dependency reconstruction and ordinary age/count/byte pruning, with failed deletions charged. Qualify cross-instance changes, real disk-full/device-loss/crash boundaries, large/corrupt artifact inventories, and the entire 256+188+1+3 resource accounting. UI must acquire evidence ownership before scheduling owned I/O, release it before native current-object observations, and handle cancellation/adoption without early retirement.

Then connect bounded traversal, overlap/closed-ID attribution and final root accounting to the call runner; integrate review commands, same-session Needs review, captured origin/marks/refresh and truthful Operations/report output. Remaining C native/provider/performance/UI gates, EFS identity/key/output work and the companion filesystem plan's outstanding collectors/output/native gates remain part of the active goal. T-REC.C/T-LIMIT.C are partial, not release passes.
