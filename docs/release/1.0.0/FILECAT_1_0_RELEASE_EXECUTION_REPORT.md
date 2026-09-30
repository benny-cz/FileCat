# FileCat 1.0.0 — release execution report

Operational plan: [FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md](../../design/FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md)
(with its review disposition). Companion records: [issue register](FILECAT_1_0_RELEASE_ISSUES.md),
[evidence index](FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md), [open blockers and decisions](FILECAT_1_0_RELEASE_BLOCKERS.md).
Candidate-specific evidence will live in `docs/release/1.0.0/<candidate-id>/` once a candidate exists.

## Current state (updated 2026-09-30)

- **Readiness: NO-GO.** Release readiness is not established. No release candidate, tag, signed artifact or qualified
  package exists. Phase: A–F (baseline, reconciliation and preliminary validation with remediation).
- **Candidate identity:** none.
- **Source:** `main` at `45efc09` (plan baseline `4f6b062` plus three remediation commits).
- **Defects found and fixed so far:** I19 (High, data loss), I15 (Critical where it happens, data loss), I20 (Medium,
  false forensic finding). All three are remediated and verified by targeted and affected regressions; closure awaits
  re-audit and final-candidate evidence.

## Execution baseline

| Property | Observed (2026-09-30) |
|---|---|
| Plan baseline | `4f6b062fa8548fc8fd417a50262a72c0b804461f`, equal to `origin/main` when execution began; no delta to review |
| Working tree | Clean apart from the seven untracked planning documents in `docs/design` (not committed by this campaign) |
| Submodules, tags, releases, issues, PRs | None |
| CI at the baseline | Run 36722039034 green; package jobs skipped (E-A01) |
| Private vulnerability reporting | Disabled (I01) |
| Execution host | Physical Windows 11 Pro Insider 26220, elevated shell (E-ENV-00) |

## Checklist progress (plan §14)

| Step | State | Notes |
|---|---|---|
| 1 Refresh baseline | **Done** | E-ENV-04; no delta from the plan's baseline at start |
| 2 Owners, resources, provider/licence preflight | **Open (people)** | DEC-01, DEC-07, EXT-01, EXT-02; resource status in the blockers file |
| 3 Collect CI/validation evidence and skip inventory | **Partial** | E-A01 (explicit skips on TRX lanes). Still to do: portable-lane and ARM64 skip lists from logs; the early-return audit (tests that return before asserting and report Passed) |
| 4 Reconcile manifest and registers against source | Not started | Plan §§3–5 registers stand as the starting point |
| 5 Contract questions (I05, I06, PSD, Mac, FDD, I14) | **Open (owner)** | DEC-02…DEC-06, EXT-02 |
| 6 V23 source review, test-guard audit, case catalog | **Partial** | DPI P03 and P15 audited (I19, I15); the other DPI and B rows remain |
| 7 Reporting, signing, dependency approach, preview preparation | Not started | I01/I02/I03/I14/I18 |
| 8 Fixtures and harnesses | Partial | VMware VMs lent and snapshotted (E-ENV-02); Windows Sandbox unusable (E-ENV-01) |
| 9 S10 suites with native setup | **Partial** | E-L01 (Windows lane locally, preliminary) |
| 10 High-risk preliminary cases and remediation | **In progress** | V03-PARTIAL (I19) and V19-UNINSTALL (I15) done preliminarily; I20 from CI |
| 11–13 V01/V12/V13/V16, human V17/V18, remediation loop | Not started / blocked | Human and reference-hardware work blocked (PPL-01…03, ENV-08) |
| 14 Pipeline, docs, release controls, preview | Not started | DEC-07, DEC-09 |
| 15–26 Freezes, candidate, FQ, REP, GO, publication | Not reachable | Depend on everything above |

## Work log

1. Read the launcher, the operational plan, its disposition record, the planning specification and the product plan in
   full. Refreshed the baseline; `4f6b062` unchanged, no delta review needed.
2. Downloaded the A01 TRX artifacts and inventoried outcomes and skip reasons (E-A01). Ran the Windows S10 lane locally
   on the physical host: 0 failures (E-L01).
3. Static audit of DPI P03 found I19 (the plan had flagged the heuristic; the audit found two further mechanisms: fill
   records keyed by destination only, and no revalidation). Reproduced with new tests, fixed (`f87ad32`), regressed.
4. The CI run of `f87ad32` failed on Windows ARM64 in an unrelated D-56 test. Instead of re-running, reproduced it
   locally (3 of 15), traced it to I20, fixed (`45efc09`), regressed; the formerly flaky test passed 15 of 15 and CI was
   green on all four lanes.
5. Static audit of DPI P15 confirmed I15 and removed the recursive `[UninstallDelete]` (`5b061cc`). Windows Sandbox was
   unusable (0x80070780, E-ENV-01); the owner lent snapshotted VMware VMs; on a Windows 11 VM the baseline installer's
   uninstall deleted a user's files and the fixed one kept them (E-I15-V1).
6. Recorded the CI installer-compiler provenance gap (I03/I18, E-ENV-03) and foreign-architecture WebView2 loaders in
   the x64 payload (E-I15-V1 side observation).
7. Started the owner's MacBookPro access (public-key SSH; key authorization pending) and a manual CI run
   (36759624490) to produce Linux and macOS packages for preliminary V19 checks on the lent Ubuntu VM and the Mac.

## Evidence invalidated by the campaign's own changes

- `f87ad32` (job engine, interrupted-copy review): E-A01 and E-L01 no longer describe current source for transfer
  paths; V03 interrupted-copy cases and small-file copy throughput must be re-run on the candidate.
- `5b061cc` (installer script): every earlier installer build; V19 lifecycle evidence must use the final setup.
- `45efc09` (D-56 `$LogFile` reader): V14 `$LogFile` evidence; the reader now waits up to about six seconds when the
  on-disk log lags, which affects the record window's time to open in that case.

## Next actions (unblocked)

1. Finish step 3: skip and early-return inventory for all lanes.
2. Preliminary V19 on the lent Ubuntu 22.04 VM and the Mac with the CI-built packages (tar, deb, AppImage in normal
   FUSE mode; app ZIP and Gatekeeper behavior), noting that neither environment is a final target.
3. Continue the V23/DPI source review in risk order: DPI P01/P02/P04/P08/P14/P16, then B04 (broker, I17), B10 (I16),
   B08 (recovery topology, I09).
4. I04: check the `.deb` dependency set against Ubuntu 26.04 (`libicu78`) in a container or VM.
5. Keep the three records current after each change.
