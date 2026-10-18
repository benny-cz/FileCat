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

After resolving any outstanding validation failure, resume **FCUX-072**. Follow
the audit's physical S2 order; its permanent IDs are references, not a numeric
priority sort. The completed FCUX-011/012/076 and viewer FCUX-073 code scopes keep
their native acceptance gates open.

| Order | Remaining finding | First acceptance boundary |
| --- | --- | --- |
| 1 | FCUX-072 — binary comparison keyboard byte ranges | Select and copy exactly four bytes from the right, then the left, without a pointer. Cover row boundaries and aligned gaps; source, side, extent, copied bytes and semantic state must agree. The audit permits an explicit Select byte range action. |
| 2 | FCUX-013 — theme-state contrast | Verify the complete essential-text/state matrix across all palettes and System-resolved modes, including destructive actions and custom-drawn selections. Preserve the existing primary/target/tooltip fixes. |
| 3 | FCUX-014 — safe extension-cell display | Distinguish visible and invisible-character extensions in every list profile while retaining raw operation identity and legitimate Unicode presentation. |
| 4 | FCUX-071 — remaining drag-export branch | Preflight and account for the complete captured selection; never begin a silently incomplete mixed-provider drag. Clipboard publication is already corrected. |
| 5 | FCUX-075 — unsupported drop feedback | Advertised effects must match capability; revalidate on Drop and explain refusal without changing the user's active destination. |
| 6 | FCUX-015 — live selected-job reports | Update the bound issue list without changing job selection, preserve selection/scroll, and reconcile the final report with bounded update work. |

Then continue FCUX-016, FCUX-017, FCUX-019, FCUX-020, FCUX-021, FCUX-022 and onward
from FCUX-023 in the audit's S2 order. Active Undo ownership, full synchronization
endpoint identities and delayed-Paste intent guards are already implemented;
they do not close the separate history-disclosure, synchronization-group or
general navigation-supersession findings. Native S1 acceptance remains open.

### FCUX-072 implementation handoff

The next slice can add an explicit **Select byte range…** review through
`CompareWindow` and `OverlayDialogService`, leaving the established arrow/Page
scrolling and difference-navigation shortcuts intact. `HexCompareView.Selection`
already represents `(Side, Start, Length)` in source bytes. Add validated range
admission and reveal through the same state path as pointer selection. In aligned
mode, locate the chosen side's actual source interval; an alignment gap contains
no byte and must never become an invented zero or counterpart.

Preserve the comparison's existing copy contract: a **4 MiB** limit, uppercase
hex pairs separated by spaces, and its displayed-text mapping (including bytes
at or above `0xA0`). The viewer's newer `ContentCopy` policy differs in size and
format and cannot be substituted unchanged. The comparison's present asynchronous
copy needs content/selection/request ownership checks and orderly clipboard
publication before this new action can be considered robust. Closing, replacing
sources, reopening or changing interpretation must retire a pending range review
and stale copy completion while preserving active-read disposal safety.

Guard `CompareWindow.OnNavigationKey` while its overlay is open. Keep invalid
offset/count input and its error visible, reject overrun/overflow without silent
clamping, and restore focus to the current surface on Cancel. Test its actual
640 × 320 DIP minimum with normal and large text. Add bounded committed semantic
state following `ViewerAutomation`; peer getters must not initiate source reads.

The deterministic acceptance set should cover both sides in same-offset and
aligned modes; exact four-byte keyboard copy; row boundaries and source ranges
crossing visual gaps; empty/EOF/invalid/overflow inputs; stale review and delayed
copy completion; held or failed pages; copy limits and short reads; and existing
pointer, navigation, Ctrl+A, source-revision and close-during-read behavior. Use
the protected `CompareWindowTests`, revision/lifetime tests and controlled
clipboard patterns from `HexEditorCopyLifetimeTests`. This is preparation for
the next implementation, not completed FCUX-072 acceptance.

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
