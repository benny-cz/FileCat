# E-V02-L1 — paths longer than MAX_PATH on Windows

Asked by the owner (2026-10-01): does FileCat work with long paths, especially paths over `MAX_PATH` (260
characters) on Windows? Plan: V02 (local transfers, identity and fidelity), V19 (native platform).

## Why it is a question at all

Windows refuses a path over 260 characters to a program unless the program writes it as `\\?\C:\…` — or the program
declares itself long-path aware **and** the computer allows it (`LongPathsEnabled` in
`HKLM\SYSTEM\CurrentControlSet\Control\FileSystem`). That setting is **off by default**. All three FileCat executables
declare `longPathAware` in their manifests (`FileCat.App`, `FileCat.ShellHost`, `FileCat.PrivilegedHost`), but on a
default Windows that declaration does nothing; what carries FileCat there is .NET's own file APIs, which add the prefix
themselves, and FileCat's own native calls, which add it where they handle paths (`WindowsFileOperations.Long`, used for
renames, moves, deletes and stream listings; the hex editor's and recovery's own helpers). A static read found no native
call on a path that skips it.

## E-V02-L1-T1 — every operation, on both settings

`LongPathTests.Every_operation_works_on_paths_longer_than_MAX_PATH` builds a tree of ordinary folder names with a file
whose path is about 330 characters and another past 600, and runs every file operation FileCat offers through the
operations and jobs the app itself uses (`WindowsFileOperations`, `JobManager`, `WindowsFileSystemProvider`):

| Operation | Host, `LongPathsEnabled` = 1 | Lent VM, `LongPathsEnabled` = **0** (the default) |
|---|---|---|
| List a folder 300 characters deep | ok | ok |
| Copy a tree holding a file 630 deep (contents compared) | ok | ok |
| Rename a file 330 deep | ok | ok |
| Set and clear read-only on it | ok | ok |
| Move a file from 630 deep to 300 deep | ok | ok |
| Make a folder and a file 600 deep | ok | ok |
| Checksum a file 330 deep (as manifests and sidecars are checked) | ok | ok |
| List its alternate data streams (the hidden-data view) | ok | ok |
| Hex-edit it and save the change as a new file beside it | ok | ok |
| Delete a tree holding files 630 deep, for good | ok | ok |
| **Recycle** a file 330 deep | refused, unchanged | refused, unchanged |

The Recycle Bin is Windows' own limit, not FileCat's: it takes no path that long from any program, Explorer included.
FileCat says "Not deleted: the path is too long for the Recycle Bin. Nothing was changed." and leaves the file where
it was; a permanent delete (Shift+Del) works, as the table shows. The test asserts exactly this: everything else
succeeds, and recycling either succeeds or refuses with that reason and touches nothing.

Host: build `a5d4c2e`, Windows 11 Pro 26220. VM: the same test build, Windows 11 26300, via
`artifacts/vm/win-longpath.ps1` (output `artifacts/vm/win-longpath-survey.txt`). It runs on every CI Windows lane too.

## Not covered

What hands a path to another program cannot be made to take a long one by FileCat: opening a file in its default
application (`ShellExecuteEx`), the Shell's context menu, thumbnails and icons that the Shell draws, and the Recycle Bin
above all depend on Windows and on that program. Where they refuse, an icon falls back to its type's, and a launch
fails with Windows' message. Archives, search and folder comparison over long paths go through .NET's file APIs and
were not exercised separately here. A possible improvement, not a defect: when the Recycle Bin refuses an item for
its length, FileCat could offer the permanent delete then and there, as Explorer does.
