# Unix existing-archive update through a journaled two-directory exchange

10 November 2026. This checkpoint implements the Unix replacement route that
[UNIX_ATOMIC_EXCHANGE_FEASIBILITY.md](UNIX_ATOMIC_EXCHANGE_FEASIBILITY.md) proposed. It is **unregistered**: ordinary
application startup still composes no new writer, and nothing here qualifies a profile, RID or package.

## Route

1. **Admission before any stage** (`UnixArchiveFile.RequireUpdateAdmissible`, `ArchiveUnixUpdateMetadata`): the original
   must be owned by the current user, single-link, without setuid/setgid/sticky bits, without flags other than the
   preservable `nodump`/`noatime` (Linux) or `nodump`/`hidden` (macOS) and filesystem-intrinsic layout bits, without Linux
   attributes such as encryption, verity or a mount root, with only the admitted origin/ACL extended attributes, and on
   macOS without any ACL deny entry (a deny entry can block the swap or the exact cleanup). Anything else is read-only
   with a specific reason.
2. **Private preparation**: the existing journaled container preparation reserves the stage and its four sealed sidecars
   in a fresh `0700` `.filecat-archive-stage-*` folder created by `mkdirat` below the reviewed archive folder. Reservations
   from that folder carry the reviewed public parent identity, so self-inclusion and target aliasing are still checked
   against the archive folder.
3. **Promised metadata** (`UnixArchiveFile.ApplyUpdateMetadata`): while the stage is reachable only through the private
   folder, it receives the original's group, permission bits, access ACL, preservable flags and, on macOS, birth time,
   through held-descriptor calls only. The exact applied state is compared field by field with the reviewed original and
   the separately reviewed required origin mark. Content-changing modification/change times and the Linux birth time are
   not promised.
4. **Durable exchange** (`UnixArchiveExchangeUpdate`, record `archive-exchange-update`): `Reserved` →
   `MetadataApplied` → `ExchangeIntent` → one `renameat2(RENAME_EXCHANGE)` / `renameatx_np(RENAME_SWAP)` on the two held
   directory descriptors → `Exchanged` after both names, identities, contents and promised metadata are re-observed. A
   failed native call is atomic: both names are proven unchanged and the outcome is `NotPublished`. Every other anomaly is
   `NeedsReview` with both versions retained.
5. **Exact cleanup** under [the cleanup decision](UNIX_PRIVATE_CLEANUP_DECISION.md): `CleanupIntent`/`Cleaned` per object
   (sidecars, then the retired original or unpublished stage), content destroyed through the held descriptor, name removed
   with pre/post identity checks, then the empty private folder removed and `Completed` recorded.
6. **Recovery** (`UnixArchiveExchangeRecovery`): an exclusive (`flock`) descriptor-held reopening of the unfinished
   journal, the archive folder and the private folder by recorded identity. It re-observes both names and records only
   what it sees: unchanged names → `NotPublished`; swapped names with exact stage/original content and promised metadata →
   `Exchanged`; anything else → `NeedsReview`. It then resumes exact cleanup (an absent name after a durable intent is an
   interrupted unlink) and closes the journal. It never swaps back or touches the public name.

## Evidence

Recorded in [the implementation status](IMPLEMENTATION_STATUS.md) with packet and bundle hashes: ten create-then-update
jobs, the metadata/refusal cases and 16 real externally killed process points pass on Ubuntu (ext4) and macOS (APFS).

## Remaining for this route

- Preparation-phase crashes (before `Reserved`) still leave the private folder and its recorded stage/sidecars for review;
  a fresh recovery action for preparation-only transactions is separate work.
- Unix creation still stages and retains its sidecars in the public folder and publishes creator-private `0600` files.
- Application recovery UI, the confined Linux worker roles in the application, source removal (F6) on Unix, hard-link
  warning instead of refusal, and all release gates (fuzz, packages, accessibility, final review) remain open.
