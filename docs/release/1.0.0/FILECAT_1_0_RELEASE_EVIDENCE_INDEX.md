# FileCat 1.0.0 — release evidence index

Every entry follows plan §1.3: what it proves, source SHA, build/run, environment, fixture, result, limitations and issues.
**Classification** separates static evidence, preliminary automated or runtime evidence on development builds, and final
qualification (FQ) of candidate artifacts. **No entry is final qualification: no release candidate exists yet.**

Raw outputs that are not committed (TRX files, job logs, VM logs) are retained locally under `artifacts/release-evidence/`
on the execution host and identified by SHA-256 in each record. That is not the release-owner-controlled, read-only store
the plan requires (§12.5); choosing one is an open decision (see blockers).

| ID | What | Source | Environment | Result | Class | Record | Issues |
|---|---|---|---|---|---|---|---|
| E-A01 | CI run 36722039034: per-lane outcomes and explicit skips | `4f6b062` | GitHub-hosted: `windows-latest`, `windows-11-arm`, `ubuntu-latest`, `macos-latest` | Green; the three package jobs **skipped** | Preliminary automated | [E-A01](evidence/E-A01-ci-36722039034-skip-inventory.md) | — |
| E-R04 | Step 4, first pass: the plan's code anchors and C01–C29 routes against the source | `906f1e9` | Static | 137 cited names in 421 rows all present (8 rows name non-code or renamed items, each explained); every capability has a route | Static | [E-R04](evidence/E-R04-anchor-reconciliation.md) | — |
| E-A02 | CI run 36821398706: every lane's skips with their reasons from source; tests no lane runs | `dd1e326` | GitHub-hosted: all four test lanes | 37 tests run on no lane (all gated on labs, devices, a phone, benchmarks); the ARM64 lane ran no Remote tests (added `98bc539`) | Preliminary automated | [E-A02](evidence/E-A02-ci-36821398706-skips-all-lanes.md) | — |
| E-L01 | The S10 Windows lane run locally (Release) | `4f6b062` | Physical host, Windows 11 Insider 26220 | 0 failed: Core 500/32 skipped, Windows 87/15, Remote 38/5, App 154/4 | Preliminary automated | [E-L01](evidence/E-L01-local-run-4f6b062.md) | — |
| E-I19-R1 | Interrupted-copy cleanup deletes the wrong files | `4f6b062` | Physical host; git worktree at `4f6b062` | Defect reproduced: 4 Core tests fail; Run again deletes a user's edit | Preliminary automated | [E-I19](evidence/E-I19-interrupted-copy.md) | I19 |
| E-I19-V1 | The I19 fix | `f87ad32` | Physical host; CI 36754000317, 36756346845 | Targeted and full suites pass; CI green on Windows x64, Ubuntu, macOS (ARM64 red on I20), then all green | Preliminary automated | [E-I19](evidence/E-I19-interrupted-copy.md) | I19 |
| E-I20-R1 | `$LogFile` shows an earlier item's operations | `f87ad32` | CI ARM64 job 110019476573; physical host (elevated) | 1 CI failure; 3 of 15 local runs fail | Preliminary automated | [E-I20](evidence/E-I20-logfile-attribution.md) | I20 |
| E-I20-V1 | The I20 fix | `45efc09` | Physical host; CI 36756346845 | 15 of 15; full suite green; CI green on all four lanes | Preliminary automated | [E-I20](evidence/E-I20-logfile-attribution.md) | I20 |
| E-I15-S1 | Installer script and Inno Setup semantics | `4f6b062` | — | Recursive `{app}` deletion confirmed | Static | [E-I15](evidence/E-I15-uninstall-vm.md) | I15 |
| E-I15-V1 | Install and uninstall into a folder holding user files; default folder with one run | Scripts `4f6b062` / `5b061cc`; payload `f87ad32` | VMware VM, Windows 11 Insider 26300, Inno Setup 6.7.1 | Baseline deletes the user's files; fixed keeps them and still removes its own folder; FileCat writes nothing into its folder | Preliminary runtime | [E-I15](evidence/E-I15-uninstall-vm.md) | I15 |
| E-ENV-00…05 | Host, Windows Sandbox failure, lent VMs and Mac, CI installer-compiler provenance, GitHub state, test servers | — | — | See record | Preflight facts | [E-ENV](evidence/E-ENV-environments.md) | I01, I03, I11, I18 |
| E-S01 | Tests that passed by returning before asserting (step 3) | `552aa62` → `be6ca25`; scan of `5c54181` | Static | 28 such tests made explicit skips; none left outside the Windows-only project | Static | [E-S01](evidence/E-S01-early-returns.md) | — |
| E-X01 | Suites on the lent Windows 11 VM (unelevated), Ubuntu 22.04 VM (also inside its GNOME session), the owner's M1 Mac; CI runs of the campaign's commits | `be6ca25` … `d40e510`, `ca91908` | See record | Found I21, I22, I23, I27, I28 and a Mac keychain test defect; after the fixes 0 failures | Preliminary automated | [E-X01](evidence/E-X01-cross-platform-runs.md) | I21, I22, I23, I27, I28 |
| E-I17-S1/R1 | Consent text hid steps after the 60th; HKU hives mislabeled | parent of `33b7de2` | Physical host | Reproduced by 2 new tests | Static + preliminary automated | [E-I17](evidence/E-I17-consent.md) | I17 |
| E-I17-V1…V3 | Consent fix; runtime checks of the installed helper | `33b7de2`, `5c54181` | Host; lent Windows 11 VM (UI Automation) | First runtime check found 2 defects in the fix; after `5c54181` all 130 steps shown, Cancel ran nothing | Preliminary runtime | [E-I17](evidence/E-I17-consent.md) | I17 |
| E-I21-R1/V1 | Registry explicit views without administrator rights | `be6ca25` → `47c27b9` | VM (unelevated); host (restricted token); CI | Reproduced; fixed; views match Windows' own listings | Preliminary automated | [E-I21](evidence/E-I21-registry-views.md) | I21 |
| E-I22-D1/M1/R1 | Replace refused while a file is open; comparison released files late | `be6ca25`, `552aa62`, `5c54181` | VM, CI ARM64, host probe | Mechanism established; 3 reproductions fail on the unchanged code | Preliminary automated | [E-I22](evidence/E-I22-replace-open-file.md) | I22 |
| E-I22-F1 | Replacing an open file on FAT32 and exFAT destinations | `1cb395e` (gated test) | Lent Windows 11 VM; Windows-formatted virtual disks | 1/1 on each: "in use", target kept, nothing staged left, Retry replaces once closed | Preliminary automated | [E-I22](evidence/E-I22-replace-open-file.md) | I22, I34 |
| E-I22-V1 | The I22 fix | `63d5fc4` | Host; unelevated VM; CI 36773433835 | New tests fail before, pass after; all suites green; 15 more App-suite runs green | Preliminary automated | [E-I22](evidence/E-I22-replace-open-file.md) | I22 |
| E-I23-D1/M1/R1 | Discovery lost a device's name when it came late | `5c54181` | Ubuntu VM | Mechanism established from source; not reproduced by in-guest load | Preliminary automated | [E-I23](evidence/E-I23-discovery-naming.md) | I23 |
| E-I23-V1 | The I23 fix | `d40e510` | Host worktree; Ubuntu VM; CI 36773433835 | Deterministic test fails before (the U2 message), passes after; suites and 20 discovery runs green | Preliminary automated | [E-I23](evidence/E-I23-discovery-naming.md) | I23 |
| E-I28-D1/R1/V1 | Fuzzing found a damaged NTFS size that made the whole volume unreadable | `5c54181` → `98fb594` | M1 Mac; host | Round 8842 fails before, passes after; Core suite green | Preliminary automated | [E-I28](evidence/E-I28-ntfs-fuzz.md) | I28 |
| E-I28-C1 | Fuzz campaign on the fixed build, millions of rounds over four machines | `98fb594`, `bb977d0` | Host, Ubuntu VM, Windows VM, Mac | Second NTFS finding (fixed `bb977d0`); running | Preliminary automated | [E-I28](evidence/E-I28-ntfs-fuzz.md) | I28 |
| E-I26-R1/V1 | Progress at 100% during verification; the new progress model and time left | `2197074` → `d40fda0` | Host; screenshot tool | 100% for 63% of a verified copy before; after: all suites green, honest range | Preliminary automated | [E-I26](evidence/E-I26-progress.md) | I26 |
| E-I29 | Shell preview request race (CI red on ARM64) | `2197074` → `7175a41` | CI ARM64; host | Reproduced deterministically; fixed; CI green | Preliminary automated | [E-I29](evidence/E-I29-shell-preview-race.md) | I29 |
| E-I30 | How a running operation shows: strip, details, speed graph, taskbar | `d40fda0` → `67f70f9` | Host; screenshot tool; Windows VM desktop | New display pictured and covered by view-model tests; taskbar states seen on a real desktop | Preliminary automated/runtime | [E-I30](evidence/E-I30-operations-ui.md) | I30 |
| E-V08-L1 | FileCat's remote client against real SFTP and FTPS servers | `3ec60cc`, `5183cd3` | Host → Ubuntu VM (OpenSSH, vsftpd) | 13 of 13: trust, pinning, consent, byte-exact round trips, refusal, cancel (I33 fixed), an upload cut off by the server resumes after its check | Preliminary automated | [E-V08-L1](evidence/E-V08-L1-remote-lab.md) | I33 |
| E-V08-S1 | FileCat's file operations on a real SMB share | `5183cd3` → `6585024`, `02acee6` | Host (Windows' SMB client) → Ubuntu VM (Samba) | 7 of 7 after I34's fix: round trip checked by the server's digests, no Recycle Bin, server-side rename, metadata question, open file, cancel, dropped session; 7 of 7 again at `02acee6` | Preliminary automated | [E-V08-S1](evidence/E-V08-S1-smb-lab.md) | I34 |
| E-I35 | Read-back verification for uploads, downloads and extraction | `53b0794` | Host | Implemented and tested; resume with altered tail and earlier content | Preliminary automated | [E-I35](evidence/E-I35-read-back.md) | I35 |
| E-I36 | FTP names exact or refused | `e50b9d4` | Host; library probes; Ubuntu VM (vsftpd) | Heuristics off, exact listing names, unsendable names refused; live and pyftpdlib tests | Preliminary automated | [E-I36](evidence/E-I36-ftp-names.md) | I36 |
| E-I37 | Recovery scans bounded by the volume | `02acee6` | Host; Ubuntu VM kernel log | 1 GiB / 513 MB rounds found and fixed; worst rounds 0–42 MB after | Preliminary automated | [E-I37](evidence/E-I37-recovery-allocation.md) | I37 |
| E-I38-I39 | Remote transfers at a 100 ms round trip; FTP stats, SFTP uploads, the restart choice | `02acee6` → `2ba114e`, `1dce2c2` | Host → Ubuntu VM with netem | 15/16 → 16/16; 33 min 52 s → 20 min 36 s; a cut-off upload 6 min 59 s → 3 min 31 s | Preliminary automated | [E-I38-I39](evidence/E-I38-I39-remote-latency.md) | I38, I39 |
| E-I41 | SFTP socket buffers; per-file round trips | `1dce2c2` → `4c6b910` | Host → Ubuntu VM with netem | 32 MB at 100 ms: down 1.23 → 11.58 MB/s, up 1.62 → 8.17; whole lab 19/19 in 13 min 8 s | Preliminary automated | [E-I41](evidence/E-I41-sftp-socket-buffers.md) | I41, I42 |
| E-I43 | Modified times kept on FTP servers without MFMT, and back | `e399276` → `e527a86` | Host → Ubuntu VM (vsftpd, OpenSSH) | Tree times wrong on vsftpd before (both FTPS modes), kept both ways after over all three | Preliminary automated | [E-I43](evidence/E-I43-I44.md) | I43 |
| E-I44 | A running job not shown as interrupted to a second FileCat | `e399276` | Owner's Mac; host | New test fails on macOS before, passes after; journal suites green on Windows and macOS | Preliminary automated | [E-I44](evidence/E-I43-I44.md) | I44 |
| E-V08-L2 | The remote lab against a second implementation (ProFTPD: FTPS with MLSD, `mod_sftp`); the Windows VM as a client | `e527a86` → `3f1b554`, `111ebcd`, `d228632` | Host and the lent Windows 11 VM → Ubuntu VM (ProFTPD 1.3.7c beside OpenSSH, vsftpd, Samba) | 14/19 at first: I46, I47, a test expectation; after: 19/19 ProFTPD, 19/19 OpenSSH/vsftpd, 7/7 Samba; from the Windows VM 21/21, 21/21, 7/7 | Preliminary automated | [E-V08-L2](evidence/E-V08-L2-second-implementations.md) | I45, I46, I47 |
| E-DPI | Source review of DPI P01, P02, P04–P06, P08–P12, P14; P07 in part | `16b1e47` → `65a76f8`, `efc128f`, `7a99f9d`, `cf92679` | Host; owner's Mac; lab servers | I48–I51, I53–I55 found and fixed (with I44, I46, I47 from the same rows; I50's folder check redone after CI flakes); P02, P08, P12, P14 without defects | Static + preliminary automated | [E-DPI](evidence/E-DPI-review.md) | I44, I48–I51, I53–I55 |
| E-V09-W1 | Recovery from NTFS, exFAT and FAT32 images made by Windows' own drivers | `9a0725b` → `78a48ce` | Lent Windows 11 VM (images); host | NTFS 6/6, exFAT 4/4 with the reused folder's 2 rightly declared lost, FAT32 4/6 with 2 wrong guesses (I52) → 6/6; raw bytes read independently | Preliminary automated | [E-V09-W1](evidence/E-V09-W1-windows-made-images.md) | I52 |
| E-I25 | Markdown drawn as a page | `7abd0fe` | Host; WebView2 | 44 renderer tests (hostile inputs), App viewer test, real-WebView2 test with a picture of the page | Preliminary automated/runtime | [E-I25](evidence/E-I25-markdown.md) | I25 |
| E-V09-T1 | Recovery sessions under a write trace in the Windows VM: a dismounted source with FileCat's files on another disk; the system drive with FileCat's files on a share; FileCat's files on the source | `1df5a21`, `1977e8e` | Lent Windows 11 VM; Process Monitor | Source opened for reading only and unchanged (hash); no file of FileCat's on the system drive (NTFS's own metadata writes disclosed); the third refused before any device access | Preliminary live | [E-V09-T1](evidence/E-V09-T1-windows-write-trace.md) | I09 |
| E-B02-A1 | Damaged archives of every format, listed and read | `b0b2329`, `6bbbb21` | Host; Ubuntu VM; owner's Mac; CI | 5,000 rounds of each of eleven formats passed on the host; 200,000 of both 7z passed on Ubuntu; longer runs going | Preliminary automated | [E-B02-A1](evidence/E-B02-A1-archive-fuzz.md) | — |
| E-I09-T1 | Where writing goes, on real systems: loop devices, disk images and a VHDX whose files lie on the source's disk; memory and links from it; shares served by the same computer | `7418c04`, `0a52b7b` | Ubuntu VM; owner's Mac; Windows VM; host | All cases as expected after `7418c04` (which the first Ubuntu run found); the old build took `\\localhost\C$` for another disk | Preliminary live | [I09](FILECAT_1_0_RELEASE_ISSUES.md) | I09 |
| E-V19-P1 | `.deb`, tarball, AppImage on Ubuntu 22.04; macOS app ZIP on an M1 Mac | CI 36759624490 (`45efc09`) | Lent Ubuntu VM; owner's Mac | Linux packages install, run and uninstall cleanly; Gatekeeper rejects the ad-hoc app; universal dylibs in the arm64 app | Preliminary runtime | [E-V19-P1](evidence/E-V19-P1-preliminary-packages.md) | I03, I04, DEC-03 |

## Commits made by the campaign

| Commit | Change | Issues | Evidence |
|---|---|---|---|
| `f87ad32` | Interrupted-copy recovery deletes only provably cut-short copies, names each, re-checks before deleting | I19 | E-I19-R1, E-I19-V1 |
| `5b061cc` | Installer: no recursive deletion of the installation folder on uninstall | I15 | E-I15-S1, E-I15-V1 |
| `45efc09` | `$LogFile` attribution by the item's own sequence number; waits for the on-disk log | I20 | E-I20-R1, E-I20-V1 |
| `552aa62` | Release records (report, issues, evidence index, blockers) | — | — |
| `be6ca25` | A missing precondition is an explicit skip with its reason, never a pass (28 tests) | — | E-S01 |
| `33b7de2` | Consent window shows every step in pages; whose Registry each change is in | I17 | E-I17-R1, E-I17-V1 |
| `47c27b9` | Registry hives opened from predefined handles, so explicit views work unelevated | I21 | E-I21-R1, E-I21-V1 |
| `64ed037` | Test fixes: a keychain macOS cannot unlock over SSH skips; the Synchronize test reports its jobs when it fails | — | E-X01 (M1, M2) |
| `5c54181` | Consent paging updates the right element; pages of 20 keep the buttons on screen | I17 | E-I17-V2, E-I17-V3 |
| `1857882` | Release records: I17, I21, I22–I24, cross-platform runs, preliminary package checks | — | — |
| `63d5fc4` | Windows replace falls back to a POSIX-semantics rename and reports "in use"; comparisons release files at once | I22 | E-I22-R1, E-I22-V1 |
| `d40e510` | Discovery name lookups outlive the search window | I23 | E-I23-V1 |
| `98fb594` | NTFS decoder bounds sizes and runs; sparse runs as one extent; fuzz test names rounds, replays saved ones | I28 | E-I28-R1, E-I28-V1 |
| `9af4db4` | Release records: I22, I23, I28 fixed; I25–I27 queued | — | — |
| `2197074` | Panels show seconds in the Modified column by default | I24 | screenshot |
| `7175a41` | Shell previews: a request in progress stays joinable | I29 | E-I29 |
| `bb977d0` | NTFS root record never listed as an item; listing preparation inside the safety net | I28 | E-I28 |
| `d40fda0` | Progress counts all work; honest, steady time left; screenshot mode for operations | I26 | E-I26 |
| `b4bbd0b` | Release records: I24, I26, I29, I28's second finding; I30 opened | — | — |
| `6e9ee75` | A counted folder size is tied to the folder's own time when counting began | I32 | issue record |
| `67f70f9` | Operations strip and details: phase, percentage, current file, speeds, graph; Windows taskbar progress | I30 | E-I30 |
| `069732d` | Test: the shell-picture sharing test warms the helper first (ARM64 timeout) | I29 | E-I29 |
| `3ec60cc` | A cancelled upload discards its partial copy; live-server tests | I33 | E-V08-L1 |
| `5183cd3` | A lost connection is described by what happened (not "see inner exception"); live tests of an upload cut off part way | V08 | E-V08-L1 |
| `6585024` | A replace refused because the file is open is reported in use; a share is named as what cannot keep metadata; SMB lab tests | I34 | E-V08-S1 |
| `53b0794` | Read-back verification for uploads, downloads and extraction; other copies say they were not read back | I35 | E-I35 |
| `e50b9d4` | FTP names travel exactly or are refused | I36 | E-I36 |
| `02acee6` | Recovery scans no longer allocate by a damaged size; fuzz allocation budget | I37 | E-I37 |
| `5b8b180` | Test: a copy faked as cut short has a fresh creation time (CI flake, run 36790868204) | — | E-X01 |
| `7abd0fe` | Markdown files drawn as pages (built-in renderer through the page engine) | I25 | E-I25 |
| `0ca4f44` | Release records: I35–I37, I25, V08 odd names, the fuzz campaign after the OOM | — | — |
| `7847d16` | Test: the MFT-record test prints what the report saw when it misses (ARM64 diagnosis) | — | E-X01 |
| `ec5d475` | Tests: Markdown drawn by WebKitGTK and WKWebView too | I25 | E-I25 |
| `b9c41eb` | exFAT held to its volume, FAT long names capped, NTFS compression units as NTFS writes them | I37 | E-I37 |
| `f93f919` | FTP: a stat asks for the file, not the whole folder; no second stat before reading | I38 | E-I38-I39 |
| `0ec94f1` | A damaged compressed size no longer makes an object per unit nothing describes | I37 | E-I37 |
| `2ba114e` | SFTP: a new upload keeps many requests in flight; the speed limit counts from each attempt | I39 | E-I38-I39 |
| `8b0dafd` | FileCat's own folders are its user's alone on Linux and macOS | I40 | issue record |
| `9347070` | Test: replay exFAT round 5326394 (the Mac's fuzz run) | I37 | E-I37 |
| `1dce2c2` | SFTP: an upload cut off on a slow link starts again when that is quicker | I39 | E-I38-I39 |
| `4c6b910` | SFTP connections no longer held to SSH.NET's small socket buffers; lab test of links on the server | I41 | E-I41 |
| `2a6f882` | Shell helper starts the Shell before it says it is ready; the operations test waits as long for every state | — | E-X01 (CI runs 36801387149, 36801942257) |
| `e399276` | A job still running is never shown as interrupted, even to another FileCat | I44 | E-I44 |
| `e527a86` | Uploads keep modified times on servers without MFMT and say when a server did not; downloads take the stated time | I43 | E-I43 |
| `f02063b` | Release records: I38–I44, I45 opened | — | — |
| `3f1b554` | SFTP: links are renamed and moved only where the server renames links themselves | I46 | E-V08-L2 |
| `111ebcd` | FTP: an upload the server will not continue starts again; a dropped session no longer stalls; listing times as far as stated | I45, I47 | E-V08-L2 |
| `b4d0f52` | Test: the MFT-record case reads the record again while the time change is not on disk yet (CI run 36806933971) | — | E-X01 |
| `f9e7cba` | Release records: V08 against ProFTPD; I45–I47 | — | — |
| `16b1e47` | Release records: the fuzz campaign's finished ranges | — | E-I28-C1 |
| `e72e3fc` | Moves delete a source only while its copy is in place and both are as they were copied | I48, I49 | E-DPI |
| `febb51d` | Release records: DPI review, I48, I49 | — | — |
| `99145cf` | Synchronize removes and replaces a target item only while it is as the comparison saw it | I50 | E-DPI |
| `d228632` | Test: the lab's cut-off upload is held while the server drops it | — | E-V08-L2 |
| `5a8d3c9` | Release records: the lab VM's network adapter hang; ProFTPD beside vsftpd; the Windows VM as a client | — | E-ENV-05 |
| `65a76f8` | Attributes: a link's read-only is never set through it on Linux and macOS | I51 | E-DPI |
| `859f8d7` | Release records: I50, I51; V08 from the Windows VM as a client; the Ubuntu fuzz runs after the reset | — | — |
| `9a0725b` | Release records: DPI P12 reviewed | — | E-DPI |
| `78a48ce` | Recovery: an entry left unmarked in a deleted FAT32 folder starts where it says; the Windows-made images check | I52 | E-V09-W1 |
| `efc128f` | Synchronize removes a folder only while all it holds is as compared | I50 | E-DPI |
| `7791bda` | Tests: three CI flakes (a hook that raced the copy's counting thread, a short wait, a silent failure) | — | E-X01 (CI runs 36814578106, 36815095515, 36813249067) |
| `4945151` | Release records: V09 on images Windows made; I50's folder check redone; CI flakes | — | — |
| `7a99f9d` | Hex editor: a patch is applied whole or not at all; Save As keeps no copy of a file written meanwhile | I53, I54 | E-DPI |
| `07e6833` | Release records: DPI P05 reviewed | — | — |
| `155bb3e` | Release records: the host's VM drive filled; the Windows VM reverted to its lent state; fuzz ranges re-run | — | E-ENV-05, E-I28-C1 |
| `cf92679` | Registry: a .reg file FileCat exported from one view is not imported into another | I55 | E-DPI |
| `dd1e326` | Release records: DPI P06 reviewed; P07 in part | — | — |
| `cc33b5d` | Release records: I22/I34 on FAT32 and exFAT destinations; the Windows VM's lent .NET runtime | — | E-I22-F1 |
| `783c1b4` | Test: a device that names another address is checked by where FileCat connects, not by time (CI run 36821096985) | — | E-X01 |
| `98bc539` | CI: the Windows ARM64 lane runs the Remote tests too (run 36822797898: 81 passed, 33 skipped, 0 failed) | — | E-A02 |
| `3c487e4` | Release records: the Mac's 7.1–8.1 M fuzz range; 8.1–9.1 M started | — | E-I28-C1 |
| `85d512d` | Test: the macOS page engine through twelve lifecycles, each title observed, nothing raised after disposal | I12 | issue record |
| `906f1e9` | Release records: I12's two historical failures covered | — | — |
| `f81c0b9` | Release records: step 4's first pass (E-R04) | — | E-R04 |
| `99a6ae4` | Themes: the other windows over the theme's backdrop | I31 | issue record |
| `98e718f` | Release records: I31 done | — | — |
| `4a4349f` | Linux icons: symbolic variants as the last fallback, drawn in the text color | I27 | issue record |
| `34c4b9d` | Private data: crash leftovers swept from the temp folder; the benchmark's shared-temp output made new | — | E-DPI (B06) |
| `27256f6` | Recovery: no scan of a disk FileCat writes to itself; `--data`; where writing goes told through links, backing files and shares served here | I09 | issue record, E-I09-T1 |
| `d6cdd34` | Tests: where writing goes on a real system, checked by hand | I09 | E-I09-T1 |
| `7418c04` | Recovery topology: an image written into is itself as well; folders under /dev are folders | I09 | E-I09-T1 |
| `0a52b7b` | Recovery topology, macOS: diskutil's answers kept for 20 seconds | I09 | E-I09-T1 |
| `200954f` | Test: a whole recovery session for a write trace (gated) | I09 | V09 trace |
| `f241897` | Recovery: the Shell's pictures and gpg held off from the moment a disk is chosen | I09 | issue record |
| `1df5a21` | Recovery topology, Windows: a VHD written into lies on itself and on its file's disk | I09 | V09 trace |
| `ee476f0` | FTP: data connections go to the server itself, whatever its PASV reply names | I56 | issue record |
| `a5c25d1` | Network discovery: no redirects from a device's metadata address | I57 | issue record |
| `b0b2329` | Test: damaged archives of every format refused or listed, never crashed on | — | E-B02-A1 |
| `1977e8e` | Test: the recovery session reads a recovered file as the viewer does, without drawing it headless | I09 | E-V09-T1 |
| `6bbbb21` | Test: the archive damage test runs alone (CI red at `b0b2329`) | — | E-B02-A1 |
| `deaf776` | Recovery: the question says FileCat changes nothing, and that Windows may write to a mounted drive | I09 | E-V09-T1 |
| `bc8e2af` | Page views, Windows: external schemes never handed to their programs | — | E-DPI (B11) |
| `5b786a9` | Apply command: cmd.exe with `/v:off` | — | E-DPI (B11) |
| `0ba8a65` | Release records: step 3's skip inventory of every lane | — | E-A02 |
| `2f35a6b` | Git badges, icon resources and program lookups touch no path before it is known to be local, and never search the current directory | I16 | issue record |
| `1cb395e` | Test: replacing an open file on a FAT32 or exFAT drive (gated) | I22, I34 | E-I22-F1 |
