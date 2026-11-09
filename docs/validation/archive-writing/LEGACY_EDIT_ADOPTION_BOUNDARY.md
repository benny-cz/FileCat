# Legacy edit adoption boundary

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
batch passed 38/38; its full Core regression and Windows catalog batch are underway. Ubuntu
catalog replay awaits the password fuzz campaign's terminal state.

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
