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
| ZIP archives (read-only) | Yes, including duplicates and unsafe names shown as unavailable | Yes | Yes (extract, with Mark-of-the-Web) | No: ZIP writing is P5 | No | No | No | – |
| Search results and flat view | Yes | Yes | Yes (keeps relative folders unless you flatten) | No | Acts on the original items | No | Acts on the originals; removing from the set never deletes | – |

## Known gaps in v1 (planned later)

| Gap | Where it is explained | Planned |
|---|---|---|
| Registry browsing and typed value editing | Local Registry panels have explicit views, raw inspection, bounded search, guarded jobs, scoped `.reg` import/export, change notifications, and read-only key ACL inspection. Import is non-atomic; `.reg` omits ACLs and the 32/64-bit view. Links are followed only on request; elevated retry remains | P4a |
| Elevated operations (per-plan broker) | Installed builds: "Retry as administrator" for items that failed with access denied (Registry changes, delete, copy, move within a drive, rename, create folder, attributes). One UAC approval per plan; the helper shows the exact steps, refuses links, and exits. Portable ZIP: none | P4a; TV-15 VM checks pending. FileCat never runs elevated by itself and warns when started elevated |
| Hex editing | Dedicated fixed-length editor for local Windows files (File menu, or F6 in the viewer): typing in hex and text columns, find, paste, bounded undo/redo, protected baseline, journaled in-place save with in-editor or later guarded recovery, sparse-aware Save As, and patch export/apply. Existing-target saves are non-atomic; network files and links are refused | P4b; TV-04 external fixtures pending |
| Creating or updating ZIP archives; other archive formats | Opened through their system association | P5, P8 |
| SFTP, FTP, FTPS | – | P6, P8 |
| Per-file Shell thumbnails, properties, and context-menu handlers | Icons are by extension only (safety policy) | Out-of-process host, P7 |
| Editing inside archives or remote locations | "Copy the item out with F5 to edit it" | Edit sessions, P5–P6 |
| Translations | English only. Command titles and key-bar labels can already be translated with `lang/<culture>.json` next to the executable. | Other UI text once translators and QA exist |
| Alternate data streams on FAT and exFAT | Each lost stream (and a lost download mark) is named per file | – (file-system limit) |
