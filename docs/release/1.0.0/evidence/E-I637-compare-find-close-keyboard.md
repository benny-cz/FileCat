# I637 — Compare gives the keyboard to its rows neither when it opens nor when Find closes

**Found:** 2026-11-24, during final qualification of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (checklist c6, I569), on the installed candidate in the Windows 11 25H2 qualification VM ([FQ progress](E-FQ-RC2-20261124.md)). **Status:** open, awaiting the owner's decision.

## Observation

Compare of two 300-line text files that differ in two lines:

- **When Compare opens,** the window itself has the keyboard. Down ×3 and Page Down change nothing, and the first Down moves the keyboard to the toolbar's "First difference" button. F8 moves to the next difference.
- **After a click on a row,** the row has the keyboard ("Equal: left line 194, right line 194"). Down ×3 moves the current row to line 197. The rows work once they have the keyboard.
- **After Find closes,** whether by Escape or by Close Find, the window has the keyboard again, even when a row had it before Ctrl+F. In one run it was on "First difference". Down changes nothing; F8 still moves between differences. The row closes and Ctrl+F brings back the query, as I569 says.

In the Viewer and the Hex Editor, closing Find returns the keyboard to the text, the page or the bytes.

## Cause

Rc.2's source, `CompareWindow.cs`:
- **The code gives the keyboard to the rows' list.** Opening Compare calls `_rows.Focus()`, with the comment that the first view "takes the keys (arrows and Page Down scroll)". So do the Close Find handler and the Escape branch of `OnKey`.
- **The list itself cannot take the keyboard.** `_rows` is a `ListBox`, and UI Automation reports it as not keyboard-focusable. Its rows (`ListBoxItem`) can take the keyboard. So the call does nothing.
- **I569's focused test does not check where the keyboard goes.** `CompareTextInteractionTests` checks that the Find row closes and that the query is kept.

## Consequence

Low. Nothing changes, and F8, Shift+F8 and Alt+Up/Down move between differences wherever the keyboard is. But keyboard and screen-reader users:
- cannot scroll the comparison with arrows or Page Down when it opens;
- lose their row after a search.

Arrows do nothing until a row has the keyboard, which a click on a row gives it. I569's acceptance says "Closing returns focus to the content", and for Compare that does not hold.

## Remedy (proposed) and its cost

Give the keyboard to the current row's container instead of the list. Use the selected index's `ContainerFromIndex`, or the list helper the panels use. Do this in all three places. Add the check to `CompareTextInteractionTests`. This changes product source, so it reaches users only in a candidate after rc.2.

## Fix

At source, on branch `fix/i637-compare-keyboard`, for the candidate after rc.2. Changes in `CompareWindow.cs`:
- **The current row takes the keyboard.** Opening, Close Find and Escape in Find now give it to the current row through the list helper the panels use, `ListKeys.Focus`. The row is scrolled into view first when needed. With no current row (identical files), the first row takes it. The byte view still takes it with `_hex.Focus()`, and an open dialog keeps it.
- **Activation.** The first view took the keyboard only if the window was already active. So a comparison shown before its window became active opened with no control having the keyboard. That happens when another window was active, or when the activation came after the comparison. Such a view now takes the keyboard on activation, unless a control in the window already has it. The headless tests met this case: there a small comparison could finish before the window's activation arrived.

Tests in `CompareTextInteractionTests`, on I637's 300-line case where they need files:
- **`Opening_gives_the_keyboard_to_the_current_row`.** The keyboard is on row 99 (line 100, the first difference). Down moves the current row and the keyboard to row 100. Page Down scrolls the rows down and leaves both at row 100. Down then moves both to row 101 and shows it again.
- **`A_comparison_shown_before_activation_gives_the_keyboard_to_the_current_row_on_activation`.** The window is shown without activation. When it is activated, row 99 takes the keyboard.
- **`Activation_leaves_the_keyboard_with_a_control_that_has_it`.** A checkbox that has the keyboard keeps it through activation.
- **`Closing_Find_gives_the_keyboard_back_to_the_current_row`.** A click on a row's line number, then Ctrl+F, typing and Escape: the row has the keyboard, and Down moves it. Then Ctrl+F, F3 to line 150, and a click on Close Find: line 150's row has the keyboard, and Down moves it.
- **`Without_a_current_row_the_first_row_takes_the_keyboard`.** Identical files: the first row has the keyboard, and Down moves to the second.
- **`Closing_Find_keeps_the_match_of_a_tall_wrapped_row_in_view`.** With Wrap long lines on, a match near the end of a row taller than the view stays in view after Escape, and the row has the keyboard.
- **`Find_reaches_offscreen_match_and_reports_scope_and_navigation_separately`** (I569's test) now also checks the keyboard after Close Find and after Escape.

Results on the Windows host, at medium integrity:
- **Before the change:** 6 of the class's 13 tests failed. They were the five new tests that need the change, and I569's extended test. Each failed because no row had the keyboard.
- **After the change:** 13 of 13 passed, in each of six runs.
- **Wider run:** the 15 test classes that open comparisons passed, 147 of 147. The same classes passed 141 of 141 before the change, without the new tests.

Limits:
- No installed candidate has been checked. The finite check below still applies.
- On Linux and macOS, only hosted CI runs these tests, headless. How late real desktops there activate a new window was not observed.
- Screen readers were not checked with the change.
- Page Down scrolls the rows but moves neither the current row nor the keyboard. Down then moves on from that row and brings the next one into view.

## Finite check and stop

On the candidate that carries a fix, in Compare of the same two files:
- When it opens, Down moves the current row.
- After a click on a row, then Ctrl+F, typing, and Escape (and again with Close Find), the row has the keyboard and Down moves it.

Stop when both hold. If the owner accepts the behavior for 1.0.0 instead, record the acceptance and stop.
