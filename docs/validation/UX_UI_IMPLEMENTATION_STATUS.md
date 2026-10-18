# Severity-ordered UX/UI audit implementation

Work starts with the 13 S1 findings in the supplied [76-finding audit](FileCat_UX_UI_Audit.md), then continues into the shared S2 dialog and viewer foundations. The implementation is organized by finding below. The second batch also resolves the original test failures and verifies ordinary-user permission behavior. Medium and low findings remain in the audit backlog unless explicitly mentioned here. Native desktop acceptance remains separate from the automated Linux/headless checks.

## Changes and verification map

| Audit finding | Implemented behavior | Regression coverage | Remaining acceptance |
| --- | --- | --- | --- |
| FCUX-001 — interrupted transfer intent | Journal a versioned, bounded snapshot of the destination, source mappings, resolved filter definitions, layout/name, verification, preservation, rate and queue choices. Continue shows that scope and uses conservative Skip. Legacy/incomplete records require a newly reviewed transfer. Existing source/staging/content guards remain in force. Filtered same-volume folder moves cannot bypass traversal through a rename shortcut. | `RecoverableTransferIntentTests`, `RecoveryTransferConflictTests`, `InterruptedTransferIntentUiTests`, existing `Interrupted*` tests, `MaskTests` | Native filesystem/volume behavior and actual process interruption. Large, legacy or unsupported records deliberately cannot promise equivalent continuation. |
| FCUX-069 — clipboard operation authority | Cut belongs to the current exact FileCat clipboard publication and complete ordinal resource identities. External/unverifiable publications import as Copy. Running/completed/partial moves cannot replay the original Cut; later publications retain their own lifecycle. | `ClipboardIntentTests`: controlled asynchronous publications plus actual source/destination manifests | Native clipboard exchange on each supported desktop backend. Native preferred/performed Move effects are outside the supported exchange contract. |
| FCUX-070 — delayed Paste destination | Capture destination and invoking tab before clipboard reads. File Paste uses that location or explains refusal; tab closure cancels it. Text navigation also respects newer navigation/Paste intent, including pending provider preparation. | `ClipboardIntentTests`: held reads, navigation, panel/tab movement, closure, source survival and actual request destinations | Native clipboard/backend timing. |
| FCUX-002 — range anchor identity | Bind range anchors to the listing store and item identity, resolve through the current view, and retire unavailable/foreign anchors. Reset gestures on focus/window deactivation. | `FileListSelectionTests`: real key/pointer events across sort, filter, tab and focus transitions | Native input-device and platform focus routing. |
| FCUX-003 — hidden marks in rename | Show hidden count and explicit unchecked inclusion control. Capture both scopes, regenerate counter/name mapping when scope changes, and reject stale external-editor results. Preserve marks and the original operation origin across tab navigation. | `BulkRenameScopeTests`, existing `BulkRenameDialogTests`: inclusion/exclusion, counters, navigation, actual filesystem results | Separate rename preview size/validation findings FCUX-038/039 remain open. Shared small-dialog layout is covered by FCUX-011 below. |
| FCUX-004 — background decisions/focus | Pending decisions remain in Operations with an explicit Review action. They cannot stack over unfinished user input. Skip is the safe initial operation-decision action; consequential generic confirmations focus Cancel. Recovery move, cleanup, and newly selected replacement policies use that safe default. Dialog focus/Enter callbacks belong to the current session; canceled/resolved decisions retire their dialog. Find's queued startup focus also yields to an active dialog, newer focus, deactivation or closure. | `BackgroundDecisionTests`, `DialogFocusOwnershipTests`, `FindInitialFocusTests`, recovery confirmation tests, existing `OverlayDialogLifetimeTests`, `FindRemainingScopeTests`, operation scope/center tests | Native focus, automation and scaled-layout acceptance. |
| FCUX-005 — synchronization identity | Keep separate complete, selectable Source and Target fields, full accessible identities, and target-specific Mirror wording synchronized with direction. | `SyncEndpointTests`: same-name endpoints, both directions, long paths and actual submit direction | Native layout at supported scaling; S2 plan-choice/group-cancellation lifecycle remains separate. |
| FCUX-018 — tracked Undo | Reserve one immutable inverse review atomically. Filesystem inverses become typed Undo jobs through normal scheduling/activity/close handling, with complete retained per-action reports, irreversible remainder, safe cancellation boundaries and unresolved rename journals. Registry inverses share the exclusive approval boundary and retain their guarded Registry job/issue path. Original-history removal cannot orphan a running inverse. | `UndoLifecycleTests`, `UndoSafetyTests`, `UndoLifecycleUiTests`, existing bulk rename/link/recovery tests | Native Windows identities/Recycle Bin, real process/power interruption, desktop report/clipboard/scaling checks. |
| FCUX-006 — Settings data preservation | Preserve untouched structured tool definitions. Share a lossless argument/field grammar with explicit empty tokens and quote errors. Keep all launch properties, exclusion masks, comment-marker names and complete submenu structures. Invalid edits stay in the same draft with field/line feedback; protected profiles are inspectable without edits or theme side effects. | `ToolTextRoundTripTests`, `ToolAssociationTests`, user command tests, `SettingsPreservationTests`, existing `SettingsDialogTests`; tests inspect the actual non-executing launch plan and persisted settings | A structured tool editor (FCUX-036) remains a separate S2 improvement. |
| FCUX-007 — Previous text search | Search the full declared preceding range using bounded reverse windows, encoding-aware context/overlap and strict progress. Continuation belongs to the query/source/encoding/mode and active content surface; a visible Stop action cancels owned work. | `ContentSearchBackwardTests`, `ViewerSearchScopeTests`, viewer/editor search lifetime tests: distant/chunk-boundary matches, UTF-16, malformed sequences, overlaps and caller ownership | Native input routing and provider-specific cancellation boundaries. |
| FCUX-008 — checksums | Review explicit whole-file or selected-byte scope, including one byte; retain complete selectable hashes and actual/expected counts. Copy is explicit. Stop, short reads, failures and revision evidence remain visible without overwriting the clipboard. | `ViewerChecksumScopeTests`, existing checksum lifetime/revision tests: scope, immutable request, narrow results, explicit Copy, cancellation, failures and revision changes | Native clipboard/layout; sources without strong revision evidence remain explicitly qualified. |
| FCUX-009 — concealed comparison text | Replace trimmed primary cells with a virtualized aligned text surface, synchronized horizontal scrolling and optional wrapping with matched pair heights. Difference navigation reveals the actual changed glyph. Complete lines up to the explicit shaping budget of 16,384 UTF-16 code units appear in the primary surface; exceptional lines have an explicit full-line action into paired, selectable bounded parts. Retire source associations, queued reveal ownership and text layouts on replacement/close. Keep exact equality and comparison computation independent. | `CompareTextViewTests`, `CompareLineDetailsTests`, `ComparisonReplacementRetirementTests`, existing comparison equality/source-change/navigation/lifetime cases. The width/length matrix checks the actual glyph viewport, not just source strings. | Native long-line, keyboard/pointer, font fallback and automation acceptance. Full-document selection/search remains a larger content-surface target; complete source lines remain inspectable and selectable through their detail action. |
| FCUX-010 — file list accessibility | Provide bounded virtual item peers with stable identity, focus versus mark semantics, selection/action/scroll/bounds support and coherent cached events. Provide a complete paged marked-item review, including hidden/offscreen marks and original locations for result-set items, and avoid synchronous metadata I/O in accessibility queries. | `FileListAutomationTests`, `FileListSelectionTests`, `MarkPagingTests`, `MetadataPeekTests`, updated `AccessibilityTests` | **Native acceptance is open.** Screen-reader/automation bridges need testing on Windows/macOS/Linux. Large selection uses the explicitly explained paged review rather than constructing unbounded native peers. |

### Related corrections included

FCUX-034 validation-session preservation and FCUX-035 protected-profile wording/editability are addressed in Settings because they are part of the safe-save path. The clipboard and drag branches of FCUX-071 both refuse incomplete mixed-provider export. These adjacent corrections do not close the remaining S2 findings by implication.

## S2 continuation

| Audit finding | Implemented behavior | Regression coverage and remaining acceptance |
| --- | --- | --- |
| FCUX-011 — dialog reachability | Reserve header/footer space and scroll the body within the actual host client area. Adapt forms, Settings pages, bulk-rename fields and secondary actions without replacing controls or drafts. Reveal the exact focused caret through nested scrolling. Keep complete proposed conflict names in selectable body text, escaping invisible characters through the existing filename display contract. | 21 `DialogViewportTests` at the 640 × 420 DIP minimum and three font sizes; all 173 affected dialog, Settings, operation-scope, focus and lifetime cases pass together. Native scaling/font/input/assistive-technology checks remain open. |
| FCUX-012 — viewer geometry and selection | Store selection as source byte boundaries with decoded-source/display mapping. Preserve that identity through wrapping, resize and font changes. Give Text/Info and Hex horizontal access and reveal the current source glyph/byte on both axes. Explicitly retire incompatible source/encoding selections. Keep source segments, highlighting and text layout work bounded. | `TextSourceMappingTests`, `ViewerGeometryAndSemanticsTests`, plus existing wrapping/search/checksum/lifetime cases. Tests cover supported minimum/wide widths, 13/18/24/32 fonts, wide byte offsets, reflow, source replacement and resource ownership. Native font and input acceptance remains open. |
| FCUX-013 — theme state contrast | Give destructive buttons a stable danger fill/text pair in normal, hover and pressed states. Keep hidden/system/unavailable names readable on the actual panel while retaining their dimmed icon cue. Draw selected and search-hit glyphs in paired foregrounds in Text and Hex views; derive changed/diff fills and text from each palette, including current difference variants. Binary differences retain underlines and the current outline; text comparison already has changed-span underlines and +/− line signs. | `ThemePickerTests` checks all seven palettes plus the current System resolution, 4.5:1 essential text pairs, 3:1 focus boundary and live danger button states. `SelectedGlyphContrastTests` checks committed selected/search glyph layouts, hex brush resolution and the list's actionable dim brush. The 124-case Windows theme/viewer/comparison/list regression selection passes. Native rendered-pixel, grayscale, scaling and assistive-technology acceptance remain open. |
| FCUX-014 — safe extension cells | Apply the safe filename display policy to the actual Name and split Extension cell presentation, result-set Folder/Kind/Details cells, and drive labels in the list/status. Hovering a name with hidden characters gives the complete safe filename and a spoken description; focus status and the accessible row description retain that evidence. Raw entry names, extension sort keys and operation identities are unchanged. | `FileListNamePresentationTests` exercises Details, Full, Brief and custom cell profiles, provider-derived cells, layout controls, valid Unicode, the actual row tooltip/focus status, accessible descriptions and raw identity. The 71-case Windows list/profile/automation/accessibility/formatter selection passes. Native glyph rendering and screen-reader speech remain open. |
| FCUX-071 — complete selection export | The existing clipboard preflight preserves the previous publication on refusal. Drag now captures the complete marked scope, refuses mixed local/provider selections before transfer, stages supported archive-only files within the existing 100-item/64-MiB budget, and converts every requested path before beginning a native drag. Failed conversion, changed source tab, cancellation and unexpected provider errors retire unhanded staging; accepted transfers retain staged files for the existing sweep. Refusals state the full selected count and offer F5/F6. | `DragExportTests` exercises actual local/ZIP contents, origin marks, hidden marks, complete mixed refusal, unsupported sources, conversion failures, cancellation, handoff and the 5,000-item cap. `DragStagingTests` exercises staging quotas/refusals and cleanup, including unexpected provider failure. Native interapplication drag effects, cursor feedback and platform clipboard interoperability remain open. |
| FCUX-075 — truthful drop feedback | Hover offers Copy or Move only for a ready filesystem target with transfer capability. Drop rechecks that same tab/location, rejects incomplete or nonlocal file payloads as a whole, and verifies the folder still exists before activating the panel or opening review. Unsupported and changed targets explain how to choose a local destination. An accepted review uses the captured destination even if its panel later navigates. | `DropTargetAdmissionTests` covers local effects, archive/Registry/working-set refusal, failed/disconnected local listing, changed or vanished target without panel activation, complete payload admission, and an actual reviewed copy to the captured folder. Native drag cursor/effect and cross-application interoperation remain open. |
| FCUX-015 — live selected-job issues | Bind the drawer to a stable observable issue list. The selected job's new retained issues append in 256-item UI batches, while its selection and scroll remain in place. Job change dispatches coalesce, and progress polling checks only issue count. Opening or selecting a job reconciles its current retained report; completion catches up with issues added between dispatches. | `OperationCenterTests` observes the actual bound ListBox through two late conflict issues, preserved issue selection, no collection reset on progress refresh, final count, reopening, a 270-issue bounded initial load, and scroll retention during append. `JobIssueSliceTests` checks exact bounded thread-safe slices. Native rendering and assistive-technology acceptance remain open. |
| FCUX-076 — Copy representation and completeness | Separate source text from displayed text, preserve original decoded tabs and logical line endings, and capture explicit selection/visible scope. Show retained selectable evidence for the one-MiB budget, partial/short reads and failures. Serialize owned clipboard publications, reject changed-source evidence and keep cancellation limits truthful. Preserve the report's complete original Copy all route. | `ViewerSourceCopyTests` and source-mapping/geometry cases cover multibyte boundaries, giant-line continuations, control characters, caps, short reads, delayed writes, cancellation, source revisions and explicit scope transitions. Native clipboard interoperability remains open. |
| FCUX-072 — binary comparison byte ranges | Alt+B and a labelled action select an exact left or right source interval from decimal or hexadecimal offset and decimal count. Invalid, overflowing, out-of-range and stale requests keep the draft or retire; aligned gaps never become bytes. A bounded selectable preview and read-only document value expose side, source, offset, extent and copied representation. Copy belongs to its current source/selection, checks available revisions, serializes clipboard publication, and keeps the existing 4 MiB spaced-hex/text contract. | `HexCompareRangeTests` and existing comparison/accessibility tests: 79 Windows cases pass together, including 24 focused comparison cases. Native screen-reader, clipboard and 640 × 320 DIP scaling acceptance remain open. |
| FCUX-073 — viewer semantics | Publish semantic values after the current content state commits. Include source-selection extent, loading/read failures, Hex position/column and actual editing state. Retire old request/source ownership and avoid synchronous source I/O from peer queries. | `ViewerGeometryAndSemanticsTests`, `HexEditorSemanticsTests` and existing accessibility/lifetime coverage. The descriptive value stays read-only rather than promising an unsupported binary SetValue API. Native automation/screen-reader bridges remain open. |

The three viewer findings share source mapping and content ownership and are
implemented together. The [viewer notes](UX_VIEWER_SOURCE_GEOMETRY.md),
[dialog notes](UX_DIALOG_VIEWPORTS.md) and [comparison notes](UX_COMPARE_LINE_DETAILS.md)
describe the contracts and their precise limits.

## Test results

The [current execution report](UX_UI_BATCH2_TEST_RESULTS.md) and [machine-readable results](UX_UI_BATCH2_TEST_RESULTS.json) distinguish original-source and first-batch baselines, focused tests, complete regression runs, native skips, diagnostic failures and their corrections. The [first-batch report](UX_UI_TEST_RESULTS.md) remains unchanged as historical execution evidence. The source builds use the repository's pinned .NET SDK **10.0.101**, with locked package restore. No dependency versions or lock files were upgraded.

The four final complete suites contain **9,616 cases: 8,960 passed, zero failed
and 656 explicitly skipped**. All bind to the same successful solution build,
with unchanged source/runtime inventories before and after execution and actual
zero exits. All 531 Core/App cases added since the original sources passed;
no baseline cases were removed. The seven runner/evidence/packaging Python groups
also passed all 227 checks. Native prerequisites and the exact skip reasons stay
visible in the execution report.

The original 31 App failures pass under a verified ordinary Linux identity with
worker privilege refusal and journal permission checks intact. The Core device
assertion now verifies the existing early non-regular-file refusal, with stronger
regular-file budget coverage. Ordinary-user execution also exposed and fixed the
valid-XDG-data-directory/no-profile trash case. See the [runner, security probes
and resource configuration](LINUX_UNPRIVILEGED_TESTS.md) for reproducible evidence.
Successful automated execution does not substitute for native desktop acceptance.

### Windows checkout checkpoint — 18 October 2026

The current checkout builds with .NET 10.0.101 in Release with zero warnings and
errors. A focused Windows run passes all 11 App cases covering the four failures
found during a broader App attempt: culture-sensitive number formatting,
realization of the archive folder prompt, and the deliberate Operations review
route for a background decision. A focused Core run passes all 203 cases for the
changed recovery, Undo, search, selection and settings models. The Core fixture
uses normalized path identities for a hard-link conflict and an Undo assertion.

The complete Windows App and Core attempts both stopped after their test hosts
became idle; neither is recorded as a passing complete suite. After FCUX-013,
a focused 124-case Windows theme, viewer, comparison and list selection run
passed with zero failures. After FCUX-014, 71 affected Windows list, profile,
automation, accessibility and formatter cases passed. After FCUX-071,
59 affected Windows App cases passed with one platform-specific skip, and all
6 focused Core staging cases passed. After FCUX-075, all 5 focused Windows
drop-target App cases passed; the combined drop, drag, operation-scope and
clipboard selection passed 54 with one platform-specific skip. After FCUX-015,
22 affected Windows App cases passed with one platform-specific
skip, and both focused Core issue/pace cases passed. The earlier
complete Linux suite results remain in the linked execution reports. Windows
native interaction and assistive-technology acceptance remain open.

## Reproduction

From the solution directory, with the pinned SDK installed:

```sh
dotnet restore FileCat.slnx --locked-mode
dotnet build FileCat.slnx -c Release --no-restore
dotnet test tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/FileCat.App.Tests/FileCat.App.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/FileCat.Remote.Tests/FileCat.Remote.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/FileCat.Platform.Windows.Tests/FileCat.Platform.Windows.Tests.csproj -c Release --no-build --no-restore
```

Use an ordinary desktop user for platform acceptance and picture-worker tests.
For the full suites on a runner with a 1.5 GiB managed-heap cap, add
`--settings docs/validation/ux-resource-bounded.runsettings`. This limits overlap
between xUnit collections while retaining tests' internal concurrency and all
original inputs/assertions. The original four-thread configuration also remains
available. The [Linux runner instructions](LINUX_UNPRIVILEGED_TESTS.md) describe
how to execute already-built assemblies safely from a root-only container without
changing system accounts or checkout permissions.

## Review notes and next work

- The [clipboard/search/checksum notes](UX_CLIPBOARD_CONTENT_FIXES.md) describe ownership, test coverage and native boundaries in detail.
- The [recovery/Undo notes](UX_RECOVERY_UNDO.md) describe persisted transfer intent, inverse admission, retained reports, cancellation and journal safety boundaries.
- The [selection/accessibility notes](UX_SELECTION_ACCESSIBILITY.md) describe the virtual peer and complete-selection review contract and native checks.
- The [comparison notes](UX_COMPARE_LINE_DETAILS.md) explain the virtualized primary surface, complete-line detail, shaping budgets and replacement ownership.
- The [viewer continuation notes](UX_VIEWER_SOURCE_GEOMETRY.md) explain source-relative selection, copy completeness, semantic updates and resource gates.
- Close the native acceptance gates before declaring all S1 acceptance complete.
- Resume FCUX-016 operation status, then the remaining findings in the audit's physical S2 order, preserving their individual acceptance criteria and adding regression tests for each correction. The session workflow records the subsequent queue and adjacent scopes that remain open.

The [session workflow](UX_UI_SESSION_WORKFLOW.md) records what the user's next
“continue” means: resolve any outstanding validation failures, then resume the
highest-severity unfinished work with regression tests. Delivery uses one current
`FileCat-UX-changes.zip`, containing all cumulative added and modified files from
the original attached sources, including earlier session fixes. Unchanged source
files are omitted. The ZIP includes application instructions, explicit deletion
accounting and retained test evidence. The optional patch uses that same original
baseline. Superseded generated ZIPs are removed after the replacement is verified.
