# FileCat implementation status

Resume point for implementing `docs/design/FILECAT_PRODUCT_ARCHITECTURE_AND_IMPLEMENTATION_PLAN.md`.
Work happens directly on `main`; every chunk is committed and pushed. Keep this file compact.

## Build, test, run

```
dotnet build FileCat.slnx
dotnet test FileCat.slnx                   # Core 96 tests, Windows integration 8 (incl. real recycle/restore)
dotnet run --project src/FileCat.App        # [paths] --left P --right P --profile NAME --workspace NAME --new-instance --reset-layout
```

Portable mode: empty `FileCat.portable` next to the exe (state in `Data/`). Logs/crash reports: `%LOCALAPPDATA%\FileCat\diagnostics`.

## Layout

| Project | Contents |
|---|---|
| `src/FileCat.Core` | Resources/providers, `ListingModel` (paged store, background sort/filter, identity marks), masks, commands/keymap, I/O scheduler, jobs (scheduler, journal, executors, undo), content (paged reader, search, encodings), metadata service, tools launcher, result sets, state |
| `src/FileCat.Platform.Windows` | CopyFile2/MoveFileEx, IFileOperation recycle + restore, junctions, shell (icons by extension, properties, reveal, terminals), drives, SMB shares + sign-in, MotW |
| `src/FileCat.App` | Avalonia 12.1 UI: `FileListControl`, panels/tabs/workspace, overlay dialogs, operation center, viewer (text/hex), settings, themes |

## Decisions taken during implementation (also to be reflected in the plan)

- ADR-02 → custom-drawn virtualized control (TableView conflates focus and marks; one container per row).
- ADR-04 → append-only CRC-checked journal per job (no SQLite: no native dependency). Torn lines are skipped; recovery appends on a fresh line.
- TV-03 partial (this machine): pre-delete abort guard + verified recycle outcome; restore via the Recycle Bin namespace item's `undelete` verb works (the Shell reports the physical `$R…` path, not the bin item).
- ADR-16 additions: no Ctrl+Alt+letter chords (AltGr); F11 maximize, F12 panel picker, Shift+F12 target, Ctrl+J operations, Ctrl+Shift+P palette, Ctrl+E command line, Ctrl+S quick filter; Space toggles the mark without moving.
- Viewer text mode scrolls by byte offset (no line index); UTF-8 search maps chars→bytes with maximal-subpart rules.

## Phase status

| Phase | Status |
|---|---|
| P1 walking slice | **Done**: browse, marks, quick search, F3 viewer, F4/Shift+F4 editor (TV-17 rules), F5/F6 Start/Queue, conflicts, F7, F8 recycle with preflight, Shift+F8, rename, undo, journal + interrupted-job review, drag & drop, clipboard |
| P2 scalable workspace | **Mostly done**: tabs (lock/return-to-root, reopen, list, move/copy), multi-panel targets, bookmarks, named workspaces, single instance, persistence + autosave, watchers, metadata columns + analysis sort, settings dialog. **Pending**: listing spill tier (AI-10) |
| P3 v1 | **Mostly done**: SMB shares/sign-in/connect, command line, viewer search/goto/checksums/encodings, history, themes, diagnostics export, user menu (F9), Alt+F7 search → result sets, Ctrl+B flat view, Ctrl+F10 compare-and-mark, read-only ZIP (browse, F3, F5/unpack, MotW), Ctrl+Q quick view, attributes job, icon, `eng/publish.ps1` (portable/fdd/SBOM), Inno Setup script, CI workflow, notices, README |
| P4–P10 | Pending |

## Resume here (next slices, in order)

1. P2 leftover: listing spill tier for huge folders (AI-10) — memory-budgeted `EntryStore` with a private temp-file tier.
2. P3 polish: in-row rename editor, keyboard-reference/help page, TV-10 accessibility pass, App.Tests (headless) smoke tests.
3. P4a Registry provider (typed values, views, F4 editors, export/import, search) + per-plan elevation broker (`FileCat.PrivilegedHost`).
4. P4b fixed-length hex editing (patch overlay, undo, save strategies).
5. P5 ZIP create/update + edit sessions; P6 SFTP; P7 diff/sync/inspectors; post-v1 slices (bulk rename, links, manifests); P8–P10.

## Known gaps

- Code signing needs a SignPath Foundation project (manual; CI has the placeholder step).
- Inline rename uses a prompt dialog; App.Tests project has no tests yet.
