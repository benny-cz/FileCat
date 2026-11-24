# I664 — The picture grid exposes fewer tiles to screen readers than it draws

**Found:** 2026-11-24 by the Ubuntu GUI harness on rc.2's tarball (`7269ec61`), row a18, through AT-SPI. Capture: `E:\FileCat\.tmp\fq-rc2\ubuntu\shots\a18-check\a18-grid-top.png` (local only). **Status:** open; to be fixed for rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate).

## Observation

**Steps:** open a folder of 9 items in Pictures mode with 2 columns. 6 tiles are drawn, the third row partly.

**Expected:** all 6 drawn tiles are exposed to assistive technology.

**Actual:** only 5 are exposed. `02b-damaged.png` is drawn with its name but is missing from the accessibility tree.

**Cause.** `FileListAutomation.ExposedChildren` exposes `VisibleRowCapacity + 1` items. That is list mode's rule: one row per item, and one partly shown row. In grid mode the partly shown row has one tile per column, so tiles that are drawn go unexposed. The code is shared by all three platforms.

## Why it matters

A screen-reader user exploring the grid by object navigation, or by touch, does not find tiles that a sighted user sees. The keyboard still reaches every tile, because moving the cursor scrolls the grid and exposes it.

## Remedy (proposed)

In grid mode, expose every tile of the visible rows: the partly shown row is a row of columns, so the count is (visible rows + 1) × columns, bounded by the item count.

## Finite check

In Pictures mode with 2, 3 and 4 columns and a partly shown last row, every drawn tile is in the accessibility tree, and nothing more than one row past the view.
