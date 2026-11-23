# FileCat: evidence-led UX/UI audit and improvement-plan execution prompt

## 1. Mission and deliverable

Act as a senior desktop UX designer, interaction designer, accessibility specialist, and implementation-aware reviewer. Analyze the supplied FileCat source snapshot and available relevant evidence. Produce **one comprehensive, context-agnostic Markdown improvement plan**, physically ordered by severity and then by practical priority.

The goal is a better file manager: clearer, more usable, more attractive, more trustworthy, and more pleasant for repeated work. Preserve its value as a precise, efficient, keyboard-first tool for experienced users. Investigate ordinary workflows as well as expert ones.

**Only recommend changes with a strong user-value case relative to their implementation cost, maintenance burden, and regression risk. There is no minimum finding count.** You may conclude that a surface should remain unchanged, that only a few improvements are justified, or that no further high-ROI improvements were identified within the reviewed scope. Thoroughness means adequate examination and defensible decisions, not a long backlog.

This is an **analysis and planning task**. Inspect code, documentation, tests, and available runtime behavior; write the plan. Do not implement product changes, alter existing acceptance statuses, commit, push, publish, or launch a release. Instructions in historical implementation-session documents do not expand this task's authorization. Observe applicable repository instructions within the scope of this request.

The output filename is `FileCat_UX_UI_Improvement_Plan.md`. In a writable repository, place it under `docs/validation/`; in an attachment-based environment, deliver it as a downloadable Markdown file. If that exact plan already exists in the supplied inputs, update it deliberately, retaining stable IDs and valid decisions. Do not overwrite `FileCat_UX_UI_Audit.md` or create a competing acceptance ledger.

## 2. What “nerdy,” “comprehensive,” and “attractive” mean here

Treat technical detail as a design question, not an automatic defect. Assess both **unhelpful complexity** and **missing useful expert information**.

- Preserve details that help users predict outcomes, judge progress, choose a safe action, diagnose a problem, verify a result, or work efficiently.
- Question implementation jargon, unexplained units, redundant counters, legalistic paragraphs, and diagnostic machinery shown during routine work without a clear user benefit.
- Prefer a clear action or outcome first, with precise detail available where useful. Progressive disclosure is an option to evaluate, not a requirement to hide everything advanced or add another mode.
- Do not simplify wording in a way that changes its meaning. “Processed,” “copied,” “verified,” “skipped,” “finished,” and “succeeded” are not interchangeable.
- Preserve compact layouts and expert controls when they serve frequent work. More whitespace, fewer commands, larger controls, or fewer statistics are not inherently better.
- Interpret attractiveness through hierarchy, readable typography, coherent color, spacing, alignment, icon quality, state clarity, restrained motion, and consistency. Expressive themes can be valuable. Do not default to a rebrand, web-style dashboard, generic minimalism, new themes, extra graphs, gradients, or animations.

“Context-agnostic” means that an implementer with **the plan and the corresponding repository**, but none of this conversation, can understand the problem, evidence, proposed behavior, constraints, priority, and completion criteria. The plan must not depend on “as discussed,” unstated preferences, private chat history, or inaccessible evidence for its central reasoning.

## 3. Starting context: useful leads, not predetermined findings

This prompt was grounded in the attached `FileCat(2).zip` reviewed on 28 October 2026. Its SHA-256 was `9a6014efb643599774e45278214606a4fca1bdd3a396ae708b71934030ab739d`. **This identifies the authoring snapshot only. Re-establish the identity and behavior of whichever sources you actually receive; do not reject a later snapshot or assume these notes remain current.**

The authoring snapshot describes FileCat as a C#/.NET 10/Avalonia desktop file manager with commander-style panels, tabs, explicit operation targets, configurable keyboard commands, custom virtualized controls, and Windows-specific integrations alongside portable Linux/macOS surfaces. `global.json` pins SDK `10.0.101`; `Directory.Packages.props` pins Avalonia `12.1.4`. Read the actual files before relying on those versions or capability claims.

### Existing UX work requires careful reconciliation

The supplied `docs/validation/UX_UI_REMAINING.md`, `UX_UI_ACCEPTANCE_LEDGER.md`, and the opening entry of `UX_UI_IMPLEMENTATION_STATUS.md` **report** the previous 76-finding audit complete, with all required scenarios accepted and final integration gate `GATE-CI-FINAL` accepted for implementation head `42b23e2e` on 28 October 2026. These are repository records, not an independent verification of the current archive or a live CI result. Establish what is current before drawing conclusions.

The original `docs/validation/FileCat_UX_UI_Audit.md` describes earlier implementation problems and an earlier Avalonia version. Do not copy its findings into a new backlog. Use it to understand previous rationale and safeguards, then inspect the relevant current production code and accepted scope.

The eight showcase images in `docs/screenshots/windows/` have a capture record in `CAPTURE.md`: 9 October 2026, source `a2f6cd9109a6b3dfddad7e92e74c5875147c78f9`, four panels, three tabs per panel, and animations disabled. They can illustrate a historical theme or configuration. They cannot by themselves establish current clutter, clipping, defaults, motion, accessibility, or visual regressions. Other evidence images have their own dates and scopes.

Already-present facilities in the authoring snapshot include a compact operation strip, resizable operation details, optional speed history, truthful distinctions between logical progress and engine-reported data processing, a focused toolbar with More, compact Places behavior, density/readability settings, shared dialog layout helpers, and custom accessibility support. Evaluate their quality and remaining gaps; do not propose them as missing without checking.

### Source navigation map

Use this as a starting map. Follow actual callers, bindings, state transitions, and platform implementations; adjust paths when the supplied revision differs. A substantial amount of UI is constructed in C#, so AXAML-only inspection is insufficient.

| Area | Starting paths and symbols |
| --- | --- |
| Product and capabilities | `README.md`, `docs/CAPABILITIES.md`, `docs/IMPLEMENTATION_STATUS.md`, `global.json`, `Directory.Packages.props`; relevant ADRs only as needed |
| Shell, panels, navigation | `src/FileCat.App/Views/MainWindow.axaml`, `MainWindow.axaml.cs`, `MainWindow.Layout.cs`, `PanelView.axaml`, `PanelView.axaml.cs`; `ViewModels/MainViewModel*.cs`, `PanelViewModel.cs`; `Controls/FileListControl.cs`, `PathLine.cs`, `PlaceBarPanel.cs` |
| Operations and telemetry | `src/FileCat.App/Views/OperationsView.axaml` and `.axaml.cs`; `ViewModels/OperationCenterViewModel.cs` (`JobViewModel.Refresh`, `OperationCenterViewModel`), `OperationSpeedHistory.cs`; `Controls/SpeedGraph.cs`; `src/FileCat.Core/Jobs/Job.cs`, `JobModel.cs`, `ProgressEstimator.cs`, `JobManager.cs`, applicable executors |
| Decisions, outcomes, recovery | `src/FileCat.App/Views/OperationDialogs.cs`, `OperationIssueDialog.cs`, `UndoReviewDialog.cs`, `ReportWindow.cs`, `SyncWindow.cs`; `Services/OperationReport.cs`; relevant `MainViewModel.Operations`, `.Undo`, `.Interrupted`, `.Recovery`, `.Clipboard`, and `.Scope` partials |
| Visual system and settings | `src/FileCat.App/Themes/Styles.axaml`; `Services/ThemeManager.cs`, `ReadabilityPolicy.cs`, `Formatters.cs`; `Controls/ThemeEffects.cs`, `DialogLayout.cs`; `Views/SettingsDialog.cs`, `WindowFit.cs`; `src/FileCat.Core/State/StateModels.cs` |
| Input and accessibility | `src/FileCat.Core/Commands/CommandRegistry.cs`; `src/FileCat.App/Controls/FileListAutomation.cs`, `ViewerAutomation.cs`, `DescendantFocusAutomation.cs`; command routing, keyboard reference, tooltips, menus, focus ownership, and platform adapters |
| Other task surfaces | `src/FileCat.App/Views/FindWindow.cs`, `FindDialogs.cs`, `ViewerWindow.cs`, `HexEditorWindow.cs`, `CompareWindow.cs`, `DirectoryDiffWindow.cs`, `BulkRenameReview.cs`, `SettingsDialog.cs`, `AboutDialog.cs`; `Controls/QuickViewPane.cs`, `TextViewer.cs`, `HexView.cs`, `PictureView.cs`, `PageView.cs` |
| Evidence and regressions | `docs/validation/UX_UI_REMAINING.md`, `UX_UI_ACCEPTANCE_LEDGER.md`, `UX_UI_IMPLEMENTATION_STATUS.md`, `UX_UI_SESSION_WORKFLOW.md`; `tests/FileCat.App.Tests/`, relevant Core tests, `tests/FileCat.Rendering.Tests/`, `tests/FileCat.NativeAcceptance/`, supplied captures and evidence receipts |

## 4. Establish the baseline before proposing work

1. Safely inspect the supplied archive or checkout. Record the source filename/hash or available Git revision and dirty state. Do not invent a commit ID for a ZIP or equate a commit mentioned in a document with the archive's identity. Record the review date and any binaries' separately known identity.
2. Read the product overview, current capability/default settings, concise remaining-work index, ledger structure and current statuses. Read the relevant parts of the prior audit and implementation history as candidates emerge. Use searches, headings, symbols, and stable anchors rather than dumping or reproducing the entire historical narrative.
3. Identify actual user journeys, UI entry points, shared controls, resource providers, and supported platform differences. Distinguish code present in the tree from behavior reachable through the product. Inspect the normal default configuration before judging an unusually dense or customized one.
4. Establish available validation: source inspection, supplied receipts, existing tests, build/runtime access, screenshots, native input, and assistive technology. Use the pinned build setup if feasible. When a safe current build and usable UI environment are available, perform a brief first-hand walkthrough of the default workspace, populated operation strip/drawer, a common decision dialog, and a representative adjacent tool. Include relevant keyboard and constrained-layout behavior; choose states that resolve actual questions rather than a full matrix. If runtime access is unavailable, state that limitation and base visual/interaction observations only on inspected supplied evidence; keep unobserved consequences of source behavior explicitly labeled as inferences. Continue useful source analysis rather than pretending to have run the UI or blocking the whole plan.
5. Create a compact coverage map of major surfaces and consequential states. Choose representative combinations based on likely use and specific risks. Do not require every platform × theme × scale × provider × input permutation.
6. For each candidate, search for an existing implementation, related tests, historical finding, accepted scenario, and deliberately excluded refinement. Confirm whether it is new, a concrete regression, an uncovered consequential boundary, a different high-value improvement, already addressed, or only an uncertainty.

Do not reopen accepted work solely because a record is old, a document changed, another case is open, or a different design is possible. A repeat needs a specific reason: relevant code/dependency change, new contradictory evidence, invalid evidence, a materially uncovered boundary, or a bounded reliability question. Name the old ID, new scope, and stopping condition. Keep accepted sibling cases accepted; an analysis task does not mutate their ledger status.

Accepted equivalent routes and deliberate exclusions are legitimate baseline decisions. Examples in the supplied ledger include bounded/pairwise native validation, truthful lower-bound results, and safe external clipboard fallbacks. Do not automatically turn these into requirements for more testing or new functionality.

## 5. Evidence and honesty rules

Every consequential claim needs a traceable basis. Use the following distinctions explicitly in the plan, combining them where appropriate:

| Label | What it establishes |
| --- | --- |
| **Source-confirmed** | Behavior or wiring established in current production code; cite repository path and symbol, with snapshot line references where useful. Explain the causal path rather than inferring a defect from one suspicious line. |
| **Observed now** | Behavior actually reproduced during this review; record source/build identity, OS/backend, profile, theme, density/font/scale, relevant viewport, inputs, result, and evidence location. Record only dimensions that matter. |
| **Historical evidence** | A dated existing result or image for its original build, environment, and scenario. State whether the underlying receipt is available or only referenced. |
| **UX assessment** | A reasoned design judgment tied to an identified surface and user task. State the expected benefit and assumptions; do not present personal taste as measured user research. |
| **Needs validation** | A specific plausible concern whose decisive behavior or impact is not established. Give the smallest check and decision it would resolve; keep it out of confirmed defects. |

A source-confirmed deterministic defect can be actionable without native reproduction. Conversely, source structure alone does not prove rendered clipping, poor visual appeal, screen-reader speech, pointer behavior, timing, or real OS integration. A test file existing, a test inspected, a test executed successfully now, and a current native journey observed are four different facts.

Never fabricate test results, screenshots, measurements, user research, frequencies, effort estimates, or platform parity. A missing environment or receipt is a validation limitation, not automatically a defect. Resolve conflicting evidence by identifying the mismatch in build, configuration, scope, or observation; do not choose whichever supports a finding.

For actual visual conclusions, inspect the actual images or running surface. Record capture provenance. Source-only color checks are limited evidence; inspect relevant composited foreground/background and interaction states. If citing standards, APIs, accessibility guidance, or platform rules beyond the repository, consult applicable current primary documentation and cite it. Explain applicability to this native desktop app; do not mechanically apply web-only requirements or claim product-wide certification from spot checks.

Use disposable, owned fixtures and isolated profiles for any runtime work. Do not operate on the user's real files, credentials, devices, network accounts, or workstation configuration. Inspect available runners before using them and select safe relevant ones. Do not launch destructive recovery/device tests, external communication, or remote mutations to satisfy an audit checklist. Record a bounded validation need when a necessary environment is unavailable.

## 6. Required review areas

Investigate every major area below to a depth justified by user impact and available evidence. Expand coverage when source inspection reveals another consequential surface. The checklist is a search space, **not a list of presumed defects or mandatory features**. Allow “reviewed; retain current design,” “not applicable,” and “not verified” with a short reason.

### A. File operations: complete journey, presentation, and control

Follow the journey from selecting items and choosing a destination through preparation, confirmation, queuing, execution, decisions, completion, and later review. Cover relevant copy, move, rename, bulk rename, delete/recycle, archive, verification, synchronization, and undo/recovery paths that actually exist.

Evaluate whether users can readily answer: **What will happen? To which items? From where to where? What is happening now? Is attention needed? What can I safely do? What completed, what did not, and what remains?**

Inspect the compact strip, expanded drawer, history, reports, related dialogs, notifications, and OS/taskbar feedback as one system. Check hierarchy, allocation of space, stable placement, responsive behavior, action grouping, selected-job identity, multiple jobs, queue ownership, and retention of useful outcomes. Distinguish global, group, selected-job, and current-file scope.

Evaluate source/target identity, long or similar paths, truncation and full-value access, marked versus focused or hidden items, disabled commands and their reasons, ambiguous provider capabilities, and preservation of user choices after invalid input. Inspect confirmation wording, conflict comparison, replace/skip/keep-both choices, scope of “apply to all/similar,” and consequences of retry or elevated execution.

Trace pause, pausing, resume, cancel/stop, stop-after-step, skip, retry, clear/remove history, undo, and recovery actions to actual behavior. Check truthful expectations, ownership, focus return, background decisions, partial outcomes, and persistent inspection. Do not assume Stop means rollback, Undo is complete, retry is idempotent, or removing history is harmless.

Inspect recovery and failure communication for actionability: cause where known, affected scope, preserved/completed work, remaining uncertainty, available next step, and details on demand. Evaluate whether lengthy safety explanations could be clearer without weakening the safeguards they explain.

### B. Progress, speed, ETA, and expert telemetry

First inventory what already exists. In the authoring snapshot, speed history and detailed metrics are implemented. Audit usefulness, semantics, and presentation rather than requiring additional indicators by default.

For a questionable indicator, trace its **unit, numerator, denominator, source, phase, sampling period, and terminal meaning**. Distinguish logical work accounted for from the engine's reported data bytes copied/written or read; include verification and failed/retried work according to the actual counters. Do not describe these counters as physical disk or network throughput without separate measurement. Also distinguish files from items/directories; overall from current-file progress; current from average rate; copying from verification; and operation termination from complete success.

Review representative cases relevant to the implementation: counting/growing or unknown totals, one large file, many small or empty files, same-volume rename, cross-volume transfer, skipped/conflicting items, slow or stalled I/O, parallel/queued work, pause/wait, verification, cancellation, and partial failure. Do not run the full combination matrix; select cases that resolve an actual question.

Check whether percentage, bar, state label, speed, remaining time, elapsed time, and final report agree. Look for false precision, misleading 100%, premature completion, unexplained jumps, stale rates, unstable layout, or an ETA that hides unknown work. Confirm whether a displayed average includes pause/wait time before criticizing or renaming it.

For graphs, assess whether their history window, scale, unit, phase, zero activity, and textual alternative are understandable and worth their space. Inspect flicker, update cadence, animation and rendering cost only where these affect user experience. Do not prescribe an estimator rewrite, smoothing algorithm, graph redesign, benchmark suite, or extra counters without a supported problem.

For existing speed-limit controls, check the unit, per-job or broader scope, effective state, and feedback; identify which phases or providers the limit actually governs. Do not require a new limiter or extra controls merely to fill this checklist.

Preserve truthful existing semantics. For example, changing “planned bytes accounted for” to “copied” could be wrong for skipped files or same-volume moves. If wording is too technical, propose a simpler explanation that retains the distinction and validate it against the actual state model.

### C. Detail, verbosity, terminology, and disclosure

Audit information at the point where the user encounters it: labels, summaries, helper text, tooltips, menus, status bars, reports, confirmations, errors, settings, and recovery explanations. Identify the audience and decision supported by each layer.

Check the balance between immediate essentials, optional expert information, and diagnostic/export detail. Identify repeated sentences, overloaded summaries, ambiguous abbreviations, implementation vocabulary, unexplained restrictions, or long text that hides the action. Also identify useful information that users must guess or cannot retrieve.

Inspect terminology and units across surfaces, literal path and filename fidelity, Unicode/bidirectional/control characters, escaped display versus exact copying, consistent action verbs, pluralization, date/number formatting, and implemented localization boundaries. Do not demand a new localization project or beginner mode without a demonstrated need.

For material copy recommendations, quote the exact current text with its source and provide proposed wording in context. Explain which meaning, safety qualification, or expert detail must remain. Do not produce a large speculative copy rewrite.

### D. Overall usability and interaction behavior

Review discoverability and efficient repetition: first-run/default workspace, navigation, tabs, panel layout and target pairing, marks/selection, filtering/quick search, Find/result sets, breadcrumbs/address entry, command search, menus, toolbar/More, context menus, F-key bar, shortcuts/remapping, command line, clipboard and drag/drop.

Look for inconsistency between pointer and keyboard routes; focused item versus marked selection; current versus captured source/target; supported versus unsupported commands; and UI affordances versus actual behavior. Inspect recovery from errors, cancellation, empty/loading states, retained context, asynchronous updates, stale results, focus stealing, validation feedback, and accidental destructive actions.

Assess task completion, avoidable steps, reachability, response feedback, and repeated cognitive effort. Treat performance as UX when delays, blocking, rendering churn, or missing feedback create a concrete consequence. Preserve virtualized rendering and bounded resource use; do not turn this into an unrelated architecture or performance audit.

### E. Visual hierarchy, attractiveness, color, and visibility

Evaluate the normal default and representative supported theme/density settings. Inspect typography, spacing rhythm, alignment, grouping, border and divider weight, icon scale/legibility, control consistency, visual balance, noise, and the relative prominence of frequent versus exceptional actions.

Inspect semantic distinctions among active panel, target panel, keyboard focus, marked selection, hover, disabled/read-only, success, warning, error, modified content, search hits, and progress. Check text and icon visibility in their actual states, including selection, inactive windows, overlays, tooltips, menus, and high contrast. Do not rely on color alone to explain consequential states.

Consider readability at small supported windows, increased text size, fractional/large DPI scale, long names, many tabs/panels, and realistic populated/error states. Check actual layout constraints and access to clipped content before alleging loss of information.

Assess dark/light/system themes, expressive palettes, reduced motion, live theme changes, and consistency across the shell, dialogs, viewers and comparisons. Inspect small taskbar/window icons and About branding where available and material. Preserve intentional identity; recommend shared token or component corrections before isolated arbitrary colors when a shared root cause is established.

A visual improvement can have high ROI through frequent visibility, clearer hierarchy, readability, or stronger perceived quality. Explain that benefit honestly as a design assessment when no user study exists. Do not dismiss all visual polish as optional merely because it does not cause data loss.

### F. Accessibility and adaptable desktop behavior

Review keyboard-only completion, logical tab order, visible focus, escape/default-action semantics, modifier/remapped shortcuts, pointer targets, scrolling and high-resolution input, screen-reader names/roles/state, custom virtualized controls, live announcements, focus restoration, enlarged text, and reduced motion/high contrast.

Check consequential workflows as well as idle widgets. Preserve distinction between marked and focused items, source and target, progress and outcomes. Avoid overwhelming assistive-technology users with rapidly repeated telemetry. Distinguish accessible-tree/source correctness from actual native assistive-technology behavior.

Consider supported window restoration, constrained working areas, resizing, multimonitor/DPI changes, dialog ownership, and platform conventions. Require native evidence only for the specific boundary that needs it, not as a blanket rerun obligation for every platform.

### G. Adjacent tools and end-to-end consistency

Inspect representative Find, text/hex/picture/page viewer, quick view, comparison/synchronization, bulk rename, checksum/signature, settings/customization, connection/authentication, permissions, report, and About/support surfaces present in the source. Include archives, remote/provider views, result sets, recovery, and Registry where their actual supported behavior changes the user model.

Evaluate search scope, partial versus complete results, current source identity, source-relative selection/copy, mode and zoom state, loading/fallback explanations, unsaved edits, refresh, safe close/cancel, settings preview/apply/persistence, and consistency with the shell. Supported limitations should be understandable; lack of an unrelated capability is not automatically an improvement opportunity.

### H. Polish and cross-cutting consistency

Look for repeated, demonstrable rough edges: clipping, overlaps, misalignment, inconsistent spacing or labels, stale tooltips, misleading disabled states, missing empty/error feedback, visual state lag, unstable control placement, inconsistent keyboard hints, icon ambiguity, or unnecessary modal interruptions.

Group manifestations of one underlying issue instead of multiplying findings. Recommend shared fixes only where they are economical and preserve callers' semantics. Identify existing design strengths that should survive any change. Avoid pixel-level speculation from code and “make it modern/intuitive” recommendations without concrete behavior or visual criteria.

## 7. Decide which candidates deserve a plan

For each candidate, answer these questions before admitting it as recommended work:

1. What current, identifiable task or visible experience suffers, and for whom?
2. What evidence establishes the behavior, and what part is inference or design judgment?
3. Is the problem already addressed, configurable away through a reasonable existing route, or intentionally accepted? If a setting exists, assess whether the default/discoverability still causes real friction rather than assuming availability solves it.
4. What is the smallest coherent correction that materially helps? Does a simpler wording, hierarchy, default, or shared-control fix deliver most of the benefit?
5. What is the likely reach/frequency, with assumptions explicitly marked? Do not invent usage analytics.
6. What are the relative implementation, validation, migration, maintenance and regression costs? What expert capability, safety property, or performance behavior could be lost?
7. Is the expected benefit strong enough to justify those costs now? What observable result would make the change successful?

Use these dispositions:

- **Recommend:** supported problem or well-grounded design improvement with a strong value case and a bounded correction.
- **Validate before deciding:** a material unresolved question with a cheap, decisive check; no implementation commitment yet.
- **Retain current design:** examined and adequate, or the proposed alternative brings no persuasive gain.
- **Defer/not recommended:** a credible idea with insufficient ROI, excessive scope/risk, or a dependency that makes it premature.

Keep the required implementation roadmap restricted to Recommend. List only consequential validation questions and informative rejected/deferred ideas; do not manufacture a second wishlist. Treat severe credible harm seriously even when correction is expensive: identify a containment and the necessary investigation rather than hiding the problem behind a low ROI score.

## 8. Severity and implementation priority

Keep **severity, confidence, effort, and ROI separate**. Use the existing S0–S3 vocabulary where compatible, stating these definitions in the plan:

| Severity | User consequence |
| --- | --- |
| **S0 — Critical** | Demonstrated or strongly source-established serious irreversible harm in a credible reachable workflow, such as wrong-target destructive action or substantial data loss. Reserve for evidence-backed cases; a hypothetical worst case is insufficient. |
| **S1 — High** | Major task failure, materially misleading consequential feedback, loss of important user intent, or exclusion from an essential workflow with no reasonable workaround. |
| **S2 — Medium** | Significant confusion, avoidable mistakes, repeated friction, readability/visibility problems, or impaired completion with a practical workaround. |
| **S3 — Low** | Localized recoverable friction, consistency debt, or visual polish whose individual consequence is limited. Include only when the benefit justifies the cost. |

Physically order the recommended index **and full finding sections** S0 → S1 → S2 → S3. Within a severity band, prefer greater user consequence/reach and stronger benefit relative to effort and risk. Make dependencies and sensible shared batches explicit. Cheapness does not increase severity; cost does not lower it. A quick-win list must not obscure an unresolved high-severity issue.

Use relative effort **S/M/L** with a one-sentence basis; avoid invented delivery dates or hours. Rate evidence confidence **high/medium/low** with justification. Explain ROI qualitatively using benefit, reach, cost and risk; avoid unsupported numerical scoring. For validation-only hypotheses, describe conditional impact rather than presenting a confirmed severity in the defect index.

## 9. Independent agents and adversarial review

When multi-agent capability is available, use **at least three independent reviewers** in parallel with distinct responsibilities. One lead owns reconciliation and the final document. Share the same snapshot identity and evidence rules. Reviewers need not produce any new findings.

Suggested first-pass responsibilities:

1. **Operations and information:** operation journeys, scope, states, progress/speed/ETA, reports, verbosity, and preservation of precise semantics.
2. **Interaction and visual experience:** navigation, discoverability, keyboard/accessibility, hierarchy, themes, color, density, responsive layout, and adjacent tools.
3. **Evidence and ROI skeptic:** existing fixes/accepted scope, source traceability, stale evidence, severity inflation, duplicates, speculative suggestions, cost, and unnecessary process/testing.

After synthesis, send the **actual draft plan** to these reviewers for adversarial review. Require explicit challenges to its strongest assumptions and recommended changes:

- Is this still a problem in the current implementation, and can you find contrary evidence?
- Is the user consequence concrete, or is this preference presented as a defect?
- Is the severity warranted? Are uncertainty and platform/configuration limits honest?
- Would this remove valuable expert information, weaken safety, harm keyboard flow, increase maintenance, or create disproportionate tests?
- Is there a smaller correction? Does an existing feature already solve it?
- Can a fresh implementer execute and verify this section without prior conversation?
- What should be removed, merged, narrowed, deferred, or explicitly left unchanged?
- Did caution about evidence cause a real high-value behavior or visual opportunity to be overlooked?

The lead must resolve material objections through code/evidence or narrower wording, not majority vote. Verify source anchors for high-severity or disputed claims. Keep a compact review-disposition table for material challenges and how they changed the plan; do not paste transcripts or invent disagreement.

Run another focused pass only if a material unresolved disagreement could change the recommendation. Stop once that risk is resolved. If agents are unavailable, disclose that limitation and perform distinct review passes yourself; never claim independent reviews occurred when they did not.

## 10. Required structure of the Markdown plan

Write detailed, implementable findings without padding. Scale the document to the supported work: a few strong findings need careful detail, not artificial breadth. If none qualify, retain the overview, evidence limits, coverage and no-change conclusion, and omit empty finding scaffolding.

### 10.1 Executive assessment

State the decision early: what deserves work now, why, and what should be preserved. Give the number of recommended findings by severity, separate validation-only questions, and an honest scope-limited conclusion. Do not imply that this review certifies the whole product or supersedes release qualification.

### 10.2 Baseline, product model, and evidence limits

Record snapshot/build identity, review date, actual validation performed, missing prerequisites, current-versus-historical evidence, and meaningful platform/configuration limits. Explain the relevant FileCat model and terms: active/source versus target panel, focused versus marked items, provider, operation/group, and complete versus partial outcome where needed.

Summarize existing acceptance status as records report it and explain how new proposals relate. Include an implementation map sufficient for a fresh contributor; avoid reproducing broad architecture documents. List important existing strengths/safeguards.

### 10.3 Coverage matrix

For each major area, record inspected paths/journeys, evidence level, supported conclusion, and material unverified boundary. Include reviewed areas with no recommended change. Distinguish “source inspected,” “observed at runtime,” “historical evidence only,” “not applicable,” and “not inspected.” A long checklist is not proof of coverage.

### 10.4 Severity-ordered recommendation index

Assign stable new IDs such as `FCUX-NEXT-001`, checking for collisions first. Do not reuse or renumber historical `FCUX-*`, `AC-*`, or queued-defect IDs. IDs remain stable when priority changes.

Use a compact table containing ID, severity, concrete problem/proposed outcome, evidence confidence, ROI/effort/risk, and dependencies or recommended sequence. Cross-reference related historical IDs in the finding sections. This index is the plan's single recommendation list; do not repeat contradictory priorities elsewhere.

### 10.5 Self-contained finding specifications

For each recommended finding provide the following, combining headings where that improves readability:

1. **ID, title, severity, confidence, disposition, effort, ROI rationale.** State whether this is a new improvement, a narrowly evidenced regression, or a newly established boundary of previous work.
2. **Affected surface and scenario.** Identify the user task, relevant platform/provider/configuration, starting state, input sequence, current result, and meaningful user consequence. Mark a source-derived scenario as unexecuted when appropriate.
3. **Evidence and current implementation.** Cite repository paths and symbols, relevant snapshot lines, bindings/state transitions, and available tests/captures/receipts. Explain what each proves and does not prove. Quote only the small code/text fragment needed to make the claim reviewable.
4. **Prior-work reconciliation.** Name related old finding/case IDs, accepted behavior and current safeguards; explain exactly why this is additional work. State “no related prior case found” only after searching.
5. **Smallest useful correction and resulting behavior.** Describe the desired user-visible change concretely. Specify default versus expanded detail, labels, ordering, states, visibility, focus, recovery or layout behavior as relevant. Give exact proposed copy for material wording changes. Do not prescribe dimensions, a new abstraction, or a design pattern without justification.
6. **Implementation direction and boundaries.** Name likely components/callers, reuse opportunities, dependencies, and behavior to preserve. Identify any model/capability change required. Separate required outcomes from optional implementation approaches; do not write speculative production code.
7. **Acceptance criteria.** Use finite observable scenarios, preferably Given/When/Then. Include the main success behavior and consequential failure/boundary states. Tie criteria to the user problem, not to implementation constants or every possible combination.
8. **Validation and completion.** Identify existing coverage to reuse, the smallest necessary deterministic/component/native checks, required environment, and what would count as completion. Distinguish tests actually run now from future checks. For a presentation fix, specify before/after visual review at the relevant configuration as a future implementation acceptance check; retain an observed current baseline when available. For behavior, require future validation of the resulting action/data rather than only a label. Do not implement a fix or invent an after-state to satisfy this planning task. If a mockup helps resolve a material design question, label it as a proposal, never as evidence of implemented behavior.
9. **Tradeoffs, regression risks, and alternatives.** Include only material risks or rejected alternatives that influence the decision. Specify migration/compatibility/persistence implications when relevant. Do not add an optional enhancement backlog to every finding.

Shared invariants may live in one section with precise internal links. Each finding must still contain enough local context to act on it; a path reference alone is not an explanation.

### 10.6 Implementation batches and finite acceptance tracking

Group recommended work into the smallest coherent batches, respecting severity and dependencies. Each batch states included IDs, intended user benefit, scope boundary, prerequisite, and completion condition. Do not invent sprint schedules, staffing, or mandatory process machinery.

When later implementation tracking is useful, define a concise row format:

`stable scenario ID | finding | exact scope | status | tested build/environment | direct evidence | exact remaining boundary`

In this analysis-only run, proposed fixes remain proposed; do not mark them implemented or accepted. Keep the existing repository ledger intact. Recommend integration into its established workflow when implementation is actually authorized. Store detailed receipts separately when produced and link them; do not copy long histories into the plan.

Repeating a passed case requires a concrete reason and bounded stopping condition. A failed new subcase does not invalidate unrelated accepted scope. Tests should address meaningful user-visible invariants; avoid new test frameworks, exhaustive matrices, or broad reruns for harmless copy/spacing changes without a concrete risk.

### 10.7 Validation-only questions and deliberate non-changes

Keep material unknowns separate from recommendations. For each, state evidence so far, conditional impact, smallest decisive check, required environment, and how either result changes the decision. Do not disguise missing runtime access as a product issue or a large mandatory follow-up program.

Summarize important retained design choices and rejected/deferred ideas, with brief reasons. This is where to state that an inspected area needs no further improvement. Record only decisions that help prevent churn; a large discard catalogue is unnecessary.

### 10.8 Review disposition and final stopping condition

Record material agent challenges, their resolution, and any remaining consequential disagreement. Explicitly distinguish independent agents from simulated review passes.

Finish when major relevant surfaces have been examined, every immediate implementation recommendation has survived evidence/prior-work/ROI checks and has an actionable specification, the plan is usable without this conversation, and unresolved material questions are explicitly bounded in the validation-only section. Do not keep expanding the audit to resolve questions that are outside the available environment or unlikely to change a decision.

## 11. Final quality gate

Before delivering, check that:

- The review actually examined current implementation and acknowledges the prior audit's recorded closure.
- No recommendation simply reintroduces an existing facility or an accepted historical issue without new evidence.
- Operation semantics, useful expert detail, keyboard workflows, accessibility, data safety, and bounded performance are preserved.
- Requested areas received meaningful coverage, including visual quality and polish, not only correctness and regression testing.
- Each recommendation has a concrete user benefit, evidence, severity rationale, minimal scope, implementation anchors, and finite acceptance criteria.
- Severity and ROI are separate; low-confidence concerns are not dressed up as confirmed critical defects.
- The index and full findings have the same physical severity/priority order; counts, IDs and dependencies agree.
- All referenced paths/symbols exist in the reviewed snapshot, or are explicitly labeled proposed/missing/historical. Links and internal anchors resolve; no finding relies on private conversation context.
- Executed checks, historical results and proposed checks are clearly separated. No native, visual, platform, CI, or user-research result is invented.
- The outcome may legitimately be a short plan or no recommended changes. No issue quota, decorative redesign, artificial backlog, or open-ended validation campaign was added.

Deliver the Markdown plan and a concise chat summary of the strongest recommendations, the main preserved strengths, validation limits, and the result of independent review. If nothing qualifies, say: **“No further high-ROI improvements were identified within the reviewed scope,”** then name that scope and its limitations. Do not substitute a generic checklist for the completed analysis.
