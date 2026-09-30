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
| E-L01 | The S10 Windows lane run locally (Release) | `4f6b062` | Physical host, Windows 11 Insider 26220 | 0 failed: Core 500/32 skipped, Windows 87/15, Remote 38/5, App 154/4 | Preliminary automated | [E-L01](evidence/E-L01-local-run-4f6b062.md) | — |
| E-I19-R1 | Interrupted-copy cleanup deletes the wrong files | `4f6b062` | Physical host; git worktree at `4f6b062` | Defect reproduced: 4 Core tests fail; Run again deletes a user's edit | Preliminary automated | [E-I19](evidence/E-I19-interrupted-copy.md) | I19 |
| E-I19-V1 | The I19 fix | `f87ad32` | Physical host; CI 36754000317, 36756346845 | Targeted and full suites pass; CI green on Windows x64, Ubuntu, macOS (ARM64 red on I20), then all green | Preliminary automated | [E-I19](evidence/E-I19-interrupted-copy.md) | I19 |
| E-I20-R1 | `$LogFile` shows an earlier item's operations | `f87ad32` | CI ARM64 job 110019476573; physical host (elevated) | 1 CI failure; 3 of 15 local runs fail | Preliminary automated | [E-I20](evidence/E-I20-logfile-attribution.md) | I20 |
| E-I20-V1 | The I20 fix | `45efc09` | Physical host; CI 36756346845 | 15 of 15; full suite green; CI green on all four lanes | Preliminary automated | [E-I20](evidence/E-I20-logfile-attribution.md) | I20 |
| E-I15-S1 | Installer script and Inno Setup semantics | `4f6b062` | — | Recursive `{app}` deletion confirmed | Static | [E-I15](evidence/E-I15-uninstall-vm.md) | I15 |
| E-I15-V1 | Install and uninstall into a folder holding user files; default folder with one run | Scripts `4f6b062` / `5b061cc`; payload `f87ad32` | VMware VM, Windows 11 Insider 26300, Inno Setup 6.7.1 | Baseline deletes the user's files; fixed keeps them and still removes its own folder; FileCat writes nothing into its folder | Preliminary runtime | [E-I15](evidence/E-I15-uninstall-vm.md) | I15 |
| E-ENV-00…05 | Host, Windows Sandbox failure, lent VMs and Mac, CI installer-compiler provenance, GitHub state, test servers | — | — | See record | Preflight facts | [E-ENV](evidence/E-ENV-environments.md) | I01, I03, I11, I18 |
| E-S01 | Tests that passed by returning before asserting (step 3) | `552aa62` → `be6ca25`; scan of `5c54181` | Static | 28 such tests made explicit skips; none left outside the Windows-only project | Static | [E-S01](evidence/E-S01-early-returns.md) | — |
| E-X01 | Suites on the lent Windows 11 VM (unelevated), Ubuntu 22.04 VM (also inside its GNOME session), the owner's M1 Mac; CI runs of the campaign's commits | `be6ca25` … `d40e510` | See record | Found I21, I22, I23, I27, I28 and a Mac keychain test defect; after the fixes 0 failures | Preliminary automated | [E-X01](evidence/E-X01-cross-platform-runs.md) | I21, I22, I23, I27, I28 |
| E-I17-S1/R1 | Consent text hid steps after the 60th; HKU hives mislabeled | parent of `33b7de2` | Physical host | Reproduced by 2 new tests | Static + preliminary automated | [E-I17](evidence/E-I17-consent.md) | I17 |
| E-I17-V1…V3 | Consent fix; runtime checks of the installed helper | `33b7de2`, `5c54181` | Host; lent Windows 11 VM (UI Automation) | First runtime check found 2 defects in the fix; after `5c54181` all 130 steps shown, Cancel ran nothing | Preliminary runtime | [E-I17](evidence/E-I17-consent.md) | I17 |
| E-I21-R1/V1 | Registry explicit views without administrator rights | `be6ca25` → `47c27b9` | VM (unelevated); host (restricted token); CI | Reproduced; fixed; views match Windows' own listings | Preliminary automated | [E-I21](evidence/E-I21-registry-views.md) | I21 |
| E-I22-D1/M1/R1 | Replace refused while a file is open; comparison released files late | `be6ca25`, `552aa62`, `5c54181` | VM, CI ARM64, host probe | Mechanism established; 3 reproductions fail on the unchanged code | Preliminary automated | [E-I22](evidence/E-I22-replace-open-file.md) | I22 |
| E-I22-V1 | The I22 fix | `63d5fc4` | Host; unelevated VM; CI 36773433835 | New tests fail before, pass after; all suites green; 15 more App-suite runs green | Preliminary automated | [E-I22](evidence/E-I22-replace-open-file.md) | I22 |
| E-I23-D1/M1/R1 | Discovery lost a device's name when it came late | `5c54181` | Ubuntu VM | Mechanism established from source; not reproduced by in-guest load | Preliminary automated | [E-I23](evidence/E-I23-discovery-naming.md) | I23 |
| E-I23-V1 | The I23 fix | `d40e510` | Host worktree; Ubuntu VM; CI 36773433835 | Deterministic test fails before (the U2 message), passes after; suites and 20 discovery runs green | Preliminary automated | [E-I23](evidence/E-I23-discovery-naming.md) | I23 |
| E-I28-D1/R1/V1 | Fuzzing found a damaged NTFS size that made the whole volume unreadable | `5c54181` → `98fb594` | M1 Mac; host | Round 8842 fails before, passes after; Core suite green | Preliminary automated | [E-I28](evidence/E-I28-ntfs-fuzz.md) | I28 |
| E-I28-C1 | Fuzz campaign on the fixed build, millions of rounds over four machines | `98fb594`, `bb977d0` | Host, Ubuntu VM, Windows VM, Mac | Second NTFS finding (fixed `bb977d0`); running | Preliminary automated | [E-I28](evidence/E-I28-ntfs-fuzz.md) | I28 |
| E-I26-R1/V1 | Progress at 100% during verification; the new progress model and time left | `2197074` → `d40fda0` | Host; screenshot tool | 100% for 63% of a verified copy before; after: all suites green, honest range | Preliminary automated | [E-I26](evidence/E-I26-progress.md) | I26 |
| E-I29 | Shell preview request race (CI red on ARM64) | `2197074` → `7175a41` | CI ARM64; host | Reproduced deterministically; fixed; CI green | Preliminary automated | [E-I29](evidence/E-I29-shell-preview-race.md) | I29 |
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
