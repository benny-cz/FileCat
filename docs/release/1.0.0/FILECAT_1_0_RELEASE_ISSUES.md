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
| I09 | Recovery whole-source safety: FileCat wrote to its own folders on the disk being recovered after only a warning; destinations behind loop devices, disk images, VHDs and shares served by the same computer were taken for other disks | Potential Critical (writes can overwrite the deleted files being recovered) | Safety gate (V09) | **Remediated preliminarily** (`27256f6`, `7418c04`, `0a52b7b`, `1df5a21`, `f241897`, `deaf776`; live topology checks on Ubuntu, macOS and the Windows VM; write traces in the Windows VM, E-V09-T1, and on the Ubuntu VM, E-V09-T2, `d39c402`); macOS, UDisks2 and the installed helper path pending |
| I10 | Documentation drift | Medium | Blocker where safety/support claims mislead | Open |
| I11 | Missing mandatory external evidence | Qualification blocker | Blocker | Open — resources |
| I12 | Historical regressions need durable coverage | Medium | Non-blocker once covered | **Covered** (`62bd88f`'s tests; `85d512d`) — closure pending re-audit |
| I13 | Latest features lack interaction evidence | Potential Medium–High | Gates open | Open |
| I14 | RAR decoder provenance / OSI-only eligibility | High | Blocker (license/signing) | Open |
| I15 | Uninstaller removed the whole installation folder | **Critical** (data loss) | Blocker | **Remediated `5b061cc`; verified in a VM** — closure pending re-audit and the final setup |
| I16 | Automatic browse/launch boundaries | Potential High | Security gate | **The three named items remediated `2f35a6b`**; the Git route's network evidence taken (E-V24-G1, which found [I69](#i69--a-repositorys-own-configuration-sent-git-to-a-server-while-the-folder-was-merely-shown)); the gate's remaining independent file, network and process evidence (V23/V24) open |
| I17 | Broker consent/loader/pipe completeness | Potential High/Critical | Security gate | **Consent display: remediated `33b7de2` + `5c54181`, verified in a VM.** Loader, pipe, requester, cancellation: open |
| I18 | Release control and pipeline provenance | High | Blocker (integrity) | Open; new detail below |
| I19 | Interrupted-copy cleanup deleted complete or user-changed files | **High** (data loss) | Blocker (non-waivable class) | **Remediated `f87ad32`; verified** — closure pending re-audit |
| I20 | `$LogFile` attributed an earlier item's operations to the current file | Medium (false forensic finding) | Must fix (confirmed D-56 surface; destabilized the required CI lane) | **Remediated `45efc09`; verified** — closure pending re-audit |
| I21 | The Registry's 32-bit and 64-bit views of HKLM, HKU and HKCC failed without administrator rights | Medium (confirmed feature broken in the default, unelevated mode) | Must fix | **Remediated `47c27b9`; verified** — closure pending re-audit |
| I22 | Replacing a file that is open failed on Windows with a misleading "Access denied"; a closed comparison kept its files open | Medium | Must fix (confirmed copy/sync surface; destabilized two required lanes) | **Remediated `63d5fc4`; verified** — closure pending re-audit |
| I23 | Network discovery listed a device by its address when its name arrived late | Low (name missing; device listed) | Must fix (confirmed feature; nondeterministic required test) | **Remediated `d40e510`; verified** — closure pending re-audit |
| I24 | The panels' Modified column shows no seconds by default | Low (UI) | Fix before release if time allows; owner-reported | **Remediated `2197074`** (seconds by default; screenshot checked) |
| I25 | Markdown files open as plain text; they should be shown rendered | Low (viewer) | **Required for 1.0.0** (owner, 2026-10-01), low priority | **Implemented `7abd0fe`; verified in WebView2** (E-I25) |
| I26 | Progress at 100% while an operation still works, and a time left that was not honest | Medium (confirmed: 100% for 63% of a verified copy) | Must fix; owner-reported | **Remediated `d40fda0`; verified** — closure pending re-audit |
| I27 | Linux: under the Adwaita 41 icon theme FileCat finds no file-type icons | Low (cosmetic; built-in icons shown) | Fix if time allows | **Remediated `4a4349f`; verified on the Ubuntu VM (Adwaita 41)** |
| I28 | A damaged NTFS size or data run made the whole volume unreadable to recovery; a damaged root record made the scan throw | Medium (recovery completeness; potential hang; a scan that throws) | Must fix (§17.3 robustness) | **Remediated `98fb594` + `bb977d0`; verified; fuzz campaign running** |
| I29 | A shell picture asked for while the helper already worked on it was asked again (CI red on ARM64) | Low (duplicate work; nondeterministic required test) | Must fix | **Remediated `7175a41`; verified; CI green** |
| I30 | Running operations should show what happens in the best possible way | Medium (UX of data-moving operations) | Owner priority: middle | **Remediated `67f70f9`; verified** (taskbar states seen on a real Windows 11 desktop) — closure pending V17 |
| I31 | Viewer windows are only partly themed (no theme effects, e.g. Psychedelic) | Low (cosmetic consistency) | Owner-reported; assessed | **Remediated `99a6ae4`; checked in pictures** |
| I32 | A folder's counted size vanished when the listing refreshed right after | Low (UX); made a required test fail 9 in 10 on a busy host | Must fix | **Remediated `6e9ee75`; verified** |
| I33 | A cancelled upload left its partial copy on the server | Medium (junk under a hidden name on the user's server; V08 interruption requirement) | Must fix | **Remediated `3ec60cc`; verified against real servers** |
| I34 | On a network share, replacing an open file still failed with the Controlled Folder Access message, and a share was named by the file system it claims | Low–Medium (misleading causes; I22's symptom on SMB) | Must fix (PI-07) | **Remediated `6585024`; verified against Samba** |
| I35 | "Read back and compare content" was silently ignored for uploads, downloads, extraction and copies to phones | Medium (a verification the user chose was not done, and nothing said so; PI-06) | Must fix | **Remediated `53b0794`; verified** (E-I35) |
| I36 | FTP names refused, trimmed or redirected by the FTP library (a look-alike could be listed, read or deleted instead) | Medium (wrong-item operations possible; legitimate names unusable; V08 fail condition) | Must fix | **Remediated `e50b9d4`; verified against vsftpd** (E-I36) |
| I37 | A damaged size made a recovery scan allocate gigabytes; a fuzz run took the Linux VM out of memory | Medium (scanning a damaged disk could exhaust memory; V09 bounded reads) | Must fix | **Remediated `02acee6`, `b9c41eb`, `0ec94f1`; verified** (E-I37) |
| I38 | FTP: every stat listed the whole folder on servers without MLST; each file stat'ed twice | Medium (2.65 s per small file at 100 ms; copies of large folders listed them once per file) | Must fix | **Remediated `f93f919`; verified against vsftpd** (E-I38-I39) |
| I39 | SFTP: uploads wrote one request at a time (0.29 MB/s at 100 ms) | Medium (remote copies over real-world links; a resumed upload missed its test limit) | Must fix | **Remediated `2ba114e`, `1dce2c2`; verified at 100 ms** (E-I38-I39) |
| I40 | Linux/macOS: FileCat's state folders were readable by other local accounts (history, journals, previews, hex originals) | Medium (confidentiality on multi-user systems with open homes; plan P16) | Must fix | **Remediated `8b0dafd`; verified on the Ubuntu VM** |
| I41 | SFTP connections held to SSH.NET's small socket buffers (1.2 MB/s down, 1.7 up at 100 ms) | Medium (every SFTP transfer over a real-world link; FTPS on the same link 10–14 MB/s) | Must fix | **Remediated `4c6b910`; verified at 100 ms** (E-I41) |
| I42 | Remote copies cost 1–1.5 s per small file at 100 ms (about 14 round trips per SFTP upload; one file at a time) | Low–Medium (folders of many small files over long links: 1,000 files ≈ 23 min up) | Owner decision (performance; post-1.0 candidate) | **Open — measured** (E-I41) |
| I43 | Uploads to FTP servers without MFMT (vsftpd) silently carried the time they arrived; downloads took the listing's coarse time | Medium (timestamps are data; sync and "newer" decisions rely on them; nothing said so) | Must fix | **Remediated `e527a86`; verified against vsftpd and OpenSSH** (E-I43) |
| I44 | Linux/macOS: a second FileCat on the same profile showed a running job as interrupted and offered its partial files for deletion | Medium (a live operation undermined; no data loss reachable) | Must fix (DPI P04) | **Remediated `e399276`; verified on macOS** (E-I44) |
| I45 | FTP listing times taken as exact: vsftpd's LIST gives minutes, or only the day for older files | Low–Medium (panels show invented seconds; comparing with such a server sees false time differences) | Should fix | **Remediated `111ebcd`; verified against vsftpd and ProFTPD** (E-V08-L2) |
| I46 | SFTP to ProFTPD: renaming, moving or setting aside a link renamed or moved its target instead | High (a different item than the one chosen moved, possibly elsewhere, silently; the link left dangling) | Must fix | **Remediated `3f1b554`; verified against ProFTPD and OpenSSH** (E-V08-L2) |
| I47 | FTP: an upload cut off on a server that will not continue it (ProFTPD) never finished; a dropped session stalled a minute | Medium (an interrupted upload could not complete; each retry refused; a 60 s stall) | Must fix | **Remediated `111ebcd`; verified against ProFTPD and vsftpd** (E-V08-L2) |
| I48 | A move across volumes deleted its source without checking that the copy was still at the destination | Medium (data loss when another program takes the new copy away at once: antivirus quarantine, sync clients) | Must fix (DPI P01) | **Remediated `e72e3fc`; verified** (E-DPI) |
| I49 | Moves to and from servers could delete what was never copied: a folder moved off a server went whole (with files that appeared or changed meanwhile); a moved local file went though it changed during the upload | High (silent data loss under concurrent change) | Must fix (DPI P09) | **Remediated `e72e3fc`; verified against OpenSSH, vsftpd and ProFTPD** (E-DPI) |
| I50 | Synchronize removed or replaced target items that changed after the comparison (while the plan was reviewed) | High (an edited file deleted, permanently where chosen, or overwritten by an older version) | Must fix (DPI P10) | **Remediated `99145cf`, folders again `efc128f`; verified** (E-DPI) |
| I51 | Linux/macOS: setting a link's read-only changed the item it points to | Low–Medium (metadata of an item outside the selection; links must not be followed) | Must fix (DPI P11) | **Remediated `65a76f8`; verified on macOS** (E-DPI) |
| I52 | FAT32: the files of a deleted folder were placed by a guess, wrongly, though their entries said where they start | Low–Medium (recovery quality: exactly recoverable files offered only as stated guesses) | Should fix (V09) | **Remediated `78a48ce`; verified on images Windows made** (E-V09-W1) |
| I53 | Hex editor: a patch that went over the limit of changed bytes was applied in part while the editor said nothing was applied | Medium (a later save writes a half-applied patch the user believes was refused) | Must fix (DPI P05) | **Remediated `7a99f9d`; verified** (E-DPI) |
| I54 | Hex editor, Linux/macOS: Save As kept a new file that could mix old and new bytes when another program wrote the file during the copy | Low–Medium (a silently inconsistent copy; Windows keeps other writers out) | Must fix (DPI P05) | **Remediated `7a99f9d`; verified on macOS** (E-DPI) |
| I55 | Registry: a .reg file FileCat exported from the 32-bit view could be imported into the default view, writing other keys | Medium (a restore from FileCat's own backup misses and overwrites values at the same paths in the other view) | Must fix (DPI P06) | **Remediated `cf92679`; verified** (E-DPI) |
| I56 | FTP: data connections followed the address a server's PASV reply named | Low (a server could aim uploads and downloads at another host; curl's CVE-2020-8284 is the same class, rated Low there) | Should fix (V23 B05) | **Remediated `ee476f0`; verified** (test server and the remote lab) |
| I57 | Network discovery followed HTTP redirects from a device's metadata address | Low–Medium (any device answering discovery could make FileCat send a request to another address, a service on this computer included) | Should fix (V23 B05) | **Remediated `a5c25d1`; verified** (fake device) |
| I58 | A damaged TAR header made .NET's TAR reader take up to 2 GiB before finding the data missing | Low–Medium (a 31 KiB archive took 512 MiB each time it was listed; a crafted one up to 2 GiB; then refused) | Should fix (V23 B02) | **Remediated `325aa63`; verified** (E-B02-A1) |
| I60 | The AppImage's runtime was whatever type2-runtime's "continuous" release held when the package was built, unchecked | Medium (supply chain: the first code to run when FileCat's AppImage starts, taken unverified from a moving release) | Must fix (V23 B09, part of I03) | **Remediated `84b847a`; verified** (packaging run 36855265633) |
| I61 | Command line: `--workspace` and `--list` were read, forwarded, and ignored | Low–Medium (plan §19.1 promises both; a launch with them opened nothing and said nothing) | Must fix (V23 B12, product claim) | **Remediated `dcd81a1`; verified** (E-DPI) |
| I62 | Profiles: two names for one profile's folders ran as two instances at once | Low (`--profile Work!` beside `--profile Work`: one profile's settings and journals in use by two FileCats) | Should fix (V23 B12) | **Remediated `2cd313f`; verified** (E-DPI) |
| I63 | Update check: the page an answer named was opened through the system's association, whatever it was | Low–Medium (one "Open release page" away from opening any address or local program, for whoever can alter the answer: an inspecting proxy, a compromise at GitHub) | Should fix (V23 B13) | **Remediated `9bedead`; verified** (E-DPI) |
| I64 | Names: a folder's name turned its tab, the path line and the command line's path around | Low (a right-to-left override in a folder's name made the shown location read otherwise; the file list already escaped it) | Should fix (V23 B14, §18.3) | **Remediated `e6e9ad0`; verified** (E-DPI) |
| I65 | Inspector: a PE whose optional header is shorter than its fields threw instead of warning | Low (an unexpected exception from the Info view for a damaged program; the inspectors promise warnings only) | Should fix (V23 B02, V24) | **Remediated `8cb0737`; verified** (E-B02-I1) |
| I66 | Recovery, FAT: a deleted file whose entry Linux cleared was called empty and recoverable | Medium (a 3 MiB deleted file listed as "0 bytes, recoverable: the file was empty", a false finding for the user who looks for it) | Must fix (V09, V11) | **Remediated `1477de3`; verified** (E-V09-T2) |
| I67 | Listing: every refusal showed only "Access is denied.", dropping the reason FileCat gave | Low (21 places give a reason, such as the system refusing a drive; the user saw none) | Should fix (V09, UX honesty) | **Remediated `134db5e`; verified** (E-V09-T2 L6) |
| I69 | A repository's own configuration sent Git to a server while the folder was merely shown | High (an unasked connection to an attacker-named server during ordinary browsing; 21 s per repository where it does not answer) | Must fix (V24, V23 B10) | **Remediated `aaee133`; verified** (E-V24-G1, with a packet capture) |
| I68 | Linux/macOS: a permanent delete reached into a file system mounted inside the deleted folder | High (deleting a folder that holds a mounted drive, share or bind mount emptied that volume too) | Must fix (V23 B01, DPI) | **Remediated `e5b4e3b`; verified** (unit test; live on the Ubuntu VM) |
| I59 | Registry: renaming a key checked by name that it was no link, then renamed by name, and Windows' rename follows links | Low (a process able to write the key's parent, winning a race, could make an elevated plan rename another key, the one a link names) | Should fix (V23 B07) | **Remediated `b02a01f`; verified** (E-DPI) |

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
  must not apply. SMB (Samba) done in E-V08-S1: the fallback does not apply there, and the message was still wrong —
  I34. FAT32 and exFAT done in E-I22-F1 (Windows 11 VM): the fallback does not apply, the question says "in use", and
  Retry replaces the file once it is closed.

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
- **Decision (owner, 2026-10-01):** needed for 1.0.0, low priority. Approach: a built-in renderer (headings, emphasis,
  code, lists, quotes, links shown but not followed, tables, task lists) that escapes all HTML, shown through the page
  engine with scripts off and no network; no new dependency, so the 1.0 dependency set stays as audited.
- **Severity / disposition:** Low; required for 1.0.0. Rendering must keep the page engine's containment — a Markdown
  file must not become a way to load remote content or run scripts.
- **Implementation (`7abd0fe`, E-I25):** a built-in renderer (CommonMark blocks and inlines with GitHub's tables, task
  lists, strikethrough, bare addresses and heading anchors) served through the page engine; the file's HTML is shown as
  text (a few attribute-free formatting tags and hidden comments apart), links lead only to the web, mail or the page,
  pictures load only from the file's folder, the page's policy forbids everything else; F3 opens Markdown drawn, F4
  shows its text.
- **Tests:** `MarkdownTests` (44, hostile inputs included), an App viewer test, and a real-WebView2 test (drawn, picture
  served, nothing requested from the web); WebKitGTK and WKWebView draw a Markdown file in CI run 36799348088 (E-I25).

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
- **Remediation (`4a4349f`):** that fix: the symbolic variants are looked for last (after the theme chain, hicolor and
  the pixmaps), drawn in the theme's text color, and drawn again when the theme changes.
- **Tests:** `FreedesktopIconsTests.A_theme_with_only_symbolic_icons_still_gives_types_their_icons` (a theme built in a
  temporary folder; every platform), `FreedesktopIconSourceTests.A_symbolic_icon_takes_the_text_color_and_keeps_its_shape`.
  On the Ubuntu VM (adwaita-icon-theme 41.0-1ubuntu1, theme Adwaita) the icon test that skipped for want of a text
  icon passes, the symbolic icon drawn by gdk-pixbuf (`i27-ubuntu-adwaita41.txt`
  `adbbb876643fe61eb3c7efc46c38d8097dddf3988d04bfd8076c70ad68d8faf6`). Not yet seen on its desktop.

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
- **Remediation (`67f70f9`, E-I30):** the phase and a percentage on the strip; a large file's own line and bar for its
  current step; the details open on the running operation with where from and to, phase, time left and running time,
  items, data copied and verified, speeds, and a speed graph along the operation; Windows taskbar progress (yellow
  while paused or waiting, red after a failure).
- **Verification (E-I30-V2):** each taskbar state drawn as intended on a real Windows 11 taskbar.
- **Remaining:** people's judgement in V17 sessions.

### I31 — Viewer windows are only partly themed

- **Reported:** by the owner, 2026-09-30 (low priority; "check whether worth to fix").
- **Observed:** the theme effects (`ThemeBackdrop`, `ThemeGlitchOverlay`) are placed only in the main window and the
  About dialog; viewer, comparison, find and synchronize windows take the theme's colors but not its effects
  (`i31-viewer-psychedelic.png` `1e53fb9d020de999cde4f16f96e087637983919fd4604a249a55a4157c522de8`: the viewer in
  Psychedelic is flat dark with pink accents, the main window glows).
- **Assessment:** worth doing as polish, not for release safety: a shared helper that puts the backdrop behind a
  window's tool and status strips (the main window's "glass bands"), keeping text, bytes and pictures on an opaque
  surface for legibility. Moderate effort (four to six windows). Queued.
- **Remediation (`99a6ae4`):** `ThemeLayers` puts the viewer, comparison, directory comparison, Find, hex editor,
  report and synchronize windows over the theme's backdrop and glitches; their strips show it, and their text, bytes,
  lists and pictures sit on the theme's card color. Pictures before and after (`i31/`): the viewer in Psychedelic
  (`viewer-before-Psychedelic.png` `c8940d59d015df56a5c6d16600529be9b9aeb160d80e8321a571e4ff040c4adb`,
  `viewer-after-Psychedelic.png` `3e4c72e0da997655024eb1336c49763ac96ca815dd63ae0c1d403470fba94044`) and Steampunk
  (`viewer-before-Steampunk.png` `ce2b2c30fabbbab7603a2e5dfe1621f5d5fd9be173c912aea3efef2fea9c6d6a`,
  `viewer-after-Steampunk.png` `ae8923d2bddcfc689fb043ba43f5b60ea6a37380375b67c12018101a61ea14af`); Classic Dark
  unchanged but for the card color under the text (`viewer-after-ClassicDark.png`
  `679017c6b0955450cd6182513a9eab4373a1b64e2c6e05a0103e351a8d19c287`); Find in Steampunk (`find-after-Steampunk.png`
  `c5ce70bd3219efa0d544ca508bd22d289f9934e386d7994b0044b684a85465f5`) and the hex editor in Psychedelic
  (`hex-after-Psychedelic.png` `c74d1e50b73e7d902278b65cdbd02d1f1792a7b7774897fed98fbd1e40abff7a`). App tests pass
  (184, 0 failed). Not yet seen on a real desktop or by the owner.

### I32 — A folder's counted size vanished when the listing refreshed right after

- **Discovered:** `PanelKeysTests.Space_marks_and_moves_on_so_holding_it_marks_and_sizes_everything` began to fail on
  the busy host (9 of 10 runs, also on commits before the campaign's latest work), after passing earlier.
- **Mechanism (diagnosed with a recording of the listing's changes):** the listing read the new folder while its
  contents were still being written, so it showed a modification time 0.5 ms older than the folder's final one; the
  size counted afterwards (correctly, 5000 bytes) was kept only while the folder had the time the listing showed; the
  change notification then refreshed the listing with the final time, and the current size was dropped.
- **Severity / disposition:** Low for users (the size can be counted again) but it made a required test fail on a busy
  machine; must fix.
- **Remediation (`6e9ee75`):** the size is tied to the folder's time read from the file system when the counting began:
  it stays while the folder keeps that time and goes when the folder changes afterwards.
- **Tests:** `ListingModelTests.A_folder_size_stays_while_the_folder_keeps_the_time_it_had_when_counted`; the Space test
  passes 10 of 10.

### I33 — A cancelled upload left its partial copy on the server

- **Discovered:** the new live-server tests (E-V08-L1): a 256 MB upload cancelled at a fifth left 54 MB under the
  upload's hidden temporary name (`.fc-…`) on the server, over SFTP and over FTP with TLS; nothing under the file's own
  name (the staging worked).
- **Mechanism:** the upload's cleanup ran only when the transfer failed; cancellation passed by it. Once routed there,
  the cleanup still failed silently: it found the temporary file through a folder listing that used the job's token,
  already cancelled.
- **Severity / disposition:** Medium — no data of the user's is lost, but junk the user cannot see stays on their server
  (quota, clutter), and V08 requires interruption to leave no partial state.
- **Remediation (`3ec60cc`):** a cancelled upload discards its temporary copy, and the cleanup lists the folder without
  the job's cancellation (still deleting exactly the listed file, never a link's target).
- **Revalidation:** the lab tests pass over SFTP and FTPS (11 of 11); the Remote suite passes (54, 16 skipped).

### I34 — On a network share, replacing an open file still said "Controlled Folder Access", and a share was named by the file system it claims

- **Discovered:** the new SMB lab tests (E-V08-S1, run 1 at `5183cd3`) against a Samba share:
  1. a copy with Replace onto a file open in FileCat's viewer on the share was refused and reported as "Access is
     denied. If the destination is a protected folder, Windows Controlled Folder Access may be blocking FileCat." —
     I22's symptom, on a share;
  2. moving a downloaded file there asked "NTFS cannot store its download origin (Mark of the Web)", and the copy's
     warning said the same: Samba reports its file system as NTFS by default.
- **Mechanism:** (1) I22's remedy is a POSIX-semantics rename, which SMB does not offer, and Samba, like Windows,
  refuses to rename over an open file; the refusal kept its "access denied" class and text. (2) The messages named the
  destination by the file-system name the volume reports.
- **Severity / disposition:** Low–Medium. No data at risk (the old file stays, the staged copy is removed; the move
  still asks), but both messages name a wrong cause (PI-07), the first in a common situation. Must fix.
- **Remediation (`6585024`):** when a replace is refused with access denied and no POSIX rename resolves it, FileCat
  opens both files for deletion: if both open (neither one's permissions forbid the rename) or the destination is held
  without shared deletion, the destination is open, and the refusal is reported as in use — with the quiet retries and
  Retry; folders and read-only files keep "access denied". The in-use text now names viewers ("… in use by another
  program or window (for example an open viewer or editor, or an antivirus scan)"). A network destination is named "the
  network share" in the metadata question and warnings, which now say Windows "may no longer" warn (a share's own zone
  may still warn).
- **Tests:** `TruthfulOutcomeTests.A_share_is_named_as_what_cannot_store_the_metadata_not_the_file_system_it_claims`;
  `SmbLabTests` (live, gated).
- **Revalidation:** SMB lab 7 of 7 at `6585024` (E-V08-S1 run 2); Core 556 (37 skipped) and Platform.Windows 118
  (22 skipped) pass on the host.
- **Limitation:** on a share, the replace still cannot happen while the file is open (the server refuses); closing it
  and choosing Retry replaces it. FAT32 and exFAT destinations (no POSIX rename either) take the same path and were
  run with that result (E-I22-F1).

### I35 — "Read back and compare content" was silently ignored outside copies between folders on disk

- **Discovered:** while planning V08's "alter resume tails and earlier content" case (E-I35): only the executor for copies
  between folders on disk read the verification choice; uploads to SFTP/FTP, downloads, extraction from archives (and
  moves from servers) and copies to phones passed it by, and said nothing.
- **Severity / disposition:** Medium — a verification the user asked for (or set as the default) was not performed, and
  the outcome looked the same as a verified one (PI-06: state weaker guarantees). Must fix.
- **Remediation (`53b0794`):** uploads read their copy back from the server before publishing it (which also catches
  bytes before a resume's checked tail that changed during a break); downloads and extractions read their source again
  before the copy takes its name; a mismatch discards the copy. Copies whose engine cannot read back (to a phone) end
  with "Not read back: … checked by their size only".
- **Tests / revalidation:** E-I35 (seven new tests, including V08's altered resume tail and earlier content); host
  suites green.

### I36 — FTP names refused, trimmed or redirected by the FTP library

- **Discovered:** the live odd-names test (E-V08-L1): FluentFTP 55 refused legitimate names as "injection" and showed its
  configuration advice to the user; probes found it also trims spaces from both ends of every path and turns backslashes
  into slashes, and that its parser of Unix-style listings (servers without MLSD) trims names' edge spaces (E-I36).
- **Severity / disposition:** Medium — over FTP, FileCat could list, read or delete a different item than the one named
  (a look-alike without the spaces, or `slash` in folder `back`), the V08 fail condition; legitimate names (`;`, `%`, `|`,
  `..`, bidirectional marks) could not be copied at all. Must fix.
- **Remediation (`e50b9d4`):** the library's heuristics off (line breaks still refused by it); exact names recovered from
  Unix-style listing lines; paths FTP cannot carry exactly (a backslash, a space at the path's end or start, NUL, line
  breaks) refused with the reason and a pointer to SFTP.
- **Tests / revalidation:** E-I36 (unit, pyftpdlib and live vsftpd tests; the new names test fails on the previous
  adapter).

### I37 — A damaged size made a recovery scan allocate gigabytes

- **Discovered:** the Ubuntu VM's fuzz runs ended without results; its kernel log showed an out-of-memory kill of a fuzz
  process holding 4.4 GiB, after which systemd stopped every other run and VMware Tools with it (they lived in VMware
  Tools' service group) (E-I37).
- **Mechanism:** a FAT boot sector's declared cluster count sized an in-memory table of up to 268 million entries (1 GiB
  for a 40 MiB image), past the check on the table actually read; a damaged NTFS `$Bitmap` size was read whole (up to
  512 MiB).
- **Severity / disposition:** Medium — scanning a damaged disk could exhaust a machine's memory (V09: reads are bounded).
  Must fix.
- **Remediation (`02acee6`):** the FAT cluster range is held to the volume's size (the FAT type still follows the
  declared count); the `$Bitmap` read to one bit per existing cluster. The fuzz harness now fails a round that allocates
  more than 256 MiB or eight times its image, and replays both rounds.
- **Tests / revalidation:** the two rounds fail before and pass after; 1,500 rounds per image pass with every image's
  worst round at 0–42 MB (E-I37); the Ubuntu runs restarted on `02acee6` as user services with a 1 GiB heap cap.
- **Follow-ups:** reviewing FAT and exFAT the way I28 reviewed NTFS (`b9c41eb`): exFAT's declared cluster count is held to
  the volume too (284 MiB allocated for a 16 MiB image with a damaged count and bitmap entry; such a volume now also
  reads instead of being refused), FAT long-name runs are capped at 20 entries, and NTFS compression units at the 64 KiB
  NTFS writes. The Ubuntu run on `02acee6` then stopped at NTFS round 169883 on its new allocation limit (1,596 MiB): a
  damaged compressed size made an object per unit for up to 10 million units no run described; units past the last run
  are now one lost stretch (`0ec94f1`). Both rounds are replayed in every run.

### I38 — FTP: every stat listed the whole folder on servers without MLST

- **Discovered:** V08's latency run (E-I38-I39): FTP cases took about six minutes at a 100 ms round trip; a command
  trace showed FluentFTP's `GetObjectInfo` listing the whole parent folder over a data connection for each stat on a
  server without MLST (vsftpd), and FileCat stat'ing every file twice on the way to reading it — 2.65 s for a 1 KB file,
  growing with the folder.
- **Severity / disposition:** Medium — copying a large folder from a common FTP server would list it once per file (a
  10,000-file folder: 20,000 listings); the listing also reported a link instead of following it. Must fix.
- **Remediation (`f93f919`):** without MLST a stat is `SIZE` and `MDTM` (which follow links, as the channel's contract
  says); content whose length is known opens without another stat: 0.85 s per small file at 100 ms.

### I39 — SFTP: uploads wrote one request at a time

- **Discovered:** V08's latency run (E-I38-I39): an upload grew at about 314 KB/s at a 100 ms round trip; SSH.NET's
  stream writes wait for each request's answer (0.29 MB/s), where its `UploadFile` keeps requests in flight (5.71 MB/s).
  Downloads were not affected (SSH.NET reads ahead; FileCat's position setting does not stop it).
- **Severity / disposition:** Medium — uploads over real-world links slowed twentyfold; the resumed-upload test missed its
  five-minute limit at 100 ms. Must fix.
- **Remediation (`2ba114e`):** a new upload goes through `UploadFile` (still created exclusively), fed by a stream that
  counts, paces to the speed limit and lets pause and cancel act at each read; a continued upload still appends with
  stream writes. The speed limit after a resume counted the bytes already on the server; it counts from the attempt.
- **Follow-up (`1dce2c2`):** a continued upload still writes one request at a time, so after a break FileCat starts
  again when that is at least twice as quick (the new upload's measured pace against one request per round trip), and
  says so; at 100 ms the 128 MB case went from 6 min 59 s to 3 min 31 s (E-I38-I39).
- **Limitation:** an upload cut off when most of it is on the server still continues one request at a time.

### I40 — Linux/macOS: FileCat's state folders were readable by other local accounts

- **Discovered:** the P16 review (state, diagnostics, caches, scratch): only the listing scratch was made private;
  settings and history, journals, diagnostics, caches, the temp folder of previews (archive members, remote files) and
  hex-save originals were created under the usual umask (0755).
- **Severity / disposition:** Medium — on a multi-user Linux or macOS machine whose home folders are open (Debian's are
  0755), any local account could read file names and file contents FileCat kept. Must fix.
- **Remediation (`8b0dafd`):** FileCat's roots are 0700; folders an earlier start made are tightened at the next one;
  where a mode cannot be set (a portable copy on a FAT stick), nothing fails.
- **Tests:** `PathAndStateTests.FileCats_own_folders_are_its_users_alone_on_Linux_and_macOS` (a 0755 root tightened; run on
  the Ubuntu VM, and by CI's Linux and macOS lanes).

### I41 — SFTP connections held to SSH.NET's small socket buffers

- **Discovered:** tracing V08's latency run after I39 (E-I41): through FileCat a 32 MB file went up at 1.8 MB/s and down
  at 1.3 at a 100 ms round trip, where SSH.NET alone (made the default way) moved 5.3 each way and FTPS 10–14.
- **Cause:** SSH.NET fixes a connection's socket buffers once connected — 137,072 bytes after `ConnectAsync`, which
  FileCat uses so that connecting can be cancelled, 685,360 after `Connect` — and a buffer set explicitly turns off the
  system's window tuning: one buffer per round trip. Clients made four ways isolated it (FileCat's own settings made no
  difference).
- **Severity / disposition:** Medium — every SFTP transfer over a real-world link ran at a fraction of what the link and
  the server offered (about 1.3 MB/s per 100 ms of round trip). Must fix.
- **Remediation (`4c6b910`):** the connector raises both buffers to 4 MiB after connecting (through SSH.NET's private
  session socket; SSH.NET has no setting). At 100 ms, 32 MB through FileCat's jobs: up 1.62 → 8.17 MB/s, down 1.23 →
  11.58 MB/s. 16 MiB left reads slow (not investigated), so 4 MiB.
- **Tests:** a guard that fails if an SSH.NET upgrade moves the socket (every platform), a local-sshd test on CI's Linux
  and macOS lanes (Linux caps the buffers at `net.core.rmem_max`), and a lab test from the Windows host (4,194,304).
- **Limitation:** Linux clients cannot be raised past `rmem_max`, and SSH.NET's own setting has already turned off
  Linux's tuning: they gain less (not measured).

### I42 — Remote copies cost 1–1.5 s per small file at 100 ms

- **Discovered:** the same trace (E-I41): each small file costs SFTP about 1.4 s up (writing the temporary copy, setting
  its time, checking its size, taking the name: about 14 round trips, since SSH.NET has the server resolve each path
  before acting on it) and 0.76 s down; explicit FTPS 1.5 s up and 0.85–0.95 s down. Files go one at a time over one
  connection.
- **Severity / disposition:** Low–Medium (performance, not correctness): a thousand small files 100 ms away take about
  23 minutes up. Fewer requests per file and several files in flight over the pool's connections would both help; the
  latter changes the job engine. Owner decision whether before or after 1.0.0.

### I43 — Uploads to FTP servers without MFMT carried the time they arrived

- **Discovered:** the channel trace of E-I41: setting a time took no time at all over FTP. FileCat sent nothing when the
  server lacks MFMT (vsftpd, a common Linux FTP server), so every uploaded file showed its arrival time — and said nothing,
  although "keep timestamps" is on by default. Downloads set the time from the listing, which vsftpd gives to the
  minute, or for older files only the day.
- **Severity / disposition:** Medium — timestamps are part of the data (sorting, backups, Synchronize's "newer"
  decisions), and the loss was silent. Must fix.
- **Remediation (`e527a86`):** without MFMT FileCat sends MDTM with a time, which vsftpd takes as setting it (other
  servers answer it as a question about an odd name and change nothing). The size check after each upload reads the time
  as well; files whose time did not hold are counted and named once at the end of the job. A server refusing to set
  times over SFTP no longer fails the upload; "keep timestamps" off sends no time. Downloads use the time the source
  states when the content is opened (MDTM, to the second) instead of the listing's.
- **Tests:** `SftpJobTests.Uploads_keep_modified_times_and_say_so_where_the_server_does_not`; the lab's tree case now
  checks every file's time on the server and after the way back — it fails on vsftpd (both FTPS modes) before the
  change and passes over SFTP and both FTPS modes after it (E-I43).

### I44 — A running job shown as interrupted to a second FileCat (Linux, macOS)

- **Discovered:** reviewing DPI P04 (journal reconciliation): FileCat can run twice on one profile (`--new-instance`,
  "one window per profile" turned off, or a first instance not answering), and each start scans the job journals. On
  Linux and macOS a running job's journal read like a crashed one: the second FileCat showed the job as interrupted and
  offered its partial files for deletion, its renames for finishing, a rerun, and closing its journal. On Windows the
  read failed on the writer's sharing mode and the journal was skipped, by accident.
- **Severity / disposition:** Medium. No data loss was reachable (a move checks its copy by path before deleting the
  source, and deleting a partial copy re-checks it), but a live operation could be undermined or duplicated. Must fix.
- **Remediation (`e399276`):** a journal is probed for its writer before it is read: the writer keeps it open sharing
  only reads, which .NET backs with an advisory lock on Linux and macOS, so asking for it alone fails while the job
  runs. (On network home folders .NET takes no such lock; there the old behaviour remains.)
- **Tests:** `JobEngineTests.A_job_still_running_is_not_interrupted_even_to_another_FileCat` — fails on the owner's Mac
  before the change, passes after; the journal suites pass on Windows and macOS (E-I44).

### I45 — FTP listing times taken as exact

- **Discovered:** while fixing I43: on a server without MLSD (vsftpd), FileCat reads times from LIST, which gives the
  minute for files changed in the last half year and only the day for older ones (`Mar 04  2021`). FileCat treats them
  as exact: the Modified column shows seconds (`00`) that were never stated, and comparing a local tree with such a
  server — or synchronizing from it — sees times that differ by up to a day where the files are the same.
- **Severity / disposition:** Low–Medium (truthful display; comparison and Synchronize decisions). Should fix.
- **Remediation (`111ebcd`):** entries carry how precisely their time is known (from each listing line: MLSD to the
  second, Unix LIST to the minute or the day, other LIST formats to the minute); panels show only that much (a day as the
  server's date, not moved into another by the local time zone); comparisons treat such a time as every moment of its
  minute or day. Unit tests for the comparison, the line precision and the display; the lab's tree case compares the
  local tree with its copy on vsftpd and on ProFTPD by size and time and finds them the same (E-V08-L2).

### I46 — SFTP to ProFTPD: renaming or moving a link renamed or moved its target

- **Discovered:** V08's second implementation (E-V08-L2): against ProFTPD 1.3.7c's `mod_sftp`, FileCat's lab case found
  the targets moved and the links dangling. OpenSSH's own `sftp` client gets the same from that server — the server
  resolves a link given to RENAME — while OpenSSH renames the link; removing a link is right on both.
- **Severity / disposition:** High — a different item than the one the user chose is renamed or moved (into another
  folder, or out of one, a whole folder included), silently, and the link no longer leads anywhere. Must fix.
- **Remediation (`3f1b554`):** SSH.NET cannot read a link to check a rename first or undo it, so FileCat renames, moves
  and sets aside links over SFTP only on servers that identify as OpenSSH; elsewhere the item fails with the reason, and
  no retry question is asked. FTP servers (RNFR/RNTO) rename links themselves (vsftpd and ProFTPD in the lab).
- **Tests:** `SftpJobTests.Links_are_not_renamed_moved_or_set_aside_where_the_server_may_rename_their_targets`; the lab
  case holds on every server that targets stay where they were, and passes on OpenSSH, vsftpd and ProFTPD.
- **Limitation:** other SFTP servers are not known, so link renames are refused on them too.

### I47 — FTP: an upload cut off on ProFTPD never finished; a dropped session stalled a minute

- **Discovered:** E-V08-L2: the FTPS cut-off case against ProFTPD ran into its time limit. A trace: closing the broken
  transfer waited the whole 60 s read timeout for a reply from the killed session; then ProFTPD refused to append
  (`451 Append/Restart not permitted, try again`, its default), FileCat asked the user, and each Retry was refused the
  same way.
- **Severity / disposition:** Medium — an interrupted upload to such a server could not complete without starting it
  again by hand, and every break cost a minute of apparent stall. Must fix.
- **Remediation (`111ebcd`):** a server's refusal to continue makes the upload start again, saying why (the channel's
  contract said so; the upload did not do it). After a broken transfer FileCat waits 5 s for the server's verdict,
  keeping a reason such as a full quota, then ends the connection without QUIT and reports it lost, so Retry reconnects.
  FTP replies are quoted in the server's words (FluentFTP repeated the code first).
- **Tests:** `SftpJobTests.A_dropped_upload_starts_again_where_the_server_will_not_continue_it`; the lab's cut-off case
  passes on ProFTPD (started again) and vsftpd (continued), each in under half a minute.

### I48 — A move deleted its source without checking its copy

- **Discovered:** DPI P01 review (E-DPI): `DeleteMovedSource` re-checked the source (unchanged, not the destination
  through a link) but not the copy; between the copy's publication and the source's deletion another program can take
  the new file away (an antivirus quarantine, a sync client), and the move then deleted the only copy.
- **Severity / disposition:** Medium — data loss, though it needs another program acting in that moment. Must fix.
- **Remediation (`e72e3fc`):** the copy must be at the destination, whole, just before the source goes; otherwise the
  source stays and the job says why.
- **Tests:** `TruthfulOutcomeTests.A_move_keeps_its_source_when_the_copy_is_gone_before_the_source_would_go` fails
  before (the file was gone from both places) and passes after.

### I49 — Moves to and from servers could delete what was never copied

- **Discovered:** DPI P09 review (E-DPI). A move from a server copied a folder, then deleted it on the server as a whole
  (a fresh listing, everything in it): files that appeared there during the move, or changed after they were copied,
  went with it, never copied. A move to a server deleted the local file once its copy was published, even if it had
  been saved again meanwhile (local moves keep such a source).
- **Severity / disposition:** High — silent loss of other people's or the user's own newer data whenever the source
  changes during a move. Must fix.
- **Remediation (`e72e3fc`):** the copy step reports each file with the version its source stated when it was read; the
  move deletes exactly those, each only while a fresh stat shows that version, and folders only once empty; anything
  else stays and is named. A move to a server keeps a source that changed during the upload.
- **Tests:** two `SftpJobTests` cases (both fail before), and a lab case moving a tree off OpenSSH, vsftpd and ProFTPD.

### I50 — Synchronize removed or replaced target items that changed after the comparison

- **Discovered:** DPI P10 review (E-DPI): Synchronize's plan becomes ordinary jobs — removals as Delete or Recycle,
  replacements as copies that replace — acting on whatever is at each path when they run, though the plan may be
  reviewed for a while first.
- **Severity / disposition:** High — a target file edited meanwhile was deleted (permanently where chosen) or replaced by
  the source's older version; a folder planned for removal went with files added since. Must fix.
- **Remediation (`99145cf`):** each removal goes only while the item has the size and time the comparison saw (a folder
  its time, which changes when items are added to it or removed; changes deeper inside are not seen), each replacement
  only while the target file is as compared; anything else stays and is named, to compare again.
- **Tests:** `SyncTests.Mirror_removes_a_target_item_only_while_it_is_as_compared` and
  `SyncTests.Mirror_replaces_a_target_file_only_while_it_is_as_compared` fail before (the edited file deleted;
  overwritten by "left d") and pass after.
- **Follow-up (`efc128f`):** the check of a folder by its own modified time failed both ways on NTFS, which CI showed
  as two flaky tests from `99145cf` on. The time a listing shows for a folder lags its own time by up to a few
  milliseconds after something is created in it (61–83 of 200 probes on the host's E: drive), so an untouched folder
  was refused. An item added within the same clock tick leaves the folder's time as it was (22–53 of 200), so a folder
  holding a file added after the comparison was removed with it (CI, Windows ARM64). Nor does a folder's time ever
  tell of changes deeper in. Now the comparison reads all a one-sided folder holds (files, folders, sizes, times: counts
  and one fingerprint) and the plan says it; a folder goes only while a fresh reading is the same, and one that could
  not be read in full is not offered. On E:, `SyncTests` failed 10 and 5 of 40 runs before, 0 of 40 after (probe
  `i50-dir-times-probe.txt` `23e5fe5a4293cc05655b5726fd6a8a6ebd06bfc7774e199a2ff6d20831e7c340`, runs
  `i50-folder-check-stress.txt` `7dd4394666637dcf2f3872e8053292b4fc6c66323dad7e11743f0c728eeacc64`). New tests:
  `SyncTests.Mirror_removes_a_folder_only_while_all_it_holds_is_as_compared` (a change two levels down keeps the
  folder), `TreeCompareTests.What_a_folder_holds_reads_the_same_until_something_in_it_changes`.

### I51 — Linux/macOS: setting a link's read-only changed the item it points to

- **Discovered:** DPI P11 review (E-DPI): the attribute job applied read-only to a link chosen itself with .NET's
  `File.SetAttributes`, a chmod on Linux and macOS that follows the link. On the owner's Mac the target became
  read-only. Recursion never entered links; permissions were already guarded; times are set on the link itself.
- **Severity / disposition:** Low–Medium — the metadata of an item outside the selection, possibly anywhere. Must fix.
- **Remediation (`65a76f8`):** on Linux and macOS a link's attributes are left as they are, with a note.
- **Tests:** `AttributeLinkTests.Changing_a_links_time_or_read_only_never_changes_what_it_points_to` fails on macOS before
  and passes after; passes on Windows throughout.

### I52 — FAT32: the files of a deleted folder were placed by a guess though their entries said where they start

- **Discovered:** V09 check on disk images made by Windows' own drivers (E-V09-W1): on FAT32, the two files of the
  deleted folder `photos` came back "Uncertain", read from blank space.
- **Mechanism:** Windows erases the upper half of a deleted FAT32 entry's first cluster number, so FileCat weighs every
  place the lower half allows. The entries in `photos` were never marked deleted on disk: the folder was deleted right
  after its files, and its listing, which Windows writes back lazily, was freed before those marks were written. Their
  numbers were whole, but FileCat took every entry inside a deleted folder for a half-erased one. It weighed 53 and
  65,589 for `a.jpg`, and since the fixture's ".jpg" files hold text, the blank place ranked first.
- **Severity / disposition:** Low–Medium. Files that could be recovered exactly were offered only as guesses, here
  from the wrong place. FileCat always said so, so it never made a false claim of recovery. Should fix (V09).
- **Remediation (`78a48ce`):** only an entry marked deleted itself counts as half-erased.
- **Tests:** `ErasedFatStartTests.Entries_a_deleted_folder_kept_unmarked_start_where_they_say` fails before (`A.JPG`
  Uncertain) and passes after; on the Windows-made FAT32 image 6 of 6 files come back byte for byte after (4 before).

### I53 — Hex editor: a patch that went over the limit of changed bytes was applied in part

- **Discovered:** DPI P05 review (E-DPI). Applying a patch checks every range's expected bytes first, then stages the
  ranges one by one as unsaved edits. The overlay refuses an edit that would take its changed bytes over 8 MiB, so with
  edits already made a patch could stop part way. The editor then said "Patch not applied … Nothing was changed",
  while the ranges staged before the stop stayed as unsaved edits.
- **Severity / disposition:** Medium. The user believes the patch was refused, keeps editing and saves, and the save
  writes a half-applied patch into a binary. Must fix.
- **Remediation (`7a99f9d`):** the whole patch is checked against the limit, beside the edits already made, before
  anything is staged. Should reading the file fail part way, the editor says how many ranges were applied as unsaved
  edits, and that Undo removes them.
- **Tests:** `HexEditingTests.A_patch_that_does_not_fit_beside_the_edits_made_is_not_applied_in_part` fails before
  (8,388,608 changed bytes instead of 7,340,032: 1 MiB of the patch left staged) and passes after.

### I54 — Hex editor, Linux/macOS: Save As could keep a copy mixing old and new bytes

- **Discovered:** DPI P05 review (E-DPI). On Linux and macOS no program can be kept from writing a file another has
  open. The in-place save checks every byte it replaces and says so before it runs, and its warning points to Save As
  as the alternative. Save As, however, read the whole file without noticing a write during the copy.
- **Severity / disposition:** Low–Medium. The new file could hold the old bytes of what was read before the write and
  the new bytes of the rest, without a word. Windows keeps other writers out while the editor has the file open. Must fix.
- **Remediation (`7a99f9d`):** the file's length and modified time must be the same at the end of the copy as at its
  start; otherwise the copy is not kept, and the editor says why.
- **Tests:** `HexEditorPosixTests.Save_as_keeps_no_copy_of_a_file_another_program_wrote_meanwhile` fails on macOS
  before (no exception; the mixed copy kept) and passes after.

### I55 — Registry: a .reg file exported from one view could be imported into the other

- **Discovered:** DPI P06 review (E-DPI). FileCat's .reg export, including the backup offered before deleting Registry
  keys, names the view the keys were read in only in a comment, and the import ignored it. In the 32-bit view the same
  path text names other keys than in the default (64-bit) view. A backup of a 32-bit-view key, restored as its hint
  says from the default view, would have written the 64-bit keys at those paths.
- **Severity / disposition:** Medium. The restore misses its keys, and values already at the same paths in the other
  view are overwritten (the preview counts them, but does not say they are in the wrong view). Must fix.
- **Remediation (`cf92679`):** the import refuses a file FileCat exported from the other view and names the view to
  open. Files from regedit carry no such comment, and their paths name `WOW6432Node` themselves.
- **Tests:** `WindowsRegistryProviderTests.A_reg_file_FileCat_exported_from_one_view_is_not_imported_into_another`
  fails before (no exception) and passes after; the Registry provider tests pass.

### I16 — Automatic browse and launch boundaries: the three items the plan names

- **Git badges:** a repository's ".git" file (a linked work tree) and its "commondir" name other folders, and FileCat
  checked them with ordinary file calls before deciding anything about them: a downloaded folder naming a network path
  made Windows try to connect there while the folder was merely shown (the call took 21.1 s to fail against a
  documentation address, a missing local path 0.8 ms). Now a path that is not on this computer means no badges,
  decided from the path itself first.
- **Icon resources:** an icon named by a user's file (a folder's desktop.ini) had its time read before the helper's
  policy (files on this computer only, unless allowed) was asked; now the policy decides first, and a refused path is not
  touched.
- **Programs by name:** gpg was looked for through every PATH entry, relative ones included, which follow FileCat's
  current directory. The same held for external tools and Windows' terminal lookup, and several programs were started
  by bare name, which Windows (and .NET on Linux and macOS) also looks for in the current directory first: Windows
  PowerShell, the fallbacks for PowerShell 7 and Windows Terminal, the openers, terminals and keep-awake helpers on Linux
  and macOS. Now every one is found by full path through PATH's absolute entries and the usual folders, or reported as
  not found.
- **Remediation (`2f35a6b`)** with tests: `GitStatusTests` (linked work tree and commondir on a network path: no badges,
  at once), `IconResourceTests`, `VerificationTests.Gpg_is_found_by_full_path_never_through_a_relative_PATH_entry`,
  `ContentAndToolTests.Programs_are_found_by_full_path_never_through_a_relative_PATH_entry`, `ProgramLookupTests`.
- **Still open:** the gate's independent file, network and process evidence (V23/V24) on the candidate. The Git route
  has it preliminarily (E-V24-G1, under a packet capture); it found that the guard above stopped at the paths FileCat
  follows itself and not at what the repository's configuration makes Git open ([I69](#i69--a-repositorys-own-configuration-sent-git-to-a-server-while-the-folder-was-merely-shown)).

### I12 — The two historical failures have lasting coverage

- **`$Secure`** (plan §6.3): the comparison that failed on CI's Temp folder (a DACL stored without inheritance marks, which
  Windows reports marked and reordered) is held by `62bd88f`'s tests: `Security_descriptors_are_compared_part_by_part_not_as_text`
  (the stored and reported descriptors as structures, the unmarked case among them, and real differences named) and
  `As_administrator_a_DACL_stored_without_inheritance_marks_is_the_same_as_Windows_reports` (a live folder set that way,
  compared with Windows' own report, GetSecurityInfo, as the independent oracle; it runs on CI's elevated Windows lanes).
- **macOS page title** (CI run 36711390817): the page engine smoke now runs twelve lifecycles, each engine showing two
  pages in one view, each title as observed, then disposed with the loop run on, counting events raised afterwards
  (`85d512d`): 12 of 12, 0 events after disposal on the owner's Mac (`i12-page-smoke-mac.txt`
  `da193a7a5fffe5a7d1703c874000c6239299223e4afbcd18991f78bc68c021f2`); CI's macOS lane runs it. The original failure
  was intermittent and cannot be forced, so the phase shows the lifecycle holds, not that it would have caught it.

### I09 — Recovery scanned a disk FileCat itself writes to, and took some destinations on the source disk for other disks

- **Found by:** the V23 review of trust boundary B08 (image/device → raw access and output) against plan V09, whose
  pass criterion says a warning followed by writes fails. Earlier, the DPI review's P14 row called the warning enough;
  that was wrong (corrected in E-DPI).
- **What was wrong:**
  1. A scan of a drive or disk only warned that FileCat keeps "settings and logs" on that disk, judged by the journal
     folder alone, and then went on writing there: settings, history (written as the scan's folders are visited),
     logs, journals (an F5 copy writes one), the administrator helper's exchange, caches, the listing scratch (on the
     user's local disk even for a portable copy), hex originals, page view data.
  2. Destinations and FileCat's folders were placed wrongly in cases V09 names. Linux: a missing sysfs entry counted as
     "on no disk"; a folder reached through a link was placed by the link's own mount; a loop device written to counted
     as a disk of its own, not as its backing file's disk; a btrfs file system counted as only the device mounted; a
     share served by this computer counted as remote. macOS: written into, a disk image counted as itself only; network
     mounts came out unknown. Windows: shares served by this computer (`\\localhost\C$`, its own name and addresses) and
     disks made from a file or from other disks (VHD/VHDX, storage spaces) counted as other disks. On the host, the
     campaign's `ca91908` build answered "another disk" for `\\localhost\C$\x` against C: (checked by loading it).
     Links and junctions on Windows were already followed (`GetVolumePathName` resolves them; checked the same way).
  3. A device's scan could open without its checks: the persisted folder history recorded device scans, and opening
     such an entry later started reading the device. A disk plugged in under a chosen disk's number (Windows
     `PhysicalDriveN`, Linux `/dev/sdX`, macOS `diskN`) between the choice and the approval was read in its place.
- **Remediation:**
  - `27256f6`: every folder FileCat writes in is listed (`AppPaths.WriteFolders`); a drive or disk whose disk holds
    any of them, or where that cannot be told, is not scanned, and the refusal names them and gives the command that
    starts FileCat with everything it writes in one folder on another disk (`--data`, new; a FileCat started that way
    also waits while the usual one runs with its files on that disk). While a scan is open, the Shell is asked for no
    pictures and gpg is not run when their folders lie on that disk (both write there). A device opens only from the
    drive's own command in this session, and only at the size it had when chosen. The topology changes above.
  - `7418c04` (found by the live checks): a loop device or disk image written into counted only as its backing file's
    disk, so recovering an image into itself was allowed; it now counts as itself as well. Folders under `/dev`
    (`/dev/shm`) were taken for devices.
  - `0a52b7b`: macOS answers kept for 20 s (each `diskutil` round took 2–3.6 s under load; the fourteen folders now
    take 4–7 ms once one is known).
- **Tests:** `UnixDeviceTests.Where_writing_goes_is_told_by_real_paths_backing_files_and_servers_and_unknown_never_counts_as_elsewhere`
  (a sysfs and mount table laid out as Linux does, every platform), `…A_folder_reached_from_memory_through_a_link…`
  (Linux), `ThisComputerTests`, `DeviceReadTests.Destinations_are_judged…` (shares served here) and
  `…A_folder_reached_through_a_link…` (Windows), `RecoveryJobTests.A_drive_opens_only_as_the_user_chose_it`,
  `PathAndStateTests.With_a_data_folder_everything_FileCat_writes_is_in_it…`, `VerificationTests.While_a_disk_holding_GnuPGs_folder…`,
  `ShellHostTests.While_paused_the_helper_is_asked_for_nothing…`, `RecoverySafetyTests` (the refusal, the hold-offs,
  `--data`, the usual instance). Full Core, Platform.Windows and App suites on the host: 642/0 failed, 125/0, 187/0.
- **Live checks** (the gated `Where_writing_goes_on_this_system_is_what_the_tester_expects`): Ubuntu VM, `7418c04`
  (`i09/ubu-topology-7418c04.txt` `d261d42e135c6bcbadbb31b96af193c7ee2ecc2cd9b5c8614c83ee164788642a`): a loop-mounted image whose file is on
  sda — shares sda; the loop device read, recovering to sda — separate; into itself — shares; `/dev/shm` — memory; a
  link from it to sda — sda. Owner's Mac, `7418c04` (`i09/mac-topology-7418c04.txt` `5343619e45384b5a2ca496fdd760ad5f1046432f5eb6b8107dd1e3b368e421cb`):
  the same with an attached disk image whose file is on disk0 (APFS's physical store). Windows VM, `0a52b7b`
  (`i09/win-topology-0a52b7b.txt` `6a3a0f8c2946eaea9b2a1267155674e56166036eda44ca2648927c4d5ca1e3bc`): a VHDX whose file is on disk 0 —
  unknown (refused), read — separate, into itself — shares; `\\localhost\C$`, `\\127.0.0.1\C$` and
  `\\DESKTOP-A60F1NE\C$` — unknown. Not run live: a CIFS share served by the Ubuntu VM itself (no `mount.cifs`
  there; the unit test covers the mount table's form).
- **Write traces (E-V09-T1):** in the Windows VM under Process Monitor: a dismounted source disk scanned with FileCat's
  files on another disk was opened for reading only and hashed the same before and after; the VM's own system drive
  scanned with FileCat's files on another computer's share got no file of FileCat's, only NTFS writing its own pending
  metadata while FileCat read the mounted volume (disclosed, and the question's wording corrected in `deaf776`); with
  FileCat's files on the system drive, its scan was refused before any device access. On the way: a VHDX data disk was
  refused as unknown (`1df5a21` places a VHD by its file), and the Shell and gpg are now held off from the moment a
  disk is chosen (`f241897`).
- **Write traces on Linux (E-V09-T2):** under `strace` on the Ubuntu VM: a loop device's image unchanged (hash) and only
  read; the system disk's EFI partition and the whole system disk read only, and in the whole-disk case not one write of
  FileCat's processes on any disk; FileCat's own files on the system disk: refused before opening it. Found on the way
  and fixed in `d39c402`: in a FileCat started with `--data`, asking whether the usual FileCat runs looked up a named
  mutex, which .NET keeps in files under `/tmp`, so choosing a disk wrote to the temporary folder; the `--data` command
  now also moves .NET's own endpoints (`TMPDIR`).
- **Residual risk and what V09 still needs:** macOS (`fs_usage`, authopen), UDisks2 for a user who may not read the
  device, the installed helper path, approval refusal and device removal, and the final candidate's package rather than
  the test host. Writes FileCat cannot place stay possible: Windows itself on its own disk
  (registry hives, prefetch, error reports), access times the system updates when the user browses the mounted source,
  memory file systems swapping to a swap area on the source. A disk swapped for one of exactly the same size between
  the choice and the approval is not noticed (this host has two such disks). The usual FileCat is only noticed when
  it was started normally under the same profile. A VHD destination is refused as unknown rather than placed.
- **Severity / disposition:** Potential Critical where it happens (the deleted files being recovered can be
  overwritten); remediated preliminarily; closure needs V09's write trace and the final-candidate evidence.

### I56 — FTP data connections followed the address a server's PASV reply named

- **Found by:** the V23 review of B05 (remote server → local work). FileCat used FluentFTP's AutoPassive: EPSV, then
  PASV, whose reply names an address; FluentFTP's own log strings show it replaces only unroutable addresses
  ("PASV advertised a non-routable IPAD. Using original connect dnsname/IPAD").
- **Reproduction:** `FtpIntegrationTests.Data_connections_go_to_the_server_whatever_address_its_PASV_reply_names`
  (pyftpdlib without EPSV, naming 203.0.113.7): before, the upload timed out connecting there (22 s,
  `RemoteDisconnectedException: Timed out trying to connect to IP #1`).
- **Remediation (`ee476f0`):** once connected, the named address is ignored: PASV with the server's own address on
  IPv4 (PASVEX), EPSV (a port only) on IPv6.
- **Verification:** the FTP suites (45 tests, 1 skipped) and the remote lab against the Ubuntu VM's OpenSSH, vsftpd
  (explicit and implicit FTPS) and ProFTPD: 21/21 (`v08-b05pasv-remote.trx` `e93daa3475287fedeea9519006eb714a311dd0b3c681406207c23960b0e625c9`).
- **Severity:** Low, as curl rated the same class (CVE-2020-8284): the server already receives the data; what it gains is
  another host's position on the user's network.

### I57 — Network discovery followed redirects from a device's metadata address

- **Found by:** the V23 review of B05. FileCat asks each WS-Discovery answer's own address for the device's name (the
  I23 work made sure of the address), but its HTTP client kept .NET's default of following redirects.
- **Reproduction:** `NetworkDiscoveryTests.A_device_whose_metadata_redirects_elsewhere_is_not_followed_there`: a fake
  device whose metadata answers 302 to another port of this computer; before, discovery connected there.
- **Remediation (`a5c25d1`):** redirects are not followed; the device is then listed by its address.
- **Severity:** Low–Medium: a request (no credentials, no cookies) to an address of a network neighbour's choosing,
  services that trust requests from this computer included.

### I58 — A damaged TAR header made .NET's TAR reader take up to 2 GiB before finding the data missing

- **Found by:** the archive damage campaign (E-B02-A1, trust boundary B02): TAR round 97053 allocated 512 MiB on the
  thread that read a 31 KiB archive (the host at `b0b2329`, and again alone at `c22c793`; the Mac's run stopped on the
  same class).
- **Cause:** .NET's `TarReader` reads a PAX extended header or a GNU long name ('x', 'g', 'L', 'K') whole, into an array
  it rents at the size the header's size field gives (up to about 2 GiB), before reading the data. The round changed one
  digit of a PAX header's size (to 268 million bytes): the reader rented 512 MiB, then found the end of the file (a
  refusal). The reader does not check a header's checksum; the damaged header was taken as it was.
- **Reproduction:** `ArchiveFuzzTests.Rounds_that_once_failed_stay_fixed("tar", 97053)` and
  `ArchiveFormatTests.A_TAR_member_claiming_more_metadata_than_FileCat_reads_ends_the_list_without_taking_the_memory`
  (PAX and GNU, plain and gzip: the third member's metadata header claims 300,000,000 bytes, with a right checksum).
  Both failed before the fix.
- **Remediation (`325aa63`):** a stream between the archive and the reader follows the TAR framing and checks each
  metadata header as it passes, before the reader acts on it: more than 16 MiB of metadata, or more than a plain archive
  has left, is refused as damage. The listing ends there and says why; a member past it is refused. Large metadata that
  is there still lists and reads: a 2 MiB PAX attribute, a GNU name of 5,000 characters, plain and compressed.
- **Verification:** the archive suites (ArchiveFormatTests 26, NestedArchiveTests 2, ArchiveUpdateTests 6,
  ArchiveFuzzTests 13); TAR and TAR+gzip, 20,000 rounds each on the host, at most 1 MB in a round. The campaign goes on
  with the fixed build (E-B02-A1).
- **Severity:** Low–Medium: memory taken for a moment each time the archive is listed, then refused; nothing written,
  no data at risk.

### I59 — Registry: a key's rename could be redirected through a link put in its place

- **Found by:** the V23 review of B07 (typed Registry references → native hives). Every other change opens its key
  component by component as the key itself and refuses links (`OpenNoLink`), and deletions remove each key through the
  handle that was checked; a rename checked by name that the key was no link (`LinkTarget`) and then called
  `RegRenameKey` with the name.
- **Cause, seen on this Windows 11 (26220):** `RegRenameKey(parent, "link", "renamed")` on a Registry link renamed the
  link's **target**, a key under another parent, and left the link (an experiment in a throwaway HKCU key; the
  same rename through a handle opened with `REG_OPTION_OPEN_LINK` and `NtRenameKey` renamed the link itself). A process
  able to write the key's parent could put a link in its place between the check and the rename.
- **Exposure:** the elevated helper renames keys of HKLM and of other users' hives; where such a key's parent is writable
  by another account, that account could have an approved rename applied to a key of its choosing that the link can
  reach. It needs the right to create Registry links there, a plan the administrator approves, and a won race. In the
  user's own hive the writer is the user already.
- **Remediation (`b02a01f`):** the key is opened as itself (`REG_OPTION_OPEN_LINK`), checked through that handle, and
  renamed through it with `NtRenameKey`, which renames that object only; a key swapped away meanwhile is not renamed at
  all. Links are still not renamed.
- **Reproduction and verification:** `RegistryHardeningTests.A_key_is_renamed_through_the_handle_it_was_checked_by_never_through_a_link_put_in_its_place`
  swaps the key for a link to another key between the check and the rename: the rename fails and the link's target
  keeps its name; the plan step refuses a link and renames a plain key, with its undo. The Windows platform suite: 127,
  24 skipped (gated), none failed.
- **Severity:** Low: a renamed key can disable what reads it, but the setup needs an account with write and link rights
  under the key's parent and an administrator's approval of that very rename.

### I61 — `--workspace` and `--list` were read, forwarded, and ignored

- **Found by:** the V23 review of B12 (startup modes). `StartupOptions` read both options and a second launch forwarded
  them to the running FileCat, but nothing opened them, at start or forwarded; no list file reader existed. Plan §19.1:
  "Command-line arguments open locations, named workspaces, and list files; a list file opens as a result set, like
  Total Commander's LOADLIST."
- **Remediation (`dcd81a1`):** a named workspace opens first and the locations given with it open in it; a list file
  (paths one per line, UTF-8 or as its byte order mark says, relative paths from the list's own folder) opens as a
  result set in a new tab, saying how many lines named nothing. Network paths in a list are left out and counted, never
  contacted on the list's behalf (I16's rule); a relative list path is made full before it is forwarded.
- **Verification:** `ListFileTests` (UTF-8, UTF-8 with BOM, UTF-16; files, a folder, a relative line, a missing one, three
  network forms, a duplicate; a missing and an oversized list refused) and
  `StartupArgumentsTests` (the list's result set in a new tab; a named workspace, then a file's folder opened in it with
  the file focused); the App suite: 190, 7 skipped, none failed.

### I62 — Two names for one profile's folders ran as two instances

- **Found by:** the V23 review of B12. A profile's folders keep only letters, digits, `-` and `_` of its name (at most
  40), while the single-instance check used the name as given; a name with nothing usable named the folder that holds
  every profile.
- **Remediation (`2cd313f`):** both use the folder's name (`AppPaths.ProfileFolderName`); a name with nothing usable is
  the default profile. **Verification:** `RecoverySafetyTests.Profile_names_that_name_one_folder_are_one_instance`.

### I63 — The update check opened whatever page its answer named

- **Found by:** the V23 review of B13 (update and diagnostic inputs). Help → Check for updates and the daily check take
  `tag_name` and `html_url` from GitHub's answer; on a newer tag the user is asked "Open release page?" and the
  address goes to the system's association (`Shell.Open`). The address was not checked (an `https` page elsewhere, a
  `file:` address, a UNC path to a program, an `ms-settings:` link), the tag was shown as the version whatever its
  text (a pre-release part may hold any characters: "1.0.1-Visit … to update"), and the answer was read whole however
  long.
- **Remediation (`9bedead`):** a tag is shown and compared only when it reads as a version (up to four numbers, a
  pre-release of SemVer's characters, 64 at most); only an `https` page under `github.com/benny-cz/FileCat/releases/`
  is offered (the releases page otherwise); at most 4 MiB of the answer is read. The answer comes over TLS from GitHub,
  so this guards against an inspecting proxy or a compromise there, not a network neighbour.
- **Verification:** `UpdateCheckTests` (eight foreign addresses replaced, five foreign tags neither shown nor taken for
  newer, malformed answers refused) and `ToolAssociationTests.A_release_tag_reads_as_a_version_or_not_at_all`.

### I64 — A folder's name could turn the shown location around

- **Found by:** the V23 review of B14 (configuration and text reaching trusted UI). `Formatters.SafeName` escapes control
  and bidirectional characters in names (plan §18.3); the file list, quick view and the operation dialogs use it, but
  a tab's title, the path line and the path beside the command line showed a folder's name as it was, so a folder named
  with a right-to-left override (U+202E) made the location read otherwise. `SafeName` also let the Unicode line and
  paragraph separators through, where a text engine may break a one-line name and hide its end.
- **Remediation (`e6e9ad0`):** the three show the name escaped; the path itself, which editing and every operation use,
  stays as it is, and the path line's parts still go to the folders they stand for. The separators are escaped too.
- **Verification:** `FormattersTests.A_name_cannot_turn_itself_around_or_hide_its_end`,
  `TabStripTests.A_folders_name_cannot_turn_its_tab_or_path_around`,
  `PathLineTests.A_folders_name_is_drawn_escaped_and_its_part_still_goes_there`; the App suite: 195, 7 skipped, none
  failed. (A culture-aware `Contains` ignores such format characters: the test checks ordinally.)

### I65 — A damaged PE's optional header made the inspector throw

- **Found by:** the inspector damage campaign (E-B02-I1): round 197769 of the fixed `test.exe` threw
  `IndexOutOfRangeException` in `PeInspector.OptionalHeader`.
- **Cause and remediation (`8cb0737`):** the linker version was read by index (`o[2]`, `o[3]`) while every other field
  of the optional header goes through the bounds-checked readers; a damaged size made the header shorter than its
  fields. It is read the checked way; the round is replayed in every run, and rounds 197,769–199,999 pass.

### I66 — A deleted FAT file whose entry Linux cleared was called empty

- **Found by:** V09's UDisks2 trace on the Ubuntu VM (E-V09-T2, L5): files deleted with `rm` from a FAT32 volume
  listed as "0 bytes, recoverable: the file was empty", where the morning's L1 run, with the same recipe, recovered
  3 MiB. The raw entries showed why: marked deleted with first cluster 0 and size 0. Linux's FAT driver may write the
  emptied file's entry back after marking it deleted, which clears both; whether it does depends on its timing.
- **Cause:** a deleted entry of size 0 was classified "recoverable, the file was empty". An empty file has the same
  entry, so nothing tells the two apart.
- **Remediation (`1477de3`):** such an entry is listed by its name only, saying that the file was empty, or that the
  system that deleted it cleared its size and start (as Linux may), and that nothing then locates its content.
- **Verification:** `ErasedFatStartTests.A_deleted_entry_with_neither_size_nor_start_is_not_called_empty`; the FAT and
  recovery suites (ErasedFatStartTests 25, RecoveryEngineTests 13, RecoveryJobTests and the replayed fuzz rounds 25,
  RecoveryUiTests 2). Finding such files' content needs carving by content, which FileCat does not claim for them.
- **Severity:** Medium: no data is harmed, but a recovery tool telling the user a lost file was empty is a false
  finding (the class of I20).

### I69 — A repository's own configuration sent Git to a server while the folder was merely shown

- **Found by:** the V24 pass on the Git route, which is the one place where showing a folder runs another program in
  it. I16's guard (`2f35a6b`) covered the paths FileCat follows itself and refused a configuration naming programs
  (`[filter]`, `[include]`); it did not cover what the configuration makes **Git** open.
- **What was wrong:** `git status` opens what `core.excludesFile`, `core.attributesFile`, `core.worktree` and
  `core.hooksPath` name, and the object directories in `objects/info/alternates`, before comparing anything. A
  downloaded repository writes those itself, so one naming `\\server\share\…` made Windows connect to that server
  while the folder was merely listed. Measured on the host: 21.1–21.2 s per repository against an address that never
  answers (TEST-NET-1), and, under a packet capture on the lab VM, a TCP connection, an SMB2 negotiate and a session
  setup with the server the folder named.
- **Remediation (`aaee133`):** a repository is read only when none of those settings leaves this computer, decided from
  the text of the value (relative stays here; absolute must be local, never a share or a mapped network drive), in the
  spellings Git accepts, and likewise for the alternates files of this repository and of the one a linked work tree
  shares. A value that is no usable path no longer throws out of the listing.
- **Verification:** E-V24-G1 — the reproduction failed before the fix; afterwards the capture shows no packet while
  FileCat lists the folder and reads inside the repository, bracketed by two runs that do contact the server, with an
  ordinary repository beside it keeping its badge as the control.
- **Severity:** High: ordinary local browsing contacts a server the content names, which V24's pass criterion forbids
  outright, and the listing stalls for 21 s per such repository.

### I68 — A permanent delete reached into a file system mounted inside the folder

- **Found by:** the V23 review of B01 (names → file-system changes). The permanent delete (Shift+F8) recurses into every
  child folder that is not a link. On Linux and macOS the folder a drive, a share or a bind mount is mounted at is an
  ordinary directory, so deleting `~/work` with a USB stick mounted at `~/work/usb` deleted every file on the stick
  before failing to remove the mount point itself (as `rm -r` does without `--one-file-system`). On Windows the folder a
  volume is mounted at is a reparse point with a target (.NET reads both junctions and volume mount points), so it was
  already treated as a link and only the mount point removed.
- **Remediation (`e5b4e3b`):** `IFileSystemOperations.IsMountPoint` (the system's mount table on Linux and macOS, bind
  mounts included); the delete stops at such a folder, says "Another file system is mounted here … FileCat does not
  delete into it. Unmount it first.", deletes the rest, and leaves the mount point and the folders above it. A folder
  the user chooses is deleted as chosen. Moves that cross volumes copy before they delete, as `mv` does, and lose
  nothing.
- **Verification:** `JobEngineTests.A_permanent_delete_never_reaches_into_a_file_system_mounted_inside` (a mount point
  stood in for) and, live on the Ubuntu VM, `A_permanent_delete_stops_at_a_real_mount_inside`: a tmpfs owned by the
  test user mounted inside the folder, so its file could have been deleted; it was left, the folder's own file deleted.
- **Severity:** High: an ordinary action destroys data on another volume the user did not choose.

### I60 — The AppImage's runtime came unchecked from a moving release

- **Found by:** the V23 review of B09. `eng/package-linux.sh` pins appimagetool 1.9.1 by SHA-256, but appimagetool, given
  no `--runtime-file`, downloads the runtime it puts at the front of every AppImage: the manual packaging run of
  2026-09-30 (CI run 36759624490) logged "Downloading runtime file from
  https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-x86_64". That release is rebuilt on
  every change upstream, and nothing checked what came.
- **Remediation (`84b847a`):** the runtime is the build of type2-runtime commit `8f39b89` (2026-09-28; the one change
  since the dated release `20251108` makes extraction directories with mode 0700), 944,632 bytes, SHA-256
  `156f4bdbde9c52d01814600013e0a273f0118dc2de98975f3c8c63427ec79074` (GitHub's own digest of the asset agrees), checked
  before use and passed with `--runtime-file`. When the continuous release moves on, packaging stops with a message
  until the new runtime is reviewed and pinned; `APPIMAGE_RUNTIME` takes a reviewed file instead. The notices name the
  pinned build.
- **Verification:** the manual packaging run 36855265633 (`84b847a`): `runtime-x86_64: OK` beside
  `appimagetool: OK`, no runtime downloaded by appimagetool, and the AppImage, the `.deb` and the tarball each print
  their version.
- **Residual (I03):** a rebuild of this commit needs that exact file; once the continuous release moves on, it has to
  come from a copy kept by the release owner (DEC-10's store) or a release asset of FileCat's own.
- **Severity:** Medium for supply chain: no compromise is known; the gap was that a compromised or merely changed
  upstream build would have shipped in FileCat's AppImage unnoticed.

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

I01–I08, I10, I11, I13, I14 and I18 keep the plan's §7 text as their current record, I16 beyond what is recorded
above, and I17 for the parts not worked above.
I24–I27 are queued owner reports and findings of lower severity.
None has been closed. Their evidence, reproduction and remediation fields will be filled when worked.
