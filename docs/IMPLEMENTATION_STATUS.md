# FileCat implementation status

This is the resume point for implementing `docs/design/FILECAT_PRODUCT_ARCHITECTURE_AND_IMPLEMENTATION_PLAN.md`.
Work happens directly on `main`, and every chunk is committed and pushed. Keep this file compact.

## Build, test, run

```
dotnet build FileCat.slnx
dotnet test FileCat.slnx                              # Core 166, Windows integration 12, App headless 13 tests
FileCat.exe --benchmark 1000000 --benchmark-panels 4  # TV-01 native benchmark (isolated state, JSON results)
dotnet run --project src/FileCat.App                  # [paths] --left P --right P --profile NAME --workspace NAME --new-instance --reset-layout
```

- **Portable mode:** put an empty `FileCat.portable` next to the exe; state goes to `Data/`.
- **Logs and crash reports:** `%LOCALAPPDATA%\FileCat\diagnostics`.
- **Environment switches:**
  - `FILECAT_RENDERING=compat` uses Avalonia's default composition.
  - `lang/<culture>.json` next to the exe translates command titles.

## Layout and records

| Path | Contents |
|---|---|
| `src/FileCat.Core` | Providers, listing (spillable store, mapped sorting, external index, selection snapshots), masks, commands and keymap, I/O scheduler, jobs (scheduler, journal, executors, undo), content, metadata, tools and associations, result sets, state |
| `src/FileCat.Platform.Windows` | CopyFile2/MoveFileEx, IFileOperation recycle and restore, junctions, streams, Mark-of-the-Web, shell (icons, terminals, shutdown block, elevation), SMB shares and sign-in |
| `src/FileCat.App` | Avalonia 12.1 UI: glyph-run `FileListControl`, panels, tabs, workspace, overlay dialogs, operation center, viewers, settings (column profiles, associations), themes, benchmark |
| `docs/adr/` | Decided ADRs: 02, 03, 04 (append-only journal instead of SQLite), 10, 15, 16. The plan's §26 points to them. |
| `docs/validation/` | TV-01 (scale and latency) and the P3 validations (TV-03/07/10/13/14/16/17 plus truthful outcomes) |
| `docs/CAPABILITIES.md`, `docs/SERVICING.md`, `SECURITY.md` | What works where; release servicing; vulnerability reporting |

## Phase status

| Phase | Status |
|---|---|
| P1 walking slice | **Done** |
| P2 scalable workspace | **Done.** Tabs, multi-panel targets, bookmarks, workspaces, single instance, persistence, watchers, metadata columns, column profiles (Settings → Columns, persisted widths). TV-01 ran natively at 4 × 1M: complete in 2.9 s, re-sort 270 ms, held paging p95 16.9 ms, peak private 504 MiB. |
| P3 v1 | **Done (engineering scope).** SMB, command line, viewer, search and result sets, compare-and-mark, read-only ZIP, quick view, associations, Alt+F8, themes, diagnostics, packaging. Also: truthful outcomes (fault-injection tests), stream and Mark-of-the-Web loss reporting, fuzzing, shutdown block, update check (notify only), automated TV-10 pass, ADR and validation records. |
| P1–P3 review loop | **Done (2026-09-27).** A: durable moves (flush + write-through before a source is deleted), tiered journal with fill records, direct small-file copies (100k × 4 KiB within the ≤25% budget), PI-05 metadata question, EFS/sparse, mount-point volumes, safe exit. C: bulk reads for whole-listing commands on spilled listings (see TV-01). B: history pins/clear, target-panel bookmarks, `.lnk` folders, guarded Space sizing, partial Ctrl+A and Ctrl+Shift+A, viewer list and go-to-line, Alt+F10 folder scan, Find within results, saved filters (`@name`), lossless F9 editing, "Run again…" with a durable source manifest. D: job routes, bounded drag-out, re-armed folder watches. |
| P4 | **In progress.** Local Registry navigation with explicit views, typed/raw read-only value inspection, and HKCU tests landed. Mutation, search, import/export, elevation broker, and hex editing remain. |
| P5–P10 | Pending |

## Resume here (next slices, in order)

1. **External P3 release gates.** These are manual and need infrastructure or people; see `docs/validation/P3-validations.md`.
   - SignPath signing (ADR-15).
   - Smart App Control on a clean machine (TV-13).
   - Narrator, NVDA, and JAWS runs, plus users of the three references (TV-10).
   - Recycle quota, UNC, and removable fixtures (TV-03).
   - ReFS/Dev Drive cloning and SMB server-side copy on real servers.
2. **P4a:** Finish Registry typed mutations/editors, search, import/export, notifications, and per-plan elevation broker (`FileCat.PrivilegedHost`).
3. **P4b:** fixed-length hex editing (patch overlay, undo, save strategies).
4. **Later phases:** P5 ZIP create/update and edit sessions; P6 SFTP; P7 diff, sync, and inspectors; post-v1 slices (bulk rename, links, manifests); P8–P10.

## Notes for the next session

- **Headless tests:** Avalonia's headless text layout spins on long wrapped text with blank lines. The native app renders it fine.
  Audit dialogs with short texts (see `AccessibilityTests`).
- **Commits:** check `dotnet test` exit codes before committing; grep output alone hides failures.
- **Benchmarks:** `FILECAT_COPY_BENCH=100000 FILECAT_COPY_BENCH_ROUNDS=2 dotnet test tests/FileCat.Platform.Windows.Tests --filter SmallFileCopyBenchmark --logger "console;verbosity=detailed"`.
  Rounds alternate the order. Other test runs or antivirus scans on the machine distort single runs by several times.
- **Shell edits:** in perl substitutions, never write `\|` in the pattern with `|` delimiters (it becomes alternation and matches empty text at the file start).
  The plan file uses CRLF line endings.
