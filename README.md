# FileCat

A keyboard-first, dual-panel file manager and system-resource navigator for Windows (Linux and macOS
builds run the portable core), written in C# on .NET 10 and Avalonia 12. MIT-licensed.

FileCat follows the conventions that Open Salamander, Total Commander, and FAR Manager share —
F3–F8, Insert/Space marking, masks on Num+/Num−/Num*, Tab to the other panel — and adds tabs,
additional panels with explicit targets, and an operation engine that says exactly what completed,
what failed, and what remains uncertain.

## Highlights

- **Panels and tabs**: two panels by default, more when there is room, each with its own target;
  locked tabs, recently closed tabs, bookmarks (Ctrl+0–9), histories (Alt+F11/F12), named workspaces.
- **Fast listings**: a custom virtualized list streams millions of entries without per-row controls;
  quick search as you type, quick filter (Ctrl+S), column profiles (Alt+0–4), lazy metadata columns.
- **Safe operations**: staged copies published only when complete, Start or Queue per drive, overlap
  detection, conflict prompts with compare, verified Recycle Bin outcome (never a silent permanent
  delete), guarded undo, and a crash-safe journal with interrupted-operation review.
- **Find and compare**: Alt+F7 search into result sets that act on the originals, flat view (Ctrl+B),
  compare-and-mark (Ctrl+F10).
- **Viewer**: F3 text/hex viewer that opens multi-gigabyte files instantly, encoding detection with
  evidence, search, go to offset, checksums, follow mode; Ctrl+Q quick view.
- **Archives**: ZIP browsing and extraction (read-only) with Mark-of-the-Web propagation.
- **Windows integration**: native CopyFile2 (keeps ReFS block cloning and SMB offload), SMB share
  listing with credential prompts, terminals, reveal in Explorer, extension-only icons (no
  third-party Shell handlers run inside FileCat).
- **Git state on icons**: clean, changed, added, untracked, and conflicted items in local Git folders
  get small badges. Status loads in the background and refreshes with the folder or when FileCat regains focus.
- **Themes**: Classic (follows light/dark), Cyberpunk, Psychedelic, and High Contrast (automatic with
  the OS setting).

## Build and run

Requires the .NET 10 SDK. No paid components or accounts are needed.

```
dotnet build FileCat.slnx
dotnet test FileCat.slnx
dotnet run --project src/FileCat.App -- [path] [--left PATH] [--right PATH] [--profile NAME]
```

Packages: `pwsh eng/publish.ps1 -Version 0.1.0` builds the self-contained, portable, and
framework-dependent payloads; `eng/installer/FileCat.iss` builds the per-machine installer.
A portable copy keeps its settings in `Data/` next to the executable (marker file `FileCat.portable`).

## Keyboard essentials

| Keys | Action |
|---|---|
| F3 / F4 / Shift+F4 | View / edit (external editor) / edit new file |
| F5 / F6 / Shift+F5 | Copy / move or rename / duplicate here |
| F7 / F8 / Shift+F8 | Create folder / delete to Recycle Bin / delete permanently |
| F2 | Rename in place |
| Insert / Space | Mark and move down / mark (sizes folders) |
| Num+ / Num− / Num* / Num/ | Select by mask / unselect / invert / restore selection |
| Tab / Ctrl+U | Switch to target panel / swap locations |
| Ctrl+T / Ctrl+W / Ctrl+Tab | New / close / next tab |
| Alt+F7 / Ctrl+B / Ctrl+F10 | Find files / flat view / compare directories |
| Ctrl+Shift+P / F1 / Ctrl+J | Command palette / keyboard reference / operations |

Every binding can be changed in Settings (Ctrl+,).

## Documentation

- Design: `docs/design/FILECAT_PRODUCT_ARCHITECTURE_AND_IMPLEMENTATION_PLAN.md`
- Implementation status: `docs/IMPLEMENTATION_STATUS.md`
- Third-party notices: `THIRD-PARTY-NOTICES.md`
- Security policy and vulnerability reporting: `SECURITY.md`
- Release servicing (no updater; security-release cadence): `docs/SERVICING.md`
- Validation records: `docs/validation/` (TV-01 scale and latency)
