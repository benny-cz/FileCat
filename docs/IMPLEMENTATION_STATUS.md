# FileCat implementation status

This is the resume point for implementing `docs/design/FILECAT_PRODUCT_ARCHITECTURE_AND_IMPLEMENTATION_PLAN.md`.
Work happens directly on `main`, and every chunk is committed and pushed. Keep this file compact.

## Build, test, run

```
dotnet build FileCat.slnx
dotnet test FileCat.slnx                              # Core 195, Windows integration 46, App headless 17 tests
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
| `src/FileCat.PrivilegedHost` | Per-plan administrator broker (installed builds only; ADR-14) |
| `src/FileCat.App` | Avalonia 12.1 UI: glyph-run `FileListControl`, panels, tabs, workspace, overlay dialogs, operation center, viewers, settings (column profiles, associations), themes, benchmark |
| `docs/adr/` | Decided ADRs: 02, 03, 04 (append-only journal instead of SQLite), 05, 07, 10, 14, 15, 16. The plan's §26 points to them. |
| `docs/validation/` | TV-01 (scale and latency) and the P3 validations (TV-03/07/10/13/14/16/17 plus truthful outcomes) |
| `docs/CAPABILITIES.md`, `docs/SERVICING.md`, `SECURITY.md` | What works where; release servicing; vulnerability reporting |

## Phase status

| Phase | Status |
|---|---|
| P1 walking slice | **Done** |
| P2 scalable workspace | **Done.** Tabs, multi-panel targets, bookmarks, workspaces, single instance, persistence, watchers, metadata columns, column profiles (Settings → Columns, persisted widths). TV-01 ran natively at 4 × 1M: complete in 2.9 s, re-sort 270 ms, held paging p95 16.9 ms, peak private 504 MiB. |
| P3 v1 | **Done (engineering scope).** SMB, command line, viewer, search and result sets, compare-and-mark, read-only ZIP, quick view, associations, Alt+F8, themes, diagnostics, packaging. Also: truthful outcomes (fault-injection tests), stream and Mark-of-the-Web loss reporting, fuzzing, shutdown block, update check (notify only), automated TV-10 pass, ADR and validation records. |
| P1–P3 review loop | **Done (2026-09-27).** A: durable moves (flush + write-through before a source is deleted), tiered journal with fill records, direct small-file copies (100k × 4 KiB within the ≤25% budget), PI-05 metadata question, EFS/sparse, mount-point volumes, safe exit. C: bulk reads for whole-listing commands on spilled listings (see TV-01). B: history pins/clear, target-panel bookmarks, `.lnk` folders, guarded Space sizing, partial Ctrl+A and Ctrl+Shift+A, viewer list and go-to-line, Alt+F10 folder scan, Find within results, saved filters (`@name`), lossless F9 editing, "Run again…" with a durable source manifest. D: job routes, bounded drag-out, re-armed folder watches. |
| P4 | **Done (engineering scope, 2026-09-28).** P4a: Registry views (explicit 32/64-bit), guarded jobs with undo, link-safe subtree delete, HKCR/HKCC writable route, search, `.reg` import/export, notifications, ACL inspection, and the per-plan administrator broker (`FileCat.PrivilegedHost`, ADR-14: "Retry as administrator" for access-denied items). P4b: fixed-length hex editor (ADR-05). TV-04/05/15 VM and hardware checks remain. |
| P5 | **Done (engineering scope, 2026-09-28).** ZIP pack (Alt+F5), add (F5), delete (F8), rename (F2), folder entries (F7), and Test by staged, verified rebuilds with parent-version checks (ADR-07); nested archives read-only; F4 edit sessions with explicit, guarded commit that survive restarts. TV-07 native-engine parts wait for P8. |
| Post-v1 slices | Bulk rename (Ctrl+M, OPS-007): masks, counters, regex, case, live preview blocking collisions, editor round-trip, swaps and chains through journaled temporary names (Operations can finish an interrupted rename), Undo guarded by identity. Create link (File menu): symbolic links (probed right, relative option), junctions, and hard links, checked per drive and target type before creation; Undo removes links that are unchanged (hard links only while provably another name of the file). Checksum manifests (§9.4): GNU, BSD-tagged, and SFV formats are recognized, never hashed automatically; verification is a read-only job with byte progress, per-file results, refused absolute and `..` paths, and failing files openable as a result set; the checksum dialog saves manifests. |
| P6–P10 | Pending |

## Resume here (next slices, in order)

1. **Post-v1 slices (§23.1, any order after P3):** per-item tool invocation (FAR Apply command) is the last one.
2. **P6:** SFTP provider (connection profiles, host-key trust incl. `known_hosts`, agent/key/password auth with OS secret storage), browse, transfer with safe resume and download marks, SFTP edit sessions reusing P5 sessions, explicit SSH terminal.
3. **External release gates:** P3 cases in `docs/validation/P3-validations.md`; P4 TV-04/05/15 VM and hardware checks remain pending after code and automated tests.
4. **Later:** P7 diff, sync, and inspectors; P8–P10.

## Notes for the next session

- **Headless tests:** Avalonia's headless text layout spins on long wrapped text with blank lines. The native app renders it fine.
  Audit dialogs with short texts (see `AccessibilityTests`).
- **Commits:** check `dotnet test` exit codes before committing; grep output alone hides failures.
- **Junctions in tests:** .NET's recursive `Directory.Delete` fails on junctions here (its `DeleteVolumeMountPoint` call returns "parameter is incorrect"); delete junctions non-recursively first. Product deletes never use the recursive API.
- **Benchmarks:** `FILECAT_COPY_BENCH=100000 FILECAT_COPY_BENCH_ROUNDS=2 dotnet test tests/FileCat.Platform.Windows.Tests --filter SmallFileCopyBenchmark --logger "console;verbosity=detailed"`.
  Rounds alternate the order. Other test runs or antivirus scans on the machine distort single runs by several times.
- **Shell edits:** make edits that contain a backslash-n escape with the Edit tool; heredoc scripts turn it into a real newline.
  In perl substitutions, never write `\|` in the pattern with `|` delimiters (it becomes alternation and matches empty text at the file start).
  The plan file uses CRLF line endings.
