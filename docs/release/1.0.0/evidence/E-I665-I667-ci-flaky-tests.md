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
