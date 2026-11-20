# Unix edit cleanup boundary

9 November 2026. Native Unix edit creation, capture, publication and read-only recovery have
component evidence. Native Discard and capture cleanup still refuse removal. This is intentional
until a deletion operation can prove that the object removed is the exact reviewed object.

The current lease pins parent directories and files and rechecks their identities. That prevents
ancestor path redirection and detects many replacements, but it does not make a later name-based
unlink conditional on the reviewed file identity. Linux
[`unlinkat(2)`](https://man7.org/linux/man-pages/man2/unlinkat.2.html) removes the entry named
relative to a held directory descriptor; its supported flags do not include an exact-file identity
condition or `AT_EMPTY_PATH`. The
[macOS `unlinkat(2)` manual](https://man.freebsd.org/cgi/man.cgi?manpath=macOS+26.3&query=unlinkat&sektion=2)
likewise specifies removal by directory descriptor plus path. An uncooperative process with the
same user identity can replace the final name after FileCat's last review and before that call.
The private 0700 directory and an advisory lock do not exclude another process with the same user
identity. A rename into quarantine is also name-based at its first step; it can retain a mismatch
for review, but cannot by itself prove that the original reviewed object was the one moved.

Therefore `UnixEditDirectoryLease.DeleteSnapshot` and `RemoveEmpty` must continue to retain their
artifacts, and the UI must report incomplete cleanup honestly. A future Unix deletion route needs a
platform/filesystem-specific conditional operation or an independently enforced namespace owner
that excludes same-user replacements during the entire review and removal. Any route must be
qualified on Linux and macOS with attacker replacement, hard-link, symlink, interrupted cleanup,
same-byte new inode, and process-kill cases before it is registered. No pathname-only fallback may
be added merely to make Discard appear complete.

## Update, 13 November 2026

The [private cleanup decision](UNIX_PRIVATE_CLEANUP_DECISION.md) now applies to native edits too, through the leases
opened for a reviewed discard or created removable (`UnixEditDirectoryLease.Discard.cs`, `UnixExactRemoval`): a file's
name is set aside by an exclusive rename, removed only while it binds the held, reviewed object, and that object must then
have no link left (on Linux under a write lease, which proves that no other program has it open); a folder goes only once
empty while its name binds it. Content is not truncated first, because an editor may still map the working copy. Anything
that does not match is kept and reported. The reviewed discard uses these rules since `aa9d8a13`; a stopped, cancelled or
failed edit creation (F4) now uses them to remove what it made, and "Remove unfinished copy…" to remove a copy a crash
interrupted. The pathname-only fallback this record rules out was never added.

Save copy is made alike for legacy and native edits: the working copy is copied (a native one through its held private
namespace) into a private folder in FileCat's temporary area, outside the edit stores, and that folder is removed under
the private cleanup decision (`EditPrivateCleanup`). On Windows it goes through verified handles. Unix has no
handle-bound removal of a folder tree, so the folder is removed by name, recursively, and only while it is a folder only
this user can change (owned, not a link, without other write, and with group write only where the group is the user's own
private group), so that no other user can have put or replaced anything in it. The stage beside the destination is a single file, removed only while its name binds the identity
recorded when it was made; on Unix, in a folder other users can change, it is kept and named instead.

A folder whose edit record is gone is only offered for removal when what is left cannot hold the user's work: no working
copy at all, a copy shorter than the length its creation marker records, or a copy beside the stage of that creation's own
record publication. A copy that is already as long as the file it was made from (or longer, or whose marker is an earlier
build's and records no length) is kept whole and said so, because FileCat cannot tell it from an edit of the user's. An
empty copy is the exception: it holds nothing of anyone's, whatever its marker records, so it is removed with the rest. The
residual case the rule accepts: an edit whose record is lost by something outside FileCat *and* whose working copy the user
shortened in their editor reads as an unfinished copy. FileCat's own routes never produce a record-less folder with an
edited copy (a discard removes the working folder before the record), so this needs an outside cause, and the removal still
asks first and names the folder.

A removal reviews the user's own bytes by the rules that every other review of them uses: an ordinary file this user
owns, with no second link, inside the 0700 folder whose privacy is reviewed on every operation. Its permission bits are
the editor's to set - a replacement under an ordinary umask leaves group and other read bits, a script being edited may
be made executable, and on macOS it may carry an access list of its own - and none of that confers access through that
folder, which no other user can traverse. Set-user-ID, set-group-ID and sticky bits are still refused, and FileCat's own
records must still be exactly as FileCat wrote them, with no list at all. Until
`dcf19885` the removal demanded 0600 of the user's working copy too, so an edit an editor had replaced could not be
discarded at all.
