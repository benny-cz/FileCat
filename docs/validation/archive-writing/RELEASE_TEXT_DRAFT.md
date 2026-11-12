# Archive writing: release text

Plan §16.2 asks for README, help, capability, release-note and downgrade text in the implementing change. The routes of
the [route decision](RELEASE_ROUTE_DECISION.md) are listed (Windows x64 on NTFS, Linux x64 on ext4 and macOS on APFS,
every format; native edit storage is not listed yet), and the listing change published this text in the README and
[CAPABILITIES.md](../../CAPABILITIES.md) (which the help page shows). The release notes and the downgrade note below are
for the release's own notes, which the owner publishes with the signed packages.

## Release notes

**Create and change more archive formats.** Pack (Alt+F5) can create TAR, TAR + gzip, TAR + bzip2, TAR + Zstandard, 7z,
RAR5 and ZIP archives, and compress a single file to `.gz`, `.bz2` or `.zst`. Inside an archive whose format and contents
FileCat can write back exactly, F5/F6 add files, F7 creates a folder, F2 renames, F8 deletes and F4 edits a member. Every
change rebuilds the whole archive beside the original, checks the new file in a separate process, and only then replaces
the original. An interrupted change can be finished or discarded from Interrupted operations.

**ZIP changes now keep more.** ZIP archives are changed the same way as the other formats. Unchanged members are copied
as they are, with their exact times, Unix owners and comments, where the old route recompressed them and dropped those
fields. A ZIP that holds something FileCat cannot write back (encryption, extra fields it does not know, a self-extractor,
names in an unknown code page) is now read-only, and FileCat says why, where the old route silently dropped such data.

**What stays read-only, and why.** An archive is changed only if every member and all of its recorded metadata can be
written back exactly. Otherwise FileCat explains what it would lose, for example a gzip file that stores its original
name or time (the `gzip` command's default), links, sparse files, encryption FileCat cannot re-create, or unknown
extensions. Archives inside archives, archives on network shares, and archives on file systems FileCat has not qualified
stay read-only.

**Where archive writing runs.** Every archive is read and written by a separate worker process that cannot reach your
files, the network or other programs:

- Windows: a restricted AppContainer. The installed FileCat needs nothing more. A portable copy grants that worker read
  access to its own program files the first time it writes an archive (only in a folder you own; never its Data folder
  or anything else beside it). The framework-dependent build cannot run the worker.
- Linux: a confined worker (cgroup, seccomp, Landlock), from the .deb package. The AppImage cannot run the worker: its
  mounted image is not a place the confined worker may run from. Install the .deb, or extract the AppImage
  (`--appimage-extract`) into a folder on an ext4 drive.
- macOS: an App Sandbox worker with no access to system services. macOS lets an ordinary app set no hard memory limit, so
  FileCat watches the worker's memory and stops it above its budget; a very fast allocation can briefly exceed the budget
  before it is stopped. The system keeps an empty folder for the worker in `~/Library/Containers`
  (`io.github.benny-cz.filecat.archive-worker`); you can delete it after removing FileCat.

Where a copy cannot run its worker, archive writing on drives the release covers is unavailable there and says why; ZIP
archives on drives no route covers keep the old ZIP route, on every copy.

**Places FileCat does not write.** FileCat says before anything starts when it cannot write an archive's place. On Windows: a folder a cloud service syncs (OneDrive, iCloud, Dropbox), a folder reached through a junction, a symbolic link or a mount point (open the folder where it leads instead, such as the drive a profile folder was moved to), and, for an existing archive, a drive without NTFS transactions (some drives support none) or a read-only archive. On Linux and macOS: a folder another user can change, and an archive owned by another user, with a group you cannot give it, special permission bits, file flags (such as Finder's Locked), an access-list entry that denies access, encryption or verity, or extended attributes FileCat cannot keep (such as the download details browsers add on macOS); a folder reached through a symbolic link is judged where it really is. Everywhere: an archive with another hard link (a second name for the same file), and FileCat running as administrator or root. A ZIP in such a place keeps the old ZIP route. On macOS the home folder, Desktop, Documents and Downloads are writable: their access list only denies deleting the folder itself.

**Costs and limits.** A change rewrites the whole archive and needs free space for a second copy; there is no undo; at most
100,000 members, 8 GiB per member, 64 GiB in all and an 80 GiB archive.

## Downgrade

Archive edit sessions created by this version use a newer record format. An older FileCat keeps their working files on
disk but may not list the sessions. Archives are never rewritten back to an older form automatically; interrupted changes
keep their retained files until they are resolved in a version that understands them. ZIP edit sessions made before the
upgrade keep committing through the old ZIP route.

## Capability matrix rows (CAPABILITIES.md)

| Row | Text at enablement |
|---|---|
| Other archive formats | Replace "Changing these formats is not supported" with the listed formats and their per-format exceptions (above) |
| ZIP updates | Through the worker profile where a ZIP route is listed, with ZIP's read-only exceptions (above); the old route elsewhere |
| Platforms | Windows (installed and portable), Linux (.deb), macOS (app); not the AppImage or framework-dependent builds |

## Help and keyboard availability

The command palette and menus already state each archive command's specific unavailable reason. At enablement, the help
page's archive section lists the same commands as the release note, and the F-key bar text needs no change.
