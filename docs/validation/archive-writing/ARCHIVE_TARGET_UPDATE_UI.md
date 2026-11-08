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

The corrected validation batch runs focused protocol, ownership, UI and native tests on each platform before a
shared gate releases the complete regression suites. A failure on one platform stops the other platforms at that
gate. Builds and completed phases are retained; source changes require a new checked packet. This reduces wasted
long runs while retaining complete Core, affected application, native, independent-reader and source/binary checks.
Only VMs and Mac execute application tests and archive readers. The host reads sealed evidence and compiler inputs.

Shared member commands, browsing/edit password sessions, Unix native mutation/confinement, production resource,
fuzz, RID and package qualification, and the final independent review remain open.
