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
