# I609 — separate filename clicks should start inline rename

**Status:** Remediated preliminarily at product `0ee87c9d`; latest-source five-lane CI and frozen installed 1.0.0 candidate checks remain open. **Severity:** Medium UX friction in an everyday file operation. **Owner:** V17/V23/GATE-CI-FINAL. Public preview.5 predates the correction.

## Observation and boundary

The owner reported that clicking twice on a filename with a pause between clicks did nothing. They explicitly distinguished this from a double-click, which opens an item. F2 and the existing rename editor already worked; neither covered the mouse convention. Source review confirmed that `FileListControl` opened on `ClickCount >= 2` and otherwise only selected/focused the row or prepared a drag. There was no separate-click rename event. This report preempted the next advanced-filesystem draw in the exploratory loop. No native before-state recording was made, so the report and source path are the pre-fix evidence.

## Correction and focused verification

The list now accepts two separate released clicks on the **same rendered filename**, within four seconds, when the second press is a single-click event. It dispatches the existing Rename command, which uses the existing inline editor for local files. A real double-click still opens; a parent `..` row, whitespace in a wide Name column, a different row/name, drag, mark gesture, scroll, key, focus or tab/list change retires the candidate. The row is bound to its current store index and exact name, so a reused row does not inherit a prior click.

The focused headless pointer test requests Rename once after two separate filename clicks, no Open, and no Rename on a Name-column whitespace click. The affected list-selection class passed **12/12**. Release App builds completed with **0 warnings and 0 errors**. These controls do not substitute for native input.

The disposable Win11 VMware guest ran a new isolated `--data` instance with owned source and empty target directories. The committed `0ee87c9d` App DLL SHA-256 was `3BC562A4EED1E4F852F8D157D91F4AEAD224D9EC39856AEA1E30BC9B7F9DF61B`. Process-scoped UI Automation located `sample-evidence.txt`; two actual mouse clicks at its filename, 800 ms apart, produced one **Rename selected item** editor with native focus. Escape canceled. The original source still existed with SHA-256 `2B49D0FF12542AE209B918EE511986020C1AC49EC0738FF9597B4AB64B06D29F`; the target count remained zero. The isolated process stopped, and path/hash-guarded cleanup removed only its owned fixture. Ignored local receipts: `.tmp/i602-vm/cycle27-inline-ui-result.txt`, `cycle27-inline-final-ui-result.txt` and `cycle27-inline-cleanup.ps1`.

## Finite candidate acceptance and stopping rule

On the frozen installed Windows 1.0.0 candidate, use an owned file and folder: a single click selects, two separated clicks on the same filename focus the inline editor, Escape leaves names/bytes unchanged, and Enter commits one renamed owned item with its bytes preserved. A true double-click opens without entering rename. Clicking whitespace, another row, or dragging must not start rename. Check one native macOS or Ubuntu input route if that platform's click-count convention differs in the candidate; otherwise the cross-platform affected test plus five required CI lanes suffice. Compare source and target manifests and remove only owned fixtures. Stop at this gesture boundary and exact-source producer/Windows x64/Windows ARM64/Ubuntu/macOS CI with skips reconciled; do not replay bulk rename or other accepted file-operation cases without a consequential new observation.
