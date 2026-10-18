# FileCat UX/UI implementation session workflow

This file records the user's continuation and delivery instructions for this
implementation session. The original [UX/UI audit](FileCat_UX_UI_Audit.md) stays
unchanged; the [implementation status](UX_UI_IMPLEMENTATION_STATUS.md) records
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

After resolving any outstanding validation failure, resume **FCUX-030**. Follow
the audit's physical S2 order; its permanent IDs are references, not a numeric
priority sort. The completed FCUX-011/012/013/014/076/072 and viewer FCUX-073 code scopes keep
their native acceptance gates open.

| Order | Remaining finding | First acceptance boundary |
| --- | --- | --- |
| 1 | FCUX-030 — measured panel layout | Base panel admission and cramped-state feedback on the actual workspace viewport. |

Then continue FCUX-031 onward in the audit's S2 order. Active Undo ownership, full synchronization
endpoint identities and delayed-Paste intent guards are already implemented;
they do not close the separate general navigation-supersession finding. Native S1
acceptance remains open.

### FCUX-029 implementation record

Main-window notifications now publish text, severity, action and repeat count
as one state. Notices get six seconds; errors stay until explicitly dismissed.
The timer defers while a user hovers or focuses the banner. Identical messages
within thirty seconds share a history row and visible occurrence count without
changing live message text. The banner offers Copy, History and Close controls;
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

## Cumulative baseline

The immutable source baseline is the original attached FileCat source archive,
represented locally by commit:

```text
aed1ae5053f50ef0c90f682200c5cb5d0b03ae24
```

Always compare the final reviewed tree with this original baseline. Do not reset
the delivery baseline to the previous iteration or the latest commit. The
package must include every cumulative added or modified file, including fixes
from earlier turns. Also include a file that was edited earlier and then restored
to its original bytes or mode: otherwise an overlay on the earlier iteration
would leave that old edit behind. Files never edited during the session can be
omitted. Record deleted files explicitly; a missing file in a changes-only archive
cannot communicate deletion by itself.

When resuming from a delivered ZIP, retain its
`_delivery/PACKAGE_MANIFEST.json`. Its original-source hash,
`session_change_status`, restored-file records and `deleted_paths` preserve the
same cumulative baseline and previously touched-file set if the local Git
history needs to be reconstructed. Carry that history into the next delivery
alongside the new changes.

## One generated ZIP

Maintain one current generated ZIP named **`FileCat-UX-changes.zip`**. It contains:

- `FileCat/`: the cumulative added and modified repository files, using their
  original repository-relative paths and retaining executable/symbolic-link
  semantics where applicable.
- `_delivery/`: application instructions, a manifest of changed and deleted
  paths with hashes, and retained test/build evidence. This directory is delivery
  metadata, separate from the product files.

Unchanged repository files, build outputs, dependency caches and Git internals
are omitted. This ZIP is an overlay for the original source tree or an earlier
iteration of this same session; it is not a complete standalone checkout. Back
up unrelated local edits before overlaying it. Apply the deletion manifest's
explicit file/link removals before merging; preserve directories that now hold
replacement files. For directory-to-file changes, remove only obsolete listed
files and empty blocking directories, keeping unrelated local contents. Replace
an existing symbolic link itself instead of copying through it. The ZIP's
application instructions explain this order. An optional cumulative binary-safe
patch is another way to apply the changes to the exact original baseline.

Create and verify the replacement ZIP before removing superseded generated ZIPs.
Verify its complete path set and hashes against the baseline diff, its CRCs, and
the cumulative patch's application to a clean original checkout. Then remove
older generated deliverable ZIPs so only the current ZIP remains. Preserve the
original attached source archive and repository archive fixtures: those are
inputs, not superseded deliverables. Keep expensive execution evidence in the
current package even when its previous standalone ZIP is removed.

## Version control

Commit the reviewed implementation, regression tests and status records locally.
Push directly when an existing intended remote and working credentials are
available, as the user requested. Do not invent a remote or change repository
visibility. If pushing is unavailable, deliver the verified cumulative changes
ZIP and state that limitation accurately. Preserve existing user changes and
avoid destructive history rewrites.
