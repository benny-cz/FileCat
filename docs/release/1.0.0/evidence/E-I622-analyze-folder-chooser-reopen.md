# I622 — Analyze folder chooser can be reopened

**Source:** Owner report against the prechange main behavior at `b07842c6`; correction `a290bdbe41ae17e1896dd3a171c27731366c65c9`. This is source-level qualification, not final installed-candidate acceptance.

## Finding and correction

`View → Analyze folder` initially showed a field chooser. Once the panel had a metadata sort, the next invocation bypassed that chooser and immediately reanalyzed the old field. The user could not select another analysis field in the same session through this command.

The command now opens the chooser on every invocation. It selects and labels the current sort, so a repeat is easy, and choosing that field again does not reverse the sort direction. Escape leaves the current sort and any existing analysis untouched. Choosing another field applies that field and starts its full analysis. The existing analysis cancellation, folder-lifetime and stale-work controls are unchanged.

## Focused verification

- A real Avalonia chooser appeared in the headless main window on two consecutive invocations after the panel had a metadata sort. Escape closed it both times without changing the field.
- A command-level fixture chose an initial field, reopened and canceled, re-ran the current field without reversing descending order, then chose a different field. The chooser's selected row and current-sort description matched the panel state.
- The changed tests plus existing current-demand and leave-folder analysis suites passed **8/8** on the rebuilt Windows source. An initial new visual test queried the tree before it had laid out; after waiting for the chooser controls, both new tests passed and the complete eight-test suite passed. This was a test synchronization issue, not another product failure.
- Exact-source main run 41648160388 and tagged run 41656633973 passed all five required jobs with identical 20-TRX results and the chooser fix; the installed-candidate keyboard/visual checks remain open.

## Finite acceptance and stopping rule

On a final installed candidate on each supported desktop OS, analyze a small owned folder by one available metadata field, invoke `Analyze folder` again, see the chooser with that field selected, cancel without changing the order, reopen and select a different field, and verify the new sort and completed status. Reopen only for a missing chooser, unintended sort reversal, wrong field or stale analysis result. Once the focused tests, exact-source CI and candidate path pass, stop this scenario; do not repeat unrelated analysis work without a consequential change.
