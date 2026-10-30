# I568–I569: viewer Find feedback and dismissal

2026-10-30. Preliminary source and local-input evidence. This record does not qualify a signed, installed 1.0.0 candidate.

## Observed defects and corrections

- **I568:** Viewer Find originally waited for Enter, showed a red border before any search, and stopped at the end of the page. A first implementation at `87c71035` added 250 ms search-on-type in Text, Hex, Info, Page, Report, and Hex Editor; neutral/present/missing feedback; and directional wrap in those viewers and Compare. The owner confirmed wrap locally but reported that its status message was immediately replaced. `a613d169` keeps a wrap notice in the Find row and preserves it through Viewer and Report status refreshes. A new query or search clears the old notice.
- **I569:** The Find row opened on demand but had no clear dismissal control within the row. `d8b3e6b3` adds **Close Find** to Viewer, Compare, and Hex Editor. Escape closes the row when a Find control owns focus; an active search still uses the first Escape to stop. Closing returns focus to the content, and reopening retains the query. Report's search is part of its always-visible toolbar, so this row-dismissal behavior does not apply there.

## Verification at the product source

- Focused App regression at `a613d169`: **32 passed, 0 failed, 0 skipped**, including an extra status refresh after forward/backward wrap in Page, Text, and Report. Hex Editor's existing `_message` status and Compare's dedicated Find status already survive general status updates.
- Focused App regression after `d8b3e6b3`: **34 passed, 0 failed, 0 skipped** across Viewer toolbar/Find/Page, Compare text interaction, Hex Editor Find, and Report search lifetime. It checks close/reopen with retained query, content focus after the Close button, Escape dismissal, Page reopening with focus, neutral/green/red feedback, and wrap notices after redraw.
- The earlier Windows 11 VM source build, before `a613d169`, received actual keyboard input in Markdown Page mode: an empty query was neutral; typing `needle` found a match and turned the border green; F3 and Shift+F3 wrapped in both directions; `zzabsent` yielded zero matches and a red border. The raw [native result](I568/native-win11-result.json) and [screenshots](I568/wrapped-forward.png) are **pre-status-persistence** evidence, not screenshots of `a613d169` or `d8b3e6b3`. The App DLL SHA-256 was `FC981234F7721CC281814CE4B48ED3604DE3BFD04098ABEB3263181B0E71B927`; the Windows platform DLL SHA-256 was `645338DA82038F0C39E2E4FE46F7BD03B249227BE186A5F52349232F92306D35`.
- The owner tested the wrap behavior locally after the status correction and reported it worked as expected. No exact installed-package identity was supplied for that observation. A further headless VM attempt was interrupted after the VM was off and yielded no new result; it is not counted as a pass.

## Remaining release gate and stopping rule

`d8b3e6b3` is the latest product source. Its required producer, Windows x64, Windows ARM64, Ubuntu, and macOS CI jobs and any new skips must be reconciled under GATE-CI-FINAL. On the exact signed, installed Windows 1.0.0 candidate, verify Page and at least one text/hex search with a neutral initial state, debounced positive and zero results, forward and backward wrap with a notice still visible after a status redraw, Close Find and Escape dismissal, retained query on reopening, and content focus after close. Exercise Compare and Hex Editor dismissal on that candidate. Stop these child cases only when those observations pass on the candidate and no consequential search regression remains. The public preview.3 binaries predate both changes.
