# FileCat implementation status

Resume point for implementing `docs/design/FILECAT_PRODUCT_ARCHITECTURE_AND_IMPLEMENTATION_PLAN.md`.
Work happens on `main`; every chunk is committed and pushed. Keep this file compact.

## How to build and run

```
dotnet build FileCat.slnx
dotnet test tests/FileCat.Core.Tests
dotnet run --project src/FileCat.App        # args: [paths] --left P --right P --profile NAME --new-instance --reset-layout
```

Portable mode: put an empty `FileCat.portable` next to the executable (state goes to `Data/`).

## Solution layout

| Project | Contents |
|---|---|
| `src/FileCat.Core` | Resource model (Location/EntryData/ItemRef/providers), listing engine (paged store, background sort/filter, identity marks), mask language, commands + keymap (ADR-16), I/O scheduler with hang isolation, state persistence, file-operation contracts, portable platform |
| `src/FileCat.Platform.Windows` | Shell services (extension-only icons, properties, reveal, terminals), drive list, SMB share listing + credential prompt, volume profiles, recycle classification, Mark-of-the-Web |
| `src/FileCat.App` | Avalonia 12.1 UI: custom virtualized `FileListControl`, panels/tabs/workspace view models, overlay dialogs, themes, command dispatch |
| `tests/*` | xUnit v3 (Core: 66 tests) |

## Decisions taken during implementation (plan updates)

- **ADR-02 → custom control.** Avalonia 12.1 `TableView` is a row-container `ItemsControl`: its selection conflates focus and marks and it realizes a control per row. FileCat uses a custom-drawn control over `ListingModel` (paged `EntryStore`, identity bit-set marks). Spill tier: pending (P2).
- **ADR-04 → append-only checksummed journal** (planned, next chunk) instead of SQLite: no native dependency (signing gate, ARM64), simpler reconciliation.
- **Keymap (ADR-16):** no Ctrl+Alt+letter chords (AltGr on CZ/PL/DE layouts); F11 maximize panel, F12 focus-panel picker, Shift+F12 choose target, Ctrl+Shift+T reopen tab, Ctrl+J operations, Ctrl+Shift+P palette, Ctrl+E command line, Ctrl+S quick filter. Space toggles the mark without moving (and sizes folders).
- `SortSpec` flags are inverted (`MixDirectories`, `Ordinal`) so `default` is the correct order.

## Phase status

| Phase | Status |
|---|---|
| P1 walking slice | **In progress.** Done: browsing, streaming listing, sort, quick search, marks (Insert/Space/masks/invert/same-ext/restore), tabs, drive/location menu, history, bookmarks, palette, key bar, themes, command line (`cd` + terminal), single instance. **Next:** job engine (copy/move/delete/recycle/mkdir/rename), journal, operation center, F3 viewer, F4 editor launcher (TV-17), native CopyFile2 + verified Shell recycle |
| P2 scalable workspace | Partly done (multi-panel targets, tabs, workspace persistence). Pending: spill store, watchers, column profiles UI, named workspaces |
| P3 v1 | Pending (search/result sets, compare, ZIP, viewer, packaging, CI) |
| P4–P10 | Pending |

## Known gaps / TODO

- File operations commands currently report "not available in this build yet".
- No app icon yet; no CI workflow yet.
