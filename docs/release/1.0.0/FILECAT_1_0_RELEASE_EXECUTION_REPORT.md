# FileCat 1.0.0 — release execution report

Operational plan: [FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md](../../design/FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md)
(with its review disposition). Companion records: [issue register](FILECAT_1_0_RELEASE_ISSUES.md),
[evidence index](FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md), [open blockers and decisions](FILECAT_1_0_RELEASE_BLOCKERS.md).
Candidate-specific evidence will live in `docs/release/1.0.0/<candidate-id>/` once a candidate exists.

## Current state (updated 2026-10-01)

- **Readiness: NO-GO.** Release readiness is not established. No release candidate, tag, signed artifact or qualified
  package exists. Phase: A–F (baseline, reconciliation and preliminary validation with remediation).
- **Candidate identity:** none.
- **Source:** `main` at `85d512d` (plan baseline `4f6b062` plus the campaign's commits listed in the evidence index).
- **Defects found and fixed so far:** I19 (High, data loss), I15 (Critical where it happens, data loss), I20 (Medium,
  false forensic finding), I17's consent display (potential High, privileged boundary), I21 (Medium, Registry views
  without administrator rights), I22 (Medium, replacing an open file on Windows), I23 (Low, discovery naming), I28
  (Medium, a damaged NTFS field made recovery give up on the whole volume). All are remediated and verified by
  targeted and affected regressions; closure awaits re-audit and final-candidate evidence. Also fixed since: I24
  (seconds in the Modified column), I26 (progress and time left, confirmed Medium), I28's second finding, I29 (a CI-red
  race in shell previews).
- **Done since:** I30 (how running operations show, `67f70f9`; the taskbar still to be seen on a real desktop), I32 (a
  folder's counted size vanishing at a refresh, `6e9ee75`), I33–I41 and I43–I51 (remote transfers against real
  servers of two implementations, recovery allocation, state-folder permissions, a second FileCat seeing a running job,
  moves deleting a source whose copy was gone or deleting what was never copied, Synchronize acting on targets changed
  since the comparison, a link's read-only set through it, FAT32 recovery guessing where an entry was whole: I52, the
  hex editor's patch and Save As: I53, I54, a .reg backup restorable into the wrong Registry view: I55)
  and I25 (Markdown drawn as a page). **Open, measured:** I42 (per-file round trips of remote copies; owner decision).
  **Queued:** I27 (Linux icons under Adwaita 41), I31 (viewer windows only partly themed; assessed).
  **Running:** a fuzz campaign of the recovery scanner over millions of rounds on both VMs and the Mac (E-I28-C1).

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
| 3 Collect CI/validation evidence and skip inventory | **Done (preliminary)** | E-A01 (TRX lanes), E-A02 (every lane from the log, reasons from source; 37 tests run on no lane, all gated; the ARM64 lane's missing Remote tests added, `98bc539`); early-return audit (E-S01, `be6ca25`). To repeat on the candidate's run |
| 4 Reconcile manifest and registers against source | Not started | Plan §§3–5 registers stand as the starting point |
| 5 Contract questions (I05, I06, PSD, Mac, FDD, I14) | **Open (owner)** | DEC-02…DEC-06, EXT-02 |
| 6 V23 source review, test-guard audit, case catalog | **Partial** | DPI P01–P06, P08–P12, P14–P16 reviewed, P07 in part (I15, I19, I40, I44, I48–I51, I53–I55; E-DPI); B04 consent display audited (I17); P07's loader audit (V06), P13 and the other B rows remain |
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
32. V08 interruption (E-V08-L1): the server cut uploads off part way over SFTP and explicit FTPS; FileCat asked,
    reconnected on Retry, continued after checking the part on the server, and the files arrived byte for byte. The
    FTPS question read "see inner exception"; lost connections are now described by what happened (`5183cd3`).
33. V08 SMB (E-V08-S1) through Windows' client against Samba: round trip checked by the server's digests, times kept
    exactly, no Recycle Bin on a share, rename on the server, cancel, a dropped session (recovered by Retry once, by
    Windows' own reconnection once). A replace onto a file open in the viewer still said "Controlled Folder Access",
    and a share was called NTFS in the metadata question (I34), fixed (`6585024`).
34. Fuzz campaign status (E-I28-C1, in progress): Mac 1 M rounds each of fat12, exFAT and NTFS at `bb977d0` passed;
    Windows VM 1 M rounds of fat12 at `98fb594` passed; the other runs continue.
35. Read-back verification was ignored for uploads, downloads, extraction and copies to phones (I35); fixed
    (`53b0794`), with V08's altered-resume cases as tests.
36. V08 odd names: FTP refused, trimmed or redirected names (I36); fixed (`e50b9d4`); odd names now arrive exactly over
    SFTP and FTPS (checked against the bytes on the server's disk).
37. The Ubuntu VM ran out of memory: a fuzz round of a damaged FAT image sized a 1 GiB table, and a damaged NTFS
    `$Bitmap` was read at 512 MiB (I37); fixed (`02acee6`). The VM's runs restarted as user services, at the lowest
    priority and with a heap cap; guest operations to that VM now go over SSH (E-ENV-05).
38. CI on `a1be480` failed one recovery-review test on Windows: the test faked an interrupted copy whose creation time
    came from its source, which a stalled runner put outside the review's margin; reproduced and fixed in the test
    (`5b8b180`). The product logic is unchanged (an interrupted copy never gets its source's times).
39. I25 Markdown viewer implemented (`7abd0fe`): built-in renderer, drawn in the page engine, checked in WebView2; drawn
    by WebKitGTK and WKWebView in CI too (`ec5d475`).
40. I37 follow-ups: FAT and exFAT reviewed the way I28 reviewed NTFS (`b9c41eb`); the Ubuntu run's NTFS round 169883
    (1.6 GiB for a damaged compressed size, `0ec94f1`) and the Mac's exFAT round 5326394 (`9347070`) found, fixed and
    replayed; the runs resumed on the newest decoders.
41. V08 at a 100 ms round trip (E-I38-I39): correctness held, but an SFTP upload cut off by the server missed its time
    limit. FTP stats listed whole folders on vsftpd (I38, `f93f919`); SFTP uploads wrote one request at a time (I39,
    `2ba114e`); a cut-off upload on a slow link now starts again when that is quicker (`1dce2c2`).
42. P16 review: FileCat's state folders were readable by other local accounts on Linux and macOS (I40, `8b0dafd`).
43. A channel trace at 100 ms found SFTP held to SSH.NET's socket buffers after `ConnectAsync` (I41): fixed (`4c6b910`),
    32 MB at 100 ms now 11.6 MB/s down and 8.2 up instead of 1.2 and 1.6; the whole lab at 100 ms 19 of 19 in 13 min 8 s
    (33 min 52 s before I38/I39). The trace also measured what one small file costs (I42, open: owner decision).
44. A lab case renames, moves, sets aside, replaces and deletes links on the real server over SFTP and FTPS, checked
    with the server's own shell: links change themselves, never their targets (no defect; `4c6b910`).
45. CI red twice on unrelated tests (`2ba114e` ARM64 shell preview, `8b0dafd` x64 operations strip): the Shell helper
    now starts the Shell before it says it is ready, and the test waits as long for every state (`2a6f882`).
46. DPI P04 review: a second FileCat on the same profile showed a running job as interrupted on Linux and macOS (I44),
    reproduced on the Mac, fixed (`e399276`). The hex-save journal and edit sessions were checked for the same and are
    safe (exclusive journal while saving; commits refuse a changed target).
47. Uploads to FTP servers without MFMT silently kept the arrival time, and downloads took vsftpd's coarse listing time
    (I43): fixed (`e527a86`); the lab's tree case now checks every time both ways and fails before the change.
48. V08's second implementations (E-V08-L2): ProFTPD 1.3.7c with FTPS (MLSD) and `mod_sftp` beside the lab's servers
    (installing it removed vsftpd, which was put back beside it). 14 of 19 at first: over ProFTPD's SFTP, renaming or
    moving a link moved its target (I46, High; the server's behaviour, confirmed with OpenSSH's own client; fixed
    `3f1b554` by renaming links over SFTP only on OpenSSH), and an FTPS upload cut off never finished (I47: a refused
    APPE retried forever, a 60 s stall; fixed `111ebcd`). FTP listing times are now shown and compared as far as the
    server states them (I45, `111ebcd`). After: 19/19 on ProFTPD, 19/19 on OpenSSH/vsftpd, 7/7 on Samba.
49. CI red once more on the MFT-record test (x64, after ARM64 earlier): the record and its log read a moment before the
    time change reached the disk; the test now reads again as a user would (`b4d0f52`).
50. Fuzz campaign collected (E-I28-C1): the Windows VM's 1.1–2.1 M range passed for five images and NTFS; the Mac's
    5.1–6.1 M range passed (exFAT after its I37 fix); the idle Mac started 7.1–8.1 M of every image.
51. DPI review (E-DPI): P01 found I48 (a move deleted its source although its copy was gone; fixed `e72e3fc`), P09 found
    I49 (moves to and from servers could delete what was never copied; fixed `e72e3fc`, checked on three servers);
    P02, P08 and P14 held.
52. DPI P10: Synchronize removed or replaced target items edited while the plan was reviewed (I50, High; fixed
    `99145cf`). DPI P11: on Linux and macOS a link's read-only was set through it (I51, seen on the Mac; fixed `65a76f8`).
53. The lab VM's emulated network adapter hung under load from both clients at once ("Detected Tx Unit Hang"); the VM
    was reset, its offloads turned off; its fuzz runs restarted as one queue, four at a time (E-ENV-05, E-I28-C1).
54. V08 from the Windows VM as a client (E-V08-L2): 21/21 against OpenSSH and vsftpd, 21/21 against ProFTPD, 7/7 against
    Samba, after the lab's cut-off case was made independent of the drop command's speed (`d228632`).
55. V09 on disk images made by Windows' own drivers (E-V09-W1): NTFS 6/6 byte for byte; exFAT 4/4, the two files of a
    folder whose listing Windows reused rightly declared lost; FAT32 4/6, two files placed by a wrong guess though
    their entries were whole (I52, fixed `78a48ce`; 6/6 after). The raw bytes were read independently.
56. CI red from `99145cf` on: I50's folder check by the folder's own time failed both ways on NTFS (a folder's listed
    time lags its own; a second item within one clock tick leaves it unchanged). Reproduced on the host (10 and 5 of
    40 runs fail), redone by what the folder holds (`efc128f`; 0 of 40). Three test flakes fixed or made to explain
    themselves (`7791bda`): the move test's hook raced the copy job's counting thread on macOS.
57. DPI P05 (hex saves, recovery, Save As, patches): a patch that went over the limit of changed bytes was applied in
    part while the editor said it was not (I53, Medium); on Linux and macOS Save As could keep a copy mixing old and
    new bytes (I54). Both fixed `7a99f9d`, each test failing before (the second on the Mac).
58. The host's VM drive (V:) filled: the Windows VM's snapshot disk had grown to 87.7 GB, and the VM stopped on a
    disk-full question. It had no `filecat-before` snapshot after all; it was reverted to the owner's snapshot it was
    lent from (one unrelated file moved off V: for the revert and put back unchanged). Its unfinished fuzz ranges run
    again on the host; a watchdog pauses the Ubuntu VM's runs should V: run low again (E-ENV-05, E-I28-C1).
59. DPI P06 (Registry): a .reg backup from the 32-bit view could be imported into the default view (I55, fixed
    `cf92679`). DPI P07 in part: consent, scope and path handling held; the loader audit stays with V06.
60. I22/I34's last case: replacing an open file on FAT32 and exFAT, in the Windows VM on Windows-formatted virtual
    disks: "in use", kept, replaced on Retry once closed (E-I22-F1); the VM reverted to its lent state afterwards.
61. CI red once on ARM64 (`cf92679`): the discovery test judged "not followed to another address" by time; it now
    checks that nothing connects there, and fails when discovery is made to follow (`783c1b4`).
62. Step 3 completed (E-A02): every lane's skips from CI run 36821398706 with their reasons from source. 37 tests run
    on no lane, all gated on labs, devices, a phone or benchmarks (their evidence is this campaign's runs); the ARM64
    lane ran no Remote tests, now added (`98bc539`; 81 passed, 33 skipped, 0 failed on its first run).
63. I16's three named items: Git badges no longer touch a repository's linked paths before they are known to be on this
    computer, an icon named in a folder's desktop.ini goes through the helper's policy before anything is read, and every
    program FileCat starts is found by full path, never in the current directory (`2f35a6b`).
64. I12: the `$Secure` failure is held by `62bd88f`'s unit and live tests (Windows' own report as the oracle); the macOS
    page title by twelve lifecycles in the page engine smoke (`85d512d`; 12 of 12 on the Mac, none raising events after
    disposal).

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
- `f93f919`, `2ba114e`, `1dce2c2`, `4c6b910`, `e527a86` (FTP and SFTP channels, remote jobs): every V08 result before
  them; the lab's runs must be repeated on the candidate.
- `8b0dafd` (state folders), `e399276` (journal recovery): V11/V23 state evidence and V03 interruption evidence.
- `2a6f882` (Shell helper): the helper binary changed; V16 Shell-preview evidence must use the candidate.
- `78a48ce` (FAT recovery): V09/V11 FAT recovery evidence before it.
- `efc128f` (comparison, Synchronize): V13 comparison and synchronization evidence before it; a one-sided folder is now
  read in full when compared.

## Next actions (unblocked)

1. Collect the fuzz campaign's results (E-I28-C1).
2. DPI P13 (needs the phone); P07's loader audit with V06 on installed candidates.
3. I42's options for the owner (fewer requests per file; several files in flight).
4. The queued Low issues: I27, I31.
5. Recovery and device-read cases on disposable virtual disks attached to the VMs (FAT/exFAT/NTFS images, block devices;
   I09 topology), as the owner permitted.
6. Continue the V23/DPI source review in risk order: the rest of B04 (I17), B10 (I16), B08 (I09).
7. I04 on Ubuntu 26.04 (needs that system).
8. Keep the records current after each change.
