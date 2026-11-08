# Existing archive review and pack updates

This component connects Pack to updates of explicitly registered TAR, TAR.GZ, TAR.BZ2, TAR.ZST, RAR5 and
7z targets. Default application composition still registers no new writer. The controlling requirements remain
the [implementation plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md).

An existing destination receives a complete assessment through a retained native read lease. This lease exposes
no reservation, stage, publication or deletion operation. The assessment checks every admitted member and payload;
the parent rechecks the native source and verifies that a no-op update can preserve the catalog. The final Add
confirmation submits an immutable plan bound to both the native observation and complete reviewed manifest.
Execution assesses the source again and rejects a different manifest before capturing inputs or creating artifacts.

Existing RAR5 and 7z targets use a single masked field for their existing password. Protection mode is observed and
preserved rather than taken from creation options. A specific worker refusal can request one fresh assessment with
a different observed mode over the same pinned target. That refusal supplies no catalog or publication authority.
The second complete assessment must succeed. Plain targets discard an unnecessary borrowed password before
submission. Wrong-password errors retain the draft for correction; leaving the dialog or transferring the password
to its job clears the form.

Native review runs on the destination's device queue with an owner that survives cancellation until the call returns.
Cancel and application shutdown request worker cancellation and wait for reaping and native-handle release before
releasing the borrowed password. Other device queues can continue. The command shows review progress and a Cancel
button while preparation is active, then asks for Add before queuing the update.

The first real Windows worker run exposed a gap in the protocol fixtures: hidden-name RAR5 and 7z refuse a
mismatched protection profile before returning a catalog. The corrected fixtures now exercise that refusal and
the bounded second assessment. The original failed native run and earlier successful portable runs are retained
separately; their transport and file manifests are verified locally. Ubuntu's first preparation also exposed an
overlong private keyring socket path; shorter batch names avoid that environment failure.

Complete regression runs then found two older assessment tests expecting the former generic refusal instead of
the new specific unprotected-mode reason. Both still require refusal and worker reaping. Two error-dialog fixtures
also observed replacement buttons before their scroll content finished layout. They now wait for the visible
error, and the creation fixture completes layout before reading text. These cases join the early gate. The original
failures, the Mac cohort canceled before long regressions, and Ubuntu's isolated fixture continuation are retained.

The corrected validation batch runs focused protocol, ownership, UI and native tests on each platform before a
shared gate releases the complete regression suites. A failure on one platform stops the other platforms at that
gate. Builds and completed phases are retained; source changes require a new checked packet. This reduces wasted
long runs while retaining complete Core, affected application, native, independent-reader and source/binary checks.
Only VMs and Mac execute application tests and archive readers. The host reads sealed evidence and compiler inputs.

The frozen source candidate is `3fcf51b09e2c3be90a3e9c11de0a2af09960d34f`. The corrected v6 cohort passes 28 Core
preflight cases, 16 update UI cases, 20 creation UI cases, eight mutation preparation cases and 21 password cases
on every platform. Windows also passes ten actual confined-worker UI cases, each creating and then updating an
archive. The portable update cases use synthetic dispatch fixtures and do not claim publication. Unix's ten actual
native preflight cases cover creation and pinned target review, not native update publication.

All previous Core cases and passes are retained in the complete current inventory:

| Platform | Passed | Skipped | Cases |
|---|---:|---:|---:|
| Windows VM | 7,941 | 461 | 8,402 |
| Mac | 8,190 | 207 | 8,397 |
| Ubuntu VM | 8,212 | 185 | 8,397 |

The affected application selection passes all 114 cases on Windows and 113 with one platform skip on each Unix
platform. Source verification checks exact compiler snapshots and DLL/PDB bindings, then compares their normalized
C# text with the frozen Git commit. Exact compiler bytes retain their own sealed hashes; line ending normalization
is used only for Git comparison. This lets implementation advance without rebinding old evidence to a moving
worktree. No complete Core regression lane is reused in the final v6 cohort.

Shared member commands, browsing/edit password sessions, Unix native mutation/confinement, production resource,
fuzz, RID and package qualification, and the final independent review remain open.
