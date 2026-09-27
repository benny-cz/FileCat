# P3 validations (TV-03, TV-07 ZIP, TV-10, TV-13, TV-14, TV-16, TV-17)

Status on 2026-09-27 on the development machine (Windows 11 Pro 26220, NTFS, .NET 10.0.12). "Automated" means a test in
`dotnet test FileCat.slnx` enforces it. "Manual" items need infrastructure or people and gate the signed v1 release.
TV-01 has its own record: [TV-01.md](TV-01.md).

| TV | Automated evidence | Result | Manual / pending |
|---|---|---|---|
| TV-03 recycle | `Recycle_reports_outcome_and_restore_uses_the_bin_item`, `Volume_profile_and_recycle_classification` (Windows), `Unrecyclable_items_are_left_alone_or_deleted_by_consent_with_positions` | Pass. The pre-delete abort guard prevents silent permanent deletion; each outcome is verified; restore uses the bin item's `undelete` verb; items the bin cannot take are asked about before the job. | Quota-full, UNC, removable, and long-name fixtures on dedicated media |
| TV-07 (read-only ZIP) | `Zip_browses_members…`, `Zip_extraction_refuses_escaping_names_and_propagates_origin`, `A_member_lying_about_its_size…`, `Corrupted_archives_fail_cleanly…` (300-round fuzz) | Pass. Traversal names are refused, duplicates stay distinct, Mark-of-the-Web propagates, output is bounded by the declared size plus the ratio limit, and corruption fails cleanly. | Large real-world corpus |
| TV-10 accessibility and keyboard | `AccessibilityTests` (headless): every interactive control in the main window, all Settings pages, and the copy/delete/mkdir/find/mask dialogs is named; the list peer announces focus; viewers are read-only document peers; the palette and menu are bound | Pass (automated part) | Narrator/NVDA/JAWS runs; DPI 100–200%; users of the three references; high contrast; IME; XWayland |
| TV-13 packaging | `eng/publish.ps1` builds all artifacts; CI packages on tags | ReadyToRun startup to first frame 836–1,148 ms warm | SignPath signing; Smart App Control on a clean machine; installed versus portable helper trust |
| TV-14 persistence and privacy | `State_store_recovers_from_corruption_and_refuses_newer_schema`, `Workspace_state_round_trips_locations`, journal recovery tests (torn lines, streaming, bounded headers) | Pass. Corrupt state falls back to defaults and is reported; state from a newer schema opens read-only; logs hash paths unless diagnostic mode is on; journals and diagnostics have bounded retention. | Crash while the journal writer runs, on real hardware |
| TV-16 Shell integration (P3 policy) | Design: icons come from `SHGetFileInfo` with `SHGFI_USEFILEATTRIBUTES` (by extension, with no file access), no thumbnails or property handlers, no Shell context menus in-process, and copy hooks disabled for recycle (`FOFX_NOCOPYHOOKS`) | Pass by construction: no handler code runs in the UI process | The out-of-process Shell host is post-v1 (P7) |
| TV-17 external tools | `Tool_launcher_refuses_batch_files_with_metacharacters`, `Tool_launcher_uses_absolute_paths_list_files_and_splits_long_selections`, `Command_line_quoting_is_shell_specific`, `Associations_parse_validate…` | Pass. Tools must be real executables; batch files are refused when arguments contain metacharacters; paths are absolute with `--`; list files are used past 32,767 characters; quoting is shell-specific. | Hostile-name corpus against real editors |

## Truthful outcomes (P3 exit)

`TruthfulOutcomeTests` injects the Win32 codes Windows reports:

- disk full;
- network name deleted;
- virus detected;
- device not ready;
- access denied;
- write-protected;
- cloud provider unavailable;
- sharing violation, which gets three quiet retries before the question.

In every case the job asks with a classified, plain explanation. It reports the item, leaves no staged file, completes the
other roots, and ends as CompletedWithIssues or Failed, never as a false Completed. Canceling at the question ends as
Canceled with completed work kept. A really locked file and a source deleted before its turn are reported truthfully.

## Small-file copy budget (plan §21: 100,000 × 4 KiB, ≤25% over CopyFile2, journaling included)

`SmallFileCopyBenchmark` (Windows integration tests) alternates the order of a plain CopyFile2 loop and a FileCat copy
job over the same fixture. On the development machine (NVMe, NTFS, Defender on): 100,000 files 10% and 21% (another
project's test run in parallel); 20,000 files 16% and −2%; 10,000 files median 5% over four rounds. Pass.

What keeps it there: new small files go straight to their name without a staged rename, and journal intents for new
names are group-committed. Each destination folder gets one durable fill record, and after a crash recovery compares
the files the job created there with their sources. Replacing an existing item stays staged, journaled synchronously,
and written through. A move also flushes each copy and writes its publish through before it deletes the source.
