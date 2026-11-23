# What the file list's columns show — 22 November 2026

The owner added the optional **Reparse type** column on Windows and found it blank for ordinary items, right beside the right-aligned **Hard links** numbers. The numbers read as reparse types, and nothing said what a type meant. This receipt covers the fix and the column audit that followed it: every optional column was checked for blank, cryptic or wrong values, on Windows x64, Ubuntu and macOS.

## Changes

| Commit | What changed |
|---|---|
| `8c7a09559` | Reparse type shows a muted **none** for an item observed without a reparse point. It sorts before every type, so a descending sort brings reparse points to the top; unknown tags and failed reads still sort last both ways. Each value explains itself: what the type does, its tag number, its published `IO_REPARSE_TAG_…` name and its flag bits. Column headers got tooltips. Extra data on Windows counts exactly where `FileEaInformation` reports no extended attributes (their total size, read without names or values), instead of "Not inspected" on nearly every row; a folder without named streams no longer fails its stream inventory. |
| `8035f4035` | Tooltips laid out instead of run together, in every theme: a header's title, lead, legend (terms in the theme's key color) and quiet hints; a value's column name, the value, its meaning, its tag line as code and its source in a quieter color. |
| `661d2a7c2` | Recycle Bin folders show their recorded size without the "still counting" ellipsis; drive rows sort by the free space they show. |
| `4cc8813aa` | Version builds the number from the fixed file information (old-template strings such as "1, 0, 0, 1" showed as "1,"); Dimensions walks JPEG segments past the first 64 KiB. |

## Machines

- **Windows x64, workstation:** the Core and Windows tests, and the App tests at medium integrity (a hidden script started through explorer.exe).
- **Windows x64, VMware guest:** Windows 11 Pro Insider build 26340, a local administrator signed in, each run in that desktop with the account's limited token. FileCat was a self-contained win-x64 Release build of this work.
- **Ubuntu 24.04.5 VMware guest** and the **Apple silicon Mac (macOS 27.2.1):** the Core, App and Rendering test projects built from a source archive of `661d2a7c2` with each machine's .NET 10.0.101 SDK.

## Before and after

The fixture folder held a plain file, hard links, a junction, directory and file symbolic links, NTFS- and WOF-compressed copies of a 317 KiB text, a sparse file, hidden, read-only and system files, a copy of `cmd.exe`, a PNG, a file with a `Zone.Identifier` stream, a file with a named stream and a `SHA256SUMS` file.

- [Before](receipts/2026-11-22-column-meanings/before-reparse-type-blank.png): the Reparse type column is blank for every ordinary item, beside Hard links.
- [After](receipts/2026-11-22-column-meanings/after-storage-profile.png): **none** in a muted color for ordinary items; Symbolic link, Junction or volume mount and, in `WindowsApps`, App execution alias. This build preceded the tooltip layout of `8035f4035`; the list's cells are unchanged since.

## Extra data

- [Before](receipts/2026-11-22-column-meanings/extra-data-before.png): "Not inspe…" on nearly every row; only the two files with a named stream show "1+".
- [After](receipts/2026-11-22-column-meanings/extra-data-after.png), release build `ABBA9009…`: **0** for most items, **1** for `downloaded.txt` (its `Zone.Identifier`) and `streams.txt`, **1+** for binaries that carry extended attributes (the copy of `cmd.exe`, `powershell.exe`), and **—** for links and the WOF file, whose inventory is not qualified. The tooltip on `downloaded.txt` lists a category a line: 1 named stream, 0 extended attributes (FileEaInformation reports none), and when it was read.

## WOF hides its tag

`compact.exe /c /exe:xpress4k` stored the 324,602-byte `wof.txt` in 24,576 bytes ("13,2 to 1"), yet `fsutil reparsepoint query` reported "not a reparse point" and the column shows **none**: the Windows Overlay Filter hides its tag from the ordinary query. The **none** tooltip says so, and points to File-system details → Read full record, whose [copied report](receipts/2026-11-22-column-meanings/wof-full-record.txt) shows "Windows Overlay Filter backing: compressed by the file provider with XPRESS4K" from `FSCTL_GET_EXTERNAL_BACKING`. The native column test now covers a WOF-compressed file the same way.

## Tooltips in every theme

- [Windows, real app](receipts/2026-11-22-column-meanings/tooltips-real-app-windows.png): the release build (`FileCat.Core.dll` `ABBA9009…`) on the VMware guest in each of the seven themes, the pointer on the Reparse type header and on plain.txt's **none**, the screen captured around the window. UI Automation found the rows, so each theme's own font and row height placed the pointer.
- [Windows, rendered](receipts/2026-11-22-column-meanings/tooltips-rendered-windows.png), [Ubuntu, rendered](receipts/2026-11-22-column-meanings/tooltips-rendered-linux.png) and [macOS, rendered](receipts/2026-11-22-column-meanings/tooltips-rendered-macos.png): `ColumnTooltipRenderingTests` draws a header tooltip and a value tooltip in all seven themes with each platform's own fonts, from a fixture reader so every platform shows the same values. It checks the tooltip surface, text, key and muted colors, and passed on all three.
- The App tests check, in every theme, a header tooltip's surface and text colors, its legend terms in the key color, its hints in the muted color, and that the muted **none** reaches 4.5:1 on an active and an inactive panel.

## Test runs

| Platform | Core | App | Rendering |
|---|---|---|---|
| Windows x64 | 62 passed (the changed classes) | 43 run, 37 passed, 6 skipped | 1 passed |
| Ubuntu 24.04.5 | 73 passed | 127 run, 88 passed, 39 skipped (Windows-only) | 1 passed |
| macOS 27.2.1 | 73 passed | 127 run, 89 passed, 38 skipped (Windows-only) | 1 passed |

The Windows native classes `WindowsDetailsNativeGapTests` and `WindowsHiddenDataTests` passed (21 run, 3 skipped). The theme test ran six times in a row without a failure after its tooltip check moved before queued input: in the headless platform the pointer may leave the list for the opened popup, which closes the tooltip as leaving it does in the app.

## Left open

- **Find and Flat view** show a found link as "P" and `<DIR>`: the search reads no reparse tags, and the details plan forbids per-item queries made only to match a listing's completeness. A fix that reads a found reparse point's own tag is ready on a branch, waiting for the owner's decision on that rule.
- **Created on Linux** shows the older of the modification and change times: .NET reads no birth time there. On the Ubuntu guest a file created on 22 November with its modification time set to 2020 showed `Birth 2026-11-22` in `stat` and a creation time of 1 January 2020 through .NET. Fixed on 23 November: see [Created on Linux](2026-11-23-linux-birth-time.md).
