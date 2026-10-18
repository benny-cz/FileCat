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

### Next queued implementation

After resolving any outstanding validation failure, follow the audit's physical
S2 order; its permanent IDs are references, not a numeric priority sort. The
FCUX-036 and UXBUG-002 native acceptance checks remain open. The completed
FCUX-011/012/013/014/076/072 and viewer FCUX-073 code scopes also keep their
native acceptance gates open. The user confirmed the PE viewer scrollbar-drag
fix on the supplied Windows EXE. FCUX-042, the newly reported Markdown resize
correction, FCUX-043, FCUX-044, and FCUX-045 are implemented with focused coverage.
The complete App run twice stopped on the same drop test; its premature Enter
now waits for asynchronous destination validation, and the corrected class passes.
Rerun the complete App gate, then continue with FCUX-046.

| Order | Remaining finding | First acceptance boundary |
| --- | --- | --- |
| 1 | FCUX-036 — advanced Settings editing | Provide supported editors and validation for advanced setting fields. |
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
| 13 | UXBUG-005 — viewer window size | Remember each viewer window's last usable size and restore it on reopen, including after app restart; clamp saved bounds to the current display and cover reopened text, Markdown, PE, and other viewer modes. |
| 14 | UXBUG-006 — Streams count after deletion | After Delete removes a stream from Streams and attributes, refresh the parent file's Streams count when the provider can query it; cover Delete-key action, stale-row retirement, and a clean fallback when the count is unavailable. |
| 15 | FCUX-045 — Find completion | Visible completion and qualification, plus search-specific empty states, are implemented; 98 related Release tests pass. Full App and native acceptance remain open. |
| 16 | UXBUG-007 — resizable search windows | Make Find in Registry and comparable search windows expandable and shrinkable, with usable minimum bounds and responsive criteria, results, and action layouts. Cover resize and reopen behavior. |

FCUX-034/035 were completed within the earlier Settings preservation work.
After FCUX-037, continue FCUX-038 onward in the audit's S2 order. Active Undo ownership, full synchronization
endpoint identities and delayed-Paste intent guards are already implemented;
they do not close the separate general navigation-supersession finding. Native S1
acceptance remains open.

### FCUX-036 work in progress

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
these routes. Native keyboard/focus, picker and scaled layout acceptance, and
an explicit file import/export route for Advanced text, remain open for FCUX-036.

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
