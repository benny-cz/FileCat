# Selection ownership and file-list accessibility

This note covers audit findings **FCUX-002** and **FCUX-010**. The original implementation used Avalonia **12.1.1**; the current Git build pins **12.1.4** to repair the Windows native selected-array bridge. It records the implemented behavior, test locations, and the native acceptance work that remains necessary. The [authoritative ledger](UX_UI_ACCEPTANCE_LEDGER.md#ac-s1-010) names the one remaining delayed Ubuntu provider route; the Windows archive is [accepted with an exact receipt](UX_UI_IMPLEMENTATION_STATUS.md#evidence-archive-uia-20261027).

## FCUX-002: range selection contract

A mouse range anchor consists of an `EntryStore` instance and an index within that store. The current visible position is resolved when the user extends a range. Sorting, filtering that preserves the anchor, metadata updates, and streaming additional entries cannot transfer the anchor to a different item merely because its row number changed.

The anchor is intentionally retired when its item becomes hidden, its store is replaced by refresh, or the list changes tabs. A later Shift-click starts from the current visible focused item and captures that fallback identity for subsequent Shift-clicks. Revealing an earlier hidden item does not reactivate an old anchor. Tabs keep their existing persistent marks; the list does not copy marks between tabs.

Commander keyboard behavior is preserved: Shift with movement marks or unmarks the rows traversed according to the first row's mark state. Releasing Shift, losing keyboard focus, native window deactivation, changing tabs, or replacing the store ends that gesture. A pointer gesture starts fresh keyboard gesture state. Ctrl-click still toggles a mark without replacing an established mouse anchor.

Automated regressions are in `tests/FileCat.App.Tests/FileListSelectionTests.cs`. They use actual headless pointer and keyboard input, asynchronous completed reorders, two already loaded tabs, streaming, refresh, and focus/window changes. Native Alt-Tab behavior still belongs in the platform acceptance procedure below.

## FCUX-010: implemented semantic model

The renderer remains virtualized. The accessibility model supplies `ListItem` peers for the visible window and the focused item, with identities bound to the listing's actual store. Peers do not create Avalonia controls for every file. The visible peer window is capped at 256; the list's peer cache is capped at 769. An automation client retaining an older peer can read its former safe name, but a retired store/tab or a detached visual host cannot be marked, focused, or invoked through that peer. A detached host exposes no active selection, even when the underlying tab remains alive.

The list's accessible name identifies its panel number, source/target role, and location. Item names remain stable names; provider-correct types, current position, marks, logical cursor focus, hidden status, cloud/offline state, unavailable state, and already cached verification appear in state/help channels. Parent navigation is labeled as navigation and does not expose a selectable-item pattern. Raw resource names remain untouched; accessible names use the same safe-display escaping as the UI.

Item actions expose selection addition/removal, atomic replacement of the marked set, invocation, focus, and bring-into-view through the supported Avalonia peer APIs. Selection and keyboard focus remain separate. Invocation revalidates listing ownership through activation and focus callbacks so a tab change cannot redirect it to another file. Row bounds are clipped to the same content viewport as drawing, including partially visible rows and excluding the scrollbar.

The list exposes vertical scroll state and actions. Invalid percentages and unsupported horizontal requests fail explicitly. Scrolling does not change the focused item or the operation's selected set.

Changes are coalesced on the dispatcher. Genuine changes use the appropriate Name, HelpText, Value, ItemStatus, IsSelected, bounds, scroll, and selection properties; repeated unchanged background updates and a mark toggled back before publication do not manufacture announcements. Refresh retains the old selection snapshot long enough to report its actual removal. The current-item Value retains the earlier screen-reader description path while the list name remains stable.

### Complete selection and native array limits

For **up to 512 marks**, `ISelectionProvider.GetSelection()` returns the complete marked set, including offscreen and filtered marks. With no marks it returns an empty set, even when commands would use the focused fallback item.

For **more than 512 marks**, this method throws an explicit `NotSupportedException` directing the caller to **Review marked items**. It never returns a truncated array or represents the focus fallback as marked. Changes involving an unsupported full array invalidate child discovery and update the selection summary; they do not fabricate null/empty selected-array property values. This is an intentional bridge limitation: Avalonia exposes the selection as a collection of peers that native bridges may materialize into one complete array. Returning a nominally lazy C# collection alone does not bound that native operation.

The full review is available from **Mark → Review marked items…**, the command palette and keyboard reference, and the default **Ctrl+Alt+M** binding. Find windows expose the same command in their **Edit** menu. User-remapped bindings continue to apply through the central command registry.

The review materializes **64 records per page**, using an extra index only to determine whether a next page exists. Previous/Next and a full selectable detail field are ordinary accessible controls. The summary gives the complete mark count and hidden-mark count. Hidden marks remain individually inspectable. Navigating the review never changes the marks. A changed selection restarts the review at the first page; a changed store retires the displayed review and explicitly asks the user to reopen it.

Search and working-set entries retain their original locations in peer help and review labels. Equal names from different folders, repeated archive members, and equal remote paths in different profile/view identities remain distinguishable from cached `ResultTag` data. The review also describes offline, unavailable, protected and link states already present in each listing record.

Bitset paging allocates no complete selection array. Accessibility inspection can read existing listing records and cached metadata; it does not resolve provider `ItemRef`s or initiate file-content/verification production. `MetadataService.Peek` is the explicit cache-only read API.

### Automated coverage

| Test file | Contracts covered |
| --- | --- |
| `tests/FileCat.App.Tests/FileListSelectionTests.cs` | Preloaded tabs, identity after sorting/filtering/reveal, refresh creation/deletion, streaming, Ctrl-click, Commander Shift transitions, focus and window release. |
| `tests/FileCat.App.Tests/FileListAutomationTests.cs` | Stable panel identity, item role/patterns, focus versus marks, complete small selections, hidden/offscreen actions, current bounds, reordering, stale-peer and detached-host rejection, activation ownership races, atomic selection replacement, loading/empty/filter/partial/failure states, provider nouns, exact original identity for same-name result entries, event values/coalescing, scroll validation, paged keyboard traversal, million-item budgets, and the registered command through the actual main window. |
| `tests/FileCat.Core.Tests/MarkPagingTests.cs` | Sparse and word-boundary marks, complete nonduplicating pages, invalid/terminal cursors, cleared state, and allocation-free page reads over a million marks. |
| `tests/FileCat.Core.Tests/MetadataPeekTests.cs` | Cache-only access neither starts production nor returns a different entry revision's cached value. |
| `tests/FileCat.App.Tests/AccessibilityTests.cs` | Existing application/dialog naming audit plus the stable list name, current-item Value, and selected item peer. |

Run focused checks from the repository root after the pinned locked restore:

```sh
dotnet test tests/FileCat.App.Tests/FileCat.App.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~FileListSelectionTests|FullyQualifiedName~FileListAutomationTests|FullyQualifiedName~AccessibilityTests'
dotnet test tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~MarkPagingTests|FullyQualifiedName~MetadataPeekTests'
```

### Recorded execution — 17 October 2026

The final coordinated Release gate against the pinned SDK and Avalonia 12.1.1 completed **114 tests** in 39 seconds on the Linux headless host: **112 passed, zero failed, and two platform-specific Windows drive-letter tests were skipped**. All **31 selection/accessibility tests passed**: 10 real-input range-selection cases, 17 file-list automation cases, and 4 existing accessibility cases. These include rejection of actions from a detached visual host and distinct original identities for same-name search/working-set items. The group also passed 67 comparison cases, eight Find scope/focus cases, and six Working Set/menu integration cases, including the Review marked items menu icon invariant. Result artifact: `final-selection-compare-find-integration.trx`.

The focused Core run passed **6 of 6 tests**, with **zero skipped**: 5 mark-paging cases and 1 cache-only metadata case. Its recorded result file was `selection-accessibility-core.trx`; the same six Core cases also passed in the complete Core run. These headless results do not constitute native screen-reader acceptance.

## Historical native acceptance checklist

This original broad checklist is preserved for context. The
[acceptance ledger](UX_UI_ACCEPTANCE_LEDGER.md#ac-s1-010) records the finite
current requirement and the exact accepted native subcases; its scope and
stopping rules govern further testing.

Headless tests and compiling against the pinned assembly do **not** establish screen-reader compatibility. The custom renderer remains the physical keyboard-focus owner. Virtual rows expose the logical cursor through peer getters and state changes, and the container supplies the current-item Value; the pinned toolkit does not offer a public arbitrary-peer focus-event raiser. The exact native event/announcement behavior and accessibility root's focused-element routing must therefore be checked before FCUX-010 can be declared fully resolved.

Use at least one supported Windows screen reader (NVDA or Narrator), VoiceOver on macOS, and Orca on the supported Linux desktop/backend. Record the OS, backend/session type, screen-reader version, FileCat build, font size, scaling, and whether results came from an inspector or actual speech/braille output.

1. Open two panels showing identically named files in different full paths. Verify that each panel can be distinguished by number, role, and location. Change the active source and target and verify that the role changes without turning the current filename into the container's name.
2. Move through parent navigation, folders, files, drives, and Registry keys/values where supported. Verify names, types, logical focus, invocation, and the absence of a selectable mark on parent navigation. Confirm the actual focused native element and focus announcement route; a nonempty property string is insufficient.
3. Mark one item, an offscreen item, and an item subsequently hidden by filtering. Verify that the standard selection query returns all three and that focus remains independent. Unmark the hidden item through its selection peer and verify the resulting operation scope.
4. Sort, filter, refresh, navigate, and switch between two already loaded tabs. Verify peer identities/bounds and stale-item rejection. Repeat the Shift-click anchor scenarios with a late row in a 100-item tab and an early row in a 10-item tab.
5. Hold Shift while moving, switch to another native application, release Shift there, return, and start another Shift gesture. Verify that the new gesture derives its mark/unmark intent from the current item.
6. Exercise empty, no filter matches, a held loading provider, stopped partial listing, permission failure, and a failed refresh retaining previous rows. Verify the spoken distinction and the user's ability to reach the indicated Refresh, filter, and Location controls.
7. Mark more than 512 items. Confirm that the native inspector handles the explicit array limitation without freezing or silently displaying a partial selection. Use Review marked items by keyboard, inspect hidden items on later pages, return to previous pages, read/copy the full detail text, and close with Escape. Check that the actual marks did not change.
8. Use the existing synthetic benchmark facility for a million-item listing (`docs/validation/TV-01.md`). With an accessibility client attached, query children, change focus, scroll, mark all, and open the review. Measure UI responsiveness, managed/native memory, peer retention, and announcement rate. A passing ordinary rendering benchmark alone does not satisfy this gate.
9. Repeat the core journey at enlarged fonts and supported scaling, including 125%, 150%, and 200%, a small supported client area, high contrast, and keyboard-only operation. Verify that partial row bounds and the review's navigation/footer remain useful.

Record new native failures against the ledger with their exact tested scope; headless tests alone do not close an untested native route. Selected arrays above the documented bridge bound, genuine virtual-item focus events, and platform-specific discovery deficiencies remain possible follow-up work only if a required scenario or new regression demonstrates them.
