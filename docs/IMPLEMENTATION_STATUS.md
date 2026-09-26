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
| P3 v1 | **In progress**: done — SMB shares/sign-in/connect, command line, viewer search/goto/checksums/encodings, history, themes, diagnostics export, user menu (F9). **Next**: Alt+F7 search + result sets + flat view, Ctrl+F10 compare-and-mark, read-only ZIP, quick view pane, attributes dialog, packaging (portable ZIP, installer script, CI, notices) |
| P4–P10 | Pending |

## Known gaps / TODO

- No app icon; no CI workflow yet; App.Tests project empty.
- Inline rename uses a prompt dialog (not in-row editing).
