# Native archive moves

8 November 2026. The Windows native move component and shared accounting have passed their recorded live
follow-ups. No new archive writer is registered in application composition; Unix native moves and the remaining
implementation and qualification requirements are open.

`ArchiveNativeMoveSources` connects an explicit typed move to the captured native selection. Publication is
recorded before source removal. The backend verifies the saved archive's complete bytes before the first
removal, retains its native publication pin, and reviews its current namespace and metadata before each later
removal. Each source is reopened through its captured ancestor graph and reviewed before native deletion.
An archive itself cannot be a captured file input to its update.

Files are removed before directories, and directories in descending depth order. Removal never recursively
deletes a folder. A changed source, unarchived data stream, excluded item or failed child keeps its source or
containing folders. Excluded ancestors are tracked independently of the 128-item preview limit. Cancellation
and a failed publication-pin review preserve the saved archive and remaining sources. Root outcomes, progress,
journal steps and the saved-archive summary distinguish completed and partial moves; there is no automatic Undo.

The Windows candidate uses exact native deletion handles and a closed, bounded stream inventory. It accepts
only the primary file data and the reviewed origin stream, and checks that folders are empty before deletion.
Directory removal tracks last-write/change-time metadata after its own child removals. Unrelated changes,
including changes whose last-write time is restored, invalidate the next removal. The retained indexes and
current directory digests are charged to the capture budget; timestamp checks do not enumerate all remaining
children after every file deletion.

The first update matrix exposed a lifetime bug in this implementation: beginning the native content transaction
consumes the old source lease. Reviewing that disposed lease after a committed update incorrectly failed the
job while retaining the remaining source. The correction acquires a fresh reader under the
transaction's retained publication pin. If that additional reader cannot be acquired, publication remains
committed and sources are retained. All ten create/update profile pairs now pass with this reader lifetime.

The legacy ZIP move accounting also used Windows case-insensitive maps. These could merge distinct sources on
case-sensitive NTFS. Its source and child-accounting maps now use ordinal paths. Four new cases cover distinct
leaves or parents, with equal or different content. Windows' actual adapter passes all four; its complete legacy
move selection passes 23 cases and skips the Unix-only permission case.

## Validation

| Platform | Passed | Skipped | Evidence |
| --- | ---: | ---: | --- |
| Ubuntu x64 | 8,147 | 185 | Complete 8,332-case Core run |
| macOS ARM64 | 8,125 | 207 | 8,121 original full-run passes plus four corrected case-sensitive APFS cases |
| Windows x64, standard user | 7,876 | 461 | Complete 8,337-case Core run; unchanged binaries retained for the Windows-only follow-ups |

All six shared move-accounting cases pass on every platform. Mac's four initial new source-case failures were
fixture failures: its default volume resolved both spellings to one inode. The original failed TRX is retained;
the four exact cases subsequently pass on a task-owned case-sensitive APFS image. No C# source or binary changed
for that repair, and the image was detached. These are combined results, not a second clean full-suite run.

Mac and Ubuntu pass the inherited independent catalog, worker, native-vector, workflow and native-job readers.
Eleven applicable changed C# files match compiler checksums and ten DLL/PDB pairs bind on each. Final censuses
find zero owned FileCat/test processes, retain Mac's wake helper, and confirm Ubuntu's private keyring stopped.
The static checks are in `eng/validation/archive-writing/verify_native_move_checkpoint.py`.

Windows passes 23 native source-removal checks, including streams present before capture, and twenty actual
create/update moves across the ten container/password profiles, with origin marks and fresh assessment.
Independent readers verify six RAR archives and six 7z archives, each with 33 metadata records and 21 payloads;
the eight TAR variants contain 44 metadata records and 28 payloads. The inherited 197 native cases and ten
application-entry cases also pass. All 18 changed C# files match compiler sources and 17 DLL/PDB pairs bind.
The final census finds zero owned FileCat/test processes. The [checkpoint](native-archive-move-checkpoint.json)
records these results and their exact evidence hashes.

Original Windows failures retain actual source, binaries, compiler checksums and exit censuses. The strengthened
directory guard initially accessed a sealed-only input view while capturing; retaining the initial native
observation corrects that sequencing defect. The consumed-source-lease failure is recorded separately. A later
standard-user process startup failed before any C# test ran; the continuation preserves that exit record and
reruns only the startup/tests/readers phase against the same built binaries. A final evidence-tail continuation
also preserves the completed test/readback/source outputs, verifies their hashes unchanged, and repeats only
machine information, the exit census and packaging.

The guest drivers batch focused checks, native workflows, independent readers and source/evidence cleanup.
The Windows follow-ups reuse the full Core result only after every FileCat DLL/PDB in its actual test closure
matches the preceding frozen binaries. Other testers postpone guest work. Runtime testing stays on the VMs/Mac.

Still open: Unix native source deletion/update/confinement, automatic safe cleanup and
recovery UI, shared creation/member/edit/password UI, remaining resource/crash/fuzz/RID/package qualification,
and the final independent review. This component does not reduce those requirements.
