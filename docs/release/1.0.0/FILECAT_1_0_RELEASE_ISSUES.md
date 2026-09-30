# FileCat 1.0.0 — release issue register

Authority: [release plan](../../design/FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md) §7 (initial register I01–I18) and §11.1
(issue lifecycle). Severity and release disposition are independent. "Remediated" means the change landed; an issue is
**Closed** only when the evidence named in its record supports closure. Evidence IDs point into the
[evidence index](FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md). Details of unfixed security-relevant concerns are kept at the
level the plan already states; exploit-level detail is not recorded here.

## Summary

| ID | Title | Severity | Disposition | Status |
|---|---|---|---|---|
| I01 | Advertised private security-reporting route is disabled | High (operational) | Blocker | Open |
| I02 | Stable signing unavailable | High | Blocker | Open — owner/provider action |
| I03 | Incomplete dependency and artifact provenance | High | Blocker (audit) | Open; new detail below |
| I04 | Platform/package claim mismatches | High (where a clean install fails) | Blocker for the affected claim | Open; new detail below |
| I05 | Media/record claim reconciliation | Medium | Contract gate | Open |
| I06 | Aggregate content-cache accounting | Potential High | Validation gate | Open |
| I07 | Performance targets not proved | Medium–High | Performance gate | Open |
| I08 | Containment documentation versus reality | Potential High/Critical | Security gate | Open |
| I09 | Recovery whole-source safety | Potential Critical | Safety gate | Open |
| I10 | Documentation drift | Medium | Blocker where safety/support claims mislead | Open |
| I11 | Missing mandatory external evidence | Qualification blocker | Blocker | Open — resources |
| I12 | Historical regressions need durable coverage | Medium | Non-blocker once covered | Open |
| I13 | Latest features lack interaction evidence | Potential Medium–High | Gates open | Open |
| I14 | RAR decoder provenance / OSI-only eligibility | High | Blocker (license/signing) | Open |
| I15 | Uninstaller removed the whole installation folder | **Critical** (data loss) | Blocker | **Remediated `5b061cc`; verified in a VM** — closure pending re-audit and the final setup |
| I16 | Automatic browse/launch boundaries | Potential High | Security gate | Open |
| I17 | Broker consent/loader/pipe completeness | Potential High/Critical | Security gate | **Consent display: remediated `33b7de2` + `5c54181`, verified in a VM.** Loader, pipe, requester, cancellation: open |
| I18 | Release control and pipeline provenance | High | Blocker (integrity) | Open; new detail below |
| I19 | Interrupted-copy cleanup deleted complete or user-changed files | **High** (data loss) | Blocker (non-waivable class) | **Remediated `f87ad32`; verified** — closure pending re-audit |
| I20 | `$LogFile` attributed an earlier item's operations to the current file | Medium (false forensic finding) | Must fix (confirmed D-56 surface; destabilized the required CI lane) | **Remediated `45efc09`; verified** — closure pending re-audit |
| I21 | The Registry's 32-bit and 64-bit views of HKLM, HKU and HKCC failed without administrator rights | Medium (confirmed feature broken in the default, unelevated mode) | Must fix | **Remediated `47c27b9`; verified** — closure pending re-audit |
| I22 | Replacing a file that is open failed on Windows with a misleading "Access denied"; a closed comparison kept its files open | Medium | Must fix (confirmed copy/sync surface; destabilized two required lanes) | **Remediated `63d5fc4`; verified** — closure pending re-audit |
| I23 | Network discovery listed a device by its address when its name arrived late | Low (name missing; device listed) | Must fix (confirmed feature; nondeterministic required test) | **Remediated `d40e510`; verified** — closure pending re-audit |
| I24 | The panels' Modified column shows no seconds by default | Low (UI) | Fix before release if time allows; owner-reported | **Remediated `2197074`** (seconds by default; screenshot checked) |
| I25 | Markdown files open as plain text; they should be shown rendered | Low (viewer) | Owner-reported improvement | **Queued** |
| I26 | Progress at 100% while an operation still works, and a time left that was not honest | Medium (confirmed: 100% for 63% of a verified copy) | Must fix; owner-reported | **Remediated `d40fda0`; verified** — closure pending re-audit |
| I27 | Linux: under the Adwaita 41 icon theme FileCat finds no file-type icons | Low (cosmetic; built-in icons shown) | Fix if time allows | **Queued** |
| I28 | A damaged NTFS size or data run made the whole volume unreadable to recovery; a damaged root record made the scan throw | Medium (recovery completeness; potential hang; a scan that throws) | Must fix (§17.3 robustness) | **Remediated `98fb594` + `bb977d0`; verified; fuzz campaign running** |
| I29 | A shell picture asked for while the helper already worked on it was asked again (CI red on ARM64) | Low (duplicate work; nondeterministic required test) | Must fix | **Remediated `7175a41`; verified; CI green** |
| I30 | Running operations should show what happens in the best possible way | Medium (UX of data-moving operations) | Owner priority: middle | **In progress** (builds on I26) |

## Records of issues worked in this campaign

### I19 — Interrupted-copy cleanup deleted complete or user-changed files, and missed real partial copies

- **Discovered:** static audit of DPI P03 (plan §8.3), 2026-09-30, source `4f6b062`.
- **Requirement / invariant:** OPS-003, OPS-006, PI-05 (destructive scope visible before the step), PI-07 (truthful
  outcomes), AI-11; plan V03-PARTIAL ("a heuristic match is not ownership").
- **Platforms:** all (portable job engine and UI).
- **Mechanism (verified in source):** `JournalRecovery.FindIncompleteCopies` offered every file in a direct-copy
  destination folder that was created after the job started and differed in size or time from a namesake in the
  *first* source folder recorded for that destination (`TransferExecutor._fillDirs` was keyed by destination only).
  `OperationsView.OnCleanupInterrupted` listed at most 10 names and deleted every match permanently (`File.Delete`);
  `MainViewModel.RunInterruptedAgainAsync` named none ("N partial files … will be deleted") and did the same. Nothing
  was re-checked between the review and the deletion.
- **Actual behavior (reproduced, E-I19-R1):** four new Core tests failed on the baseline — (1) a complete copy from a
  second source folder was offered for deletion because an unrelated namesake exists in the first folder; (2) a copy
  cut short from a second source folder was *not* found, so Run again would skip the truncated file as already
  arrived; (3) a copy the user edited after the crash was offered for deletion; (4) a complete copy whose source
  changed since was offered for deletion. End to end through the real window, Run again on the baseline showed
  "First, 2 partial files left by the interruption will be deleted." and afterwards the user's edited file read
  "as copied": **the edit was permanently deleted**.
- **Expected:** only files provably left incomplete by the interruption are deleted, each is named before deletion,
  and anything else is left in place and reported.
- **Severity / disposition:** High (permanent loss of user data, bounded to post-interruption scenarios); release
  blocker — non-waivable class "destructive operation affecting unintended resources".
- **Remediation (`f87ad32`):** a fill record per (source folder, destination folder) pair; a file counts as cut short
  only when it is shorter than a same-named file in a recorded source folder and byte-identical to that file's
  beginning (so deleting it loses nothing the source lacks); files differing otherwise are listed and never deleted;
  every file to be deleted is named; `DeleteIncompleteCopies` re-checks size, times, link status and bytes immediately
  before each deletion; journals naming more than 10,000 fill folders say that not everything was checked.
- **Tests added:** `InterruptedCopyRecoveryTests` (7 cases, real copy jobs with a crashed journal: second source folder,
  partial copy from any folder, user edit after the crash, source changed since, bytes complete but time not set,
  change between review and deletion, a name replaced by a link) and `InterruptedOperationUiTests` (Run again through
  the real main window and dialog).
- **Evidence invalidated:** job-engine transfer evidence that relied on fill records (direct small-file copy path) and
  the V03 interrupted-copy cases; copy throughput for copies that flatten many source folders into one destination
  (one durable journal write per distinct source folder instead of per destination). Tree copies and single-folder
  copies write the same number of fill records as before.
- **Revalidation performed:** targeted (the new tests fail on `4f6b062` and pass on `f87ad32`), affected regression
  (full local suite, E-I19-V1), CI run 36754000317 (Windows x64, Ubuntu, macOS green; Windows ARM64 failed on I20,
  unrelated), CI run 36756346845 on `45efc09` (all four lanes green).
- **Remaining before closure:** independent re-audit of the change (plan §11.1 prefers a reviewer other than the fix
  author); V03-KILL real process-kill cases for the direct-copy path on the final candidate; small-file copy
  benchmark rerun on the performance machine.

### I20 — `$LogFile` section attributed an earlier item's operations to the current file

- **Discovered:** CI run 36754000317, job 110019476573 (Windows ARM64): `WindowsFileRecordsTests.As_administrator_the_MFT_record_shows_a_creation_time_set_afterwards`
  failed ("Timestamp checks: 1 sign" instead of 2). The test had passed on `4f6b062` (A01). Not caused by the I19 change.
- **Reproduction (E-I20-R1):** locally on the physical machine, 3 of 15 runs of that test failed; in each failure the
  report's `$LogFile` table held the history of the *previous* item that used the same MFT record (its
  "DeallocateFileRecordSegment — the record was freed (the item deleted)" and its "Created 2019-05-01" change), while
  the current file's own creation was absent.
- **Mechanism (verified in source):** the item's MFT record is read through `FSCTL_GET_NTFS_FILE_RECORD` (the cache),
  `$LogFile` raw from disk, which NTFS writes a moment later. The item's history was taken to start at the newest
  `InitializeFileRecordSegment` on the record regardless of whom it was made for; when the item's own creation was not
  on disk yet, everything since the earlier item's creation was listed as the current item's, and the earlier item's
  "time set back" became a timestamp finding for the current item.
- **Requirement:** D-56 (confirmed), PI-07; plan V14 ("timestamp discrepancies are evidence with limitations, not
  conclusive accusations"). A false "a program set it" finding about the wrong file is a correctness defect in a
  shipped forensic report.
- **Severity / disposition:** Medium (misleading forensic output; no data change). Must be fixed: confirmed feature
  surface, and it made a required CI lane nondeterministic.
- **Remediation (`45efc09`):** `NtfsLog.OwnHistoryStart` — the item's history starts at the creation whose new record
  header carries the item's own sequence number; without it, operations count as the item's only when the log shows no
  reuse and reaches the record's own latest change; otherwise none do and the report says why. The reader re-reads the
  on-disk log up to three times, two seconds apart, while it has not reached the record's latest change.
- **Tests added:** four `NtfsLogTests` cases for the attribution rule (including the exact failure scenario).
- **Revalidation:** the formerly flaky test passed 15 of 15 locally (E-I20-V1); full local suite green; CI run
  36756346845 green on all four lanes including Windows ARM64.
- **Remaining before closure:** re-audit; V14 independent-oracle checks of `$LogFile` output on the final candidate.

### I15 — Uninstaller removed the whole installation folder

- **Discovered:** plan static concern DPI P15; confirmed in `eng/installer/FileCat.iss` at `4f6b062`:
  `[UninstallDelete] Type: filesandordirs; Name: "{app}"`. Inno Setup documents that `filesandordirs` deletes matching
  directories "including all files and subdirectories in them". The folder page accepts a typed path, so an existing
  folder (for example `C:\Tools`) can be the installation folder.
- **Requirement:** OPS-006 spirit, PI-05, plan V19-UNINSTALL ("unrelated files in a selected nonempty folder must
  survive"); non-waivable class (silent data loss).
- **Severity / disposition:** Critical where it happens (unrelated user files deleted by an uninstall); blocker.
- **Remediation (`5b061cc`):** the section is removed. The uninstaller's own record removes the files the installer
  placed and then the folder once empty. Static basis: installed FileCat writes nothing into its installation folder
  (`AppPaths.Resolve` keeps state in the user profile).
- **Runtime verification (E-I15-V1):** on a snapshotted Windows 11 VM, the baseline and fixed installers built from the
  same payload with Inno Setup 6.7.1 were installed into a folder holding user files and uninstalled: the baseline's
  uninstall deleted the user's files, the fixed one kept them and still removed its own folder when that held nothing
  else; the fixed installer in its default folder, with FileCat started once, left nothing behind. (The Windows Sandbox
  attempt failed for environmental reasons, E-ENV-01.)
- **Remaining before closure:** re-audit; V19-UNINSTALL on the final signed setup (FQ).

### I17 — Administrator helper consent hid steps after the sixtieth and called any HKU hive the user's own

- **Discovered:** static audit of B04 (plan §8.2, V06-CONSENT), source unchanged since `4f6b062` (E-I17-S1).
- **Requirement / invariant:** ADR-14 and AI-13 (the helper shows exactly which steps it will run; the displayed plan
  is the consent boundary because the requester check proves only that some installed FileCat of the same user runs),
  PI-05.
- **Mechanism (verified in source):** the consent text listed the first 60 steps and summed up the rest as "N more
  steps of the same plan", while validation accepts up to 10,000 steps and the helper runs all of them; every `HKU\…`
  Registry change was described as "in the requesting user's own Registry", including LocalSystem's, the default
  profile's and other accounts' hives.
- **Reproduction (E-I17-R1):** new tests failed on the unchanged code: step 61 of a plan (an `HKLM\…\Run` value) was not
  in the consent text; a change under `HKU\S-1-5-18\…\Run` was called the requesting user's own.
- **Severity / disposition:** potential High — the approval covered steps the user could not see and mislabeled whose
  Registry would change; security gate for the privileged boundary.
- **Remediation:** `33b7de2` — every step numbered and shown in pages with Earlier/Later buttons, every kind of step
  counted in the text, a message-box fallback that refuses plans it cannot show whole, hive owners named. The runtime
  check of that build in the VM (E-I17-V2) found two defects in the fix itself: the page text replaced the plan's title
  (wrong task-dialog element index) and pages of 60 pushed the buttons off a 1080-pixel screen at 150%. `5c54181`
  fixed both (element index; pages of 20).
- **Tests added:** `ElevationConsentTests` (4); the UI Automation harness used in the VM is retained with the evidence.
- **Revalidation:** unit tests; CI 36763921747 and 36767308673 green; VM runtime check of `5c54181` (E-I17-V3): all 130
  steps shown across 7 pages, buttons enabled correctly, Cancel ran nothing.
- **Evidence invalidated:** any earlier observation of the consent window (none was recorded as evidence).
- **Remaining:** re-audit; human attestation of the consent window on the candidate (PPL-03); the rest of I17 (loader
  search order, pipe ownership, requester identity limits, cancellation and partial results) is not yet worked.

### I21 — The Registry's 32-bit and 64-bit views of HKLM, HKU and HKCC failed without administrator rights

- **Discovered:** E-X01 run W1, the first unelevated run of the Windows suites (lent VM, `be6ca25`):
  `RegistryHardeningTests.Explicit_32_bit_view_reaches_redirected_keys_below_the_root` failed with
  `UnauthorizedAccessException`. CI never saw it: every CI Windows lane runs elevated.
- **Requirement:** AI-05 (the main window runs without administrator rights) with the Registry view's explicit 32-bit
  and 64-bit views (plan §3 registers); V06/V13 unelevated behavior.
- **Mechanism (E-I21-R1):** the provider opened a hive with `RegistryKey.OpenBaseKey(hive, view)` and used its `Handle`;
  for an explicit view, .NET reopens the predefined root with write access, which a token without administrator rights
  is refused for `HKLM`, `HKU` and `HKCC`. Default-view browsing, `HKCU` and `HKCR` were unaffected.
- **Severity / disposition:** Medium — a confirmed feature failed in FileCat's default mode; must fix.
- **Remediation (`47c27b9`):** hives are opened from their predefined handles with `RegistryKey.FromHandle(handle,
  view)`.
- **Tests added:** `RegistryStandardUserTests` (2) run under a restricted token (Administrators deny-only) and compare
  each view's `HKLM\SOFTWARE` listing with Windows' own.
- **Revalidation:** the new tests failed before and pass after; CI 36763921747 green; VM run W2 (unelevated, `47c27b9`):
  Platform.Windows 0 failures.
- **Remaining before closure:** re-audit; V13 Registry checks as a real standard user on the candidate (ENV-05).

### I22 — Replacing a file that is open failed on Windows with a misleading "Access denied", and a closed comparison kept its files open

- **Discovered:** intermittent failure of the Synchronize App test — VM run W1 and CI run 36759824994 (Windows ARM64):
  the replaced file kept its old content (E-I22-D1).
- **Requirement:** plan §9.5 (viewers never block other programs), PI-07 (truthful outcomes and error causes), OPS
  copy/replace semantics; V03 (transfers) and the required CI lanes' determinism.
- **Mechanism (E-I22-M1):** (1) a content comparison released its two files only after its load finished *on the UI
  thread*, so for a moment after closing — longer while that thread was busy — FileCat still held them open;
  (2) Windows refuses `MoveFileEx(REPLACE_EXISTING)` onto a file another handle holds open, even when that handle shares
  deletion; (3) FileCat classified that refusal as an access problem ("… Windows Controlled Folder Access may be blocking
  FileCat"), skipped its quiet retries for files in use, and asked the user.
- **Reproduction (E-I22-R1):** a closed comparison still held its files 10 s later while the window's thread was busy; a
  copy with Replace onto a file open in FileCat's own viewer (F3) failed with the Controlled Folder Access message. The
  same copy succeeds on Linux and macOS, where replacing an open file is allowed.
- **Severity / disposition:** Medium. No data is lost (the staged copy is discarded; the target keeps its content), but
  a replace fails in a common situation (the file is open in FileCat's viewer, a comparison, or another program that
  shares deletion), the message points to the wrong cause, and two required lanes became nondeterministic. Must fix.
- **Remediation (`63d5fc4`):** the comparison disposes its files when its readers stop, without waiting for the UI
  thread; on Windows, a replace refused with access denied is retried as a POSIX-semantics rename (`FileRenameInfoEx`),
  which succeeds when every open handle shares deletion — the open handle keeps reading the old content, as on Linux —
  and a sharing violation from that attempt is reported as "in use" (with the quiet retries) instead of "access
  denied"; folders, read-only files and file systems without POSIX renames keep the classic behavior.
- **Tests added:** `CompareWindowTests.Closing_releases_the_files_while_the_windows_thread_is_busy`, two
  `OpenTargetReplaceTests` (Windows).
- **Revalidation (E-I22-V1):** the new tests fail on `5c54181` and pass on the fix; all suites green on the host, in the
  unelevated VM (plus 15 whole App-suite runs) and on CI 36773433835.
- **Evidence invalidated:** V03 replace cases on Windows; E-X01 App and Platform.Windows results before `63d5fc4`.
- **Remaining before closure:** re-audit (the replace path is safety-relevant: review the fallback's conditions and its
  write-through semantics); V03 replace cases on the candidate, including SMB and FAT destinations where the fallback
  must not apply.

### I23 — Network discovery listed a device by its address when its name arrived late

- **Discovered:** E-X01 run U2 (Ubuntu VM under host contention): the WS-Discovery device was listed as "127.0.0.1"
  instead of "TESTBOX" (E-I23-D1).
- **Mechanism (E-I23-M1):** the name lookup (WS-Transfer Get of the device's metadata) was canceled with the search
  window, so a device answering late in the window, or answering the Get slowly, lost its name.
- **Severity / disposition:** Low for users (the device is still listed and usable by address); must fix because the
  feature is confirmed and a required test depends on timing.
- **Remediation (`d40e510`):** the lookups started within the window end at the metadata client's own timeouts (3 s),
  not with the window.
- **Test added:** `NetworkDiscoveryTests.A_name_that_arrives_after_the_search_window_is_still_used` (deterministic; fails
  on the unchanged code with the U2 message).
- **Revalidation (E-I23-V1):** Ubuntu VM suites and 20 discovery runs green; CI 36773433835 green.
- **Remaining before closure:** re-audit; V08/network discovery against real devices on the candidate.

### I24 — The panels' Modified column shows no seconds by default

- **Reported:** by the owner, 2026-09-30 (interactive use).
- **Observed in source:** the default date format "Culture" formats with .NET's `g` pattern (short date and short
  time), and three of the four preset formats in Settings (`yyyy-MM-dd HH:mm`, `dd.MM.yyyy HH:mm`, `MM/dd/yyyy h:mm tt`)
  also drop seconds; only `yyyy-MM-dd HH:mm:ss` keeps them. Conflict dialogs already show seconds
  (`Formatters.DateWithSeconds`).
- **Expected:** the Modified column shows seconds by default.
- **Severity / disposition:** Low (presentation); queued behind the Medium issues. Before changing it, check the column
  widths, the culture's long time pattern, and tests that compare formatted dates.

### I25 — Markdown files open as plain text

- **Reported:** by the owner, 2026-09-30 (interactive use).
- **Observed in source:** no Markdown handling exists; the viewer renders HTML (`.htm`, `.html`, `.xhtml`, …) through
  the page engine (`HtmlPage`) and shows every other text file, `.md` included, as plain text.
- **Expected:** Markdown shown rendered (a better viewer, for example through the page engine), with the plain text
  still available.
- **Severity / disposition:** Low (improvement); queued. Rendering must keep the page engine's containment (scripts off,
  no network) — a Markdown renderer must not become a way to load remote content.

### I26 — Progress at 100% while an operation still works, and a time left that was not honest

- **Reported:** by the owner, 2026-09-30: an operation showed 100% while something was evidently still happening; the
  time left must be realistic, steady, and honest about its uncertainty (a lowest and a highest estimate).
- **Reproduction (E-I26-R1):** a 512 MB copy with read-back verification showed 100% for 1.7 s of its 2.7 s (63%) and no
  time left during that part: only copied bytes counted.
- **Remediation (`d40fda0`, E-I26-V1):** all work counts (copying, reading back, what skipped or failed files settle); a
  new estimator models time per megabyte plus time per file, gives a likely and a pessimistic time left (shown as a range
  while they differ), claims nothing while measuring, counting, stalled or paused, never shows 100% before the end, and
  smooths what is shown so it counts down steadily.
- **Tests added:** `ProgressEstimatorTests` (8), `JobProgressViewTests`, time-left formatting cases.
- **Revalidation:** all four host suites green; screenshot of a verified copy under way.
- **Remaining before closure:** re-audit; watching the estimate on long real jobs on the candidate (USB, network, phones);
  stream jobs (archives, remote) do not count verification work yet.

### I27 — Linux: under the Adwaita 41 icon theme FileCat finds no file-type icons

- **Discovered:** E-X01 run U3 (the Ubuntu 22.04 VM's own GNOME session, icon theme Adwaita):
  `FreedesktopIconsTests` skipped with "The icon theme Adwaita has no text icon here".
- **Observed:** adwaita-icon-theme 41.0 ships only `text-x-generic-symbolic.svg` for text files (`ubu-adwaita.txt`);
  FileCat's lookup (theme, its parents, hicolor) asks for the full-color names only, finds nothing, and shows its
  built-in icons. GTK falls back to the `-symbolic` variant in that case.
- **Severity / disposition:** Low (cosmetic); queued. A fix would add the `-symbolic` names as the last fallback and draw
  them in the text color.

### I28 — A damaged NTFS size or data run made the whole volume unreadable to recovery

- **Discovered:** a 100,000-round `RecoveryFuzzTests` run on the owner's M1 Mac (E-X01 M3, E-I28-D1).
- **Mechanism (E-I28-R1):** NTFS round 8842 damages the `$Bitmap` record so that its data size reads as negative;
  `NtfsScanner.ReadStream` allocated an array of that length (`OverflowException`), and the scanner's safety net
  reported the whole volume "damaged beyond what FileCat reads", offering nothing on it. Review of the same decoder
  found negative or enormous cluster numbers, unbounded sparse runs, and a per-cluster walk over sparse runs that a
  damaged run could turn into a hang.
- **Requirement:** plan §17.3 (untrusted disk structures: a damage report or a smaller listing, never an unexpected
  exception, a hang or an unbounded allocation); recovery completeness (I09 context).
- **Severity / disposition:** Medium — no crash (the safety net held) and no write, but a damaged volume whose files
  FileCat could otherwise offer became entirely unrecoverable in FileCat, and a hang was possible. Must fix.
- **Remediation (`98fb594`):** bounds on every decoded cluster number, size and run; the stream reader clamps to the
  stream; sparse runs become one extent. The fuzz test names the failing image and round, can run a chosen image and
  range, and replays saved failing rounds on every run.
- **Revalidation (E-I28-V1):** the saved round fails on the unchanged code and passes on the fix; Core suite green. A
  fuzz campaign over millions of further rounds on four machines is running (E-I28-C1).
- **Second finding (`bb977d0`):** the campaign then found NTFS round 56958 on the fixed build: damage that cleared the
  root record's in-use or folder flag listed the root folder as a nameless file, and numbering the listing threw out of
  the whole scan (other volumes included). The root record is never listed as an item, and preparing a volume's listing
  runs inside the per-volume safety net. Rounds 0–99,999 of NTFS pass on `bb977d0`; both rounds are saved as tests.
- **Remaining before closure:** the campaign's results; re-audit; the same review for the FAT and exFAT decoders.

### I29 — A shell picture asked for while the helper already worked on it was asked again

- **Discovered:** CI run 36778104838 (Windows ARM64) failed a shell-preview test on an unrelated commit (E-I29-D1).
- **Mechanism (E-I29-M1):** the preview worker took a request off the queue before asking the helper, so an identical
  request made meanwhile asked the helper again and its answer replaced the first in the cache.
- **Severity / disposition:** Low (duplicate work, no wrong picture) but a required lane went red; must fix.
- **Remediation (`7175a41`):** the request in progress stays joinable until its answer is cached; a deterministic test
  holds the first request at the helper while the second is made (it failed with CI's message before the fix).
- **Revalidation:** Platform.Windows suite green; CI 36779059604 green on all lanes.

### I30 — Running operations should show what happens in the best possible way

- **Requested:** by the owner, 2026-09-30 (middle priority): when users move their data they need to know what is
  happening, visualized in the best possible way.
- **Observed (E-I26 pictures):** the Operations strip is one line of text over a 4-pixel bar with the current file's name;
  there is no percentage, no progress of the current (large) file, no phase (copying, verifying, finishing), no speed
  history; the details drawer lists jobs, but its right half stays empty until a job is picked.
- **Plan:** a percentage and the phase on the strip; the current file's own progress for large files; the details show
  the running job at once: where from and to, elapsed time, the honest time left, speeds, counts of done, skipped and
  failed items, and a speed history; Windows taskbar progress for a minimized window; each change pictured with the
  screenshot tool and covered by view-model tests.

## New detail on open issues

- **I03 / I18:** the Windows installer's compiler is whatever Inno Setup the hosted runner image provides: the A01
  ARM64 job log shows `choco install innosetup` reporting "InnoSetup v6.7.1 already installed" and `ISCC` from
  `Inno Setup 6`. Current upstream stable is 7.1.0 (2026-08-12); the workflow hard-codes the `Inno Setup 6` path. The
  compiler contributes bytes (setup and uninstaller stubs) and must be pinned and inventoried (plan §10.2).
- **I03:** `eng/publish.ps1` writes `sbom-<version>.json` for each RID under the same name; running it for win-x64 and
  win-arm64 with one version leaves only the last RID's inventory.
- **I03 / I18 (E-V19-P1, E-I15-V1):** packages carry code for other architectures: the macOS arm64 app bundles
  universal (`x86_64 arm64`) `libAvaloniaNative`, `libHarfBuzzSharp` and `libSkiaSharp`; the x64 Windows payload carries
  foreign-architecture WebView2 loaders. The inventory must list them, and the release owner decide whether to thin them.
- **I04 (E-V19-P1):** the `.deb`'s ICU alternatives end at `libicu76` (Ubuntu 26.04 supplies `libicu78`, plan §4.1);
  the macOS bundle declares `LSMinimumSystemVersion 13.0`, which the Platform Support Decision must match (DEC-02). On
  Ubuntu 22.04 the `.deb`, tarball and AppImage installed, ran and uninstalled cleanly.
- **DEC-03 input (E-V19-P1):** Gatekeeper rejects the ad-hoc-signed app, quarantined or not.

## Initial register entries not yet worked

I01–I14, I16 and I18 keep the plan's §7 text as their current record, and I17 keeps it for the parts not worked above.
I24–I27 are queued owner reports and findings of lower severity.
None has been closed. Their evidence, reproduction and remediation fields will be filled when worked.
