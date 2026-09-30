# FileCat 1.0.0 — release execution report

Operational plan: [FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md](../../design/FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md)
(with its review disposition). Companion records: [issue register](FILECAT_1_0_RELEASE_ISSUES.md),
[evidence index](FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md), [open blockers and decisions](FILECAT_1_0_RELEASE_BLOCKERS.md).
Candidate-specific evidence will live in `docs/release/1.0.0/<candidate-id>/` once a candidate exists.

## Current state (updated 2026-09-30)

- **Readiness: NO-GO.** Release readiness is not established. No release candidate, tag, signed artifact or qualified
  package exists. Phase: A–F (baseline, reconciliation and preliminary validation with remediation).
- **Candidate identity:** none.
- **Source:** `main` at `3ec60cc` (plan baseline `4f6b062` plus the campaign's commits listed in the evidence index).
- **Defects found and fixed so far:** I19 (High, data loss), I15 (Critical where it happens, data loss), I20 (Medium,
  false forensic finding), I17's consent display (potential High, privileged boundary), I21 (Medium, Registry views
  without administrator rights), I22 (Medium, replacing an open file on Windows), I23 (Low, discovery naming), I28
  (Medium, a damaged NTFS field made recovery give up on the whole volume). All are remediated and verified by
  targeted and affected regressions; closure awaits re-audit and final-candidate evidence. Also fixed since: I24
  (seconds in the Modified column), I26 (progress and time left, confirmed Medium), I28's second finding, I29 (a CI-red
  race in shell previews).
- **Done since:** I30 (how running operations show, `67f70f9`; the taskbar still to be seen on a real desktop), I32 (a
  folder's counted size vanishing at a refresh, `6e9ee75`). **Queued:** I25 (Markdown shown rendered), I27 (Linux icons
  under Adwaita 41), I31 (viewer windows only partly themed; assessed). **Running:** a fuzz campaign of the recovery scanner over millions of rounds on both VMs and the
  Mac (E-I28-C1).

## Execution baseline

| Property | Observed (2026-09-30) |
|---|---|
| Plan baseline | `4f6b062fa8548fc8fd417a50262a72c0b804461f`, equal to `origin/main` when execution began; no delta to review |
| Working tree | Clean apart from the seven untracked planning documents in `docs/design` (not committed by this campaign) |
| Submodules, tags, releases, issues, PRs | None |
| CI at the baseline | Run 36722039034 green; package jobs skipped (E-A01) |
| Private vulnerability reporting | Disabled (I01) |
| Execution host | Physical Windows 11 Pro Insider 26220, elevated shell (E-ENV-00) |

## Checklist progress (plan §14)

| Step | State | Notes |
|---|---|---|
| 1 Refresh baseline | **Done** | E-ENV-04; no delta from the plan's baseline at start |
| 2 Owners, resources, provider/licence preflight | **Open (people)** | DEC-01, DEC-07, EXT-01, EXT-02; resource status in the blockers file |
| 3 Collect CI/validation evidence and skip inventory | **Partial** | E-A01 (explicit skips on TRX lanes); early-return audit done (E-S01, `be6ca25`: 28 tests now skip with reasons). Still to do: portable-lane and ARM64 skip lists from logs |
| 4 Reconcile manifest and registers against source | Not started | Plan §§3–5 registers stand as the starting point |
| 5 Contract questions (I05, I06, PSD, Mac, FDD, I14) | **Open (owner)** | DEC-02…DEC-06, EXT-02 |
| 6 V23 source review, test-guard audit, case catalog | **Partial** | DPI P03 and P15 audited (I19, I15); B04 consent display audited (I17); the other DPI and B rows remain |
| 7 Reporting, signing, dependency approach, preview preparation | Not started | I01/I02/I03/I14/I18 |
| 8 Fixtures and harnesses | Partial | VMware VMs lent and snapshotted (E-ENV-02); the owner's M1 Mac (E-ENV-05); SFTP, FTP/FTPS and SMB servers on the Ubuntu VM (E-ENV-05, one implementation each); consent UI Automation harness (E-I17); Windows Sandbox unusable (E-ENV-01) |
| 9 S10 suites with native setup | **Partial** | E-L01 (Windows lane locally); E-X01 (unelevated Windows 11 VM, Ubuntu 22.04 VM, M1 Mac; CI for every commit) — preliminary |
| 10 High-risk preliminary cases and remediation | **In progress** | V03-PARTIAL (I19), V19-UNINSTALL (I15), V06-CONSENT display (I17) done preliminarily; I20, I21, I22, I23 from test runs; preliminary V19 package checks on Ubuntu 22.04 and macOS (E-V19-P1) |
| 11–13 V01/V12/V13/V16, human V17/V18, remediation loop | Not started / blocked | Human and reference-hardware work blocked (PPL-01…03, ENV-08) |
| 14 Pipeline, docs, release controls, preview | Not started | DEC-07, DEC-09 |
| 15–26 Freezes, candidate, FQ, REP, GO, publication | Not reachable | Depend on everything above |

## Work log

1. Read the launcher, the operational plan, its disposition record, the planning specification and the product plan in
   full. Refreshed the baseline; `4f6b062` unchanged, no delta review needed.
2. Downloaded the A01 TRX artifacts and inventoried outcomes and skip reasons (E-A01). Ran the Windows S10 lane locally
   on the physical host: 0 failures (E-L01).
3. Static audit of DPI P03 found I19 (the plan had flagged the heuristic; the audit found two further mechanisms: fill
   records keyed by destination only, and no revalidation). Reproduced with new tests, fixed (`f87ad32`), regressed.
4. The CI run of `f87ad32` failed on Windows ARM64 in an unrelated D-56 test. Instead of re-running, reproduced it
   locally (3 of 15), traced it to I20, fixed (`45efc09`), regressed; the formerly flaky test passed 15 of 15 and CI was
   green on all four lanes.
5. Static audit of DPI P15 confirmed I15 and removed the recursive `[UninstallDelete]` (`5b061cc`). Windows Sandbox was
   unusable (0x80070780, E-ENV-01); the owner lent snapshotted VMware VMs; on a Windows 11 VM the baseline installer's
   uninstall deleted a user's files and the fixed one kept them (E-I15-V1).
6. Recorded the CI installer-compiler provenance gap (I03/I18, E-ENV-03) and foreign-architecture WebView2 loaders in
   the x64 payload (E-I15-V1 side observation).
7. Started the owner's MacBookPro access (public-key SSH; key authorization pending) and a manual CI run
   (36759624490) to produce Linux and macOS packages for preliminary V19 checks on the lent Ubuntu VM and the Mac.
8. Early-return audit (step 3): 28 tests passed without testing when their platform or environment was missing; they
   now skip with their reasons (`be6ca25`, E-S01).
9. Ran the suites unelevated on the Windows 11 VM, on the Ubuntu 22.04 VM and on the owner's M1 Mac (E-X01). The
   unelevated run found I21 (Registry explicit views failed without administrator rights); reproduced under a restricted
   token on the host, fixed (`47c27b9`), rerun clean in the VM. The Mac run exposed a test that failed instead of
   skipping when the keychain cannot be unlocked over SSH (`64ed037`).
10. Audited B04's consent display: I17 (steps after the 60th hidden; HKU hives mislabeled), reproduced, fixed
    (`33b7de2`). A runtime check of the installed helper in the VM, driven by UI Automation, found two defects in that
    fix (the page text replaced the plan's title; pages too tall for the screen); fixed in `5c54181` and checked again:
    all 130 steps of a test plan shown, Cancel ran nothing (E-I17).
11. Preliminary package checks (E-V19-P1): the Linux `.deb`, tarball and AppImage installed, ran and uninstalled cleanly
    on Ubuntu 22.04; the ad-hoc-signed macOS app is rejected by Gatekeeper and bundles universal libraries.
12. Set up SFTP, FTP/explicit and implicit FTPS, and SMB servers on the Ubuntu VM for V08 (E-ENV-05).
13. The Synchronize test failed intermittently (VM once, CI ARM64 once). Traced to I22: a closed comparison held its
    files until the UI thread ran, and Windows refuses to replace an open file even when it is shared for deletion;
    FileCat then reported "Access denied … Controlled Folder Access". Reproduced deterministically, including a copy onto
    a file open in FileCat's own viewer. Fix in progress.
14. A discovery test failed once on the Ubuntu VM under host contention: I23 (a device's name lookup was canceled with
    the search window). Fix in progress.
15. The owner reported I24 (no seconds in the Modified column); queued.
16. Fixed I22 (`63d5fc4`: POSIX-semantics replace fallback, "in use" instead of "access denied", comparisons release
    files at once) and I23 (`d40e510`: name lookups outlive the search window). Both reproduced deterministically first;
    host, unelevated VM, Ubuntu VM and CI regressions green.
17. Ran the tests CI can only run in synthetic sessions inside the Ubuntu VM's real GNOME session (E-X01 U3): the page
    engine works with WebKitGTK 4.0 (the `.deb`'s alternative); Samba browsing through GVFS works; under the session's
    Adwaita 41 theme FileCat finds no file-type icons (I27, queued).
18. A 100,000-round fuzz run on the Mac found I28 (NTFS: a negative `$Bitmap` size made recovery give up on the whole
    volume). Reproduced (round 8842), located, fixed with its neighbors (`98fb594`), and the failing round saved as a
    permanent test. A fuzz campaign over millions of rounds of the fixed build is running on four machines.
19. The owner reported I25 (Markdown shown as plain text) and I26 (100% shown while still working; unconfirmed);
    queued.
20. Fixed I24 (`2197074`): seconds in the Modified column by default, checked in a screenshot.
21. CI went red on ARM64 in a shell-preview test (I29): a request race, reproduced deterministically, fixed (`7175a41`),
    CI green again.
22. The fuzz campaign found a second NTFS defect (round 56958: the scan threw on a damaged root record); fixed
    (`bb977d0`); NTFS rounds 0–99,999 pass; the VMs' NTFS runs restarted on the fixed build.
23. I26 confirmed (a verified copy showed 100% for 63% of its time) and fixed (`d40fda0`) with a new progress model and
    an honest, steady time-left estimator, as the owner asked (likely and pessimistic values, no jumps); pictured with a
    new screenshot mode.
24. The owner asked for the best possible way to show running operations (I30, middle priority): the strip now shows
    phase, percentage and the large file's own step; the details open on the running operation with its facts and a
    speed graph; Windows shows the progress on the taskbar button (`67f70f9`).
25. A Space/folder-size test began failing 9 in 10 on the busy host; diagnosed as a real race (a size tied to a
    modification time the listing had read mid-write) and fixed (`6e9ee75`, I32).
26. The owner reported I31 (viewer windows only partly themed); assessed and queued as polish.
27. Observation: one run of `ListingModelTests.Untouched_cursor_stays_on_the_first_row_while_entries_stream_in` timed
    out in a full-suite run while both VMs saturated the host; 30 isolated runs passed. Watched, not changed.
28. The taskbar progress (I30) was seen on the Windows VM's desktop: blue, amber, red and marquee states as intended.
29. CI's ARM64 lane timed out the new I29 test's first picture (a cold helper on a busy runner); the test now warms the
    helper and checks the sharing itself (`069732d`).
30. The owner decided the Markdown viewer is required for 1.0.0 (DEC-11); approach: a built-in renderer.
31. V08 started against real servers (E-V08-L1): trust, pinning, consent and byte-exact round trips over SFTP and both
    FTPS modes pass; a cancelled upload left its partial copy on the server (I33), fixed (`3ec60cc`).

## Evidence invalidated by the campaign's own changes

- `f87ad32` (job engine, interrupted-copy review): E-A01 and E-L01 no longer describe current source for transfer
  paths; V03 interrupted-copy cases and small-file copy throughput must be re-run on the candidate.
- `5b061cc` (installer script): every earlier installer build; V19 lifecycle evidence must use the final setup.
- `45efc09` (D-56 `$LogFile` reader): V14 `$LogFile` evidence; the reader now waits up to about six seconds when the
  on-disk log lags, which affects the record window's time to open in that case.
- `33b7de2`, `5c54181` (administrator helper): any evidence of the consent window and V06-CONSENT; the helper binary
  changed, so broker evidence must be taken on the candidate.
- `47c27b9` (Registry provider): V13 Registry browsing evidence for explicit views.
- `63d5fc4` (Windows file operations, comparison window): V03 replace and move-over-existing evidence on Windows.
- `98fb594` (NTFS recovery decoder): V11 NTFS recovery evidence.

## Next actions (unblocked)

1. V08: a connection dropped mid-transfer (resume), SMB through the operating system, latency; collect the fuzz
   campaign's results (E-I28-C1).
2. I25 Markdown viewer (required for 1.0.0, low priority).
3. Review the FAT and exFAT decoders the way I28 reviewed NTFS (after the campaign's results).
4. The queued Low issues: I27, I31.
3. V08 remote harness against the Ubuntu VM's servers from the Windows VM and the host: host-key trust and change, TLS
   validation, interruption and resume, latency, a server-side oracle.
4. Recovery and device-read cases on disposable virtual disks attached to the VMs (FAT/exFAT/NTFS images, block devices;
   I09 topology), as the owner permitted.
5. Continue the V23/DPI source review in risk order: DPI P01/P02/P04/P08/P14/P16, the rest of B04 (I17), B10 (I16),
   B08 (I09).
6. Finish step 3's skip lists for the portable and ARM64 lanes; I04 on Ubuntu 26.04.
7. Keep the records current after each change.
