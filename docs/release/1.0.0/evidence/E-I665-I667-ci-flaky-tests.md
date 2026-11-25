# I665, I666, I667 — Three tests that fail intermittently on hosted runners

**Found:** each is listed in [the release-branch sync record](E-REL-SYNC-20261124-release-branch.md#test-failures-seen-on-the-way), and I665 and I666 failed again in rc.3's integration runs on 2026-11-24. That record left their causes uninvestigated. **Status:** open; to be fixed for rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate). Each failed in code that the change under test did not touch, and passed when run again. An intermittent failure can hide a real one in the same job, as I661 shows: the fourth test on that record's list turned out to be a product fault.

## I665 — `ColumnHeaderHelpTests`: a header's tooltip reads as none

| When | Test | Job |
|---|---|---|
| PR #16, first attempt | `Headers_explain_their_columns_and_a_header_with_nothing_to_add_has_no_tooltip` | Windows ARM64 App (remainder) |
| integration 2, run 45445858172 | `Header_tooltips_and_the_quiet_none_follow_every_theme` | macOS App (remainder) |
| integration 3, run 45476564759 | `Headers_explain_their_columns_and_a_header_with_nothing_to_add_has_no_tooltip` | Windows x64 App (remainder) |

The headless hover over the Reparse type header reads `ToolTip.GetTip(list)` as null where the header's help is expected. The header help itself does not depend on the platform. The test's `HeaderTip` reads the columns' positions from a private field and moves the pointer twice, then runs the dispatcher once, so a layout or hit-test pass still pending can leave the pointer on no header. On the physical Mac the class passed 20 of 20 runs.

## I666 — `OwnerColumnTests.Unavailable_and_gone_inspection_reasons_are_cached_and_sanitized`: hovering adds provider references

| When | Case | Job |
|---|---|---|
| PR #17, first attempt (run 45302608520) | Gone | macOS App (remainder) |
| integration 3, run 45476564759 | Denied | ubuntu-24.04 App |

Both times, after the owner column had loaded, inspecting and hovering added 11 provider references (22 to 33), about one per visible row, where the test expects none. Drawing the list's metadata columns asks the provider for each visible row's item (`TabViewModel.ItemForDrawing` since I649), so a frame drawn between the test's count and its check would add one reference per visible row. The test takes its count before the view has settled.

## I667 — `ChangeMonitorCadenceTests.A_busy_folder_is_read_again_at_the_churn_interval_until_it_calms`

Seen once, in main's run 45288995088 of `7ba3aa00`, on the Windows ARM64 Core tests. After six seconds of a file written every 400 ms, the monitor no longer counted the folder as busy. Its rereads ran at 0.28, 0.97, 1.51, 2.72, 3.71, 4.86 and 5.58 s. The monitor counts a folder as calm after 1.5 s without a change notice, so the writes or their notices were held up that long at least once. That is runner scheduling. The test judges the product's cadence by wall-clock callback times, which a loaded runner stretches.

## Remedy (proposed)

Make each test judge what it means to, not the runner's timing:
- **I665:** wait for layout and hit testing to settle before hovering, and poll the tip within a bound, rather than reading it once.
- **I666:** take the counts after the view has settled (idle frames drawn), and count only what inspection and hovering add.
- **I667:** judge the cadence on the monitor's own timestamps, through an internal hook, rather than on callback times.

For each, find out whether a product fault lies underneath, as I661 did.

## Fix — I665

Branch `fix/i665-i667-flaky-tests`. Two product faults lay underneath; each was reproduced headless, on the Windows workstation at medium integrity.

**Cause.**
- **The tooltip covered the pointer, and the list took that for the pointer leaving.**
  - Avalonia's tooltip service is shared by all windows of a process. It shows a tooltip at once, without its delay, when another closed less than 100 ms before (`BetweenShowDelay`); a tooltip that the test before closed is enough.
  - The list's tooltip was then open as the pointer moved along the header row, and took each header's help. Reparse type's, the tallest, did not fit below the pointer in the fixture's 420-pixel window and was moved up over it.
  - The next frame found the pointer over the tooltip. The list counted that as the pointer leaving it (`OnPointerExited`) and withdrew its tip, which also closed the tooltip.
  - A diagnostic test (not committed) opened and closed a tooltip in another window just before the hover. With rc.3's list, the headers read Name, Attr, Hard links, then none, in 6 of 6 rounds: CI's failure.
  - In the app, by the same path, a header's help with no room beside the pointer (on a short screen) would close as soon as it was shown; not seen live.
- **Values arriving for the rows withdrew a header's help.**
  - A tab that becomes active asks to show its values again (`ExpireRefreshable`). About 120 ms later the metadata service says that values arrived, and the list withdrew whatever tip it had, a header's too.
  - With only the first fault fixed, the same reproduction still read none for Reparse type in 4 of 12 rounds; the round logged with call stacks showed that notice withdrawing the tip.
  - In the app, a header's help closed whenever values arrived, so it stayed closed while a folder's values were being read.
  - The two CI failures of `Headers_explain_…` lasted 74 and 77 ms, too short for that notice; the macOS failure of `Header_tooltips_…` lasted 208 ms, so either fault fits it.

**Change.**
- `FileListControl.OnPointerExited` keeps the tip while the pointer is still within the list: it is over something shown above the list, such as the list's own tooltip.
- Row changes (values, rows, Git states, vertical scrolling) withdraw a row's tip only. A header's help is withdrawn when the columns, their widths, the font, the date format or the horizontal scroll change (`WithdrawTip`).
- The tests' `HoverHeader` turns the tooltip service off on the list. So no tooltip opens over the header row, whatever another window did.
- Two new tests, each failing with the old code:
  - `A_header_tooltip_shown_over_the_pointer_keeps_its_help`: "The tooltip closed once it was shown."
  - `Values_arriving_for_the_rows_keep_a_headers_help_and_withdraw_a_rows_tip`: the header's help was withdrawn.

**Results.**
- With both fixes, the reproduction read all four headers in 6 of 6 rounds, with the tooltip service on or off.
- `ColumnHeaderHelpTests` (7 tests), pinned to two cores shared with two CPU-busy loops at below-normal priority: 30 of 30 runs passed.
- The whole App suite in Release at medium integrity: 4,166 tests (11 of them the uncommitted diagnostics), none failed, 331 skipped.
- Hosted run 45496536945, from a throwaway branch (since deleted) that ran only these classes: windows-11-arm 15 of 15 runs, ubuntu-24.04 15 of 15, macos-26 5 of 5. Its reproductions with the fixed code read all four headers on each.
- The Mac (M1, macOS 27.2.1, four CPU-busy loops at the tests' priority) and the Ubuntu 24.04.5 VM (six loops): 30 of 30 runs each, and the same reproductions read all four headers.

## Fix — I666

**Cause: a frame drawn during the hover, not inspection or hovering.**
- `ShowOwner` waits for the focused row's value only. The other visible rows' values are read moments later, and the metadata service announces new values at most every 120 ms, so their notice can come after the test's count.
- The list then draws again, during `HoverOwner`: `MouseMove` runs pending work and draws. Each frame asks the provider for each drawn row's item (`TabViewModel.ItemForDrawing`, I649): 11 rows, 11 references.
- Reproduced with a diagnostic copy (not committed) that counts as the test did: on the Windows workstation under CPU load (Gone, 33 to 44 references, no owner read), and on the Ubuntu VM (Denied, 22 to 33, CI's numbers). Each time the notice came as the count was taken, and the frame during the hover.

**Product question.** Drawing asks for each drawn row's item in every frame, whatever value is cached. The cache is keyed by the item's path, and the item reference is what gives the path, for a Denied or Gone value as for any other. For file-system items the reference is computed and does no I/O. The extra frame read no owner again and resolved no name. This is not a fault. Keeping the references from one frame to the next would save them, but nothing reads more.

**Change.**
- `OwnerFixture.Settle` waits until every drawn row has its value and names are idle. It then joins the metadata service's notice still to come, or sends one, and draws the frame it asks for.
- The three tests that count references across a hover call it before counting: `Unavailable_and_gone_…`, `Plain_resolved_name_…` and `Unnamed_identity_and_last_known_value_…`.
- They still check that inspecting and hovering add no provider reference, no owner read and no name lookup.

**Results.**
- The diagnostic copy, counting as the old test did and after `Settle`:
  - Windows workstation, pinned to two cores shared with two CPU-busy loops: the old count failed 2 of 400 rounds (Gone, 33 to 44 each time); the settled count passed 300 of 300.
  - Ubuntu VM: the old count failed once in 50 rounds, as CI did there (Denied, 22 to 33; the notice came 1.3 ms after the count); the settled count passed 50 of 50.
  - The Mac and each hosted runner: 50 of 50 rounds either way.
- The three tests (6 cases), pinned to two cores shared with two CPU-busy loops: 40 of 40 runs passed.
- Hosted run 45496536945: windows-11-arm 15 of 15 runs, ubuntu-24.04 15 of 15, macos-26 5 of 5.
- The Mac (four CPU-busy loops) and the Ubuntu VM (six loops): 30 of 30 runs each.

## Fix — I667

**Cause: the runner, as recorded; and a product fault beside it.**
- The folder counted as calm because for 1.5 s the monitor got no change notice: the writes or their notices were held up.
  - Holding one notice for 2 s, about 3 s in, makes the old test fail the same way: rereads at 0.25, 0.67, 1.08, 2.29, 3.29, 5.52 and 5.93 s, against CI's 0.28, 0.97, 1.51, 2.72, 3.71, 4.86 and 5.58 s.
- **Product fault: a reread armed before the last one could still be taken early.**
  - A .NET timer does not withdraw a callback it has already queued when it is armed again.
  - When a change was noticed while such a callback waited, the callback read the folder at once, before the interval since the last reread had passed. A busy folder could then be read sooner than every `ChurnInterval`, and a large folder sooner than its `MinInterval`.
  - `A_reread_armed_before_the_last_one_waits_for_its_interval` forces that order with held callbacks. With the old `Fire`, the two rereads were taken 0.259 s apart with a 2-second interval.

**Change.**
- `ChangeMonitor.Fire` checks under its lock that the interval since the last reread has passed, less 15 ms for a timer falling due early. If not, it arms the timer for when the reread falls due.
- New internal hooks beside `Fired`: `Noticed` gives the time of each notice and when its reread falls due; `Turned` gives the time the folder turned busy or calm.
- `A_busy_folder_is_read_again_at_the_churn_interval_until_it_calms` writes for six seconds at least, and on, up to 25 s, until the folder counts as busy and two rereads have been taken while it did. It judges on the monitor's own times:
  - busy from the third reread within the window;
  - while busy, rereads at least the interval less one 15.6 ms Windows tick apart;
  - calm only after 1.5 s without a notice;
  - `ChurnChanged` once per turn;
  - the change after the quiet spell due within `MinInterval` of it.
- Mutations it catches: without the churn interval, rereads 0.40 s apart; calm on the timer regardless of notices, "calm 0.123 s after the last change".

**Results.**
- With the 2-second hold, on the Windows workstation under four CPU-busy loops: the old test failed in 10 of 10 runs, and the new one passed in 10 of 10. The Mac, the Ubuntu VM and each runner of hosted run 45496536945 gave the same, once each. Without the hold, both passed 30 of 30 runs under the same loops.
- `ChangeMonitorCadenceTests` (6 tests), pinned to two cores shared with four CPU-busy loops: 29 of 30 runs passed, and the busy-folder test 30 of 30.
  - The one failure was `A_change_noticed_as_a_reread_falls_due_is_not_read_again_on_its_own`, which this branch leaves as it was: a second reread 1.5 s after the first ("rereads: 0.51, 2.05").
  - That needs a change notice the test did not make; the new check in `Fire` can only delay a reread. Under the same load, 60 more runs of the test and 60 of a copy that logs the watcher's own notices all passed, and no such notice came. Not investigated further.
- `ChangeMonitorCadenceTests` and `ChangeMonitorOverflowTests` in Release: 9 of 9 passed.
- The Mac (M1, macOS 27.2.1, four CPU-busy loops at the tests' priority) and the Ubuntu 24.04.5 VM (six loops): 30 of 30 runs each.
- Hosted run 45496536945: windows-11-arm 15 of 15 runs, ubuntu-24.04 15 of 15, macos-26 5 of 5.
