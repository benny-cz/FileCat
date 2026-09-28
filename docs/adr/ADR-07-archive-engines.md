# ADR-07: Archive engines and update semantics

**Status:** Decided for ZIP (P5, 2026-09-28). Other formats (TAR, 7z, RAR, ISO) are P8 decisions, one format at a time, under the signing and license gate.

## Decision

**ZIP only, with the in-box engine.** ZIP uses `System.IO.Compression`, in-process, under enforced limits:

- entry count;
- declared size (hard cap on produced bytes);
- expansion ratio.

These are the same limits as v1 extraction. Managed parsing under these limits stays in-process (plan §6.2), so P5 adds no worker process. No native engine is adopted, so none needs isolation yet.

**Updates are staged rebuilds.** Every change is one job: add files or folders, delete, rename, replace (an edit-session commit), or add a folder entry. The job:

1. Opens the original deny-write.
2. Writes a complete new archive beside it (`.filecat-zip-*.tmp`). Untouched members keep their order, names, duplicates, times, attributes, and comments, and the archive comment is kept. Every copied member's CRC is compared with its header, so a damaged member stops the update instead of spreading.
3. Re-reads the new file: the planned members in order, and every written member's CRC.
4. Checks that the original is still the version the plan saw (length and write time).
5. Replaces it with `ReplaceFile`, which keeps the original's ACL, attributes, and creation time. A lost Mark-of-the-Web is re-applied.

**Parent-version tracking.** Plans carry the archive's baseline, and a changed archive refuses the update.

**Edit sessions.** An edit session records the member's CRC and length when extracted. A commit against a changed archive is offered as an explicit rebase only when the member itself is unchanged. Otherwise overwriting is a deliberate, danger-styled choice, and Save copy is the default.

**Nested archives.** They open read-only from a private `DeleteOnClose` spool (at most 4 cached, depth 8). Nested writes are refused.

## Why

- **The original stays intact.** A rebuild never changes it until the new file is verified. A crash leaves at most a staged file that the interrupted-operation review deletes, because it was never published.
- **No in-place ZIP mutation.** In-place editing (appending members, rewriting the central directory) can corrupt the only copy on a crash, and needs its own crash evidence (plan §15).
- **Encryption is refused.** .NET cannot re-create encrypted members, so such archives are read-only rather than silently re-encoded.

## Limits

- **Cost.** Every change decompresses and recompresses the whole archive; .NET exposes no raw member copy. The dialogs state the rewrite cost for large archives.
- **Undo.** None is recorded for archive updates.
- **Hard links.** Another hard link to the archive keeps the old content, because ReplaceFile gives the path a new file.
- **Compression method.** A copied member is stored uncompressed if it was stored before; otherwise the chosen compression level applies.
- **Stability of added files.** Files added while another program writes them are read as they are at that moment.

## Evidence

Automated tests (Core, all platforms):

- **Create and update:** pack with empty folders, never overwriting on create; add, replace or skip, delete, rename of folders, and folder entries, with order, duplicates, comments, and the archive comment preserved.
- **Guards:** a stale baseline, rename collisions, and path traversal are refused and the original stays byte-identical. Damaged and encrypted members stop an update; the test command names damaged members.
- **Scale:** 20,000 members plan and rebuild in about a second.
- **Nested archives:** navigation, display paths, parents, typed paths, device keys, and the outer download mark on extraction.
- **Edit sessions:** the private copy carries the mark; commit and re-base; archive-changed, member-changed, and archive-missing are classified; sessions restore and discard.

TV-07's native-engine and fuzzing parts wait for P8 formats.
