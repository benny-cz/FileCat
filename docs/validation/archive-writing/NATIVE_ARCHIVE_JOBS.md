# Native archive jobs

8 November 2026. Native job component implemented and exercised on all three platforms; no new writer is registered in application
composition. This follows the typed job/password component in `03107264`.

`ArchiveNativeJobBackend<T>` connects actual manager jobs to strict assessment, native source capture, shared
workflow review, role-bound transform/verification workers and native publication. Existing targets require a
complete command-time native observation as well as the legacy content baseline. Execution reacquires native
authority and checks identity, bytes, timestamp, origin and security metadata before parsing. The native
observation is internal and is not serialized as reusable authority.

`ArchiveSelectionForest` freezes all source roots before opening a native parent, binds captures to the source
specification and checks expected file/directory kinds. Numeric root selectors distinguish equal leaf spellings
in different folders. Native ancestor paths remain separate from archive destination names. The current forest
ceiling is 16 retained parent directories; names, nodes, source specifications and planning indexes share the
retained-input budget. This is a bounded implementation, not final process/resource qualification.

`ArchiveJobPlanning` maps raw names and explicit duplicate occurrences into catalog-bound edits and explicit
recursive scopes. Source paths stay in the parent. Ordinal lookup uses indexed file groups; repeated folder/key
edits refuse before repeated expansion. Replacement, exclusion and 7z precision decisions precede staging and
default to cancellation. Exclusion counts come from the actual captured selection. Name previews are bounded,
quoted and escape control/bidirectional characters; combining spellings remain distinguishable.

`WindowsArchiveJobBackend` uses the actual AppContainer application worker and existing NTFS creation and
transactional content-update recipes. It preserves target identity on updates and retains exact old bytes in
the native snapshot. Creation and update currently retain private transaction evidence for reviewed recovery;
jobs report that committed outcome with a warning. Cancellation arriving after publication still records the
saved archive before releasing the operation password.

The first focused run passed 79 and failed 13 of 92 cases on each platform. Three 7z cases used deliberately
fine fake directory timestamps without accepting the declared conversion. Ten cases exposed a real limitation:
the format planners rejected recursive relocation of an existing duplicate group. The correction retains source
name provenance: a complete original group can move together, while partial relocation, combining distinct
original names, new duplicate additions, file/folder clashes and file-as-parent namespaces remain rejected.
The failed runs are frozen with their actual binaries, compiler sources, checksums and cleanup censuses.

The corrected combined batch includes expanded focused coverage, native manager creation/read-back on Mac and
Ubuntu, Windows manager creation/update/snapshot checks through confined workers, stale native baseline refusal,
native source aliases and committed-output cancellation. The [verified checkpoint](native-archive-job-checkpoint.json)
records 81 new core cases and 14 new Windows native cases, with these final live results:

| Platform | Full core passed | Skipped | Focused passed | Native job preflight |
| --- | ---: | ---: | ---: | ---: |
| macOS ARM64 | 8,113 | 207 | 114 | 12 |
| Ubuntu x64 | 8,135 | 185 | 114 | 12 |
| Windows x64, standard user | 7,868 | 457 | 114 | 14 |

Windows also passes 177 native transaction/worker cases and ten application-entry cases. It discovers five
additional PE-inspector cases, as in the preceding checkpoint. Unix native jobs here use ordinary probe
subprocesses; they do not qualify production Unix application confinement or updates.

Each platform passes the inherited independent catalog (273 archives), worker (74), native 7z (21 Unix/33 Windows)
and workflow (20) checks. The job readers additionally verify 11 Unix or 20 Windows archives, including every
RAR/7z password mode, native create/update pairs on Windows and committed-output cancellation on Unix. Exact
metadata and payload counts, official-reader Zstandard limitations and pinned fallback coverage remain in the
checkpoint. All applicable changed C# files match compiler source checksums: 18 on Mac/Ubuntu, 21 on Windows.
Ten DLL/PDB pairs bind on Unix and 17 on Windows. Final censuses find zero owned FileCat/test processes, the Mac
wake helper is preserved, and Ubuntu's owned private keyring daemon is stopped.

The follow-up exposed test-fixture defects: a stale expected filename, an undisposed caller-owned manifest stream
that blocked Windows teardown, and optional system-store preflight skips incorrectly treated as failures by the
driver. The original failed phases are retained with actual binaries, PDB bindings, compiler-source hashes and
exit censuses. The source-stream leak is fixed and Windows' 14 actual native job cases now pass.

Independent reader helpers also needed a distinct job matrix: the older workflow matrices deliberately require
different filenames and counts. The added `--jobs` checks keep the original workflow assertions, require every
expected format/password/operation tuple, and check exact metadata and payload counts. A reader-only continuation
retains both helper versions and the original refusal, pins the corrected helpers, proves all exported archive
bytes unchanged, then runs the final source audit and process census. It does not rerun unchanged C# suites.

## Validation process

The reusable guest driver runs early focused/native checks, full core regressions, Windows native/application-entry
checks, independent catalog/worker/workflow/job readers, compiler-source/PDB binding and a final process census in
one batch. Separate idle machines run concurrently. Ubuntu uses an owned private bus/keyring and verifies its
service PID before tests, then stops that daemon; the desktop keyring is not an implicit test dependency.

Checked deltas retain the preceding raw source bytes and source ledger. Fixture-only changes rebuild affected test
projects while retaining hash-verified application/probe assets; a driver-only Windows correction also verifies
the unchanged test DLL/PDB files against the sealed previous phase. Validation-only reader fixes can continue
against immutable archives and binaries. Production changes still require affected tests and the final inherited
gates. The driver, reader-only continuation and static checkpoint verifier are retained in
`eng/validation/archive-writing/run_native_job_batch.py`, `resume_native_job_oracles.py` and
`verify_native_job_checkpoint.py`. Use the continuation only after the original batch is terminal and the machine
is idle. These changes reduce transport, build and repeated-test work without changing pass criteria or treating
skips/refusals as passes. Runtime tests remain confined to the Windows/Ubuntu VMs and Mac.

Still open: automatic safe creation cleanup; recovery UI coverage for every backend; native source deletion/F6;
production creation/member/edit/password UI; Unix update and confined application launch; aggregate resource,
crash/fuzz and RID/package gates; and the final independent review. The candidate currently refuses move intents.
These are incomplete requirements, not a reduction of the controlling implementation plan.

The next native/UI batch must also remove the TAR-only format filter in `ArchiveCleanupReview.CanOffer` when
the RAR/7z recovery journeys are connected and exercised. Their native update cleanup catalogs already exist;
leaving the UI filter in place would hide a required recovery action for those formats.

The later [native source-case follow-up](NATIVE_ARCHIVE_SOURCE_CASE.md) fixes the Windows path maps that merged
distinct source spellings on case-sensitive NTFS. Its twenty actual application-worker cases cover distinct
files and parents for every shared container/password profile, alongside exact independent readers. Sibling
target/source admission and legacy/native F6 source accounting remain separate qualification work.
