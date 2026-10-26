# FileCat UX/UI implementation session workflow

Updated 26 October 2026. Continue the
[audit](FileCat_UX_UI_Audit.md) using one source of truth for **required work**:
the finite [acceptance ledger](UX_UI_ACCEPTANCE_LEDGER.md). The
[remaining-work index](UX_UI_REMAINING.md) orders only its open scenarios.
[Implementation status](UX_UI_IMPLEMENTATION_STATUS.md) preserves dated,
build-specific changes, tests, native results and limitations. The former
session narrative is [archived as history](UX_UI_SESSION_WORKFLOW_HISTORY.md);
its old pending statements have no current authority.

## On every continuation

1. Read the current ledger and exact Git state. Resolve any failed CI lane or
   active defect first, then take the highest-severity genuinely untested
   scenario. Do not infer an open case from a parent finding, an old status
   paragraph or a documentation-only commit.
2. Keep one reviewable scope: inspect the relevant implementation and dated
   evidence, choose the smallest consequential fixture, implement the
   correction if needed, and add regression coverage for the user-visible
   invariant. Preserve scope capture, cancellation, source bytes, marks,
   recovery ownership and resource lifetime.
3. Run affected tests, then applicable complete suites with up to 16 workers.
   A failed, skipped or interrupted process is diagnostic, not a passing
   gate. Native pointer, timing, backend and spoken claims require native
   evidence from the corresponding environment.
4. Record build/revision, OS/backend, scale/logical bounds, exact fixture
   manifests, input sequence, result and evidence path. Update the ledger
   row's status and precise remaining boundary; move passing detail into
   the dated implementation status. The remaining-work index contains only
   work still required.
5. Commit and push each verified increment to main. Keep unrelated
   untracked scripts. Use the Git checkout and Git-built binaries; do not
   restore the old ZIP input/output test workflow. Fix GH CI failures in the
   loop and require the final integration gate below.

## Closure and stopping rule

Close a scenario when the consequential behavior and applicable audit
acceptance criteria are demonstrated by deterministic, component and native
evidence as appropriate. A justified equivalent inspection/input route is
acceptable if it proves the same outcome. Record a limitation honestly.
Use pairwise coverage for general layout and focused combinations for known
risks (for example long paths plus a small dialog, or remote delay plus newer
navigation). Do **not** expand to every platform × theme × scale × provider ×
input permutation. Stop testing once the remaining risk is resolved; do not
write tests that only restate constants or implementation order.

Before repeating any **passed** scenario, enter its ID and a concrete reason
in the ledger's repeat authorization record: relevant implementation or
dependency change, new regression, invalid evidence, an uncovered required
boundary, or a bounded reliability investigation. Name a stopping condition
before starting. An open parent, unrelated code or docs-only commit is never
enough. A reconfirmation must be labeled as such and cannot be counted as new
coverage.

Keep optional refinements outside required closure. Track new user-reported
defects in the ledger's queued-defect section until diagnosed; they do not
silently enlarge all original findings. Native acceptance can close for an
individual scenario even while the audit-wide CI integration gate remains
open. The final implementation head still requires green producer, Windows,
Windows ARM64, Ubuntu and macOS CI; docs-only commits do not reopen already
passed native journeys.

Use the current ledger order for the next eligible case; this workflow keeps
no separate priority snapshot. Keep Mac audio muted and perform desktop input
on the isolated VMs/Mac, not the owner's workstation.
