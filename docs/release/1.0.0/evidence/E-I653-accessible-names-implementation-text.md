# I653 — Accessible names expose implementation text: keyboard reference rows and spin buttons

**Found:** 2026-11-24 by the Mac GUI harness rehearsing [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md)'s b-rows (scenario `x01_internal_names`), and confirmed on Windows in the qualification VM (batch 30). The Mac harness and its results are kept locally in `.tmp/fq-rc2/mac/` (`results/rc2b/`). It is the class of I583. **Status:** open. A fix is proposed for the next candidate, and the owner decides.

## Observation

**Help → Keyboard reference rows.** Each row's accessible name is a record's text, not its visible title.
- **macOS (AX):** all 10 realized rows. For example, `KeyboardHelpEntry { Id = app.about, Title = About FileCat, Category = Application, Gestures = , Desc…`, shown on screen as "About FileCat".
- **Windows (UI Automation, installed rc.2):** the same, for example `KeyboardHelpEntry { Id = app.changeShortcut, Title = Change shortcut…, …`.

**Spin buttons.**
- **Where.** Settings → Appearance, for the two font sizes, and on macOS also Settings → Columns, for Width.
- **Names.** Their increase and decrease buttons are named `Avalonia.Controls.PathIcon` (`PART_IncreaseButton`, `PART_DecreaseButton`).
- **Windows.** The same four Appearance buttons have the same names (batch 30).

## Why it matters

A screen reader announces implementation text instead of "About FileCat" or "Increase". I583 fixed the same fault in command search by returning the command title from the entry's display string; the keyboard reference's entries have no such display.

## Remedy (proposed) and its cost

- **Keyboard reference.** Give each entry an accessible name, its visible title (and gesture), for example through the list item's automation name or the record's display string.
- **Spin buttons.** Name the increase and decrease buttons, for example "Increase font size" and "Decrease font size", or rely on a themed template that names them.
- **The rest of the product.** Check for other list items or templated buttons whose names fall back to type names.

This changes product source, so it reaches users only in a candidate after rc.2.
