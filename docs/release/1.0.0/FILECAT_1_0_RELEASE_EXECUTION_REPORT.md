# FileCat 1.0.0 — release execution report

Operational plan: [FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md](../../design/FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md)
(with its review disposition). Companion records: [issue register](FILECAT_1_0_RELEASE_ISSUES.md),
[evidence index](FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md), [open blockers and decisions](FILECAT_1_0_RELEASE_BLOCKERS.md).
Candidate-specific evidence will live in `docs/release/1.0.0/<candidate-id>/` once a candidate exists.

## Current state (updated 2026-10-04)

Execution resumes after the owner restarts Codex elevated. Computer Use still exits before selecting a
window; one retry reports Windows sandbox setup refresh errors. The tool shell is still unelevated.
Independent V12 work reproduces four direct viewer-source close failures and verifies a working correction
(E-I128); affected/full host suites, four clean c453925 CI jobs and 143 SDK-free guest controls pass. All
143 affected Windows/41 Unix App cases pass without affected skips. Both VMs remain running. The owner again
authorizes the connected G: USB; only identity inventory is queried, no mutation, prior source-change gate held. No candidate or human GO exists; overall NO-GO.

- **Readiness: NO-GO.** No candidate, release tag, signed artifact, final qualification or human GO exists.
  Work remains in preliminary validation and remediation; the historical source baseline is retained below.
- **Resumption input:** clean `main` at `08c2e2dee4e04c936a34cd867770d05d758af686`, with prior work through I95.
  The seven existing planning documents were preserved and pushed unchanged as `3316f15` with owner authorization.
  Current validation changes and exact build/harness identities are in E-ENV-06/07, E-I96/98, E-V03-CLONE-1 and E-V19-P2.
- **CI:** all four lanes passed at `08c2e2d` and `a5a3c0c`; the intervening documentation commit's ARM64 lane failed
  on a transient native Recycle Bin query (I96). Its bounded test remedy `5503262` and UNC fix `ca1afe0` pass all four
  lanes. Manual run 36994087185 at d14199b passes all four lanes and Linux/macOS package jobs; development packages
  are under native validation (E-V19-P2). Later CI through fa3a02a passes all four lanes. Manual run 37036698071 at
  exact fa3a02a also passes Linux/macOS packaging; dev.539 bytes/hash provenance retained for both clean baselines.
  I105 at 1cd803c also passes all four lanes and manual development packaging (37055272672); dev.545 retained,
  strict Unix inventory independently verifies 35 pass/four skips per lane. I106 ecd61f3 and successor ea4a2ac
  pass all four lanes; ea4a2ac manual 37068909015 also packages dev.549. Strict Unix 47 pass/four skips per lane.
  Checker 2e6dffe and observed-manifest successor 85bb17d both pass all four CI lanes. Direct Windows inventories
  independently verify all thirteen new checker cases; package jobs skip (E-V09-G2).
  Guard 1883eb4 and successor 090a2b6 also pass all four lanes. Later b5ce744/a50b3b8 documentation/title inputs
  fail only the Windows synthetic lease helper readiness; an older bb2d748 macOS run exposes a timed progress
  observer race. I108 synchronization correction 6cad380 passes all four lanes in run 37119313116, with original
  failures and successful Windows archive/affected-case inventory retained (E-I108).
  I109 cfcc9e6 and documentation successors 087917d/f2f0141 also pass all four lanes; exact metadata/logs retained.
  These results do not qualify release packages or replace
  the candidate's skip inventory.
- **VMs:** guest access works after owner clarification (E-ENV-06). Windows Insider 26300 was gracefully shut down
  after completed copy cases, as authorized. Fresh Ubuntu 24.04.5 and 26.04.1 full desktops each have a powered-off clean
  baseline snapshot and actual GNOME Wayland session (E-ENV-07); current guest is 26.04. Preliminary 24.04 packages/native
  suites pass with recorded skips. Unmodified dev.526 Debian install on 26.04 fails on
  ICU dependency choices (E-I04); producer corrected and rebuilt 26.04 install/desktop/lifecycle pass. 26.04 native suites
  pass with recorded skips after native setup correction and I99's socket-path remedy. Separate-session GUI launch
  reproduces I100; profile-local lock/actual endpoint remedy passes native process, GUI, FAT/exFAT and affected App
  checks. I101–I103 affected native/process checks and CI pass. Final 26.04 raw archive independently hash-verified,
  owned Samba/loop fixtures cleaned before restoration. Dev.539 package/desktop/lifecycle, long temporary-path and
  session-forwarding checks pass on both restored clean SDK-free baselines; both raw archives independently verified.
  Continued audit exposes I105: a portable probe misses per-user fallback owners and portable profile roots.
  Hash-bound working fix passes ordinary/independent native GUI and Windows affected checks (E-I105), then all CI
  lanes. Separate portable installation exposes I106; corrected process guard passes ordinary/--data native GUI
  and CI checks. Dev.549 all three Linux formats pass successor checks on SDK-free Ubuntu 26.04 (E-V19-P3).
  Wider audit reproduces a renamed apphost missed by the name-only census; executable-identity correction under
  validation, refined root native census and App checks pass; ordinary-account visibility/races remain open
  (E-I106). I107 native trace identifies unchanged theme rebuilding the open menu; palette guard,
  affected App/CI and owner host check pass. Owner confirms clean 1a9f1ba guest menus; I107 is preliminarily closed,
  while exact-candidate interaction remains pending. Owner restored the
  Windows snapshot and guest access works; keep both VMs running. Computer Use runtime remains unavailable:
  updated 26.930.41038 import and separate plain JavaScript startup both crash before input (E-I126).
  Full recovery write-location audit continues. GA Windows/reference hardware remain open.
- **Physical recovery:** authorized USB host preflight and FAT32/exFAT/NTFS component runs pass at clean 1df5dff,
  325 generated deleted files recovered exactly per filesystem. Byte-checker audit exposes false acceptance beside
  missing ranges; corrected checker passes thirteen controls and both affected CI runs. Clean 85bb17d includes
  observed-file hash capture; its native checker/guard inventory passes 27/27. The stronger attempt overlaps an older
  campaign and is invalidated. Interprocess correction and exclusive clean 090a2b6 successor pass controls and
  physical preflight 1/1 plus formats 3/3 without skips. Each format recovers 325 generated deleted files exactly;
  all complete claims and recorded input/output hashes independently verify (E-V09-G3).
  Installed-helper/source-write tracing and I106 production census availability remain open.
  Pinned Windows tracer positive-read/write and untouched-path controls now pass; full PML/CSV and independent
  file/event/configuration verification retained. This control does not access the USB (E-V09-G4).
- **Native copy case:** guarded identity-bound ReFS/Dev Drive and same-server SMB harness added (`a5a3c0c`, `2a58fdb`,
  `021a885`). Local 1 GiB copies pass the clone-space, SHA-256 and copy-on-write checks. The first SMB run stopped
  before copying because its UNC volume root lacked the trailing separator (I97, fixed `ca1afe0`). Corrected local
  and SMB cases each pass 1/1 without skips; all three copy paths pass bytes/copy-on-write and use 0 reported MiB.
  Successful VHDX/share cleanup independently checked. Candidate reruns remain mandatory.
- **Prior campaign status:** fuzz campaigns collected (work log item 109); Windows and Linux recovery write traces
  are recorded (E-V09-T1/T2). macOS/installed-helper/device and candidate cases remain open. The issue register,
  rather than the historical checkpoint below, controls current defect disposition; non-Closed issues still block GO.
- **Controls and people:** private reporting, release protections, signing, retained REP storage, support decisions
  and human validation/GO gates remain open. No release controls were changed and nothing was published.

## Historical checkpoint (2026-10-01; superseded by the current state above)

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
| 1 Refresh baseline | **Done** | Historical E-ENV-04; resumption at the progressed campaign source, CI and guest access refreshed in E-ENV-06 |
| 2 Owners, resources, provider/licence preflight | **Open (people)** | DEC-01, DEC-07, EXT-01, EXT-02; resource status in the blockers file |
| 3 Collect CI/validation evidence and skip inventory | **Done (preliminary)** | E-A01 (TRX lanes), E-A02 (every lane from the log, reasons from source; 37 tests run on no lane, all gated; the ARM64 lane's missing Remote tests added, `98bc539`); early-return audit (E-S01, `be6ca25`). To repeat on the candidate's run |
| 4 Reconcile manifest and registers against source | **Partial** | E-R04: every code name the plan's rows cite exists (137 in 421 rows; 8 rows explained), every capability has a route; whether each claim holds is left to the V cases |
| 5 Contract questions (I05, I06, PSD, Mac, FDD, I14) | **Open (owner)** | DEC-02…DEC-05, EXT-02; I06's page caches meet the planned shared budget (`61b028f`), so DEC-06 is closed |
| 6 V23 source review, test-guard audit, case catalog | **Partial** | DPI P01–P06, P08–P12, P14–P16 reviewed, P07 in part (I15, I19, I40, I44, I48–I51, I53–I55; E-DPI); B04 consent display audited (I17); B05 (I56, I57), B06, B07 (I59), B08 (I09), B09 (I60), B10 (I16), B11, B12 (I61, I62), B13 (I63) and B14 (I64) reviewed; B01 (I68) and B03 source passes; B02 by the damage campaigns (I58, I65); P14 corrected to I09; P07's loader audit (V06) and P13 remain |
| 7 Reporting, signing, dependency approach, preview preparation | Not started | I01/I02/I03/I14/I18 |
| 8 Fixtures and harnesses | Partial | VMware VMs lent (E-ENV-02/05/06); the owner's M1 Mac (E-ENV-05); two SFTP/FTPS implementations and Samba on the Ubuntu VM (E-V08-L2; current server availability not revalidated); consent UI Automation harness (E-I17); new guarded ReFS/SMB clone harness (E-V03-CLONE-1); Windows Sandbox unusable (E-ENV-01) |
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
    a closed tab's listing again (no exception); a disposed listing now ignores Load (`34042cc`). A comparison, a
    viewer, a hex editor, a search and the quick view closed while they work raise nothing (`f2b850b`; the same test
    sees I88's exceptions with the old count code). The same mistake in View → Analyze folder: **I91** (Medium,
    `800cd52`): an analysis outlived its folder, labelled and re-sorted the next one, or failed reading a closed tab's
    listing; now it ends with the tab's stay.
123. V12, large listings (E-V12-L1): a million synthetic entries list their first rows in 71 ms (names of 29
    characters) and 72 ms (240 characters), complete in 1.5–1.7 s; four such listings at once show rows at 621 ms.
    Long names cost spill space: 503.5 MiB of temporary disk for a million 240-character names. Regression profile.
    Type-to-find walks the names on the window's thread at each key: a miss over a million names holds the window
    43 ms to about 0.6 s (**I92**, Low, open). Many tabs (`1cf3af5`): forty tabs hold two watches (the active tab of
    each panel); a background tab catches up with its folder when it is active again.
121. CI run 36941532909 (red on a records-only commit): **I89** (Low–Medium, `2ad2cfa`): the change journal reader went
    on from the new oldest entry only once when the journal wrapped during a read; a busy runner wrapped it twice and
    the read failed. Now as often as needed, said in the view; tests with a stand-in journal fail under the old rule.
122. V13, result sets (E-V13-R1): refine and append read in the code (identity leaves size and time out; keep and
    remove matching search within the found items; append adds only new references), and **I90** found (Low–Medium,
    `54c33de`): a root typed in another letter case gave other items for the same files, so appending listed them
    twice. Searches now walk their roots as the disk spells them. Duplicates: **I93** (Medium–High, `bf395c6`): two
    names of one file (a hard link, a path through a junction) were grouped as copies, so "all but one" could mark the
    file itself for deletion; now names of one file count once, by file identity, and links are left out.
    Synchronize: **I94** (Medium–High, `65cee78`): folders inside each other (also through a junction) were offered,
    and Mirror toward the outer one removed the source; now not offered for them.
124. V15 begun (E-V15-G1): OpenPGP checked against keys and signatures an independent GnuPG made. **I95**
    (Medium–High, `cd37342`): with no trust line from gpg (trust-model always in gpg.conf), a good signature read as
    good for any key in the keyring; now as signed by a key gpg did not vouch for. No key server is contacted even when
    gpg.conf asks (a listener in its place saw nothing). minisign not covered (no independent tool here).
125. Execution resumed on 2026-10-02 (E-ENV-06): all four authority documents and the launcher-required planning
    prompt read in full; progressed campaign source verified; the seven planning documents retained unchanged and
    pushed (`3316f15`). Both VMware guests are running; owner-corrected Windows credentials work. Ubuntu remains
    22.04.5 and Windows Insider 26300. Reporting/release controls remain disabled/unprotected; no settings changed.
126. **I96:** documentation-only CI `3316f15` failed on ARM64's first Recycle Bin query (`0x800700B7`); the same
    test passed in CI at `a5a3c0c`. `5503262` retries only that response up to four times, 50 ms apart; persistent and
    other errors still fail, no new skip. Targeted test and full four-lane CI pass (E-I96); native cause unconfirmed.
127. ReFS/SMB preliminary copy validation resumed (E-V03-CLONE-1). The old fixed-Q:/fixed-share script was not run.
    New harness binds a unique VHDX to its disk/partition, rejects wrong guest/bundle controls, retains failed
    fixtures, and checks identity for format/cleanup. Windows PowerShell CIM bus-type handling and Storage cmdlet
    error propagation were corrected after safe setup failures. The local Dev Drive case passes all three copies,
    byte hashes and copy-on-write checks. **I97:** SMB's native query failed before copying because its UNC root
    lacked the required trailing separator; independently reproduced (123 without, success with it), fixed
    `ca1afe0`; corrected local/SMB runs each pass 1/1 with byte/copy-on-write checks and 0 reported MiB extra space
    for all three paths. Cleanup independently checked. No product copying defect demonstrated by the setup failures.
128. Owner authorizes updating/reinstalling the lent Ubuntu VM for the required 24.04/26.04 desktop matrix; its
    existing rollback snapshot is sufficient and its current contents need not be preserved. Fresh installs will
    be used sequentially, with OS snapshots for repeatable tests. Canonical checksum signatures have been verified
    against the documented CD-image signing fingerprint; ISO downloads/provisioning and Linux package checks remain
    in progress, not qualification evidence yet.
129. ENV-04 provisioning begun (E-ENV-07): Ubuntu NAT restores internet access; exact VMware UUID and sole 200 GiB
    SCSI disk identities recorded. Canonical desktop ISO signatures and hashes verified on host/guest. Guarded media
    built for 24.04/26.04; positive identity check and four negative controls pass. Installer selection corrected to
    use udev ID_SERIAL before any boot. Manual CI `36994087185` at `d14199b` passes all four lanes and Linux/macOS
    packaging; no tag/publication. Fresh installations and package/native validation remain in progress.
130. Fresh Ubuntu 24.04.5 installed and baseline snapshot retained before FileCat or test tools, GNOME Wayland/XWayland
    confirmed, no SDK present (E-ENV-07/E-V19-P2). Development .deb installs and displays the app; ordinary GUI Unicode
    copy passes byte/hash checks. Owner explicitly authorizes disabling idle lock/blanking and unlocking this disposable
    VM after auto-review requested specific security authorization. Completed Windows VM tests retained; guest shut down.
131. **I98:** actual tar desktop launch fails when its folder contains ampersand; unescaped sed replacement corrupts
    Exec/Icon. Independent native GLib/argv/icon harness passes 4/12 before and 12/12 after the separate escaped helper,
    including quotes/percent/newlines/Unicode (E-I98). Linux CI coverage added; rebuilt packages/26.04/candidate pending.
132. I98 fix pushed as ecf5349; push CI 36999606344 and manual CI 36999624175 pass all four test lanes. Manual Linux/macOS
    package jobs pass; dev.526 hashes retained (E-V19-P2). Rebuilt tar opens normally from ampersand/percent/Unicode path.
    Debian remove/purge/reinstall/update preserves state and unrelated install files; normal AppImage FUSE GUI works.
    Native FAT32/exFAT record test passes 1/1 without skips. Remaining native suites and 26.04 matrix continue.
133. Fresh 24.04 native suites complete (E-X02): Core 702/741, Remote 94/116 and App 203/230 pass, all remaining cases
    explicitly skipped. Required native keyring/WebKit/Samba/GVfs/FAT cases pass; readelf and Windows-formatted recovery
    corpus checks replace prerequisite skips. Initial keyring setup caused an aborted run; corrected isolated native
    service yields a full Core pass, with all failed inputs retained. Tar/AppImage corrupt-state and restart checks pass.
    Owned loops detached, test Samba stopped and raw evidence transferred/hash-verified on host before 26.04 overwrite.
134. I03's Windows inventory filename collision remediated while 26.04 installs (E-I03-RID): per-RID filenames retain
    both native package-list JSON outputs. Full artifact/native/runtime/helper SBOM and source/license gates remain open.
135. Fresh Ubuntu 26.04.1 full desktop and actual Wayland session verified; private installer evidence retained and
    clean powered-off baseline snapshotted (E-ENV-07). Existing dev.526 Debian install exits 100 because only ICU78
    is available. I04 reproduced and producer adds libicu78; rebuilt 26.04/24.04 verification pending (E-I04).
136. I04 remedy cc97a8d passes all CI lanes and manual Linux/macOS packaging (37015434145). Exact dev.531 Debian
    dependency/CLI/desktop/lifecycle cases pass on 26.04. Tar desktop GUI/native Unicode copy and normal FUSE AppImage
    pass; all rebuilt formats preserve corrupt settings and restart state, and packaged helper passes 12/12 native
    path cases (E-V19-P2). Private SDK/native prerequisites added only after clean-package cases.
137. Fresh 26.04 native Core 703/741 and Remote 94/116 pass with explicit skips; actual Secret Store/Samba/GVfs
    branches required. Root FAT32/exFAT 1/1 and Windows-formatted recovery corpus 3/3 pass. Initial private-bus GVfs
    setup failure retained; isolated runtime/keyring rerun passes (E-X02).
138. Full 26.04 App run reproduces I99 under a valid 73-byte TMPDIR: two socket address exceptions. Hash-bound remedy
    passes boundary/long/Unicode fresh-process checks and full App rerun 204/231 with 27 skips. Actual same-session
    GUI forwards/persists tabs. A separate-session launch exposes I100's Local mutex scope and socket replacement;
    retained independently, not counted as a pass. CI/rebuilt package validation and I100 remediation continue.
139. I99 pushed d38f915; CI 37024802245 passes all four lanes including the native Unix boundary harness. I100's
    production API harness reproduces distinct-session/cross-TMPDIR failures (1/5). Profile-local native lock and
    published actual endpoint remedy passes 6/6 cases, including two case-sensitive data folders, and 14 boundary
    tests with one expected skip. Affected native App 205/232 passes with 27 skips. Actual GUI across sessions/TMPDIRs,
    FAT32/exFAT state locks and native syscall oracle pass (E-I100). Setup/assertion failures retained separately.

140. I100 pushed 4746592; all four CI lanes pass (37030149306), including native Unix boundary/process harnesses on
    Linux and macOS. The continuing write-location audit reproduces I101: runtime TMPDIR omitted from recovery.
    Own runtime folder and published running-instance socket/runtime locations now guarded; invalid metadata refuses.
    Native before 1/1 fails, fixed guards 5 pass/1 expected skip, boundary 17 pass/1 expected skip, process 6/6 and full
    App 206/233 with 27 explicit skips pass. Both required WebKit cases pass. Windows controls 3 pass/3 Unix skips.
    Exact inputs, failed setup/build attempts and native results retained (E-I101). CI/rebuilt packages remain pending.

141. I101 pushed 369f55f and all four CI lanes pass (37032428169). Actual --new-instance Ubuntu GUI is missed by
    the recovery probe, and another-profile guard regression fails: I102. Independent lifetime locks/metadata stay
    in guarded state folders; all usual profiles checked. Fixed actual GUI is detected, then closed/cleaned correctly;
    8/8 process cases include last-owner/crash and normal-owner forwarding. Boundary 23 pass/1 skip, full App 208/235
    with 27 explicit skips, native paths 13 pass/1 skip. Windows guards 5/8, paths 13/14 with platform skips.
    Intermediate native/Windows failures retained and remedied (E-I102). CI/rebuilt packages/write tracing pending.

142. I102 pushed 5b69fba; all four CI lanes pass (37034741307). Windows profile case aliases reproduce a missed
    owner and portable/usual states collide (I103): regression 1/1 fails, process baseline only 1/4 passes. Actual
    state-directory identity remedy passes 4/4 process cases and Windows guards 6 pass/3 Unix skips; Ubuntu native
    boundary guards 23 pass/4 explicit skips. CI process harness/records added. Source/payload/results retained (E-I103).
143. Owner's Windows title report queued as Low-priority I104. Later clarification: the repeated word comes from the
    selected directory being named FileCat and is valid. Requested order is FileCat first, then selected path/location,
    then username/elevation. Both reported titles retained; no title code changed during safety validation.
144. I103 pushed fa3a02a: all four push CI lanes pass (37036329081). Manual run 37036698071 binds that same source,
    passes all four lanes and Linux/macOS package jobs; dev.539 Linux bytes downloaded/hashed (E-V19-P2).
    Windows tag-only packaging skipped; no publication or release controls changed.
145. Final Ubuntu 26.04 raw/native archive retained on host with matching guest SHA-256, 1,093,581,374 bytes and
    7,705 members. Independent streaming verifier checks 14 required TRXs/payloads/immutable fixture hashes,
    passes without missing/mismatched files. All failures/nonpasses retained; SDK download/source/selected built
    payloads retained separately from excluded extracted build/runtime trees. Identity-checked test-owned Samba
    stopped and three owned loop devices detached; immutable FAT16 bytes unchanged (E-X02). Snapshot restoration
    can now proceed without losing this evidence.
146. Restored clean Ubuntu 24.04 baseline; identity/pinned SSH/no FileCat/no SDK preflight passes. Exact dev.539 .deb,
    tar and normal FUSE AppImage pass GUI/corrupt state, 203-byte TMPDIR, separate-session Unicode-TMPDIR forwarding,
    saved workspace/restart and clean socket/lock/mount release. Debian/tar native desktop entries pass; F5 copy of
    the Unicode fixture matches 45-byte independent hash. GLib helper oracle 12/12; Debian remove/reinstall/purge
    preserves user state and unowned sentinel. Setup/oracle failures retained, no product remedy for them. Full
    324,638,274-byte archive copied/hash-verified and independently checked before shutdown/restoration (E-V19-P2).
147. Clean Ubuntu 26.04 restored via VMware-bound SSH key; identity/no FileCat/no SDK preflight passes. Exact dev.539
    repeats pass: 3/3 native GUI formats with long TMPDIR/forwarding/corrupt-state/restart, normal FUSE, both native
    desktop routes, Unicode F5 copy, 12/12 GLib helper cases and Debian lifecycle/state/sentinel preservation. Loaded
    ICU78 verified. Full 255,953,184-byte archive retained/hash-verified; independent verifier confirms exact inputs,
    copied bytes and result/state records. Both fresh Ubuntu package matrices now pass preliminarily (E-V19-P2).
148. Exact fa3a02a manual CI raw process artifacts retained: Windows 4/4, Linux/macOS 8/8 each; strict boundary runs
    pass all three temporary-path scenarios on each Unix lane. macOS dev.539 archive retained with exact byte/hash
    provenance; CI startup does not replace signed/native-desktop candidate qualification (E-I102/E-I103).

149. I105 reproduced on exact dev.539's SDK-free Ubuntu GUI: root-owned unwritable Data makes the window fall back
    to per-user state; independent lease busy, same-base probe false. Three Windows baseline regressions fail.
    Read-only portable/per-user candidate and profile-root discovery remediated; only actual live roots guarded.
    Native working-overlay ordinary/independent windows pass 2/2; Windows App 225 pass/15 skips, final guards 10
    pass/3 Unix skips and paths 13 pass/1 Unix skip. Full before/after archives independently verified (E-I105).
    CI/rebuilt packages, wider discovery and candidate/device tracing pending; NO-GO remains.

150. I105 all four lanes and development Linux/macOS packaging pass at exact 1cd803c (37055272672); raw dev.545
    packages/CI artifacts retained, each strict Unix result independently confirms 35 pass/four skips. Broader
    audit reproduces I106: second portable GUI PID 12176/lease busy, own-base probe true, other-base probe false.
    Native before bundle retained. Device gate gains read-only process census: Windows baseline guards 1/3,
    working guards 14/17 and final App 229/244 with explicit skips; native after/CI/rebuild still pending (E-I106).
151. Owner's Windows host menu failure persists after host tests finish (I107). Copied Program Files App DLL matches
    current working-build bytes; version stamp 1cd803c includes I106 working inputs, not proof of a clean tree.
    Live host test authorized, but Windows Computer Use initialization crashes twice and after reset/retry;
    no window selected or input sent. Cause unproved; preserve this as a UI-connection gate (E-I107).
152. I107 raised to High/must-fix at the owner's instruction. Same symptom reported in the running Windows VM and
    after Codex restart/compatibility launches. Headless menu press/release checks pass 1/1 with/without opt-in trace.
    Initial isolated diagnostic GUI execution is not established: state/log absent, although both version probes
    return 0. Startup capture and native diagnostic payload in preparation. Exact earlier fa3a02a/08c2e2d archives
    publish successfully for the owner's requested comparison; no native menu result or fix claimed (E-I107).
153. I107 second diagnostic and exact fa3a02a/08c2e2d comparisons staged and hash-verified in the Windows VM.
    Launchers use isolated state, invocation markers and pinned-runtime stderr capture; owner clicks requested
    because the Windows automation runtime cannot start. Full affected App regression passes 230/245 with 15
    explicit platform skips and no failures. Source/payload/test identity retained; native result pending (E-I107).
154. I107 owner-operated VM trace identifies repeated unchanged theme application removing the open menu;
    popup detaches/closes while the main window remains active. All three comparison GUIs started/exited 0;
    earlier failures are owner-observed. Two new regressions fail on baseline, then pass after the palette guard;
    targeted menu/theme/tooltip checks 14/14, no skips. Remaining App/native after/CI pending. Automation retry
    after Claude closes still fails, now with specific Windows sandbox setup refresh errors (E-I107).
155. I107 palette guard's disjoint targeted/remainder runs cover all 248 App cases: 233 pass/15 platform skips,
    zero failures. Owner confirms the host works and authorizes remaining release work; Program Files App DLL
    independently matches working-fix bytes. Host symptom cleared preliminarily; guest after/affected CI and
    candidate interaction remain pending. Resume I106 native process guard and wider recovery audit (E-I107).
156. I106 native after passes on SDK-free Ubuntu 26.04.1: separate portable and --data GUIs/independent leases are
    detected, both absent again after graceful close. Full before/after archives independently stream-verified.
    Exact ecd61f3 all four CI lanes pass; strict Unix inventory 47 pass/four skips per lane (E-I106).
157. Exact ea4a2ac passes all four push lanes and manual lanes plus Linux/macOS dev.549 packaging (37068909015).
    Strict Unix inventories again 47 pass/four skips each; raw artifacts retained. Rebuilt native package checks
    and wider recovery audit remain pending. Windows VM restarted after owner's keep-running instruction, then
    owner reverts snapshot; guest access verified. Preserve earlier evidence on host and keep VM running (E-I107).
158. Exact dev.549 .deb/tar/FUSE AppImage pass successor native startup, corrupt-state preservation, 209-byte
    temporary folders, separate-session forwarding and graceful restart on existing SDK-free Ubuntu 26.04.1.
    Full archive independently stream-verified; this is not another clean-install baseline (E-V19-P3).
159. I106 renamed-apphost audit reproduces false absence despite actual GUI/independent busy lease. Read-only
    executable-identity correction detects ordinary portable, --data and renamed native GUIs. Complete App
    inventory passes 235/250 with 15 explicit skips. Ordinary-account absent cases now return unknown rather
    than false; safely refused, but process-visibility/availability remains an open qualification issue. Full
    before/after archives independently verified; successor CI and exact-candidate tracing pending (E-I106).
160. I106 identity source 06c5791 passes all four CI lanes. Exact 1a9f1ba manual run 37073593358 passes all lanes
    and Linux/macOS dev.553 packaging; strict Unix independently verifies 53 pass/four skips per lane. Packages
    retained, not native-qualified; later availability change requires successor checks (E-I106).
161. I106 availability audit identifies kernel tasks/no executable and normal Python/snapd images above the
    initial bound. PF_KTHREAD-only exclusion and larger bounded/vectorized inspection pass native root absence,
    all three ordinary/root live cases and root absence on close; ordinary restricted census remains unknown.
    Complete App inventory 236/251 with 15 skips, no failures; raw archive independently verified. Correction
    d8c6f3b pushed; affected CI, rebuilt packages, broader visibility/races/candidate tracing pending (E-I106).
162. I107 clean 1a9f1ba self-contained Windows input staged after owner snapshot restoration. UUID/OS and all
    256 payload hashes verified, CLI version exits 0; Admin desktop launcher captures isolated menu trace/exit.
    Computer Use reset/import still fails before input; owner native click result requested and pending (E-I107).
163. Exact d8c6f3b passes all four CI lanes (37075680108). Strict Unix inventories independently confirm 56
    pass/four skips each, 60 outcomes. Raw run/results retained; no tag/release. Both VMs remain running, corrected
    guest menu check awaits owner input. I106 broader privilege/runtime-alias/race and candidate tracing open.

164. Owner confirms clean 1a9f1ba guest menus work. All 256 inputs and trace module reverified; 13 menu episodes
    close through pointer input, none through logical detachment; one remains open 47.6 seconds. Complete raw
    log/export collection retained and independently parsed (E-I107). App still running at collection; no exit
    result claimed. I107 closed for preliminary remediation; exact-candidate interaction remains mandatory.
    Windows Computer Use remains independently unavailable. Continue unblocked recovery safety work.

165. I106 confirmation-window gap reproduced: original admission checks precede the dialog, allowing a new
    writer before acceptance. Controlled elevated Windows guest baseline fails eight cases, four controls pass;
    all four routes covered. Recheck full safety after acceptance, before device authorization. Corrected guest
    RecoverySafety inventory 29 pass/three skips, all twelve new cases pass. Host complete App inventory 242/263,
    21 prerequisite/platform skips, no failures. Exact working source/bundles/outputs retained (E-I106). No actual
    device reader used; broader census/lifetime/physical tracing remains open. Native Unix/affected CI pending.

166. Confirmation fix 5388e5c and test prerequisite correction cc1acf2 pushed. Clean cc1acf2 passes all four CI
    lanes (37079952362). Independently verified strict Unix inventories: 74 pass/22 explicit skips per lane,
    96 outcomes. SDK-free Ubuntu 26.04.1 native self-contained test input passes the same matrix; all 350 inputs
    verified. Successful and earlier failed full archives retrieved and every regular member stream-verified.
    Source/runtime/device race limits remain explicit (E-I106); no source-device scan or candidate claimed.
167. Read-only serial-filtered media preflight finds no 2F2000129618 drive on the host, Windows VM or Ubuntu VM
    at 00:08–00:10 UTC. Owner asked to reconnect it to the host for remaining V09 source-device write checks;
    identity must be reverified before use. Both VMs remain running. I107 preliminarily closed; I106 and final
    qualification remain open, release NO-GO, no candidate/tag/publication.

168. Owner connects G: and authorizes necessary disposable USB use. Exact serial 2F2000129618, disk 5, Storage
    capacity 7,796,162,560 bytes, FAT32 volume GUID and non-boot/system partition verified. Audit before change
    finds narrow/incomplete physical test guards; 1df5dff pins identity/capacity/GUID/backing disks, rechecks mutation
    phases and retains expected file hashes off source. Fourteen refusal cases and real preflight pass 15/15;
    absent-opt-in skips independently verified. First progress-stream harness failure retained. No source mutation.
169. Clean 9257967 census returns unknown on the host and elevated guest after exact owned menu-fixture teardown;
    later independent guest inventory has no FileCat/dotnet name. Protected module identities reproduce Windows
    availability limit. Diagnostic limited-rights comparison retained without weakening production refusal
    (E-V09-G1). Prepare exact 1df5dff guest payload; raw-read/trace work needs USB guest routing or host elevation.
    Broader I106/physical/candidate gates remain open; both VMs remain running, no tag/publication.

170. Exact clean 1df5dff self-contained guest payload staged: all 300 input hashes verified, fourteen native guard
    cases pass, exit 0, direct XML and every output independently verified (E-V09-G1). Defaults to synthetic
    verification; separate physical phases await routing the authorized USB into this elevated VM. CI 37083622189
    passes Windows x64/Ubuntu/macOS so far, ARM64 pending, packages skipped. No actual physical scan/format claimed.

171. Final exact 1df5dff CI 37083622189 passes all four lanes, packages skipped. Full run/log retained; direct
    Windows TRX inventories independently verify App 248 pass/15 skips, Core 699/47, platform 148/33, Remote 88/28,
    no failures. New guard cases 14/14; hardware preflight explicitly skipped. Both Unix strict direct XML
    inventories independently confirm 74 pass/22 declared skips each. USB routing/elevated physical execution is
    the next setup gate; guarded guest phases are ready. No stable release/candidate or source-device pass claimed.

172. USB routing retry identifies the authorized stick on guest disk 1, E:, then VMware disconnects it before the
    native physical preflight starts. Wrapper refuses absent media; logs/identity snapshots retained (E-V09-G1).
    Host G: is available again. All 300 clean 1df5dff inputs rehashed unchanged; host launcher prepared with an
    explicit one-pass/no-skip preflight gate before FAT32/exFAT/NTFS disposable component scenarios. Parser checks
    pass; launcher not run. Host session lacks administrator rights: owner launch/UAC is the next setup gate.
    No physical scan/format or zero-source-write result claimed; I106 and candidate qualification remain open.

173. Owner executes the host launcher in an elevated shell. Native preflight passes one case and FAT32/exFAT/NTFS
    component recovery passes all three, 325/325 generated deleted files exact per format. All 300 inputs and
    fifteen recorded output hashes verified; actual XML/expected manifests retained. No installed-helper,
    independent source-write trace, partial-item or exact-candidate qualification claimed (E-V09-G2).
174. Physical byte-checker audit reproduces ten controlled false acceptances, three controls pass. 2e6dffe compares
    exact unmissing intervals, rejects wrong lengths/invalid ranges and requires complete Recoverable bytes.
    Thirteen cases pass; affected inventory 27 pass/seven explicit hardware skips. Initial empty-opt-in setup
    refusals retained separately. All four 2e6dffe CI lanes pass; direct Windows cases independently verified.
175. 85bb17d adds off-source observed per-item lengths/hashes/missing ranges and requires complete reads.
    Affected inventory again 27 pass/seven skips; all four successor CI lanes pass. Clean self-contained payload
    has 300 verified inputs/archive members; native checker 13/13 and guard 14/14. Stronger host launcher parses
    in Windows PowerShell 5.1 and preparation shell, not yet executed. Owner elevated launch is the next setup
    gate. Preliminary evidence inventory retains 378 files; no candidate/tag/publication (E-V09-G2).
176. Owner launch is followed by two overlapping USB campaigns, 1df5dff and 85bb17d, on the same source. Both
    finish Failed; the stronger attempt's passing cases are also invalid for qualification. All 28 wrapper-recorded
    output hashes and direct cases are independently verified; original guard/caller sources retained before fix.
    1883eb4 adds a fixed per-serial cross-process guard lease; 090a2b6 honors test cancellation. Affected checks pass
    29 with seven hardware skips; App compiles with one explicit live skip. Clean successor native checker/guard/lease
    controls pass 29/29; all 300 inputs/archive streams verify. Launcher mutex controls pass duplicate refusal,
    available success and abandoned-owner recovery. Legacy entry points retired with original bytes retained.
    Distinct exclusive launcher is ready; one elevated host launch is the setup gate. No successor physical pass or
    source-write qualification claimed. NO-GO remains (E-V09-G3).
177. Guard 1883eb4 and successor 090a2b6 CI both pass all four lanes, three package jobs skipped. Complete logs,
    exact run identities and original Windows test archives retained; archive/member hashes verify. Direct Windows
    TRX inventories independently confirm App 248 pass/15 skips, Core 699/47, Windows platform 163/33 and
    Remote 88/28, no failures; all 29 checker/identity/lease controls pass in each. Physical skips remain explicit;
    no hardware or candidate qualification inferred (E-V09-G3).

178. Owner reports failing GitHub CI. Exact b5ce744/a50b3b8 runs fail only Windows lease helper readiness at 15 s;
    complete logs, original archives and direct inventories retained. Older bb2d748 macOS failure is a live-copy
    observer timing race. I108 corrects readiness synchronization and holds the real copy before verification,
    preserving all substantive assertions. Affected local 29 USB synthetic and eight progress checks pass.
    Baseline full-solution host run retains a separate Windows records assertion failure; not claimed green.
    Successor CI validation is required before resuming the next physical trace setup (E-I108).

179. Exclusive elevated USB run at exact 090a2b6 completes: preflight 1/1 and FAT32/exFAT/NTFS 3/3, no failures or
    skips. Independent verification matches all 325 generated deleted files per format and all 327/327/326 complete
    claims, verifies 300 unchanged staged inputs and nineteen recorded output hashes. Pinned USB/partition identity,
    native phase/child records and scoped observer retained. Separate 25-file completed-run inventory preserves the
    original preparation inventory. This clears the earlier owner-launch gate; installed-helper/source-write/full
    source hash/candidate qualification remains open (E-V09-G3).
180. I104 formatting correction a50b3b8 puts FileCat first in Windows titles, then selected location and account/
    elevation. Existing assertions fail one case on baseline b5ce744, pass 2/2 corrected; existing headless window
    title case passes 1/1. Selected folder named FileCat remains valid. No live desktop or elevation-detection claim;
    exact candidate interaction remains pending (E-I104). CI failure at a50b3b8 is isolated to I108.

181. CI synchronization correction 6cad380 passes all four lanes (37119313116); three package jobs skip.
    Exact complete metadata/log and original Windows archive retained, GitHub archive digest/member checks pass.
    Direct inventories confirm App 248 pass/15 skips, Core 699/47, Windows platform 163/33, Remote 88/28,
    zero failures. All affected lease/oracle/guard/progress/title cases pass; lease helper readiness takes 9.784 s.
    Full local repaired Core also passes 700/46 explicit skips. I108 is remediated preliminarily; no candidate
    qualification or release GO inferred (E-I108). Owner keeps the USB connected; no current test touches it.

182. The separately retained host file-record assertion fails again. Independent native probe confirms error 50
    on a 300,000-byte NTFS fixture with metadata, data and Generic Read handles; the host driver declines the cluster
    query. I109 makes the unavailable layout explicit and checks native support without skipping the rest of the
    integration case. Updated assertion fails on the unchanged report; after correction full Windows 159 pass/
    37 hardware/platform skips, 196 total, and a native report probe confirms the explanation. Original sources,
    reports, failures and nineteen-file independent inventory retained. Successor CI remains required (E-I109).

183. Documentation successor c042d91 and I109 correction cfcc9e6 both pass all four CI lanes. Complete exact
    cfcc9e6 run/log and original Windows archive retained; direct inventory verifies the file-record case and
    earlier I108/I104 cases, no failures in four Windows project inventories. I109 is remediated preliminarily;
    native branch is not separately logged, candidate reruns remain required (E-I109).
184. Prepare the next V09 tracing prerequisite: pinned installed Process Monitor 3.95, existing accepted license,
    no active capture. A marked private launcher captures a known off-source temporary file's reads/writes and a
    never-accessed-path negative control, retains native PML/full CSV, and verifies restoration of the existing
    flat tracer configuration. Parser/preparation controls pass without capture or USB access. Real command-line
    capture/export remains unverified until the owner launches it elevated. The agent is unelevated; owner action
    requested with the concrete LaunchProcmonCaptureControl.cmd. This is an instrumentation control, not source-write
    qualification. Private preparation: artifacts/release-evidence/v09-usb-trace-20261003; runner SHA-256
    08535ab32b175b24c1d0fd9193dbdfdf1f6dec275a799b7b3b0d2926f44af319, launcher
    fc81ed7681853f16ffa61d7635f9a104fc00dfeb6534614923e71ba2e75460ce. USB remains idle and Windows VM kept running.

185. Owner launches the prepared tracer control elevated; run control-8a15fbb06c284156873117bfa820f3be succeeds.
    Independent streaming verification confirms 455,237 CSV events, exact 4,096-byte positive read/write and zero
    never-accessed-path events; seventeen files size/hash verified. Original native PML/full CSV retained.
    Independent registry read confirms all thirty saved values/types restored, no subkeys or remaining tracer.
    The first verifier's wrong expected file count is retained and corrected. USB is not accessed. This clears
    the instrumentation-control launch gate, not physical source-write or installed-helper qualification (E-V09-G4).
186. Prepare the bounded read-only USB component trace at clean self-contained f2f0141: all 300 native inputs and
    four embedded source versions independently verify. Selector-only control chooses one explicit prerequisite skip;
    no raw source access. Windows PowerShell 5.1 parser/hash/truncated-input/duplicate-campaign/available controls pass.
    Independent preparation inventory retained. The launcher will hash the complete physical disk before/after and
    retain the native case's full PML/CSV, controls, process/lease handovers and restored configuration. Current USB
    metadata is safe/present; owner elevated host launch is required. No live result or installed-helper/GUI/candidate
    qualification inferred. 087917d/f2f0141 CI metadata confirms four green lanes each (E-V09-G5).
187. Final owner-launch check catches malformed launcher line generation, then an inherited PowerShell 7 module
    precedence that makes Get-FileHash unavailable under cmd.exe/Windows PowerShell 5.1. Both failures/inputs are
    retained. The corrected five-line launcher sets system Windows PowerShell module precedence locally; its exact
    command with ValidatePreparationOnly exits zero without raw USB access/capture. Final independent inventory
    verifies 300 native inputs and 45 preparation files, including command control. Initial launcher-readiness
    inference is superseded; payload source stays exact f2f0141. Owner launch gate remains (E-V09-G5).
188. Owner executes the exact G5 elevated launcher. Preflight/native read case each pass once; all 300 inputs and
    43 run files independently verify. Before/after full 7,796,162,560-byte physical hashes differ. PML and full CSV
    independently contain only 104,749 events ending before the marker/test, so positive controls fail and no source
    attribution/zero-write evidence exists. Original verifier failure is retained; separate incomplete inventory
    explicitly refuses qualification. Thirty original configuration values restored; no workers/tracers remain.
    Hold further USB tests and investigate timing off-source. Known guest tracer/config paths absent after snapshot
    restoration; no new license accepted. Exact 81b352e CI has four passing lanes/three package skips (E-V09-G6).
189. Complete scanning of every G6 PML event/CSV row confirms its one-second range and no later events. Prepare six
    off-source capture comparisons varying runtime, working/temp directories and readiness, with exact early/late
    read/write markers, child lifetime controls and per-case configuration restoration. No USB/FileCat launch.
    PS5.1 parser, marked/unmarked child content/ownership controls and actual cmd.exe preparation invocation pass;
    nineteen preparation files independently verify. Old USB launcher bytes preserved; original path now exits one
    with a hold notice, verified by actual cmd.exe execution. Owner elevated host launch is required. Exact 43e520d CI has
    four passing lanes/three package skips. Live capture qualification remains pending (E-V09-G7).
190. Owner authorizes agent launch. Tool token is standard but Windows RunAs succeeds; native controller validates
    administrator token. Off-source A reproduces one-second incomplete capture; B/C sustain exact marker/child
    controls, independently matching PML/full CSV. Seventy-five completed-case files verify; remaining cases running.
    Runtime alone cannot explain failure. Initial cross-clock timestamp assertion fails by 91 microseconds and is
    retained; revised native-clock ordering/coverage checks preserve exact counts/ranges and report offsets.
    Actual 84615d9 CI passes four lanes/three package skips. No USB access/qualification (E-V09-G8).
191. All six timing comparisons finish: B/C/D pass exact marker/child controls; A/E/F fail with only about one second
    of events. Final independent 158-file/PML/CSV inventory and thirty-value registry/worker census verify. System
    temp association does not explain G6, and separate WaitForIdle does not correct F. No durable remedy inferred.
    Local WPR has DiskIO/FileIO profiles and reports no active recording; investigate off-source recorder controls
    and loss statistics. USB path remains held for unexplained G6 source difference (E-V09-G8).
192. Pinned built-in WPR/tracerpt scripts parse/preparation passes; agent RunAs executes the unique named DiskIO/FileIO
    control without USB access. Exact parent/child file bytes, typed file events, one corresponding physical disk
    write per marker and child lifecycle pass; native/summary agree 2,561,544 events, zero reported loss. Independent
    63-run/23-reader-file inventory and named-session/worker cleanup verify. Full ETL/XML/profile/reports retained;
    XML schema/timezone limitations disclosed, native cached TraceEvent 3.2.6 reader used. No product dependency or
    raw-device/source-write pass. Continue owned virtual-device visibility control before physical work (E-V09-G9).

193. Agent RunAs executes the owned raw virtual-device WPR controls. Two setup attempts stop before raw I/O and are
    retained with all-zero fixtures and independent detach/session/worker checks. Successor validates numeric CIM
    bus and empty-safe partition query before exact 64-MiB virtual access. Native FileIO/DiskIO capture all three
    raw reads and the deliberate 4-KiB write with exact process/thread/offset/count/call boundaries; two System
    attachment reads are separately disclosed. Full offline data oracle finds only the intended positive block.
    Native 2,057,617 events, zero reported loss; independent 154-run/74-diagnostic-file inventory and cleanup pass.
    No protected USB/product/candidate qualification. Exact c930c8f CI passes four lanes/three package skips.
    Continue source-change isolation while FileCat USB validation remains held (E-V09-G10).

194. Correct the physical comparison's equal-short-read acceptance gap (I110): each returned count must equal the
    request, counts/hashes and deleted-entry totals are logged, expected session ending required. No production
    changes. Native off-source controls pass 23/two declared skips; strict physical body remains held. Elevated
    existing host C: read fails error 50, independently reproduced by four aligned native controls; four E: controls
    succeed. Owned E: temporary fixture then passes the existing elevated reader case, one/no skips. Failed and
    incomplete attempts retained; 32-file inventory and final worker check verify. No C:/USB/candidate pass (E-I110).

195. Pure read-only source-change observation at c92d31a retains two full 7,796,162,560-byte physical images,
    each with 1,859 exact chunks; independent bytes/hashes match each other and G6's after hash. No FileCat launch
    or source writes requested. Native trace loses 51,216 events despite zero live counters, so source-write
    qualification fails; no historical attribution inferred. Sixty-seven run files, exact read events/lifetimes,
    cleanup/leases and post-compression logical hashes verify. G6 remains unresolved and USB held (E-V09-G11).
196. Custom single-kernel-collector short raw controls pass: exact four raw operations and full 64-MiB oracle,
    parent marker/lifetime, 61 run/74 diagnostic files and cleanup independently verify. Native 609,387 events,
    zero reported loss; initial verifier schema/name failures retained. Continue a duration control before another
    source observation. Exact c92d31a CI passes four lanes/three package skips; strict USB body remains held (E-V09-G12/E-I110).

197. Nine-minute custom kernel control initially saves only 32 metadata events, despite success/loss-zero reports.
    Matched short pilots associate the failure with trace temporary location; exact Windows cause remains unknown.
    Original captures and a pre-capture launcher failure are retained. Short-scratch successor passes 1,190,789
    native events/zero loss, exact early/late raw operations separated by 540.020002 seconds, full 64-MiB oracle,
    64 run/98 diagnostic files and independent cleanup. Exact 6267331 CI passes four lanes/three package skips.
    Continue proof-gated read-only source observation; FileCat USB path stays held (E-V09-G13).

198. Proof-gated read-only USB observation passes with the exact G13 profile and short trace scratch. Both full
    images match G6's after hash; 5,634,333 native events/zero loss contain all 3,718 source reads and no source
    disk/file writes. Exact images/chunks, positive visibility, lifetimes, 65 run files and independent cleanup
    verify. Initial profile-byte mismatch refuses before source access and remains retained. No FileCat launched;
    historical G6 difference remains unresolved and product USB validation held. Exact b477783 CI passes four
    lanes/three package skips (E-V09-G14).

199. Read-only Windows process-structure audit verifies standard/elevated snapshots and native self controls.
    Limited image paths remain unavailable for 140/412 standard and five/411 elevated processes. Four elevated
    rows have null PEB/protection flags; Idle never opens. No structural/name exemptions or absence claim added.
    Reserved-variable failure retained; corrected pins, accounting and worker cleanup independently verify.
    Continue I106 availability and other unblocked audits; no candidate or source-device pass (E-I106-P1).

200. Windows census now uses a limited read-only image query instead of requiring module/memory access.
    Owned-child error-5/positive image/negative query controls pass under standard and elevated launches;
    initial elevated fixture failure retained and repaired within its own impersonation scope. Full host App
    242 pass/21 skips and corrected Windows platform 161 pass/37 skips, no failures. Exact working source/DLLs,
    623 files, XML/skip inventories and cleanup independently verify. No exemption/absence/USB pass; native clean
    guest and successor CI pending (E-I106-P2).

201. Preceding documentation c162481 CI passes Windows x64/Ubuntu but fails ARM64's global thumbnail helper-start
    count and macOS's verified-copy totals checkpoint. Full failing run/log retained; I108 reopened for diagnosis.
    No source/candidate result is inferred from that run (E-I106-P2).

202. Clean 36ee824 Windows process-query payload passes both native permission controls in the elevated guest,
    zero skips. All 300 input pins/301 archive members, exact XML/control output and worker cleanup verify;
    first missing-marker retrieval refusal retained and corrected. Exact successor CI passes four lanes/three
    package skips. Broader I106 absence/device/candidate gates stay open (E-I106-P2).

203. Correct I108 observers: copy waits for finalized discovery totals at its held checkpoint; thumbnail case
    requires its helper answer/UI binding, while native reuse/containment assertions remain separate. Attempted
    rendered-byte control fails on the existing mock backend and is retained; no rendered-pixel claim. Full Core
    700 pass/46 skips, corrected full App 242 pass/21 skips, targeted UI 2/2 and native client 9/9 pass. Exact
    working source/XML/skip inventories independently verify; successor CI pending (E-I108-P1).
204. Exact 1bd931b successor CI passes all four required lanes; three tag/manual package jobs skip. Complete
    metadata/log and Windows artifact retained; direct XML independently verifies all four inventories, eight
    progress, two thumbnail UI and two limited-image cases. I108 is verified preliminarily; candidate and native
    rendered-pixel qualification remain open (E-I108-P1).
205. Resume V12/I92: fresh million-long-name baseline reproduces 202–345/737–1,168 ms blocking scans. Large
    quick search now leases captured rows/indexes and runs off the UI thread, with ordered keys, pending feedback,
    stale-view retry and immediate UI cancellation. Nine Core/seven new headless App controls pass; serialized
    model acknowledgement 0.017–0.494 ms. Initial fixture/observer failures retained; native frame/AT/reference
    and successor CI remain open (E-I92).
206. Affected full Core exposes I111: publishing an empty/parent-only view can suppress its delayed first file.
    Four controlled baseline timeouts reproduce it; revised geometric batching passes all four and original
    cursor/streaming controls. Exact failing/corrected inputs retained (E-I111).
207. Further full Core exposes I112's cache observer sampling active loads after a constant total. Held-read
    negative control proves that plateau is insufficient; bounded actual-load wait retains exact sum/ceiling/
    disposal assertions. Seven cache cases pass. Final full Core 714 pass/46 skips; App 249 pass/21 skips;
    twelve exact source files, 790 payloads, ten XML inventories and 2,480 files independently verify. Native
    computer-use initialization still fails before app selection/input; CLI work continues. Candidate/CI pending.
208. Clean b7d2e8 successor CI passes all four required lanes, three package jobs skip. Direct Windows XML
    verifies Core 713/47 skips, App 255/15, platform 165/33, Remote 88/28 and all 39 affected cases. Clean
    self-contained Windows VM run passes 20 Core/seven headless App cases with no skips. Independent payload,
    source-content/raw-byte, XML and cleanup checks verify; controller/workers and owned temp files are absent.
    Mixed line-ending observer corrections retained. Native frame/AT/reference/candidate remain open (E-I92–112).
209. V12 slow quick view exposes I113: six controlled baseline failures cover stale errors/repeated-key readers,
    failed initialization/empty reset cleanup, abandoned reads and ten concurrent held opens. Immediate request
    retirement, separate result ownership and existing per-device scheduling pass seven controls; two held opens
    bound eight canceled queued demands, while another device completes. Full App 256/21 skips passes (E-I113).
210. Affected full Core has one GnuPG test failure. Two parallel fixtures mutate the shared tool override (I114);
    a controlled unchanged signature is Good → UnknownKey → Good across tool swap/restore. Original interleaving
    remains untraced. Override fixture isolated; full Core 714/46 skips passes. Exact three-file overlay, 1,056
    final inputs and 3,329 retained files independently verify. Clean CI/guest/candidate pending (E-I114/E-I113).
211. Clean 7497acf passes all four required CI lanes; three package jobs skip. Windows XML independently verifies
    all four inventories and 47 affected cases (native GnuPG explicitly skips there; Git-GnuPG passes). Clean
    Windows guest passes seven quick-view controls without skips; 364 payloads/365 ZIP members/ten source-content
    files and XML/cleanup verify. Controller/test worker/temp files absent. Native presentation/candidate remain open.

212. Resume V13 archive-member results. Four valid controls fail because narrowing silently skips every non-file-system
    reference (I115). Revalidation now retains original identities/ordinals/relative paths and only the input subset;
    contents, unsupported scopes, partial/missing/unreadable listings are explicit. Eleven Core and two headless
    Find/content/log/navigation controls pass; full Core 725/46 skips and App 258/21 skips pass. Exact baseline/final
    input and direct XML inventories independently verify (E-I115). Two earlier fixture replacement faults and
    deferred-row observer failures are retained separately. Clean CI/guest execution is next; candidate/native remain open.

213. Clean ff8746a passes all four required CI lanes, three package jobs skip. Six direct XML inventories verify,
    including all 13 new Windows cases and both App flows on Linux/macOS; three artifact ZIPs match server digests.
    The exact clean self-contained payload passes 13 Windows guest controls without skips. All 683 payloads/684 ZIP
    members/eleven source copies and retrieved XML verify; controller/workers absent, owned temp empty. Additive
    guest-locator correction retained, payload unchanged. Other formats, initial listing warnings and candidate/native remain open.

214. V13 initial archive search exposes I116: the adapter discards damage/duplicate-name warnings from actual
    providers. Two valid baseline failures plus two positive TAR/gzip narrowing controls; warning forwarding and
    per-archive deduplication now pass 32 affected cases. Full Core 729/46 skips and App 258/21 skips pass. Exact
    input/active-assembly/direct XML inventories verify (E-I116). Detector-misconfigured and disk-case observer
    attempts are retained separately. Clean CI/guest is next; other formats/native/candidate remain open.

215. Clean 6ecf4a8 passes all four required CI lanes, three package jobs skip. Six direct XML inventories and
    three server-digest-matching artifact ZIPs verify, including all 34 affected Windows cases and both Find flows
    on Linux/macOS. The exact self-contained source passes 34 Windows guest cases without skips; 680 payloads,
    681 ZIP members, eight source copies and XML/cleanup verify. Controller/workers absent, owned temp empty.
    E: fills during initial publish; all 668 failed-output files transfer to the authorized second workspace and
    hash-verify before the redundant failed tree is removed. Clean package/CI evidence uses that workspace.
    No historical/source evidence discarded; other-format/native/candidate and sealed-store gates remain.

216. V13's remaining saved-criteria/duplicates component controls pass (E-V13-F2). Four actual headless Find
    flows save literal/regex/hex/Unicode criteria, reload settings from disk and reopen/search an independently
    specified positive/negative byte corpus. Duplicates within a six-file subset retain both known groups,
    original relative paths/source membership and correct extra-copy marking, excluding unselected identical
    files and same-size different bytes. Full App 263/21 skips passes; 106 inputs/ten sources/direct XML verify.
    No production defect found. Clean CI/guest is next; process restart/native/candidate remain open.

217. Exact aed64a7 ARM64 CI fails the late-name discovery test after one second with an empty host list
    (I117); other three lanes pass. Its timer starts before probe/request admission; original scheduling is
    untraced. Controlled cutoff after the fixture receives the metadata request passes immediate/delayed
    setup; a coupled name/probe cancellation mutation fails both controls. Public timings/policy remain.
    Full Core 730/46 skips and seven related App controls pass; 218 inputs/source/direct XML and original
    failure verify (E-I117). Clean successor CI/guest next. Clean ab919ed meanwhile passes four lanes,
    with all five new saved-search/duplicate App cases on Windows/Linux/macOS independently verified
    against three server-digest-matching artifacts (E-V13-F2).

218. Clean da3a3d6 passes all four CI lanes; six direct inventories and three server-digest-matching ZIPs
    verify, including all eight network and five saved-search/duplicate Windows cases and all five affected
    App cases on Linux/macOS. Exact self-contained source passes eight Core/seven App guest cases, zero skips.
    All 685 payloads/686 ZIP members/13 source copies and retrieved XML/output pins independently verify.
    Controller/workers are absent, owned temp empty (E-I117/E-V13-F2). Native/candidate and real-device gates remain.

219. V12 exposes I118's stale metadata publication after actual checksum-sidecar invalidation/Forget;
    four unchanged-production failures include explicit Compute. Validity records and coordinated cache
    publication reject obsolete values and preserve unrelated fields. Four stronger event controls expose
    the missing retry wakeup in an intermediate fix; completed remedy passes all six controls, including
    1,000 abandoned viewport requests, a healthy second device and controlled interactive priority.
    Full Core 736/46 skips and App 263/21 skips pass. All 477 inputs/source/direct XML verify (E-I118).
    Clean CI/guest is next; wider/native/candidate checks remain.

220. Clean 2896108 passes four CI lanes; six direct XML inventories and three server-digest-matching ZIPs
    verify, including eight metadata and two checksum UI cases on Windows and both App cases on Linux/macOS.
    The exact self-contained source passes ten Windows guest cases, zero skips. All 680 payloads/681 ZIP
    members/eight source copies and output pins independently verify. Controller/workers are absent, owned
    temp empty (E-I118). Native demand/frame/AT, concurrent workloads and candidate qualification remain.

221. V12 page-load controls expose I119: closing a reader disposes a held actual-file read/revision source,
    and later page/refresh requests access the closed source. Eight baseline failures plus one normal cached-byte
    control; coordinated source-use accounting defers disposal while immediately retiring cache/new demand.
    Nine controls/38 affected cases and full Core 745/46 skips/App 263/21 skips pass. All 267 inputs/eleven
    sources per stage/active assemblies/direct XML verify (E-I119). Clean CI/guest next. Direct Source/picture
    use and wider queue/device/native/candidate remain. Native automation import again ends with trusted-Node
    exit/kernel reset before any input; component and VIX guest execution remain available.

222. Clean de1fd71 passes 45 Windows guest cases, zero skips; 683 payloads/684 ZIP members/eleven sources,
    output pins and process/temp cleanup verify (E-I119). Additive private-working-directory wrapper and ten
    round-tripped xUnit string-name escaping mappings retained; payload/tests unchanged. Three CI lanes pass;
    Windows fails the existing NTFS fixture I120 while all 45 affected Windows/seven Linux/macOS App cases pass.
    Six direct XML inventories and three server-digest-matching artifact ZIPs independently verify.

223. Original I120 CI report says the fixture's last LSN is older than its circular log retains, but the MFT test
    dereferences its absent history table. Raw blocks/IO timing unretained; no unobserved attribution claimed.
    Separate current-MFT and bounded-live-history cases preserve positive assertions and declare unavailable
    live history skipped. Full host platform 161/38 skips and nine golden NTFS log cases pass; four record
    privilege skips do not qualify live history. All 130 inputs/source/active assemblies/original CI XML verify.
    Elevated guest and clean successor CI next (E-I120); production file-record code unchanged.

224. Clean 9074cf6 passes all four CI lanes, three package jobs skipped; six direct inventories and three
    server-digest-matching ZIPs verify. All 65 selected Windows/seven Linux/macOS App cases pass, zero
    affected skips, including complete live NTFS history. Exact self-contained source passes 20 elevated
    guest cases, zero skips, ending 06:44:27 UTC; 625 payloads/626 ZIP members/seven sources/output pins verify.
    Controller 13568/workers 5944/8176 absent, owned temp empty at 06:47:42 UTC. Guest system-volume metadata
    reads explicitly requested; no physical USB/recovery-source qualification. Original failed CI retained
    (E-I120/E-I119). Overall NO-GO; direct picture demand, wider/native/candidate checks and USB hold remain.

225. V12 direct picture-feed controls expose I121: four held actual-file reads are disposed by viewer/quick-view
    close; a fifth baseline case retains a loaded F3 bitmap, while normal quick view passes. Actual feeder
    borrows preserve source ownership after prompt cancellation; feed boundaries/exception observation and
    bitmap retirement prevent abandoned resources. Six controls/20 affected App cases/full App 269/21 skips
    and Core 745/46 skips pass. All 1,809 inputs/fourteen sources/active assemblies/direct XML verify (E-I121).
    Clean CI/guest next; per-device picture bounds, other direct Source use, native/candidate remain.

226. Clean a550fcd passes four CI lanes, three package jobs skipped; six direct inventories/three server-digest-
    matching ZIPs verify, including 56 affected Windows and eighteen App cases on each Unix lane, zero affected
    skips. Exact source passes 34 Windows guest cases, zero skips, ending 09:04:25 UTC; 686 payloads/687 ZIP
    members/fourteen sources/output pins verify. Controller 12360/workers 9500/12532 and owned decoder
    children absent, temp empty at 09:06:23 UTC. Original harness expects seventeen Core cases instead of
    actual sixteen; all sixteen pass but its guard stops App. Failed run/cleanup retained; exact corrected
    successor uses identical payload in a new root (E-I121). Per-device/native/candidate scopes remain.

227. V12 device-picture controls expose I122: F3 and rapid quick view hold three actual file reads despite
    two configured device workers; healthy device pictures complete in both baselines. Provider-keyed
    scheduled feeding retains actual callback ownership and drops canceled queued reads. Two controls/22
    affected App cases/full App 271/21 skips and Core 745/46 skips pass. All 1,812 inputs/fifteen sources/
    active assemblies/direct XML and scoped original/corrected call-count traces verify (E-I122).
    Clean CI/guest next; watchdog/hard-cap, aggregate decoder, other Source/native/candidate remain.

228. Clean 82f7488 passes all 36 elevated Windows guest picture/page-reader/budget controls, zero skips.
    Exact 687-file payload/688 ZIP members/fifteen sources/direct XML and worker/decoder-child/temp cleanup
    verify (E-I122). CI 37192262649 has three passing lanes and one Windows synchronization failure:
    changed.txt remains old, replacement job AwaitingDecision; its original request is unretained.
    All 58 affected Windows and twenty App cases per Unix lane pass. Server artifact digests/size and
    extracted XML inventories verify; failed CI is retained and synchronization investigation proceeds.

229. I123: the headless synchronization fixture omits native Windows adapter registration. A held target
    reproduces old content/AwaitingDecision/error-access with the portable adapter; the scoped native adapter
    passes 17 affected cases and full App 271/21 declared skips. All 2,202 inputs/thirteen sources per stage/
    unchanged production DLLs/direct XML verify (E-I123). Intermediate culture/path/location/space harness
    failures are retained, then identical captured assemblies pass in short outside-repository system temp.
    Production is unchanged; original CI request/interleaving remains unavailable. Clean CI/guest is next.

230. Clean 08acc2f passes four CI lanes and all 17 elevated Windows guest comparison/synchronization/operation
    cases, zero skips. Exact 367-file payload/368 ZIP members/thirteen sources/XML and worker/temp cleanup
    verify (E-I123). Three server artifact digests/sizes and extracted XML verify: all 75 affected Windows
    Core/App and 37 App cases per Unix lane pass, including all I122 controls. Complete live NTFS history passes.
    The original failed run and unavailable request/interleaving remain recorded; native/candidate remain.

231. V12 scheduler controls expose I124: the original production DLL's real timer throws an unhandled
    list-enumeration exception when adding a replacement; a separate controlled Watch probe exceeds a two-worker
    cap with three active calls. Enqueue now checks the cap and Watch scans the initial list count. Both corrected
    probes/two new controls/48 Core and 37 App affected cases/full Core 747/46 skips/App 271/21 skips pass.
    All 1,084 inputs/nineteen sources/probe DLLs/observations/XML/process cleanup verify (E-I124).
    Clean CI/guest next; synthetic callbacks do not qualify physical hung hardware or native/candidate scope.

232. I124 clean source 18006cc passes all four required CI lanes (37195976222); package jobs skip. All 85 affected
    Windows controls and 37 App controls per Unix lane pass. Six full XML/skip inventories, server digests and
    ZIP/extracted bytes verify. The self-contained SDK-free 26300 guest passes the same 85 Core/App controls,
    zero skips; 691 payloads/692 ZIP members/nineteen sources/pre-launch pins/direct XML/owned process and temp
    cleanup verify. Synthetic watchdog/cap qualification is preliminary; physical/wider/native/candidate remain.

233. V12 shutdown checks expose I125: original production Run admitted after disposal leaves an existing-queue
    task incomplete or starts a new-queue callback after the disposal snapshot. Both owned baseline probes exit 2;
    identical corrected probes cancel both tasks with zero callbacks. Enqueue now checks queue and owner shutdown
    under its lock. Two new controls/50 Core and 37 App affected/full Core 749/46 skips/App 271/21 skips pass;
    1,084 inputs/nineteen sources/probe DLLs/direct XML/owned process cleanup verify. Clean CI/guest next (E-I125).

234. I125 clean source 749f55f passes four required CI lanes (37198032750); package jobs skip. All 87 affected
    Windows controls and 37 App controls per Unix lane pass. Six full XML/skip inventories, server digests and
    ZIP/extracted bytes verify. The self-contained SDK-free 26300 guest passes the same 87 Core/App controls,
    zero skips; 691 payloads/692 ZIP members/nineteen sources/pre-launch pins/direct XML/owned process and temp
    cleanup verify. Admission/watchdog/cap qualification is preliminary; wider/physical/native/candidate remain.

235. V12/V13 comparison lifetime checks expose I126: four actual-production/owned-file activation/page revision
    cases dispose during their held call when closed or reopened, then fail on the released handle. Views now count
    revision/length/read calls and activation captures views. Identical corrected probe and App-only DLL swap pass
    all four; four regressions/41 App and 50 Core affected/full App 275/21 skips pass. All 1,434 inputs/twenty-one
    sources/24 fixtures/DLLs/XML/identity-aware cleanup verify. NU1015, inherited Core stamp and reused-PID observer
    records remain retained. Clean CI/guest next; updated Computer Use import still crashes before input (E-I126).

236. I126 clean source e406c96 passes four required CI lanes (37200743448); package jobs skip. All 91 affected
    Windows controls and 41 App controls per Unix lane pass. Three server artifact digests/ZIP bytes and six full
    XML/skip inventories verify. The self-contained SDK-free 26300 guest passes 50 Core and 41 headless App cases,
    zero skips; 693 payloads/694 ZIP members/twenty-one canonical sources/pre-launch pins/direct XML/owned process
    and temporary-folder cleanup verify. Comparison revision/length lifetime qualification remains preliminary.
    A plain Node call independently fails before loading Computer Use; exact records retained, no input/state
    observation. Live UI needs runtime setup restored; wider content/native/candidate remain (E-I126).

237. Resumed plain Node startup at 12:25:57 UTC fails before importing Computer Use. Kernel reset followed by
    one retry at 12:26:12 UTC fails identically: exit code 1 and Windows sandbox helper setup-refresh error.
    Complete tool diagnostics retained and pinned (E-I126). A full Codex restart is requested as the next setup
    recovery attempt. No desktop state/input observed, no USB action; both VMs remain running. Live interaction
    is gated; this does not qualify remaining native/reference/AT/candidate scopes.

238. Owner reports Codex restarted; fresh Computer Use initialization still exits before selecting a host/VM
    window (exact tool result retained with E-I126). Continue independent V12 checks. Actual clean e406c96 DLLs
    reproduce I127: two owned denied-subtree cases show/cache a 1,000-byte lower bound as exact and skip retry
    after access restoration; two accessible controls correctly count 1,234 bytes. Refresh preserves the defect.
    Valid failure XML/loaded-copy pins/unchanged file hashes/owned ACL restoration retained (E-I127). Intermediate
    harness failures remain separately recorded. Remediation/revalidation in progress; no USB action or native
    desktop qualification. Both VMs remain running; overall NO-GO.

239. I127 remedy carries lower-bound state through row and refresh/pending caches, marked stats and quick-view
    captions; Count can retry partial folders and a complete retry clears uncertainty. Identical probe with only
    App/Core DLLs replaced passes four cases; seven App and three Core regressions pass, including zero bounds and
    attached-pane updates. All 59 Core/63 App affected and full Core 752/46 skips/App 282/21 skips pass. Independent
    verification checks 1,800 inputs/nine sources/two properties files per capture, DLLs/XML case multiplicity and
    unchanged skip reasons, sixteen fixture payloads, restored ACLs and owned worker/temp cleanup. Original test
    encoding failure retained; corrected expectation passes identical production bytes. Clean CI/guest next.

240. Clean I127 source 2be20cf CI 37214155885 passes Linux/macOS and fails both Windows lanes: all seven new
    App controls fail the owned-fixture ACL-restoration check, including accessible controls. Complete direct
    TRX inventories and three server artifact digests verify; failure retained, not waived (E-I127). Descriptor
    diagnostics preserve the assertion and all seven host controls still pass. Corrected clean CI/guest pending.

241. I127 diagnostic b80f286 CI identifies Windows' auto-inheritance marker as the only descriptor difference
    in all seven direct Windows failures/seven ARM64 logged failures; every ACE matches. Corrected fixture
    comparison ignores only that marker and preserves all permission/cancellation/hash checks; seven host cases
    pass, 751 captured inputs verified. Clean b80f286 SDK-free guest separately passes all 122 affected cases
    without skips; 702 payloads/703 archive members/thirty canonical sources and owned process/temp cleanup
    verify. This precedes the fixture correction; successor clean CI/guest still required (E-I127).

242. I127 clean 9d33282 passes all four CI lanes, including ARM64 package startup/installer compilation, and
    122 SDK-free guest controls without skips. Complete server artifact digests/six TRX inventories verify:
    Windows Core 751/47 skips, App 288/15, Platform 166/33 and Remote 88/28; Unix App each 260/43. All 122 affected
    Windows cases pass; Unix App each 50 affected passes/13 declared Windows-fixture skips, including six
    unchanged Windows-factory reasons. Raw IDs/names/case multiplicity verify with only runner quote/backslash
    escaping accounted for. Native 702 payloads/703 ZIP members/thirty canonical inputs and owned process/temp
    cleanup independently verify (E-I127). Both earlier failed fixture CI attempts remain retained. Owner asks
    to finish this slice and stop before restarting Codex elevated; stop after evidence push, leave both VMs
    running. No USB action, native/AT/candidate qualification or stable publication. Overall NO-GO.

243. I128: actual clean 9d33282 production disposes an owned file during held line, HTML, Markdown and Info
    reads; the line task throws ObjectDisposedException. Four completed-read controls pass, hashes unchanged.
    Existing reader borrowing plus close cancellation/result retirement corrects all eight identical probe
    cases with only App/Core DLLs changed. Eight App/two Core regressions, 102 Core/41 App affected cases and
    full Core 754/46 skips/App 290/21 skips pass; 1,096 captured files/twenty-five source inputs/complete case
    inventories independently verify. Clean CI/guest pending, native/hardware/aggregate/candidate remain.
    Elevated restart still fails Node sandbox setup before any target/input; VIX works, both VMs stay running,
    no USB action. Initial probe compile error retained as harness-only evidence (E-I128).

244. I128 clean c453925 passes all four CI jobs (including ARM64 package startup/installer compilation),
    all 143 affected Windows/41 Unix App cases without affected skips and 143/143 SDK-free guest controls.
    Three server artifact digests/six full TRX inventories, 697 guest payloads/698 ZIP members/twenty-five
    canonical source/build inputs and exact owned process/temp cleanup independently verify. No native
    browser/input/frame, hung hardware, aggregate or candidate qualification is claimed. Owner reconnects
    and authorizes G: USB rewriting; identity alone verifies the same serial/non-system disk, original G6
    source-change evidence remains held and no USB mutation occurs. Both VMs stay running (E-I128).

## Evidence invalidated by the campaign's own changes

- I128: prior viewer passes do not qualify direct line/page/Info reads during close. Working controlled
  correction/affected/full host suites, four clean CI jobs and 143 SDK-free guest controls pass. Native/
  hardware/aggregate/candidate remain.

- I127: earlier folder-count passes do not qualify inaccessible-subtree lower bounds or retries after restored
  access. Corrected probe/affected/full host suites, four clean CI lanes and 122 guest controls pass. Native/AT/
  candidate remain required.

- I126: previous comparison passes do not qualify revision/length lifetime during close or F5. Corrected owned
  real-file probes/affected Core/App/full App pass, as do four clean CI lanes and 91 guest controls. Wider content
  lifetime and native/candidate remain.

- I125: earlier scheduler passes do not cover waiting admission across disposal or new queues missed by its
  snapshot. Corrected owned probes, affected Core/App and full host suites pass; shared consumers require clean
  CI/guest and native/candidate revalidation. Clean 749f55f now passes four CI lanes and 87 guest controls;
  wider resource lifetimes and native/candidate remain.

- I124: previous scheduler passes do not qualify watchdog replacement while appending workers or enforcement
  of the cap when all workers are quarantined. Corrected owned probes, affected Core/App and full host suites
  pass, as do four clean CI lanes and 85 guest consumer controls. Actual hung hardware, wider shutdown/queue
  lifetimes, aggregate decoders and native/candidate validation remain.

- I123: prior passing synchronization runs do not establish use of the shipping Windows adapter or a held
  replacement target. Controlled baseline/final, seventeen affected/full App, four clean CI lanes and 17 guest
  cases pass; native/candidate remain.
  The original failed CI remains retained; its unavailable decision request prevents exact attribution.

- I122: earlier picture evidence does not qualify the new provider-keyed scheduler route. Working/full host,
  36 clean guest and four successor CI lanes/all affected controls pass; original Windows synchronization failure
  is retained. Native/candidate remain. Watchdog/hard-cap and aggregate decoder bounds have separate
  uncompleted scopes.

- I121: prior picture/quick-view evidence does not qualify actual feed ownership after cancellation or F3 bitmap
  retirement. Working/full host, four clean CI lanes and 34 guest cases pass; native/candidate checks remain required.
  Per-device picture-feed bounds and other direct Source consumers remain outside this remedy.

- I120: the original MFT integration pass did not distinguish live-history prerequisites/omitted assertions.
  Its successor separates MFT and live-log cases; skipped history is not qualified. Host platform/golden
  controls, four clean CI lanes and 20 elevated guest cases pass, including complete history with zero affected
  skips. Candidate/native scopes remain required.

- I119: prior page-reader tests do not qualify disposal against active provider calls or retired refresh demand.
  Working component/full host, clean guest and four successor CI lanes pass; original I120 CI failure retained.
  Native/candidate checks remain required. Direct
  calls through Source, including picture feeds, are outside this remedy's protection.

- I118: earlier metadata cache/verification evidence does not qualify the new publication and demand lifetimes.
  Working component/full host, clean CI and guest controls pass; native frame/AT/candidate checks remain required.

- I116: prior initial archive-search logs do not prove provider warnings were visible. Working/full host,
  clean CI and guest controls pass; other-format/native/candidate checks remain required.

- I115: old result-narrowing evidence does not qualify archive-member revalidation or the revised log/navigation.
  Working/full host, clean CI and guest controls pass; other-format/native and candidate checks remain required.

- I113: old quick-view evidence does not qualify the new request, scheduler and bitmap lifetimes. Controlled
  headless/full host suites, clean CI and guest controls pass; native presentation/AT and candidate reruns remain required.
- I114: old parallel GnuPG fixture evidence may use another fixture's selected executable. Failure is retained;
  controlled tool swap and isolated full Core pass. Production GnuPG policy is unchanged.
- I92/I111: prior quick-search/streaming-first-row execution does not qualify the revised listing/UI bytes.
  Exact working/full affected suites, clean successor CI and Windows guest controls pass (E-I92/E-I111);
  native/reference/candidate input/frame evidence remains required. The pending-search caption needs native AT revalidation.
- I112: the old concurrent cache plateau observer is insufficient for quiescent accounting. Its failed result
  stays failed; held-read/completion and full corrected host inventories pass (E-I112). Cache policy is unchanged.
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
- I99 (Unix instance sockets/recovery write folders): previous Unix instance and recovery write-location evidence;
  affected boundary/CI and dev.539 native packages revalidated; separate-session remedy tracked as I100. Candidate reruns required.
- I100 (Unix instance lock/endpoint and usual-instance probe): prior Unix process-election/probe and recovery guard
  evidence; native working inputs, CI and both rebuilt Linux package baselines pass. Candidate evidence still required.
- I101 (runtime temporary-folder guard and endpoint metadata): earlier Unix recovery/endpoint metadata evidence;
  affected native suites/harnesses, CI and rebuilt Linux packages revalidated. Candidate write tracing still required.
- I102 (independent instance lifetime/profiles and guarded instance directory): prior recovery probe, independent
  startup/forwarding and write-location evidence; affected native/CI and rebuilt Linux checks pass; wider discovery/candidate tracing pending.
- I103 (Windows instance names/state identity): prior Windows election/forwarding/probe and portable/installed
  state-isolation evidence; preliminary process/guard/CI checks pass. Windows release package/candidate evidence pending.
- I105 (portable/per-user candidate and profile discovery): earlier recovery lookup/profile inventory evidence;
  affected native working-overlay, Windows and CI checks pass. Rebuilt native checks and candidate tracing pending.
- I106 (device process census): prior device admission evidence; ordinary-name native after and CI pass at their
  recorded identities. Renamed-apphost audit invalidates broader absence conclusions; executable-identity
  correction under validation. Wider visibility/race/availability audit and candidate tracing pending.
- I106 confirmation recheck: previous device admission results do not cover changes while confirmation is open;
  targeted baseline/after and affected App/native Windows inventory retained. Native Ubuntu and all affected
  successor CI lanes pass at their recorded identities; wider physical/candidate work pending.
- I106 Windows limited image query: previous Windows executable-lookup evidence does not qualify the changed
  App/platform bytes. Working native controls/full affected host inventories and clean 36ee824 guest/CI pass
  (E-I106-P2); candidate tracing remains pending. Unavailable identities still refuse admission.
- Physical fixture checker 2e6dffe/85bb17d: earlier exact positive comparisons remain evidence, but the broader
  truthfulness gate can hide corruption beside missing ranges or truncated claims. Controlled baseline/correction
  and exact native/CI checks pass; the exclusive stronger physical rerun with observed hashes passes (E-V09-G3).

## Next actions (unblocked)

1. I107 is closed for preliminary remediation: failure path/baseline reproduced, affected App/CI pass and corrected
   host/clean guest success supported by retained native trace. Windows Computer Use is independently unavailable. I106 native process/GUI guard
   and affected CI pass at their identities. Dev.549 formats pass successor checks on 26.04 (E-V19-P3); wider
   audit reproduces a renamed-apphost gap. Identity correction passes native positive cases and affected App
   checks; refined root census now establishes absence while ordinary-account visibility remains unknown.
   D8c6f3b successor CI passes; confirmation gap is also corrected, with Windows/Ubuntu native and all cc1acf2 CI
   passing. Continue visibility/runtime-alias/lifetime and write-location audits. Physical source-device checks
   now have the identity-bound USB on host G:, with owner authorization for disposable use. Strengthened guards
   and physical preflight pass; Windows census remains unknown even elevated after owned app teardown. Continue
   that availability correction. VMware routing drops before native guest execution; owner elevated host run then
   passes all three filesystem component scenarios. Corrected byte checker passes affected/native checks and all four
   successor CI lanes. Stronger attempt overlaps an older USB campaign and is invalidated. Interprocess correction
   passes controls; clean exclusive 090a2b6 successor passes all three physical formats (E-V09-G3).
   Installed-helper raw-read/source-write tracing still needs separate evidence; tracer controls pass (E-V09-G4).
   Read-only component source-hash/trace run at f2f0141 has differing full hashes and failed capture controls (E-V09-G6).
   Hold further USB tests; off-source timing matrix has mixed outcomes (E-V09-G8). Built-in WPR file/disk marker
   controls pass with zero reported loss (E-V09-G9); owned virtual-device raw read/write controls now pass,
   with exact native attribution and full fixture-byte oracle (E-V09-G10). Resolve the unexplained physical source
   change before resuming FileCat USB validation. Read-only diagnostic images match G6's after hash, but its long
   trace loses 51,216 events and fails source-write qualification (E-V09-G11). Narrower kernel short controls pass
   with zero reported loss (E-V09-G12). Nine-minute duration control now passes after short trace-scratch correction
   (E-V09-G13). Proof-gated read-only diagnostic now passes complete images, all source reads, zero reported loss
   and no source writes (E-V09-G14); historical G6 attribution is still unavailable and FileCat USB validation
   stays held. Windows structural diagnostic accounting passes but does not establish absence (E-I106-P1). Prepared
   Windows guest menus pass preliminarily; exact-candidate checks remain. Exact dev.539 Linux packages pass on both fresh
   Ubuntu baselines (ENV-04/I04/I99–I103), with raw evidence retained and independently verified. ReFS/Dev Drive and same-server SMB copy cases are done preliminarily (E-V03-CLONE-1),
   including I97's corrected rerun. Fuzz campaigns are already collected (item 109).
1b. V12, what is left: further viewport/page-load and picture-feed demand, rapidly
   changing viewports, visible rows beside a copy or a search, many folders counted and partial sizes after Esc.
   Done this session: page and archive budgets (I06), the watcher (I87), counts and analyses ending with their folder
   (I88, I91), quick-view initial-load demand and stale-result lifetime (I113 host/clean CI/guest pass),
   views closed while busy, million-entry listings (I92 host/clean CI/guest pass; native frame pending), many tabs.
   In-flight invalidation/retry and controlled rapid viewport demand now pass working/full host suites and clean
   CI/guest controls (I118). Native request/queue traces and UI-thread timing remain required.
   Page-reader calls now retire new demand and release active sources safely on close (I119 working/full host
   suites, 45 clean guest cases and four successor CI lanes pass; original I120 failure retained). Direct
   Picture feeds now retain active sources after cancellation and release closed F3 bitmaps (I121 working/full
   host, clean CI and 34 guest controls pass). Controlled per-device feeds now pass working/full host cases
   and 36 clean guest cases/four successor CI lanes/all affected controls (I122; original synchronization failure retained);
   watchdog/hard-cap, aggregate decoders, other direct Source use and wider queues remain open.
   Real timer/list mutation and controlled worker-cap failures now pass working probes, 85 affected cases and
   full host suites, four clean CI lanes and 85 guest controls (I124); actual hung hardware and wider/native/candidate
   scopes remain. Shutdown admission races now pass corrected probes/two new controls/87 affected cases and full
   host suites, four clean CI lanes and 87 guest controls (I125); wider/native/candidate remain.
   Comparison revision/length lifetime now passes owned probes, four regressions/affected and full App,
   four clean CI lanes and 91 guest controls (I126). Wider content/native/candidate remain.
   Inaccessible subtree/zero-byte counts now retain lower-bound labels and permit restored-access retry (I127):
   owned probes/affected/full host suites, four clean CI lanes and 122 SDK-free guest controls pass. Native/AT/
   candidate remain, alongside many marked folders and the other partial-count scenarios.
   Direct viewer line/page/Info read lifetime now passes four baseline-failure corrections/eight controls,
   affected/full host suites, four clean CI jobs and 143 guest controls (I128). Native/hardware/aggregate/
   candidate remain.
1c. V13: duplicates among a set and saved content criteria now pass working/full App controls (E-V13-F2);
   clean ab919ed/da3a3d6 CI passes all four lanes and affected Windows/Linux/macOS App cases; combined guest
   execution also passes (E-I117). Archive-result narrowing now
   passes working and clean CI/guest Core/headless Find flows (I115); TAR/gzip and initial warning propagation
   now pass working/full host and clean CI/guest controls (I116), with other formats still to validate.
   Candidate/native checks remain required.
1d. V16: ready-for-input and input-to-frame latency need the window on a desktop and the reference machine.
   Current desktop state was not observed: updated Computer Use import and plain Node startup both fail before
   input. Restore that runtime for live interaction; I92's worker remedy passes host/clean CI/guest controls,
   with native frame/AT checks pending.
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
candidates); I09's device-level zero-write cases (USB connected to host; source-change/capture investigation pending); steps 2, 5, 7 and
11–26 of the plan. I04's Ubuntu 26.04 environment is now available and the package remedy is under validation.
