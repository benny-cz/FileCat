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

## Fix

Commit `11ee3842f` on branch `fix/i649-i653-i654`.

**Cause.** A list item or templated button without a name of its own is named after what it holds. A probe of Avalonia 12.1.4's own peers showed the rule:
- **A list item** takes its template root's name or text. An unnamed panel root, such as the keyboard reference's `Grid`, falls back to the item's `ToString()`. For a record, that lists its fields; for a class, it gives the type's name.
- **A button** holding a control other than text is named after that control's type: a `PathIcon` gives "Avalonia.Controls.PathIcon".

**What changed**
- **Keyboard reference.** `KeyboardHelpEntry.ToString()` returns the row's title and shortcut as shown, for example "Copy, F5" and "About FileCat, Unbound". This follows I583. It names the rows of Help → Keyboard reference and of the Change shortcut list.
- **Spin buttons.** One style in `Themes/Styles.axaml` names every `ButtonSpinner`'s `PART_IncreaseButton` "Increase" and `PART_DecreaseButton` "Decrease".
  - This covers every `NumericUpDown`: the two font sizes in Appearance, Verify automatically up to in Behavior, Width in Columns, the three limits in Analysis history, and the archive dialog's delta distance.
  - The box itself keeps the field's name ("List font size"), so the buttons need no copy of it.
- **Audit.** I swept the main window, its menus and twelve dialogs for names that are a type or a record. The dialogs were Settings (every page), Help, Change shortcut, Theme, Copy, Attributes, Bulk rename, Select by mask, Tab list, Folder history, Bookmarks and Review marks. I also read every list item template and every button that holds an icon or a panel. Three more were clear, and are fixed:
  - **View → Theme…**: each choice was named "Avalonia.Controls.StackPanel". It is now named after the theme, with the theme's description as its help text.
  - **Disk space, indexed analysis entries**: each row was named with the whole captured record, the volume's identity and time stamps included. A row is now named by its cells, as the unindexed rows already were.
  - **Registry search results** (Windows): each was named "Row { Item = …, Folder = … }". A result is now named by kind, name and key, for example "Value, Path, HKEY_CURRENT_USER\Environment", with control characters escaped.

  The other lists are already named by their text, by a named template root, or by a `ToString()` written for them: command search, choices, comparisons, sync, operations, permissions, verification results, bulk rename, marked items and the analysis dialogs. The other icon buttons (dialog Close, the comparison's navigation buttons) have names.

**Tests**
- **`AccessibleNameFallbackTests`**: 5 headless App tests.
  - The keyboard reference rows' exact names, an unbound and an unavailable command included.
  - Every spin button on every Settings page is named "Increase" or "Decrease"; Appearance's two boxes and Columns' Width are counted.
  - The theme choices' names and help texts.
  - The registry results' names.
  - The sweep above: no name in the main window, its menus or those twelve dialogs is a type or a record.
- **`AnalysisRenderingTests.IndexedTabPagesAndInspectsCapturedNativeMetadataAfterSourceClosure`** (Rendering tests) now also checks the indexed rows' names.

These read the names from Avalonia's own automation peers, the ones UI Automation (Windows), AX (macOS) and AT-SPI (Linux) read. So the checks are platform-neutral, and CI runs them on Windows, Ubuntu and macOS.

**Results**, at medium integrity on Windows 11. The TRX files are kept on the development PC, in `%LOCALAPPDATA%\Temp\fc-a0267\`.
- **Before**, against the unchanged product code, every check then written failed.
  - Four App tests (`before-app.trx`, SHA-256 `8362f5cddbea5f55c53b7b7755215d22da393c14315453900477f10a465dab9d`).
    - The keyboard rows read "KeyboardHelpEntry { Id = file.copy, Title = Copy, …".
    - The spin buttons on four Settings pages read "Avalonia.Controls.PathIcon".
    - All 7 theme choices read "Avalonia.Controls.StackPanel".
  - The indexed rows check (`before-render.trx`, `b3b1de9765e35ebf1e2a41be4b585aa821be3824cec2736d0351174e95117774`): a row read "AnalysisHierarchyRow { Path = AnalysisPathMetadata { Id = 303, …, ObservedIdentity = win:HarddiskVolume8:… } … }".
  - The registry test came after the fix. Dropping the results' `ToString()` made it fail (`mutation-a.trx`, `d3c7adb551d5151f567dcf7df657b570c48e94174f29ae6f0a8d0bd7b120615f`).
- **After**: 5 of 5 App tests passed (`after-app.trx`, `e2148f3a31b8a3c8cc739e5fc33d2be70b1de87af87bdada178fd97c2860463c`), and so did the indexed test (`after-render.trx`, `b72a7974b34328351639f5164c95f970d86bad8edbb0cffed6c9cf5df72d8742`).
- **Regression**: the whole App test suite on the three fixes ran 4,003 tests: 3,709 passed, 0 failed, and 294 were skipped as their environment requires (`full-app.trx`, SHA-256 `faec871c6108e203823cdb9b3121827f9d94f598ca619240e361976d8f0a39bc`, 38 min).
- **Rendering tests**: `AnalysisRenderingTests` and `PictureGridRenderingTests` ran 969 tests: 963 passed, 5 were skipped, and 1 failed (`render-regression.trx`, `b417ebd2b2af836621154fc96e845f5c622bd232615dc28ae2c2654c9e11880e`).
  - The failure was `FolderExclusionsPreserveNativeCoverageAcrossInventoryRescanAndBranchRefresh(indexed: True)`: "A native private-state boundary could not be captured."
  - It failed only with the fixture root I had set, a folder under `%LOCALAPPDATA%\Temp`, deeper than the tests' default.
  - With the default root, `%USERPROFILE%\FileCatValidation`, and the same build, both of its cases passed (`render-folderexclusions.trx`, `ff695fea0f6eed168110474262b4408182a880e94edb6bb1c27067b4a6094ba1`). The code it failed in, the indexed acquisition's preparation, is not changed here.

**Not verified here.** No screen reader was run, and no installed build. These checks read Avalonia's peers; the platform bridges that pass their names on were not observed. macOS and Linux run only in CI.

**The next installed candidate must check**, with NVDA or Narrator on Windows and VoiceOver on macOS:
- Help → Keyboard reference rows are read as their title and shortcut, with no record fields.
- The spin buttons in Settings → Appearance and → Columns are read as "Increase" and "Decrease".
- View → Theme… choices are read by their theme's name.
