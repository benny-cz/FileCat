# E-I153 — Ubuntu cadence assertion failure

Date 2026-10-06. Open required-CI defect; diagnosis in progress. V12/I87/I108.

Run 37412907260 attempt 1, exact source e3c99d5, Ubuntu job 112105093539 fails Core step 5 in ChangeMonitorCadenceTests.A_folder_that_keeps_changing_is_read_again_while_it_changes, line 44: no reread asked after the last change. Log reports writer times 0.04–6.10 seconds and callbacks 2.04, 4.04, 6.07 seconds. The fixture samples its last time after synchronous file-write return; callback contents/event timestamps are not captured. A callback can in principle see a change before the writer resumes, but that mechanism is not yet proved for this run. Production monitor and Core sources are unchanged by I152. Reproduce callback/write observation before deciding whether production or fixture needs correction; do not label flaky or erase the failure.

The initial log request failed while ARM64 was running and is retained. Completed-run log and failed-run seal verify three successful lanes, one failure, three expected tag-package skips, four artifact digests/five complete inventories. Ubuntu fails before App execution; its per-case Core inventory is unavailable, and no Linux App CI pass is inferred. Native I152 Ubuntu controls independently pass.

Private root `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\ci-37412907260-attempt1`.

| Retained path | SHA-256 |
|---|---|
| ubuntu-failed-job-log-v2.txt | bf409a7d7bfe703542354ffe00cb8b61ff7d010b8b4cb2cdd5d0dc8c31bfeed8 |
| independent-ci.json | 115e66f3647aa7e6d900123a07fa98df2c1df4d5035d2e4f215a779cc4d4e8da |

Status Open; controlled reproduction, remediation and exact-source CI revalidation remain. No candidate or stable GO.

## Controlled observer correction (2026-10-06 04:40 UTC)

The original timestamp oracle is unsuitable: an owned real-watcher probe using the unchanged production monitor deliberately holds the writer's observation after the final write. The callback has already observed the complete exact name/one-byte-length inventory, yet the original tail timestamp assertion rejects it. This proves that observer-order defect; it does not recover the scheduling history or callback contents of the original Ubuntu run.

The test-only correction captures each actual reread's sorted name/length snapshot, independently constructs the expected final inventory, and still requires at least two rereads during the six-second arrival period. A second case forces final-file observation before the delayed writer resumes. It waits at most five seconds for the final-file observation and retires in-flight callbacks before owned-folder cleanup. Production `ChangeMonitor` is unchanged.

Missing-final and missing-during synthetic observation controls are rejected. A separate exact-source copy deliberately removes the production two-second debounce cap: both unchanged corrected tests fail for zero rereads during churn. Restoring only `FileCat.Core.dll` makes both pass; all 861 final payload pins verify. This negative mutation is a control, not a production finding.

Host affected suite: three passes, zero skips (two cadence cases and the Windows overflow case). Full Core: 819 passes, 56 declared skips, 875 complete unique results. Native Windows/macOS/Ubuntu and exact committed-source CI revalidation remain pending; I153 remains Open until that evidence is sealed.

Private root `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\cadence-20261006`.

| Retained path | SHA-256 |
|---|---|
| independent-host-v1.json | 8079d4ad87165601317d9ec1f03aa941bf0f02ab92eee591c47e345284654cdf |
| negative-control/independent-negative-control-v1.json | 001bfc09f6abecdb775ab17c05d4e1085246b9cfca886d48b650c27d977218c7 |

## Exact committed native repeat

Source `fd1d780ad3732bf0940f1d5bd896831d7881f6ab` was exported from raw Git blobs, with every exported blob ID/size/SHA-256 verified and its source archive pinned. SDK-free native execution uses the actual published Core test program: Windows 26300 VM (three passes, zero skips, 331 payload pins); Ubuntu 26.04.1 VM (two passes, one explicit Windows-overflow skip, 331 pins); macOS 27.0.1 arm64 UID 501 (two passes, the same explicit skip, 330 pins). Both cadence cases pass in each environment. All input pins verify before/after, retained result pins and exact inventories independently match, and owned worker/temp cleanup verifies. The Mac runner's transient ordinary-user caffeinate ends with its command; no persistent power setting changes were introduced.

| Private path under cadence-20261006 | SHA-256 |
|---|---|
| clean-v1/producer.json | e3d325bc5744c3e7623a7f4a7158b3549c475def6582b1f799b456a269856403 |
| clean-v1/windows-executed/independent-guest-v1.json | 564af4816126579929cc6802b9a603e99124a9254b7f568c8f60f2b6142ddccb |
| clean-v1/linux-executed/independent-guest-v1.json | 0f55a4e1158f71ef085be641efd69dffc57bb562314ad2f47445ba5a511e606f |
| clean-v1/mac-executed/independent-native-v1.json | b6a06449a0125e26bfe35a7126e14721039ecdd5412dbf0de40901c5310232e5 |

Required run 37414852140 has passed the Windows/macOS/Ubuntu lanes, including Ubuntu Core; ARM64 package-start/installer completion and the full artifact seal are still pending at this update. Original failure remains retained. No native GUI or candidate qualification is inferred.
