# E-I678 — The real file-system record test gave real I/O the scripted records' 5 seconds

2026-11-25. Found by main's CI [run 45676863831](https://github.com/benny-cz/FileCat/actions/runs/45676863831) on `4ffe68ba`, the merge of PR #60. Main's code there equals PR #60's tested head `8679dd50`, except release records.

## Finding

- **Failure:** `FileSystemRecordViewTests.The_real_record_shows_the_items_times_checks_stream_values_and_rights_notice_and_copies_when_bound` failed on both Windows lanes, after 7.5 s on x64 and 8.0 s on ARM64. The condition "the real record is published" was not reached within the shared `FileDetailsWindowTests.Until` helper's 500 rounds of 10 ms.
- **Other jobs:** every other test in those jobs passed. The macOS App focus job of the run was cancelled for want of a runner.
- **The same test elsewhere:**

  | Where | Durations |
  |---|---|
  | Hosted runners, x64, green runs (PRs #57, #59, #60, #61, #62 and rc.4's tag) | 1.9 to 4.9 s |
  | PR #63's run, the same hour as main's | 7.1 s on x64, 8.8 s on ARM64 |
  | PR #61's run, ARM64 | 7.6 s |
  | A Windows 11 x64 host, medium integrity, three runs | 0.85 to 0.90 s |

## Cause

The test's wait, not the product. The record is real I/O: `WindowsFileRecords` runs in-process, and its privileged parts are skipped at medium integrity. On the hosted runners it takes many times a desk machine's time. The shared helper's rounds were sized for the scripted records the other tests use, so this test passed or failed on the margin.

## Fix

[PR #64](https://github.com/benny-cz/FileCat/pull/64) (`fix/i678-real-record-wait`).
- That one wait now runs by the clock for up to 60 s.
- If it gives up, the failure says what the window shows.
- The shared helper keeps its 5 s for the scripted records.
- No product change.

## Tests

`FileSystemRecordViewTests` and `FileDetailsWindowTests` (21 tests, 4 environment skips) passed three times in a row at medium integrity on Windows x64.

## Not covered

- **Why the read is slower on the runners:** a hosted runner's system volume is large and busy. No product user sees a stall: the read runs off the window's thread on request.
