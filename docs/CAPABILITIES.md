# What works where (v1 capability matrix)

PLATFORM-003: known gaps are documented here and explained in the app wherever you run into them. When a command is
unavailable for a location, FileCat's status line says why (for example, "Items can be pasted only into a file-system folder").

## Platforms

| Platform | Tier | Notes |
|---|---|---|
| Windows 11 x64 (serviced releases) | **A: release-blocking.** Correctness, UX, performance, accessibility, and packaging are tested. | Native copy, move, and delete (CopyFile2/MoveFileEx); verified Recycle Bin with restore; junctions; Mark-of-the-Web; SMB shares with sign-in; extension icons; terminals |
| Linux (Ubuntu), macOS (Apple silicon) | **B: development lanes.** Core tests run in CI. | Portable file operations, freedesktop or `~/.Trash`, no Mark-of-the-Web, no native icons. Not released before the P9 gates. |

## Locations

| Location | Browse | View (F3) | Copy from (F5) | Copy into | Move, rename | Create (F7, Shift+F4) | Delete (F8, Shift+F8) | Watch |
|---|---|---|---|---|---|---|---|---|
| Local folders (NTFS, ReFS, Dev Drive, FAT, exFAT) | Yes | Yes | Yes | Yes | Yes | Yes | Recycle where the volume has a bin; otherwise explicit permanent delete | Yes |
| SMB shares (`\\server\share`, mapped drives) | Yes (sign-in prompt when needed) | Yes | Yes | Yes | Yes | Yes | Permanent delete only (no Recycle Bin; asked explicitly) | Yes (best effort) |
| Network servers (`\\server`) | Share list | – | – | – | – | – | – | – |
| This PC | Drives | – | – | – | – | – | – | – |
| ZIP archives | Yes, including duplicates and unsafe names shown as unavailable; archives inside archives open read-only | Yes | Yes (extract, with Mark-of-the-Web from the outermost file) | Yes (F5 and Alt+F5 add; existing names ask once) | Rename (F2); moving out is copy then delete | Folder entries (F7); F4 edits a member through an explicit edit session | Yes (members, one duplicate copy at a time) | – |
| SFTP servers (`sftp://`, saved connections in Alt+F1/Alt+F2) | Yes, after the server key and sign-in; links show what they point to | Yes | Yes (downloads marked as coming from the server) | Yes (a temporary name, then a rename; replacing is atomic where the server supports posix-rename) | Yes: F6 within a server renames; F6 to a local folder deletes on the server only what arrived completely | Folders (F7); copy new files in with F5 | Permanent only (no Recycle Bin; asked explicitly) | – (Ctrl+R refreshes) |
| Search results and flat view | Yes | Yes | Yes (keeps relative folders unless you flatten) | No | Acts on the original items; the set follows renamed and moved items | No | Acts on the originals; removing from the set (Ctrl+Del) never deletes | – |
| Working sets (Navigate → Working sets, or Alt+F1/Alt+F2) | Yes: named sets kept between sessions; each item shows where it is, and vanished ones show as unavailable | Yes | Yes (flat) | Adds references only (F5 or F6 from another panel, or Ctrl+Shift+W); nothing is copied | Acts on the originals; the set follows them | F7 in the list of sets creates a set; F2 renames one | Ctrl+Del removes from the set; F8 deletes the originals and drops their references; F8 in the list forgets a set, never its items | – |

## Known gaps in v1 (planned later)

| Gap | Where it is explained | Planned |
|---|---|---|
| Registry browsing and typed value editing | Local Registry panels with explicit 32/64-bit views (switchable in place), raw inspection, bounded search, guarded jobs on marked items with Undo, `.reg` backups before key deletion, scoped `.reg` import/export (F5 to a folder offers it), binary save/load of value data, HKCR/HKCC writable-location route, change notifications, read-only key ACL inspection, and administrator retry. Import is non-atomic; `.reg` omits ACLs and the view; links are followed only on request | P4a |
| Elevated operations (per-plan broker) | Installed builds: "Retry as administrator" for items that failed with access denied (Registry changes, delete, copy, move within a drive, rename, create folder, attributes). One UAC approval per plan; the helper shows the exact steps, refuses links, and exits. Portable ZIP: none | P4a; TV-15 VM checks pending. FileCat never runs elevated by itself and warns when started elevated |
| Hex editing | Dedicated fixed-length editor for local Windows files (File menu, or F6 in the viewer): typing in hex and text columns, find, paste, bounded undo/redo, protected baseline, journaled in-place save with in-editor or later guarded recovery, sparse-aware Save As, and patch export/apply. Existing-target saves are non-atomic; network files and links are refused | P4b; TV-04 external fixtures pending |
| Bulk rename | Ctrl+M on files and folders on disk: name and extension masks (counters, character ranges, folder, date, and time), text or regular-expression replace, letter case, a live preview that blocks invalid and colliding names, editing the new names in your editor, swaps and chains, and Undo. Operations finishes a rename interrupted by a crash. Archive members rename one at a time (F2) | Post-v1 slice (OPS-007) |
| Creating links | File → Create link…: symbolic links (need Developer Mode or administrator rights on Windows; FileCat checks first and suggests a junction or hard link), junctions to local folders (Windows), and hard links to files on the same drive. FAT and exFAT drives support none of them. Undo removes links that still point where they were made | Post-v1 slice |
| Checksum manifests | `.sha256`, `.sha512`, `.sha1`, `.md5`, `.sfv`, and `SHA256SUMS`-style files (GNU, BSD-tagged, and SFV lines): File → Verify checksum manifest…, or Calculate checksums… on a manifest. Only files in and below the manifest's folder are verified; MD5, SHA-1, and CRC-32 matches show integrity, not authenticity. Calculate checksums… saves a manifest | Post-v1 slice |
| Running a command for each item | Ctrl+G on files and folders on disk: a program with placeholders, or a shell command line (cmd.exe on Windows, /bin/sh elsewhere) with names quoted for it. Commands run one after another in each item's folder; output is not shown, but a failure reports its exit code and last output lines. A canceled job leaves a running program to finish | Post-v1 slice |
| Synchronizing folders | Ctrl+F10 → Include subfolders → Synchronize…: one-way Update (new and newer items) or Mirror (also replaces differing files and removes what only the target has, to the Recycle Bin, or permanently only when chosen for targets without one). Every step is listed and can be excluded (Space); names that differ only in letter case and unsafe names are left out; the steps run as ordinary jobs. The target must be a folder on disk; no two-way synchronization | P7 |
| Inspecting executables and images | Viewer (F3) → Info, or Ctrl+I: Windows executables and DLLs (architecture, ASLR/NX/CFG, sections, imports, exports, .NET header, version information, requested execution level, symbols file name, and whether a signature is present, which is never verified) and images (PNG, JPEG, GIF, WebP, BMP, ICO, TIFF: dimensions, depth, animation, color profile, EXIF orientation, camera, and date). Read statically from bounded headers; nothing is loaded or run | P7 |
| Other archive formats (TAR, 7z, RAR, ISO) | Opened through their system association | P8 |
| ZIP updates | Each change rebuilds the archive beside itself and replaces it only when verified and unchanged since you saw it; encrypted archives and archives inside archives are read-only; there is no undo | – (by design; see ADR-07) |
| FTP, FTPS | – | P8 |
| Shell thumbnails and programs' own icons | Quick view shows the Shell's thumbnail for binary files, and programs, icon files, and similar types show their own icons. Windows' handlers for these run only in a separate helper at low integrity that cannot start programs (ADR-06). Shortcut-like files, themes, `desktop.ini`, and cloud placeholders are never handed to the Shell. Network and removable drives are included only if you opt in (Settings → Privacy) | P7 |
| Shell property handlers and context-menu handlers | Not run: property columns come from FileCat's own readers, and the context menu offers FileCat's commands | Later, through the same helper |
| Editing files on servers | F4 on a file on an SFTP server edits a private copy in your editor; Commit (F4 again, or File → Edit sessions) writes it back only while the server file is still the version the edit started from, and a changed file is never overwritten without an explicit choice | P6 (done) |
| SSH terminal | Open terminal on an SFTP panel starts the OpenSSH client in that folder; it checks host keys against OpenSSH's own known_hosts. SSH agent sign-in in FileCat itself is not supported yet | P6 (done); agent later |
| Translations | English only. Command titles and key-bar labels can already be translated with `lang/<culture>.json` next to the executable. | Other UI text once translators and QA exist |
| Alternate data streams on FAT and exFAT | Each lost stream (and a lost download mark) is named per file | – (file-system limit) |
