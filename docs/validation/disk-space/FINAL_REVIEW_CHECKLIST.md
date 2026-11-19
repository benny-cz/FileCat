# Independent final review

The independent review is in progress. It started after the r1778 regression passed on Windows, macOS and Ubuntu on source `c4a4a928`. The review uses that build and the linked native receipts as its baseline. [Merged regression results](NATIVE_PACKAGES_QUALIFICATION.md).

Start a separate review pass from the final requirements, current code, native behavior and attack surfaces. Do not assume an implementation decision or an earlier passing test establishes correctness. Record reproducible findings with severity, trigger, expected behavior, actual behavior and the affected build/source hash. Fix each finding and repeat the relevant tests; perform another review pass if the fix changes the reviewed behavior.

- UX: first use and root capture, understandable measurements and uncertainty, hard-link attribution, filters and unknown cohorts, scoped selection/marks/baskets, preserving defaults, recoverable mistakes, clear unavailable states and accurate outcomes.
- UI: all supported themes, narrow/short windows, large text and DPI, keyboard-only navigation, focus and screen-reader announcements, chart/table/inspector agreement, tooltips, raw-name/spoofing display, clipping/scrolling, modeless ownership and session lifecycle.
- Functionality: every mandatory requirement and packet, state/epoch/revision transitions, metric and component math, identity/alias cases, native filesystem/race/permission/cloud/package cases, stop/pause/hung I/O, save/load/compare/export, cancellation and hard resource limits. Recheck the final build natively on Windows, macOS and Ubuntu when each shared machine is free.
- Security: ordinary authority admission, data-only imports and taint/provenance, parser/graph/numeric/resource bounds, raw locators, no-follow containment, private ACL/mode and publication, exact-entry binding at commit, protected objects, immutable review and job submission, helper boundary/protocol/install trust, content/recipe isolation and actual diagnostics leakage.
- Regression and evidence: existing panel commands and job safeguards, package variants, all required negative controls, final capability registration versus checked-in evidence, and any limitation/unavailable behavior backed by the plan's required feasibility evidence.

Completion requires no unresolved review findings, no unperformed mandatory qualification, and a final report that distinguishes verified behavior from practical limits. Never claim that testing proves the absence of every possible bug.

## Findings under retest

- The Mac compositor showed a bare byte number in Sunburst. The chart automation names also used “reported units” for every metric. Fix `e4b4510e` adds the active metric and its units to the accessible names and formats the visible scope amount with its units. The focused test covers a known size, an unknown size and a file count.
- Resource limits showed the fixed scratch path. Moving index files to another disk required moving the entire profile with `--data`. Fix `e4b4510e` adds the per-launch `--analysis-scratch` option. Native preparation qualifies the existing local parent before creating an owner-private child. The alternate path retains the profile's shared quota and preserves the existing private boundary. An unavailable selected path falls back to bounded memory. The focused tests exercise both paths, retain ordinary neighboring data and check the `--data` boundary.
- The enabled Standard backends lacked scoped qualified build identifiers. The preview, copy and stable-content rows also lacked evidence links. Fix `e4b4510e` identifies the applicable builds and cites the native receipts for each scoped capability. [Capability evidence](capabilities.json).

The r1787 focused checks have advanced on all three platforms. The complete regression receipts and a fresh native desktop capture are pending. The comprehension study still needs participant responses. [Comprehension study](UX_COMPREHENSION_STUDY.md).
