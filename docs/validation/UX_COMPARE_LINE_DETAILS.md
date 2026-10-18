# Inspectable aligned text comparison

The FCUX-009 implementation now presents ordinary source lines directly in the main virtualized comparison. Both panes share a horizontal position; **Wrap long lines** gives each source pair a common variable row height. Source line numbers and actual gaps remain aligned. Lines above the explicit main-rendering bound retain the complete-line detail route. The main renderer is bounded; it is not an unlimited comparison editor or a completion claim for FCUX-056.

## Main comparison surface

Each ordinary side cell renders the complete decoded source line, up to **16,384 UTF-16 code units**, with no ellipsis or synthetic newline. Line numbers stay in their own gutters while both text panes move together. The common horizontal scrollbar, **Alt+Left / Alt+Right**, and horizontal or Shift+vertical wheel input provide explicit horizontal routes.

**Wrap long lines** wraps both sides at their actual pane widths. The enclosing row takes the taller side's height, preserving the source pair and any absent counterpart. A gap is labeled as an absent source line rather than being represented as an invented empty line. Line-ending differences retain their gutter cue and route to the existing Binary view for exact bytes.

**Next/Previous difference**, including **F8**, **Shift+F8**, the existing Alt shortcuts and pointer buttons, reveal the first exact changed-text location horizontally and vertically in the main surface. The reveal uses the same shaped source-offset geometry as drawing, including tabs and combining sequences. A long token whose final character differs is therefore navigable to that final character. Changed spans also have geometric underlines, so the distinction does not depend only on color.

A line above the main bound has an explicit action such as **60,000-character source line — inspect complete line**, with the source side, line number, exact UTF-16 length and bound in its accessible description. Activating it opens the complete-line reader on the first changed location. It does not quietly draw a shortened source line and claim that it is complete.

## Exact full-line details

Selecting any text row updates the **Full-line detail** area. **Alt+L**, the **Full-line details** button, a double-click on a row, or an exceptional line's action focuses that area. Its two read-only standard text fields preserve the decoded source text and support normal text selection and copy controls. Source line numbers and explicit gaps remain above the fields.

Unwrapped detail fields synchronize horizontal scrolling. **Wrap details** enables wrapping inside both fields. **Reveal changed text** returns to the decisive source text after scrolling or part navigation. A source line ending before its counterpart is identified as an ending. If the comparison options treat different raw strings as equal, the detail says so.

A detail field normally shapes **16,384 UTF-16 code units per part**. If the first differing text element starts in that part but ends just beyond it, the part extends by at most **256 code units** to include the whole element. Up to two further code units avoid splitting a surrogate pair at a boundary, giving an absolute bound of **16,642 code units per field**. This makes the decisive accent visible for 16,383 `x` characters followed by `e\u0301` versus `e\u0300`, without shaping an unbounded cluster on the UI thread.

Exceptionally long lines expose **Previous part / Next part**, source offsets and the displayed range. The initial part contains the start of the first difference; every part remains reachable. Adjacent parts can overlap the bounded extension or boundary surrogate pair, with the displayed ranges identifying that overlap. Paging never substitutes an ellipsis for undisclosed source text. It is not general grapheme-safe paging: unrelated text elements may cross a boundary, and an element requiring more than the 256-code-unit extension remains split across parts. Visible status and accessibility help disclose a partial differing element and direct the user to the remaining parts.

## Resource and lifetime guarantees

The list uses a finite virtualized viewport. Only realized ordinary source cells own text layouts. Each cell caches one layout for its source, actual wrap width, font and text style; horizontal movement changes only its drawing origin. Replacing a layout or detaching a cell disposes that layout. Transient star-column widths below 80 DIPs do not shape an entire line into thousands of temporary rows before the usable width exists.

The main layout owns one source string representation. Underline rectangles are generated once per layout by intersecting ordered change spans with their actual visual lines. Repeated painting does not allocate a fresh all-lines geometry list for every changed span. The primary source bound and finite realized control set apply even when the model contains a million rows.

Each source cell has a read-only text peer with a stable side/source-line name and the exact source value. Reading the peer does not trigger shaping or source I/O. The standard full-line fields remain the route for normal text selection and copy. Native speech and backend behavior still require platform validation.

Queued reveal callbacks capture only a request number. The current pending request owns the row/result identities and selection index while it is valid; reset and close clear that ownership immediately, even if the dispatcher queue is blocked. Callback execution revalidates ownership after layout callbacks as well as before them, and releases its pending state when it finishes. A newer selection, deliberate scroll, replacement result or closed window retires the older request. Exceptional-row actions also verify their current container and source-row identity before changing selection or focus.

Result replacement immediately clears retired container contexts and template children. When the virtual list reuses a container, comparison preparation restores only that outer presenter's normal content binding, so a same-sized replacement renders the new source rather than retaining retirement's local null. Removed nested templates stay retired. Templates keep a weak association to the source model owned by the live result list: a detached template retained by render/scroll state cannot keep an old comparison model alive. Reset and disposal explicitly clear current associations and release their source layouts; later arrangement cannot recreate retired layouts.

For lines exceeding the main bound, finding the first changed text element runs off the UI thread over the already decoded strings. A row change cancels the earlier detail request. Queued detail focus is applied only while the original root and focused element still own it and the window remains active. Closing clears detail text, row references and layouts. Presentation, part navigation and accessibility inspection do not reopen sources or initiate file-content reads.

The existing exact-byte equality, decoded-text equality, BOM/encoding distinctions, ignored whitespace/case options, line-ending difference, binary alignment, heuristic/approximate summaries and source-revision guards retain their original comparison contract.

## Automated validation

`tests/FileCat.App.Tests/CompareTextViewTests.cs` covers:

- All twelve combinations of **640 / 980 / 1200 DIPs** and source lengths **60 / 80 / 200 / 10,000**, with only the final character differing. Each window is sized before being shown and its actual client width is asserted. Tests use actual keyboard and pointer input, verify the pointer's rendered hit target, enable main wrapping, and assert the changed glyph is inside both its source pane and the actual main scroll viewport.
- A shared keyboard-controlled horizontal position, stationary line-number gutters, actual source offsets through tabs/emoji/combining sequences, unequal wrapped line heights, inserted gaps and source line numbers.
- A 60,000-character exceptional row with an honest bound/action and complete source-part inspection by keyboard and pointer.
- A million-row model with bounded materialization at the first and last rows, retired row actions, queued reveal after source replacement and closure, disposal of detached layouts, and immediate collection of retired row/result models while the dispatcher queue remains blocked.
- Deferred shaping at transient zero/tiny widths, font-driven reflow, exact read-only source semantics without shaping, and repeated unchanged arrangement of a 10,000-character line with 5,000 small changed spans without rebuilding its layout or fragment geometry.

`tests/FileCat.App.Tests/CompareLineDetailsTests.cs` retains the twenty full-line-detail cases: the same twelve widths/lengths, exact source reassembly through a 60,000-character line, synchronized scrolling, source-state labels, ownership/focus retirement, the combining-mark part-boundary reveal, and explicit disclosure/reassembly for an oversized differing text element.

Run these with the existing comparison/revision/retirement contract tests:

```sh
dotnet test tests/FileCat.App.Tests/FileCat.App.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~CompareTextViewTests|FullyQualifiedName~CompareLineDetailsTests|FullyQualifiedName~CompareWindowTests|FullyQualifiedName~CompareRevision|FullyQualifiedName~ComparisonWindowRetirementTests|FullyQualifiedName~ComparisonReplacementRetirementTests' --blame-hang --blame-hang-timeout 90s --blame-hang-dump-type none
```

### Recorded execution

On 2026-10-17 the pinned .NET SDK 10.0.101 / Avalonia 12.1.1 Release build completed with **zero warnings and zero errors**. The final focused gate above completed successfully: **96 passed, zero failed, zero skipped**, in **76.4 seconds**. This comprises 23 new primary-surface/ownership cases, the 20 existing full-line-detail cases, and 53 existing comparison, revision, window-retirement and replacement-retirement cases. The unchanged public replacement tests retain the former realized row and closed window while requiring the source model to be collected at 16, 4,096 and 32,768 rows.

Evidence files are `comparison-commit-build.log`, `comparison-commit-final.log` and `test-results/comparison-commit-final/comparison-commit-final.trx` in the implementation workspace. The final gate ran without a hang or process termination, using a 90-second blame-hang bound per test. Earlier diagnostic failures and incomplete resource runs are retained for diagnosis and are not counted as passing evidence.

## Remaining scope and platform validation

The main surface displays complete ordinary lines within its stated shaping bound and retains the exact full-line/source-offset alternative for larger lines. General comparison text search, multirow per-side text selection, and richer selection/export remain FCUX-056 scope. This change does not claim a full editable comparison or merge surface.

Native screen-reader journeys and enlarged-font/scaling layouts still require platform validation. Detail paging does not guarantee whole-grapheme presentation for exceptional oversized clusters. These limits remain disclosed rather than being described as an unlimited text surface.
