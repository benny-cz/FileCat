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
| E-ENV-00…04 | Host, Windows Sandbox failure, lent VMs, CI installer-compiler provenance, GitHub state | — | — | See record | Preflight facts | [E-ENV](evidence/E-ENV-environments.md) | I01, I03, I11, I18 |

## Commits made by the campaign

| Commit | Change | Issues | Evidence |
|---|---|---|---|
| `f87ad32` | Interrupted-copy recovery deletes only provably cut-short copies, names each, re-checks before deleting | I19 | E-I19-R1, E-I19-V1 |
| `5b061cc` | Installer: no recursive deletion of the installation folder on uninstall | I15 | E-I15-S1, E-I15-V1 |
| `45efc09` | `$LogFile` attribution by the item's own sequence number; waits for the on-disk log | I20 | E-I20-R1, E-I20-V1 |
