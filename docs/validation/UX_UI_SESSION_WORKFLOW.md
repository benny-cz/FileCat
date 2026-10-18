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

After resolving any outstanding validation failure, resume **FCUX-016**. Follow
the audit's physical S2 order; its permanent IDs are references, not a numeric
priority sort. The completed FCUX-011/012/013/014/076/072 and viewer FCUX-073 code scopes keep
their native acceptance gates open.

| Order | Remaining finding | First acceptance boundary |
| --- | --- | --- |
| 1 | FCUX-016 — operation status wording | Distinguish requested Pause from effective Paused state and keep canceled/failed work from claiming 100% completion. |

Then continue FCUX-017, FCUX-019, FCUX-020, FCUX-021, FCUX-022 and onward
from FCUX-023 in the audit's S2 order. Active Undo ownership, full synchronization
endpoint identities and delayed-Paste intent guards are already implemented;
they do not close the separate history-disclosure, synchronization-group or
general navigation-supersession findings. Native S1 acceptance remains open.

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
