# FileCat 1.0.0 — Release Readiness, Validation, UX Audit, and GitHub Release Planning

FileCat has already been substantially implemented from an extensive product, architecture, UX, security, testing, validation, and implementation plan.

The next task is **not to implement FileCat and not to execute the release campaign**.

The task is to produce one comprehensive, evidence-driven, context-agnostic Markdown document that defines exactly how the current FileCat implementation must be:

1. reconciled against the complete FileCat planning corpus;
2. audited for implementation completeness and correctness;
3. validated functionally and under realistic failure;
4. reviewed for data safety, security, architecture, performance, licensing, dependencies, and packaging;
5. evaluated practically for UX/UI quality;
6. tested for accessibility and international input;
7. live-tested on the required target environments;
8. remediated where evidence reveals problems;
9. revalidated after remediation;
10. frozen into an exact release candidate;
11. qualified through an evidence-backed human GO / NO-GO decision;
12. and, only after explicit GO, published as **FileCat 1.0.0 stable on GitHub**.

The resulting Markdown document will later guide a separate execution phase performed by humans and/or AI agents.

It must be usable without access to this conversation.

---

# 1. Objective

The final plan must make it possible to answer, with defensible evidence:

> Was the intended FileCat actually implemented?

> Does every shipped capability behave according to its current product contract?

> Do FileCat's Product Invariants and Architecture Invariants hold in the real implementation?

> Are destructive and security-sensitive operations safe and truthful about their guarantees?

> Is FileCat responsive and scalable under the workloads it claims to support?

> Is the actual UX understandable, predictable, efficient, and usable?

> Does accessibility work in practice?

> Does FileCat behave correctly on every platform and architecture for which 1.0.0 makes a support claim?

> Are the exact release artifacts proposed for publication the artifacts that were actually qualified?

> Can this exact candidate responsibly be called FileCat 1.0.0 stable?

The plan must not try to prove FileCat is ready.

It must define how to determine whether FileCat is ready.

---

# 2. Deliverable

Produce **one self-contained Markdown release-readiness and execution-planning document**.

It must combine:

- current-product reconstruction;
- implementation completeness audit;
- evidence audit;
- validation-gap analysis;
- remediation workflow;
- practical UX/accessibility evaluation;
- cross-platform live-testing plan;
- release-candidate qualification;
- GO / NO-GO criteria;
- GitHub publication;
- post-release verification.

Do not produce several disconnected plans.

Do not produce generic software-QA boilerplate.

Do not merely summarize the existing FileCat architecture document.

The final document should be detailed enough that a capable future execution agent can act on it without needing to reinterpret the original planning intent.

---

# 3. Planning-only boundary

During this task, do **not**:

- edit source code;
- modify tests;
- modify documentation;
- modify project files;
- change dependencies;
- fix defects;
- refactor;
- generate new test fixtures;
- build FileCat;
- run FileCat;
- execute tests;
- execute benchmarks;
- perform UI automation;
- trigger CI workflows;
- perform live platform testing;
- perform destructive filesystem, Registry, privilege, or recovery testing;
- create installers or release artifacts;
- sign or notarize artifacts;
- create or push release tags;
- publish packages;
- publish GitHub Releases.

Read-only investigation is expected.

You may inspect existing:

- source;
- tests;
- benchmarks;
- documentation;
- ADRs;
- validation reports;
- Git and GitHub history;
- CI configuration and history;
- build/package configuration;
- dependencies;
- existing execution evidence;
- existing artifacts.

Use authoritative current external documentation where release correctness depends materially on current:

- OS behavior;
- platform support;
- framework behavior;
- signing/notarization;
- packaging;
- dependency licensing;
- security advisories;
- lifecycle/support policies.

Do not create new runtime evidence during this task.

---

# 4. Investigation honesty

Never claim to have inspected, verified, executed, or observed something that was unavailable.

If a repository area, GitHub resource, external service, document, artifact, or history source cannot be accessed:

1. state the limitation explicitly;
2. explain what uncertainty it creates;
3. continue with the planning task where possible;
4. add the missing inspection or evidence as a future execution prerequisite.

Do not invent:

- repository contents;
- runtime results;
- CI outcomes;
- issue history;
- artifact contents;
- platform behavior;
- signing state;
- release history.

Unknown is an acceptable result.

Fabricated certainty is not.

---

# 5. Mandatory inputs

Inspect the complete relevant repository.

At minimum locate and inspect, where present:

- `FILECAT_PRODUCT_ARCHITECTURE_AND_IMPLEMENTATION_PLAN.md`;
- `docs/IMPLEMENTATION_STATUS.md`;
- ADRs;
- `docs/validation/` and equivalent validation records;
- requirement and decision logs;
- source projects;
- test projects;
- benchmark projects;
- fixtures/corpora;
- CI workflows and relevant history;
- build and packaging scripts;
- signing configuration;
- dependency manifests and lock files;
- README;
- user documentation;
- keyboard documentation;
- SECURITY documentation;
- CONTRIBUTING documentation;
- changelog/release notes;
- relevant issues and pull requests;
- existing releases/prereleases.

Do not assume a document is current merely because its filename or heading says `Final`.

---

# 6. Repository snapshot

Record the exact repository state analyzed:

- repository identity;
- branch;
- HEAD commit SHA;
- working-tree status;
- relevant submodule state;
- dependency-lock state;
- version metadata;
- current release configuration;
- relevant CI configuration.

If the working tree is dirty, state that explicitly.

Do not treat a dirty or ambiguous repository state as an already reproducible release baseline.

---

# 7. Separate product intent from implementation truth

The FileCat planning corpus evolved during implementation.

It contains:

- historical phases;
- confirmed requirements;
- later product-owner decisions;
- ADR outcomes;
- implementation claims;
- technical validations;
- deferred items;
- functionality implemented beyond the original v1 boundary.

Use two distinct precedence systems.

## 7.1 Product-intent precedence

When determining what FileCat is intended to do today:

1. latest explicit product-owner decision;
2. current confirmed requirement or Product/Architecture Invariant;
3. accepted ADR outcome;
4. later implementation/validation documentation explicitly recording an intentional product change;
5. earlier architecture recommendation;
6. historical roadmap description;
7. candidate/example/superseded recommendation.

Later confirmed decisions may supersede historical phase boundaries.

If equally authoritative decisions materially conflict, record the contradiction.

Do not silently choose one.

## 7.2 Implementation-truth precedence

When determining what FileCat actually implements:

1. current source, configuration, and package composition;
2. implementation-specific tests and fixtures;
3. relevant current execution/CI/validation evidence;
4. implementation-status documentation;
5. planning-document labels such as `Implemented`, `Done`, or completed phase.

Labels are claims to investigate, not proof.

---

# 8. Confirmed decisions cannot be silently weakened

If audit evidence shows that a confirmed product commitment is incomplete, difficult, unsafe, expensive, or inconsistent with the implementation:

1. identify the gap;
2. determine release impact;
3. identify remediation alternatives;
4. identify the relevant trade-off;
5. require an explicit product-owner decision before weakening or removing the commitment.

Do not silently reclassify a confirmed capability as:

- experimental;
- deferred;
- optional;
- unsupported;

merely to make 1.0.0 pass.

Do not solve release failures through **scope laundering**.

Feature removal or support downgrade is a product decision, not ordinary remediation.

---

# 9. Reconstruct the current 1.0.0 product before designing validation

Do not assume:

> historical v1 = P1–P3 = current 1.0.0.

Historical phases are provenance.

FileCat subsequently adopted and implemented additional functionality.

First reconstruct the current product.

---

# 10. Current Release Capability Manifest

Create a Current Release Capability Manifest covering every significant present or currently committed capability.

For each capability determine independently:

| Dimension                   | Meaning                                   |
| --------------------------- | ----------------------------------------- |
| Product intent              | What current decisions require            |
| Implementation claim        | What documentation claims exists          |
| Code present                | Relevant implementation exists            |
| Reachable                   | Ordinary release users can invoke it      |
| Enabled                     | Enabled in intended release configuration |
| Documented                  | Public/user documentation claims it       |
| Intended for 1.0.0          | Current release contract includes it      |
| Platform/architecture scope | Where support is intended                 |
| Existing automated evidence | Relevant tests/CI/benchmarks              |
| Existing live evidence      | Existing runtime/real-platform evidence   |
| Missing evidence            | Validation still required                 |
| Release impact              | Effect of failure on 1.0.0                |

Never collapse these dimensions into one `Implemented` flag.

---

# 11. Audit the entire planning corpus

The user explicitly requires reconciliation of the **complete FileCat plan**, not only historical v1.

Every significant item must receive a current disposition.

Classify items as appropriate:

- shipping in 1.0.0;
- implemented but intentionally not shipping;
- fully implemented;
- partially implemented;
- implemented differently but acceptably;
- implemented beyond historical scope;
- currently incomplete;
- intentionally deferred;
- experimental;
- superseded;
- explicitly abandoned;
- non-goal;
- obsolete;
- contradictory;
- unable to determine.

Auditing an item does not automatically make it a 1.0 blocker.

Historical placement outside v1 does not exempt a capability that is now shipping.

---

# 12. No-orphan reconciliation rule

Every identifiable item in these categories must receive an explicit disposition:

- every Requirement ID;
- every Product Invariant;
- every Architecture Invariant;
- every ADR;
- every `TV-*` validation;
- every `D-*` decision;
- every known capability gap;
- every explicitly deferred item;
- every explicit non-goal;
- every feature marked `Implemented`;
- every feature marked `In progress`;
- every feature marked `Planned`;
- every release-significant risk.

They do not each need a dedicated chapter.

They may not silently disappear.

The final document must make orphaned requirements or validations easy to detect.

---

# 13. Shipping-surface rule

Any capability reachable by an ordinary user in the proposed stable 1.0.0 build belongs to the release-quality surface even if the historical roadmap called it P4–P11, post-v1, or later.

If it ships as stable functionality, it must meet stable-release quality appropriate to its claims.

Developer-only, disabled, or explicitly experimental functionality may have a different contract, but must still be reviewed where it affects:

- security;
- dependencies;
- startup;
- configuration;
- packaging;
- release artifacts.

---

# 14. Evidence vocabulary

Use these terms consistently.

### Requirement

Current product/architecture obligation.

### Implementation claim

Documentation claims something exists.

### Verified static fact

Established through current repository inspection.

### Existing automated evidence

Relevant test/benchmark/CI evidence exists.

### Historical evidence

Execution evidence belongs to an older commit/build/environment.

### Existing live evidence

Real runtime/platform validation has already occurred.

### Planned validation

Must be performed during the later execution phase.

### Gap

Evidence demonstrates missing or incorrect behavior.

### Suspected gap

Evidence is insufficient.

### Intentional divergence

Implementation deliberately differs from an older recommendation.

### Known limitation

A supported capability has a bounded, truthful limitation.

### Deferred

Intentionally outside the release contract.

### Release blocker

Must be resolved before stable publication.

Do not confuse:

- test code with passing tests;
- CI configuration with successful CI;
- source implementation with correct runtime behavior;
- accessibility properties with accessible UX;
- successful compilation with platform support.

---

# 15. Evidence-pointer standard

For every important static conclusion retain enough provenance to find the evidence again.

Use concrete references such as:

- repository path;
- symbol/type/method where useful;
- test name;
- ADR ID/file;
- Requirement ID;
- Decision ID;
- validation report;
- Git commit;
- issue/PR number;
- CI workflow/run;
- package/dependency identity.

Avoid vague conclusions such as:

> The implementation appears complete.

Prefer:

> Implementation claim: D-XX. Static evidence: `src/...`, type `...`. Automated evidence: `tests/...`. Runtime validation remains required.

The purpose is auditability, not unnecessary line-number ceremony.

---

# 16. Evidence freshness

For existing results proposed for reuse determine:

- source commit;
- build identity;
- date where material;
- OS/platform/architecture;
- configuration;
- fixture/corpus version;
- applicable capability;
- whether later changes invalidate it.

Reuse valid evidence where defensible.

Do not rerun work purely for ceremony.

Do not reuse stale evidence merely because it is green.

---

# 17. Preserve original acceptance mechanisms

Existing feature-specific acceptance mechanisms and `TV-*` validations are important minimum evidence where still applicable.

Do not replace a specific original validation such as:

- physical platform testing;
- real filesystem semantics;
- privilege-boundary testing;
- screen-reader testing;
- zero-write recovery validation;
- hostile-input testing;

with a weaker generic smoke test.

If a historical validation is obsolete, explain:

- why;
- what supersedes it;
- why the replacement provides equal or stronger evidence.

---

# 18. Close every `TV-*` explicitly

For each `TV-*` determine:

- original uncertainty;
- current implementation relevance;
- existing evidence;
- whether it is satisfactorily closed;
- whether later changes made the evidence stale;
- remaining execution work;
- required environment;
- release impact.

No `TV-*` may disappear merely because its feature was implemented.

Implementation completion and release qualification are separate states.

---

# 19. Required planning sequence

The final Markdown document must prescribe work in this dependency order.

## Phase A — Baseline reconstruction

Inspect repository and evidence.

Produce:

- repository snapshot;
- source inventory;
- evidence inventory;
- current decision inventory.

No execution.

## Phase B — Product reconstruction

Produce:

- Current Release Capability Manifest;
- current platform/architecture contract;
- historical-vs-current scope reconciliation;
- list of incomplete/planned/in-progress commitments.

## Phase C — Static implementation audit

Map current implementation to product intent.

Identify:

- missing implementation;
- partial implementation;
- unreachable/dead paths;
- hidden/disabled capabilities;
- documentation drift;
- architecture divergence;
- areas requiring runtime proof.

## Phase D — Existing evidence audit

Map current tests, CI, benchmarks, validation records, and issue history to the manifest and requirements.

## Phase E — Validation-gap derivation

Determine what remains to be proven and why.

## Phase F — Pre-release remediation

Later execution uses:

`validate`\
→ `discover`\
→ `reproduce`\
→ `understand`\
→ `remediate`\
→ `targeted regression`\
→ `affected regression`

Development/candidate builds may be used here.

## Phase G — Release-contract freeze

Once blockers and intended scope are resolved:

- freeze functionality;
- freeze platform support claims;
- freeze artifact set;
- establish immutable candidate source identity.

Do not add unrelated features after this gate.

## Phase H — Final candidate qualification

Run release-critical qualification against the exact candidate.

## Phase I — Release Evidence Package and human GO / NO-GO

Consolidate evidence and request approval.

## Phase J — Publication and post-release verification

Only after GO.

---

# 20. Preflight resource check

Before future execution starts, verify availability of all release-critical resources.

Include where applicable:

- physical Windows x64 machine;
- physical Apple Silicon Mac;
- Ubuntu x64 VM;
- physical Windows ARM64 device if ARM64 will ship as supported;
- filesystem/media fixtures;
- SMB environment;
- controlled SFTP/FTP servers;
- disposable privilege/Registry environments;
- recovery images/media;
- assistive technologies;
- signing infrastructure;
- Apple signing/notarization capability;
- GitHub publication permissions;
- required CI runners/accounts.

Missing prerequisites must be visible early.

Do not discover them after final candidate freeze.

---

# 21. Requirement, invariant, ADR, and decision audit

For every relevant Requirement ID, Product Invariant, Architecture Invariant, ADR, and current product decision determine:

- current meaning;
- release relevance;
- implementation mechanism;
- existing evidence;
- missing evidence;
- current disposition;
- release impact.

Do not infer completion from a documentation status field alone.

A `Decided` ADR is not runtime proof.

An `Open` ADR is not automatically a release blocker.

Architectural implementation may legitimately differ from an original recommendation if the required guarantee is preserved.

Do not require architectural conformity for its own sake.

---

# 22. Test-suite audit

Inspect existing tests rather than trusting them automatically.

Look for:

- implementation-mirroring tests;
- weak assertions;
- excessive mocks around OS/provider semantics;
- skipped/ignored tests;
- flaky tests;
- stale fixtures;
- platform tests never running on their actual platform;
- missing persistence checks;
- missing cancellation/failure paths;
- weak concurrency/race coverage;
- UI tests bypassing real input;
- accessibility tests without assistive technology;
- benchmarks no longer representative of current behavior.

Code coverage is supporting evidence, not an objective by itself.

---

# 23. Independent oracle rule

Where FileCat interprets external formats or system state, avoid using FileCat itself as the only oracle.

Where practical, cross-check against independent trusted mechanisms.

Examples include:

- filesystem metadata against native APIs/tools;
- checksums against independent implementations;
- signatures against independent verification tools/APIs;
- PE/ELF/Mach-O structures against independent readers;
- Registry data against native APIs/tools;
- archives against independent readers;
- recovery fixtures against known ground truth;
- file-operation outcomes against direct filesystem inspection.

Avoid circular validation.

---

# 24. Standard validation-item template

For every release-significant validation activity specify enough information that another agent can execute it without guessing.

Use a compact structure containing:

- **ID**
- **Objective / requirement being proven**
- **Risk or failure being detected**
- **Prerequisites**
- **Environment/platform**
- **Fixture/input**
- **Actions or test charter**
- **Independent oracle where applicable**
- **Expected result**
- **Explicit pass criteria**
- **Explicit fail criteria**
- **Evidence to retain**
- **Release-blocking consequence**
- **Remediation path if failed**
- **Revalidation required after remediation**
- **Dependencies / what may run in parallel**

Do not write vague items such as:

> Test this thoroughly.

or:

> Verify security.

---

# 25. Use existing repository workflows where possible

Where the repository already contains an authoritative:

- build command;
- test command;
- benchmark invocation;
- package script;
- release script;
- CI workflow;
- validation harness;

reference that existing mechanism in the plan.

Do not invent an alternative command merely to make the document look complete.

If no suitable execution mechanism exists, specify the required **intent and evidence**, and mark creation of the missing harness/script as future remediation or execution work.

Do not invent shell commands, paths, project names, package names, or scripts that are not supported by repository evidence.

---

# 26. Functional validation

Derive workflows from the actual shipping Capability Manifest.

Likely families include, where shipped:

- startup;
- navigation;
- panels/tabs/workspaces;
- selection;
- source/target;
- F3–F8;
- copy/move/delete;
- queues/conflicts;
- search/result sets;
- comparison/synchronization;
- archives;
- Registry;
- remote resources;
- external tools;
- text/hex;
- metadata;
- checksums/signatures;
- hidden streams/attributes;
- filesystem records/journals;
- structured viewers;
- recovery;
- packaging/platform integration.

This list is illustrative, not exhaustive.

Derive additional validation from actual shipped functionality.

---

# 27. Failure and fault validation

Derive faults from actual implementation boundaries.

Include where relevant:

- access changes;
- disappearing source/destination;
- full storage;
- media removal;
- network disconnect/latency/hang;
- concurrent mutation;
- name/case collisions;
- Unicode/path aliases;
- symlink/reparse replacement;
- watcher overflow/loss;
- antivirus/security interference;
- malformed structured input;
- parser/worker/broker failure;
- process termination;
- shutdown/logoff;
- temporary-storage exhaustion;
- uncertain remote outcome;
- stale resource identity;
- external-editor replacement behavior.

Validate both user-visible state and residual resource state.

---

# 28. Data-safety validation

Treat data-safety failures as release critical.

Validate where shipped:

- copy;
- move;
- guarded source deletion;
- replacement;
- overwrite;
- recycle/trash;
- permanent deletion;
- conflicts;
- journaling/reconciliation;
- archive extraction/update;
- remote transfer/edit commit;
- Registry mutation;
- hex saving;
- recovery destination safety;
- raw-source read-only behavior.

Never accept:

- false success;
- silent data loss;
- silent permanent deletion;
- invented atomicity;
- invented rollback;
- uncertain outcome reported as success.

---

# 29. Security and trust-boundary review

Review actual shipped boundaries.

Derive validation from components such as:

- parser/viewer workers;
- native parsers/libraries;
- archives;
- Shell integration;
- privileged broker;
- IPC;
- raw-device access;
- external commands;
- browser/HTML preview;
- remote protocols;
- credentials;
- temporary files;
- Registry;
- recovery;
- download-origin metadata;
- signature/checksum presentation.

Validate the actual claimed containment.

A child process is not automatically a sandbox.

Signing is not a substitute for security review.

---

# 30. Dependency, provenance, and licensing audit

For every shipped dependency/component record:

- exact version/commit;
- artifact/package;
- license/SPDX;
- mixed-license content where relevant;
- transitive dependencies;
- native components;
- RID/architecture coverage;
- security advisories;
- maintenance status;
- notices/source obligations;
- build provenance;
- replacement boundary.

Verify:

- distribution-policy compatibility;
- SBOM;
- third-party notices;
- required license texts;
- signing-policy compatibility.

---

# 31. Performance and scalability

Audit each applicable performance commitment.

Determine:

- target;
- current relevance;
- existing evidence;
- final workload;
- environment;
- methodology;
- pass/fail threshold.

Relevant workloads may include:

- startup;
- first rows;
- keyboard latency;
- panel/tab switching;
- scrolling;
- memory;
- million-entry directories;
- sort/filter;
- metadata;
- huge-file access;
- comparison;
- large-file copy;
- small-file copy;
- remote operations;
- Registry;
- archives;
- recovery.

Do not silently weaken thresholds to make the release pass.

An evidence-backed target revision is an explicit decision.

---

# 32. Practical UX/UI audit

UX must be evaluated using the running product during execution.

Do not infer usability from source, XAML, screenshots, or architecture alone.

Evaluate:

- first-run comprehension;
- everyday workflows;
- multi-panel source/target clarity;
- command discoverability;
- keyboard efficiency;
- mouse efficiency;
- conflict resolution;
- destructive confirmations;
- operation feedback;
- partial/error/uncertain states;
- advanced-feature discoverability;
- visual hierarchy;
- long-session readability;
- theme consistency.

Do not redesign working UX merely because another design appears more modern.

---

# 33. Human usability evidence

Some questions require real users.

Where practical include:

- experienced Commander-style users;
- technically competent FileCat newcomers;
- users familiar with each tested OS;
- assistive-technology users for accessibility-critical flows.

Do not teach first-run participants FileCat's internal mental model before evaluating whether the UI communicates it.

Do not invent statistical confidence from tiny samples.

The goal is concrete usability evidence.

---

# 34. UX issue format

Each significant UX finding must record:

- task;
- participant/context;
- observed problem;
- user consequence;
- severity;
- affected users;
- likely cause;
- possible remediation;
- revalidation method.

Avoid statements such as:

> This dialog could be improved.

without evidence.

---

# 35. Manual exploratory testing

Scripted acceptance tests alone are insufficient for a complex interactive file manager.

Include bounded exploratory charters on every mandatory platform.

Focus on combinations such as:

- rapid navigation;
- mixed keyboard/mouse use;
- state changes while jobs run;
- unusual filenames;
- interrupted operations;
- resizing/scaling;
- many tabs/panels;
- uncommon but reachable commands.

Exploration supplements requirement-driven testing.

It does not replace it.

---

# 36. Keyboard, mouse, accessibility, and input

Validate complete keyboard and mouse paths for core workflows.

Keyboard validation should cover where relevant:

- F3–F8;
- modifiers;
- tabs/panels;
- source/target;
- selection;
- quick search/filter;
- Find;
- command search;
- conflicts;
- operation center;
- history/bookmarks;
- viewers/comparison;
- Escape behavior;
- rapid input and auto-repeat;
- focus restoration;
- international layouts;
- IME;
- laptop function-key behavior.

Mouse validation should cover:

- selection;
- navigation;
- drag/drop;
- panel docking;
- tabs;
- context menus;
- target selection;
- dialogs;
- operation center;
- viewers.

Accessibility validation should cover:

- real screen readers;
- accessible names/states;
- virtualized controls;
- focus order;
- keyboard-only use;
- high contrast;
- reduced motion;
- DPI/scaling;
- non-color-only state;
- IME;
- Unicode;
- combining marks;
- emoji;
- bidi/control characters;
- long strings;
- localization-safe layouts.

---

# 37. Mandatory live-test environments

The execution campaign must include:

## Windows x64

A **physical Windows machine** using an officially supported Windows 11 release.

Windows receives the deepest validation.

## macOS

A **physical Apple Silicon Mac** using the intended supported macOS version.

## Ubuntu

A **fresh Ubuntu x64 virtual machine** using the intended supported Ubuntu version.

These environments are mandatory for the release-readiness campaign.

The resulting support tier may differ among them, but FileCat must not make unsupported claims.

---

# 38. Windows ARM64 conditional gate

If a Windows ARM64 artifact is intended to be published as a supported stable 1.0.0 asset, require validation on a **physical Windows ARM64 machine**.

The following are supporting but insufficient substitutes:

- cross-compilation;
- ARM64 CI;
- automated startup;
- x64 emulation.

If physical ARM64 qualification cannot be obtained, require an explicit product/release decision to:

- omit the stable ARM64 artifact;
- classify it differently if current policy permits;
- or obtain hardware validation.

Do not silently claim stable ARM64 support without real hardware evidence.

---

# 39. Validate platform semantics, not fake parity

Expected behavior differs by platform.

Examples include:

- Registry;
- recycle/trash;
- ACLs/xattrs;
- filesystem identity;
- hex-save concurrency guarantees;
- browser engines;
- Shell integration;
- raw-device authorization;
- signing/notarization;
- accessibility backends.

A platform passes when FileCat behaves correctly and truthfully for that platform.

It does not need to imitate Windows.

---

# 40. Test fixtures and safety

Use only controlled, disposable validation resources.

Examples:

- temporary directory trees;
- disposable filesystems/media;
- disk images;
- VMs;
- dedicated SMB shares;
- controlled SFTP/FTP servers;
- allowlisted Registry roots;
- dedicated accounts;
- recovery fixtures.

Never use ordinary personal or developer data as destructive-test fixtures.

Keep fixtures reproducible where practical.

Record fixture version/hash where it materially affects evidence.

---

# 41. Clean-environment validation

Validate public distributions outside developer machines.

For each supported distribution test:

- installation/extraction;
- first launch;
- first-run state;
- restart;
- uninstall/removal;
- reinstall;
- upgrade where applicable;
- state migration;
- corrupt state;
- native/runtime prerequisites;
- PATH assumptions;
- permissions;
- credential integration;
- portable mode where shipped.

---

# 42. Artifact matrix

Determine exactly what 1.0.0 intends to publish.

For every artifact record:

- artifact name/type;
- platform;
- architecture;
- package format;
- runtime model;
- helpers/workers/native libraries;
- signing/notarization requirements;
- external prerequisites;
- support classification;
- required live environment.

No public binary should exist outside the intended artifact matrix.

---

# 43. Signing and release-pipeline reconstruction

Inspect the actual signing and publication pipeline.

Determine:

- current signing provider/status;
- eligibility/approval;
- CI integration;
- tag requirements;
- preview/public-project prerequisites;
- credentials/permissions;
- artifacts/components requiring signing;
- approved fallback policy.

Do not hardcode an assumed sequence such as:

`GO → tag → build → sign`

if the actual signing provider requires tagged source before producing the artifacts that themselves require qualification.

Missing release prerequisites must appear early in the plan.

---

# 44. Immutable candidate identity

The final candidate must have an unambiguous provenance chain:

`source commit`\
→ `immutable candidate reference`\
→ `CI/build run`\
→ `artifact inventory`\
→ `artifact hashes`

The immutable candidate reference may be:

- release-candidate tag;
- final version tag;
- another immutable mechanism required by the actual pipeline.

Derive the appropriate mechanism from real tooling.

The invariant is:

> The exact artifacts that pass final qualification are the artifacts that may be published.

Never validate one build and publish another.

Never move a validated tag to different source.

---

# 45. Candidate tag and public release are separate concepts

Creating an immutable candidate/tag required for building or signing is not the same as publishing the stable GitHub Release.

If the real release pipeline requires a tag before qualification:

- create/use an immutable candidate reference as required;
- qualify the resulting exact artifacts;
- retain traceability if the candidate fails;
- do not move the immutable reference to unrelated source.

Public stable publication remains gated by human GO.

Design this sequence from actual repository/signing constraints rather than an assumed ceremony.

---

# 46. Preliminary validation vs. final qualification

Avoid repeating expensive final validation after every development fix.

Use two levels.

## Preliminary validation

Used during remediation to discover problems.

May run on development or intermediate candidate builds.

## Final qualification

Runs against the frozen exact candidate.

Must cover evidence dependent on:

- exact binary;
- package;
- final dependencies;
- signing/notarization;
- platform integration;
- final UI;
- final performance-sensitive implementation.

---

# 47. Remediation loop

Every release-relevant issue follows:

`discover`\
→ `classify`\
→ `reproduce`\
→ `understand`\
→ `choose remediation`\
→ `implement`\
→ `targeted regression`\
→ `affected regression`\
→ `re-audit`\
→ `close`

Capture:

- issue ID;
- evidence;
- reproduction;
- affected requirement/invariant;
- expected behavior;
- actual behavior;
- affected platform/capability;
- severity;
- release disposition;
- data/security impact;
- remediation alternatives;
- chosen direction;
- required regression;
- invalidated evidence.

Do not prescribe a code fix before understanding the failure.

---

# 48. Evidence invalidation

Every remediation or release-related change must include impact analysis.

Determine whether it invalidates:

- automated tests;
- fault tests;
- UX findings;
- accessibility findings;
- benchmark results;
- security review;
- dependency audit;
- platform testing;
- clean-machine testing;
- package testing;
- signatures;
- artifact hashes.

Do not rerun everything blindly.

Do not retain invalid evidence.

---

# 49. Release freeze discipline

Once final qualification begins:

- no unrelated refactoring;
- no opportunistic features;
- no cosmetic churn without release justification;
- no dependency upgrades without release need.

A release-blocking fix creates a new candidate where artifact identity changes.

Revalidate according to impact.

---

# 50. Severity and release disposition

Track two independent properties.

## Severity

- Critical
- High
- Medium
- Low

## Release disposition

- Blocker
- Requires explicit risk acceptance
- Non-blocker

Severity is based on impact, not implementation effort.

The following are non-waivable blockers while known:

- data corruption or silent data loss;
- destructive operation affecting unintended resources;
- false success for destructive operations;
- material exploitable security vulnerability;
- privilege escape;
- credential compromise;
- corrupted or incorrectly signed release artifact;
- broken core workflow on a platform claimed as stable-supported.

Do not hide such issues as Known Issues.

---

# 51. Known-issue acceptance

A known issue permitted in 1.0 must record:

- ID;
- severity;
- affected capability/platform;
- reproduction;
- user impact;
- workaround if available;
- reason release is acceptable;
- owner/follow-up;
- documentation/release-note requirement.

High issues require explicit product-owner risk acceptance.

Do not downgrade severity to make release possible.

---

# 52. Stop and escalation conditions

Identify failures requiring immediate escalation or quarantine of an affected release path.

Examples:

- evidence of data loss;
- privilege-boundary escape;
- source writes during read-only recovery;
- signing-chain compromise;
- credential disclosure;
- corrupted release artifacts.

Unrelated safe validation may continue where appropriate.

The affected release path may not proceed until the issue is understood.

---

# 53. Release Evidence Package

Final qualification must culminate in one consolidated Release Evidence Package.

Include or reference:

- source commit;
- immutable candidate reference;
- CI/build identity;
- complete artifact inventory;
- hashes;
- signatures/notarization;
- dependency inventory;
- SBOM;
- third-party notices;
- automated test results;
- fault/data-safety results;
- performance results;
- Windows live results;
- macOS live results;
- Ubuntu live results;
- Windows ARM64 results if applicable;
- UX results;
- accessibility results;
- security review;
- clean-environment results;
- known issues;
- accepted exceptions;
- final support matrix.

The human approver should not need to reconstruct release readiness from scattered logs.

---

# 54. Evidence-record quality

Manual/live validation records should contain enough information to reproduce the conclusion.

Record where relevant:

- candidate identity;
- tester;
- date;
- machine/hardware;
- OS/build;
- architecture;
- FileCat artifact hash/version;
- fixture/input;
- steps or charter;
- result;
- supporting logs/screenshots where useful;
- discovered issue IDs.

Avoid evidence that merely says:

> Tested, works.

---

# 55. 1.0.0 release gate

GO requires at minimum:

- complete no-orphan reconciliation;
- explicit current 1.0 capability manifest;
- no unresolved Critical issue;
- no non-waivable blocker;
- core workflows correct;
- Product Invariants reviewed against reality;
- Architecture Invariants reviewed against reality;
- applicable ADRs reconciled;
- every relevant `TV-*` closed or explicitly resolved;
- release-blocking automated validation passing;
- required fault/data-safety validation complete;
- performance acceptance complete;
- practical UX evaluation complete;
- keyboard/mouse evaluation complete;
- accessibility validation complete;
- mandatory live-platform testing complete;
- clean-environment testing complete;
- security review complete;
- dependency/license/SBOM audit complete;
- signing/notarization requirements satisfied;
- artifacts traceable to exact candidate source;
- public documentation matching reality;
- known issues explicitly accepted/disclosed;
- platform support claims truthful.

Green CI alone is insufficient.

---

# 56. Human GO / NO-GO

Passing technical gates does not automatically publish FileCat.

Prepare a concise release-readiness summary containing:

- candidate identity;
- exact artifact set;
- support matrix;
- automated results;
- live-platform results;
- data-safety status;
- security status;
- performance status;
- UX/accessibility status;
- known issues;
- risk acceptances;
- unresolved risks;
- publication prerequisites.

The product owner explicitly chooses:

# GO

or

# NO-GO

No public stable release occurs without explicit GO.

---

# 57. GitHub publication plan

After GO, publish exactly the validated candidate according to the actual release pipeline.

Cover:

- tag/candidate relationship;
- GitHub Release;
- stable/prerelease setting;
- release title;
- release notes;
- artifact upload;
- checksums;
- SBOM if published;
- third-party notices;
- signing/notarization information;
- platform/support statement;
- known issues;
- installation instructions;
- documentation;
- bug-reporting route;
- security-contact route.

Do not introduce a new unvalidated build between GO and publication.

---

# 58. Release notes and documentation truthfulness

Release notes must describe the actual product.

Include:

- what FileCat is;
- intended users;
- supported platforms/architectures;
- major capabilities;
- important safety semantics;
- significant limitations;
- known issues;
- installation;
- documentation;
- reporting/security information.

Audit public claims across:

- README;
- repository description;
- website if applicable;
- screenshots;
- Help/About;
- keyboard reference;
- installation docs;
- feature lists;
- platform-support docs;
- release notes.

Do not advertise:

- roadmap functionality;
- disabled features;
- experimental functionality as stable;
- platform parity that does not exist.

Documentation drift is a release defect.

---

# 59. Post-release verification

Immediately after publication test the **public distribution path**.

Verify:

- release page;
- tag;
- assets;
- public download;
- hashes;
- signatures/notarization;
- installation from downloaded artifact;
- first launch;
- documentation links;
- bug-reporting route;
- security-contact route.

Perform at least one clean smoke test from the publicly downloaded artifact.

---

# 60. Emergency post-release response

Define response for severe post-release defects such as:

- data loss;
- exploitable security issue;
- corrupted artifact;
- invalid signature;
- fundamentally unusable package.

Cover:

- warning/advisory;
- asset removal where appropriate;
- evidence preservation;
- impact assessment;
- patch release;
- credential/certificate response;
- user communication.

Do not silently replace a published binary under the same version as though it were unchanged.

---

# 61. Required matrices

The final Markdown plan must contain equivalent practical views.

## A. Current Release Capability Manifest

\| Capability | 1.0 intent | Platforms | Implementation | Existing evidence | Missing evidence | Release impact |

## B. Full Planning-Corpus Reconciliation

\| ID | Type | Current intent | Implementation status | Validation status | Release disposition |

This must provide no-orphan coverage of requirements, invariants, ADRs, TV validations, decisions, capability gaps, and other required categories.

## C. Requirement-to-Evidence Matrix

\| Requirement | Expected behavior | Implementation evidence | Existing validation | Required validation | Status |

## D. Workflow / Platform Matrix

\| Workflow | Windows x64 | macOS ARM64 | Ubuntu x64 | Windows ARM64 if applicable | Automated | Live |

## E. Artifact Matrix

\| Artifact | Platform | Architecture | Package | Signing/notarization | Qualified artifact/hash | Status |

## F. Issue / Remediation Matrix

\| Issue | Severity | Release disposition | Requirement/invariant | Evidence | Remediation | Revalidation |

Do not invent evidence to fill tables.

Unknown remains explicit.

---

# 62. Required final document organization

The final document may combine adjacent topics but must substantively cover:

1. Executive Summary
2. Objective and Planning Boundary
3. Repository Snapshot
4. Evidence Sources and Limitations
5. Product-Intent and Implementation-Truth Rules
6. Current Product Reconstruction
7. Current Release Capability Manifest
8. FileCat 1.0.0 Release Contract
9. Full No-Orphan Planning-Corpus Reconciliation
10. Requirement / Invariant / Decision Traceability
11. ADR Audit
12. TV Validation Audit
13. Implementation Completeness Audit
14. Existing Test/CI/Benchmark Evidence Audit
15. Validation Gap Analysis
16. Functional Validation Plan
17. Failure/Fault Validation Plan
18. Data-Safety Validation
19. Security and Trust-Boundary Validation
20. Dependency/License/SBOM Audit
21. Performance and Scalability Validation
22. UX/UI Audit
23. Human Usability Validation
24. Exploratory Testing
25. Keyboard/Mouse Validation
26. Accessibility and International Input
27. Platform Support Matrix
28. Windows x64 Physical Test Plan
29. macOS Apple Silicon Physical Test Plan
30. Ubuntu x64 VM Test Plan
31. Windows ARM64 Physical Test Plan if applicable
32. Test Fixtures and Safety
33. Clean-Environment Testing
34. Artifact Matrix
35. Signing and Release-Pipeline Prerequisites
36. Preliminary Validation and Remediation
37. Regression / Evidence Invalidation
38. Release-Contract Freeze
39. Immutable Candidate Identity
40. Final Candidate Qualification
41. Known Issues and Risk Acceptance
42. Release Evidence Package
43. 1.0.0 Release Gate
44. Human GO / NO-GO
45. GitHub Publication Procedure
46. Release Notes and Documentation Audit
47. Post-Release Verification
48. Emergency Response
49. Open Questions / Assumptions / Risks
50. Ordered Execution Checklist

Avoid duplicate chapters merely to satisfy this structure.

---

# 63. Ordered execution checklist

End the document with an executable ordered checklist.

The checklist must:

- respect dependencies;
- identify gates;
- show what may run in parallel;
- show what blocks subsequent stages;
- distinguish preliminary validation from final qualification;
- identify human-required activities;
- identify physical-machine-required activities;
- identify product-owner decisions;
- identify the release-contract freeze;
- identify the candidate-identity freeze;
- identify GO / NO-GO;
- identify publication;
- identify post-release verification.

It must be possible to follow this checklist without rereading the entire document to infer execution order.

Where repository-supported commands or workflows already exist, reference them.

Do not invent unsupported commands merely for completeness.

---

# 64. Validation depth must be risk-proportionate

Do not generate thousands of shallow test cases.

For safety/security/core workflows, provide reproducible detail.

For lower-risk behavior, grouped scenarios or exploratory charters may be more appropriate.

Prioritize:

1. data safety;
2. security/trust boundaries;
3. core workflow correctness;
4. Product Invariants;
5. Architecture Invariants;
6. platform-specific semantics;
7. common workflows;
8. historically fragile areas;
9. complex/high-risk capabilities;
10. cosmetic polish.

Every proposed validation should answer:

> What meaningful failure can this detect?

---

# 65. Core derivation loop

Do not treat example lists as the complete validation suite.

Use this reasoning:

> What does FileCat promise?

↓

> How is the promise implemented?

↓

> What could make the promise false?

↓

> What evidence already exists?

↓

> Is that evidence current and sufficiently independent?

↓

> What evidence is still missing?

↓

> What constitutes pass or failure?

↓

> What remediation alternatives exist?

↓

> What previous evidence becomes invalid after remediation?

↓

> What must the exact final candidate prove?

This reasoning is more important than mechanical checklist completion.

---

# 66. Avoid release theater

Do not confuse process artifacts with product quality.

For example:

- `Implemented` is not proof;
- a closed issue is not proof;
- a passing mock test is not OS evidence;
- high coverage is not correctness;
- a screenshot is not usability evidence;
- build success is not platform support;
- ARM64 CI is not physical ARM64 qualification;
- signing is not security review;
- a `Decided` ADR is not runtime validation;
- a completed checklist item without evidence proves little.

---

# 67. Avoid a second architecture project

This is release readiness, not greenfield redesign.

Do not recommend broad refactoring merely because:

- another abstraction looks cleaner;
- another framework is newer;
- a different pattern is fashionable;
- implementation differs from an old recommendation.

Require pre-release architectural changes only where evidence ties them to:

- incorrect behavior;
- data risk;
- security risk;
- unacceptable performance;
- platform failure;
- severe UX/accessibility problems;
- inability to obtain release confidence;
- serious stable-release maintainability risk.

---

# 68. Avoid perfectionism

FileCat 1.0.0 does not require:

- zero Low-severity issues;
- every historical feature idea;
- identical functionality on every OS;
- every possible protocol or format;
- cosmetic perfection;
- completion of explicit research-only items.

It does require:

- correctness;
- data safety;
- appropriate security;
- reliability;
- usable UX;
- truthful support claims;
- acceptable performance;
- trustworthy release artifacts;
- evidence appropriate to its claims.

---

# 69. Do not bias toward GO

Past implementation effort must not influence the quality threshold.

Do not weaken gates because FileCat is almost finished.

Conversely, do not invent new product requirements simply to postpone release.

Evaluate FileCat against its actual current contract.

---

# 70. Questions and uncertainty

Do not stop because uncertainty exists.

Represent uncertainty as:

- assumption;
- missing evidence;
- validation requirement;
- risk;
- decision gate;
- release dependency.

Ask the user only when the repository and available authoritative sources cannot answer a question and proceeding would otherwise require inventing a **material product or release-policy decision**.

Do not ask questions whose answers can be discovered.

---

# 71. Completion criteria for this planning task

The Markdown plan is complete only when:

- repository state is explicit;
- current FileCat product intent is reconstructed;
- the actual proposed 1.0.0 shipping surface is explicit;
- all required no-orphan categories are reconciled;
- every shipped capability has a validation path;
- every unresolved capability has an explicit disposition;
- every relevant `TV-*` has a closure path;
- evidence provenance and freshness are addressed;
- mandatory environments and prerequisites are identified;
- practical human UX validation is included;
- remediation and evidence invalidation are defined;
- release-contract freeze is defined;
- candidate identity cannot drift;
- exact validated artifacts are tied to publication;
- release blockers and waivers are explicit;
- human GO / NO-GO is mandatory;
- publication and post-release verification are defined;
- remaining uncertainty always maps to a named validation, decision gate, or release dependency.

Do not leave vague instructions such as:

> test thoroughly

> review security

> validate UX

> verify cross-platform support

without explaining what evidence closes them.

---

# 72. Final quality bar

The final plan must be:

- evidence-driven;
- repository-specific;
- current rather than historically literal;
- self-contained;
- context-agnostic;
- executable by another capable agent;
- explicit about claims versus proof;
- exhaustive in traceability without becoming checklist theater;
- risk-prioritized;
- practical;
- strict about data safety and security;
- proportionate about polish;
- explicit about human UX evidence;
- explicit about platform semantics;
- explicit about candidate and artifact identity;
- explicit about remediation and revalidation;
- explicit about release authority.

Prefer concrete, repository-supported instructions over abstract prose.

Prefer explicit decision criteria over generic recommendations.

Prefer traceable evidence over confidence language.

Prefer the smallest validation set that provides defensible confidence over redundant testing.

The plan succeeds only if another capable agent can follow it from the current FileCat repository to a defensible answer:

> **Is this exact FileCat candidate ready to be FileCat 1.0.0 stable?**

and, if the answer is yes:

> **How do we publish exactly the validated artifacts without introducing any unvalidated change?**
