# I635 — Alt+Enter shows the cursor item's properties while several items are marked

**Found:** 2026-11-24, during final qualification of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (checklist c17, I601), on the installed candidate in the Windows 11 25H2 qualification VM ([FQ progress](E-FQ-RC2-20261124.md)). **Status:** remediated preliminarily for rc.3 on 2026-11-25 ([I635](../FILECAT_1_0_RELEASE_ISSUES.md#i635), merged into main as `a2f983e9a`); first recorded as open, awaiting the owner's decision.

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

## Fix

Branch `fix/i635-alt-enter-marks`, made from `main` at `565f7577`, whose product source is rc.2's (`7269ec61`). It changes product source, so it reaches users only in a candidate after rc.2.

- **One rule for both routes.** `MainViewModel.ShowPropertiesForSelection` decides for the Properties command and for Windows' context menu. More than one item opens nothing and says "Select one item to show its properties." One item shows its properties. No item says "Select a file, folder, or drive to show its properties.", as before.
- **The command now chooses as the context menu does.** The Properties command runs from Alt+Enter, from File → Properties and from FileCat's own item menu. With marks, it acts on the marked items, wherever the cursor is: one marked item shows its own properties, even with the cursor on another row. With no marks, it acts on the cursor item, as in rc.2: ".." still stands for the folder itself, and a This PC drive row for the drive's root.
- **Windows' context menu** (`PanelView`) gives the same helper the paths it was opened for. What it does is unchanged. A stand-in for that menu, used only by tests (`PanelView.NativeItemMenuForTests`, null in the product), lets headless tests choose its Properties.
- **macOS and Linux** have no system property sheet. There, Properties opens FileCat's file-system record for the chosen path ([I589](E-I589-macos-properties-fallback.md)). The command is the same on every system, and FileCat's own item menu runs it, so the marks rule holds there too.
- The command's description now reads "Shows the properties of the marked item (or the one under the cursor); with several marked, it asks you to select one."

**Tests.** New `tests/FileCat.App.Tests/PropertiesMarksTests.cs` has 5 headless tests, in a folder with `a.txt`, `b.txt` and `plain.txt`. A recording shell stands in for Windows' property sheet. On macOS and Linux, a counting reader reads the record. Nothing native opens.

1. Two marks: Alt+Enter (a key press, with the cursor on a marked file) and the command (with the cursor on an unmarked file, then on "..") each give the one-item message and show nothing. The marks stay.
2. One mark on `b.txt`, with the cursor on `a.txt`, then on "..": both show `b.txt`'s properties.
3. No marks: the cursor item `plain.txt`, then ".." shows the folder itself.
4. Windows' context menu (Windows only; a stand-in menu answers Properties): it is opened for the two marked files and gives the message, then for the one marked file and shows it, then with no marks for the cursor item and shows it.
5. FileCat's own item menu → Properties: two marks give the message, and one mark shows that item.

On Windows 11 (build 26320), run with the xUnit v3 runner at **medium integrity**: PropertiesMarksTests passed **5/5**. Related classes on the same build had no failures: DriveListTests 2/2, FileDetailsWindowTests 8 passed and 4 skipped of 12 (a macOS File Provider domain, and native Unix links), BackgroundContextMenuTests 20/20, FileDetailsFindScopeTests 4/4, ClipboardIntentTests 51 passed and 1 skipped of 52 (needs a case-sensitive file system), and KeyBarTests 5/5. To check that the tests see I635, rc.2's cursor-only rule was put back in the command for one run. Tests 1, 2 and 5 then failed: Alt+Enter with two marks gave no message, and one mark showed `a.txt` instead of `b.txt`. Tests 3 and 4 passed, as they should.

**Not verified here:** the new tests on macOS and Linux (the pull request's CI runs them), and any installed build.

**Still to do on the candidate that carries this fix:** the finite check above. Two marked files and Alt+Enter give the one-item message and open no sheet. One marked file with the cursor elsewhere opens that file's sheet. No marks opens the cursor item's sheet. Windows' context menu → Properties behaves as in rc.2.
