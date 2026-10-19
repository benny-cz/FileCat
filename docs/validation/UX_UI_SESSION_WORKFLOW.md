# FileCat UX/UI implementation session workflow

This file records the user's continuation and Git delivery instructions for this
implementation session. The original [UX/UI audit](FileCat_UX_UI_Audit.md) retains
its findings; the [implementation status](UX_UI_IMPLEMENTATION_STATUS.md) records
what has been implemented, verified and left open.

## What “continue” means

Resume this plan without asking the user to repeat it. Resolve any failing
validation gate from the preceding iteration, then implement the highest-severity
unfinished finding. Complete shared prerequisites together when separating them
would leave inconsistent ownership, safety or user-visible behavior. Keep each
audit finding's scope and acceptance criteria explicit.

Preserve the earlier session changes. Add regression tests for every implemented
fix, including the relevant refusal, cancellation, stale-state, accessibility,
resource-lifetime and boundary cases. Test real outcomes and retain meaningful
existing assertions. Run the affected tests first and the applicable complete
regression suites before delivery. A failed or incomplete process is diagnostic
evidence, not a passing gate. Native prerequisites and unexecuted desktop
acceptance remain visible and cannot be inferred from headless tests.

Update the status and execution report after each iteration. Record concrete
remaining work so the next “continue” resumes it. Work proceeds until all audit
findings and their required acceptance checks are complete; a successful batch
does not imply that the whole audit has been closed.

### Implementation history and remaining acceptance

As of 19 October 2026, code and regression coverage for all 76 audit findings
and eleven queued bugs are present. The [most recent fully green source CI run](https://github.com/benny-cz/FileCat/actions/runs/39290466271)
passes producer checks and all four platform lanes. An earlier Linux run exposed
that WebKitGTK reports a directional count, rather than the original total,
on Next/Previous; commit `ddbc444b` caches the initial count, and its Linux
lane passed. The viewer-geometry commit `8578a7d3` passed the complete
local App suite (2,348 passed, 63 skipped). Its CI passed Ubuntu, macOS and
Windows ARM64; the Windows lane had one timeout in an existing 70-step Undo
lifecycle test, while its other test projects passed. The same case and the
complete Core suite pass locally (4,428 passed, 150 skipped). The bulk case now
uses a bounded 60-second deadline with monotonic timing and progress details;
all 14 focused Undo lifecycle cases pass.
Continue with the native desktop, provider, scaling, clipboard
and assistive-technology acceptance named
for each finding in the implementation status. Page-mode Find now exposes the
WebKitGTK count when complete and labels its 1,024-match limit; Windows and
macOS still show a directional lower bound. The queue below records the order
in which code changes landed. After a Codex restart, native Windows Computer Use
became available. A live WebView2 Markdown viewer showed the Find toolbar,
Previous/Next and the truthful Windows lower-bound status; expanding the window
also expanded prose and table columns. F5 refreshed a changed Markdown file
and cleared its visible changed-file banner. Narrow-width native pointer acceptance
remains open. A maximize/restore/reopen check exposed a transient-size persistence
bug. A second live run showed the large size can arrive before the window-state
notification. Debounced capture now discards that pending size; the revised
Windows build preserved a 1,592 × 1,414 viewer through maximize, restore,
close and reopen, with its saved dimensions unchanged after exit. Native Windows
pointer checks expanded and shrank Registry Find, Advanced criteria and
Ignored folders. The latter two kept their inputs reachable by scrolling at
minimum size. A temporary NTFS stream was removed through FileCat's Delete
command and confirmed absent on disk; its parent's Streams cell was blank as
soon as the user returned to the folder, without a restart.
The latest build also ran in a Windows 11 VMware guest without taking over the
host desktop. A guest-local pointer test expanded Find files from 1,056 × 759
to 1,226 × 879 pixels and restored it exactly; the same test passed with
Advanced criteria open, and its checked inputs/actions remained visible in the
native UI Automation tree. The reusable runner lives in
`tests/FileCat.NativeAcceptance`. The `684a9e7e` CI run passed producer checks
and all four platform lanes.
The same Windows 11 guest then passed FCUX-036 editor Browse/Cancel and
spaced-path preview checks. Selecting the existing .NET executable through the
Windows picker showed its full path and a spaced sample filename in the
nonexecuting preview. Canceling Settings restored the original editor value
when reopened. A guest keyboard pass then moved Tab from the program field to
Browse, opened the picker with Enter, closed it with Escape and retained
Settings. Broader native keyboard, scaling and assistive-technology checks
remain open.

The same guest passed repeatable Markdown Page acceptance against the tracked
implementation-status file. Find, Next and Previous exposed enabled controls
and a truthful “At least 1 match · total unavailable in Page mode” status.
Dragging the viewer from 996 × 739 to 1,346 × 919 pixels widened the table
from 916 to 1,251 pixels and reduced the height of the preceding prose block
from 118 to 93 pixels as it reflowed. The runner restores the original bounds.
A second guest pointer drag narrowed the viewer from 996 to 696 pixels. The
document stayed within the window at 680 pixels, the keyboard-focusable table
scroll region contracted from 901 to 601 pixels around 916 pixels of table
content, and the prose block grew from 118 to 144 pixels. Native scaling and
assistive-technology checks remain open.

Native Windows inspection found a real FCUX-010 bridge gap after the headless
tests: visible file-row peers had zero screen rectangles. Cached peers now own
bounded attached controls, which let Avalonia convert logical row geometry to
screen coordinates. The live `accessibility-list` runner verified positive
rectangles for both source files, stable distinct IDs, an empty destination,
and a selected `a.txt` while focus moved to unselected `b.txt`. The complete
local App suite passed with 16 workers. `9495910c` was pushed to main; its
cross-platform CI run is pending. The same guest passed `breadcrumb-native`
twice: Alt+Up focused the nearest full-path ancestor, Right exposed Open here,
Open in new tab and Copy path with that target, and Escape returned to the
source list. It also passed `panel-resize-native` twice: command search focused
the named splitter, Right moved it eight physical pixels and updated the
announced share from 50 to 51 percent, and Escape restored its exact rectangle
and list focus. Those runner additions were pushed in `bacbfa39` and
`b80cfc71`; their CI runs are pending. Native spoken screen-reader behavior,
non-Windows desktop bridges and scaled-display acceptance remain open.

The guest also passed FCUX-009's long-line comparison on a disposable pair of
files with 2,000-character common prefixes and different suffixes. The native
`compare-native` runner queried both complete source values for the first and
second changed rows, used Next difference, copied the exact left source line
without its number, and verified that Wrap left the selected changed row in
the viewport. A captured VM desktop view showed both suffixes in the primary
aligned text surface. The strengthened runner passed again after a fresh app
launch. Font fallback, pointer selection and spoken/cross-platform acceptance
remain open; [the live remaining-work list](UX_UI_REMAINING.md) tracks the next
gates.

The guest also ran a native FCUX-001 recovery review in an isolated `--data`
instance. FileCat Core's journal writer seeded a begin-only filtered move with
`*.jpg`, read-back verification and Queue intent. Startup showed one
interrupted operation. The review retained the original mapping and choices
and explained the conservative Skip policy. Enter on the default Cancel kept
the journal and both source files; approving a second review moved the JPG to
the target while the excluded TXT stayed at source. A further guest run killed
the test process after its Core worker recorded copied bytes. FileCat then
recognized the durable journal and one partial staging file, preserved the
filter, verification, rate and queue choices in review, and completed the
8 MiB JPG move while leaving TXT at source and clearing staging. A separate
guest fixture terminated FileCat itself during a 32 MiB copy. The retained
staging file contained a nonzero source marker, the journal preserved the
64 KiB/s choice, and the target was unpublished. Relaunch showed the
partial-file cleanup and Skip policy; Continue reproduced the full source
content, retained the copy source, and removed staging. Cross-volume moves
and other providers still need native acceptance.

A separate incomplete-journal guest fixture followed the legacy path. It
offered a newly reviewed copy with explicit missing-intent text and the chosen
source-to-target mapping. Cancel kept the journal, source and empty target;
approving a second new-copy review placed `approved.txt` at the selected
destination while preserving `draft.txt` at source.

UXBUG-010 now highlights the action and key transfer/cleanup choices at the
top of these dense confirmations, with the complete selectable review below.
Thirteen focused App cases pass; the Windows 11 VM showed the native summary,
the `draft.txt` → `approved.txt` mapping, the full-review heading and approval
control. The `legacy-highlight` runner mode asserts those visible positions.

After resolving any outstanding validation failure, follow the audit's physical
S2 order; its permanent IDs are references, not a numeric priority sort. The
FCUX-036 and UXBUG-002 still need native scaling and assistive-technology
acceptance after their guest interaction checks. The completed
FCUX-011/012/013/014/076/072 and viewer FCUX-073 code scopes also keep their
native acceptance gates open. The user confirmed the PE viewer scrollbar-drag
fix on the supplied Windows EXE. FCUX-042, the newly reported Markdown resize
correction, FCUX-043, FCUX-044, FCUX-045, FCUX-046, and FCUX-047 are implemented
with focused coverage. The drop test now waits for asynchronous destination
validation. The interrupted-review section opens expanded. The complete App
Release suite passes (2,232 passed, 63 skipped); the complete Core suite passes
with 16 workers (4,420 passed, 150 skipped). FCUX-047 then passed its complete
App rerun (2,238 passed, 63 skipped). UXBUG-005 viewer geometry persistence
passes two focused and 27 related App cases, plus 17 Core state cases.
UXBUG-006 then passes its real Delete-key and parent-column regression, plus
the related 89-pass, 16-skip App selection. UXBUG-007 resizable Registry,
Advanced criteria, and Ignored folders dialogs pass 72 related App cases and
the full App suite passes 2,242 with 63 skips. Its native pointer and scaling
acceptance remains open. UXBUG-008 viewer Find controls and bounded counts
pass 240 related App and 111 related Core cases. Its full Core rerun passes
4,427 with 150 skips using 16 workers. Native acceptance and the exact
Page-mode total remain open. UXBUG-009's selected active job and visible row
pass 44 related App cases. Its native acceptance remains open. FCUX-048's
folder-background menu passes five focused and 63 related App cases with one
platform skip. Native location-menu acceptance remains open. FCUX-049's open
viewer palette and event-lifetime correction passes two focused and 116 related
App cases; native acceptance remains open. FCUX-050's opaque functional
surfaces, removed content overlay and frozen disabled clock pass three focused
and 54 related App cases. Native animation, contrast and reduced-motion checks
remain open. FCUX-051 adds Windows, macOS and GNOME preference probes,
three-second active-theme refresh, and accurate unsupported-desktop wording.
Thirty-two related theme and Settings App cases pass; native cross-platform
preference acceptance remains open. FCUX-052 adds provider-aware Refresh/F5 for
the viewer, preserves the old snapshot until a replacement is ready, and keeps
the changed-file banner and error visible if reopening or Page loading fails.
Its nine focused App cases and 326 related viewer cases pass, with 12 platform
skips in the related set. Its complete App rerun passes 2,285 cases with 63
platform skips and zero failures. The complete App run through FCUX-055 passes 2,276 cases
with 63 platform skips. Native provider and page-engine acceptance remain open.
FCUX-053 adds a selection-owned loading state after debounce, Cancel/Retry,
visible full-viewer action, and explicit empty, unavailable, permission, size
and picture-decode explanations. Fourteen focused and 68 related cases pass.
Native Shell, remote and assistive-technology acceptance
remain open. FCUX-054 replaces the Viewer Go to window with the shared inline-
validated dialog. Four focused and 338 related cases pass, with 12 platform
skips in the related set. Native layout, input and remote capability acceptance remain open.
FCUX-056 adds text comparison source selection/copy and scoped Find, with 81
related App cases passing. FCUX-057 corrects automatic-update Privacy wording,
with 29 related cases passing and one platform skip. FCUX-058 adds a focused
toolbar, complete More menu, compact/pinnable Places and classic layout choices;
71 related cases pass, plus six focused model/persistence tests after the final
place-key refinement. Its complete App rerun is in progress; one old drive-menu
test needed an updated order assertion after the new Pin action, and the 28
related command-search/drive cases pass. FCUX-074 routes the effective Help
binding in command search, offers a named focusable Keyboard reference action,
and restores the search query, caret and selected result on dismissal. All 26
command-search cases pass, including remapped Help and keyboard activation.
FCUX-059 now adds Compact/Comfortable chrome density, separate saved content
size, file-list row spacing and visible content-size controls in viewer,
comparison and hex editor. Six focused cases pass, including 640-DIP primary
and Settings action reachability. Its related App run passed 365 cases with two
skips, and the pinned earlier FCUX-059 complete App snapshot passed 2,317 cases
with 63 skips and zero failures. Final density tokens were verified in focused
tests and will join the next full rerun. The complete Core run passed 4,427
cases with 150 platform skips.
FCUX-060 now groups viewer and
editor commands into primary rows, on-demand Find rows and complete More menus.
Its two focused hierarchy cases and six combined Page/Picture mode cases pass;
the wider viewer/editor rerun passed 342 cases with 14 platform skips. Its final
complete App rerun passed 2,320 cases with 63 platform skips. FCUX-061 now offers full safe values for
clipped list cells on hover and a read-only inspection dialog from the keyboard
or item menu. Six focused and 53 related list/profile cases pass, along with the
combined post-refinement 59-case selection. FCUX-062 now distinguishes same-named
local, archive and remote tabs with short parent cues; navigation locks and
return-to-root locks have distinct visible and accessible meanings. Its 39-case
tab/workspace/docking/navigation run passes. FCUX-063 now disables subordinate
Settings options with their parents while retaining the draft value, and makes
column-profile buttons reflect limits, required Name, duplicate choices and
first/last positions. The related selection passes 58 cases with one platform
skip. FCUX-064 now saves normal-window geometry separately from maximized state
and restores it on an available display; 33 related App cases pass. FCUX-065
preserves a valid one-panel workspace and asks for a destination on F5/F6;
43 related workspace/transfer cases pass. FCUX-066 now rejects the last-tab
close before touching recent history; 30 related App cases pass. FCUX-067 keeps
About support actions in the open dialog and opens precisely named state folders
in new tabs; 46 related App cases pass. FCUX-068 gates queue movement on real
queued order, explains disabled moves and protects synchronization plans;
51 related App and 28 Core cases pass with one Core skip. Native scaling,
window-manager, pointer and assistive-technology acceptance remain open.
Continue with remaining queued bugfix acceptance.

The first full App run after UXBUG-009 finished with 2,246 passes, 63 skips
and one interrupted-review accessibility assertion before two button bindings
settled. The isolated test passed; the assertion now waits up to 10 seconds
for the Loaded bindings and still reports any persistent unnamed controls.
The 59 related cases pass. The full App rerun through FCUX-049 passes with
2,254 passes, 63 skips and no failures. The full App rerun through FCUX-054
passes with 2,296 passes, 63 skips and no failures.

| Order | Remaining finding | First acceptance boundary |
| --- | --- | --- |
| 1 | FCUX-036 — advanced Settings editing | Structured/raw editors, previews and file import/export pass 30 related App cases with one platform skip. The Windows 11 VM passed editor picker Cancel, spaced-path preview, Settings draft restoration and the Tab/Enter/Escape Browse path. Broader native keyboard, scaling and assistive-technology acceptance remain open. |
| 2 | UXBUG-002 — Markdown Page search | Run native Linux WebKitGTK and macOS WKWebView search tests and desktop keyboard acceptance. Windows WebView2 and headless viewer behavior now pass. |
| 3 | FCUX-037 — toolbar availability | Code and focused tests pass; native screen-reader, pointer and rapid-focus acceptance remain open. |
| 4 | FCUX-038 — bulk rename review | Full virtualized row review and focused tests pass; native acceptance remains open. |
| 5 | FCUX-039 — rename validation responsiveness | Pure preparation, device-scheduled checks and current-generation tests pass; native timing acceptance remains open. |
| 6 | FCUX-040 — counter field validation | Exact field validation and focused App/Core tests pass; native acceptance remains open. |
| 7 | FCUX-041 — transfer destination validation | Code and focused tests pass; the complete App Release attempt aborted when its host exited after 124 passes and two skips. Native network/focus acceptance remains open. |
| 8 | UXBUG-003 — PE viewer scrollbar-drag freeze | Thumb seeks are coalesced and Info line counting is cached. Portable drag/value tests and a local headless check pass; the user confirmed native Windows thumb dragging on SignalSetup.exe works. |
| 9 | FCUX-042 — operations drawer layout | Bounded, persisted vertical resize; scrolling selected recovery review; full-window report entry; responsive graph and actions. Focused tests pass; native scaled desktop acceptance remains open. |
| 10 | UXBUG-004 — Markdown prose width on resize | Prose and tables now share available Page width. Forty-eight Markdown tests pass; a rendered Chrome check measured both at 505 px narrow and 1,441 px wide. Native WebView2 desktop acceptance remains open. |
| 11 | FCUX-043 — recovery preparation state | Reviewing, Awaiting approval, Applying and Submitted are visible. Stop before approval preserves the journal; Stop after a cleanup effect retains a report. Six focused cases pass; full App and native delayed-device acceptance remain open. |
| 12 | FCUX-044 — Find scope | Unsupported SFTP, FTP, archive, and virtual origins show no local root and require deliberate local selection. Presets name their source; result-set scope stays fixed. Twelve focused cases and a related 47-case Find/Registry selection pass; native acceptance remains open. |
| 13 | UXBUG-005 — viewer window size | Deliberate normal-size changes persist across restart and viewer modes; saved dimensions are validated and display-clamped. Three focused cases cover state-transition transients. Native Windows preserved the normal viewer size through maximize, restore, close and reopen; multi-monitor and DPI acceptance remain open. |
| 14 | UXBUG-006 — Streams count after deletion | Targeted metadata retirement makes the parent row requery its actual Streams/Attributes count after Delete, including partial failures. The physical-key App regression and 89 related cases pass. Native Windows removed a temporary NTFS stream via Delete and showed a blank Streams cell on the parent without restarting; Unix attribute acceptance remains open. |
| 15 | FCUX-045 — Find completion | Visible completion and qualification, plus search-specific empty states, are implemented; 98 related Release tests pass. Full App and native acceptance remain open. |
| 16 | UXBUG-007 — resizable search windows | Registry, Advanced criteria, and Ignored folders dialogs have a visible pointer/keyboard resize grip, responsive content, host bounds, and reopen behavior. Seventy-two related tests pass. Native Windows pointer checks expanded and shrank all three; Registry controls stayed visible, and Advanced criteria and Ignored folders inputs remained reachable by scrolling at minimum size. Scaling and assistive-technology acceptance remain open. |
| 17 | FCUX-046 — Find shortcut contexts | Stable contextual command IDs, current menu/hint gestures, overlap preview and focused tests pass; complete App Release run passes. Native keyboard acceptance remains open. |
| 18 | FCUX-047 — fractional scroll | Shared wheel-fraction policy and one-row paging fixes pass 15 focused App cases across the list and three viewer controls, plus 117 adjacent regressions. The complete App rerun passes; native device acceptance remains open. |
| 19 | UXBUG-008 — visible viewer Find results | Text, Hex and Info expose visible Previous/Next and bounded background match counts. Page shows WebKitGTK's rendered-page total when complete, a labelled lower bound past its limit, and a directional lower bound on Windows/macOS. The 240 related App and 111 Core cases cover zero, many, search boundaries, mode/query changes, cancellation, and keyboard/button parity; native GTK integration passed on Linux CI. Native Windows WebView2 controls/status passed; native macOS and exact Page totals on Windows/macOS remain open. |
| 20 | UXBUG-009 — Details selects the active operation | Reopening Details selects and reveals active work over a retained finished selection; open-drawer choices remain stable as jobs arrive. The eight-finished-job regression and a new genuinely Running job regression pass; the earlier 44 related App cases pass. Native pointer, keyboard, and screen-reader acceptance remain open. |
| 21 | FCUX-048 — listing background context menu | Whitespace right-click opens captured-location actions without targeting marked or focused files. Five focused and 63 related App cases pass with one platform skip. Native location-provider, pointer and keyboard acceptance remain open. |
| 22 | FCUX-049 — live viewer theme changes | Attached text and hex controls re-resolve semantic brushes on theme change and release static subscriptions on detach. Two focused and 116 related App cases pass; native theme and high-contrast acceptance remain open. |
| 23 | FCUX-050 — Psychedelic effect boundaries | The menu and work surfaces are opaque, decoration stays below content, focus/grid brushes stay fixed, and disabled animation freezes visual time. Three focused and 54 related App cases pass; native cycle/contrast/motion acceptance remains open. |
| 24 | FCUX-051 — desktop motion preference | Windows, macOS and GNOME motion probes refresh while an animated theme is active; startup/reactivation wait for the first preference read. Thirty-two related App cases pass. Native platform toggles, fallback and screen-reader acceptance remain open. |
| 25 | FCUX-055 — picture zoom state | Fit and 100% are separate controls; custom 50%, 125% and 200% display their percentage with neither mode checked. A small image keeps Fit and 100% distinct even when both render at the same scale. Twelve focused and 74 related App cases pass, with 12 platform skips. Native scale and assistive-technology acceptance remain open. This is a prerequisite for preserving zoom through FCUX-052 refresh. |
| 26 | FCUX-052 — refresh changed viewer content | Refresh/F5 reopens the original provider item and stages new Text, Info, Picture or Page content before retiring the old source. Stale and failed refresh states remain visible; zoom, scroll and safe selections survive. Nine focused and 326 related App cases pass, with 12 platform skips in the related set. Native archive/remote/WebView2 and assistive-technology acceptance remain open. |
| 27 | FCUX-053 — quick-view loading and fallback | Loading appears after a 140 ms debounce and 180 ms no-flicker interval with Cancel/Retry. A visible button opens the full viewer. Empty, denied, unsupported, oversized and failed picture states explain themselves while Hex remains available. Automatic picture pixels respond to pane size within a 1,024 physical-pixel cap. Fourteen focused and 68 related App cases pass. Native Shell and remote acceptance remain open. |
| 28 | FCUX-054 — Viewer Go to dialog | A bounded shared prompt keeps invalid drafts open with inline errors and visible Go/Cancel. Offset, percentage and line parsing rejects invalid or unsupported targets before action. Four focused and 338 related App cases pass, with 12 platform skips in the related set. Native scaling, remote and assistive-technology acceptance remain open. |
| 29 | FCUX-056 — compare selection and Find | Per-side source selection, bounded clipboard actions and scoped Find operate on aligned rows without UI line numbers. Eighty-one related App cases pass; native input and assistive-technology acceptance remain open. |
| 30 | FCUX-057 — update Privacy wording | The checkbox names automatic checks, while Help checks and requested network locations are explained separately. Twenty-nine related tests pass with one platform skip; native wording acceptance remains open. |
| 31 | FCUX-058 — command and Places hierarchy | Primary and More actions remain stable, classic toolbar and full Places are optional, and compact Places supports pinning without hiding locations from menus. Seventy-one related tests and six post-refinement focused cases pass; complete App rerun and native four/six-panel acceptance remain open. |
| 32 | FCUX-059 — readability preferences | Compact/Comfortable chrome density, separate list/content font sizes and visible viewer size controls pass six focused and 365 related cases with two skips. A pinned complete App snapshot passed; final density-token rerun and native acceptance remain open. |
| 33 | FCUX-060 — viewer/editor hierarchy | Stable mode and primary rows, on-demand Find and complete More menus pass two focused and 342 related viewer/editor cases with 14 skips. Complete App rerun passes 2,320 cases with 63 skips; native acceptance remains open. |
| 34 | FCUX-061 — clipped cell inspection | Full safe hover values and a keyboard/context-menu inspection dialog pass six focused, 53 related, and 59 combined post-refinement list/profile cases. Native acceptance remains open. |
| 35 | FCUX-062 — distinguishable tabs | Collision-aware parent/provider cues and distinct navigation/root lock labels pass 39 related tab/workspace/docking/navigation cases. Native width, speech and remote/archive acceptance remain open. |
| 36 | FCUX-063 — dependent controls | Indented subordinate Settings choices retain values while disabled; column-editor actions track the draft and explain disabled states. The combined Settings/column/dialog run passes 58 cases with one skip. Native accessibility acceptance remains open. |
| 37 | FCUX-064 — window restoration | Saved normal size and position survive maximization and are clamped or recentered on available displays. Three focused and 33 related App cases pass; native multi-monitor/DPI/window-manager acceptance remains open. |
| 38 | FCUX-065 — one-panel workspace | A valid one-panel state round trips without an extra panel; missing/unusable state still defaults to two, and invalid target IDs clear. The 43-case related selection passes; native docking/transfer acceptance remains open. |
| 39 | FCUX-066 — last-tab history | A rejected middle-click leaves real closed-tab history intact; command, context menu and middle-click use the same close rule after a tab move. Eight focused and 30 related App cases pass; native acceptance remains open. |
| 40 | FCUX-067 — About support actions | Copy and folder actions keep About open; settings and local-data labels open their displayed paths in new tabs, while equal paths use one action. 31 focused and 46 related App cases pass; native clipboard/assistive-technology acceptance remains open. |
| 41 | FCUX-068 — queue movement | Only queued jobs show Move earlier; the first and fixed sync steps explain why it is unavailable. Actual swaps announce the change. 28 related Core cases pass with one skip and 51 related App cases pass; native acceptance remains open. |

FCUX-034/035 were completed within the earlier Settings preservation work.
The audit's listed FCUX findings through FCUX-068 are implemented. Continue with queued bugfix
acceptance and native review. Active Undo ownership, full synchronization
endpoint identities and delayed-Paste intent guards are already implemented;
they do not close the separate general navigation-supersession finding. Native S1
acceptance remains open.

### FCUX-036 implementation record

The Editor field now offers a local program picker, argument token insertion,
an explicit batch shell-interpretation choice and a sample invocation preview.
The preview resolves the executable and displays
each argument boundary without launching a process or creating a list file;
changed inputs retire the old preview. The Settings draft still commits only
after OK. A structured saved-filter editor with ordering controls and an
Advanced text route is implemented. Focused headless Settings regressions cover
exclusion masks, ordering, item-level errors, raw-text repair, save and Cancel.
The association list/detail editor now supports reordering and a nonexecuting
first-match preview, including filters added in the same Settings draft. Its
Advanced text route and field validation have focused tests. F9 commands now
have a tree-aware list/detail editor with menu insertion, sibling reordering,
program Browse, item-level errors and a lossless Advanced text route. Settings
search finds labels and explanations across tabs and navigates to the result.
Focused Settings tests cover command editing, nested ordering, malformed input,
raw/structured round trips, Cancel and cross-tab search. The Keyboard tab now
opens the existing searchable reference and shortcut capture dialog against the
Settings draft; Cancel leaves saved bindings untouched. Command and association
rows now offer token insertion and an asynchronous no-write argument-vector
preview that retires on edits and selection changes. Focused regressions cover
these routes. A shared Advanced text file route now reads bounded strict UTF-8
and writes confirmed local exports atomically; focused import/export and the
related Settings cases pass. Native keyboard/focus, picker and scaled layout
acceptance remain open for FCUX-036.

### UXBUG-001 implementation record

Markdown Page tables now use the viewer's available width. A keyboard-focusable table region
scrolls horizontally when a table's content needs more width. A headless Chrome
geometry check of `UX_UI_IMPLEMENTATION_STATUS.md` at a 1,536 px viewport found
the four columns visible in a 1,457 px table region with no inner overflow; at
600 px, the 521 px region contained a 916 px table and horizontal scrolling,
without expanding the page's horizontal extent. Markdown renderer regressions
cover the safe region markup and stylesheet contract. UXBUG-004 subsequently
removed the fixed prose measure so prose grows with the page. Native WebView2
desktop acceptance remains open.

### UXBUG-002 implementation record

Markdown and HTML Page modes now expose the viewer's Find and case controls.
Ctrl+F/F7, F3 and Shift+F3 route from the native page to the viewer; Esc and
Stop search cancel pending work. Page results are owned by the current engine,
page object, source generation and active mode. Windows WebView2 uses a
host-controlled find operation while page-authored scripts remain disabled;
Linux WebKitGTK and macOS WKWebView use their native find facilities.
`ViewerPageSearchTests` covers match, no match, direction, cancellation and
stale mode/source results. The real Windows WebView2 test covers rendered
Markdown, repeated and backward find, absent and quote-bearing text, and
script-disabled title behavior. Linux's native test and the macOS smoke test
are source/build checked here but require their respective platforms to run.

### FCUX-037 implementation record

The toolbar now refreshes command admission from `GetAvailability` when the
active tab, listing, history or relevant switches change. Listing bursts use
the existing posted coalescing path, and unchanged command presentations avoid
rebuilding their tooltip visuals. A disabled button keeps its accessible name,
adds the refusal reason to automation help text and gives its enabled wrapper
a hoverable explanation. `ToolbarAvailabilityTests` covers Back/Forward across
tabs, focus and provider changes, key-bar and command-search agreement, the
refusal reason and execution's second admission check. The related 35 App tests
pass. Native tooltip/screen-reader behavior and rapid-focus latency on a large
listing or remote provider still need desktop acceptance.

### FCUX-038 implementation record

Bulk rename now binds the entire accepted plan to an explicitly virtualized
list. Problems and Changed filters, name/path search, Next problem and a
selectable old/new full-path detail field expose rows beyond the old 2,000-row
cutoff. A synthetic 3,001-row planner case places the only collision at row
2,501 and verifies one-click discovery and a small realized visual set. The
existing rename scope, execution and dialog viewport tests pass in the same
33-case selection. Native screen-reader, scaled-layout and maximum-size
responsiveness checks remain open. The separate FCUX-039 work below moves
filesystem existence probes to device workers.

### FCUX-039 implementation record

The planner now separates pure rule expansion and duplicate checks from the
candidate destinations that need filesystem probes. The dialog computes the
pure plan off the UI thread and runs existence probes in bounded device queues.
Edits and scope changes clear stale rows, cancel queued work and show an
accessible “Checking names…” state; only the current validated generation can
enable Rename. After confirmation, a fresh device-scheduled check refuses a
new collision before job admission, and the executor retains its no-replace
guard. `BulkRenameAsyncPreviewTests` holds a probe to verify responsive edits,
Cancel and stale-result rejection, and makes a target appear after preview.
The affected App selection passes 38 cases with one platform skip; seven Core
planner cases pass. Native slow-network, maximum-size and shutdown timing
acceptance remain open.

### FCUX-040 implementation record

Counter start and step now require signed 64-bit whole numbers, and digits
requires 1–12. The dialog keeps invalid text, shows field-adjacent accessible
feedback, and refuses to plan or submit a fallback value. Empty intermediate
text gets neutral guidance while editing. Negative steps count down and zero
repeats the counter value; the pure planner catches overflow at each affected
row instead of wrapping. Eight focused App field cases and eight Core planner
cases pass. Native focus, screen-reader and scaled-layout checks remain open.

### FCUX-041 implementation record

The transfer form validates its destination before either Start or Queue can
submit. Parser and provider-capability refusals appear beside the destination
field with accessible help, while the filter, verification, rate, conflict and
selection choices remain in place. One captured asynchronous result supplies
both the inline folder/new-name interpretation and the eventual request.
Filesystem existence probes run on the device IO scheduler; changed text
cancels queued probes and retires late results. Cancel does not wait for a
blocked filesystem probe. Four focused UI cases cover unsupported providers,
both buttons, retained options, an existing-parent rename mapping, correction,
and held-probe cancellation. Existing operation and recovery tests now wait for
the form's validated submission state. A complete App attempt stalled and was
stopped; the gate remains open. Native network-path, focus, screen-reader and execution-time-change
checks remain open.

### UXBUG-003 implementation record

The user clarified that dragging the PE viewer's scrollbar thumb down and up,
not turning the mouse wheel, triggers the freeze. The supplied SignalSetup.exe
sample is 282 MB; its static Info report is about 18,000
characters and 460 lines. Thumb changes previously synchronously sought and
shaped each intermediate viewport. The text view now keeps only the latest
thumb position for each 32 ms update and cancels a pending seek when the source
or another navigation action changes. The Info status line uses a line count
captured when the report is generated. Portable tests cover 80 rapid thumb
values and a real headless drag down/up through 40 pointer moves in each
direction. A temporary local-only headless test against the supplied EXE also
passed and was removed after verification. The user confirmed the native
Windows scrollbar drag on that EXE works. The complete App suite still needs
a passing run with the current code.

### FCUX-042 implementation record

The operations drawer has a pointer and keyboard resize grip. Its preferred
height is saved with settings, and live workspace measurements keep at least
100 DIP for the file panels when space permits. An initial 640 × 420 window and
a sudden shrink from a taller window both retain usable workspace. The bounded
recovery section lists interruptions and shows actions for one selected item;
the list and section scroll, including through 20 items by keyboard. The
operation header keeps a complete row and an Open report action visible at
640 × 420. The fixed graph is behind a collapsed expander so it does not take
diagnostic width. `OperationsDrawerLayoutTests` covers 1/5/20 interruptions,
small/wide geometry, pointer and keyboard resize, settings persistence, and
window shrink/expansion. Native scaling and screen-reader acceptance remain
open.

### UXBUG-004 implementation record

The Page stylesheet previously capped every non-table block at 880 px and
centered it. That made prose stay narrow after the viewer grew, while tables
used the new width. Direct prose blocks now use the available article width;
the article keeps its 32 px side padding and table overflow behavior. The
48-case `MarkdownTests` selection passes. A local headless Chrome check of the
rendered implementation-status page measured prose and table regions at
505 px in a narrow window and 1,441 px in a wide window, with no page-level
horizontal overflow. Native WebView2 desktop resize acceptance remains open.

### FCUX-033 implementation record

When a root, a long final folder, or a one-part path exceeds the breadcrumb
width, the path now keeps a bounded root cue, one non-actionable ellipsis and
the distinguishing tail of the current name. Display cutting follows Unicode
graphemes and treats each escaped hidden character as one unit. Pointer hit
tests and underlines use only visible, contiguous fragments; hidden segments
cannot be clicked through the ellipsis. Hover, the static accessible name and
the Alt+Up ancestor menu expose the complete safe-display path, while Copy
retains the exact raw spelling. Headless tests cover 120/240/480 DIP widths,
one/two/many parts, long UNC and provider-style roots, emoji and combining
marks, bidi/control escapes, full inspection and mapped hit targets. The
complete Core suite passes 4,413 cases with 150 platform/fixture skips. Native
font, screen-reader, touch and scaling acceptance remains open.

### FCUX-032 implementation record

Alt+Up, Navigate and command search now open a single ancestor menu based on
the same provider-derived segments used by pointer breadcrumbs. Each ancestor
has standard menu actions to open here, open in a new tab or copy its full path;
the menu items expose complete safe paths to accessibility clients. The drawn
path names the current location and explains Alt+Up and Ctrl+L without adding
an ancestor per ordinary Tab stop. Escape returns to the current list. Menu
actions refuse a changed tab/location, and closing a menu never reapplies an
old row index after refresh or navigation. Successful navigation focuses the
new listing. Keyboard-driven headless tests cover the actions, cancellation,
stale actions and Ctrl+L; the existing pointer path tests pass. Native screen-
reader and provider-specific desktop acceptance remains open.

### FCUX-031 implementation record

Panels → Arrange and command search expose Resize panel boundary. The command
focuses the closest measured splitter to the active panel; Tab chooses another
boundary, arrows change its size by eight DIPs, Shift+arrows by 32. Enter saves
the proportion and Escape restores its starting values while that tree is still
current. A new layout, workspace replacement, deactivation or focus departure
retires the old interaction. The splitter and temporary visual strip name its
orientation, neighboring panels and current percentage, while normal Tab
navigation skips splitters. Keyboard resizing shares the pointer's minimum-size
policy and saved layout model. Four headless interaction tests, command-search
coverage and the 35-case focused layout selection pass; the full App suite is
passed (2,155 passed, 62 platform skips). Native keyboard, screen-reader and
scaling acceptance remains open.

### FCUX-030 implementation record

Panel admission and drag previews now use the measured workspace host, the
same rectangle used by the cramped-layout strip. Before the first measurement,
an add-panel command explains why it is temporarily unavailable. Changing
toolbar, command-line or key-bar visibility immediately changes admission.
The compact address row keeps the location and target on top and moves free
space and filter entry below them; the role button keeps its full accessible
name and tooltip. The panel minimum is 260 × 240 DIPs, verified with at least
two visible file rows at that size. A restored or subsequently cramped tree
shows the reversible F11 route, with a one-panel warning when even that panel
needs more room. Host-aware admission, narrow and wide address geometry,
cramped recovery, core tree math and docking tests pass. Native 100%, 150% and
200% scaling and screen-reader acceptance remain open.

### FCUX-029 implementation record

Main-window notifications now publish text, severity, action and repeat count
as one state. Notices get six seconds; errors stay until explicitly dismissed.
The timer defers while a user hovers or focuses the banner. Identical messages
within thirty seconds share a history row and visible occurrence count without
changing live message text. The banner offers Copy text, History and Dismiss controls;
its selectable text no longer dismisses on click. Polite and assertive live
regions carry notices and errors. Tools and command search open a selectable,
bounded recent history even after a notification disappears. Escape still
dismisses first; ordinary commands keep the current message. Headless tests
cover severity transitions, repeats, focus/hover, copy, history, command
continuity and the 100-row bound. Native screen-reader, clipboard and scaled
layout acceptance remain open.

### FCUX-028 implementation record

The filter field keeps a rejected draft and caret in focus while its applied
mask and rows stay unchanged. Its validation message appears beside the field,
is linked through automation HelpText and is an assertive live region. Correcting
and applying clears only that message; Escape, blur, tab switch and navigation
discard the unapplied draft. All-items masks no longer erase an unrelated
working-set, Registry or listing warning. Headless tests cover malformed quote,
regular expression and saved-filter reference, correction, focus, warning
preservation and actual working-set context. Native screen-reader and scaled
layout acceptance remain open.

### FCUX-027 implementation record

Explicit sorts now register a focused-item reveal against the requested listing
revision and the invoking tab, location, store, focus, navigation generation and
viewport intent. The list reveals that same item only when those sorted rows
arrive. Later vertical or horizontal scroll, focus change, navigation, tab
switch or another view revision retires the request. Background refresh retains
its manual-scroll behavior. Headless regressions cover both sort directions,
metadata order, delayed manual scroll/focus/tab changes, refresh preservation
and identity-based pointer ranges. Native pointer, keyboard and screen-reader
acceptance remain open.

### FCUX-026 implementation record

The virtual file list now has a horizontal scrollbar whenever the configured
columns exceed the pane. Header, rows, pointer hit tests, resize edges,
tooltips and the rename editor share one horizontal offset. Left/Right keys,
horizontal or Shift+wheel input and the accessibility Scroll pattern can reach
the remaining columns. The Name column retains a font-scaled useful minimum;
tab/profile changes reset the offset and resize clamps it to the current
extent. Vertical and horizontal bars reserve each other's space. Focused
geometry tests cover 320–800 DIP widths, enlarged text, two fill columns,
vertical overflow, scrolled sort/resize, rename, automation and return to
Name. Native touchpad, screen-reader and scaled desktop acceptance remain open.

### FCUX-025 implementation record

Opening a named workspace now captures one bounded, complete previous
arrangement before disposing tabs. Restore previous workspace appears in Tools,
command search and the completion notification. It restores supported tab
order, locks, active panel/tab, split proportions and target assignments and
swaps the one previous slot with the current arrangement. Ordinary Recently
Closed entries are untouched. Temporary views are counted from live tabs and
disclosed before any switch; canceled disclosure leaves the current layout
intact. Persisted active-tab indices account for filtered transient tabs.
Headless tests cover 40 tabs across four panels, repeated switching, the
notification action, transient disclosure/refusal and safe omission. Native
keyboard, scaled-layout and assistive-technology acceptance remain open.

### FCUX-024 implementation record

Save workspace as suggests an unused Project name and validates display names
before using them as filenames. Unsupported characters, trailing aliases and
device names are refused, so distinct accepted names cannot silently sanitize
to one record. Choosing an existing name shows its saved and replacement panel
and tab counts and asks explicitly before replacing it. Cancel leaves both the
main and backup files alone. New saves publish without replacing an occupied
name; approved replacements compare the reviewed file immediately before
publication and retain the existing atomic backup behavior. A failed save
reopens the same draft for correction. Focused Core/App tests cover these paths,
including case-only aliases and concurrent changes. Native keyboard, layout
and multi-process replacement acceptance remain open.

### FCUX-023 implementation record

Capacity queries now resolve the displayed directory and report the containing
volume separately from the IO scheduler key. Windows uses the containing volume
mount point and native available-to-caller bytes, including UNC shares; Unix
selects the longest containing mount. Explicit Refresh and completed mutations
force a recheck. The active tab periodically checks a short expiry, while slow
queries coalesce and late results cannot replace a newer location. Pending,
unavailable and expired values are labeled instead of showing an old number.
Core and headless App tests cover mount and share identities, actual capacity,
mutations, expiry, slow-query coalescing and obsolete replies. Native mounted
volume/share and disconnected-server acceptance remain open.

### FCUX-022 implementation record

Navigation intent now belongs to one tab generation and one cancellable pending
preparation. A current prepared location commits under that generation; Back,
Forward, Escape and tab closure cancel or supersede it. Late provider success,
failure and SFTP connection errors cannot change a newer destination's listing,
banner, marks, focus or history. Held provider and connector tests cover a
noncooperative late completion, plus the current successful and failed routes.
Native network-mount, keyboard and connection-pool acceptance remain open.

### FCUX-021 implementation record

The sync preview retains explicit choices only for matching entries, directions
and actions. Permanent deletion requires current target and removal-set consent.
The job manager admits each plan as a group before scheduling children, and the
Operations drawer shows its endpoints, revision, aggregate outcome and full
pending-removal paths. Stop cancels only that group's unfinished children.
Unexpected transfer failure or cancellation holds later removals for review;
an expected policy Skip does not. Core scheduler and App controls have affected
regression coverage, including delayed decisions and unrelated work. Native
layout, accessibility and real interruption acceptance remain open.

### FCUX-020 implementation record

The former Dismiss button is now Hide for now and never ends the journal.
Mark reviewed explicitly ends recovery tracking after a deliberate review and
stores a bounded report before the journal end record; storage failure keeps
recovery available. Review report exposes all retained known paths and Copy all
and Save as. Reviewed reports remain in Operations with no mutation actions,
bounded to 200 records. Cleanup and reviewed transfer continuation also record
deleted, kept, unresolved and submitted-scope effects before closure. Tests
cover cancellation, duplicate ownership, storage refusal, kept partials,
unresolved renames, source manifests and existing recovery guards. Native
process-interruption, screen-reader and scaled-layout acceptance remain open.

### FCUX-019 implementation record

Clear finished history and Remove history row now disclose report/unused Undo
retirement. Useful records require a review dialog; routine finished rows clear
directly. Approval removes only the reviewed rows, preserving jobs that finish
while the dialog is open. An already admitted Undo keeps its own work and result.
Global Undo identifies the most recent undoable operation and warns if newer
finished actions are outside it. Focused UI, GC-retirement and Core ownership
tests pass. Native dialog and assistive-technology acceptance remain open.

### FCUX-017 implementation record

Ordinary operation issues now have full selectable path/reason details, exact
copy actions, and Reveal for existing, known-local items. A bounded clipboard
report and streaming local save include all retained details. Total and severity
counters continue past the 10,000-detail cap; the drawer and report disclose
omissions. Structured transfer rows preserve Skip, failed copy, published copy
with source kept, and deletion refusal. Focused App/Core tests and the Release
solution build pass. Native clipboard, reveal, screen-reader and large-layout
acceptance remain open.

### FCUX-016 implementation record

A requested pause now says “Pausing — waiting for a safe point” until the
worker acknowledges it, and the action says “Keep running” while the pause is
pending. Canceled, failed and interrupted work no longer displays a success
percentage; completion with issues keeps explicit counts. Terminal summaries
distinguish planned bytes accounted for from actual copied/read data, so skips
and same-volume moves do not create a false transfer claim. Deterministic
state tests and the existing verified-copy progress route pass; native worker
timing, taskbar and scaling checks remain open.

### FCUX-015 implementation record

The selected operation now binds one observable issue list. Newly retained
issues append in bounded slices, while ordinary progress refreshes check only
the count and do not reset the report. Job change dispatches coalesce. The
actual drawer list updates twice after selection without changing jobs, keeps
its selected issue, and retains scroll position as a 270-issue report completes
its second batch. Final counts reconcile on completion and reopen. Native
rendering and assistive-technology acceptance remain open.

### FCUX-075 implementation record

The drop cursor offers Copy or Move only when the displayed filesystem tab is
ready and its provider accepts transfers. Drop rechecks the same tab and
location, verifies the folder is available, and refuses incomplete external
file payloads without starting a partial job. Unsupported or changed targets
explain the local-folder and F5 alternatives without activating the panel.
An accepted transfer review keeps its captured destination after navigation.
The five focused tests pass; native cursor and interapplication checks remain.

### FCUX-071 implementation record

Clipboard and drag now account for every captured item before publication.
The drag path refuses mixed local/provider scopes as a whole, stages eligible
archive-only files within the existing item/byte limits, and validates each
storage identity before beginning a native transfer. Refusal, cancellation,
source-tab change and conversion errors retire unhanded staging. Tests use
actual local and ZIP content, hidden marks, origin marks, conversion failures,
unexpected provider errors and scope limits. Native interapplication drag and
clipboard acceptance remain open.

### FCUX-014 implementation record

Actual list cell presentation escapes hidden controls in separate extension
cells and provider-derived folder/kind/details text. Hover on an affected name
offers the full safe name and a character description; focus status and row
automation preserve the same identity evidence. Sorting and operations continue
to use the raw filename. The profile, list and automation regressions pass;
native glyph and screen-reader acceptance remain open.

### FCUX-013 implementation record

The seven explicit palettes and current System resolution now have numeric
regressions for essential normal, muted, actionable hidden, selected, search,
changed, primary, danger and binary-difference text, plus the focus boundary.
Destructive button template states are tested for normal, hover, press and focus.
Selected and highlighted Text/Info glyph layouts are committed before paint;
Hex and binary comparison draw paired glyph foregrounds on their semantic fills.
The existing text comparison uses line signs and changed-span underlines.
Headless drawing does not provide rendered pixels in this Windows test host, so
native rendered contrast, grayscale, scaling and assistive-technology acceptance
remain open rather than being inferred from the token/layout checks.

### FCUX-072 implementation record

Alt+B and the labelled toolbar action open a range review with side, start and
count. The control validates against the current source interval and reveals
the chosen byte without turning alignment gaps into bytes. Invalid and
overflowing input stays editable. Source replacement, view changes and closing
retire a pending review. Cancel returns focus to the byte surface.

Copy retains the comparison's 4 MiB budget, spaced uppercase hex and displayed
text mappings. Its read and clipboard publication are owned by the current
source, selection and request; changed-source evidence blocks publication.
The selectable bounded preview and read-only automation value expose source,
side, range and bytes without synchronous peer reads. The 79 comparison and
accessibility tests pass on Windows, including old pointer and replacement tests. Native
screen-reader, scaling and clipboard interoperability acceptance remains open.

## Version control

Use the current Git checkout and `main` history as the source of truth. Review
each change with `git diff`, run its focused tests and applicable complete suites,
then commit the implementation, regression tests and status records together.
Push each verified commit to `origin/main`, as the user requested. Preserve
unrelated local changes and avoid destructive history rewrites. If a push fails,
keep the commit locally, report the failure and retry when the remote is available.
Record test evidence and remaining native acceptance in the repository's validation
documents. Do not create a source or changes ZIP for this audit workflow.
