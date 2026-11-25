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

## Fix

Branch `fix/i662-i664-ubuntu-harness-findings`, from `rc3/integration-3`.

**What changed** (`src/FileCat.App/Controls/`)
- `FileListControl.DrawnItemCapacity` says how many items, from the top one, the view draws: the whole rows and the partly shown row after them. In the list that row is one item; in the picture grid it is a tile per column.
- The list and the grid draw that many items, as they did, and `FileListAutomationPeer` now exposes exactly those, plus an offscreen focused item as before. Its peer cache counts the same items as in view.
- So in Pictures mode every drawn tile is exposed, and nothing more than one row past the view. The list keeps its rule, `VisibleRowCapacity + 1`.

**Tests.** `PictureGridAutomationTests` (App, headless):
- **The grid**: 2 columns with 9 items (the record's folder); 2, 3 and 4 columns with 20 items; 4 columns with 9 items, whose partly shown row holds one tile. Two whole rows and half a row are in view. Each is checked at the top, one row down and at the end.
  - Drawn tiles are the ones whose bounds meet the view.
  - Every drawn tile must be exposed, and nothing else but an offscreen focused tile.
- **The list**: 100 items and a partly shown last row. Exactly the whole rows and that row are exposed.

**Results**, headless on the Windows workstation at medium integrity. TRX files are kept in `%LOCALAPPDATA%\Temp\fc-i662-i664\` there.
- **Before**, against the base `2e5bb419`: 4 of the 5 grid cases fail, all at the top of the grid (`app-before.trx`, SHA-256 `fe67148600d23ae2d7ade496112f234fc93dfaf8a6ea12e1872dbef51cabf9df`):
  - 2 columns, 9 items: 6 drawn, 5 exposed, as the record found;
  - 2 columns, 20 items: 6 drawn, 5 exposed;
  - 3 columns: 9 drawn, 7 exposed;
  - 4 columns, 20 items: 12 drawn, 9 exposed.

  4 columns with 9 items and the list test pass on the base too.
- **After**: the 6 pass.
- **Mutations**:
  - the list's rule in the grid too: the 4 grid cases fail;
  - one row more exposed than drawn: the 4 grid cases and the list test fail;
  - the list exposing two rows past its whole rows: the list test fails.
- **Regression**: the 36 App classes in I663's Fix, 355 tests, 0 failed (`app-regression2.trx`, `2a548f579a987c15d5544d5c169869b375f3dd246174d00d0d49ea920e9e415a`). The Rendering tests' `PictureGridRenderingTests`, drawn with Skia: 3 of 3 passed (`rendering-grid.trx`, `81ef12a4def77b3a53f177dc21f6f09845fe19aa3a6cac1e2dde990729a06af9`).
- **The Ubuntu GUI harness**, row a18, with a linux-x64 build of the branch's final tree (package SHA-256 `19ad8de287fc1bb3c19995a2d084e5d2dc15e1412dd166396745c4b3e458ce6b`, results in `~/FileCatValidation/aw-claude/i662-i664/harness-final/` on the VM):
  - PASS: ok 16, FAIL 0 (`a18.txt`, `b769578bfa63f6521701f588b8798316fd6bfe4989a0a2d1f552ae9f607461e3`).
  - Its observation, through AT-SPI: "at the top of the grid 6 tiles lie in the panel's view (2 columns), 6 are exposed to accessibility", `02b-damaged.png` among them. rc.2's run `20261124-200647-a18` had 6 and 5.
  - An earlier build of the branch gave the same.

**Not verified here.** A screen reader on the grid (NVDA, VoiceOver or Orca); the harness read the tree through AT-SPI only. More tiles in view than the peer limit (256) stay bounded by it.
