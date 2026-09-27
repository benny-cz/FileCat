# FileCat implementation status

Resume point for implementing `docs/design/FILECAT_PRODUCT_ARCHITECTURE_AND_IMPLEMENTATION_PLAN.md`.
Work happens directly on `main`; every chunk is committed and pushed. Keep this file compact.

## Build, test, run

```
dotnet build FileCat.slnx
dotnet test FileCat.slnx                   # Core 146, Windows integration 9, App headless 8 tests
$env:FILECAT_UI_SCALE_COUNT=1000000; dotnet test tests/FileCat.App.Tests --filter FullyQualifiedName~UiScale # optional TV-01 headless run
FileCat.exe --benchmark 1000000 --benchmark-panels 4   # TV-01 native benchmark (isolated state, JSON results; docs/validation/TV-01.md)
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

- ADR-02 → custom-drawn virtualized control. Filesystem records spill to user-local delete-on-close files at a 96 MiB budget; sort/view indexes use a shared 512 MiB reservation and external sorted runs plus mapped indexes beyond it.
- ADR-04 → append-only CRC-checked journal per job (no SQLite: no native dependency). Torn lines are skipped; recovery appends on a fresh line.
- TV-03 partial (this machine): pre-delete abort guard + verified recycle outcome; restore via the Recycle Bin namespace item's `undelete` verb works (the Shell reports the physical `$R…` path, not the bin item).
- ADR-16 additions: no Ctrl+Alt+letter chords (AltGr); F11 maximize, F12 panel picker, Shift+F12 target, Ctrl+J operations, Ctrl+Shift+P palette, Ctrl+E command line, Ctrl+S quick filter; Space toggles the mark without moving.
- Viewer text mode scrolls by byte offset (no line index); UTF-8 search maps chars→bytes with maximal-subpart rules.

## Phase status

| Phase | Status |
|---|---|
| P1 walking slice | **Done**: browse, marks, quick search, F3 viewer, F4/Shift+F4 editor (TV-17 rules), F5/F6 Start/Queue, conflicts, F7, F8 recycle with preflight, Shift+F8, rename, undo, journal + interrupted-job review, drag & drop, clipboard |
| P2 scalable workspace | **Done**: column profiles (Alt+0–9) editable in Settings → Columns (fields incl. metadata, order, widths, 0 = fill), dragged widths persist per profile (a fill column's border resizes its neighbor), tabs (lock/return-to-root, reopen, list, move/copy), multi-panel targets, bookmarks, named workspaces, single instance, persistence + autosave, watchers, metadata columns + analysis sort, settings dialog. TV-01 executed (native 1M × 4: complete 2.9 s, re-sort 270 ms, held paging p95 16.9 ms; `docs/validation/TV-01.md`). At scale: mapped spill sorting, glyph-run cells, low-latency swap chain, record/name spill, shared 512 MiB index cap with external sort/view tier, geometric batching, four-panel headless UI scale, bounded job overlap scopes/header, streaming journal recovery, huge selections captured as leased store-index snapshots (`SelectionSnapshot`; jobs stream them, outcomes are per-position bitsets, undo capped at 100k steps, recycle in 2,048-item chunks) |
| P3 v1 | **Mostly done**: SMB shares/sign-in/connect, command line, viewer search/goto/checksums/encodings, history, themes, diagnostics export, user menu (F9), Alt+F7 search → result sets, Ctrl+B flat view, Ctrl+F10 compare-and-mark, read-only ZIP (browse, F3, F5/unpack, MotW), Ctrl+Q quick view, attributes job, icon, `eng/publish.ps1` (portable/fdd/SBOM), Inno Setup script, CI workflow, notices, README, in-row rename, searchable F1 help + headless UI smoke; truthful outcomes (full/offline/AV-blocked/policy/device/cloud/in-use with quiet retries, racing sources; fault-injection tests), alternate-stream and MotW loss named per file, ZIP lying-size/corruption fuzzing, mask/encoding fuzzing, shutdown-block reason while jobs run, elevated-start warning, device not-responding notices, Esc stops folder sizing, UI-stall/listing/queue-depth tracing; per-type associations (mask → View/Edit/Open programs, Settings → Tools), Alt+F8 command history, opt-in daily update check + Help → Check for updates (notify only), SECURITY.md, docs/SERVICING.md |
| P4–P10 | Pending |

Scale (this machine, details in `docs/validation/TV-01.md`): core 1M × 1 complete 1.6 s (in memory 1.4 s), forced external index 1.7 s, 1M × 4 2.3 s / peak private 348 MiB; native 1M × 4 first rows 566 ms, complete 2.9 s, cursor handler p95 0.2 ms, startup (R2R) ≈ 0.9 s.

## Resume here (next slices, in order)

1. P3 polish: TV-10 accessibility pass (automation peers for viewer/hex/quick view, names, focus announcements, headless reachability test).
2. P3 docs: ADR records (02/03/04/15/16), validation records (TV-03/13/14/16/17), capability matrix, command-title localization infrastructure.
3. P4a Registry provider (typed values, views, F4 editors, export/import, search) + per-plan elevation broker (`FileCat.PrivilegedHost`).
4. P4b fixed-length hex editing (patch overlay, undo, save strategies).
5. P5 ZIP create/update + edit sessions; P6 SFTP; P7 diff/sync/inspectors; post-v1 slices (bulk rename, links, manifests); P8–P10.

## Known gaps

- Code signing needs a SignPath Foundation project (manual; CI has the placeholder step).
- Run full assistive-technology and keyboard workflow checks on target OS hardware (TV-10).
