# Shared archive creation UI

Component validation passes on `codex/archive-writing`. New profiles remain unregistered; this is not
release qualification. The controlling requirements are the [implementation plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md).

The creation command captures the source selection, source location and target folder before showing a dialog.
The dialog offers ZIP and explicitly registered formats, with effective compression options, automatic filename
suffixes, 7z methods/filters/dictionaries/header options, and masked password confirmation for RAR5 and 7z.
Manual suffix conflicts remain editable. Unreadable, self-containing and existing non-ZIP destinations without a
review-capable backend preserve the draft for correction. The [target-review follow-up](ARCHIVE_TARGET_UPDATE_UI.md)
connects registered review-capable destinations to an existing-archive update. Existing ZIP destinations retain
the established add workflow. Focus after completion
belongs to the captured source location and never moves a listing that the user has navigated elsewhere.

Password validation preserves exact Unicode and whitespace. RAR5 uses the shared 1,024-byte UTF-8 bound; 7z uses
the same byte bound with UTF-16LE. The UI refuses mismatches and overlong encoded values before submission.
Passwords are absent from public profiles and journals, transferred to one job owner, cleared from the form on
cancellation or successful transfer, and wiped after the job. The manager validates the same encoding bounds.

Two UI defects found during the live loop are fixed. Delayed `TextChanged` events mistook the initial suggestion
for a manual edit and prevented subsequent format changes from changing `.zip`. Synchronous property changes now
preserve the distinction. Reopening a refused destination attempted to reparent controls still owned by the old
dialog; each overlay now receives a fresh wrapper that releases the reusable draft on close. Application
regressions also cover retaining a draft after metadata errors. Explicit legacy ZIP replacement is marked as a
dangerous action while Skip remains the default.

The guest batch checks 21 password cases, two early creation smoke cases, 20 portable creation/UI cases and the
114-case affected application regression selection. Its complete Core pass compares the previous case inventory
and preserves every previous pass. Windows additionally creates ten real native archives through the actual
application worker, covering all six new containers and all RAR5/7z password modes, then performs fresh confined
assessment, provenance checks, cleanup checks and independent payload/metadata readback.

The initial stalled run, bounded diagnostic failures, suffix failure, refused-draft failure and Windows full-disk
build failure are retained separately. A duplicate Windows build-log creation in the validation helper was fixed;
the successful builds and frozen runtime assets were explicitly revalidated before continuing without a rebuild.
The corrected helper also checks for 2 GiB free before applying future packets, so a full test disk refuses before
source mutation or build work. This helper guard was added after the frozen v5 candidate; the recorded v5 tests
still bind the exact executed helper and C# inputs.
Only VMs and Mac execute tests or archive readers. Host verification reads sealed artifacts and compiler inputs.

The same 20 creation UI and 21 password cases pass on all three platforms. The affected application selection
passes 113 cases with one platform skip on each Unix platform and all 114 on Windows. Complete Core results are:

| Platform | Passed | Skipped | Cases |
|---|---:|---:|---:|
| Windows VM | 7,915 | 461 | 8,376 |
| Mac | 8,164 | 207 | 8,371 |
| Ubuntu VM | 8,186 | 185 | 8,371 |

Windows also passes ten actual native creation UI cases, 217 native regressions, 18 creation cleanup process-crash
cases and 14 application-entry/cleanup UI cases. The independent readers pass the inherited catalog/worker/native/
workflow/job/move/cleanup exports and all ten new UI-created archives. Mac and Ubuntu sealed evidence passes the
static verifier against all 12 changed C# sources and 15 DLL/PDB pairs, with zero owned test instances and the
Mac wake helper retained. Windows sealing resumed after the other tester's rendering process exited; the passed
application tests and archive readers were retained without repetition. The [checkpoint](creation-ui-checkpoint.json)
records all three sealed bundles, original failed phases, compiler inputs, counts and final process census.

Shared member/update/edit/password journeys, Unix native update/confinement,
the full resource/fuzz/RID/package gates and the final independent review remain unfinished.
