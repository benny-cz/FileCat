# E-REL-SYNC-20261124 — `release/1.0` synced with main at `84cca148`

2026-11-24 (UTC). **Release-branch maintenance; no candidate, tag or publication.** The owner's standing instruction is to sync `release/1.0` with main once every open PR has merged. After PR #14 ([E-REL-SYNC-20261123](E-REL-SYNC-20261123-release-branch.md)), main merged PRs #13, #12 and #15 and the release records `a7ff2531`. No PR was then open, so the branch was synced through PR #16.

## Source and procedure

- **Source:** `84cca148`, main's tip and the merge of PR #15. Main's [run 45297757429](https://github.com/benny-cz/FileCat/actions/runs/45297757429) passed.
- **What came in (4 commits and their merges):**
  - `dd626a5f` (PR #12) and `22e5f146` (PR #13): the analysis rendering tests wait 30 s for an indexed comparison and 20 s by default.
  - `cb06d20f` (PR #15): the Esc review test waits for the dialog to take the keyboard, and the 64 MiB Linux proxy test runs on its own thread under 120 s.
  - `a7ff2531`: the release records.

  Four test files and ten documents changed, with no product source.
- **Sync branch:** `sync/release-1.0-84cca148b` was pushed at `98467c6b`, with parents `84cca148` and `6b2404f3` and `84cca148`'s tree.
- **CI:** [PR run 45298117183](https://github.com/benny-cz/FileCat/actions/runs/45298117183).
  - The first attempt passed all five required checks, but two non-required jobs failed one test each (below).
  - Re-running those two jobs on the same commit passed both.
- **Merge:** [PR #16](https://github.com/benny-cz/FileCat/pull/16) was merged as `10320a7f` at 2026-11-24T01:27:13Z. The merge was pinned to the tested head, and the branch was deleted.

## Result

- **Tip:** `release/1.0` is at `10320a7f`, with parents `6b2404f3` and `98467c6b`.
- **Content:** its tree equals `84cca148`'s.

## Quick issue scan

Covering `48d3de08` to `84cca148`:
- No product source, workflows, packages or build configuration changed.
- No secrets, email addresses or new test skips were added.
- The four test files only change how long, or on which thread, tests wait.

## Test failures seen on the way

All were on hosted runners, in jobs that are not required, in tests this sync did not change, and each passed in a later run of the same test code.

- **`ColumnHeaderHelpTests.Headers_explain_their_columns_and_a_header_with_nothing_to_add_has_no_tooltip`** (PR #16's first attempt; Windows ARM64 App tests, remainder).
  - After 77 ms, the headless hover over the fourth header, Reparse type, read no tooltip. The first three headers had shown theirs.
  - Not investigated further.
- **`AnalysisIndexedSessionTests.PreviewPollingDuringIndexingCannotOvertakeTheDatasetRevision`** (PR #16's first attempt; macOS Core tests).
  - Its 4-second wait for the indexing's first write ran out after 4.76 s, before the ordering it checks was exercised.
  - [PR #17](https://github.com/benny-cz/FileCat/pull/17) gives the class's waits for that write 30 s. The stop that must not wait for a held write now runs on its own thread.
  - The class passed 10 of 10 runs at medium integrity on the development host.
- **`ChangeMonitorCadenceTests.A_busy_folder_is_read_again_at_the_churn_interval_until_it_calms`** (main's run 45288995088 of `7ba3aa00`; Windows ARM64 Core tests).
  - After six seconds of a file written every 400 ms, the monitor no longer counted the folder as busy. Its rereads' callbacks ran at 0.28, 0.97, 1.51, 2.72, 3.71, 4.86 and 5.58 s.
  - The monitor counts a folder as calm after 1.5 s without a change notice, so the writes or their notices were held up for that long at least once. That is runner scheduling, not a cadence fault.
  - The same tree passed the job in PR #16's run, and the Windows VM's native runs passed the class.

## Not shown

- Main's commits after `84cca148`: PR #17 and the records of this sync. They follow in a later sync.
- The causes of the tooltip failure, and of the hosted macOS failures recorded in E-REL-SYNC-20261123.
