# ADR-07: Archive engines and update semantics

**Status:** Decided for ZIP (P5, 2026-09-28) and for the read-only formats approved for P8 (2026-09-28): TAR family, 7z, RAR, xz, bzip2, zstd, ISO 9660, and UDF. Writing any format other than ZIP stays out of scope.

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

**Read-only formats (P8).** Approved by the user on 2026-09-28. They live in `FileCat.Archives`, so Core keeps no
third-party packages. All engines are managed code, so they run in-process under the ZIP limits:

- entries: 1,000,000;
- produced bytes: the declared size plus 1 MiB, or 8 GiB without a declared size;
- expansion: at most 1000:1 after 64 MiB, per member and while listing compressed TARs.

| Format | Engine | Access |
|---|---|---|
| `.tar` | .NET `TarReader` | Members read in place |
| `.tar.gz`, `.tgz` | .NET `TarReader` over `GZipStream` | Forward cursor |
| `.tar.bz2`, `.tar.xz`, `.tar.zst` | .NET `TarReader` over SharpCompress decompressors | Forward cursor |
| Single `.gz`, `.bz2`, `.xz`, `.zst` | Same decompressors | One member named after the file |
| 7z | SharpCompress 0.50.4 (MIT) | Forward cursor |
| RAR 4/5, solid, multi-volume sets | SharpCompress | Direct, or cursor for solid archives |
| ISO 9660 with Joliet names, UDF | LTRData.DiscUtils 1.0.89 (MIT) | Random access |

A forward cursor restarts only when asked for an earlier member, so extracting in archive order decompresses once.
SharpCompress's 7z reader visits entries in its own order, so the cursor matches members by name and occurrence; a
member that ends short of its declared size is an error.

The rules that apply to ZIP also apply here:

- Names with absolute paths or `..` are listed as unavailable and never extracted.
- Links, hard links, and device or pipe entries are listed and never followed or extracted.
- Encrypted members are listed and not opened. An encrypted file list is explained, not guessed at.
- Archives nest in both directions: a TAR inside a ZIP, a ZIP inside a 7z. The inner archive opens from a private
  spool (at most 4, depth 8).
- Mark-of-the-Web propagates from the outermost marked file.
- Ctrl+PgDn opens files by their signature.

Known gap: TAR names in legacy 8-bit encodings show replacement characters, because .NET decodes them as UTF-8.

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

Read-only formats (Core tests on all platforms, `ArchiveFormatTests`):

- **Fixtures.** Sample archives from SharpCompress's MIT test suite (RAR 4 and 5 including solid and multi-volume,
  7z LZMA2 and solid, xz and zstd TAR) list the same tree and yield identical bytes. Solid archives give the same bytes
  in any order.
- **Encryption.** Encrypted 7z members are refused, and an encrypted RAR file list is explained.
- **Generated archives.** TAR, gzip TAR, and bzip2 TAR fixtures with links, a pipe, and escaping names are listed
  safely; extraction jobs skip links and never write outside the destination.
- **Limits and other formats.** A gzip bomb is stopped by the ratio limit. An ISO built with DiscUtils reads back.
  Nesting works in both directions, and a renamed 7z opens by signature.
- **Damage.** Corrupted copies of every fixture report damage and never raise other errors. SharpCompress's own
  exception types (LZMA data errors, invalid format, zstd) become a damage report; a partial listing is kept.

TV-07's remaining work is fuzzing the new engines and adding a native-engine worker if one is ever adopted.
