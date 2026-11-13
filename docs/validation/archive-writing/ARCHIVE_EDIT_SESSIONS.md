# Native archive edit sessions

Candidate `f758c487647fddbc87038d157c65c12f546339c0` connects F4 and explicit Commit to the
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
correction. The combined `awe-v3` cohort passes on all three platforms:

| Platform | Focused Core passed / skipped | Connected application passed | Complete Core passed / skipped |
|---|---:|---:|---:|
| Windows | 138 / 21 | 259 | 8,065 / 465 |
| Mac | 156 / 3 | 259 | 8,318 / 207 |
| Ubuntu | 154 / 5 | 259 | 8,340 / 185 |

Windows additionally passes all ten actual native F4/Commit format/password combinations, including exact
origin bytes and late editor saves. Each primary bundle binds 24 changed C# inputs and 12 DLL/PDB pairs;
all inherited Core cases and passes remain present. No component or release qualification is claimed.

The `awe-v3-native-v2` addendum reuses the sealed current product bytes and passes 217 native checks,
18 creation-cleanup crash cases and 14 application-entry checks. The older member fixture passes eight
cases but times out on unprotected RAR/7z because its navigation wait can accept the previous listing.
Fixture-only candidate `b959b0cb9dff73dbde648807c4ab3e72e5e12084` waits for the actual archive and
closed navigation and passes all ten member/F5/F6 cases in `awe-v3-member-v1`. Static verification proves
that every captured binary except the changed test assembly is identical to the inherited native cohort.
The native and member closures bind 15 DLL/PDB pairs and respectively 24 and 25 changed C# inputs.
An earlier native-addendum startup could not read its bootstrap script; it ran no tests and is retained
separately with the explicit standard-account read-grant correction.

The [checkpoint](edit-sessions-checkpoint.json) preserves nine exact bundles, both fixture failures,
the controlled first native abort, the unexecuted v2 sharing refusal and the bootstrap permission failure.
Every final census has zero owned FileCat/test instances; Mac wake helper 36765 remains active.

Remaining gates include native session-directory/cleanup race hardening, adversarial native
session recovery and publication uncertainty, missing/unsupported archive UX, cancellation and
focus cases, Unix native mutation/confinement, and production/package/fuzz/RID acceptance.
The automated Avalonia flow does not establish native desktop/editor/accessibility acceptance.
The next ownership packet must replace path-only record/snapshot cleanup with retained native authority;
plan §12.1 explicitly keeps that gate open, including legitimate editor inode replacement and restart cases.
Include unsupported profile-constructor, malformed/oversized record and inherited ACL cases in that packet;
permissions and initial identity fields alone do not establish private native creation and cleanup.

The [retained storage follow-up](ARCHIVE_EDIT_STORAGE.md) adds native private creation, exclusive
immutable record generations and native working-file reads. Its three-platform focused matrix passes and retains
all earlier failures. Immutable native snapshot/discard ownership,
application composition and the complete qualification gates remain open.

The [snapshot follow-up](ARCHIVE_EDIT_SNAPSHOTS.md) now captures through native ownership and grants
queued archive jobs readonly views of the retained object. Its three-platform focused gates pass;
fresh cleanup and remaining integrations were open when that was written.

13 November 2026: native edit storage is listed (`fda3f03c`, with the eighteenth review's fixes in `a4606689`). On
win-x64 (NTFS), linux-x64 (ext4) and osx-arm64 (APFS) ordinary startup composes this store, so a new edit is kept in
FileCat's own identity-bound folder; everywhere else the legacy store stays, including where FileCat's own data folder
is on another file system, is reached through a link, or cannot be held privately. An edit made before the listing stays
in the legacy folder, commits through its own route, and is still discarded through the legacy store's bound removal
(`a4606689`). The routes, their evidence and the three-platform runs behind them are in the
[route decision](RELEASE_ROUTE_DECISION.md) and the [status](IMPLEMENTATION_STATUS.md).
