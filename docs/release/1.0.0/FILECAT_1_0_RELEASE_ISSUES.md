# FileCat 1.0.0 — release issue register

Authority: [release plan](../../design/FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md) §7 (initial register I01–I18) and §11.1
(issue lifecycle). Severity and release disposition are independent. "Remediated" means the change landed; an issue is
**Closed** only when the evidence named in its record supports closure. Evidence IDs point into the
[evidence index](FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md). Details of unfixed security-relevant concerns are kept at the
level the plan already states; exploit-level detail is not recorded here.

## Summary

| ID | Title | Severity | Disposition | Status |
|---|---|---|---|---|
| I125 | Scheduler shutdown races queue admission, leaving a task pending or starting late work | Medium (resource lifetime/availability) | Must fix (V12/AI-03) | Remediated 749f55f; verified preliminarily: both original admission failures retained. Locked queue/owner checks cancel both; identical corrected probes/two controls/affected and full host suites, four clean CI lanes and 87 guest controls pass without affected skips. Inputs/691 payloads/692 ZIP members/nineteen sources/artifact digests/XML/owned process/temp cleanup verify. Wider/native/candidate pending (E-I125) |
| I124 | Watchdog replacement throws from list mutation; enqueue bypasses the worker hard cap | High (availability/resource bounds) | Must fix (V12/AI-03) | Remediated 18006cc; verified preliminarily: original timer/list and cap violations retained. Corrected probes/two controls/affected and full host suites, four clean CI lanes and 85 guest controls pass without affected skips. Working inputs/691 payloads/692 ZIP members/nineteen sources/artifact digests/XML/process/temp cleanup verify. Physical/wider/native/candidate pending (E-I124) |
| I123 | Headless synchronization fixture omits FileCat's native Windows file adapter | Low (validation reliability) | Must fix (CI/V13) | Remediated 08acc2f; verified preliminarily: portable held-target baseline fails, native adapter passes 17 affected/full App, four clean CI lanes and 17 guest cases. 2,202 inputs/367 payloads/368 ZIP members/thirteen sources/artifact digests/XML/cleanup verify. Harness failures/original CI retained, historical request unavailable; native/candidate pending (E-I123) |
| I122 | Picture feeds bypass per-device workers and keep additional held calls after demand changes | Medium (bounded resources) | Must fix (V12/AI-03) | Remediated 82f7488; controlled remedy verified preliminarily: two baseline failures/healthy positives, corrected/full host suites and 36 clean guest cases pass. Original CI synchronization failure retained; clean 08acc2f successor passes all four lanes/all affected XML cases. Exact inputs/payload/source/artifact digests/XML/cleanup verify. Watchdog/aggregate decoder/native/candidate pending (E-I122) |
| I121 | Closing a picture races its active file feed; F3 retains a loaded bitmap after close | Medium (resource lifetime) | Must fix (V12/V10/AI-03) | Remediated a550fcd; verified preliminarily: five baseline failures/one positive, six corrected/20 affected App cases/full host suites, four clean CI lanes and 34 guest cases pass. Wrong harness count retained; identical payload passes exact successor inventory. Inputs/payloads/source/artifact digests/XML/cleanup verify; per-device feeds/other Source/native/candidate pending (E-I121) |
| I120 | Live NTFS fixture dereferences an absent table after its history falls outside the retained window | Low (validation reliability) | Must fix (CI/I23) | Remediated 9074cf6; verified preliminarily: original CI failure retained, full host platform/golden controls, four clean CI lanes and 20 elevated guest cases pass. Complete live history passes in CI/guest, zero affected skips. Exact inputs/payloads/source/artifact digests/XML/cleanup verify; native/candidate pending (E-I120) |
| I119 | Closing a page reader disposes an active source call and leaves later page/refresh demand alive | Medium (resource lifetime) | Must fix (V12/AI-03) | Remediated de1fd71; verified preliminarily: eight baseline failures, nine corrected/38 affected cases/full host suites and 45 clean guest cases pass. Original I120 CI failure retained; clean successor passes four lanes/all affected XML cases. Exact inputs/artifacts/XML/cleanup verify; direct Source/picture/native/candidate pending (E-I119) |
| I118 | In-flight metadata repopulates invalidated values, including stale checksum matches | Medium (metadata truth) | Must fix (V12/V15) | Remediated 2896108; verified preliminarily: four original failures, six final controls/full host suites, four CI lanes and ten clean guest cases pass. Exact working/payload/source/XML/artifact-digest/cleanup inventories verify. Native/candidate pending (E-I118) |
| I117 | Late-name discovery fixture can finish before any probe is handled | Low (validation reliability) | Must fix (CI/I23 evidence) | Remediated da3a3d6; verified preliminarily: eight network/full Core 730/46 skips and seven App controls, four clean CI lanes and 15 guest cases pass. Mutation fails twice; original ARM64 failure retained/scheduling untraced. Exact inputs/artifacts/XML/cleanup verify; candidate/real-device pending (E-I117) |
| I116 | Initial archive search discards provider warnings and hides partial scope | Medium (search truth) | Must fix (V13) | Remediated 6ecf4a8; verified preliminarily: two valid baseline failures/two TAR/gzip positives, 32 affected controls and full host suites. Four CI lanes and 34 clean guest cases pass; exact inputs/artifact digests/XML/cleanup verify. Other formats/native/candidate pending (E-I116) |
| I115 | Searching within results silently drops archive members and unavailable scope | Medium (search correctness) | Must fix (V13) | Remediated ff8746a; verified preliminarily: four valid baseline failures, eleven corrected Core/two App controls and full affected host suites. Four CI lanes, all affected Windows/Linux/macOS App XML and 13 clean guest cases pass. Exact inputs/XML/cleanup verify; other formats/native/candidate pending (E-I115) |
| I114 | Parallel GnuPG fixtures replace each other's process-wide tool selection | Low (validation reliability) | Must fix (V15/V24 evidence) | Remediated 7497acf; verified preliminarily: controlled Good → UnknownKey → Good swap, isolated full Core 714/46 skips and four successor CI lanes pass. Original failure retained/interleaving untraced; candidate pending (E-I114) |
| I113 | Quick view accepts stale results/errors, leaks failed/empty-reset readers and starts unbounded abandoned opens | Medium (preview correctness/resource demand) | Must fix (V12/AI-03) | Remediated 7497acf; verified preliminarily: six baseline failures, seven corrected controls, full affected host suites, four CI lanes and seven clean guest cases. Native presentation/candidate pending (E-I113) |
| I112 | Concurrent page-cache stress observer infers idle from unchanged totals | Low (validation reliability) | Must fix (required evidence/CI) | Remediated b7d2e8; verified preliminarily: held-read and seven cache controls, full affected host suites, four successor CI lanes and seven guest controls pass. Original failure retained; candidate rerun pending (E-I112) |
| I111 | A delayed first listing batch stays hidden after an empty/parent-only view | Medium (listing feedback) | Must fix (V12/V16) | Remediated b7d2e8; verified preliminarily: four baseline failures, corrected controls/original streaming/full host suites, four CI lanes and four guest controls. Native/candidate performance pending (E-I111) |
| I01 | Advertised private security-reporting route is disabled | High (operational) | Blocker | Open |
| I02 | Stable signing unavailable | High | Blocker | Open — owner/provider action |
| I03 | Incomplete dependency and artifact provenance | High | Blocker (audit) | Open; new detail below |
| I04 | Platform/package claim mismatches | High (where a clean install fails) | Blocker for the affected claim | Open; new detail below |
| I05 | Media/record claim reconciliation | Medium | Contract gate | Open |
| I06 | Aggregate content-cache accounting | Potential High | Validation gate | **Page caches remediated `61b028f`; verified** (E-I06-P1: open views held 148 MiB together, now one 64 MiB budget). **Archive indexes remediated `6b37c41`, `319a38c`; verified** (E-I06-A1: eight kept by count, 557 MiB each for a million-member ZIP; now earlier ones within 256 MiB, the two used last kept). Both with negative controls. Open for the rest: decoded pictures, icon caches, other materialized lists (V12) |
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
| I109 | Native cluster query unavailable but unexplained | Low (inspection/validation reliability) | Should fix (V14) | **Remediated cfcc9e6; full host Windows and all four CI lanes pass** (E-I109). Original unsupported result retained; candidate rerun pending |
| I110 | Physical direct/helper comparison could accept equal short reads | Medium (validation reliability; two zero reads could compare equal) | Must fix (V09) | **Assertion correction implemented; off-source 23 pass/two declared skips and elevated E: reader pass** (E-I110). Physical strict rerun held for unresolved source change; host-native C: error 50 retained |
| I108 | CI helper readiness and verified-copy observer timing | Low (validation reliability) | Must fix (required CI) | **Observer corrections verified preliminarily:** c162481 failures retained; finalized discovery checkpoint and scoped thumbnail-answer/UI-binding checks pass full Core/App and native helper controls (E-I108-P1). All four exact 1bd931b successor CI lanes pass, direct Windows XML independently verified. Unsupported mock-pixel attempt retained; candidate rerun remains required |
| I107 | Windows host menus appear and immediately disappear | High (loss of core mouse command access; owner-declared blocker) | Must fix (V17/I13) | **Closed for preliminary remediation** — native failure and baseline reproduced; complete App inventory 233/248 with 15 skips, affected CI pass. Owner confirms corrected host and clean 1a9f1ba guest success; verified guest trace has no logical-detach closes and a menu open for 47.6 seconds. Exact-candidate interaction pending (E-I107) |
| I106 | Recovery discovery misses a separate portable installation | Potential Critical (deleted-data safety) | Must fix (V09/I09) | **Open, broader qualification pending** (E-I106): renamed/confirmation gaps corrected; root absence established, ordinary visibility unknown. Elevated guest admission 12/12, full guards 29 pass/3 skips; host App 242 pass/21 skips. Native Ubuntu and both strict Unix CI inventories 74 pass/22 skips; all cc1acf2 CI lanes pass. Broader privilege/runtime-alias/lifetime race and physical/candidate tracing pending |
| I105 | Portable recovery misses per-user owners and portable profiles | Potential Critical (deleted-data safety) | Must fix (V09/I09) | **Remediated and verified preliminarily** (E-I105): exact dev.539 native GUI miss; three baseline regressions fail; fixed ordinary/independent GUI 2/2, Windows App 225/240 and final guards 10/13 pass with explicit skips. CI/development packaging pass at 1cd803c; native rebuilt packages/wider discovery/candidate pending |
| I104 | Windows window title should start with FileCat, then the selected path and account/elevation | Low (owner-requested title ordering) | Low-priority correction | **Remediated a50b3b8; account and headless title checks pass** (E-I104). Selected name retained; live candidate check pending |
| I103 | Windows profile case aliases miss the running instance; portable/usual roots collide | Medium (instance contract; V09 safety impact) | Must fix (A-07, V09/I09, V23 B12) | **Remediated and verified preliminarily** (E-I103): Windows baseline 1/4, fixed 4/4 and guards 6/9 pass; Unix boundary 23 pass/4 explicit skips; all four CI lanes pass at fa3a02a; native packages/candidate pending |
| I102 | Recovery misses independent windows and usual instances using another profile | Potential Critical (deleted-data safety) | Must fix (V09/I09) | **Remediated 5b69fba and natively verified** (E-I102): actual GUI before/after, two-owner/crash/forwarding controls, 8/8 process cases and App 208/235 pass with explicit skips; all four CI lanes pass, packages/candidate pending |
| I101 | Recovery omits the Unix runtime temporary folder, including another instance's different TMPDIR | Potential Critical (deleted-data safety) | Must fix (V09/I09) | **Remediated 369f55f and natively verified** (E-I101): baseline fails 1/1; guards, boundary/process harnesses and App 206/233 pass with explicit skips; all four CI lanes pass, packages/candidate pending |
| I100 | Unix session-local mutex allows separate launches to run on one profile and replace its live socket | Medium (instance/state contract; V09 safety impact) | Must fix (A-07, V23 B12, V09) | **Remediated 4746592 and natively verified** (E-I100): 1/5 before, 6/6 after; actual GUI/FAT/exFAT and affected App pass; all four CI lanes pass, rebuilt packages and candidate closure pending |
| I99 | Unix instance socket throws under valid long temporary paths | Medium (instance startup and recovery safety check) | Must fix (A-07, V23 B12, V09) | **Remediated d38f915; native cases, affected App and all four CI lanes pass** (E-I99); rebuilt packages and candidate closure pending |
| I98 | Linux tar desktop entry corrupts extraction paths containing special characters | Medium (desktop launch fails) | Must fix for Linux tar claim | **Remediated ecf5349; native helper and Linux CI 12/12 verified** (E-I98; unchanged 4/12); rebuilt tar GUI/helper pass on 24.04 and 26.04; candidate closure pending |
| I97 | CloneCopyTests: a UNC volume root lacked the separator required by the native query, so the SMB case failed before copying | Low (validation setup; no product copying defect established) | Must fix (native copy coverage) | **Remediated `ca1afe0`; independent probe and corrected local/SMB cases verified**, 1/1 each without skips (E-V03-CLONE-1); candidate rerun and closure pending |
| I96 | ARM64 CI: the native Recycle Bin integration test assumed its first query always succeeds; one returned `ERROR_ALREADY_EXISTS` | Low (validation reliability; no product data loss established) | Must fix (required CI lane) | **Remediated `5503262`; targeted test and four-lane CI verified** (E-I96); candidate rerun and closure pending |
| I95 | Signatures: a good OpenPGP signature without a trust line (gpg.conf trust-model always) read as good, for any key in the keyring | Medium–High (a file signed by an arbitrary, uncertified key shown as signed by its publisher) | Must fix (V15: an unknown signature never becomes a shield) | **Remediated `cd37342`; verified** (E-V15-G1; independent GnuPG test fails under the old reading) |
| I94 | Synchronize: folders inside each other (also through a junction) were offered for synchronizing; Mirror toward the outer one removed the source as an item only in the target | Medium–High (the source removed: to the Recycle Bin, or for good when deleting permanently was chosen) | Must fix (V13/V02: no removal of what is being copied; S3 data safety) | **Remediated `65cee78`; verified** (Core, NTFS junction and App tests; the App test fails with the check bypassed) |
| I93 | Duplicates: two names of one file (a hard link, a path through a junction) were grouped as copies, so "all but one" could mark the file itself for deletion; a link to a file was grouped with its target | Medium–High (deleting the marked "copy" through a junction deletes the only copy; recoverable from the Recycle Bin unless deleted permanently) | Must fix (V13; S3 data safety) | **Remediated `bf395c6`; verified** (E-V13-R1; NTFS test: 4 names in one group before, 1 group of 2 after) |
| I92 | Quick search: a miss walks every name on the window's thread in large listings | Low (extreme listings) | V16 limit (§9 command feedback ≤100 ms) | Remediated b7d2e8; verified preliminarily: worker/leased-view and ordered/cancellable keys pass nine Core/seven App controls, full host suites, four CI lanes and clean guest controls. Model acknowledgement 0.017–0.494 ms; native frame/AT/reference/candidate pending (E-I92) |
| I91 | Analyze folder: an analysis went on after its tab left the folder (then labelled and re-sorted the next folder) or closed (then failed reading the released listing) | Medium (a false "every value computed" on another folder; an exception where the analysis is awaited) | Must fix (V12: expensive sorting asked for and cancelled; navigating away) | **Remediated `800cd52`; verified** (E-V12-C2; tests fail on the old code) |
| I90 | Find: appending a search from a root typed in another letter case listed each common file twice | Low–Medium (a set listing files twice; copying or deleting it acts on each twice) | Must fix (V13: result sets keep exactly their items) | **Remediated `54c33de`; verified** (E-V13-R1; the test lists 4 items for 2 on the old code) |
| I89 | Change journal: a journal that wrapped twice while it was read failed the whole read; entries skipped once were skipped silently | Low–Medium (the journal view of a busy volume could not be shown) | Must fix (CI red; V14: records shown as they are) | **Remediated `2ad2cfa`; verified** (unit tests with a stand-in journal fail on the old rule with CI's error) |
| I88 | Count: a folder's count went on after its tab closed and threw on every progress post, enough for the crash guard to end FileCat; leaving the folder kept the tab "counting" | High (FileCat ends about 1.6 s after closing a tab during a count with that much left: 24 exceptions in five seconds measured) | Must fix (V12: navigating away and closing while counting) | **Remediated `a9a9dcf`; verified** (E-V12-C2; tests fail on the old code; the experiment: 24 exceptions before, 0 after) |
| I87 | Folder watch: a folder that kept changing was not read again until the changes stopped | Medium (a folder a program keeps saving into stayed as first shown: no reread in six seconds of a file every 50 ms, none in 30 s of churn) | Must fix (V12: rapidly changing folders) | **Remediated `3d2bb2e`; verified** (E-V12-W1; unit test with a negative control) |
| I86 | Operations: "Clear finished" could leave a job that had just finished, and keep offering to clear it | Low (a click did nothing; a second one worked) | Must fix (CI red; V17: controls do what they say) | **Remediated `506cc75`; verified** (unit test fails on the old code) |
| I85 | Count: a folder deleted and made again, or replaced, while its size was counted took the first folder's size as counted | Low–Medium (a size shown, as counted, for a folder that was never counted) | Must fix (V12: results never land on replacements) | **Remediated `1ec9d13`; verified** (E-V12-C1; unit test with a negative control) |
| I84 | Compare directories: of two names differing only in letter case one was dropped unseen; a size or time a listing does not give counted as the same | Medium (items silently missing from a comparison, and pairs called the same that were never compared) | Must fix (V13: no false equality) | **Remediated `bc65646`; verified** (E-V13-C1; unit tests with negative controls) |
| I83 | Find: a saved time range shown again in the dialog lost the last minute of its end day | Low (an item modified in the end day's last minute missed by a saved search run again) | Should fix (V13) | **Remediated `c67fa85`; verified** (unit test with a negative control) |
| I82 | Compare: the window said "1 difference" over a list of two (changed lines, then lines only on one side) | Low (the summary's count disagreed with the list and with next and previous) | Should fix (V13) | **Remediated `db2e9b4`; verified** (E-V13-C1) |
| I81 | Compare: the text comparison anchored on a line that occurs once on each side even where it was far from its place, and presented the result as exact | Low–Medium (an 11-line edit shown as 84 lines only left or only right, unlabelled; no false equality) | Must fix (V13: labels correct) | **Remediated `db2e9b4`; verified** (E-V13-C1, with a negative control) |
| I80 | Tooltips over icons are not styled by the selected theme, and an icon button's tip ran its parts together on one line | Low (looks; the owner's request) | Should fix (owner's request, 2026-10-01) | **Remediated `fab03b8`; verified** (unit tests with negative controls; all seven themes pictured) |
| I79 | Resuming checked only the 64 KiB before the break: an iPhone, reconnected, sends some photos with other bytes at their start, and the resumed copy kept the old start with the new rest | Low–Medium (a resumed copy can mix two versions of a file whose start changed at the same size and time; on the owner's iPhone it happened to equal one version) | Must fix (V21, plan §14) | **Remediated `7ee8e92`; verified** (unit test with a negative control; the Motorola) |
| I78 | Phones: an unplugged phone was reported as the file being copied "no longer exists"; a folder listed as it was unplugged came back shorter or empty, without an error | Medium (a false statement about the user's file; a listing cut short passed off as the folder's contents) | Must fix (V21) | **Remediated `7ee8e92`; verified** (unit tests with a negative control; the owner's Motorola, cable pulled) |
| I77 | Windows: a FileCat test left 163 records of its deleted files in the owner's Recycle Bin; FileCat's undo of a recycle leaves the item's record behind, as Explorer's own Restore does | Low (records Windows neither shows nor counts, a few hundred bytes each, without bound; no user data affected) | Should fix (E-BIN-1, test hygiene) | **Remediated `f95e4cd`; verified** (unit test with a negative control; the owner's bin; Windows' own Restore observed on the lent VM) |
| I76 | Delete: FileCat could not delete a folder OneDrive keeps in sync, nor any customized folder (their read-only mark), and blamed Controlled Folder Access | Medium (a common folder could not be deleted, nor moved off its drive whole; the message pointed elsewhere) | Must fix (E-CLOUD-1, V02) | **Remediated `edd950a`; verified** (unit tests with a negative control; the owner's OneDrive) |
| I75 | Taskbar: the pinned icon read small on a dark taskbar; the 24-pixel frame fixed earlier is not what a pinned item draws | Low (looks; the owner's report) | Should fix (owner's request, E-ICON-1) | **Remediated `a9f48cf`; verified** (the lent VM's real taskbar, dark and light) |
| I74 | Content search answered otherwise than the files at 1 MiB read boundaries, and missed UTF-8 inside UTF-16 files | Low–Medium (false positives for anchored or look-around regular expressions and accents at read boundaries; a documented reading left out) | Must fix (V13) | **Remediated `b0a2313`; verified** (a differential corpus test, before and after) |
| I73 | Type icons: a placeholder name made the Shell try to open `C:\file.url` from FileCat's own process | Low (a constant, local, nonexistent path at the system drive's root, where standard users can make only folders; nothing was read) | Should fix (V24, I16's file half) | **Remediated `f1b48de`; verified** (file trace with and without the change; unit test) |
| I72 | Phones: creating folders, renaming and copying onto an iPhone were offered, and every one failed | Low–Medium (V21's read-only capability was not shown; F7 there ended in "The request is not supported. (0x80070032) (0x80070032)") | Must fix (V21) | **Remediated `2e93339`; verified** (on the owner's iPhone, and unit tests with a negative control) |
| I71 | An older FileCat saved over a newer FileCat's window layout within two minutes | Medium (the newer layout and its backup were both gone) | Must fix (V11, plan §19.1) | **Remediated `b70be07`; verified** (unit test with a negative control) |
| I70 | A Shell picture that got no answer was remembered as the file having none | Medium (after a helper died, quick view showed no picture for those files for the rest of the session) | Must fix (V24, CI flake) | **Remediated `3f647bd`; verified** (unit test with a negative control) |
| I69 | A repository's own configuration sent Git to a server while the folder was merely shown | High (an unasked connection to an attacker-named server during ordinary browsing; 21 s per repository where it does not answer) | Must fix (V24, V23 B10) | **Remediated `aaee133`; verified** (E-V24-G1, with a packet capture) |
| I68 | Linux/macOS: a permanent delete reached into a file system mounted inside the deleted folder | High (deleting a folder that holds a mounted drive, share or bind mount emptied that volume too) | Must fix (V23 B01, DPI) | **Remediated `e5b4e3b`; verified** (unit test; live on the Ubuntu VM) |
| I59 | Registry: renaming a key checked by name that it was no link, then renamed by name, and Windows' rename follows links | Low (a process able to write the key's parent, winning a race, could make an elevated plan rename another key, the one a link names) | Should fix (V23 B07) | **Remediated `b02a01f`; verified** (E-DPI) |

## Records of issues worked in this campaign

### I122 — Picture feeds bypass their provider's bounded workers

- Three actual held picture-file reads occur against a two-worker provider device in both F3 and rapid quick
  view. A healthy device completes in each baseline; failure is the extra held call, not a timing prerequisite.
- Runtime feeding now uses the provider's device key and interactive scheduler queue. The actual callback
  borrows its source; canceled queued feeds perform no read. Existing scheduler/worker limits are unchanged.
- Two controls/22 affected App cases/full App 271/21 skips/Core 745/46 skips pass. Inputs/source/direct XML
  and original/corrected/full-suite call/healthy-device traces independently verify. Clean CI/guest next;
  watchdog/hard-cap, aggregate decoder resources, other Source/native/candidate remain (E-I122).

### I121 — Picture feeders outlive the source ownership of their closed view

- Four unchanged-production controls hold the real decoder's second read over an owned PNG file; viewer/quick
  view close disposes its source before return on success/failure routes. A fifth case retains the loaded F3
  bitmap; normal quick view passes. No fixture timeout is counted as a product failure.
- Reader borrows now cover actual feed callbacks while cancellation finishes UI demand promptly. Feed cancellation
  is checked after held reads, abandoned exceptions observed, unreturned bitmaps released and closed F3 clears
  its bitmap/rejects late results. The raw-source overload drains feeding before completing.
- Six controls/20 affected App cases/full App 269/21 skips and Core 745/46 skips pass; 1,809 inputs/fourteen
  sources per stage/active assemblies/direct XML verify. Clean a550fcd passes four CI lanes and 34 guest
  cases, zero affected skips; 686 payloads/687 ZIP members/fourteen sources/artifact digests/XML/cleanup verify.
  An original harness count failure remains retained; corrected exact inventory uses the identical payload.
  Per-device feed bounds, other Source consumers, native/candidate remain. See [E-I121](evidence/E-I121-picture-feed-lifetime.md).

### I120 — Live NTFS log fixture assumes an expired history table exists

- Clean de1fd71 Windows CI fails with NullReferenceException. The actual report explains an empty table:
  the fixture's last LSN is older than the circular log's retained window. Original raw blocks/IO trace are
  unavailable; report/XML/log/artifact provenance is retained, without asserting an unobserved cause.
- MFT checks are separate from bounded live-log checks. The latter declares known missing history skipped,
  preserves creation/name/time-change assertions and fails other missing-table reasons with the report.
- Full host platform 161/38 skips and nine golden log cases pass; eleven record cases have seven passes/four
  declared privilege skips. All 130 inputs/source/active assemblies/original XML verify. Clean 9074cf6 passes
  four CI lanes and 20 elevated guest cases, zero affected skips; complete live history passes in both.
  All 625 payloads/626 ZIP members/seven sources/artifact digests/XML/process/temp cleanup verify.
  Guest system-volume metadata reads are explicit; no physical USB or recovery-source qualification.
  Native/candidate remain. See [E-I120](evidence/E-I120-ntfs-live-history-fixture.md).

### I119 — Closing page readers races source calls and leaves abandoned demand

- Eight unchanged-production controls hold actual local-file read/revision calls: close disposes their source
  during the call; a multi-page read loses its first page, and closed readers request pages or access the source
  during Refresh. A completed-byte/cache/budget control passes. No fixture-timeout failure counts as evidence.
- Source-use accounting defers disposal to the last active call, while close releases cache memory immediately.
  New page/refresh requests stop, multi-page reads end at a safe boundary and retired errors/revisions cannot land.
- Nine final controls/38 affected cases and full Core 745/46 skips/App 263/21 skips pass; 267 inputs/eleven sources
  per stage/active assemblies/direct XML independently verify. Clean de1fd71 passes 45 guest cases, zero skips;
  683 payloads/684 ZIP members/eleven sources/XML/cleanup verify. Three CI lanes pass; all 45 affected Windows
  and seven Linux/macOS App cases pass, but Windows fails I120. Clean successor 9074cf6 passes all four CI
  lanes and all affected cases, with original failure retained. Many active calls, direct Source/picture lifetime,
  scheduler bounds, native/candidate remain. See [E-I119](evidence/E-I119-page-reader-lifetime.md).

### I118 — In-flight metadata restores values invalidated by a sidecar change

- Four unchanged-production failures: Get restores Matches after an actual checksum-sidecar mismatch and
  invalidation/Forget; explicit Compute also returns/caches an obsolete Available value.
- Production validity and cache invalidation/publication are coordinated; obsolete work cannot republish,
  explicit results become NotRequested, unrelated fields survive selective Forget and queued canceled work
  releases demand. Completion wakes visible rows to retry. Four intermediate event controls require that wakeup.
- Six final controls and full Core 736/46 skips/App 263/21 skips pass. Positive viewport control includes 1,000
  abandoned requests, an independent healthy device and interactive priority with one freed worker. All 477
  inputs/source/direct XML verify. Clean 2896108 passes four CI lanes and ten guest cases, zero skips; 680
  payloads/681 ZIP members/eight sources and output pins verify, controller/workers absent and temp empty.
  Wider/native/candidate scopes remain. See [E-I118](evidence/E-I118-metadata-invalidation.md).

### I117 — Late-name discovery fixture can expire before handling a probe

- Exact aed64a7 ARM64 CI receives an empty host list after one second; other lanes pass. No original scheduling
  trace is available. The fixed timer starts before the fixture confirms its metadata request.
- The loopback fixture now controls the actual cutoff after receiving that request and replies afterward, on
  its dedicated device thread. Immediate/delayed setup controls pass; a name/probe cancellation mutation fails
  twice. Public timings and network policy are unchanged; the internal helper shares the production algorithm.
- Full Core 730/46 skips and seven related App controls pass. Original run/artifacts and 218 input files/direct
  XML verify. Clean da3a3d6 passes all four CI lanes and 15 combined guest cases; exact input/artifact/XML/process/
  temp checks verify. Candidate and real-device discovery remain. See [E-I117](evidence/E-I117-network-discovery-cutoff.md).

### I116 — Initial archive search silently discarded provider warnings

- V13, medium truth defect: a damaged TAR's usable prefix and a duplicate-name ZIP both lose warnings at the
  Find adapter. Two valid controlled baseline failures; the first detector-misconfigured attempt is separate.
- The adapter forwards warnings; search deduplicates each archive-wide warning across member folders within
  the existing log bound. Usable original references remain; contents/parser/nesting policy is unchanged.
- Thirty-two affected controls and full Core 729/46 declared skips/App 258/21 declared skips pass. Actual TAR
  and gzip-TAR narrowing/content-reference controls extend I115. Clean 6ecf4a8 passes four CI lanes and 34 guest
  cases; exact inputs/artifact digests/XML/cleanup verify. Disk-full failed publish is retained and verified on the
  authorized second workspace. Other formats/native and candidate remain. See [E-I116](evidence/E-I116-archive-discovery-warnings.md).

### I115 — Searching within results silently omitted archive members

- V13, medium correctness defect. Four valid controlled failures on unchanged 7648865 show omitted matching
  ZIP members and missing diagnostics for removed ordinals, content exclusions and unavailable lookup.
- Revalidation lists each original parent once, preserves identities/ordinals/relative paths, uses current
  metadata and only the original subset. Partial listings stay explicit; unlisted members are not called gone.
  No member content or nested archive opens during lookup. The log keeps the original typed location.
- Eleven Core controls and two headless Find/content/log/navigation flows pass. Full Core 725/46 declared skips
  and App 258/21 declared skips pass; exact inputs/direct XML verify. Early fixture/observer faults are retained
  separately. Clean ff8746a passes four CI lanes, affected Windows/Linux/macOS App XML and 13 guest cases with
  exact payload/cleanup verification. Preliminary remediation verified; other-format/native and candidate checks remain.
  See [E-I115](evidence/E-I115-archive-result-search.md). Overall NO-GO and USB hold remain.

### I109 — unsupported cluster query was silently absent from the file record

- Low severity, honest inspection/validation result (V14). An independent Windows query confirms native error 50
  on the host NTFS fixture with three access masks; the inspector hides that unsupported result. The test also
  assumes an allocation map is always available. Original failures and reports retained (E-I109).
- The report now explains unavailable layout; the existing test checks native support and still asserts all other
  metadata/security. Updated assertion fails before the product correction; full Windows 159 pass/37 explicit
  skips afterwards. No privilege/access-right change. Exact cfcc9e6 successor CI 37121005911 passes all four lanes;
  original Windows archive and affected-case inventory independently verify. Candidate rerun remains required.
  See [E-I109](evidence/E-I109-unavailable-cluster-layout.md).

### I108 — CI tests depend on helper output and scheduler timing

- Low severity, required CI reliability fix. Two Windows x64 runs time out before the new synthetic USB lease
  test's contention assertion; an earlier macOS run observes verified-copy progress after verification has already
  advanced. Original logs and Windows TRX archives are retained and independently checked (E-I108).
- Correction: atomic readiness marker after child lease acquisition, bounded startup with process diagnostics;
  deterministic real-copy checkpoint before verification. Immediate contention refusal and five-second post-crash
  availability bounds remain. Product interlocks and estimator are unchanged.
- Local 29 lease/guard/oracle and eight progress checks pass without skips; full Core 700 pass/46 explicit skips.
  Exact 6cad380 successor CI 37119313116 passes all four lanes, three package jobs skipped. Direct Windows archive
  independently confirms all affected cases pass. Original failures are retained; candidate rerun remains required.
  See [E-I108](evidence/E-I108-ci-test-synchronization.md).

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

### I107 — Windows host menus appear and immediately disappear

- Owner report persists after automated host tests complete; host live test authorized. Copied Program Files DLL
  matches current working build. Direct live click cannot proceed because Windows Computer Use initialization
  crashes even after reset. No input sent; no independent reproduction or causal conclusion ([E-I107](evidence/E-I107-host-menu-report.md)).
- Owner declares this a blocker and reports the same symptom in the running Windows VM. Codex restart does not
  clear it. Automation JavaScript still crashes, including a basic health check without the UI helper import.
- Owner comparison of all three staged builds fails; logs establish all isolated GUIs started and exited normally.
  Native trace shows an unchanged Classic theme event rebuilding/removing the open menu, closing its popup while
  the window stays active. Two new regressions reproduce this; ordinary mouse-click control passes.
- Theme application now skips an unchanged installed palette while preserving the requested preference. Targeted
  menu/theme/tooltip checks pass 14/14; remainder 219/234, 15 skips. Owner confirms host success, copied DLL matches
  working fix and remaining work is authorized. All affected CI/manual lanes subsequently pass. Owner confirms
  clean 1a9f1ba guest success; all 256 input hashes and native trace module are reverified. Thirteen recorded menu
  episodes close through pointer input, none through logical detachment; one stays open for 47.6 seconds.
  **Closed for preliminary remediation**; exact-candidate interaction remains pending. Automation still fails
  after Claude closes; no-import health
  reports Windows sandbox setup refresh errors. This gate is independent of the traced FileCat defect.

### I106 — Recovery discovery misses a separate portable installation

- [E-I106](evidence/E-I106-other-process-recovery.md): actual second portable GUI holds a busy lease but the first
  installation's production probe misses it. Baseline presence/unknown guard regressions fail; absent control passes.
- Device safety now also takes a read-only process census and requires other FileCat processes/helpers to finish;
  unavailable census refuses. No global disk registry added. Storage tests isolate the inventory from owner activity.
- Windows subset 14 pass/3 Unix skips and final App 229 pass/15 skips. Remediated preliminarily, not Closed;
  native after and all affected CI lanes now pass, with independent strict Unix inventory 47 pass/four skips.
  Rebuilt package native checks, wider visibility/alias/race audit and candidate tracing remain pending.
- Confirmation-window audit reproduces a second admission gap: another writer starts while the dialog is open.
  All four routes admit incorrectly in eight elevated guest baseline cases; four controls pass. Full safety is now
  checked again before device authorization. Corrected elevated guest admission 12/12; guard inventory 29 pass,
  three skips. Host complete App inventory 242 pass/21 skips, no failures. Raw source/bundles/results retained.
  This does not reserve against later process starts; broader I106 remains Open. Native Ubuntu and all cc1acf2
  CI lanes subsequently pass, strict Unix 74 pass/22 declared skips in each matrix. Prior test routing failure
  retained; Windows-only admission cases now declare their platform prerequisite.

- Windows availability audit at clean 9257967 reproduces unknown census on the ordinary host and elevated guest
  after owned menu-fixture teardown, despite no FileCat/dotnet name in the later visibility inventory. Sixteen
  guest module identities are unreadable; diagnostic limited-information queries still cannot identify four OS
  processes. No unsafe name-based exemption or guard bypass. USB identity/interlock preflight passes; raw-read,
  product admission and source-device tracing remain pending ([E-V09-G1](evidence/E-V09-G1-usb-interlocks.md)).

- Read-only structure audit retains five unavailable elevated image identities; no production exclusions
  ([E-I106-P1](evidence/E-I106-P1-windows-process-structure.md)). Windows lookup now uses the limited image API;
  controlled standard/elevated permission cases and full affected host suites pass at exact working inputs
  ([E-I106-P2](evidence/E-I106-P2-windows-limited-image-query.md)). Clean 36ee824 guest controls and all four CI lanes
  also pass. Broader absence,
  runtime/lifetime/physical/candidate qualification remain open; unknown identities still block recovery.

### I105 — Portable recovery misses per-user owners and portable profiles

- [E-I105](evidence/E-I105-portable-fallback-discovery.md): an exact dev.539 GUI with unwritable portable Data falls
  back to per-user state but is missed by the production probe; independent fcntl confirms the live lease. Removing
  only the owned marker makes the same probe see it. Portable profile enumeration also uses the wrong root.
- Discovery now reads portable and per-user candidates and both profile roots; recovery guards the folders of
  the actual live candidate. The literal default and profiles/DEFAULT roots remain distinct. Probes write nothing.
- Before regressions fail 3/3. Working-overlay ordinary/independent native GUI cases pass 2/2; final Windows guards
  pass 10 with three Unix skips, full App 225 with 15 skips, paths 13 with one Unix skip. No unsafe disk scan.
- Potential Critical, V09/I09 must fix. All four CI lanes and development packaging pass at 1cd803c (37055272672).
  Remediated/verified preliminarily; rebuilt native packages, wider
  installation/process discovery and exact-candidate tracing remain pending. Previous lookup evidence is stale.

### I104 — Windows title should put FileCat before the selected path and account/elevation

- Owner report, 2026-10-02; Low priority. Formatting correction a50b3b8 validated preliminarily (E-I104).
- Reported administrator title: `Administrator: FileCat - FileCat - marek (elevated)`.
- Reported user title: `FileCat - FileCat - marek (elevated)`.
- Owner clarification, 2026-10-02: the selected directory itself was named FileCat, so the repeated word is valid.
  There is no requirement to suppress FileCat when it occurs in the selected directory/path.
- Requested order: `FileCat`, then the selected path/location, then username and elevation information, so the
  application is easy to identify. Apply this order for administrator and ordinary-user launches.
- Windows titles now start with FileCat; selected location and account/elevation follow. Baseline updated assertions
  fail one case with one passing control; corrected account checks pass 2/2 and the existing headless window title
  case passes 1/1. Unix ordering and account/elevation detection are unchanged. The reported elevation text has not
  been independently verified on a desktop. Exact candidate interaction remains pending
  ([E-I104](evidence/E-I104-windows-title-order.md)).

### I103 — Windows instance identity disagrees with its state folders

- [E-I103](evidence/E-I103-windows-instance-identity.md): same-folder case alias is missed; portable and usual roots
  share a namespace despite distinct state. Baseline regression fails 1/1 and process cases pass only 1/4.
- Actual selected state directory now identifies the Windows mutex/pipe. Case aliases share it; genuinely different
  state roots remain separate. The read-only usual-owner probe retains an older-name compatibility check.
- Windows targeted guards 6 pass/3 Unix skips and production process cases 4/4 pass. Unix boundary guards 23 pass/4
  explicit skips. All four CI lanes pass at fa3a02a (37036329081 and manual 37036698071); Linux/macOS development
  packaging passes. Remediated/verified preliminarily; native rebuilt packages and candidate qualification pending.

### I102 — Recovery misses independent instances and other profiles

- [E-I102](evidence/E-I102-independent-instance.md): real usual-profile GUI launched with --new-instance is missed
  by the production probe; other-profile guard regression fails 1/1. Potential Critical, V09/I09 must fix.
- Independent lifetimes now use unique locked files and bounded metadata inside their guarded state directory.
  Normal forwarding remains with its elected owner; recovery checks all discoverable usual profiles and refuses
  unknown live locations. Probes create/write nothing; stopped/crashed stale files are ignored.
- Native boundary 23 pass/1 expected skip, production process 8/8, full App 208 pass/27 skips, path/state 13 pass/1
  skip and actual independent GUI before/after pass. Windows guards 5 pass/3 Unix skips, paths 13 pass/1 Unix skip.
- Remediated/natively verified; CI, rebuilt packages, wider instance discovery, write tracing and candidate closure pending.

### I101 — Recovery omits runtime temporary files

- Reproduction and exact native inputs in [E-I101](evidence/E-I101-runtime-temporary-folder.md).
- Potential Critical, must fix under V09/I09. The guard accepts a target disk containing the Unix runtime temporary
  folder when application state and socket fallback are elsewhere. No unsafe physical-device scan was performed.
- Include this process's configured temporary folder and publish both the socket and runtime temporary folder of
  the usual running instance. Refuse recovery when the latter locations cannot be determined.
- Status Remediated and natively verified: native guards 5 pass/1 expected skip, boundary cases 17 pass/1 expected
  skip, process protocol 6/6, full App 206 pass/27 explicit skips. CI/packages and candidate/device-level tracing pending.

### I100 — Unix launches in separate sessions run independently

- Native GUI failure and independent syscall trace in [E-I100](evidence/E-I100-unix-instance-session.md).
  Same user/profile/data/TMPDIR, different Unix sessions: each Local mutex reports its own first instance and the
  second process replaces the original socket. Same-session control forwards. Medium; A-07/V23 B12 must fix.
- Remediation replaces Unix named Mutex with a required native profile-local file lock and publishes the actual
  socket path for cross-TMPDIR discovery. Usual-instance probe is read-only, detects a busy owner before listener
  readiness, and its socket directory is included in recovery safety. Shutdown orders listener disposal before unlock.
- Native production API harness 1/5 before, 6/6 after; boundary cases and affected App 205/232 pass with explicit skips.
  Actual different-session/different-TMP GUI and FAT32/exFAT state cases pass. Independent syscall trace observes
  native lock/profile writes and no .NET shared mutex mutations. Full runtime write-location audit (I09), CI/macOS,
  rebuilt packages, re-audit and final-candidate closure pending. Exact working inputs/payloads in E-I100.

### I99 — Unix instance sockets under long temporary paths

- Reproduced by two full-suite failures at cc97a8d; native full App TRX and source/payload hashes in
  [E-I99](evidence/E-I99-unix-instance-path.md). Valid temporary folder plus the pipe prefix exceeds sun_path.
  Expected: instance probe/start/forward works. Actual: ArgumentOutOfRangeException. Medium; A-07/V23 B12/V09 must fix.
- Remedy preserves fitting endpoints, uses an absolute short name otherwise, and owner-only UID directory when
  necessary; UTF-8 bytes and OS limits accounted. That extra write folder participates in recovery safety checks.
  New native fresh-process harness and recovery guard regression cover boundary, long ASCII, Unicode and unknown disk.
- Eleven passes/one expected boundary skip; real same-session GUI forwarding/persistence pass; affected App
  204/231 passes with 27 skips. Separate-session failure tracked as I100. CI/macOS, rebuilt packages, re-audit and
  final-candidate closure pending. Earlier instance/recovery write-location and rebuilt-artifact evidence invalidated.
- d38f915 CI 37024802245 passes all four lanes, including the Unix boundary harness. I100 adds cross-process coverage;
  its current native boundary run has 14 passes/one expected skip after adding the usual-socket recovery guard case.

### I98 — Linux tar desktop entry corrupts special extraction paths

- **Reproduction:** development tar executable works from ampersand/space path; its Exec/Icon contain replaced sed
  lines. Native desktop validation and gio launch fail (E-I98, E-V19-P2).
- **Severity/disposition:** Medium, must fix for Linux tar desktop launch. No unintended external execution established;
  Debian package entry unaffected.
- **Remedy:** separate helper writes values as data with both Desktop Entry escaping layers. /bin/sh passes the helper
  path as an argument; --launch execs FileCat with preserved file arguments, including percent paths.
- **Verification:** real native launcher/argv/icon checks 12/12 versus unchanged 4/12; exact hashes in
  [E-I98](evidence/E-I98-linux-desktop-entry.md). Linux CI 12/12 and all four lanes pass at ecf5349. Rebuilt dev.526
  tar GUI opens from ampersand/percent/Unicode/space path on fresh 24.04. Fresh 26.04 tar GUI and dev.531 packaged-helper native argv/icon oracle 12/12 also pass. Final-candidate closure pending.

### I97 — CloneCopyTests: native UNC volume query received an incomplete root

- **Found by:** preliminary same-server SMB copy case, E-V03-CLONE-1, at `a5a3c0c`: failed before copying.
- **Cause verified independently:** `Path.GetPathRoot` gives a UNC share without a trailing separator;
  `GetVolumeInformationW` requires one. On the same controlled ReFS fixture, a separate native probe returned
  Win32 123 without it and succeeded with it. No product copying defect established.
- **Remediation (`ca1afe0`):** normalize the test's root before the native query; include the native error on failure.
- **Verification:** independent probe verifies cause; corrected local and same-server SMB cases each pass 1/1,
  without skips, at `ca1afe0`. All three copy paths preserve bytes and original content after a copy edit, with
  0 reported MiB additional space. Four-lane CI 36989898493 passes. Severity Low, validation setup;
  candidate coverage and closure remain outstanding.

### I96 — ARM64 CI: the first Recycle Bin query transiently failed

- **Found by:** CI 36985897644 at documentation-only source `3316f15`; the Platform.Windows suite had 132 passed,
  1 failed, 33 skipped. `SHQueryRecycleBinW` returned `0x800700B7` (`ERROR_ALREADY_EXISTS`) instead of S_OK.
- **Verification of transience:** CI 36987552356 at `a5a3c0c` passed all four lanes with the same Recycle Bin test and
  production query. The native cause is unconfirmed; no Shell initialization race is claimed as proved.
- **Remediation (`5503262`):** only that HRESULT may be retried, four times, 50 ms apart. Persistent failure and all
  other HRESULTs still fail. No new skip; production behavior unchanged.
- **Verification:** targeted Release test 1 passed, 0 failed, 0 skipped on the Insider 26220 host; full CI 36989092570
  at `5503262` and 36989898493 at `ca1afe0` pass all four lanes. Raw failed log, CI metadata and TRX retained;
  [E-I96](evidence/E-I96-recycle-bin-ci.md) records provenance and limits. Candidate rerun and closure pending.
- **Severity:** Low, test-environment reliability. This finding does not establish a new product data-safety defect.

### I95 — Signatures: no trust line read as a good signature

- **Found by:** V15 with an independent GnuPG (E-V15-G1).
- **What was wrong:** FileCat read gpg's status lines and took a good signature with no `TRUST_*` line as good, signer
  named. GnuPG gives no trust line under `trust-model always` in gpg.conf (observed with 2.4.9), so any key in the
  keyring, imported from anywhere and certified by no one, made a file read as signed by its publisher.
- **Remediation (`cd37342`):** no trust line reads like an uncertified key, and says gpg did not vouch for it; gpg.conf
  joins the files a cached result depends on.
- **Verification:** `GnuPgVerificationTests` (keys and signatures made by gpg 2.4.9): own key good; imported uncertified,
  trust-model always, missing key unknown; changed file bad; no key server contacted although gpg.conf asks. The
  trust-model always case reads good under the old reading. Core 746.
- **Severity:** Medium–High: a false assurance of origin, the very thing a signature check exists to give.

### I94 — Synchronize: folders inside each other were offered for synchronizing

- **Found by:** following I93 to the other place where two names of one folder matter (V13, comparison and
  synchronization).
- **What was wrong:** comparing a folder with one inside it (`P` and `P\backup`, or a folder and a junction leading
  into it) offered Synchronize as usual. In Mirror toward the outer folder, its `backup` is an item only in the target,
  and the plan removed it: the source of the synchronization itself. The other way it would copy a folder into itself,
  which the copy job refuses.
- **Remediation (`65cee78`):** the comparison runs as before, but Synchronize is not offered when one folder is the
  other or lies inside it, by path or, through links and junctions, by final path (as the copy job decides "into
  itself"); the window says why.
- **Verification:** the plan for `P\backup` mirrored onto `P` removes `backup` (what would have happened), and
  `SyncPlanner.Overlap` refuses the pair (also the same folder, and not a folder whose name merely begins alike); on
  NTFS a junction makes two folders that look apart overlap, found only through final paths; the comparison window
  offers no Synchronize for nested folders (the App test fails with the check bypassed). Core 745, Windows 166, App 230.
- **Severity:** Medium–High: the folder being synchronized from is removed, recoverably from the Recycle Bin unless
  permanent deletion was chosen.

### I93 — Duplicates: names of one file were grouped as copies

- **Found by:** reading Find's duplicates for V13 (E-V13-R1).
- **What was wrong:** duplicates group files by name, size or content, never asking whether two paths are one file. A
  set holds one file under two names through hard links, or through searches appended from a junction and from the
  folder it points to (also a substituted drive or a share of a local folder). Both names formed a group, and "Select
  all but one in each group" marked one of them as the copy: deleting it through a junction deletes the file itself,
  the only copy. A link to a file was grouped with its target the same way.
- **Remediation (`bf395c6`):** the finder takes each file's identity (FileCat's `GetFileIdentity`: volume and file ID,
  device and inode): names of one file count once (the first by path stays, the others are reported), a group needs
  two files, links to files are left out and reported; Find says what it left out and why.
- **Verification:** a stand-in identity test (portable); on NTFS, a file, a hard link to it, its path through a
  junction and a real copy: one group of the file and the copy, two names reported; without identities, one group of
  all four names. Core 744, Windows 165, App 229. Links to files: the code path is read, not tested (making one needs
  Developer Mode or administrator rights).
- **Severity:** Medium–High: data loss on a permanent deletion; it takes aliasing names in one set.

### I91 — Analyze folder: an analysis outlived its folder

- **Found by:** V12's "expensive sorting asked for and cancelled", following I88 (E-V12-C2).
- **What was wrong:** View → Analyze folder computes a column's value for every item, then sorts. Nothing tied it to
  the folder: when the tab moved on, it finished and wrote "Sorted by … with every value computed (20,000 items)" on the
  next folder's tab and re-sorted that folder; when the tab closed, it read the released listing and failed with
  `ObjectDisposedException`, which reached whoever awaited the analysis.
- **Remediation (`800cd52`):** leaving the folder or closing the tab cancels it; a listing let go meanwhile counts as
  cancelled; nothing it reports or sorts lands on the next folder.
- **Verification:** `AnalysisLeaveTests` (20,000 files, the "hidden" column, the folder left or the tab closed 0.1 s in)
  fail on the old code (the next folder's banner said "Sorted by"; the analysis failed with the exception) and pass
  twice now. App 225.
- **Severity:** Medium: a false statement about a folder that was never analyzed, and an error from a closed tab.

### I90 — Find: appending a search from a differently spelled root listed files twice

- **Found by:** reading refine and append for V13 (E-V13-R1).
- **What was wrong:** items are the same only when their folders' paths are, exactly; on Windows a root typed in
  another letter case names the same folders, but its finds carried the root as typed. Appending such a search to one
  from the disk's spelling listed each common file twice.
- **Remediation (`54c33de`):** a search walks each root as the disk spells it (each existing name in its own case,
  one lookup per name; a missing tail kept as written; other systems unchanged).
- **Verification:** `SearchRootCaseTests` (4 items for 2 before, 2 after; the spelling itself); two tests that built
  expected paths from the environment's spelling of the temporary folder adjusted. Core 743, App 222, Windows 164.
- **Severity:** Low–Medium: no file is touched by the search, but an operation on the set would act on a file twice.

### I89 — Change journal: a journal that wrapped twice during a read failed the read

- **Found by:** CI run 36941532909 (Windows, a records-only commit): `UsnJournalTests` failed after 90 s with "The
  journal entry has been deleted from the journal."
- **What was wrong:** when the journal overwrote its oldest entries during a read, the reader went on from the new
  oldest entry once only; a busy volume (a CI runner running four test assemblies) wrapped it a second time and the
  whole read failed. The skip itself was not said.
- **Remediation (`2ad2cfa`):** each jump lands past the entry asked for, so the reader goes on as often as needed (the
  start only moves on towards the end being read to) and stops if the journal answers without moving on; the journal
  view says its list begins later than the journal did.
- **Verification:** `UsnJournalWrapTests`, a stand-in journal wrapping three times during a read, and one answering
  without moving on: both fail with CI's error under the old rule, pass now. Windows platform suite 164, 0 failed.
- **Severity:** Low–Medium: no wrong data, but the view of a busy volume's journal could fail outright.

### I88 — Count: a folder's count went on after its tab closed, and could end FileCat

- **Found by:** writing V12's check that a count stops when its folder is left (E-V12-C2): the test closing the tab
  failed with `ObjectDisposedException` from the count's post to the window.
- **What was wrong:** the count posts progress to the tab's listing four times a second and its result at the end; a
  closed tab has disposed its listing, so each post threw on the window's thread. The crash guard reports one and keeps
  going but ends the process past five in three seconds: a tab closed 0.3 s into a 6-second count raised 24 in five
  seconds. Leaving the folder kept the count running for nothing, and the next folder's status line said it was
  counting, refused Count, and Esc spoke of the old folder's counts.
- **Remediation (`a9a9dcf`):** a count ends with its tab's stay in the folder: leaving or closing cancels it at once,
  takes it off the tab's running counts at once, and nothing it posts touches the listing afterwards. The same kind,
  read for elsewhere (`34042cc`): a closed SFTP tab's late navigation and a closed result tab's refreshes loaded its
  disposed listing again (a read for nobody, no exception); a disposed listing now ignores `Load`. Esc likewise ends a
  count in its tab at once, not when a call held by a slow disk returns (`84bb7ed`).
- **Verification:** `FolderCountLeaveTests` (the count held at its start, then the folder left or the tab closed) fail
  on the old code and pass; the experiment (90,300 folders, not committed): 24 window-thread exceptions before, 0
  after. App 222.
- **Severity:** High: FileCat itself ends, from an everyday action (Space on a large folder, then closing its tab),
  whenever the count had more than about a second and a half left.

### I87 — Folder watch: a folder that kept changing was not read again until the changes stopped

- **Found by:** forcing a change-notification overflow for V12 (E-V12-W1): through 100,000 changes in 30 s, the watcher
  asked for no reread at all.
- **What was wrong:** `ChangeMonitor` coalesces bursts and promised never to put a reread off more than two seconds past
  the first change, but it re-armed its timer at every change, and past two seconds the delay was floored at the
  minimum interval (300 ms or more): while changes came more often than that, the reread never came. A file every
  50 ms for six seconds: no reread until 0.32 s after the last.
- **Remediation (`3d2bb2e`):** due a quarter second after the last change, no later than two seconds after the first,
  and no sooner than the minimum interval after the previous reread (the throttle for large folders stays). The watcher
  also counts overflows of the system's buffer and logs the first (path hashed unless diagnostic mode is on).
- **Verification:** `ChangeMonitorCadenceTests`: rereads at 2.0, 4.0 and 6.0–6.1 s in five runs; fails with the old
  debounce. `ChangeMonitorOverflowTests`: an overflow forced (the handler held up 2 ms a notification) is counted and a
  reread follows. The App's churn test matches the disk 0.0 s after the churn (0.6–0.7 s before). Core 740, App 220.
- **Severity:** Medium: nothing is lost, but the panel showed a folder as it was, for as long as something kept writing
  into it.

### I86 — Operations: "Clear finished" could leave a job that had just finished

- **Found by:** CI run 36931084621 (Windows, a records-only commit): `ApplyCommandDialogTests` saw the panel still
  offering to clear after "Clear finished".
- **What was wrong:** the panel counts finished jobs by each job's own state, which the job's thread sets; "Clear
  finished" removed the rows whose own state said finished, and a row learns that its job ended from a refresh queued
  on the window's thread. Between the two, the offer was shown and clearing left the job, and the offer, in place.
- **Remediation (`506cc75`):** the summary brings rows that lag their job up to date before counting, and clearing goes
  by the job's own state, as the count does.
- **Verification:** `OperationCenterTests` holds the window's thread while a job (stopped at a name conflict, then
  skipped) ends on its own: on the old code the job stays listed after clearing; with the fix none is left and nothing
  is offered. App 220, 0 failed.
- **Severity:** Low: nothing is lost; a click did nothing, and a second one, after the refresh, worked.

### I85 — Count: a folder's counted size landed on the folder put in its place

- **Found by:** reading how a folder's size count is applied, for V12 (E-V12-C1).
- **What was wrong:** the count put its size on the panel's row of the folder's name when it ended; a folder deleted and
  made again, or replaced, under that name meanwhile is another folder, and its row showed the first one's size as
  counted. The time recorded at the start (I32) cannot tell such a folder apart.
- **Remediation (`1ec9d13`):** the folder's identity from the file system (file ID; device and inode) is read as the
  count begins and as it ends; when it differs, the row shows no size and FileCat says the folder was replaced while
  counted. Without an identity, as before.
- **Verification:** `FolderCountIdentityTests` (fails without the check); FileCat's identity of a folder deleted and
  made again at once on NTFS differs. App 218; 0 failed.
- **Severity:** Low–Medium: a size is a hint, but one shown as counted for a folder that was never counted is false.

### I84 — Compare directories: a letter-case collision dropped an item; undecided counted as the same

- **Found by:** reading the directory comparison for V13's letter-case and precision cases (E-V13-C1).
- **What was wrong:** names compared without letter case went into a dictionary that kept the first of two names
  differing only in case, so in a folder from a case-sensitive file system the second was neither compared, marked nor
  counted, and an exact-case match could lose to a case variant; the panels' comparison ignored case whenever FileCat ran
  on Windows, also against a server's folder. And a size or time a listing does not give (the Registry, some servers,
  some archives) counted as the same under that criterion.
- **Remediation (`bc65646`):** one pairing rule for both comparisons (exact names first, a case variant only where it is
  the only one left on each side, the rest one-sided and marked); case ignored only between two local folders; a
  criterion the listings cannot answer leaves the pair undecided ("could not be compared", marked; Unknown with its
  reason in the recursive comparison) unless another criterion tells the files apart.
- **Verification:** `DirectoryCompareTests` (4, among them 3,000 random folder pairs against a reference written from
  the rules); each half of the change, taken out, fails its tests. Core 728, App 213; 0 failed.
- **Severity:** Medium: a comparison exists to show what differs; one that drops an item or calls an uncompared pair the
  same misleads exactly where it is relied on.

### I83 — Find: a saved time range shown again lost the last minute of its end day

- **Found by:** reading Find's dialog for E-V13-F1's reference.
- **What was wrong:** a date typed without a time as the range's end means the end of that day (23:59:59.9999999).
  Shown again (a saved search opened), it was written with minutes only, "31.03.2026 23:59"; read back, that has a time,
  so the end became 23:59:00. A start typed with seconds lost them the same way.
- **Remediation (`c67fa85`):** the dialog's ends go through one place: an end of day and a midnight start are written as
  the date alone, any other time with its seconds; a midnight typed as an end keeps its time.
- **Verification:** `FindTimeTextTests` in cs-CZ, en-US, de-DE and ja-JP: every end and start comes back as written; the
  old formatting fails it ("31.03.2026 23:59", read back 23:59:00).
- **Severity:** Low: a minute at the edge of a range, but a saved search must find the same items each time it is run.

### I82 — Compare: the count of differences disagreed with the list

- **Found by:** E-V13-C1's corpus: two unrelated files gave a changed block followed by a right-only block, and one
  difference.
- **What was wrong:** the text comparison counted one difference per run of changes, while the compare window lists and
  steps through one per block (changed lines; lines only left; lines only right), as the result type documented: the
  summary said "1 difference" over a list of two.
- **Remediation (`db2e9b4`):** one difference per block.
- **Verification:** the corpus checks the count against the blocks in every pair; the existing tests' counts unchanged.
- **Severity:** Low: the list and the steps were right; the number above them was not.

### I81 — Compare: one coincidental unique line misaligned a text comparison, unlabelled

- **Found by:** E-V13-C1's corpus, case 6295 of seed 1309.
- **What was wrong:** the text comparison split a region on the lines that occur once on each side (patience) whenever
  there were any, and aligned exactly (Myers) only where there were none. With few unique lines, one that happened to
  occur once on each side 42 lines apart became an anchor: an edit of 11 lines (`git diff --no-index`: 5 insertions and
  6 deletions, with each of its algorithms) showed as 84 lines only left or only right, and the result was presented as
  exact. No line was called equal that was not; 40 of 20,000 generated pairs were short of the fewest differences, none
  labelled.
- **Remediation (`db2e9b4`):** a region of up to 20,000 lines (both sides) is aligned exactly when the work budget
  allows; larger regions are split on unique lines as before, which keeps million-line comparisons fast, and such a
  result is labelled heuristic in the window ("can show more differences than the fewest possible") unless it pairs as
  many lines as the two sides could share at all, which proves it the best.
- **Verification:** `CompareCorpusTests`: over 20,000 pairs no unlabelled result short of the fewest differences; the old
  alignment, without its label, fails. The million-line benchmark at the same speed; its shifted and scattered-edit
  results provably the best and unlabelled. Core 722, App 209; 0 failed.
- **Severity:** Low–Medium: nothing false was claimed equal, but the comparison exaggerated what changed, in exactly the
  kind of file (repeated values, few unique lines) where a user relies on it, and said nothing.

### I80 — Tooltips over icons are not styled by the selected theme

- **Found by:** the owner (2026-10-01): "improve tooltips for icons, style them better according to the selected theme".
- **What was wrong:** tooltips were Fluent's own, light or dark, whatever FileCat's theme was (a white tooltip over
  DosCommander's blue). The place and toolbar buttons' tips were one line run together with dots ("Recycle Bin · Deleted
  items, ... · a middle click opens it in a new tab · the right button offers ...").
- **Remediation (`fab03b8`):** tooltips take the theme's surface, as its menus and notifications do: its card color made
  solid over its window color (Psychedelic's card is see-through, which let the toolbar show through the words), its
  text and border colors and its font, with rounded corners. Icon buttons' tips are laid out: the title, its key at the
  right in the accent color where that reads as text does (WCAG's 4.5:1; Psychedelic's accent reaches 4.3:1, so its key
  is in the text color), the description, then how else the button is used, muted. Their plain text goes to screen
  readers as help text.
- **Verification:** `TooltipTests` (3): the surface is solid and text, hints and key read at 4.5:1 or more in every
  theme (fails with the card's own color: alpha 240; and with the plain accent: Psychedelic 4.3:1); a rich tip's plain
  text and parts; a toolbar button's tip drawn in Classic's colors, then in ClassicDark's. `PanelKeysTests`: toolbar tips
  still start with the button's name and give its key; screen readers get the same words. The screenshot tool's new
  `tip:` mode pictured the Copy button's and the Recycle Bin place's tips in all seven themes, looked at. App suite 209,
  0 failed. The owner wrote, after this change was pushed: "tested it on my own, works".

### I79 — Resuming checked only the bytes before the break

- **Found by:** E-V21-U1, the owner's iPhone, its cable pulled while FileCat copied photos off it.
- **What was wrong:** a copy that stops part way resumes only when the source is provably the same file. The check was
  the same size and time, and the last 64 KiB before the break reading the same. After a physical reconnect the
  iPhone sends seven of 50 photos with other bytes at the same size (a conversion's metadata, most likely); a photo cut
  at 4 MiB passed the check, and the copy kept its old first 4 MiB and appended the new rest. Here the result equalled
  the version sent before the pull, since the two versions differed only before the break; where they differ on both
  sides, the copy would have mixed them.
- **Remediation (`7ee8e92`):** the file's first 64 KiB must read the same too, or the copy starts again from the
  beginning and the job says so. The start is read first, so a source that reads forward only (a phone) reads nothing
  more than before.
- **Verification:** `ResumeTransferTests` (one byte at the start differs: copied again from the start; it fails without
  the change; the other seven unchanged); the Motorola resumed at 8,388,608 bytes after the new check, every file
  whole (E-V21-U1). Not rerun on the iPhone.
- **Severity:** Low–Medium: no copy was wrong here, but the check's promise ("provably the same file") did not hold for a
  device that regenerates what it sends.

### I78 — Phones: an unplugged phone was reported as the file "no longer exists"

- **Found by:** E-V21-U1: the owner pulled the cable seven times while FileCat copied to and from the iPhone and the
  Motorola.
- **What was wrong:** each pull during a copy off a phone ended in FileCat's question saying "The item no longer exists or
  its folder was removed." An unplugged phone answers "not found" (0x80070002) to opening it and to much else, and .NET
  raises that code as `FileNotFoundException`, not as `COMException`; every handler in `WpdSession` caught
  `COMException` only, so the device's answer reached the copy unexplained. Folder listings also took an error from
  the device's enumerator for the end of the folder, and dropped items they could not describe: a phone unplugged while
  a folder was listed gave a shorter or empty folder without an error.
- **Remediation (`7ee8e92`):** every device call's failure is handled in one place, which takes those exceptions too.
  A failure from a device that Windows no longer lists, or that does not answer a request for its own description, is
  "the device was disconnected. Connect it again and unlock it, then try again.", whatever the code, and the session is
  opened anew; a connected device's "not found" is still "not found". A listing fails on an enumerator error, and an
  empty or cut answer from a device that is gone is "disconnected".
- **Verification:** `MtpDisconnectTests` (5; the case of a device Windows no longer lists fails without the device-list
  check). On the Motorola, the same pull before the fix (three times) and after it: "Could not open the device: the
  device was disconnected...", resumed after Retry, all files whole; a trace of each step showed the cause. The MTP
  device tests on the Motorola after each change: 19, 0 failed.
- **Severity:** Medium: telling the user their file no longer exists, when the phone was unplugged, is a false
  statement about their data; a cut listing shown as the folder's contents is worse.

### I77 — Windows: a FileCat test littered the owner's Recycle Bin with records

- **Found by:** E-BIN-1, the first look at FileCat's new view of the owner's Recycle Bin: two leftovers of a FileCat
  test, `filecat-recycle-test-…`, among the owner's items.
- **What was wrong:** Windows keeps each deleted item as two files in `X:\$Recycle.Bin\<SID>`: the item (`$R…`) and its
  record (`$I…`: where it was and when it was deleted). FileCat's undo of a recycle restores through the Recycle Bin's
  own "undelete" command, which moves the item back and leaves the record; Windows neither shows nor counts a record
  without its item, and nothing removes it, emptying the bin included. `WindowsFileOperationsTests` recycles a file and
  undoes it on the computer it runs on, so every run left one record in the real bin: the owner's bin held 163 records
  of the test's files (162 found at first, one more, from a run in the user's own temporary folder, found when this
  entry was checked) and two of their items, never restored.
- **Not FileCat's alone:** on the lent Windows 11 VM (build 26300), Explorer's own "Restore the selected items" brought
  a file back in milliseconds and left its record unchanged (three times out of three), as did the undelete command
  run on a thread that kept handling messages for 15 s, on a thread that ended at once (as FileCat's did), and through
  Explorer's own view of the bin; emptying the bin with Windows' own call left such records where they were. The first
  version of this entry, the fix's comment and its commit message said Explorer's restore removes the record; that was
  not checked then, and it is wrong. Whether Explorer's Ctrl+Z after a restore uses the record was tried and stayed
  unknown (the keystroke did not demonstrably reach the list).
- **Remediation (`f95e4cd`; comment corrected afterwards):** the test removes its own items and records from the bin
  whatever happens. After its own undo, FileCat also removes that item's record, which Windows' own Restore leaves: only
  the record beside the restored item, named after it, and only once the item has left the bin and is back where it was.
  This goes one step past Windows; Windows uses no such record (it neither shows nor counts it), and FileCat's restore,
  done in FileCat's own process, is not on Explorer's undo list. The 163 records and the two items were removed from the
  owner's bin, only those (by the test's path in each record). Six other records without items stay: one on D: from
  2023, three on C: from 2025-02 to 2026-05, and two on C: in a temporary folder from FileCat's first night, which
  match neither the test's names nor its file's size and are not provably FileCat's.
- **Verification:** `WindowsFileOperationsTests` (a recycle undone leaves nothing of the item in the bin; it fails without
  the change); Windows platform suite 152, 0 failed. The owner's bin: the Shell's own count of each drive's items
  (`SHQueryRecycleBin`) equals what FileCat's view lists; no record of FileCat's test is left. The VM's bin was emptied
  and its experiments' nine records removed (scripts `artifacts/vm/win-restore-exp*.ps1`).
- **Severity:** Low: no user data is affected and Windows ignores such records, but a test should leave nothing on the
  computer it runs on, least of all in the user's Recycle Bin.

### I76 — Delete: a folder's read-only mark stopped FileCat's delete

- **Found by:** E-CLOUD-1's writing test in a test folder of the owner's OneDrive: the test's own folders could not be
  removed by FileCat's permanent delete.
- **What was wrong:** on Windows a folder's read-only attribute is the Shell's mark of a customized folder (one with a
  desktop.ini), and OneDrive sets it on every folder it keeps in sync; it protects nothing, and Explorer deletes such
  folders. `RemoveDirectory` refuses a folder while it is set, and FileCat removed folders with it as they were, so its
  delete stopped at each one with "Access is denied. If the destination is a protected folder, Windows Controlled
  Folder Access may be blocking FileCat" — advice about something else. The same stopped a move of such a folder to
  another drive from removing its source.
- **Remediation (`edd950a`):** removing a folder on Windows clears the mark when it is the reason for a refusal, and
  puts it back when the folder still cannot be removed. Read-only files are asked about, as before; Linux and macOS,
  where the permission means what it says, are unchanged.
- **Verification:** `ReadOnlyFolderTests` (a read-only folder goes; a tree of them is deleted without a question; a
  non-empty one keeps its mark, which walks the put-back path) — all three fail without the change. On the owner's
  OneDrive, FileCat's delete removed the read-only folders and the test folder; Windows platform suite 143, 0 failed.
- **Severity:** Medium: deleting a folder in OneDrive is everyday work, and the message sent the user to the wrong place.

### I75 — Taskbar: the pinned icon read small on a dark taskbar

- **Found by:** the owner ("icon still looks too small in taskbar on Windows when compared to others ... when pinned but
  not executed yet"), after `ecaa254` had redrawn the 24-pixel frame for the same complaint.
- **What was wrong:** on the lent Windows 11 VM's real taskbar a pinned item is drawn from the 32-pixel frame shrunk to
  24, not from the 24-pixel frame, so `ecaa254` changed a frame the taskbar never shows. The artwork's 32-pixel edge
  is a dark teal around a near-black window; shrunk, it melts into the window, and on a dark taskbar only the lit rows
  read.
- **Remediation (`a9f48cf`):** the frames the taskbar shrinks (32, 48, 64) get a two-pixel cyan edge, everything inside
  and the transparency unchanged; the 16- and 24-pixel frames take the artwork's proportions; 128 and 256 unchanged.
- **Verification:** the VM's dark taskbar as pinned and not running: the new icon reads as tall and wide as Salamander's;
  the built exe carries exactly these frames (E-ICON-1, "The taskbar again").
- **Severity:** Low: appearance, but the first thing a user sees of the program.

### I74 — Content search answered otherwise than the files at read boundaries, and missed UTF-8 inside UTF-16 files

- **Found by:** V13's differential corpus test (E-V13-S1): files of every encoding FileCat reads, words planted at
  chosen offsets and encodings, every query's answers compared with reading each whole file at once.
- **What was wrong:** FileCat reads a file in 1 MiB windows and carries the end of one into the next. (1) A regular
  expression without whole words was matched against each window as though the window's edges were the file's, so
  `word$` matched where a read happened to end and `^word` or a look-behind where a window's carried text began. (2)
  A case-ignoring linguistic match that ended with a window was accepted although the next read began with a combining
  mark that changes its last letter ("naïve" found in "naïvë"). (3) With Unicode, a word kept as UTF-8 inside a file
  read as UTF-16 was not found, though `SearchQuery.Unicode` promises UTF-8: that reading was left out for ASCII text.
- **Remediation (`b0a2313`):** every regular expression and every linguistic match follow the window rule whole words
  already followed (a match at a window's first character is left to the window before; one that ends with the window
  waits for the next read, and counts at the file's end); the UTF-8 reading is added for files read as UTF-16.
- **Verification:** before, 2 of 640 answers wrong with the default corpus and 5, 4 and 2 of 1,920 with three larger
  ones; after, none. The existing content search tests pass; core suite 714, 0 failed.
- **Severity:** Low–Medium: false results in a search, rare per file (a candidate must sit on a read boundary) but
  untrustworthy where they happen; the UTF-16 case is a documented promise not kept.

### I73 — Type icons: a placeholder name made the Shell try to open `C:\file.url` from FileCat's own process

- **Found by:** V24's file trace of browsing a folder built to tempt FileCat (E-V24-D1-F1): the kernel's file events of
  the browsing processes, between the moment the fixtures are built and the moment browsing ends.
- **What was wrong:** to draw a type's icon (and to name a type), FileCat asks the Shell about a placeholder such as
  "file.url", with the flag that tells it not to touch the file. For .url the Shell's handler tried to open it anyway, as
  `C:\file.url`, from FileCat's own process rather than the restricted helper. The path is constant and local, nothing
  was there, and standard users can make only folders at the root of `C:\` (`icacls`); but a placeholder should name no
  place at all.
- **Remediation (`f1b48de`):** every Shell type lookup (icon place, small icon, type name) uses a fully qualified name
  in a folder below System32 that does not exist and that only an administrator could make.
- **Verification:** `TypeIconTests` (the name; and the Shell answering it exactly as it answered the bare name, icon
  place and type name of ten types, a folder among them). The trace with the change taken out shows the attempt, and
  with it does not, nothing else differing (`v24-browse-files-before.txt`, `v24-browse-files-after.txt`). Platform suite
  139, app suite 203, 0 failed.
- **Severity:** Low: hardening; nothing was read, contacted or run.

### I72 — Phones: creating folders, renaming and copying onto an iPhone were offered, and every one failed

- **Found by:** V21 on the owner's iPhone (E-V21-I1): the plan asks that a read-oriented device of the iPhone class
  shows its read-only capability as such.
- **What was wrong:** `MtpProvider.GetCapabilities` offered the same changes inside every device's storage — new
  folders, renaming, copying onto it and deleting — whatever the device. The iPhone's storage even says read-write
  (`WPD_STORAGE_ACCESS_CAPABILITY` 0); what says otherwise is its driver's list of commands, which has deleting and
  no command to create an object or set a property. So F7, F2 and copying onto the iPhone were all offered, and the
  one attempt made (a folder named `FileCat-test`, which the owner's rule allows) ended in "Could not create the
  folder: The request is not supported. (0x80070032) (0x80070032)" — the code twice, since COM's message already
  carries it. Nothing on the phone changed.
- **Remediation (`2e93339`):** the session reads the commands the driver lists when the device is opened, and each
  storage's access when the storages are listed (for a write-protected card, or a camera that offers its pictures
  read-only apart from deleting). FileCat offers what both allow and explains the rest — on an iPhone, "The device
  does not let a computer create folders on it; it offers its files to copy off and to delete, as iPhones do." The
  device jobs refuse with the same reason before sending anything, so a drop, a typed destination or a resumed job
  ends the same way. While unknown (a device not opened yet), everything is offered and the device decides, as before.
  Also: a COM message that ends with its code no longer gets it twice; "not supported" is said plainly; the advice for
  a locked or untrusted device names the iPhone's question to trust the computer.
- **Verification:** on the iPhone, `MtpTests.What_FileCat_offers_in_a_storage_is_what_the_device_allows_there`: the
  driver's answers, and FileCat offering listing, reading and deleting only, from the device's list and with the
  storage opened directly. Without a device, `MtpCapabilityTests` (five): every combination of commands and storage
  access, the explanations, and the jobs refused before reaching the device; with the upload's guard taken out, its
  case fails. Windows platform suite: 137 total, 0 failed. A positive control for the commands read: a USB drive's
  WPD view on the same computer lists every create, write and set command.
- **Severity:** Low–Medium: nothing could be harmed (the device refused each time), but the plan's V21 requirement
  failed and every offered change ended in an error that did not say why.

### I71 — An older FileCat saved over a newer FileCat's window layout

- **Found by:** the V11 pass on state files ("newer schemas not destructively rewritten"). Plan §19.1 says state a
  newer FileCat wrote opens read-only and is never overwritten; `JsonFileStore` reports such a file as
  `NewerSchemaReadOnly`, and the guarantee rests on every save checking that.
- **What was wrong:** settings and history check it; the window layout (`workspace.json`) did not. Startup set a newer
  layout aside and used the default one, but nothing marked the file read-only, and the layout is saved every minute
  and on exit. Measured with the unchanged code: after two saves, **no file in the folder still held the newer
  layout** — the first save rotated it into `workspace.json.bak`, the second rotated that out. Running an older
  FileCat for two minutes (a portable copy, a downgrade to try something) cost the newer one its layout.
- **Remediation (`b70be07`):** `MainViewModel.LoadSavedWorkspace` loads the layout and remembers when a newer FileCat
  wrote it (`WorkspaceReadOnly`); `SaveWorkspace` then leaves the file alone, and so does a layout reset the user asks
  for at start. FileCat says once that the layout was saved by a newer version, as it already did for settings.
- **Verification:** `WorkspaceSchemaTests.A_layout_saved_by_a_newer_FileCat_is_not_saved_over` — a layout of the next
  schema with a field this version has never heard of, two autosaves and an exit save, with and without a reset: the
  file is byte for byte unchanged and no backup was made of it; a layout of the current schema is still saved (the
  control). It fails with the guard taken out. App suite: 201 total, 0 failed.
- **Severity:** Medium: no files of the user's are touched, but their working arrangement is silently lost.

### I70 — A Shell picture that got no answer was remembered as the file having none

- **Found by:** a CI failure on the Windows ARM64 lane (`ShellPictureUiTests`, run 36875934453), which said "quick
  view's request was answered with no picture; helpers started: 2" — the helper had died and been replaced, and the
  file was left without a thumbnail.
- **What was wrong:** the restricted helper is meant to be fragile: it runs the Shell's own handlers, so one that
  crashes takes the helper with it, and a busy computer can miss the 20-second start. `ShellHostClient` answers all of
  these with `null`, exactly as it answers "this file has no picture", and `ShellPreviews` cached that null under a key
  made of the file, its time and the size. Quick view asks once per item shown, so the picture stayed missing for the
  rest of the session even though a fresh helper would have made it. Icons fell back to the type icon the same way.
- **Remediation (`3f647bd`):** the client now says which of the three it was — the helper **answered** (a picture or a
  definite none), it **refused** (this item hung or crashed a helper before, or pictures are off for this session), or
  it **failed** (no helper answered at all). Only an answer or a refusal is remembered. A failure is tried again next
  time the picture is wanted, at most three times per file, so a handler that brings the helper down on every try is
  still given up on.
- **The same again on the icon side (`855674a`):** `NativeIconSource` keeps a plan per item, so a shortcut or a
  customized folder whose icon the helper never answered for stayed a plain type icon for the session too. The same
  distinction now reaches it (`ShellPreviews.GetWithAnswerAsync`), and a plan made from an unanswered request is not
  kept. This also covers a case that has nothing to do with a crash: while FileCat recovers deleted files it pauses
  Shell pictures (I09), and every icon asked for in that time was being remembered as "none" for the rest of the
  session — now they are asked again once the scan is over.
- **Verification:** `ShellHostTests.A_request_that_got_no_answer_is_not_remembered_as_the_file_having_no_picture` —
  two failures leave nothing remembered and are asked afresh, the answer that follows is remembered and ends the
  asking, and a file that fails every time is given up on after three. Checked against the unfixed code as well, where
  it fails on the first assertion. The paused case is checked in the same test.
- **Severity:** Medium: a visible feature degrades for the rest of a session after a fault it was designed to survive.
- **And the single showing (`c7a02e9`):** quick view asks once for the item on screen, so when that very first attempt
  got no answer the picture still did not appear until the file was looked at again — which is exactly what failed the
  ARM64 lane: its own diagnostics show the item was never poisoned (a direct request right after succeeded), so the
  first helper had simply not started. `ShellPreviews.GetForDisplayAsync` asks once more when no helper answered,
  which starts a fresh one; a refusal or a real "none" stays final, and nothing at all is asked while paused.
  `ShellHostTests.What_is_on_screen_is_asked_for_once_more_when_no_helper_answered` covers the four cases and fails
  with the retry taken out.

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
  Filename collision remediated to sbom-<version>-<rid>.json; actual assignment/native inventory queries retain both
  documents (E-I03-rid-inventory). This is a package list; complete per-artifact/native/runtime/helper provenance remains open.
- **I03 / I18 (E-V19-P1, E-I15-V1):** packages carry code for other architectures: the macOS arm64 app bundles
  universal (`x86_64 arm64`) `libAvaloniaNative`, `libHarfBuzzSharp` and `libSkiaSharp`; the x64 Windows payload carries
  foreign-architecture WebView2 loaders. The inventory must list them, and the release owner decide whether to thin them.
- **I04 (E-V19-P1, E-I04-ubuntu26-icu):** fresh Ubuntu 26.04.1 reproduces the `.deb` clean-install failure: its ICU
  alternatives end at libicu76, none is available and APT exits 100. Producer adds libicu78 (cc97a8d); rebuilt 26.04
  dependency/CLI/desktop/lifecycle cases pass, 24.04 compatibility pending. Support-contract/other-platform gaps remain open;
  the macOS bundle declares `LSMinimumSystemVersion 13.0`, which the Platform Support Decision must match (DEC-02). On
  Ubuntu 22.04 the `.deb`, tarball and AppImage installed, ran and uninstalled cleanly.
- **DEC-03 input (E-V19-P1):** Gatekeeper rejects the ad-hoc-signed app, quarantined or not.

## Initial register entries not yet worked

I01–I08, I10, I11, I13, I14 and I18 keep the plan's §7 text as their current record, I16 beyond what is recorded
above, and I17 for the parts not worked above.
I24–I27 are queued owner reports and findings of lower severity.
None has been closed. Their evidence, reproduction and remediation fields will be filled when worked.
