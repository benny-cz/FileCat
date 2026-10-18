# Viewer geometry, source Copy, and current semantic state

This note covers the implementation of **FCUX-012**, **FCUX-076**, and the coupled viewer/editor portion of **FCUX-073**. It supplements the earlier S1 clipboard/search/checksum note. Comparison surfaces are implemented and validated separately.

## Delivered behavior

### Source identity survives layout changes

`TextViewer` stores selection endpoints as decoder-consumed byte positions in the current source. A bounded decoded segment retains both the original characters and their displayed form, with mappings for graphemes, tabs, control symbols, malformed input and legacy decoder fallback groups. It does not infer source positions by re-encoding displayed text.

Changing width, content font size or Wrap reprojects the same selected range. Pointer hit testing, selection rectangles, search highlights and text rendering use the same shaped `TextLine` geometry. UTF-8/UTF-16 boundaries are retained when a giant logical line is split into bounded segments; a segment continuation is not a source newline. Known segment tab-stop context remains attached to the segment during navigation and reflow.

The decoder remains bounded to 16 KiB source segments, with a bounded predecessor cache rather than a file-wide line index. Direct navigation into a previously unvisited giant line uses a stable bounded source block and reveals the target within it. Ordinary source line endings are recorded separately from display wrapping. Unwrapped Text and Info have a horizontal scrollbar, keyboard Home/End and left/right navigation, and horizontal or Shift-wheel scrolling. Info retains report styling and exposes Wrap. The chosen content font is retained.

Source/report replacement and encoding changes retire an existing selection. The reason is visible, and a subsequent ordinary Copy cannot silently become a copy of unrelated visible rows. The user can make a new selection or explicitly choose **Copy visible source text**.

Copy captures the revision of the committed view. If a background reader refresh has already observed replacement content but its UI callback is still pending, the old rows cannot authorize copying bytes from the new revision. That request retires the old selection; an explicit visible-content action is available after the current view is reconciled. A synchronous content or automation subscriber can also replace or clear a reader: intermediate rows and copy scopes are immediately retired, and the outer layout commit finishes with the current source.

`HexView` retains sixteen bytes per source row and adds a horizontal viewport. Cursor movement, text/hex column choice, rendering, hit testing and the selected byte range share that geometry. The active byte and column are revealed at supported font sizes without shrinking the user's chosen font. Offset labels widen for sources beyond 4 GiB.

### Copy has an explicit representation and completion result

The viewer and report expose **Copy source selection / Copy visible source text** and **Copy displayed text**. Ctrl+C uses the selected source range, or the explicitly described visible-lines scope when no selection has ended. Ctrl+Shift+C uses the displayed representation. The editor preserves its established hex-versus-ASCII column choice.

Source text means characters decoded with the captured encoding. Tabs, CRLF, LF, CR and the final absence of a line ending are preserved. Visual wrapping, display decoration and the 16 KiB segmentation add no source newlines. This is a decoded-text contract, not an assertion of original-byte fidelity; Hex Copy provides byte values.

Displayed text expands tabs at the view's four-character stops and uses visible symbols for ASCII controls. The editor's displayed ASCII copy uses dots for nonprintable bytes. These alternatives are named and described. NUL is refused for source text because native text clipboard representations can terminate at U+0000; the visible result explains the limitation and points to displayed text or hex bytes. The original source is not modified.

Ordinary selected/visible Copy reads at most 1 MiB of source bytes, with small bounded decoder lookahead so a text prefix does not split a valid character. The result records the requested extent, represented bytes, representation, budget and reason for any omission. A deliberate cap states **first X of Y** and that remaining bytes were omitted. A short or failed read is explicitly **INCOMPLETE READING**, including when the budget also applies. A missing decoder lookahead is treated as a read shortfall rather than silently appending a replacement character. Zero readable bytes and refused representations preserve the prior clipboard value.

Results remain in a selectable panel until the user dismisses them or starts another Copy. Cursor status changes cannot erase the copy evidence. ReportWindow retains its original complete **Copy all** route; the ordinary selection/visible copy budget does not truncate that existing full-report action.

### Asynchronous work and native clipboard ownership

Each Copy freezes the reader, source generation, range, encoding, representation and relevant mode before awaiting work. Read completion, errors and result presentation belong only to the current live request. Closing, replacing content, changing interpretation or issuing a newer Copy retires older work. Source revision/length are checked around the background read. These checks have the same practical limits as the source's revision token; an unavailable or weak token cannot prove that bytes never changed without a detectable revision change.

The hex editor captures the current in-memory edit-buffer revision. It refreshes the page reader's older metadata on the worker before reading, so newly typed bytes remain copyable without treating a stale page revision as the current edit state. Held metadata tests cover another edit, switching to the saved-as source, closing and Stop copy. The old source remains pinned until the outstanding metadata call returns, and canceled work cannot publish bytes or replace current status.

Clipboard publication is serialized within each host. A delayed older native write completes before a newer admitted write, so it cannot subsequently overwrite the newer result. The pinned clipboard interface does not expose cancellation for an already-issued native write. If cancellation reaches that boundary, the retained message says that the backend may already have received the captured result; it does not claim that the write was revoked. Superseded requests cannot overwrite a newer request's result panel.

### Committed accessibility state and bounded layout work

Text navigation commits current rows before announcing changed Value, HelpText or ItemStatus. Delayed page completion announces only current content. Pointer/source selection, mode and source changes update the cached semantic snapshot. Getters never initiate source reads, and equivalent snapshots are deduplicated independently of repainting. Accessible excerpts are bounded and use visible control symbols; they are not full-file eager extraction.

Hex semantics include the current byte and row, selected byte range, active editor typing column, pending first nibble, unsaved byte count and effective editing eligibility. The editing host supplies the actual save/recovery state. The descriptive `IValueProvider` remains read-only; setting its string is not an API for replacing binary content. F6 moves from editor content to toolbar/recovery actions and back; existing Tab column switching and Ctrl+F remain available.

The editor footer uses the platform's actual protection contract: exclusive writer exclusion where `ProtectedHexFile.ExcludesWriters` applies, and an advisory-lock description with the possibility of other writers on the other supported platforms. Its unsaved and interrupted-save states are exposed through the actual controls and peers.

Shaped text layouts are owned by the current bounded source segments. Height changes, copy-result presentation and movement within a wrapped segment reuse that layout. A change to font, wrapping width, source interpretation or revision invalidates it; layouts outside the committed viewport are disposed. Search match ranges are mapped and cached before painting, and only ranges intersecting the visible text are drawn. A common search term on a giant line does not rescan every source atom for every hit on every repaint.

Selection and highlight rectangles are cached with the committed content. Unwrapped geometry is bounded before measuring by the intersecting shaped runs in visual order. Each run maps back to source-display positions independently, preserving intervening right-to-left runs in mixed-direction text. A future formatter that cannot provide the known string-backed mapping falls back to the already bounded segment for correctness. Repainting does not rebuild selection bounds.

The fixed cross-platform font list is resolved once, during the existing initialized font-measurement lifecycle, to each selected regular/bold face's actual system family name. The chosen primary family, style, weight and requested size are retained. Subsequent missing characters use the font manager's fallback after that primary face; the former list of secondary platform aliases no longer determines that fallback priority. This prevents the pinned stack from repeatedly synthesizing alias faces for unsupported characters. It does not change global font settings or dependencies. Native Unicode fallback remains an acceptance item below.

## Test coverage and evidence

The new suites are:

- `TextSourceMappingTests`: exact decoder consumption for valid and malformed UTF inputs and a system multibyte code page, tab/control mapping, real endings, copy character boundaries, missing-lookahead shortfalls, source versus display fidelity, and NUL behavior.
- `ViewerGeometryAndSemanticsTests`: actual window/viewport/glyph bounds at 480/980 DIP text widths and 480/620/980 DIP hex/editor widths with 13/18/24/32 fonts; sources beyond 4 GiB; real pointer and keyboard paths; selection preservation through resize/zoom/wrap; giant-line navigation; selected font family/style/size retention; mixed-direction visible selection; committed and reentrant event payloads; held first pages and retired sources; stable painting; and bounded shaping/highlight reuse.
- `ViewerSourceCopyTests`: actual Text/Info/report command boundaries and clipboard contents, real logical endings and 40,000-character lines, 1 MiB/1 MiB+1/2 MiB hex scopes, short/failed reads, cancellation/close/source/encoding/mode/new-request ownership, completed refresh with a pending UI commit, native-write serialization, persistent errors, and complete report Copy all.
- `HexEditorSemanticsTests`: real editor typing in both columns, pending nibble and unsaved announcements, minimum-width/max-font editing and exact edited clipboard bytes, focus return from search/toolbar, platform-specific writer-protection wording, held metadata ownership, and an isolated durable stopped-save fixture that blocks typing and exposes recovery actions without changing the original bytes.

### Final combined verification

The final Release App.Tests build completed with **exit 0, zero warnings and zero errors** using SDK **10.0.101**, the locked restore and unchanged dependency graph. Build output is retained at `test-results/viewer-final-build.log`.

The complete combined selection finished **257 passed, zero failed, zero skipped, zero aborted**. This is **122 new cases plus 135 protected existing cases**, run together through the ordinary Linux runner. The runner exited 0 in 62.728 seconds, including staging and evidence collection.

| New class | Passed cases |
| --- | ---: |
| `TextSourceMappingTests` | 29 |
| `ViewerGeometryAndSemanticsTests` | 60 |
| `ViewerSourceCopyTests` | 26 |
| `HexEditorSemanticsTests` | 7 |
| **New total** | **122** |

The protected 135 cases include text wrapping; viewer/hex-editor copy and search lifetimes; backward-search scope; checksum scope, revision and lifetime; initial detection and revision polling; direct-content/source admission; Info and reports; editor smoke/POSIX behavior; and the existing read-only viewer accessibility case. Every selected class and parameterized case is enumerated in `test-results/viewer-case-multiset-comparison.json`.

The retained final evidence is `test-results/viewer-final-verified/`: `results.trx`, `run.json`, `identity.json`, `tests.log` and `tests.runsettings`. The runner used private copies of the built outputs and SDK. All real/effective/saved/filesystem user and group IDs were 65534, supplementary groups and all capability sets were empty, `no_new_privs` was set, and the attempts to regain root or bypass ordinary file permissions were denied. Its receipt retains the assembly and runsettings hashes. The exact VSTest filter was:

```text
FullyQualifiedName~FileCat.App.Tests.Viewer|FullyQualifiedName~FileCat.App.Tests.HexEditor|FullyQualifiedName~FileCat.App.Tests.ReportWindow|FullyQualifiedName~FileCat.App.Tests.TextViewerWrap|FullyQualifiedName~FileCat.App.Tests.TextSourceMapping|FullyQualifiedName~AccessibilityTests.Viewers
```

The preceding complete diagnostic run at `test-results/viewer-final-ordinary/` passed 255 of the same 257 cases. Two fixture corrections account for the change: the delayed-copy close case now retains the actual clipboard service before `Window.Close`, and the checksum scope-movement case arms its held source before the public navigation action can request the accessible row. Exact clipboard, source-lifetime, checksum scope and hash assertions remain. The retained multiset comparison confirms no missing or added cases, unchanged class counts, and only those two failure-to-pass outcome changes. An earlier stopped-save fixture was also isolated with its own `dataRoot`, and the Stop button fixture uses the actual **Stop copy** action name.

Focused runs overlap the combined regression and later integrated/full-suite runs. They must not be added together as independent test totals.

### Long-report resource evidence

The same four Text/Info × wrapped/unwrapped cases retain the same 40,000-character input, source/display clipboard assertions and 1.5 GiB managed-heap cap. The pinned headless drawing/font configuration is unchanged.

| Measurement | Before canonical font resolution | Corrected |
| --- | ---: | ---: |
| Complete four-case result | 4 passed, 20.11 s | 4 passed, 7.13 s |
| Sampled actual xUnit application process peak RSS | 1,237,304 KiB | 267,148 KiB |
| Kernel child maximum RSS | 1,236,224 KiB | 266,744 KiB |
| Unwrapped Info preparation, managed allocation delta | 6,163,074,600 bytes | 62,965,536 bytes |
| Wrapped Info preparation, managed allocation delta | 2,506,545,304 bytes | 25,555,360 bytes |

The relevant paths in the validation evidence are `test-results/viewer-info-font-diagnostic/` and `test-results/viewer-info-font-corrected/`, each containing its TRX and `process-memory.json`. The RSS samples and kernel child maximum use different measurement methods and are reported separately. The corrected run's cgroup OOM/OOM-kill counters did not change. Earlier incomplete or high-allocation exploratory runs are not final passing gates.

The retained diagnosis at `test-results/viewer-font-diagnosis/` contains the exact pinned upstream source/font hashes and MIT license. It traces per-character fallback against the headless font's minimal character coverage through repeated composite-alias bold synthesis and unmanaged font allocation. The predicted allocation arithmetic is explicitly an inference, corroborated by the measured preparation phase and the same-input correction; it is not a claim that a native Skia renderer was profiled. In this headless configuration the test font and drawing backend differ from native production fonts. The application-level layout/geometry caches also avoid repeated shaping and per-frame bounds allocation, and the long-report tests retain independent opening, unchanged-layout and repaint allocation guards.

## Native acceptance still required

Headless Avalonia tests compile against the pinned 12.1.1 implementation and inspect actual arranged client/control bounds and calculated glyph geometry, injected pointer/keyboard actions and peer event payloads. The default headless drawing/font implementation is used. These tests do not establish native pixels, a native screen-reader journey or every platform's font and clipboard backend behavior.

The remaining acceptance matrix is Windows UIA, macOS accessibility, and Linux's supported accessibility/backend combinations: traverse horizontal scroll controls; inspect a long Info line at each supported font/minimum width; edit both hex columns and return from toolbar/search/recovery; hear current selection, typing mode, pending nibble, unsaved and blocked state; and paste the exact tab/control/newline representations into a native recipient. Verify focus and scroll visibility under native font fallback and display scaling. Native clipboard replacement/write-completion behavior and NUL restrictions need backend-specific confirmation. The delivered code does not claim richer portable document-range or binary editing automation interfaces than the pinned toolkit provides.
