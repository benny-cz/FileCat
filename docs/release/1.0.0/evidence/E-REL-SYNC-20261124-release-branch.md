# E-REL-SYNC-20261124 — `release/1.0` synced with main three times: at `84cca148`, `d0ebd1d3` and `dae7207c`

2026-11-24 (UTC). **Release-branch maintenance; no candidate, tag or publication.** The owner's standing instruction is to sync `release/1.0` with main once every open PR has merged. After PR #14 ([E-REL-SYNC-20261123](E-REL-SYNC-20261123-release-branch.md)), main merged PRs #13, #12 and #15 and the release records `a7ff2531`. No PR was then open, so the branch was synced through PR #16.

## First sync: main at `84cca148` (PR #16)

### Source and procedure

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

### Result

- **Tip:** `release/1.0` is at `10320a7f`, with parents `6b2404f3` and `98467c6b`.
- **Content:** its tree equals `84cca148`'s.

### Quick issue scan

Covering `48d3de08` to `84cca148`:
- No product source, workflows, packages or build configuration changed.
- No secrets, email addresses or new test skips were added.
- The four test files only change how long, or on which thread, tests wait.

## Second sync: main at `d0ebd1d3` (PR #18)

- **Source:** `d0ebd1d3`, the merge of [PR #17](https://github.com/benny-cz/FileCat/pull/17). The owner's rule applied again: PR #17 had merged and no PR was open. Main's run 45311249434 of `d0ebd1d3` passed.
- **What came in:** `0696551a` (PR #17: the indexed-session tests' waits) and `5b5af7ef`, the records of the first sync and of the Windows run of the final tree. Two test files and seven documents changed, with no product source. The quick scan found no secrets, email addresses or new skips.
- **Sync branch:** `sync/release-1.0-d0ebd1d31` was pushed at `7618806b`, with parents `d0ebd1d3` and `10320a7f`.
- **CI:** [PR run 45311340608](https://github.com/benny-cz/FileCat/actions/runs/45311340608) passed all 19 jobs that ran in its first attempt.
- **Merge:** [PR #18](https://github.com/benny-cz/FileCat/pull/18) was merged as `f9f8f1cb` at 2026-11-24T03:02:47Z, pinned to the tested head; the branch was deleted. `release/1.0`'s tree then equalled `d0ebd1d3`'s.

## Third sync: main at `dae7207c` (PR #20)

- **Source:** `dae7207c`, main's tip. It holds the records of I633 and of the second sync, on top of `58ac8453`, the merge of [PR #19](https://github.com/benny-cz/FileCat/pull/19). PR #19 had merged and no PR was open. Main's run 45322746301 of `58ac8453` passed.
- **What came in:**
  - `bfefc469` (PR #19, [I633](E-I633-interrupted-list-focus-choice.md)): `OperationsView.axaml`, `OperationsView.axaml.cs` and `OperationsDrawerLayoutTests.cs`. The interrupted operation that has the keyboard becomes the chosen one.
  - `dae7207c`: the records.

  No workflows, packages or build configuration changed, and the quick scan found no secrets, email addresses or new skips.
- **Sync branch:** `sync/release-1.0-dae7207c7` was pushed at `ea2f58cd`, with parents `dae7207c` and `f9f8f1cb`.
- **CI:** [PR run 45323047164](https://github.com/benny-cz/FileCat/actions/runs/45323047164) passed all 19 jobs that ran in its first attempt.
- **Merge:** [PR #20](https://github.com/benny-cz/FileCat/pull/20) was merged as `8f122365` at 2026-11-24T04:47:37Z, pinned to the tested head; the branch was deleted.
- **Result:** `release/1.0` is at `8f122365`, with parents `f9f8f1cb` and `ea2f58cd`. Its tree equals `dae7207c`'s, which was main's tip.

## Test failures seen on the way

All were on hosted runners, in jobs that are not required, in tests that the changes being checked did not touch, and each passed in a later run of the same test code.

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
- **`OwnerColumnTests.Unavailable_and_gone_inspection_reasons_are_cached_and_sanitized(outcome: Gone, …)`** (PR #17's first attempt, run 45302608520; macOS App tests, remainder).
  - After the owner column had loaded, inspecting and hovering added 11 provider references (22 to 33), about one per visible row, where the test expects none.
  - Nothing in the app acts on the Gone observation, so a refresh it triggers is ruled out. Not investigated further.
- **`PlaceBarHierarchyTests.Compact_panel_header_uses_available_width_and_keeps_overflow_places_reachable`** (PR #19's first attempt, run 45315256109; macOS App tests, remainder).
  - After the window was widened, a pinned place button's right edge lay past the row (line 127).
  - The test was already made to finish its layout before that check because of layout timing.

## Not shown

- The records of the third sync, which stay on main until a later sync.
- The causes of the tooltip, owner-column and place-bar failures, and of the hosted macOS failures recorded in E-REL-SYNC-20261123.
