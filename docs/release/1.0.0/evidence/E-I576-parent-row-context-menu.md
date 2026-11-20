# I576 — parent row had an item menu instead of parent actions

Recorded 2026-10-31 after the owner asked whether right-clicking `..` should offer a context menu. This follows I572's accepted parent navigation fix but asks a different UX question: whether the menu describes the parent target without inviting mutation of a synthetic navigation row.

On Ubuntu 24.04.5 at exact source `ed87efc9`, native right-click on `..` opened the ordinary item menu with Open, Open with system application, Cut/Copy, Paste, Properties, Streams and File-system record. It did not name the parent path or offer a clear parent-specific new-tab route. Ignored capture: `artifacts/exploratory-20261031-parent-popup-before.png`.

Product `a00dcfd7` recognizes the focused parent row before the Windows Shell menu or ordinary item menu. Its small menu offers only **Go to parent**, **Open parent in new tab**, and **Copy parent path**. It captures the current tab/location and refuses a stale menu action if that location changes. Keyboard Shift+F10 on the parent uses the same menu; ordinary files and listing whitespace keep their existing menus.

The focused App parent-menu test passed **1/1**: it saw only those three actions, no Delete/Cut, activated Go to parent, preserved the owned file and reached the exact parent location. The exact pushed `a00dcfd7` Ubuntu Release build had zero warnings/errors. Native right-click showed exactly those three entries, and clicking Go to parent reached the owned parent while the target remained empty and both owned source hashes stayed exact. Ignored captures: `artifacts/exploratory-20261031-parent-popup-after.png` and `artifacts/exploratory-20261031-parent-nav-after.png`. The isolated app/fixture was removed. Latest-source five-lane CI is in progress; this is not an installed candidate result.

## Finite 1.0.0 stopping rule

On the exact installed candidate, right-click and Shift+F10 on `..` in a normal file-system folder. Require only parent navigation/new-tab/path-copy actions and no mutation action. Go to parent must reach the exact parent; new tab must leave the original tab unchanged; copied path must equal the parent. Preserve the ordinary file and whitespace menus. Reconcile final-source CI; stop without a wider menu audit unless this specific route fails.
