# FileCat implementation status

This is the resume point for implementing `docs/design/FILECAT_PRODUCT_ARCHITECTURE_AND_IMPLEMENTATION_PLAN.md`.
Work happens directly on `main`, and every chunk is committed and pushed. Keep this file compact.

## Build, test, run

```
dotnet build FileCat.slnx
dotnet test FileCat.slnx                              # Core 360, Windows integration 75 (9 need a phone, 5 a USB stick), App headless 52, Remote 42 tests
eng/package-linux.sh VERSION linux-x64                # Linux .tar.gz, .deb, AppImage (on Linux); eng/package-macos.sh VERSION on macOS
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
| `src/FileCat.Recovery` | Read-only NTFS, FAT12/16/32, and exFAT engines, MBR/GPT, evidence-based states, `RecoveryProvider` (P10, ADR-08) |
| `src/FileCat.Remote` | SFTP over SSH.NET: channel, host-key trust, connection leases, provider, jobs (P6) |
| `src/FileCat.App` | Avalonia 12.1 UI: glyph-run `FileListControl`, panels, tabs, workspace, overlay dialogs, operation center, viewers, settings (column profiles, associations), themes, benchmark |
| `docs/adr/` | All 17 ADRs decided (04: append-only journal instead of SQLite). The plan's §26 points to them. |
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
| P5 | **Done (engineering scope, 2026-09-28).** ZIP pack (Alt+F5), add (F5), delete (F8), rename (F2), folder entries (F7), and Test by staged, verified rebuilds with parent-version checks (ADR-07); nested archives read-only; F4 edit sessions with explicit, guarded commit that survive restarts. Measured (`docs/validation/P5-P8-archives.md`): extraction 1.2–2.4× the in-box extractors with no scratch, members over 32 MiB decompressed as read (first page in milliseconds), ZIP CRCs verified. |
| Post-v1 slices | Bulk rename (Ctrl+M, OPS-007): masks, counters, regex, case, live preview blocking collisions, editor round-trip, swaps and chains through journaled temporary names (Operations can finish an interrupted rename), Undo guarded by identity. Create link (File menu): symbolic links (probed right, relative option), junctions, and hard links, checked per drive and target type before creation; Undo removes links that are unchanged (hard links only while provably another name of the file). Checksum manifests (§9.4): GNU, BSD-tagged, and SFV formats are recognized, never hashed automatically; verification is a read-only job with byte progress, per-file results, refused absolute and `..` paths, and failing files openable as a result set; the checksum dialog saves manifests. Apply command (Ctrl+G, FAR): one command per item with placeholders, split into tokens before substitution, previewed exactly, refusing option-like names, BatBadBut, and shells as programs (shell mode quotes names instead); a sequential job records each exit code with the output's tail. Dialogs confirm only a preview of the current input. |
| P6 | **Done (engineering scope, 2026-09-28).** SFTP over SSH.NET (ADR-17): lstat listings, remote changes only through fresh listing entries (SSH.NET's path operations follow links), host keys in FileCat's own known_hosts seeded by OpenSSH's, saved connections with secrets in Windows Credential Manager, connect UI, F3, downloads with origin marks, uploads through temporary names, moves, delete, rename, new folder, F4 edit sessions with guarded commit, SSH terminal, SSH agent sign-in (own agent protocol, no package; RSA only with SHA-2), resume where safe (downloads from any seekable source, SFTP/FTP uploads; partial content checked first). Measured (`docs/validation/P6-P8-remote.md`): small files 1.5–3× the bare connection, large files near its rate. Pending: TV-12 with varied servers. |
| P7 | **Done (engineering scope, 2026-09-28).** Compare files (text side by side with within-line changes, exact binary ranges; TV-08 measured in `docs/validation/TV-08.md` with search: 1,000,000-line diffs about 1 s, unrelated inputs labelled approximate, cancellation within milliseconds). Recursive comparison (Ctrl+F10 → Include subfolders): preview, result sets, and one-way synchronization (approved scope: Update and Mirror, every step previewed and excludable, letter-case collisions and unsafe names excluded, ordinary jobs; no two-way sync, no stored state; targets on disk only). Viewer Info mode (Ctrl+I): PE and image inspectors; Picture mode for images, decoded by FileCat's own worker process (sandboxed on Windows). Persistent working sets (Ctrl+Shift+W or F5 toward a set; the list's F7/F2/F8 act on sets only). Shell integration host (ADR-06, TV-16): quick view thumbnails and programs' own icons from a low-integrity helper in a job object. |
| P8 | **Done (engineering scope, 2026-09-28).** Approved 2026-09-28: read-only archives in `FileCat.Archives` (ADR-07): TAR family (in-box readers; gzip, bzip2, xz, zstd), 7z and RAR (SharpCompress 0.50.4), single compressed files, ISO/UDF (DiscUtils 1.0.89); forward cursors for compressed and solid archives, nesting with ZIP both ways, open by signature, Unpack; engine errors become damage reports (fuzzed). Inspectors (Ctrl+I): ELF, Mach-O (universal too), Java class, APK/AAB (binary and protobuf manifests), MP4/MOV, Matroska/WebM, MP3, FLAC, WAV, AVI, Ogg, HTML; fuzzed. FTP/FTPS (FluentFTP 55.0.0, ADR-17 addendum): the same remote channel, jobs, and edit sessions as SFTP; FTPS certificates OS-validated or explicitly pinned (changed ones refused by default); unencrypted FTP only by explicit choice; tested against pyftpdlib. MTP (Windows Portable Devices, own COM interop): devices, storages, and folders by name path; F3, F5 both ways, F2, F7, F8 as jobs; verified on hardware (motorola edge 60 pro: writes inside a FileCat-test folder; iPhone: reads). |
| P9 | **Engineering scope done (2026-09-28); release gates pending.** Approved packages: Linux `.tar.gz` (+ menu entry script), `.deb`, AppImage (appimagetool pinned by checksum); macOS arm64 `.app` zip, ad-hoc signed. CI builds, installs, and starts each on tags and manual runs. Linux and macOS: saved passwords in the keychain or the desktop keyring (Secret Service via libsecret; tested against GNOME Keyring in CI), permissions/owner/group columns and editing (chmod `X` inside folders, folders last, links untouched), copies that keep permissions and are never wider while written, the freedesktop trash on each item's own volume (private folders, no links, reserved names), download origins in xattrs, terminals, keep-awake, ⌘ keys. Fixed on the way: filtered copies that stalled when a folder was listed before a matching file (ext4 order). Pending: TV-10 on real desktops (Orca, VoiceOver, IME, file clipboard and drag with Nautilus and Finder), TV-13 clean installs; Developer ID signing and notarization are not approved; Windows ARM64 later. |
| P10 | **Done (engineering scope, 2026-09-28).** Own NTFS, FAT12/16/32, exFAT engines with MBR/GPT (ADR-08): Recoverable / Uncertain / Partly lost / Overwritten / Name only with evidence (FAT32 starts Windows half-erased are placed by their neighbors, "."/".." entries, and content signatures, or stated as guesses; on request, free space is searched for deleted folders' listings nothing points to, with progress in the panel), declared lost bytes in F3 and F5 (`IPartialContent`), Commands → Find deleted files on an image or a drive in This PC, fixed columns, Ctrl+R rescans. Drives: a `ReadDevice` plan of the ADR-14 helper (one device, private pipe for the requesting process, bounded sector reads, no writes, no elevated parsing), destinations refused on the same physical disk, live-drive warnings, drive tabs not restored. Fixtures from real drivers (`eng/make-recovery-fixtures.sh`), byte-level truthfulness tests, 21,000-image fuzzing, pipe protocol tests; TV-09 overhead about 2×; 512 MiB NTFS and FAT32 images with 10,100 deleted files scan in under 0.1 s; a real USB stick: 1,030 deleted files, 296 signed programs recovered bit-exact (`docs/validation/P10-recovery.md`). Manual: the elevated read on real drives in the installed build. |

## Resume here (next slices, in order)

1. **Plan complete in engineering scope (P1–P10), all ADRs decided; the phase exit measurements are recorded** in `docs/validation/` (TV-01, P3, P5–P8 archives, P6–P8 remote, TV-08 comparison/search/Registry, P10 recovery), each with an opt-in benchmark that asserts its budgets. Beyond the plan, if wanted later: searching whole disks for deleted partitions, drive reading on Linux/macOS (the plan gives them image workflows), aligned binary comparison, Windows ARM64 packages (PLATFORM-002).
2. **External release gates:** P3 cases in `docs/validation/P3-validations.md`; P4 TV-04/05/15, P6 TV-12, P9 TV-10/13, and P10 (the elevated read on real drives) remain pending after code and automated tests.

## Notes for the next session

- **Headless tests:** Avalonia's headless text layout spins on long wrapped text with blank lines. The native app renders it fine.
  Audit dialogs with short texts (see `AccessibilityTests`).
- **Commits:** check `dotnet test` exit codes before committing; grep output alone hides failures.
- **Real-server SFTP tests** start a user-mode sshd (Linux/macOS CI). Locally in WSL: extract openssh-server and libwrap0 debs, then run with `FILECAT_SSHD` and `LD_LIBRARY_PATH` set.
- **Stopping stuck test hosts:** only processes under `E:\FileCat` (other sessions run tests on this machine). A dialog with blank lines hangs headless layout: `dotnet-stack report -p <pid>` shows it.
- **Junctions in tests:** .NET's recursive `Directory.Delete` fails on junctions here (its `DeleteVolumeMountPoint` call returns "parameter is incorrect"); delete junctions non-recursively first. Product deletes never use the recursive API.
- **Benchmarks:** `FILECAT_COPY_BENCH=100000 FILECAT_COPY_BENCH_ROUNDS=2 dotnet test tests/FileCat.Platform.Windows.Tests --filter SmallFileCopyBenchmark --logger "console;verbosity=detailed"`.
  Rounds alternate the order. Other test runs or antivirus scans on the machine distort single runs by several times.
  The others are opt-in the same way: `FILECAT_ARCHIVE_BENCH`, `FILECAT_COMPARE_BENCH`, `FILECAT_SEARCH_BENCH`,
  `FILECAT_REGISTRY_BENCH`, `FILECAT_REMOTE_BENCH` (+ `FILECAT_PYTHON`), `FILECAT_RECOVERY_BENCH`, `FILECAT_MTP_BENCH`.
- **Profiling a test:** xUnit v3 runs tests in `<project>.exe`, not `testhost.exe`: sample that PID with `dotnet-stack report`.
  Per-item costs found so far: durable journal writes per file, '~' in paths (8.3 expansion), per-item buffers, and
  folder listings repeated per item.
- **Shell edits:** make edits that contain a backslash-n escape with the Edit tool; heredoc scripts turn it into a real newline.
  In perl substitutions, never write `\|` in the pattern with `|` delimiters (it becomes alternation and matches empty text at the file start).
  The plan file uses CRLF line endings.
