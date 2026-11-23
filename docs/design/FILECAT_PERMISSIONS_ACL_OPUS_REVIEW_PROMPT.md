# Opus 5.5 — independent review of the FileCat permissions plan

Review and, only where justified, improve the existing FileCat permissions implementation plan. Be independent, pedantic about native semantics, and skeptical about authorization, object binding, failure handling, and completion claims. Previous reviewers' approval is not evidence; neither is a new finding required.

## Inputs and goal

Read `FILECAT_PERMISSIONS_ACL_IMPLEMENTATION_PLAN.md` (normally `docs/design/FILECAT_PERMISSIONS_ACL_IMPLEMENTATION_PLAN.md` in the repository) and the finalized `FILECAT_PERMISSIONS_ACL_PLANNING_PROMPT.md` supplied with it. Review the current plan, not an earlier copy.

The goal is usable in-app inspection and editing of permissions/ownership for files, folders, and mounted volume roots on Windows, Linux/Ubuntu, and macOS, with honest filesystem/provider coverage. Supported changes must finish through FileCat. Native authentication is acceptable; opening another management application does not complete the feature. Preserve native models and existing FileCat UX/themes.

This is a **plan review**, not an implementation task. Do not execute the plan's implementation launcher.

## Establish the evidence boundary

Read applicable repository instructions. Record the actual checkout, working changes, relevant dependency/OS versions, and which sources you could inspect. Preserve unrelated work. The plan's historical Git state and native receipts are reported evidence until independently checked; do not present them as your own runs.

Compare consequential decisions with current source and primary native documentation or pinned upstream code. If only documents or incomplete sources are available, complete the useful review and identify exact unresolved checks. Failed retrieval is not proof that a claim is false.

Do not install helpers, elevate the application, mutate real permissions, implement production code, or commit/push. Propose necessary native experiments as bounded qualification work on authorized disposable fixtures.

## Independent review method

If parallel agents are available, give the complete draft to three independent reviewers:

1. Windows/native security and privilege boundaries.
2. Linux/Darwin/mounted/remote semantics and API feasibility.
3. UX interaction, visual/readability contracts, migration, dependencies and completion evidence.

Let them form findings before sharing verdicts. Otherwise perform these as distinct passes and disclose that limitation. Reconcile findings once; do not create another broad review loop.

For each proposed change require: severity, exact plan clause, realistic failure/user impact, supporting code/API evidence, and the smallest effective correction. Distinguish a confirmed defect, an implementation decision still missing, an acknowledged native gate, and optional refinement. Prioritize correctness and executable delivery over more requirements.

## Highest-value questions

- **Do the native routes deliver the promised ordinary workflows?** In particular, adjudicate D1's ordinary non-executable macOS document acquisition. Directory, executable-file and pre-held-descriptor success cannot close that essential gate or justify removing an existing ordinary-document permission workflow. Check whether the acquisition constraints have a feasible supported solution; if not, identify the precise bounded technical/product decision. Do not solve it by silently weakening subject or special-node protections.
- **Are remote editing claims attainable?** Assess R1/R2's binding, field-presence, handle and namespace prerequisites against the pinned libraries and actual server contracts. A successful setter or before/after stat alone does not establish target identity through the write. Conversely, do not mistake a missing FileCat adapter for an OS/protocol limitation. State which ordinary file/directory journeys can actually qualify and which remain explicit gates.
- **Do authority and scope match the operation?** Check Windows denied-DACL acquisition, separately authorized writes, backup/audit/owner privilege distinctions, Unix helper trust, alternate administrators, namespace/mount interpretation, package prerequisites, and volume-root boundaries. Ordinary authorized refresh must remain unelevated.
- **Does the final state match the reviewed intent?** Check compound ownership/mode/ACL/flag ordering, kernel/server side effects, errors after partial writes, read-back under self-lockout, cancellation and reconnect. Earlier component verification cannot substitute for final reconciliation. Do not implicitly restore privilege-bearing bits after ownership-only changes.
- **Can an implementation agent proceed without deadlocks or repeated qualification?** Verify package dependencies, implementation states, operation/subject-specific migration, shared gate subcases, finite acceptance criteria and reopening triggers. Implemented code is not release qualification; unrelated open subcases must not invalidate an accepted slice.
- **Can the existing UI infrastructure deliver §3?** Check persistent Apply/OK/Close/Stop, local Enter ownership and discoverable disabled-state errors against `OverlayDialogService`/`DialogContracts`. Verify bounded list/diff layout, friendly summaries, safe identity display/exact copy, real selected-row/color pairs, and dynamic `ThemeManager`/`ReadabilityPolicy` typography/density without draft or focus loss. The visual contract is a requirement, not rendered evidence. Use focused G-UX cases; avoid a global dialog/theme redesign.
- **Is the plan internally consistent and appropriately scoped?** Preserve native masks/order/propagation, lossless deltas, unresolved identities and detached subjects. Reopen settled details only with concrete evidence.

These are review priorities, not predetermined findings. Do not force an alternative Windows UI architecture or broaden platform/provider scope without a demonstrated problem.

## Editing and completion

Update the same implementation-plan file with only accepted, material corrections. Preserve stable package/gate IDs, correct decisions, useful source references and still-valid evidence. Consolidate superseded wording instead of appending competing rules. Keep implementation state separate from passed native evidence; do not manufacture passes or erase useful prior review provenance.

If a required workflow remains blocked, name its owner/package, exact failed or unrun gate, next bounded action and affected release claim. Do not close an essential journey as an optional exclusion. Ask the user only when a concrete product decision cannot reasonably be resolved from the task.

Return:

1. Verdict: ready to implement, ready with named qualification gates, or blocked by a specific decision.
2. A short prioritized table of accepted changes and reasons, plus consequential rejected findings.
3. The updated plan path and a concise summary of actual remaining blockers/gates.
4. The next eligible work package and any prerequisite decision—without starting implementation.

“No further changes needed” is a valid conclusion. Finish after resolving concrete findings; do not generate work merely to prolong review.
