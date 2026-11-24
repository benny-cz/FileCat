# I635 — Alt+Enter shows the cursor item's properties while several items are marked

**Found:** 2026-11-24, during final qualification of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (checklist c17, I601), on the installed candidate in the Windows 11 25H2 qualification VM ([FQ progress](E-FQ-RC2-20261124.md)). **Status:** open, awaiting the owner's decision.

## Observation

In a folder with two marked files, `archive.txt` and `hidden.txt`:

- **Right-click a marked file → Properties** (Windows' own menu): no sheet opens, and FileCat says **"Select one item to show its properties."**
- **Alt+Enter with the cursor on `archive.txt`:** opens Windows' "archive Properties" sheet, with no word about the other marked file.
- **Alt+Enter with the cursor on an unmarked file** (`plain.txt`): opens "plain Properties", with no word about the two marked files.

## Cause

The two routes choose their item differently, in rc.2's source:
- **Context menu:** `PanelView` takes the panel's selection, which is the marked items. For more than one, it shows the message above.
- **Alt+Enter:** the Properties command uses `PropertiesPathForFocus()`, which reads only the focused row and ignores marks.

## Consequence

Low. Nothing is changed or opened beyond a read-only system sheet, and the sheet's title names its item. But I601's stop rule asks that "multiple marks explain the one-item limit". Through Alt+Enter they do not. In a Commander layout, where marks normally decide what a command acts on, a user can take the sheet for one of the marked items.

## Remedy (proposed) and its cost

Give the Properties command the context menu's rule: with marks, act on the marked selection, and say "Select one item to show its properties." when it holds more than one. This changes product source, so it reaches users only in a candidate after rc.2. Fix it with any other change that needs a new candidate, or accept it for 1.0.0 as a known limitation.

## Finite check and stop

On the candidate that carries a fix: two marked files and Alt+Enter give the one-item message and open no sheet. One marked file with the cursor elsewhere opens that file's sheet. No marks opens the cursor item's sheet. Stop when all three hold. If the owner accepts the behavior instead, record the acceptance and stop.
