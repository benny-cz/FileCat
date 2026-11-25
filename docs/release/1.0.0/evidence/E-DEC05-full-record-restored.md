# E-DEC05-RECORD — the full file-system record restored in File-system details (DEC-05)

**Prepared for rc.3 on branch `fix/dec05-full-file-system-record`, cut from `origin/rc3/integration-3` at `4512483f8` on 2026-11-25. Not merged, no pull request and no hosted CI, by instruction: the owner decides whether rc.3 restores the record or ships File-system details as metadata only. Component and native evidence only, not candidate qualification or release GO.** The owner has since decided, and the fix is merged: see [After the owner's decision](#after-the-owners-decision-2026-11-25).

## What was missing

- DEC-05 (owner, 2026-10-28) requires every advertised D-56 field before 1.0.0. I05 implemented them in `IFileRecords.Read` ([implementation](E-I05-D56-implementation.md), [field audit](E-I05-D56-field-audit.md)).
- `4364d1b37` (2026-11-17) rebuilt Ctrl+Alt+Enter as File-system details: a summary and a metadata-only full record. Since then `IFileRecords.Read` had no product caller, and the App had no view for its `InspectionReport`. 1.0.0 therefore showed none of the record's fields beyond the metadata tiers: no timestamp checks, change-journal entry or history, object ID, decoded reparse or Cloud Files state, EFS users and recovery agents, MFT record and attribute runs, `$LogFile`, `$Secure`, `$I30` slack, `$ObjId` and `$Reparse` entries, ext4 inode checksums, FAT entries or HFS+ journal.
- The command-search keywords "MFT, USN, timestomping, forensics, SDDL" had become untrue.

## What the user sees now

- File → Inspect → **File-system details…** (Ctrl+Alt+Enter; Properties on Linux and macOS) opens as before. The summary comes first, and **Read full record** still shows the unchanged metadata-only tier.
- The same window has a new action, **Read file-system record**, enabled once the first observation is shown. It reads the item's record with `IFileRecords.Read` and shows the `InspectionReport` as the record writes itself (`ToText`, the rendering of the route before `4364d1b37`):
  - the title and its warnings;
  - sections with aligned fields;
  - tables with their rule and "more" line;
  - preformatted lines and child sections;
  - the Windows non-administrator notice ("Needs administrator rights") and the Linux and macOS "needs root" lines.
- While the record is shown:
  - the window title, the report's accessible name and Save as's suggested file name say "File-system record";
  - the status line says "File-system record · read at … · N lines · F5 reads it again";
  - the action reads **Show summary** or **Show full record**, back to the tier shown before;
  - F5, Ctrl+R and Refresh read the record again, and only the record;
  - Find, Wrap and line navigation work as in every report;
  - the details actions (tier, streams tab, permissions, stored target, compression link) are hidden and return with the details;
  - a record read once is shown again by **Show file-system record** without another read.
- The spoken line reads each part in words: "Section: Timestamp checks", "Warning: …", "Created: …", "Table with 4 rows. Columns: ID, Type, Name, …", "Table rule.", "Row 1 of 4. ID: 0; Type: $STANDARD_INFORMATION; …", "Subsection: …". The text that is shown, found, copied and saved is unchanged. When the report's structure does not account for every line, every line is read as shown.
- The placeholders are "Reading the file-system record…" and "Waiting for another file-system record read to finish (FileCat reads one at a time)…". A failed read says "Record read failed: …" and keeps a previous record under that notice.
- Label: the request suggested "Full file-system record". The window already has **Read full record** for the metadata tier, so the new action is **Read file-system record**, the command's name before `4364d1b37`, and its help text says how the two differ.

## How it is read

- `FileDetailsReader.ReadRecordAsync` (Core) uses the window's existing details reader:
  - one running and one pending request per window, so a record request and a tier request retire each other;
  - closing the window disposes the reader;
  - publication retires at once, while a started native call keeps its permit until it returns, as for the tiers.
- One owned call reads `ReadSummary`, then `Read`, then `ReadSummary` again, so the record can be bound to one object.
- Admission is the item's device's details permit (four per device, eight per process) through `FileObservationRequests.RunOwned`. The call runs on a scheduler worker of its own queue (`<device> · file-system record`), never on the UI thread.
  - The device scheduler reports a drive "not responding" after one call runs 8 s, and Disk usage and analysis stop on such a drive.
  - An administrator read can legitimately take longer: a large change journal, and `$LogFile` catch-up waits of up to 6 s. On its own queue it is never taken for the drive hanging.
- **Working-memory reservation:** FileCat reads one record at a time, process-wide. As administrator the raw readers hold up to 512 MiB of `$LogFile` with a run-sized copy, 64 MiB per `$Extend` view index and 16 MiB of a folder's `$I30`, and scan up to 2 GiB of the change journal. A request that must wait holds no device permit, and its window says that it waits.
- **macOS:** `Read` runs with dataless-file downloads disabled on its thread (`MacMaterializationPolicy`), as the details tiers do.
- **Windows:** the reader is unchanged. It already exposes Cloud Files placeholders on its thread and omits supplementary values for cloud and offline items.
- The assembled text has the details window's 2 MiB cap and its "Further detail omitted" line.

## Copy and Save

- The same buttons, keys and output route as the details window:
  - Copy and Save capture the shown revision;
  - a refresh, a view switch or closing cancels a pending Copy or Save before publication;
  - on Windows, Save passes the existing destination guard (`WindowsMetadataReportOutput`) and writes UTF-8 with a BOM;
  - on Linux and macOS, Save writes through the details window's branch.
- `FileRecordObservation.ContentKind` is **Payload**. The record can hold stream, EA and xattr value excerpts and, read from the volume as administrator or root, resident attribute data, record slack, `$LogFile` images and raw inode bytes.
- `FileRecordObservation.OutputRefusal` decides publication:
  - **Bound to one object:** the identity read before and after the record is known and equal.
  - **Windows also needs no EFS:** not EFS-encrypted before and after, or on a volume without EFS, where the observation is Unsupported (FAT, exFAT, ReFS). The legacy reader reads stream and EA excerpts through Windows, which decrypts EFS content.
  - Otherwise Copy and Save are disabled, the record's first line says "Copy and Save are unavailable for this record: …", and a Copy or Save key says it again.
  - The summary and the metadata full record stay publishable.
- A record whose object differs from the details' earlier observation gets a "Changed object" first line. It stays publishable, because the record is consistent in itself.

## Tests

`FileCat.App.Tests.FileSystemRecordViewTests` (9 tests):
- **Read on request, off the UI thread, every part shown and spoken.** This test goes through the `MainViewModel.FileRecordsForTests` seam with a fake `IFileRecords` and `ShowFileRecordForPath`. It checks that:
  - opening, a refresh and both metadata tiers never call `Read`;
  - the action's help text names administrator, root and **Read full record**;
  - one click makes one `Read` on a scheduler device worker under admission, never on the UI thread;
  - the window contains exactly the fixture's `ToText`;
  - the accessible name, title and status say "File-system record";
  - the title, warning, notice section and line, a field, the table header, both rows, the "more" line, a subsection and its field are spoken in words, and no line speaks box-drawing characters;
  - **Show summary** returns the details names, a cached record needs no new read, and F5 reads only the record, with one summary before and one after.
- **The real record:** `WindowsFileRecords` or `UnixFileRecords` on a temporary file, with a named stream on NTFS. The test checks:
  - timestamp checks are present;
  - the stream row is present with its value, or with the value reader's "not read:" reason;
  - "Needs administrator rights" at medium integrity, or "MFT record" when elevated;
  - `SpokenReport` accounts for every line of the real record;
  - the "Timestamp checks" heading and the stream row are spoken under their column names;
  - Copy all publishes the shown text.
- **A second window waits for the one record read:** its text and status say so, Copy is disabled, and both records are read in turn.
- **Leaving or closing during a held read** (two cases): the native call's token is cancelled and nothing is published. The started read keeps the fourth device slot until it returns, and the slot is released exactly once.
- **Copy and Save** (four cases):
  - bound: Copy all and Save publish the exact text, with a BOM;
  - replaced (identity changed during the read): refused on every platform;
  - EFS encrypted, or its state unknown: refused on Windows, published on Linux and macOS;
  - a refusal leaves the clipboard untouched and opens no picker, and the summary stays publishable.

`FileCat.Core.Tests.FileRecordReadTests` (8 tests):
- the summary, record, summary order on a device worker under admission;
- the output rule's matrix;
- cancellation retires publication at once, while the permit and the reservation stay until the native call returns;
- the waiting callback fires, and a summary request retires a waiting record request;
- a long record read is not taken for its device hanging (the control: its own queue is);
- record and tier requests retire each other;
- an unsupported reader fails and returns the permit and the reservation;
- macOS: the materialization policy is Off during `Read`;
- the real Unix record is bound and publishable; on ext4 its inode section has fields as root and says "which needs root." otherwise.

**Failing before, passing after:** with only the new App test file added to `4512483f8`, its 7 tests at that time all failed at medium integrity: `Assert.Single` found no **Read file-system record** action. With the change they pass, and so do the two tests added afterwards.

## Runs

| Machine | Rights | Tests | Result |
|---|---|---|---|
| Windows 11 Pro 26320 x64 host | medium (explorer.exe, hidden script) | `FileSystemRecordViewTests` at `4512483f8` plus the test file | 7 of 7 failed, as expected |
| Same | medium | `FileSystemRecordViewTests`, final | 9 passed |
| Same | medium | 26 App classes that use `ReportWindow` or the details route, before the final queue and waiting change | 428, 0 failed, 81 skipped |
| Same | medium | All of `FileCat.App.Tests`, final | Started at 04:03 and still running when this record was committed (no failure among the tests reported by then); its result is not recorded here |
| Same | elevated (High) | 21 Core record classes (records, details, parsers, Unix readers) | 302, 0 failed, 56 skipped (Linux and macOS cases) |
| Same | elevated | The real-reader and Copy and Save App tests (5), with TEMP on C: and on E: | 5 of 5 each |
| Same | elevated | `WindowsFileRecordsTests` (reader unchanged) | TEMP on C:: 32, 2 failed, 13 skipped. TEMP on E:: 32, 1 failed, 13 skipped. Each failure passes on the other volume (see below) |
| Mac M1, macOS 27.2.1 | user (no passwordless sudo) | Core record classes; `FileSystemRecordViewTests` | 302, 0 failed, 42 skipped; 9 passed |
| Ubuntu 24.04.5 VM, kernel 7.0 | user | Core record classes (twice); `FileSystemRecordViewTests` | 302, 0 failed, 42 skipped, both times; 9 passed |
| Same | root (`sudo -n`), FAT32 and ext4 loop volumes as CI makes them | Core record classes | 302, 0 failed, 41 skipped |

The elevated window test on E: showed the administrator record through the window:
- `$LogFile`: a 64 MiB log and this item's 10 operations;
- its `$Secure` descriptor, whose hash matches;
- the MFT record with its attributes and resident content;
- the `$ObjId` and `$Reparse` comparisons;
- the item's change-journal history.

Host volumes, unchanged by this branch:
- C: refuses this host's raw cluster reads and `FindFirstFileNameW` ("The request is not supported"). There the record says that `$LogFile`, `$Secure`, `$Reparse` and `$ObjId`'s blocks could not be read, and two `WindowsFileRecordsTests` fail.
- E: answers `FSCTL_GET_EXTERNAL_BACKING` with "Incorrect function", which the supplementary value reader treats as unqualified. There the record says "not read: External-backing state is unavailable…" for a stream value, and one `WindowsFileRecordsTests` fails.

The first full Ubuntu run had one failure: the long-read test's neighbouring summary waited more than 10 s for the process-wide details admission, which other test classes running in parallel held. The test now checks the device's queue through the scheduler directly, and waits up to 30 s wherever it passes the shared admission. It passed alone three times and in two full runs.

The Ubuntu loop volumes, mount points and images were removed after the root run.

## Documents

- `docs/CAPABILITIES.md` (File-system details row), `README.md` (Data beneath the file), `docs/IMPLEMENTATION_STATUS.md` (D-56 paragraph) and the command's description now say what the record shows on each platform, what an administrator window or root adds, and when Copy and Save take it.
- The command-search keywords "MFT, USN, change journal, timestomping, inode, forensics, file ID, hard links, clusters, fragments, SDDL" stay: they are true again.

## Not covered or not verified

- **Screen readers:** no NVDA, Orca or VoiceOver run of the record view; its spoken forms are verified headless only. The details tiers, which the existing N-UX receipts cover, are unchanged.
- **Hosted CI:** not run (no pull request, by instruction).
- **Final build:** after the runs above, one line of `ReportWindow.Record.cs` changed (the record's first lines are flattened to one line each, so a multi-line failure message cannot shift the spoken forms), and three local variables in `FileRecordReadTests` were renamed. Neither change was rebuilt and rerun before this record was committed, and the full App suite above was still running.
- **Real administrator window and root FileCat:** not driven through the real UI. The administrator record was shown through the window by headless tests in an elevated process; root was exercised through the Core route on Ubuntu.
- **Mac as root** (no passwordless sudo there): FAT and exFAT entries and the HFS+ journal were not read through the record route as root.
- **macOS dataless File Provider items:** not read through the record route; only the thread policy is checked.
- **exFAT as root on Ubuntu:** not run; the VM has no `mkfs.exfat`.
- **FAT32 and exFAT on Windows:** not checked whether `FileIdInfo` gives an identity (no FAT volume attached). If it does not, the record is shown there but Copy and Save are refused as unbound.
- **Not run through the record route in this pass:** a Windows non-administrator account, a real EFS-encrypted file (`FILECAT_TEST_EFS`) and Cloud Files placeholders. The reader's own tests cover placeholders (I05).
- **Memory:** no measurement of an administrator record read. The bound is the legacy reader's own caps plus one record read at a time.
- **Binding window:** the legacy reader reopens streams by path for their excerpts. An object swapped in and back between the two identity reads would not be detected, because the identities before and after match.

## Verification closed (2026-11-25, release session)

- The full `FileCat.App.Tests` run at medium integrity, started 04:03 on the build before the last one-line edit, ended at 04:37: 4,179 tests, 0 failed, 332 skipped.
- After rebuilding with the last two edits (the one line in `ReportWindow.Record.cs` and the renamed locals in `FileRecordReadTests`): `FileRecordReadTests` 8, 0 failed, 2 skipped (Unix or root only); the 26 App classes that use `ReportWindow`, at medium integrity, 429, 0 failed, 81 skipped.

## After the owner's decision (2026-11-25)

- **Decision:** asked whether rc.3 restores the record or ships File-system details as metadata only, the owner answered "Yes, restore it". The register records the gap as [I671](../FILECAT_1_0_RELEASE_ISSUES.md#i671).
- **Pull request and CI:** [PR #55](https://github.com/benny-cz/FileCat/pull/55) (`d2f98f384`) merged into `rc3/integration-3` as `9956448b8`. Integration run 45570809782 passed all 19 jobs that ran on it, which closes "Hosted CI: not run" above. Integration PR #44 merged it into main as `a2f983e9a`.
- **Through the real window** (batch43, in the Windows 11 qualification VM on a local build of `9956448b8`; the OneDrive guard saw no change):
  - at medium integrity: **Read file-system record** was enabled once the details showed; the record (75 lines) said that the raw on-disk records need administrator rights; the title, the view's name and the status line said "File-system record"; the spoken line read 8 section headings and 3 table lines in words, with no box-drawing characters; Copy all published 3,111 characters with the timestamp checks and the named stream; F5 read it again; **Show summary** returned to the details and **Show file-system record** showed the same record without another read.
  - in an administrator window: the same, with 172 lines, its MFT record and the change-journal history, and no administrator notice (9,465 characters copied). This closes "Real administrator window" above for Windows; a root FileCat through the UI remains unrun.
- **Still open:** a screen reader's reading of the record view; the installed rc.3 check (batch43 and batch43-high in the Windows qualification run).
