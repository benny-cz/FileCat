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
updated inherited-layout and exposed-parent cases still await guest replay. This remains an
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
initial four adoption cases passed; Mac full Core passed 8,668 cases and Windows full Core
passed 8,139, both with zero failures. The revised tamper/discard and inherited-mode cases
and the remaining full inventories are still running or awaiting replay. The application
factory remains on the legacy store until active/retained routing, old-folder retirement,
process-kill recovery and full platform evidence are complete.

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
