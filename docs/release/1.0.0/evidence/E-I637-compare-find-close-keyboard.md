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

## Finite check and stop

On the candidate that carries a fix, in Compare of the same two files:
- When it opens, Down moves the current row.
- After a click on a row, then Ctrl+F, typing, and Escape (and again with Close Find), the row has the keyboard and Down moves it.

Stop when both hold. If the owner accepts the behavior for 1.0.0 instead, record the acceptance and stop.
