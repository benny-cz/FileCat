# Decision: exact cleanup of Unix transaction objects

10 November 2026. This record revises the earlier position in
[UNIX_EDIT_EXACT_REMOVAL_BOUNDARY.md](UNIX_EDIT_EXACT_REMOVAL_BOUNDARY.md) for **archive-update transaction objects in a
creator-private stage directory**. It does not change the legacy edit-session cleanup (risk R21), which keeps its own
record.

## Problem

Linux `unlinkat(2)` and macOS `unlinkat(2)` remove a name, not a held object. No supported call makes removal conditional
on the reviewed inode, and `AT_EMPTY_PATH` is not accepted for unlink. Retaining every Unix artifact forever is safe, but
it would leave a full copy of every updated archive (the retired original) and its manifests in a hidden folder beside
the archive after each update. That makes the Unix update route unusable in practice and contradicts §7.1 (Cleaning) and
§11.7, where retained backups are the exception reported to the user, not the normal result.

## Threat model from the plan

§7.4: "A hostile process with the same OS identity may defeat pathname assumptions; minimize its authority and do not
claim to isolate equivalent-privilege malicious processes." INV-19: "Cleanup cannot convert failure into data loss;
unknown, changed, over-budget or unowned artifacts are retained and reported."

## Decision

Cleanup of an archive-update transaction object is allowed only when all of these hold:

1. The object was recorded in the journal by this transaction (stage, sidecar, or the original after a reconciled
   exchange), and its record names its exact identity and version.
2. It lives in this transaction's private stage directory, created by `mkdirat` below the reviewed archive folder with
   mode `0700`, owned by the current user, with no group/other namespace write and no Darwin ACL entries.
3. FileCat holds the object through a descriptor and observes identity, owner, single link, size and either its recorded
   metadata digest (which includes modification and change times) or its exact SHA-256 content.

The removal then runs in this order (`UnixArchiveFile.DestroyOwned`):

1. Destroy the **content** through the held descriptor (`ftruncate(fd, 0)`, `fsync`). This is an exact-object operation.
   No copy of archive data can survive a later name race.
2. Re-observe that the name binds the held object, call `unlinkat(dirfd, name, 0)`, then require the held object's link
   count to be zero.
3. Remove the private directory with `unlinkat(…, AT_REMOVEDIR)`, which refuses a non-empty directory.

Each step is preceded by a durable `CleanupIntent` record and followed by `Cleaned`; the directory removal ends in
`Completed`. Every refusal or mismatch records `NeedsReview` and keeps the remaining objects and the folder.

## What this does and does not claim

- No archive content that FileCat still needs is removed: only reconciled objects reach cleanup, and their content is
  destroyed through the held descriptor, never through a name.
- An equivalent-privilege process that writes into FileCat's random-named private folder between step 2's check and
  `unlinkat` can have **its own injected object** removed. The held object then keeps its link, which FileCat detects and
  reports for review. That process already holds the same authority over the user's files, so FileCat grants it nothing
  new (§7.4). Non-hostile processes do not write into this private folder.
- A retained or uncertain object is never cleaned by pathname, age or prefix. Recovery must re-observe identities first.
- This record covers private stage directories only. Since commit `8872044c` new archives are prepared there too, so
  their sidecars are destroyed by the same rules; nothing is ever removed from the public archive folder.
