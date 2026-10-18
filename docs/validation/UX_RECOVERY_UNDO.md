# Recovery intent and Undo lifecycle

This note covers the S1 source corrections for [FCUX-001 and FCUX-018](FileCat_UX_UI_Audit.md). It records the implemented contract and regression coverage. Native desktop and actual process-interruption acceptance remain separate gates.

## FCUX-001 — preserve the reviewed transfer

`RecoverableTransferIntent` records a versioned transfer snapshot in the job journal. It includes the exact local destination, ordered source roots and their relative folders, replacement name, resolved matching rules, flatten choice, verification, attribute/timestamp preservation, rate limit and queue mode. Saved masks are captured as resolved expressions, so subsequently editing or deleting a saved definition cannot broaden continuation. Verification comes from the record rather than the current preference.

Continuation requires a complete, supported snapshot and a matching source manifest. The parser rejects missing fields, unsupported versions, wrong scalar types, invalid paths/mappings, changed header/manifest order and excessive input. Bounds are 1,000 selected roots, 131,072 UTF-16 code units of path/mapping text and a 1 MiB encoded snapshot; matching expressions additionally have 16,384 code units of text, 256 terms and eight nested levels. The existing source/content review budgets still apply independently. Transfers with special comparison or edit-commit preconditions are deliberately not presented as equivalent continuation.

The recovery action is labeled **Continue copy/move** and previews the actual source-to-target mapping and options. Its conflict policy is explicitly **Skip**, even if the interrupted action originally used Replace. Skip does not verify an existing destination or authorize deleting a remaining move source. Existing folders may still receive missing descendants; alias and file/folder conflicts retain their existing safe decisions.

Legacy and incomplete records retain review/cleanup routes and offer **Review a new transfer…**. That flow explains which intent cannot be assumed, expands the choices, defaults to Skip, and requires a new review. Relative destination edits resolve against the captured destination, independent of later tab navigation. The old journal remains open until the reviewed request is submitted.

Existing source-change, content, partial-file ownership, staged-publication and in-use-journal guards remain active. Cleanup uses the reconstructed mapping and requires durable copy evidence before considering an incomplete destination. A filtered same-volume directory move now uses the guarded traversal path; renaming the whole directory would bypass its filter.

Move continuations, partial-file cleanup and newly selected replacement/rename-existing policies use consequential confirmation defaults. Cancel has initial focus and is the default action. Enter from the review text also cancels; approving the explicitly labeled action remains available.

### Acceptance coverage

| Requirement | Regression fixtures |
| --- | --- |
| Exact name, relative hierarchy/flatten, verification and all recorded options | `RecoverableTransferIntentTests`, `InterruptedTransferIntentUiTests` |
| Resolved saved-mask semantics and bounded parsing | `MaskTests`, `RecoverableTransferIntentTests` |
| Excluded descendants stay excluded during a filtered same-volume move | Real source/destination manifest tests in `RecoverableTransferIntentTests`; actual recovery UI flow in `InterruptedTransferIntentUiTests` |
| Legacy/new-transfer distinction and replaced manifests cannot trigger cleanup | `InterruptedTransferIntentUiTests` |
| Existing file, both copies after a cross-volume move, alias, case-only move, type mismatch and existing folder with missing descendants | `RecoveryTransferConflictTests` |
| Changed sources/partials, staged ownership, review limits and late approval | Existing `Interrupted*` regression fixtures retained |
| Safe initial action and Enter do not mutate or close the journal | Six recovery cases in `InterruptedTransferIntentUiTests`; ordinary/consequential generic cases in `DialogFocusOwnershipTests` |

## FCUX-018 — own and explain one Undo attempt

The original operation is atomically reserved **before** its review opens. Canceling releases that reservation with its recorded steps intact. The approval uses an immutable `UndoPlan`; Registry byte arrays are defensively copied both when captured and when exposed. A failed admission cannot erase the only recorded inverse.

An approved filesystem inverse becomes a typed Undo job. JobManager owns it before consuming the original steps. It uses the existing mutation scheduler, queue, progress, cancellation, active-work close gate and journal lifecycle. Clearing or removing the original finished row does not remove the active inverse. A queued canceled attempt produces an explicit unattempted report; it does not silently replay.

Registry Undo gains the same exclusive reservation and immutable approval boundary. Its execution and results continue through the existing guarded Registry job and issue-reporting path. The new complete `UndoReport` and **Show affected files** surface described below apply to filesystem Undo; extending that presentation to Registry is outside this finding's filesystem scope.

Dependent rename Undo supplies the bulk runner with its journal and cancellation checkpoint. All temporary-name intents are durable before staging starts. Cancellation during staging attempts safe rollback. Failed rollback or unexpected failure retains unresolved temporary names and an open journal for the existing recovery scanner. Publication retains the runner's safe cancellation boundary. A stop request does not promise transaction rollback.

The review names each inverse with full paths and its actual verb: move back, restore from Recycle Bin, or remove a created empty item/link. Hard-link wording distinguishes the created name from the surviving name. Known effects outside the recorded inverse are captured when the effect commits, including overwritten destination contents and the copy/delete part of mixed moves.

Every recorded filesystem inverse receives one structured result: restored, removed, kept by a safety guard, failed, or not attempted. The retained report leads with totals and presents failures, guarded keeps and unattempted effects before successes. It accounts for unexpected worker exit as well as ordinary failures. Operations provides **Undo report** and **Show affected files**; the existing report window supports selecting and copying the complete report, and closing/reopening it preserves the result. The affected-file inventory preserves distinct ordinal paths, including names differing only by case.

### Bounds and intentional limits

- Existing eligibility remains: completed jobs with recorded inverses; canceled/failed originals and truncated inverse records are not newly made undoable. The existing inverse limit is 100,000 actions.
- The irreversible remainder records a complete count and the first 1,000 path/reason entries. The UI explains any additional counted effects.
- Reports are retained with the inverse operation during this session under the existing finished-history policy (50 finished operations by default). Review explains that clearing its row, history retirement or closing FileCat can remove access and offers Copy all for preserving the result. Ordinary Undo history is not persisted as a replayable transaction.
- There is no automatic retry of successful, kept, failed or unattempted inverses.
- Directory MoveBack moves current contents; it does not claim a historical recursive snapshot. Created-empty-item, symbolic-link, hard-link identity/last-name, changed-file, occupied-original-name and platform recycle guards keep their individual semantics.
- Source/path checks are not a new native atomic handle protocol. Existing filesystem races and platform identity guarantees must be evaluated on the target platform.

### Acceptance coverage

`UndoLifecycleTests` exercises concurrent claims, immutable Registry capture, canceled review, failed handoff, a held active inverse, overlapping work, queued cancellation, original-history removal, complete late failures, unexpected worker exit, replacement/mixed-move disclosures, and durable dependent-rename interruption/rollback evidence. `UndoSafetyTests` adds twelve cases for real portable hard links with controlled identity responses, empty-item replacement/population, current directory contents, platform recycle refusal and exact link verbs. Existing bulk rename, link and Windows identity tests are retained.

`UndoLifecycleUiTests` exercises both row and keyboard duplication, actual review cancellation, queued inverse close policy, approval arriving after window/IO shutdown, a 70-action report with a late guarded keep, Copy all/reopen, distinct-case affected paths, bounded-retention wording and replacement-move review.

## Executed evidence

The companion final test summary is authoritative for the final packaged revision.

- Final full Core execution: **4,395 passed, 112 skipped, 1 pre-existing failure; 4,508 total**. The failure is the original device-file checksum exception-type expectation, also reproduced on the original sources. Within this run, the recovery/Undo prefixes account for **129 passed, 0 failed, 0 skipped**: 42 recoverable-intent cases, 10 recovery-conflict cases, 40 mask cases, 6 bulk-rename cases, 4 link cases, 1 attribute-link case, 14 Undo lifecycle cases and 12 Undo safety cases. Evidence: `implementation-core-bounded.trx`, packaged as `final/core.trx` in the companion evidence archive.
- Final affected App run: **231 passed, 8 skipped, 1 pre-existing failure; 240 total** across every `Interrupted*` fixture, `UndoLifecycleUiTests` and `DialogFocusOwnershipTests`. The focused classes pass completely: **13 recovery-intent cases, 7 Undo lifecycle cases and 7 dialog-focus cases; 27 passed, 0 failed, 0 skipped**. The eight skips require Windows or an ordinary POSIX user. Evidence: `recovery-undo-app-verified.trx`, packaged as `focused/recovery-undo-app.trx` in the companion evidence archive. The pre-existing failure is described below.
- The existing `InterruptedDemandTests.A_failed_journal_close_preserves_the_durable_source_manifest` failure was reproduced by executing that exact test on the unmodified attached-source checkout. Its POSIX permission assertion assumes an unprivileged process; this validation environment runs as root. Production permission/journal guards and the assertion were not weakened to make it pass.

Remaining native checks are Windows file identity/last-name and Recycle Bin behavior, actual cross-volume transfer behavior, controlled process termination/power interruption, ordinary-user permission refusal, and desktop focus, automation, clipboard and scaling. Controlled exceptions, portable filesystem fixtures and headless input establish the tested contracts without claiming those native checks were executed.
