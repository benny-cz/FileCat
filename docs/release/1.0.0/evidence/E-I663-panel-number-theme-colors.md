# I663 — Panel numbers keep the startup theme's colors after a theme change

**Found:** 2026-11-24 by the Ubuntu GUI harness on rc.2's tarball (`7269ec61`), row a14. It was seen in six themes, and again in a fresh FileCat (`probes/probe_number_theme.py`). **Status:** open; to be fixed for rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate).

## Observation

**Steps:** with a fresh profile (System, which is Classic on that machine), choose View → Theme… → Steampunk.

**Expected:** the panel number badges take Steampunk's colors, as the TARGET chip does.

**Actual:** they keep Classic's colors, white on #0063B1 and #5E5E5E on #F0F0F0, until the active panel changes (Tab) or FileCat restarts. In High Contrast they show blue and grey.

**Cause.** In `PanelView.axaml`, `NumberBadge` takes its colors from `Converters.ActiveAccent` and `ActiveAccentText`, which read the theme once. The panel's theme-change handler rebuilds only the place buttons. The code is shared, so Windows and macOS behave the same.

## Why it matters

The badges stay legible: the harness measured at least 4.5:1 in every theme. But they show the old theme's colors, which in High Contrast means colors that the high-contrast palette does not use.

## Remedy (proposed)

Re-evaluate the badge's colors when the theme changes, for example through dynamic resources or by refreshing the binding in the panel's theme-change handler.

## Finite check

After a theme change, both panels' number badges have the new theme's active and inactive colors without a Tab or restart, in every theme, High Contrast included.

## Fix

Branch `fix/i662-i664-ubuntu-harness-findings`, from `rc3/integration-3`.

**What changed** (`src/FileCat.App/`)
- `NumberBadge` in `Views/PanelView.axaml` takes the classes `panelNumber` and `active` (bound to `IsActive`) and no longer sets its colors itself.
- Four styles in `Themes/Styles.axaml` give its colors as dynamic resources, as the TARGET chip's are. The tokens are the same as before: the active panel's number `FcAccentFill` with `FcOnAccent` text, the others `FcHeader` with `FcTextMuted` text. A theme change swaps the resources, so every panel's number takes the new theme at once.
- `Converters.ActiveAccent` and `ActiveAccentText`, which read the theme once, had no other user and are removed.
- The number's text now carries the padding and a transparent background, so the whole number takes presses even where no style paints it. The headless App tests run without FileCat's styles: the first regression run's `DockingTests` drag of the number failed there until this was added.

**Test.** `PanelNumberThemeTests` (App, headless, 1 test), with FileCat's styles loaded:
1. Classic at startup, both panels checked.
2. View → Theme… previews Steampunk: both numbers have Steampunk's colors. Cancel brings Classic's back.
3. Every theme, High Contrast included, with each panel active in turn.

Each number's fill and text are compared with the palette: `ActiveAccent` with `OnColor(ActiveAccent)` for the active panel, `Header` with `TextMuted` for the other.

**Results**, headless on the Windows workstation at medium integrity. TRX files are kept in `%LOCALAPPDATA%\Temp\fc-i662-i664\` there.
- **Before**, against the base `2e5bb419`: fails with "Steampunk, previewed in the theme picker: panel 1 (active): the number's fill is #ff0063b1, not #ffe8b25a", Classic's blue kept (`app-before.trx`, SHA-256 `fe67148600d23ae2d7ade496112f234fc93dfaf8a6ea12e1872dbef51cabf9df`, with I664's tests).
- **After**: passes.
- **Mutations**, each failing the test:
  - the number without the `active` class: the active number in the header's colors;
  - no muted-text style: the other panel's number in #1B1B1B, not #5E5E5E;
  - the active number's text in `FcText`: #1B1B1B, not white.
- **Regression**: 36 App classes on panels, themes, the file list's accessibility and the picture grid, among them `ThemePickerTests`, `ReadabilityTests`, `DialogThemeTests`, `DockingTests`, `PathLineTests`, `AccessibilityTests`, `FileListAutomationTests` and `PictureGridTests`. On the final code: 355 tests, 0 failed (`app-regression2.trx`, `2a548f579a987c15d5544d5c169869b375f3dd246174d00d0d49ea920e9e415a`).
- **The Ubuntu GUI harness**, row a14, with a linux-x64 build of the branch's final tree (package SHA-256 `19ad8de287fc1bb3c19995a2d084e5d2dc15e1412dd166396745c4b3e458ce6b`, results in `~/FileCatValidation/aw-claude/i662-i664/harness-final/` on the VM):
  - PASS: ok 80, FAIL 0 (`a14.txt`, `c87bdc53bcb4232ad55bc59ddbdd59ece49c7fa02f5e865817aa3af62d1f67e7`). Every badge is legible in all seven themes, at the minimum window size and with larger text.
  - It logs no "badge(s) are not in its colors" observation. rc.2's run `20261124-200647` logged six, among them High Contrast with its numbers drawn #FFFFFF on #0063B1 and #5E5E5E on #F0F0F0, Classic's colors.
  - An earlier build of the branch, before the number's transparent background, gave the same.

**Not verified here.** Windows and macOS builds outside the headless tests; the harness ran on Ubuntu only.
