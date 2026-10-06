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
