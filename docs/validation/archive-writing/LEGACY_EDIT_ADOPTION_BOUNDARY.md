# Legacy edit adoption boundary

The combined final-target candidate passed on Mac with 15/15 focused Core, seven focused UI
passes (three platform skips), and complete Core/Remote/App inventories of 8,681/2,638/2,770
passes, respectively, with zero failures. Its source/compiler/binary/raw-result transport is
24,611,390 bytes, SHA-256
`e39b9cabc68a2e04bd159bd316f1511b52448bf05858dc0c3cfe60b87ee115ac`, and passed
independent verification. No prior raw case or pass was lost after the explicit UI
parameterization mapping. Windows' matching candidate passed 15/15 focused Core, 10/10
focused UI, and complete Core/Remote/App inventories of 8,139/2,405/2,845 passes with
754/264/118 skips and zero failures in the accepted runs. Its 26,003,925-byte transport,
SHA-256 `034949b428f676b1a07429670774bb4214e5f64c3b1ebb046436db942fee0aef`,
passed the same independent verification; raw inventories retained every prior pass. The
first Windows full App attempt had one folder-count quick-view timeout, followed by a 7/7
isolated pass and a complete zero-failure rerun. That initial failed raw result remains
available for timing diagnosis. Ubuntu replay waits for the active RAR5 password campaign.
Native routing and old-folder retirement are still disabled.

9 November 2026. This remains a test-only compatibility boundary. A pinned old-source copy
transaction and durable pending/ready relationship are under qualification; ordinary startup
does not migrate or retire legacy sessions.

The dual-root test-only store now permits a separate legacy root and native v3 root. It creates
new native sessions only in the native root, discovers both roots, keeps each version's save
route, and marks a record found in the wrong root unsupported. Mac's native test passes these
composition and misplaced-record cases. Legacy discard still refuses when native storage is
present; no old payload is retired. The Mac v47 connected focused batch passed all 27 gates,
Windows v47 passed all 28, and Ubuntu v47 passed all 28 with independently verified
source/compiler/binary/raw-test transports. Those guest tests exited their processes.

The version-1/2 creator uses `CreatePrivateDirectory` for the session folder, ordinary
`Directory.CreateDirectory` for its `files` child, ordinary `FileStream` creation for the working
file, and `Save` for `session.json`. The native version-3 lease requires explicit private security
on its root, every child directory and record file. On Unix, the older `files` child can inherit
mode 0755 or 0775 while its parent is 0700. On Windows, older objects can inherit a profile DACL rather
than the exact native DACL. The root itself may also have been created implicitly with inherited
security. Calling `OpenNativeRoot` and then `OpenDirectory`/`OpenFile` on these objects therefore
does not form a general adoption bridge. A code draft based on that assumption was removed before
guest execution.

The adoption implementation needs a separate **read-only** legacy source admission. It must pin
the exact root, session, `files`, record and working file without following links; inspect their
owners, effective permissions, identities, file types, hard-link counts, lengths and origin marks;
and refuse ambiguous or exposed paths. It must recheck the bounded record bytes, working version,
content hash and origin through completion. It must create version-3 records in a separately
admitted private destination root when the old root cannot satisfy native security, with a
descriptor-bound way to copy the origin mark. Both roots must remain discoverable on restart.
Any failed or interrupted copy keeps the old folder and the new partial folder for review.

A Unix admission lease now pins the v1/v2 root, 0700 session directory, inherited-mode
`files` directory, `session.json` and working file without following links. It checks creator
ownership, private containing namespaces, exact directory and file identities, single-link
regular files, bounded record/working lengths, complete record and working hashes, outer metadata
and origin marks. Its `CopyTo` rechecks the source before and after copying and never deletes or
publishes an object. Mac's direct guest gate passed 10/10 cases, including a real ZIP member
record downgraded to the historical version-1 shape, origin marks, hard links, symlinks,
replacements, unknown xattrs and exposed directories; the host Core test build has zero warnings.
An old root and `files` child may inherit group-write bits under a creator-owned 0700 parent;
the source admission now requires that private parent and checks its security during review.
The working and record files may likewise inherit mode bits inside the 0700 session. This
admits historical Ubuntu umask layouts while refusing a group-writable public parent. The
updated inherited-layout and exposed-parent cases passed on Mac; Ubuntu replay is pending. This remains an
unregistered source component. A separate Windows read-only source lease now pins the
old NTFS namespace and checks owner/DACL through a read-control leaf handle. Its standard-user
VM test passed 10/10 focused and 168 adjacent cases with zero failures; 139 guarded cases skipped.
See [Windows source checkpoint](WINDOWS_LEGACY_SOURCE.md). Old-folder retirement and connected
three-platform evidence remain open.

The test-only destination bridge now copies a fully reviewed old edit into a distinct native
root, verifies complete copied bytes and origin, publishes a pending anchor, rereads that native
copy, rechecks the old source and appends a linked ready revision. Restart discovery keeps a
pending copy unsupported and the old record available. Its relation records source object
identities and content hashes without paths. Mac, Windows and Ubuntu focused runs of the
initial four adoption cases passed; the revised Mac candidate passed 38/38 adoption, source
and adjacent storage cases. Its exact committed source, PDB snapshots and raw full Core result
(8,672 passes, 207 skips, zero failures) independently verified in a 14,104,856-byte transport,
SHA-256 `ce54964bf407ec417418fc502fe712e071fc7cef0e4079d02f703e99670dc745`.
Windows' earlier full Core passed 8,139 tests with zero failures; the independent baseline
review retained all 8,065 prior passes and accounted for 336 added cases after narrowly
normalizing one case-insensitive Windows fixture path. The exact Windows candidate then passed
6 adoption, 10 old-source, 15 native-storage and 40 native-discard cases, followed by full Core
(8,139 passes, 745 skips, zero failures). Its source, both test assemblies, PDB snapshots and
raw results independently verified in a 16,440,889-byte transport, SHA-256
`d7021321d762700d5b847b3c2b3e8527f6bd7ec581cc39bdcd2636f71e2ccbb3`.
The earlier Windows full Remote and App runs passed 2,405 and 2,843 tests respectively with
zero failures. Ubuntu exact-candidate focused tests and the remaining full inventories await
replay. The application
factory remains on the legacy store until active/retained routing, old-folder retirement,
process-kill recovery and full platform evidence are complete.

A read-only catalog review now pairs a ready native copy with exactly one old record, reopens
the old source lease, checks its recorded identities, bytes and origin, and reviews the native
working copy. It reports missing, changed or ambiguous pairs for recovery review. The old
record remains the active commit route; this snapshot grants no deletion or routing authority.
Both test assemblies compile with zero warnings on the host. Mac's connected catalog-focused
batch passed 38/38 and full Core passed 8,672 with 207 skips. Windows passed 71 focused
cases and 8,139 full Core passes with zero failures. Comparing raw full Core case names to
the preceding runs found no missing prior case or lost prior pass. Both machines then passed
11/11 focused adoption cases covering a changed original and duplicate native copies.
Ubuntu catalog replay awaits the password fuzz campaign's terminal state.

The next composition candidate orders lookup toward the old session even if its timestamp
is later than the copy, and presents the adopted native copy as a review-only item in the
edit-session UI. The startup hint counts it separately; opening it rereviews the old source
and native copy before describing their relationship. Commit, editor reopen and discard
are unavailable on that retained item. The combined Core/App/Windows test projects build
with zero warnings on the host; direct guest UI and lookup tests remain pending.

After a complete copy, the old record remains a retained recovery copy. Session lookup and startup
UI need an explicit active/retained relationship before any automatic routing changes. Retirement
requires a distinct exact-object cleanup review and confirmation; matching a GUID/path alone does
not authorize deletion. Version-1 ZIP and remote sessions must still commit through their old
path until the new record is durably reviewed. Unknown future records are never adoption inputs.

Qualification needs real old-version fixtures from each guest, inherited ACL/mode cases,
hard links, symlinks/reparse points, atomically replaced editor files, concurrent record changes,
origin-mark preservation, destination refusal, process-kill at each durable step, restart discovery,
and zero loss of the original session on any refusal. No product factory may enable adoption
before this matrix and the full connected tests pass.

An actual child-process kill at the native creation marker, pending adoption anchor and
ready revision boundaries passed on Mac and Windows. Restart discovery kept the old record
and working bytes, classified the first two native copies as unsupported, and linked the
ready copy to the old active route. Mac passed 41/41 focused and 8,675 full Core tests with
207 skips; Windows passed 74 focused and 8,139 full Core tests with 748 skips. Both full
inventories retained every prior case and pass and added exactly three expected rows.
Ubuntu and additional failure points still await replay; old-folder retirement remains open.

The subsequent Mac integrity candidate passed 13 focused Core cases and six focused UI cases,
then the complete Core (8,679 passed, 207 skipped), Remote (2,638 passed, 31 skipped) and App
(2,769 passed, 193 skipped) suites with zero failures. Its 24,593,503-byte source, PDB, binary
and raw-result transport, SHA-256
`5e81fff13ef9c9d0b4e18a196fc52a273f8ce05e58c19e6d350227b58be58d37`,
passed independent verification. A later test-only tamper candidate passed 14/14 focused Core
and seven focused UI cases with three platform skips. The raw UI inventory's apparent missing
case was an explicit replacement by two parameterized cases; both pass and no prior pass was lost.

Windows' equivalent source-bound integrity candidate passed 14/14 focused Core, 10/10 focused
UI, full Core (8,139 passed, 753 skipped), Remote (2,405 passed, 264 skipped) and App
(2,845 passed, 118 skipped), all without failures. Its 25,989,953-byte transport, SHA-256
`27e7c61241e18f3670daa9ec002a5e0f87b1718e25cd58bc0a14aceb78363a51`,
passed independent source/compiler/DLL/PDB/raw-test verification. The raw Core and App
inventories retained all prior cases and passes after explicitly accounting for the same
parameterized UI case. The test processes exited. The later full-target catalog comparison
and original-folder review UI are undergoing a combined three-platform batch; these earlier
results do not qualify those newer bytes. Old-folder retirement, production composition,
Ubuntu replay and the wider release gates remain open.

11 November, later. **Exact old-folder retirement** is implemented (`EditSessions.LegacyRetirement.cs`,
`UnixLegacyEditRemoval`, `WindowsLegacyEditRemoval`; [decision](UNIX_PRIVATE_CLEANUP_DECISION.md)). The adopted item's
review offers "Retire original folder…" while the link is intact. Nothing changes until the adoption review passes again
and a probe shows that every recorded old object can be removed (Windows opens each sharing only reads, so an editor
that has the old working copy open for writing refuses it; Linux takes a write lease; macOS cannot tell, and the
confirmation asks to close the old editor first). A durable retiring revision then makes the adopted copy the edit: the
old record no longer speaks for it (lookup and the session list skip it), and the item offers "Finish retiring
original…". The old working file, its folder, the old record and the session folder are removed one by one, each bound
to the identity recorded at adoption, a file only with its recorded bytes; a later revision drops the relationship and
the copy is an ordinary native edit (commit, reopen, discard). A stop resumes from the retiring revision; a changed old
working copy or an extra file is kept and named, so no edit is lost; while the old record and working copy are there,
"Cancel retirement…" makes the old edit the edit again and removes nothing. Every file goes through the shared exact
removal (Linux lease before the content review, the name set aside and removed only while it binds the held object,
SIGIO ignored; [decision](UNIX_PRIVATE_CLEANUP_DECISION.md)). Thirteen shared cases (complete, edited old copy, extra
file, open editor, stop after the intent, stop after the working file, a change or an extra file after the intent, a
record that is not adopted, cancel; on Unix an unreadable old folder and an atomic save at the moment of removal; on
Linux another program opening the file during the lease), with the UI path in `EditAdoptionRetainedUiTests`. The
application factory still composes only the legacy store; registering the native store remains open.

12 November. **Production composition behind a qualification gate** (`8c07260a`). `NativeEditStorage` lists the runtimes
and filesystems on which ordinary startup composes this dual-root native store, each with its evidence, as
`ArchiveWriteRoutes` does for the archive backends. For a listed runtime whose native root's filesystem qualifies,
`AppServices` composes the store with the platform's private directory leases (Windows `WindowsEditDirectoryLease`,
Unix `UnixEditDirectoryLease`) and save-copy destinations, the native root `edit-sessions-native` beside the legacy
folder (now in the write-folder inventory): new edits are created there, older ones stay in the legacy folder with their
commit route, and adoption and retirement keep their reviews. Everywhere else, and in this build everywhere, startup keeps
the legacy store exactly as before and probes nothing. `NativeEditStorageTests` pin the empty list and the gate's rules;
`EditStoreCompositionTests` drive the real application wiring, where a listed runtime creates a new edit in native storage
beside an older legacy one and both load. macOS (App 2,812, Core 580), Ubuntu (App 2,824, Core 582) and the Windows VM
(App 2,916, Core 253, Windows edit platform 332) passed with no failure (`edit-v24a-mac` `32e6e135…`, `edit-v24a-ubuntu`
`2e70ecd7…`, `edit-v24a-win` `3a146b0d…`). Registering a runtime belongs to the per-route qualification decisions.

13 November 2026: native edit storage is listed (`fda3f03c`, with the eighteenth review's fixes in `a4606689`). On
win-x64 (NTFS), linux-x64 (ext4) and osx-arm64 (APFS) ordinary startup composes this store, so a new edit is kept in
FileCat's own identity-bound folder; everywhere else the legacy store stays, including where FileCat's own data folder
is on another file system, is reached through a link, or cannot be held privately. An edit made before the listing stays
in the legacy folder, commits through its own route, and is still discarded through the legacy store's bound removal
(`a4606689`). The routes, their evidence and the three-platform runs behind them are in the
[route decision](RELEASE_ROUTE_DECISION.md) and the [status](IMPLEMENTATION_STATUS.md). `NativeEditStorageTests` now pin those three routes and their
evidence IDs instead of an empty list, and `EditStoreCompositionTests` require native composition in a qualification
run. Adoption itself is unchanged and still unreachable from ordinary startup: an older edit is never migrated, only
loaded, committed or discarded where it is.
