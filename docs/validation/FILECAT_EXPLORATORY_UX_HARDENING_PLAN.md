# FileCat exploratory UX and hardening plan

**Prepared:** 30 October 2026. **Status:** plan only; no product behavior is accepted or changed by this document. The source inspected for this plan is `37e6fc0e` on `main`. Refresh the source and CI identity before executing a shot.

## Purpose and boundary

Use the wait for SignPath's response to find consequential problems in real FileCat work: understandable actions, recoverable errors, correct files and destinations, responsive controls, and truthful results. Prefer short, surprising user journeys over another systematic platform × provider × theme matrix. A journey may cross several tools, but its value must come from a boundary that the existing evidence does not already settle.

This is exploratory work, not final 1.0.0 candidate qualification. The [release dashboard](../release/1.0.0/FILECAT_1_0_RELEASE_EXECUTION_REPORT.md) remains **NO-GO**: signing/provider acceptance, a frozen installed candidate, and its other required gates remain separate. Preview.3 predates the recent product fixes. Do not treat an unsigned source build, a VM observation, or a passing exploratory shot as signed installed-candidate acceptance.

## Reconciliation before new work

- The [UX acceptance ledger](UX_UI_ACCEPTANCE_LEDGER.md) and [remaining-work index](UX_UI_REMAINING.md) report all 76 original audit findings and `FCUX-NEXT-001` accepted at their recorded scopes. The [earlier improvement plan](FileCat_UX_UI_Improvement_Plan.md) is historical, not a fresh backlog. Its speed-control recommendation was subsequently accepted.
- The [release issue register](../release/1.0.0/FILECAT_1_0_RELEASE_ISSUES.md) already owns I566–I572: Explorer ZIP paste, viewer Find, Operations layout/order, and escape from a denied folder. Their exact signed installed-candidate checks and latest-source CI belong to the release ledger. Do not spend an exploratory slot repeating those scenarios.
- The [security plan](../security/FILECAT_SECURITY_REVIEW_AND_PLAN.md) and release evidence contain many provider, recovery, archive, clipboard, remote, race, and resource controls. Before each shot, search the exact feature and perturbation in those records and the relevant tests. A similar feature is not automatically a duplicate, but a passed consequential boundary is.
- At drafting, earlier CI failures from stale viewer-search assertions had a test-only correction, and the latest-source run had not finished. Treat any fresh required-lane failure as existing work to diagnose before attributing it to exploration. Do not rerun a passed scenario merely because a documentation-only commit exists.

## Options considered

| Approach | Value and cost | Decision |
|---|---|---|
| Replay the 76-case UX audit or the recent I566–I572 fixtures | Strong historical coverage already exists; replay would consume native and CI time without a new question. | Exclude unless a concrete change, invalid receipt, regression, or uncovered mandatory boundary is recorded first. |
| Exhaustive combinations of platforms, themes, scales, providers and file types | Broad but expensive, hard to interpret, and likely to rediscover known cases. | Exclude. Select the one environment and presentation needed by the observed consequence. |
| Unstructured clicking or unbounded fuzzing | Can reveal surprises, but a result without source identity, owned files and a repeatable action sequence cannot drive a safe fix. | Keep the surprise; bound each shot and capture enough to reproduce it. |
| Short user task with one realistic disruption | Tests the seams between features, keeps user consequences visible, and can be stopped when evidence is sufficient. | **Primary method.** Choose tasks opportunistically from the deck below; redraw when the exact boundary is already accepted. |
| Targeted parser/resource stress | Useful after a concrete anomaly or a distinct untested trust boundary; otherwise duplicates extensive security work. | Use as a follow-up to an observed issue, with a fixed input/time/resource budget and a positive control. |
| Final installed-candidate checks | Required for 1.0.0, but their exact artifact and signing prerequisites are absent. | Preserve in the release ledger; run when the candidate exists. |

## A small deck of possible shots

These are **prompts to sample**, not claims of untested defects or a mandatory sequence. Shuffle the remaining prompts, draw one, then veto or reshape it after the evidence search; a spontaneous real-user observation can replace a draw. Across the first tranche, include both everyday file work and at least one expert inspection task; do not force every operating system into every shot.

| Prompt | One possible disruption and the distinct question to seek | Existing work to check before spending a slot |
|---|---|---|
| Continue work from a saved location | A test-owned local/removable-like folder vanishes or becomes unavailable before reopening the workspace. Can the user reach a usable place, understand the missing location and keep unrelated tabs/targets? | Workspace restore, delayed remote navigation/Back, I572 denied-parent navigation. Do not replay the accepted 40-tab or denied-folder cases. |
| Inspect a file another program is changing | Open an owned document, log or binary in Quick View/Info/full viewer, then replace or truncate it externally. Does the visible content identify the current source, retire stale work and offer a clear recovery action? | Viewer revision, Quick View lifetime, checksum and nonlocal search ownership tests. Choose a cross-tool user-visible seam not already proven. |
| Turn a search result into an action | Find an owned file, then externally rename or remove it before Open, Compare or Copy. Does FileCat explain the changed result and avoid acting on an unintended object? | Find result ownership, source-binding/security guards, bulk rename and transfer review cases. Count a new UX explanation or action boundary, not another object-identity proof. |
| Carry a small project through expert tools | Inspect an owned executable or media/binary file in Info, Hex and Compare, with one odd but valid name or metadata value. Can the user keep file identity, offset/context and a useful return path across tools? | Existing Info/Hex/Compare tests, viewer Find work and accepted long-path/layout cases. Avoid replaying their individual controls. |
| Work in an externally managed local folder | Use a test-owned directory whose files are created, renamed and removed by a second process while FileCat lists, filters and navigates. Is the current selection/target understandable after the churn, without a silent wrong operation? | Navigation intent, virtualized listings, cloud/desktop clipboard and target-capture evidence. No real cloud credentials or user folders are needed. |
| Finish a normal operation amid one interruption | Run a small owned operation, then close/reopen a related view or change the active tab while the outcome arrives. Can a user still find the correct final report and next action? | I570/I571 Operations, queue/Undo/interrupted-operation acceptance. Skip this prompt if the chosen interruption merely repeats those accepted scopes. |

The deck may be replaced by a better task encountered in ordinary use. For every chosen shot, state **which accepted case was checked and why this boundary is different**. If that sentence cannot be written, redraw. Incidental traversal of an accepted route is setup, not new evidence for it.

## Execution for one shot

1. **Select and bound.** Write the user goal, one perturbation, expected user-visible outcome and exact existing-evidence veto. Use a test-owned source/target and record their initial hashes or manifests when mutation is possible. Set a 45-minute exploration limit, a 30-second guard for a deliberately held UI action, and a fixed input size. A guard expiry is a diagnostic observation, not automatically a product failure or a performance-contract violation.
2. **Observe once.** Use a current Git-built app in an isolated Win11/Ubuntu VM or Mac session as the task requires. Record commit/package identity, OS/backend, input steps, visible focus/status/target, final bytes and a screenshot or accessibility receipt where visual or spoken behavior matters. Keep the physical Windows workstation usable; perform no foreground desktop automation or exclusive loads there.
3. **Triage the consequence.** Check the exact behavior against source, tests, the UX ledger, release issues and security evidence. If it is accepted behavior, explain it and stop. If it is ambiguous, run one narrower positive/negative control. If it is a real new failure, reproduce on an owned fixture before changing code. Do not claim a security defect from a confusing label alone, or a UX pass from a component test alone.
4. **Fix only proved problems.** Prioritize wrong-item/data-loss/security or blocked core work, then repeated substantial friction, then polish. Add a regression for the user-visible invariant when it protects the fix; avoid tests that mirror implementation order. Run affected tests and the required latest-source CI after a product change. Reopen an accepted case only for its documented consequential reason and stopping condition. Keep an exact installed-candidate follow-up in the release ledger where applicable.
5. **Record and stop.** One short record per shot: source/environment, goal, prior-case check, fixture/perturbation, observations, hashes or resource/timing evidence, disposition, fix/tests if any, and precise remaining limit. File only a distinct issue; link a related existing ID for a true regression. Remove temporary fixtures and preserve only useful receipts.

## Triage and finite stopping rules

- **P0/P1:** An observed wrong destination/object, data loss, unsafe authority, silent partial success, or blocked core task enters the release issue register and is fixed before 1.0.0 qualification. Stop that shot at a minimal reproduction and positive control; expand only to the affected boundary.
- **P2:** Fix a repeated, consequential UX problem when the remedy is contained and can be verified on the current source. A one-off awkward interaction with no demonstrated user cost can be recorded without product churn.
- **P3/idea:** Keep as an optional note. Do not add a release gate or broadly redesign a control from one exploratory observation.
- **No finding:** Record the tested scope and stop. “No additional high-ROI improvement found” is a valid result; do not manufacture work to fill the tranche.
- **Tranche limit:** At most **eight distinct 45-minute shots** before a fresh prioritization decision. An evidence-vetoed duplicate is redrawn and does not count; at most two diagnostic repeats are allowed for one ambiguous result. Stop earlier if SignPath/candidate work becomes ready, a P0/P1 needs focused resolution, or the next three proposed shots all collapse into accepted coverage. Do not automatically start a second tranche.

The tranche is complete when every executed shot has a scoped record, all proved P0/P1 findings are either corrected with their release gate or explicitly blocking, P2 decisions are recorded, temporary fixtures are cleaned up, and the latest product source has no unexplained required CI failure caused by the tranche. This does not assert stable-release readiness or replace the owner’s final GO decision.
