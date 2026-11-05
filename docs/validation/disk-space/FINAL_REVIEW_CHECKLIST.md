# Independent final review

Required by the user after the implementation and testing loop. **Not performed yet.** This checklist does not close any gate or establish release qualification.

Start a separate review pass from the final requirements, current code, native behavior and attack surfaces. Do not assume an implementation decision or an earlier passing test establishes correctness. Record reproducible findings with severity, trigger, expected behavior, actual behavior and the affected build/source hash. Fix each finding and repeat the relevant tests; perform another review pass if the fix changes the reviewed behavior.

- UX: first use and root capture, understandable measurements and uncertainty, hard-link attribution, filters and unknown cohorts, scoped selection/marks/baskets, preserving defaults, recoverable mistakes, clear unavailable states and accurate outcomes.
- UI: all supported themes, narrow/short windows, large text and DPI, keyboard-only navigation, focus and screen-reader announcements, chart/table/inspector agreement, tooltips, raw-name/spoofing display, clipping/scrolling, modeless ownership and session lifecycle.
- Functionality: every mandatory requirement and packet, state/epoch/revision transitions, metric and component math, identity/alias cases, native filesystem/race/permission/cloud/package cases, stop/pause/hung I/O, save/load/compare/export, cancellation and hard resource limits. Recheck the final build natively on Windows, macOS and Ubuntu when each shared machine is free.
- Security: ordinary authority admission, data-only imports and taint/provenance, parser/graph/numeric/resource bounds, raw locators, no-follow containment, private ACL/mode and publication, exact-entry binding at commit, protected objects, immutable review and job submission, helper boundary/protocol/install trust, content/recipe isolation and actual diagnostics leakage.
- Regression and evidence: existing panel commands and job safeguards, package variants, all required negative controls, final capability registration versus checked-in evidence, and any limitation/unavailable behavior backed by the plan's required feasibility evidence.

Completion requires no unresolved review findings, no unperformed mandatory qualification, and a final report that distinguishes verified behavior from practical limits. Never claim that testing proves the absence of every possible bug.
