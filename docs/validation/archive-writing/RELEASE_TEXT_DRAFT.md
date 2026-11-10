# Archive writing: release text to publish at enablement (draft)

Plan §16.2 asks for README, help, capability, release-note and downgrade text in the implementing change. Released
documentation (README, [CAPABILITIES.md](../../CAPABILITIES.md), in-app help) correctly says that only ZIP is writable,
and it stays that way until a route is qualified and composed (ledger row 13). This draft holds the text to publish then,
per qualified route; delete each sentence for a format/platform that is not enabled.

## Release notes

**Create and change more archive formats.** Pack (Alt+F5) can create TAR, TAR + gzip, TAR + bzip2, TAR + Zstandard, 7z
and RAR5 archives, and compress a single file to `.gz`, `.bz2` or `.zst`. Inside an archive whose format and contents
FileCat can write back exactly, F5/F6 add files, F7 creates a folder, F2 renames, F8 deletes and F4 edits a member. Every
change rebuilds the whole archive beside the original, checks the new file in a separate process, and only then replaces
the original; an interrupted change can be finished or discarded from Interrupted operations.

**What stays read-only, and why.** An archive is changed only if every member and all of its recorded metadata can be
written back exactly. Otherwise FileCat explains what it would lose, for example a gzip file that stores its original
name or time (the `gzip` command's default), links, sparse files, encryption FileCat cannot re-create, or unknown
extensions. Archives inside archives, archives on network shares, and archives on file systems FileCat has not qualified
stay read-only.

**Costs and limits.** A change rewrites the whole archive and needs free space for a second copy; there is no undo; at most
100,000 members and 8 GiB per member; another hard link to the archive keeps the old content.

## Downgrade

Archive edit sessions created by this version use a newer record format. An older FileCat keeps their working files on
disk but may not list the sessions. Archives are never rewritten back to an older form automatically; interrupted changes
keep their retained files until they are resolved in a version that understands them.

## Capability matrix rows (CAPABILITIES.md)

| Row | Text at enablement |
|---|---|
| Other archive formats | Replace "Changing these formats is not supported" with the qualified formats and their per-format exceptions (above) |
| ZIP updates | Unchanged until the ZIP worker route is qualified (ZIP_WORKER_PROFILE_DESIGN.md) |

## Help and keyboard availability

The command palette and menus already state each archive command's specific unavailable reason; at enablement the help
page's archive section lists the same commands as the release note, and the F-key bar text needs no change.
