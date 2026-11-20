# I590 — hidden items were omitted from the panel without a status explanation

## User consequence and distinct scope

On macOS, **File → Inspect → Change attributes and times…** set the Finder `hidden` flag on one of two owned files. **Navigate → Hide hidden and system items** removed that row from the panel, but the status still said `0 folders, 2 files · 43 bytes`. A user could mistake the mismatch for a stale listing or a missing file. The folder total includes all items; the status did not explain why fewer rows were shown. Existing filter status and platform hidden-attribute controls did not cover this ordinary visibility handoff.

The exact before source was product `292bcb92558e72756ae1a90d77e069632f4ea4a8` in an isolated, ad-hoc-signed Mac app. `keep.txt` was 17 bytes, SHA-256 `0d62e0774ace3c74a2140b1cd2972ccd273359ff97575e82a9a72ff9959c665d`; Finder-hidden `visible.txt` was 26 bytes, SHA-256 `fcd2676a440d5022c56332266b2d1c2ca51a280ed9ff0216d0de3a1c4f53c4a0`. The owned target was empty. Both AX rows appeared with Show hidden on; only `keep.txt` appeared after hiding, while the unchanged total lacked a qualifier. `ls -lO` independently confirmed only `visible.txt` had the `hidden` flag.

## Correction and bounded verification

Product commit `55242553` adds a status qualifier when a completed, unfiltered listing hides one or more items. It subtracts the visible non-parent rows from the listing total; it leaves the full folder count and bytes intact. A focused Windows Release `HiddenStatusTests` test passed **1/1**, checking hidden-off row removal and the restored unqualified status after hidden-on.

A clean Mac source checkout at exact `55242553` built the local `1.0.0-exploratory` ZIP, SHA-256 `859c76121e6ad0583fcddb01c85e455acd8b242c413e8e15e4691fb0fb1f0c45`; staged App DLL SHA-256 `47b73a4b5a40dae7d7d5c161a2f7bebb058a3844ef4ee8f94aaa896dacc3a209`. The extracted bundle passed strict deep code-signature verification. In a fresh isolated native process, Hide hidden and system items showed only `keep.txt` and the status `0 folders, 2 files · 43 bytes · 1 hidden or system item not shown`. Show hidden and system items restored both rows and the original unqualified status. Independent `ls -lO` still showed the Finder flag, both source hashes matched the before oracles, and the target remained empty. Both isolated app processes were stopped. This checks one visibility/status outcome, not the hidden-file scanner or every filter combination.

## Disposition and finite candidate rule

Medium UX clarity issue, **remediated preliminarily**. On the exact installed 1.0.0 candidate, use one owned hidden/system item and one ordinary sibling on Windows and one Finder-hidden or dotfile item on Mac/Linux. With hidden items off, require the ordinary row, absent hidden row and a truthful hidden-count qualifier; with hidden items on, require both rows and no qualifier. Check a parent row so it is not counted as hidden, and preserve exact bytes/flags. Stop after these affected routes and latest-product five-lane CI with skip reconciliation. Public preview.5 predates this source change. Stable 1.0.0 remains **NO-GO** pending the other release gates and owner approval. The active exploratory clean streak resets to **0/20**.
