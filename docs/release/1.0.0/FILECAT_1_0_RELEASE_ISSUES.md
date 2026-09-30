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
| I04 | Platform/package claim mismatches | High (where a clean install fails) | Blocker for the affected claim | Open |
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
| I15 | Uninstaller removed the whole installation folder | **Critical** (data loss) | Blocker | **Remediated `5b061cc`; runtime verification in progress** |
| I16 | Automatic browse/launch boundaries | Potential High | Security gate | Open |
| I17 | Broker consent/loader/pipe completeness | Potential High/Critical | Security gate | Open |
| I18 | Release control and pipeline provenance | High | Blocker (integrity) | Open; new detail below |
| I19 | Interrupted-copy cleanup deleted complete or user-changed files | **High** (data loss) | Blocker (non-waivable class) | **Remediated `f87ad32`; verified** — closure pending re-audit |
| I20 | `$LogFile` attributed an earlier item's operations to the current file | Medium (false forensic finding) | Must fix (confirmed D-56 surface; destabilized the required CI lane) | **Remediated `45efc09`; verified** — closure pending re-audit |

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
- **Runtime verification:** in progress on a snapshotted Windows 11 VM (E-I15-V1): the baseline and fixed installers
  built from the same payload with Inno Setup 6.7.1, installed into a folder holding user files, uninstalled; plus the
  fixed installer in its default folder with FileCat started once. The Windows Sandbox attempt failed (environment,
  E-ENV-01).

## New detail on open issues

- **I03 / I18:** the Windows installer's compiler is whatever Inno Setup the hosted runner image provides: the A01
  ARM64 job log shows `choco install innosetup` reporting "InnoSetup v6.7.1 already installed" and `ISCC` from
  `Inno Setup 6`. Current upstream stable is 7.1.0 (2026-08-12); the workflow hard-codes the `Inno Setup 6` path. The
  compiler contributes bytes (setup and uninstaller stubs) and must be pinned and inventoried (plan §10.2).
- **I03:** `eng/publish.ps1` writes `sbom-<version>.json` for each RID under the same name; running it for win-x64 and
  win-arm64 with one version leaves only the last RID's inventory.

## Initial register entries not yet worked

I01–I14 and I16–I18 keep the plan's §7 text as their current record. None has been closed. Their evidence,
reproduction and remediation fields will be filled when worked.
