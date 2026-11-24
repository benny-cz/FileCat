# I633 — The interrupted-operation list could show one operation's actions under another's keyboard focus

**Source:** `bfefc469` (PR #19), merged into main as `58ac8453` on 2026-11-24. **Disposition:** qualify for 1.0.0 (V17/V23/GATE-CI-FINAL); remediated preliminarily at source. The native Windows VM runs of the final release tree found it ([E-REL-NATIVE-20261123](E-REL-NATIVE-20261123-release-tree.md#second-round-the-final-tree)).

## Fault and user consequence

The Operations drawer's recovery area lists interrupted operations. Under the list it shows the chosen operation's details and its actions: Clean up partial files, Review report, Mark reviewed and Hide for now. The list chose a row only when its own arrow-key handling moved the selection and the keyboard together. Focus that reached a row any other way left the choice where it was:

- Tab into the list, or a row focused by code, as the new test below shows.
- On the Windows VM, Down itself. There, the full App suite failed `OperationsDrawerLayoutTests.Recovery_section_stays_bounded_and_last_item_actions_are_scrollable(count: 5)` in three full runs out of three. Instrumented, the run showed the selection and the keyboard parting as the list scrolled:

  | After | Chosen row | Row with the keyboard | Scroll offset (DIP) |
  |---|---|---|---|
  | start | 0 | 0 | 0 |
  | Down 1 | 1 | 1 | 0 |
  | Down 2 | 1 | 2 | 33 |
  | Down 3 | 3 | 3 | 70 |
  | Down 4 | 3 | 4 | 107 |
  | Down 5 | 3 | 4 | 107 |

The row with the keyboard is highlighted as focused, while the details and the action buttons belong to the chosen row. A user moving through the list with the keyboard could act on a different interrupted operation than the one they had moved to, for example by cleaning up the partial files of an operation they did not mean.

## Change

`OperationsView` makes a row that receives the keyboard the chosen one (`OnInterruptedChoiceFocused`, a bubbling `GotFocus` handler on the list, named `InterruptedChoices`). A row already chosen is left alone, and only rows still in the list are chosen.

## Tests

- **New: `OperationsDrawerLayoutTests.Focusing_an_interrupted_operation_chooses_it_however_the_focus_arrived`.** It puts the keyboard on the fourth of five rows by programmatic, Tab and directional focus. Before the change, all three cases left the first row chosen. After it, all three choose the fourth.
- **The keyboard test's failure message.** It now names the chosen row, the focused row and the list's scroll after each press.
- **Windows host, medium integrity:**
  - `OperationsDrawerLayoutTests`: 21 of 21 passed.
  - Full App suite: 3,986 tests, 0 failed, 286 skipped.
- **Windows VM, the full lane's settings:** full App suite 3,986 tests: 3,740 passed, 246 skipped, 0 failed. The keyboard test passed there with five rows, as did the new test's three cases. Evidence `fix-recoveryfocus-bfefc469-win`, SHA-256 prefix `e85ba905dac22cd4`.
- **Hosted CI:** [PR #19's run 45315256109](https://github.com/benny-cz/FileCat/actions/runs/45315256109) passed every job in its second attempt. The first attempt's one failure was `PlaceBarHierarchyTests` on macOS, unrelated; there the new test's three cases had already passed.

## Why the VM differed

On the development host the full suite passed with the same row geometry: 37 DIP rows, a 78 DIP list, and the same scroll offsets. Why Down moved the keyboard without the choice only in the VM's full-suite process was not found; it happened after many earlier App tests in the same process. The change makes the choice follow the keyboard however the keyboard arrives, which covers that case and the others above.

## Limits

- Other lists whose details follow their selection were not audited for the same split.
- The VM's instrumented run also failed `Stop_stays_in_view_when_the_recovery_area_changes_after_a_review_starts(revealedByContinue: False)` once. Stop was 26 DIP below a 45 DIP recovery viewport. It passed in the VM's run with this change, and its cause is not known.
- No installed candidate has been checked.

## Finite candidate check and stop

1. **Hosted CI.** At the candidate source, the App test jobs pass `OperationsDrawerLayoutTests`.
2. **Installed candidate.** Put three or more interrupted operations in the recovery list. With the keyboard alone (Tab into the list, then Up and Down), each row that has the keyboard is the one whose details and actions are shown.

Stop when both pass. Reopen only for:
- a change to the recovery list or its selection;
- a demonstrated split between the keyboard and the chosen operation.
