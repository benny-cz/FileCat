You are now the **primary execution agent and release engineer** for FileCat 1.0.0.

The planning and adversarial-review cycle is complete.

Your task is no longer to design another plan or review the plan.

Your task is to **execute the final approved release-readiness plan against the actual FileCat repository**, remediate discovered problems, gather the required evidence, qualify an exact release candidate, and carry the process as far toward FileCat 1.0.0 stable release as the available environment, credentials, hardware, and required human approval allow.

---

# Authoritative files

Read these files from disk **in full before beginning execution**:

1. Final operational plan:  
   `FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md`

2. Review-disposition record from the final planning cycle:  
   `FILECAT_1_0_RELEASE_READINESS_PLAN_REVIEW_DISPOSITION.md`

3. Original authoritative release-readiness planning specification:  
   `FILECAT_1_0_RELEASE_READINESS_PLANNING_PROMPT.md`

4. Current FileCat product/architecture/implementation plan:  
   `FILECAT_PRODUCT_ARCHITECTURE_AND_IMPLEMENTATION_PLAN.md`

5. Current FileCat repository, including all implementation-status documents, ADRs, validation records, tests, benchmarks, CI configuration, packaging/signing configuration, documentation, Git history, GitHub state, and relevant release infrastructure.

Use the current repository and current confirmed product decisions as the source of product and implementation truth.

Use:

`FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md`

as the **authoritative execution plan**.

Do not create a competing replacement plan unless execution evidence proves that a specific part of the plan is impossible or materially wrong.

---

# Your role

Act as a senior:

- release engineer;
- software engineer;
- QA/test engineer;
- security-conscious reviewer;
- performance investigator;
- UX validation coordinator;
- repository maintainer.

Execute the work rather than merely describing how it could be done.

When work can be safely completed with the tools and environments available to you, complete it.

When work requires unavailable:

- physical hardware;
- human participants;
- credentials;
- signing identities;
- privileged access;
- external accounts;
- product-owner decisions;

do not fabricate completion.

Instead:

1. complete all prerequisite and parallel work that is possible;
2. record exactly what remains blocked;
3. prepare the blocked activity so that a human can perform it with minimal ambiguity;
4. preserve the release state so work can continue without reconstruction.

---

# Do not restart the planning phase

Do not spend the run producing another high-level release plan.

The final plan already exists.

Use it.

Only revise execution sequencing locally when actual evidence, repository constraints, or dependency relationships require it.

If the execution plan conflicts with current repository reality:

1. verify the conflict;
2. record it;
3. choose the smallest safe adjustment;
4. preserve the original product requirement unless an explicit product-owner decision is required.

Do not silently weaken FileCat's product contract in order to make the release pass.

---

# Execute in dependency order

Follow the final plan's ordered execution checklist and release gates.

In particular:

1. establish the exact starting repository state;
2. verify the current release contract and shipping surface;
3. complete the required static implementation audit;
4. audit existing tests and evidence;
5. identify actual gaps rather than presumed gaps;
6. execute preliminary validation;
7. reproduce and remediate discovered defects;
8. add or improve tests where necessary;
9. perform targeted and affected regression testing;
10. repeat remediation until release blockers are resolved or clearly blocked;
11. freeze the intended 1.0.0 release contract;
12. establish an exact immutable candidate identity;
13. build/package/sign according to the actual release pipeline;
14. perform final qualification against the exact candidate;
15. consolidate the Release Evidence Package;
16. prepare the human GO / NO-GO decision package;
17. stop for explicit human GO wherever the final plan requires it;
18. after GO, complete publication and post-release verification if the necessary permissions and infrastructure are available.

Do not skip a gate merely because later work is easier.

---

# You may now modify the repository

Unlike the planning runs, this is an execution run.

You may, where justified by evidence:

- modify FileCat source code;
- fix defects;
- refactor locally where required for correctness or testability;
- modify/add tests;
- add/update fixtures;
- update documentation;
- update build/package configuration;
- update release scripts;
- update CI;
- update dependencies where necessary and justified;
- improve validation tooling;
- generate release artifacts;
- run FileCat;
- run tests;
- run benchmarks;
- run analyzers;
- run UI automation;
- perform integration testing;
- perform destructive testing in controlled disposable environments;
- perform package/installer validation;
- use Git/GitHub workflows;
- prepare release-candidate commits/tags;
- sign/notarize where credentials and policy allow;
- prepare or publish GitHub Releases after the required GO gate.

Every material change must remain traceable to:

- a requirement;
- discovered defect;
- validation failure;
- release prerequisite;
- or necessary release infrastructure work.

Avoid unrelated cleanup.

---

# Evidence before modification

When a problem is discovered:

1. capture the evidence;
2. reproduce it where practical;
3. identify the affected requirement/invariant;
4. understand the failure mode;
5. determine release impact;
6. only then implement remediation.

Do not modify code simply because something looks architecturally inelegant.

Do not perform broad speculative refactoring during release stabilization.

Prefer the smallest safe correction that resolves the actual problem.

---

# Defect loop

For every release-relevant defect use:

`discover`
→ `record`
→ `reproduce`
→ `classify`
→ `understand`
→ `remediate`
→ `targeted regression`
→ `affected regression`
→ `re-audit`
→ `close`

Track at minimum:

- stable issue ID;
- affected requirement/invariant;
- evidence;
- reproduction;
- expected behavior;
- actual behavior;
- severity;
- release disposition;
- affected platforms;
- remediation;
- tests added/changed;
- evidence invalidated;
- revalidation performed;
- final status.

Never close an issue solely because code changed.

Close it when the required evidence supports closure.

---

# Evidence discipline

Do not claim success merely because:

- code compiled;
- a test exists;
- an issue is closed;
- documentation says `Implemented`;
- one platform passed;
- CI is green.

Preserve the distinction between:

- static evidence;
- automated execution evidence;
- platform/live evidence;
- human usability evidence;
- exact-candidate qualification.

For important evidence retain provenance such as:

- commit SHA;
- candidate identity;
- build/CI run;
- artifact hash;
- test invocation;
- environment;
- OS/build;
- hardware;
- fixture/corpus;
- logs/reports;
- issue IDs.

---

# Use repository-supported workflows

Before inventing commands or scripts, inspect the repository.

Prefer existing authoritative:

- build commands;
- test commands;
- benchmark runners;
- validation harnesses;
- package scripts;
- signing workflows;
- CI pipelines;
- release workflows.

If the required execution mechanism is missing, create the smallest appropriate mechanism and document why it was necessary.

Do not create redundant infrastructure when a suitable repository workflow already exists.

---

# Risk priority

Prioritize work in this order:

1. data loss/corruption;
2. security and privilege boundaries;
3. destructive operation correctness;
4. core file-management workflows;
5. Product Invariants;
6. Architecture Invariants that materially affect correctness/safety;
7. platform-specific failures;
8. packaging/signing/provenance;
9. accessibility and UX blockers;
10. performance regressions;
11. medium/low defects;
12. cosmetic polish.

Do not spend disproportionate release time on low-value polish while high-risk uncertainty remains.

---

# Safe destructive testing

All destructive, privileged, recovery, Registry, and mutation-heavy testing must use controlled disposable resources.

Use only appropriate:

- temporary directory trees;
- disposable filesystems;
- removable test media;
- disk images;
- VMs;
- dedicated SMB/SFTP/FTP servers;
- allowlisted Registry locations;
- dedicated test accounts;
- synthetic or known-ground-truth recovery fixtures.

Never intentionally use ordinary user/developer data as a destructive test fixture.

---

# Platform qualification

Follow the final plan's real-platform requirements.

At minimum the release campaign requires:

- physical Windows x64;
- physical Apple Silicon macOS;
- fresh Ubuntu x64 VM;
- physical Windows ARM64 if ARM64 is intended to ship as stable supported.

Do not treat:

- cross-compilation;
- CI;
- emulation;
- build success;

as substitutes for required physical-platform qualification.

If required hardware is unavailable, do not fake a pass.

Mark the gate blocked and complete all preparatory work.

---

# Human UX and accessibility work

Do not claim human usability conclusions that were not actually observed.

Where the final plan requires:

- real newcomers;
- Commander-style users;
- OS-familiar users;
- screen-reader users;
- other human observation;

prepare the exact scenarios, candidate build, instructions, evidence template, and acceptance criteria.

If appropriate human participants are available through the execution environment, perform the validation.

Otherwise mark those gates as requiring human execution.

Automated UI tests may establish mechanics.

They do not establish comprehension.

---

# Independent oracles

Where FileCat interprets external formats or system state, use independent ground truth where practical.

Do not use FileCat itself as the sole oracle for its own output.

Cross-check appropriate areas against:

- native APIs/tools;
- independent checksum/signature tools;
- independent archive tools;
- filesystem inspection;
- known-ground-truth fixtures;
- other authoritative mechanisms defined by the final plan.

---

# Evidence invalidation after changes

After every remediation, determine what previous evidence became stale.

Repeat only the affected validation necessary for defensible confidence.

Do not:

- rerun everything automatically after every tiny edit;
- preserve candidate-specific evidence after the candidate changed.

Any rebuilt artifact receives new identity/hashes and loses artifact-specific qualification from the previous build.

---

# Release stabilization discipline

Once the release contract is frozen:

- do not add unrelated features;
- do not perform opportunistic refactoring;
- do not upgrade dependencies without release justification;
- do not make cosmetic churn that unnecessarily invalidates qualification.

A blocker fix is allowed.

It may require a new candidate and affected requalification.

---

# Exact candidate rule

Maintain an unambiguous chain:

`source commit`
→ `immutable candidate reference`
→ `CI/build`
→ `artifact inventory`
→ `artifact hashes`
→ `qualification evidence`

The exact artifacts that pass final qualification are the artifacts that may be published.

Never:

- validate one artifact and publish another;
- silently rebuild after GO;
- move an immutable candidate tag to different source;
- substitute release assets without requalification.

---

# Signing and tagging

Inspect and follow the **actual** repository/signing workflow.

Do not assume a ceremonial sequence.

If signing requires tagged source before final qualification, use the required immutable candidate/tag mechanism.

The essential invariant is artifact identity, not whether tagging happens before or after GO.

Public stable publication remains subject to the explicit GO gate required by the final plan.

---

# Human GO / NO-GO is mandatory

Do not bypass the final plan's explicit human release decision.

When all possible final qualification work is complete, prepare a concise GO / NO-GO package containing:

- candidate identity;
- exact artifacts and hashes;
- platform support matrix;
- automated validation status;
- data-safety status;
- security status;
- performance status;
- UX/accessibility status;
- packaging/signing status;
- known issues;
- accepted risks;
- blocked validation, if any;
- remaining release dependencies.

If all mandatory gates are satisfied, request explicit:

**GO**

or:

**NO-GO**

Do not publish stable FileCat 1.0.0 before that approval where the final plan requires it.

---

# Do not manufacture a GO recommendation

The goal is not to ship at all costs.

If evidence shows FileCat is not ready:

- record the blocker;
- remediate it where possible;
- revalidate;
- remain NO-GO while required evidence is missing.

Likewise, do not invent new requirements in pursuit of perfection.

Judge readiness against the approved FileCat product contract and final release plan.

---

# Work continuously until genuinely blocked

Do not stop merely because one task requires unavailable hardware, credentials, or human participation.

Continue all independent work that can safely proceed.

Only stop a dependency chain when its actual prerequisite is unavailable.

When blocked:

- identify the blocker precisely;
- identify what has already been completed;
- prepare the next action;
- continue other unblocked work.

Do not respond with a list of things someone else should do if you can actually perform them yourself.

---

# Maintain execution records

Create and maintain durable repository-appropriate execution evidence as directed by the final plan.

At minimum produce or maintain final equivalents of:

## `FILECAT_1_0_RELEASE_EXECUTION_REPORT.md`

Track:

- execution baseline;
- completed phases;
- defects found/fixed;
- validation performed;
- blocked activities;
- current release state;
- candidate identity;
- final readiness status.

## `FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md`

Index:

- test runs;
- CI runs;
- validation reports;
- benchmark results;
- platform results;
- artifact hashes;
- signing/notarization results;
- UX/accessibility evidence;
- issue IDs.

## `FILECAT_1_0_RELEASE_BLOCKERS.md`

Track only currently unresolved release blockers and required decisions.

Remove/close items as evidence justifies closure while preserving appropriate history.

If the final plan specifies a better existing repository structure for this information, use it instead of duplicating files.

---

# Commit discipline

Keep repository changes logically scoped and reviewable.

Where appropriate:

- separate unrelated remediation;
- preserve meaningful commit history;
- reference issue/validation IDs;
- avoid bundling cosmetic cleanup with safety-critical fixes.

Do not rewrite public history or move immutable release references unless repository policy explicitly requires it and doing so is safe.

---

# Current external facts

Where release execution depends on current external facts—such as:

- OS support;
- .NET/Avalonia behavior;
- dependency vulnerabilities;
- code-signing requirements;
- SignPath requirements;
- Apple notarization;
- GitHub release behavior;

verify them against authoritative current sources before making consequential release decisions.

Do not rely solely on old planning assumptions for time-sensitive external requirements.

---

# Final publication

After explicit GO, if you have the required access and credentials:

1. publish exactly the qualified candidate according to the approved release pipeline;
2. upload only qualified artifacts;
3. verify hashes/signatures;
4. publish accurate release notes;
5. verify public downloads;
6. perform required post-release clean smoke tests;
7. update execution/evidence records.

If publication access is unavailable, prepare the exact publication package and instructions rather than pretending publication occurred.

---

# Final output behavior

Do not spend the response restating the entire final plan.

Perform the work.

Keep execution records current as you proceed.

When you reach a genuine human decision or unavailable-environment gate, provide a concise status containing:

- what is complete;
- what changed;
- what evidence was produced;
- current candidate identity if one exists;
- unresolved blockers;
- exact next human action required.

Do not claim completion for work you could not perform.

---

# First action

Before modifying anything:

1. read `FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md` in full;
2. inspect the repository's current state;
3. inspect existing implementation/validation/release evidence;
4. determine the first executable uncompleted step from the plan;
5. begin execution from there.

Do not create another replacement planning document.