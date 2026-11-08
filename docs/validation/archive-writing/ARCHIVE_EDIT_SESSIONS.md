# Native archive edit sessions

Candidate `14cb2678441ba9da2eca90b603b4ae08dbf09589` connects F4 and explicit Commit to the
shared native archive job path for TAR, TAR.GZ, TAR.BZ2, TAR.ZST, RAR5 and 7z. This is an
unregistered component candidate, not format or release qualification.

Version-2 records include the explicit profile, complete source revision/native observation,
exact unique regular member, content and semantic metadata hashes, and initial working-file
identity. Version-1 ZIP/remote sessions retain their meaning without a listing-time rewrite.
Future versions remain visible with an accessible recovery folder and cannot be rewritten or
discarded by the current build. Passwords remain runtime-only.

Creation holds a retained native source through extraction, exact content hashing, origin
propagation and source recheck. The editor's namespace is below `files/`, so editing a member
named `session.json` cannot overwrite its own record. New Unix session/snapshot directories
and files are private. Native origin propagation preserves opaque bytes, including empty and
binary xattrs, and refuses a different existing mark.

Commit uses an immutable copy. Changed members default to Save copy; an unchanged member in
a changed archive requires explicit rebase against a fresh complete assessment. The native
publisher records positive publication facts separately from the job's final state. A session
advances only after a fresh native review, complete actual-member readback and atomic record
save. Read or close failures preserve the old record. Later editor saves remain modified.

`run_edit_sessions_batch.py` freezes one source delta per platform, checks focused Core and
application gates first, exercises the real Windows F4/Commit flow with all ten format/password
combinations, and then runs the complete inherited Core inventory. Windows rebuilds current
FileCat binaries over verified immutable runtime assets. Unix batches reuse the existing
case-sensitive Mac fixture and private Ubuntu keyring. Compiler source/PDB evidence and final
instance census are required; failed cohorts are retained.

The retained `awe-v1` cohort passes 155 focused Core cases with platform skips and all 259
connected application cases on each platform. Complete Core inventories pass on Mac
(8,314 passed / 207 skipped) and Ubuntu (8,336 / 185), with all prior cases/passes preserved.
The static verifier binds 24 changed C# inputs and 12 DLL/PDB pairs on every platform.
All three final censuses report zero owned instances; Mac wake helper 36765 remains active.

Windows native v1 recorded one pass and a fixture timeout. The fixture could focus `a.txt` in
the old filesystem listing before password navigation finished. Its remaining run was stopped
by the agent, producing the recorded test-host abort; that was not an observed codec crash.
Fixture-only candidate `9cdcab72` adds target-location/dialog-closure checks and immediate
diagnostics. Its `awe-v2` startup was postponed before source application while another tester
ran AnalysisRenderingTests; v2 never ran.

Candidate `f758c487647fddbc87038d157c65c12f546339c0` adds four readback refusal cases and
guards for partial content/caveats, changed revisions and a source change exposed by content
close, plus live session demand checks around native source review. It includes the fixture
correction. Windows became idle, and the combined `awe-v3` cohort is running on all three
platforms. Its focused Core gate has passed on Mac (156 / 3) and Ubuntu (154 / 5).
The [checkpoint](edit-sessions-checkpoint.json) retains the exact historical bundles and will
record the final cohort separately; no component or release qualification is claimed.

Remaining gates include native session-directory/cleanup race hardening, adversarial native
session recovery and publication uncertainty, missing/unsupported archive UX, cancellation and
focus cases, Unix native mutation/confinement, and production/package/fuzz/RID acceptance.
The automated Avalonia flow does not establish native desktop/editor/accessibility acceptance.
The next ownership packet must replace path-only record/snapshot cleanup with retained native authority;
plan §12.1 explicitly keeps that gate open, including legitimate editor inode replacement and restart cases.
