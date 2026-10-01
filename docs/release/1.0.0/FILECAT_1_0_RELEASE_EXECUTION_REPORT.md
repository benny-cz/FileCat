# FileCat 1.0.0 — release execution report

Operational plan: [FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md](../../design/FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md)
(with its review disposition). Companion records: [issue register](FILECAT_1_0_RELEASE_ISSUES.md),
[evidence index](FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md), [open blockers and decisions](FILECAT_1_0_RELEASE_BLOCKERS.md).
Candidate-specific evidence will live in `docs/release/1.0.0/<candidate-id>/` once a candidate exists.

## Current state (updated 2026-10-01)

- **Readiness: NO-GO.** Release readiness is not established. No release candidate, tag, signed artifact or qualified
  package exists. Phase: A–F (baseline, reconciliation and preliminary validation with remediation).
- **Candidate identity:** none.
- **Source:** `main` at `1ec9d13` (plan baseline `4f6b062` plus the campaign's commits listed in the evidence index).
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
  and I25 (Markdown drawn as a page); I56, I57 (V23 B05), I58 (a damaged TAR header made .NET's TAR reader take up
  to 2 GiB, found by the archive damage campaign, `325aa63`) I59 (a Registry key's rename could be redirected
  through a link put in its place, V23 B07, `b02a01f`) I60 (the AppImage's runtime came unchecked from a moving
  release, V23 B09, `84b847a`), I61 (`--workspace` and `--list` ignored, `dcd81a1`), I62 (two names for one profile
  ran as two instances, `2cd313f`; both V23 B12) I63 (the update check opened whatever page its answer named, V23
  B13, `9bedead`) I64 (a folder's name could turn the shown location around, V23 B14, `e6e9ad0`), I65 (a
  damaged PE made the inspector throw, found by its damage campaign, `8cb0737`) and I66 (a Linux-deleted FAT file was
  called empty and recoverable, `1477de3`). **Open, measured:** I42 (per-file round trips of remote copies; owner decision).
  The queued Low issues are done: I31 (viewer windows only partly themed, `99a6ae4`) and I27 (Linux icons under
  Adwaita 41, `4a4349f`).
  **I09** (recovery scanned a disk FileCat itself writes to; destinations behind loop devices, disk images, VHDs and
  shares served by the same computer were taken for other disks; Potential Critical) remediated preliminarily
  (`27256f6`, `7418c04`, `0a52b7b`), live-checked on Ubuntu, macOS and the Windows VM; V09's write trace pending.
  **Running:** a fuzz campaign of the recovery scanner over millions of rounds on both VMs, the host and the Mac (E-I28-C1).

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
| 4 Reconcile manifest and registers against source | **Partial** | E-R04: every code name the plan's rows cite exists (137 in 421 rows; 8 rows explained), every capability has a route; whether each claim holds is left to the V cases |
| 5 Contract questions (I05, I06, PSD, Mac, FDD, I14) | **Open (owner)** | DEC-02…DEC-05, EXT-02; I06's page caches meet the planned shared budget (`61b028f`), so DEC-06 is closed |
| 6 V23 source review, test-guard audit, case catalog | **Partial** | DPI P01–P06, P08–P12, P14–P16 reviewed, P07 in part (I15, I19, I40, I44, I48–I51, I53–I55; E-DPI); B04 consent display audited (I17); B05 (I56, I57), B06, B07 (I59), B08 (I09), B09 (I60), B10 (I16), B11, B12 (I61, I62), B13 (I63) and B14 (I64) reviewed; B01 (I68) and B03 source passes; B02 by the damage campaigns (I58, I65); P14 corrected to I09; P07's loader audit (V06) and P13 remain |
| 7 Reporting, signing, dependency approach, preview preparation | Not started | I01/I02/I03/I14/I18 |
| 8 Fixtures and harnesses | Partial | VMware VMs lent and snapshotted (E-ENV-02); the owner's M1 Mac (E-ENV-05); SFTP, FTP/FTPS and SMB servers on the Ubuntu VM (E-ENV-05, one implementation each); consent UI Automation harness (E-I17); Windows Sandbox unusable (E-ENV-01) |
| 9 S10 suites with native setup | **Partial** | E-L01 (Windows lane locally); E-X01 (unelevated Windows 11 VM, Ubuntu 22.04 VM, M1 Mac; CI for every commit) — preliminary |
| 10 High-risk preliminary cases and remediation | **In progress** | V03-PARTIAL (I19), V19-UNINSTALL (I15), V06-CONSENT display (I17) done preliminarily; I20, I21, I22, I23 from test runs; preliminary V19 package checks on Ubuntu 22.04 and macOS (E-V19-P1) |
| 11–13 V01/V12/V13/V16, human V17/V18, remediation loop | **Started** (V13's content search and file comparison) / blocked | V13: content search against an independent corpus (E-V13-S1, I74); file comparison against a generated corpus (E-V13-C1, I81, I82); Find's criteria against a generated tree (E-V13-F1, I83); V01's operation scope (E-V01-S1); V12's counted sizes and churn (E-V12-C1, I85); V16's harness inventory (E-V16-H1); V01's and V12's other parts and V16's acceptance runs not started. Human and reference-hardware work blocked (PPL-01…03, ENV-08) |
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
65. Step 4, first pass (E-R04): the plan's code anchors all exist; each of C01–C29 has a menu or place route.
66. I31: the viewer, comparison, Find, hex editor, report and synchronize windows now have the theme's backdrop under
    their strips, their content on the card color (`99a6ae4`; pictures before and after).
67. I27: under Adwaita 41, whose type icons are symbolic only, files get those icons in the text color (`4a4349f`;
    the icon test passes on the Ubuntu VM where it skipped).
68. B06 reviewed (`34c4b9d`). The Windows VM ran every Windows suite of `ca91908` unelevated (W7, E-X01; its lent
    snapshot has no .NET 10, so a private runtime copy was used): 0 failures; then fuzz rounds 3.1–4.1 M of six
    images. The host's exFAT and NTFS re-runs of 6.1–7.1 M passed.
69. B08 reviewed against V09: **I09**. FileCat scanned a disk that holds its own folders after a warning (my P14 entry
    had accepted that; corrected); it now refuses, names the folders and gives `--data`, which keeps everything
    FileCat writes in one folder. Destinations and folders are placed through links, loop devices, disk images and
    shares served by the same computer; a device scan opens only from the drive's own command, at its chosen size
    (`27256f6`). Live checks on the Ubuntu VM found that an image written into still counted only as its backing
    file's disk (`7418c04`); macOS's `diskutil` answers are kept briefly so the checks take milliseconds
    (`0a52b7b`). All checks pass on Ubuntu, macOS and in the Windows VM (a VHDX on its system disk, `\\localhost\C$`).
70. B05 reviewed: **I56** (FTP data connections followed a routable address a PASV reply named; `ee476f0`, the remote
    lab still passes against OpenSSH, vsftpd and ProFTPD) and **I57** (discovery followed redirects from a device's
    metadata address; `a5c25d1`). Both reproduced by tests that failed before the fix.
71. B11 reviewed (two hardenings, `bc8e2af`, `5b786a9`). B02: a damage test for every archive format (`b0b2329`), 5,000
    rounds of each passed on the host, longer runs on the Ubuntu VM (in memory), the host and the Mac (RAM disk).
72. I09's write traces in the Windows VM (E-V09-T1): the safe topology left the dismounted source unchanged (hash) and
    only read; the system drive got no file of FileCat's (NTFS wrote its own pending metadata as FileCat read it,
    disclosed, wording corrected `deaf776`); FileCat's own files on the source: refused before any device access. The
    runs found a VHDX data disk refused as unknown (`1df5a21`) and the hold-off starting too late (`f241897`).
73. V: (the VMs' drive) fell to 24.7 GB as the Windows VM's change disk grew 59 GB this morning (Windows' own block
    rewrites; nothing large is visible in the guest); a stronger watchdog stops the VMs' fuzz below 15 GB and pauses the
    Windows VM below 8 GB. The host's re-run of the lost fuzz ranges and the Mac's 8.1–9.1 M all passed.
74. I09 on Linux (E-V09-T2): a loop device's image unchanged and only read; the whole system disk scanned with FileCat's
    files and `TMPDIR` in memory got not one disk write from FileCat's processes; FileCat's own files on the disk:
    refused. The trace found a write to `/tmp` at the moment a disk was chosen (a named-mutex lookup), fixed `d39c402`.
    The archive damage campaign's longer runs found TAR and RAR 4 rounds over the allocation budget that did not replay
    alone: the generated archives carried the run's time (fixed `c22c793`); the hunt goes on with replayable rounds.
75. The two archive rounds, replayed: **I58** — TAR round 97053's 512 MiB was .NET's `TarReader` renting the size a
    damaged PAX header gave before reading its data; a guard now checks such headers as they pass (`325aa63`; tests
    failed before, pass after; 20,000 TAR rounds then took at most 1 MB). RAR 4 round 248010's 517 MiB is the most PPMd
    model memory RAR 4 allows (256 MiB, as unrar takes too), rented by SharpCompress as a 512 MiB array: bounded by the
    format, accepted, budgeted. CI had been red on Linux and macOS since `d39c402`: a test asked the usual FileCat's
    pipe at once after its release, before the server stopped (`c5f7387`, checked on the Mac). A million rounds of every
    archive format now run on the fixed build (Mac, Ubuntu, host).
76. V: fell to 10.4 GB again: Windows Update in the Windows VM (a Visual Studio update among it) grew its change disk to
    74.7 GB and restarted the guest. The VM was reverted to its lent state (and once more by the owner); its updates and
    network are now off. Its unfinished fuzz ranges run again (E-ENV-05, E-I28-C1).
77. B07 reviewed (E-DPI): **I59** — a key's rename checked by name that the key was no link and renamed it by name;
    `RegRenameKey` follows a link and renames its target (an experiment on this Windows 11), so a link swapped in
    between could have had an elevated rename applied elsewhere. The key is now renamed through the handle it was
    checked by (`b02a01f`). The rest held: links refused on every open, values changed only while as seen, subtrees
    only while their digest matches, HKCU mapped to the requester's hive before elevation.
78. B09 reviewed (E-DPI): **I60** — appimagetool put whatever type2-runtime's "continuous" release held into each
    AppImage, unchecked; the runtime is now pinned (commit `8f39b89`, SHA-256) and passed explicitly, and the release
    action is used by commit (`84b847a`). Open for the owner (DEC-09) and for I03/I18: no branch or tag protection,
    immutable releases off, no NuGet lock files, a floating SDK and Inno Setup. CI's one red since `c5f7387` was a
    timing test on the ARM64 runner (`9fe6cea`).
79. B12 reviewed (E-DPI): every switch, worker mode and environment variable inventoried and classified. **I61** —
    `--workspace` and `--list` (plan §19.1) were read and forwarded, then ignored; now a named workspace opens first and
    a list file opens as a result set, its network paths left out uncontacted (`dcd81a1`). **I62** — two names for one
    profile's folders ran as two instances (`2cd313f`).
80. B13 reviewed (E-DPI): **I63** — the update check offered to open whatever page GitHub's answer named, through the
    system's association, and showed any tag text; now only a tag that reads as a version and a page among FileCat's
    releases, and at most 4 MiB read (`9bedead`). The log escapes control characters, so a server's text cannot pose as
    records. The archive damage campaign: a million rounds of each of the eleven formats passed on the fixed build (Mac,
    E-B02-A1); CI's red on macOS at `84b847a` was a test that assumed a copy could be watched (`a0a4bf5`).
81. B14 reviewed (E-DPI): **I64** — the file list escaped control and bidirectional characters in names, but a tab's
    title, the path line and the command line's path did not: a right-to-left override in a folder's name made the
    location read otherwise. All three escape now, as do the line and paragraph separators (`e6e9ad0`). With B14 every
    V23 boundary but B01–B03 (largely covered by the DPI rows, the fuzz campaigns, V07 and V10) has had its pass.
82. B02's inspectors got a damage campaign of their own (E-B02-I1): 200,000 rounds of fifteen formats passed; a PE round
    threw (**I65**, fixed `8cb0737`). B03's source pass found the containment as claimed (E-DPI). V09 through UDisks2
    (E-V09-T2, L5): a user who may not read the device got a read-only descriptor from UDisks2 and recovered a deleted
    file, nothing written to the source. On the way, **I66**: files Linux deleted from FAT32 showed as "0 bytes,
    recoverable: the file was empty", their entries cleared by Linux's driver; such entries now say what is known.
83. V09's refused approval on Linux (E-V09-T2, L6): with polkit saying no, nothing was opened or read. The scan first said
    only "Access is denied." because every refusal's reason was dropped by the listing (**I67**, fixed `134db5e`); it
    now says the system did not authorize reading the drive. The archive damage test's generated archives turned out to
    differ between runs and machines (the writing process's ID in PAX headers, native deflate's architecture-dependent
    bytes): made identical everywhere (`c4d81d7`), a failing round keeps its bytes (`cddce72`); one TAR+gzip round on the
    Mac over budget could not be rebuilt and stays open (E-B02-A1). Two recurring CI flakes fixed (`9b734da`).
84. V09 on macOS without administrator rights (E-V09-T3): a disk image the user attached, scanned directly, a deleted
    3 MiB file recovered identical to its original (hash), the image unchanged (hash). Every write of the processes
    (`fs_usage`) and authopen's paths need the owner's administrator rights.
85. B01 focused pass (E-DPI): **I68** (High) — on Linux and macOS a permanent delete reached into a file system mounted
    inside the deleted folder, emptying it; it now stops there and says so (`e5b4e3b`; a unit test and a live tmpfs check
    on the Ubuntu VM). Windows already treated a mounted volume's folder as a link.
86. V24 on the Git route (E-V24-G1): **I69** (High) — a downloaded repository's own configuration sent Git to a
    server while the folder was merely shown (`core.excludesFile` and the four others; 21.1 s per repository against an
    address that never answers, and an SMB session with the server on a packet capture). Fixed `aaee133`: such a
    repository is not read at all, decided from the text of the setting. The icon route of the same charter
    (`.url`, `.lnk`, `desktop.ini`) was then taken under the same capture and **held**: the three fixtures naming an
    icon on the share kept their type icon and contacted nothing, while the three naming one on this computer got it
    during the same run (E-V24-G1-I1). The gpg route **held** too (E-V24-G1-P1): a signature naming a key server, by a
    key the keyring does not have, in a home whose gpg.conf asks for missing keys to be fetched from it — FileCat
    answered "not in your keyring" in 0.1 s and contacted nothing, where a caller that does not pass
    `--no-auto-key-retrieve` sent 8 packets to that server. The launch routes (`SmbTools`, `ToolLauncher`, the Windows
    terminals) were read without a defect (E-V24-G1-S2); the tool route was then measured with a recording program
    (E-V24-G1-T2): fourteen names that mean something to a shell or an option parser each arrived once and unchanged.
87. V24 discovery (E-V24-D1): a damage campaign over the three parsers that read what anything on the network answers
    — WS-Discovery probe matches, a device's metadata, and mDNS answers. A million rounds each on the host: no
    exception, no round over 19 KB, 205 s for all three. The campaign's own CI failure led to **I70** (Medium): a
    Shell picture request that got no answer was remembered as the file having none, so after the helper died those
    files showed no picture for the rest of the session (fixed `3f647bd`, with a negative control). The same memo on
    the icon side kept every icon asked for during a recovery scan (pictures paused, I09) plain for the session
    (`855674a`); and a picture on screen is now asked once more when no helper answered (`c7a02e9`), which is what
    the ARM64 lane's failure needed.
88. V24, I16's process half (E-V24-D1-B1): a folder in which a repository's Git filter, an Internet shortcut and a
    customized folder all name the same program was listed and every icon asked for, under a trace of started
    processes. While the folder was shown, eight processes ran — two `git` runs (the ordinary repository's; the one
    naming a program is not read at all) with their console hosts, and one Shell helper — and **not** the program the
    three fixtures named.
89. V11 secrets (E-V11-S1): sentinel passwords through FileCat's remote stack as the app builds it on Windows, against
    the lab's OpenSSH and vsftpd — wrong three times, then right and saved, then a closed port — with diagnostic
    logging on and every failure logged with its whole exception chain. Saved passwords went to Credential Manager;
    none of the three secrets is in any file FileCat wrote, as UTF-8, UTF-16 or Base64. Found in passing: a
    password answered "save" was stored before the server accepted it, so a mistyped one was retried on every
    reconnect; it is now kept only once accepted (`f9adb51`).
90. V11 state files: **I71** (Medium) — the window layout was the one state file that did not honour plan §19.1. An
    older FileCat set a newer layout aside and then saved over it in its minute's autosave; two saves later no file
    held the newer layout, its backup included. Fixed `b70be07` (the layout is read-only then, as settings and history
    are; a reset does not overwrite it either).
91. Long paths on Windows, at the owner's request (E-V02-L1): every file operation FileCat offers — listing, copy,
    rename, attributes, move, new folder and file, checksum, alternate streams, hex editing, permanent delete — works
    on paths of 330 and 630 characters, on the host (long paths allowed) and on the VM with `LongPathsEnabled` 0, the
    Windows default. The Recycle Bin is the one refusal, Windows' own; FileCat says so and changes nothing.
92. V11 unwritable state (E-V11-S1-F1): a portable copy that cannot write beside itself already fell back to the
    profile and said why (now tested). A save failing during a session was only logged, so changes silently did not
    survive a restart; it is now told once per file per session (`0ade5a1`).
93. Cloud providers, at the owner's request (E-CLOUD-1): OneDrive's, Dropbox's and iCloud Drive's marks never showed
    (FileCat asked the Shell for overlays only inside Git repositories), and their folders were not among the places.
    FileCat now draws the states itself from the attributes a listing has — only in the cloud, on this computer, kept
    here always — for every Cloud Files provider, without running their Shell code or downloading anything, and lists
    the providers' folders with their icons. The owner's own folders showed an "excluded" state (both pin attributes)
    that a first version drew wrongly. Validated: looking at a cloud folder downloads nothing (42 online-only files on
    the owner's OneDrive, all still online-only after listing, icons, metadata, checksum checks, a content search and
    a size count); writing tests in a provider's folder wait for the owner to name one.
94. The application icon, at the owner's request (E-ICON-1): on the owner's dark taskbar at 100% (24 pixels) the new
    artwork's small frames all but disappeared; the 16- and 24-pixel frames are now drawn as pixel art in the
    artwork's own terms (`ecaa254`, with the owner's leave), 32 to 256 untouched. The About box needed nothing. Linux
    and macOS packages now take every size from the icon's frames instead of shrinking the 256-pixel PNG.
95. MTP on the owner's Android phone (E-V21-M1): seven device cases inside `FileCat-test` passed, and a thousand small
    files went up in 31 s and back in 8.3 s; the folder is gone afterwards. The disconnect and lock cases wait for the
    owner at the phone.
96. MTP on the owner's iPhone (E-V21-I1): **I72** (Low–Medium) — FileCat offered F7, renaming and copying onto the
    iPhone, which takes none of them: its storage says read-write, but its driver lists deleting as the only object
    command. FileCat now offers what the driver's commands and the storage's access allow, explains the rest, and the
    device jobs refuse before sending anything (`2e93339`). Checked on the iPhone without changing anything on it;
    reading a photo off it waits for the owner's leave.
97. Fuzz campaigns collected: the host's archive lanes (ZIP, TAR+gzip, gzip 5–6 M; TAR 4–5 M) and inspector lanes
    (PE, PNG, GIF, ELF 1.2–2.2 M), Ubuntu's archive lanes 3–4 M but RAR 4 and all sixteen inspector formats
    0.2–1.2 M, the recovery scanner's 3.1–4.1 M on the Windows VM, 9.1–10.1 M on the Mac and Ubuntu's GPT disk: all
    passed. RAR 4 round 3655801 on Ubuntu was still reading after 60 s and reads in milliseconds alone; not the VM
    stalling, not leftover pool memory, not leaked threads, and not the rounds before it: run again in one process with
    the same settings, all 655,802 up to it passed (E-B02-A1); and on the Mac the whole range in one process passed,
    with ZIP, TAR, TAR+gzip and gzip over 3–4 M, whose originals hash as the host's. The round is recorded as an
    unexplained one-off and repeated in every test run (`71bfd99`). The Windows VM, the Mac and Ubuntu now run ranges
    no machine had run.
98. V24, the file half of I16's gate (E-V24-D1-F1): the kernel's file events while FileCat shows the folder built to
    tempt it. Nothing on the network; the repository that names a program is only inspected by FileCat's own reader and
    never opened by Git; the program the fixtures name is read for its time and its icon, never started. One finding,
    **I73** (Low): the type-icon lookup made the Shell try to open `C:\file.url` from FileCat's own process; the
    placeholder now names no place anyone could fill (`f1b48de`), traced again with and without the change.
99. V13, content search (E-V13-S1): a differential corpus test — files of every encoding FileCat reads, words planted
    at chosen offsets, encodings and read boundaries, each query's answers compared with reading the whole file at
    once. **I74** (Low–Medium): regular expressions matched a read window's edges as the file's (`word$`, `^word`, a
    look-behind), an accent that began the next read was missed, and UTF-8 inside a UTF-16 file was not searched with
    Unicode on; fixed (`b0a2313`): 2/640 and 5, 4, 2/1,920 wrong answers before, none after. The test runs in every lane.
100. The application icon, the owner's second report (E-ICON-1, **I75**): on the lent VM's real taskbar a pinned item
    is the 32-pixel frame shrunk to 24, so `ecaa254`'s redrawn 24-pixel frame never showed; the artwork's dark edge melts
    on a dark taskbar. The frames the taskbar shrinks get a cyan edge, the small frames the artwork's proportions
    (`a9f48cf`); on the VM's dark taskbar it now reads as large as Salamander's.
101. Writing in a cloud folder, with the owner's leave for one test folder (E-CLOUD-1): FileCat's jobs in OneDrive —
    copy in, rename, move, free up space, copy out a file only in the cloud (it downloads), delete, recycle — all as
    on a plain disk. **I76** (Medium): FileCat could not delete the folders OneDrive keeps in sync (their read-only mark,
    which the Shell also sets on customized folders) and blamed Controlled Folder Access; fixed (`edd950a`). The test
    folder and its one recycled file were removed; nothing else touched.
102. The iPhone, reading (E-V21-I1): one photo read into memory with the owner's leave — a PNG, so sizes agree but
    nothing was converted; the phone lists its photos as JPEG (no HEIC), so one JPEG read would show whether converted
    photos come off whole. Asked.
103. The owner's request: the Recycle Bin among the places, beside Downloads, opening Windows' own window, since only it
    restores what it holds (`f9b0c13`): Windows' stock icon, empty or full by the fixed drives' bins; tests with a test
    platform's Shell (nothing opens on the desktop); the exact command run in the lent VM opened its Recycle Bin window.
    A view of FileCat's own (reading the bins' records, restoring) is the owner's decision.
104. CI (`f88300d`): the Registry jobs' tests waited ten seconds for a job and once ran out on a busy runner; they wait a
    minute now, the jobs themselves unchanged.
105. The owner's decision, (b) of three: FileCat's own read-only view of the Recycle Bin (`5a4161b`, E-BIN-1). The
    place beside Downloads opens it in the panel; Windows' own window is its second entry. Both record formats are read
    as untrusted input (a million damaged records: none crashed; the fuzz run found one crash before it shipped);
    deleted folders are entered, files viewed and copied out, nothing outside the bin reached (the test fails with the
    check taken out). Against the Shell's own listing, item by item: the owner's bin 40 of 40, later 38 of 38; the lent
    VM's (Czech) 6 of 6. Restoring and emptying stay with Windows' window.
106. **I77** (Low; `f95e4cd`, E-BIN-1): the view's first look at the owner's bin found a FileCat test's leftovers — 163
    records of its files and two items, one record per run of a test that recycles a file and undoes it on the computer
    it runs on. The test now removes its own items whatever happens, FileCat's undo removes the restored item's record,
    and the leftovers were removed from the owner's bin, only those. Checked afterwards on the lent VM: Windows' own
    Restore leaves that record too (three runs), as does emptying the bin with such records in it; the fix's comment and
    commit message had said otherwise, unchecked, and the records and comment are corrected.
107. V21, the cable pulled mid-transfer, with the owner at the phones (E-V21-U1): seven pulls, two on the iPhone copying
    photos off it (with the owner's leave for up to 50, deleted afterwards), five on the Motorola inside `FileCat-test`
    (one copying onto it, four off it). Nothing half-written was published or left on a phone, nothing hung, and Retry
    finished every copy whole. **I78** (Medium, `7ee8e92`): every pull during a copy off a phone was reported as the file
    "no longer exists": .NET raises the phone's "not found" as `FileNotFoundException`, which FileCat's device handlers,
    catching `COMException` only, let through; listings also passed a cut answer off as the folder. A trace of each step
    on the fourth pull found it; the fifth, after the fix, said "disconnected" and passed. **I79** (Low–Medium, same
    commit): after a reconnect the iPhone sends seven of the 50 photos with other bytes at the same size, which the check
    before resuming (the 64 KiB before the break) could not tell; the file's start is compared too now. The JPEG question
    of item 102 is answered: the iPhone sends its converted JPEGs at exactly their listed sizes. Locking a phone
    mid-transfer is not done. Suites: Core 719, Windows 159, App 206, Remote 116; 0 failed.
108. The owner's request (low priority): tooltips over icons styled by the selected theme (**I80**, `fab03b8`): tooltips
    take the theme's surface, solid, with contrast checked in every theme; icon buttons' tips laid out (title, key,
    description, how else used). App suite 209, 0 failed; the owner: "tested it on my own, works".
109. The fuzz campaigns collected: recovery (E-I28-C1) the Mac's 10.1–11.1 M of all seven images, the Windows VM's
    4.1–5.1 M of FAT12, FAT16, exFAT and both disks, and Ubuntu's MBR disk over 100,000–1,099,999 **passed**, so every
    image is now covered from 0 to 4.1 M and from 7.1 to 11.1 M, and between them as the campaign table lists (FAT32's
    4.1–5.1 M still running on the VM); archives (E-B02-A1) Ubuntu's 4–5 M of the seven formats it runs **passed**,
    nothing kept (RAR 4's 516 MB round is the PPMd model, recorded and accepted). Outputs copied off every machine and
    checked by hash; the fuzz archive's manifest grows from 64 to 80 files.
110. V13, file comparison (E-V13-C1): a generated corpus checked against references written in the test (20,000 text
    pairs, 2,000 aligned binary pairs, 20,000 positional runs past the list's limit). It found **I81** (Low–Medium,
    `db2e9b4`): one coincidental unique line misaligned a text comparison, an 11-line edit shown as 84 lines, presented
    as exact; regions up to 20,000 lines are now aligned exactly, larger ones labelled heuristic unless provably the
    best; and **I82** (Low): the summary's count disagreed with the list. Speed unchanged; Core 722, App 209.
111. V13, Find's criteria (E-V13-F1): a generated tree and random queries (masks, subfolders, hidden, sizes, times,
    attributes, ignored folders) against a reference written from the criteria's documentation: 15,400 queries, about
    293,000 results, all as meant; a mutation of the size bound is caught. Beside it **I83** (Low, `c67fa85`): a saved
    time range shown again in Find's dialog lost its end day's last minute. Core 723, App 213.
112. V13, the rest of the search and comparison: a runaway regular expression times out, says so, and the search goes on
    (`0b52e70`); directory comparison read for letter-case collisions and precision found **I84** (Medium, `bc65646`): of
    two names differing only in case one was dropped unseen, and a size or time a listing does not give counted as the
    same. One pairing rule and an undecided state for both comparisons; 3,000 random folder pairs against a reference.
    Core 728, App 213.
113. V01 begun (E-V01-S1, `49570df`): through the window, a copy waiting on a conflict keeps exactly its marked files and
    planned destination while files arrive, the target panel moves, the panels swap and a panel is added (direct
    manifests before and after); a mark the filter hides is said in F5's dialog and copied only while included. Passed
    as written; nothing found.
114. V12 begun (E-V12-C1): how a folder's size count is applied, read for "results never land on replacements", found
    **I85** (Low–Medium, `1ec9d13`): a folder deleted and made again, or replaced, while counted took the first one's
    size as counted. The count now compares the folder's file-system identity at its start and end. App 218. A folder
    churned with 12,000 changes at full speed ends as the disk is, marks and cursor kept (`4a156b5`); partial sizes are
    drawn with "…".
115. V16 begun (E-V16-H1): the harness inventory §9 asks for (what each benchmark measures, asserts or only prints)
    and preliminary runs on this machine, the historical regression profile, not the reference: comparison, search and
    archives within every asserted budget. Gaps before acceptance: ready-for-input and OS-input-to-present latency are
    not what the window's benchmark measures; no harness for the shared content cache. Huge hex got one (`6df923b`):
    a 4 TiB sparse file and 2 GiB of data through the viewer and the editor, first page at most 1 ms warm and seek p95
    at most 1.33 ms here (budgets 250 and 100 ms). Large copies got one (`5abf5b2`): against CopyFile2 with the job's
    own profile (a first run against a buffered CopyFile2 measured the write cache and is not a comparison), the
    median of five pairs was −0.3% to 19.6% over three runs on the 990 PRO; the job's own work outside the copy engine
    is 12–18 ms per 4 GiB job, and the spread is the disk's. ReFS cloning and SMB server-side copy are not covered:
    the check is written (`CloneCopyTests`, gated; a VM script making a ReFS volume and a share of it), but the lent
    Windows VM no longer starts from cold (ENV-05) and reverting it waits for the owner.
116. I06 (E-I06-P1, `61b028f`): every view's page cache was bounded by its own limit only, so the views open together
    were not: five viewers, three hex editors, two comparisons and the quick view held 148 MiB. Now one budget, 64 MiB
    by default (`ContentCacheMiB` in the settings file), shared by every reader: the least recently used page of all
    goes first, and each reader keeps its four most recent pages. Measured at 64 MiB for the same views; tests for the
    order across readers, the floor, disposal, collection and concurrent use, with three negative controls. This meets
    the plan's target, so DEC-06 (keep it or approve a change) is closed; the owner can still set another limit. I06
    stays open for other memory that grows with what is open (V12).
117. CI's red runs of the last day, each traced: the Shell-picture failure on ARM64 is I70; the recording-program test
    (`3a291e6`'s run) and the Registry jobs' wait (run 36904910785) were made robust in `a933ed6` and `f88300d`;
    the operations panel's "Clear finished" (run 36931084621, a records-only commit) was a real race, **I86** (Low,
    `506cc75`): clearing went by each row's state, the count by each job's. A test that holds the window's thread while
    a job ends fails on the old code.
118. I06, archive indexes (E-I06-A1, `6b37c41`, `319a38c`): both archive providers kept the last eight archives'
    indexes by count alone; one index of a million members holds 557 MiB (ZIP) or 291 MiB (TAR), measured. Each index
    now estimates its size (553 and 290 MiB for those), earlier archives are kept within 256 MiB per provider, the
    least recently used first, and the two used last stay whatever their size (two panels). A member being read keeps
    working when its index goes. Negative controls. I06 stays open for decoded pictures, icon caches and other
    materialized lists (V12).
119. V12, the watcher (E-V12-W1): forcing an overflow found **I87** (Medium, `3d2bb2e`): while a folder kept changing,
    the watcher's debounce put its reread off for as long as the changes went on (a file every 50 ms for six seconds:
    no reread until after the last; 100,000 changes in 30 s: none). Now every two seconds while changes come, and one
    after. Overflows of the system's buffer are counted and logged: unhindered, this machine's watcher kept up with
    100,000 changes; held up 2 ms a notification, 20,000 changes overflowed it four times, and a reread followed.
120. V12, counts and their tab (E-V12-C2): **I88** (High, `a9a9dcf`): a folder's count went on after its tab closed and
    posted to the disposed listing four times a second; each post threw on the window's thread, and the crash guard
    ends FileCat past five in three seconds (24 in five seconds measured, closing a tab 0.3 s into a 6-second count).
    Leaving the folder kept the tab "counting" in the next one. Now a count ends with the tab's stay in its folder;
    the same experiment raised none. The rest of the window's posted work, read for the same mistake: two paths loaded
    a closed tab's listing again (no exception); a disposed listing now ignores Load (`34042cc`).

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

1. The fuzz campaigns are collected (item 109; FAT32's 4.1–5.1 M and `pe-managed`'s 200,000–1,199,999 passed last).
   Both VMs are shut down with the owner's leave ("when you do not need VMs anymore, you are allowed to shut them
   down"), their snapshots kept; the V: free-space watchdog that guarded them is stopped.
1a. Continue V24: the terminal and association routes as the user drives them from a window; the same cases on a
   candidate's installed files. (`.lnk` targets on a share held, E-V24-G1-I1.) Done so far: the Git, icon and gpg
   routes (E-V24-G1), the tool route with a recording program (E-V24-G1-T2), the discovery parsers, and the process
   and file traces of browsing (E-V24-D1, E-V24-D1-F1).
2. V09 on macOS: `fs_usage` and authopen (the owner's administrator rights); the installed
   helper path, device removal; approval refusal on Windows (UAC; the lent VM elevates without asking).
3. Continue the V23 source review: B01–B03 (largely covered by the DPI rows, the fuzz campaigns and V07/V10); I16's
   independent file, network and process evidence.
4. I42's options for the owner (fewer requests per file; several files in flight), when the owner wants them.
5. Keep the records current after each change.

Waiting on people, hardware or a candidate: DPI P13's remaining case (locking the phone mid-transfer, with the owner; the disconnect cases are done, E-V21-U1); P07's loader audit (V06, installed
candidates); I09's device-level zero-write cases (the USB test drive, which is not plugged in); I04 on Ubuntu 26.04
(that system); steps 2, 5, 7 and 11–26 of the plan.
