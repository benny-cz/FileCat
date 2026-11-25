# I661 — A place on the Places bar can be laid out past the bar's edge, clipped away while not on the overflow menu

**Found:** 2026-11-24, by integration 3's hosted CI and the reason it failed. Run 45467009793, job "macos-26 (App tests, remainder)", failed `PlaceBarHierarchyTests.Compact_panel_header_uses_available_width_and_keeps_overflow_places_reachable` at `Assert.True(pinnedButton.Bounds.Right <= row.Bounds.Width)`. The same test had failed once before, in PR #19's first attempt (run 45315256109; see [the release-branch sync record](E-REL-SYNC-20261124-release-branch.md)). The code is that of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (`7269ec61`). **Status:** open; to be fixed for rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate).

## Observation

`PlaceBarPanel` (`src/FileCat.App/Controls/PlaceBarPanel.cs`) works in two steps:
- When measured, it decides how many place buttons are shown (`ShownCount`), from the width it is offered.
- When laid out, it places those buttons side by side and the rest past its right edge, where `ClipToBounds` hides them; the overflow button's menu lists the rest (`Hidden`).

The layout step does not decide again. When the final width is narrower than the width it was measured with, the last shown buttons are placed past the edge and clipped, yet they are not on the overflow menu either. Layout rounding can do this, and so can a parent that offers more when measuring than it gives when arranging.

Shown with a direct test: five 60-pixel places and a 30-pixel overflow button, measured at 330 pixels (all fit) and laid out at 290. The fifth place ends at 300, past the bar's 290, and the overflow menu is empty.

## Why it matters

- A place can be partly or wholly out of sight and out of the overflow menu, so it cannot be reached from the bar until the window's width changes.
- Hosted CI on macOS fails intermittently on this, which can hide a real failure in the same job.

## Remedy (proposed)

Decide what fits from the final width as well, through the same rule. A place that no longer fits then moves to the overflow menu, and no shown place ends past the bar's edge.

## Finite check

In a narrow panel with many places, widen and narrow the window slowly: no place button is ever cut off at the bar's right edge, and every place is either on the bar or on the "More places" menu.

## Fix

Branch `fix/i661-place-bar-arrange-fit`: one rule, `Fit`, decides how many places fit in a width, leaving room for the overflow button when not all do. `PlaceBarPanel` now applies it when measured, as before, and again when laid out, with the final width. So a place that no longer fits moves to the overflow menu, and no shown place ends past the bar's edge. A bar laid out in the width it was measured with behaves as before.

Tests, on the Windows workstation at medium integrity (`whoami /groups`: Medium Mandatory Level), headless:
- New `PlaceBarPanelLayoutTests` (2 tests). With rc.2's panel, `A_bar_laid_out_narrower_than_it_was_measured_moves_what_no_longer_fits_to_the_overflow_menu` fails: "A shown place ends at 300, past the bar's 290." With the fix both pass.
- `PlaceBarHierarchyTests`, `PlaceBarRetirementTests` and the new class: 23 of 23 pass.

Not verified here: macOS, where the CI failure appeared; hosted CI runs the classes there.
