# E-I686 — A change-monitor test read the busy state twice, and a runner's stall turned the folder calm between the reads

**Found:** 2026-11-26, by the `release/1.0` sync for rc.9 ([PR #76](https://github.com/benny-cz/FileCat/pull/76), run 45881038374, macOS Core tests: 9,920 passed, 1 failed). **Severity:** Low: CI qualification, no product defect. **Disposition:** fixed for rc.9 ([PR #77](https://github.com/benny-cz/FileCat/pull/77)).

## What failed

`ChangeMonitorCadenceTests.A_busy_folder_is_read_again_at_the_churn_interval_until_it_calms` failed after 8 s. The monitor's own record, in seconds from the first notice:
- **Notices:** at 0.000 and 0.040, then in pairs at 0.431, 0.895, 0.902, 1.437, 1.926, 2.501, 3.110, 3.379, 3.912, 4.631 and 5.271, then none.
- **Rereads:** at 0.323, 0.699, 1.425, 2.435, 3.871, 4.942 and 6.115.
- **Turns:** busy at 1.425, calm at 6.932.

## Why

The test writes a file every 400 ms until, after six seconds at least, the folder counts as busy and two rereads have been taken while it did (I667's fix). It then asserted the same condition again:
- The loop ends before 25 s only through its `break`, which needs `monitor.IsChurning`. The test ended after 8 s, so the loop saw the folder busy.
- The assertion on the next line read `IsChurning` a second time and found the folder calm.
- The runner had held the test's writes up: no change was noticed after 5.271 s. The monitor rightly turned the folder calm 1.66 s later (`CalmAfter` is 1.5 s), at 6.932 s, between the two reads.

**Not the product:**
- `ChangeMonitor` is unchanged since rc.8, and in this test its reread callback does nothing: no listing and no I/O scheduler is involved.
- The record passes every later check of the test: busy from the third reread within the window; calm 1.66 s after the last notice; and busy rereads 1.01 to 1.44 s apart.

## The fix

[PR #77](https://github.com/benny-cz/FileCat/pull/77) (`01bda801`, merged into main as `1d861029`): the test takes the busy state once, where the writing loop ends. A loop that never sees the folder busy, with two rereads taken apart, still fails after 25 s. Its full-matrix run 45885512784 passed, with this test on all four lanes; on the Windows x64 host it passes 3 of 3. The failure needs a runner stall at the end of the writing, which was not reproduced.
