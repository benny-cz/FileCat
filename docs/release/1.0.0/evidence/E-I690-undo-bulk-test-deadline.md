# E-I690 — A 70-step undo test ran out of its 60 s while a contended runner ran the undo tests 20 to 100 times slower

**Found:** 2026-11-27, by the rc.10 sync's [run 46016568801](https://github.com/benny-cz/FileCat/actions/runs/46016568801) ([PR #80](https://github.com/benny-cz/FileCat/pull/80), Windows x64 Core tests: 9,709 passed, 1 failed, 2,901 skipped). **Severity:** Low: CI qualification, no product defect. **Disposition:** fixed on main ([PR #81](https://github.com/benny-cz/FileCat/pull/81)) before the sync, so rc.10 carries the fix. Nothing shipped changes.

## What failed

`UndoLifecycleTests.A_late_failure_and_guarded_keep_are_counted_and_retained_ahead_of_successes_with_full_paths` failed with "Undo: Original operation remained Running after 00:01:00.0028682; done=35, failed=1, skipped=1". The test undoes 70 moves, one of which fails and one of which was changed and is kept, and it allowed the inverse 60 s.

## Why

- **The inverse was slow, not stuck.** After 60 s it had finished 37 of its 70 items: 35 moved back, plus the failure and the kept item. It was still going.
- **The whole undo class was slow on that runner.** The run's test results:

  | Test | This run | rc.9's tagged run and PR #79's run |
  |---|---|---|
  | This test (70 steps) | over 65 s | 0.6–0.7 s |
  | `An_admitted_inverse_is_active_once_and_survives_removing_the_original_history` | 14.8 s | 0.1 s |
  | `Canceling_during_batch_staging_rolls_back_and_retains_failed_rollback_evidence` | 12.7 s | 0.1 s |

  Other tests in the same minutes also ran several times slower than usual.
- **Each undo step writes to disk several times:** it checks both paths, writes a journal intent, moves the item and records the outcome. A runner whose disk is busy with other tests slows every step.
- **Locally:** the class passes 16 of 16 at standard rights in 1.7 s.

## The fix

[PR #81](https://github.com/benny-cz/FileCat/pull/81) (`83409298`, merged into main as `3f645f6e`) lets the test wait five minutes instead of one. The bound only guards against a hang. The test's assertions (70 outcomes: 68 restored, 1 failed, 1 kept, and the report's order) are unchanged. The rc.10 sync merges main again, and its full-matrix run covers the change.
