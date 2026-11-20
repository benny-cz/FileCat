# Automatic archive cleanup and creation recovery

8 November 2026. The focused cases, complete Core regressions, inherited native cases and independent readers pass
on their recorded platforms. No new writer is registered; complete workflows and qualification remain open.

The Windows candidate cleans its four creation sidecars and six update artifacts automatically after verified
publication. Update cleanup follows any requested F6 source removal. A cleanup failure keeps the committed archive,
recorded outcomes and recovery tracking. A final archive-review failure after the last unlink keeps tracking open
even when no sidecar remains; the job reports a committed result with a review warning.

Creation cleanup has a bounded, ordered catalog tied to the preparation scope, published object, metadata and
origin. Every native unlink follows a durable intent. Recorded deletions and freshly observed absences use separate
outcome masks. Fresh recovery retains the actual journal, parent, target and remaining artifacts. A pathname,
identity string or digest alone supplies no deletion authority. Cleanup, closure and disposal are serialized.

Retained-file review supports complete TAR, RAR5 and 7z catalogs. Creation review describes the kept archive and
sidecars without a nonexistent original-byte backup warning. Approval defaults to Cancel. Reports distinguish
successful cleanup from confirmed tracking closure. Passwords are unnecessary for reconciling the exact outer
archive and private sidecars; their payloads are not modified by this action.

A shared Windows recovery identity parser rejects malformed or noncanonical native IDs with a controlled
NeedsReview result. Creation cleanup, content cleanup and restoration use it. Malformed journal values therefore
cannot escape the UI's expected error handling as numeric parsing exceptions.

## Validation

The [checkpoint manifest](native-cleanup-checkpoint.json) records the exact evidence archives and preserved failures.
Current C# source checksums bind 23 files and 15 DLL/PDB pairs on each Unix platform, and 29 files and 17 pairs on
Windows. Every final census records zero remaining owned FileCat/test processes.

| Platform | Core passes | Core skips | Full-run evidence |
| --- | ---: | ---: | --- |
| macOS ARM64 | 8,151 | 207 | Clean 8,358-case v4 run; v7 reruns the 26 affected fixture cases with identical Core/worker dependencies |
| Ubuntu x64 | 8,173 | 185 | Clean 8,358-case v4 run; same explicitly labeled v7 follow-up |
| Windows x64, standard user | 7,902 | 461 | Clean 8,363-case run |

The focused checks comprise:

- 26 Core cases: catalog/history tampering, journal prefixes, stale snapshots, cancellation, changed objects,
  unrecorded unlink and final target-review failure after every unlink.
- 27 headless application cases: existing cleanup behavior, creation across six formats, RAR/7z update recovery,
  cancellation, changed targets, missing sidecars and approval/report behavior. All pass on all three platforms.
- Ten Windows format/password cases, each performing default creation, update with source movement, and retained
  creation followed by fresh native cleanup: 30 actual confined-worker jobs. All ten cases pass.
- Eighteen externally killed Windows recovery processes: opening, each sidecar's intent/unlink/outcome, absent-file
  reconciliation, failure before/after unlink, completion and journal closure. All pass.
- Thirteen Windows identity-parser cases: exact canonical roundtrips and controlled malformed-value rejection.
  All pass.
- Four real NTFS application approval cases cover creation/update approval and cancellation through production
  adapters; all pass alongside ten application-entry cases. Portable UI cases do not claim Unix native deletion.

The 207 inherited Windows native cases pass. Independent readers check the inherited 273-case catalog, 74 worker
outputs, native vectors, workflows/jobs and twenty moved outputs. They also check twenty cleaned outputs: six RAR5,
six 7z and eight TAR-family archives, across all ten format/password profiles. The checkpoint manifest and
[static verifier](../../../eng/validation/archive-writing/verify_native_cleanup_checkpoint.py) bind actual TRX cases,
source/PDB checksums, binary hashes, archive hashes and machine census.

The Mac full Core run uses this task's private case-sensitive APFS volume for case-distinct move fixtures. Its
until-reboot wake helper remains active. Linux uses a private bus and keyring, stopped afterward. Runtime tests run
only in the guests or on the Mac; host checks are builds and static evidence verification.

## Process and preserved failures

Checked source packets require sealed predecessor ledgers before edits. Focused preflights run before expensive
regressions. The two synthetic journal-reopener fixtures needed a Windows sharing-order correction. Their Unix
follow-up reuses the complete run only after proving that the entire Core/worker binary dependency closure is
unchanged; it reruns all 26 affected cases and all 27 UI cases. Copied archive vectors retain their hashes and are
checked again by independent readers. Unix source/PDB checks now include the application test binaries. Reused
results are labeled explicitly and never presented as another newly executed full run.

Failed phases remain sealed with actual results, binaries and compiler-source checksums. They exposed an invalid
metadata fixture, a live/recovery journal-accessor mismatch, a premature UI focus assertion and Windows fixture
sharing order. Successful follow-ups supersede these results without erasing them.

## Remaining work

Unix exact cleanup/update/source removal and confinement, complete shared creation/member/edit/password UI,
remaining resource/crash/fuzz/RID/package gates and the final independent review remain required. Unsupported Unix
cleanup retains its artifacts. Interrupted source-move and large-manifest recovery scopes also need their own
complete review flow. Restoration UI is separate from keeping an archive and reconciling its retained files.
