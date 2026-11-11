# Archive writing UX/UI gap audit (§11–§12)

10 November 2026, branch `codex/archive-writing` after `f6d58ec6`. A read-only code audit of plan §11 and §12 against the
application, made to turn ledger item 8 into concrete work. It is a working list, not qualification evidence; each item
closes with its own change and test. Ordinary startup still registers no native archive backend or native edit store
(`src/FileCat.App/Services/AppServices.cs`), so every native item is reachable only through explicit composition today;
legacy ZIP is the only live writer. Paths below are relative to `src/`. "No test" means no App or Core test asserts it.

## Progress

Addressed after the audit (each with the test that pins it; the native lanes still need the platform runs recorded in the
implementation status before any of this counts as qualification evidence):

| Item | Change | Test |
|---|---|---|
| A1 | `ArchiveJobPhase` (Preparing, Reading archive, Rebuilding, Checking, Saving, Finishing safely) set by the native backend and shown on the operation row | `JobProgressViewTests.Archive_writes_show_their_phases_…` |
| A2 | Cancel is withdrawn during Saving and Finishing safely with the help text "Finishing this archive update safely."; a cancel that still arrives after commit is reported as such | same; `MainViewModel.Operations` cancel toast |
| A3 | One polite announcement per phase change; archive jobs announce their final result | same |
| A4 | A format scan's dialog keeps the operation center above its backdrop and usable: pointer, focus and Ctrl+J move between them while panel commands stay blocked; the scan's progress bar is named "Checking <archive>" | `ArchiveScanOperationsTests.The_operation_center_stays_usable_while_a_format_scan_is_pending` |
| A5 | One combined review when replacements, exclusions and 7z time precision coincide; a single condition keeps its own question | `UnixArchiveStorageTests.One_combined_review_lists_replacements_and_exclusions_together` |
| A6 | Summary counts requested changes and entries verified and saved | `UnixArchiveStorageTests.Native_jobs_reached_through_a_linked_folder_…` |
| A7 | The Create dialog shows a bounded, cancellable count of what will be packed and the space needed against what the drive has (or that it could not be checked), before submission; links are not followed | `ArchiveCreationEstimateTests` |
| A8 | Packing onto an existing ZIP asks "Add to this archive"; Cancel returns to the dialog and keeps the file | `ArchiveZipExistingTargetTests` |
| A9 | "Open archive" on a finished creation's row | `ArchiveOpenCreatedTests` |
| A10 | Native F8 focuses the nearest surviving row, never another copy of a removed name | `ArchiveMemberCommandUiTests.Wrong_password_can_retry_and_duplicate_removal_…` |
| A12 | Specific read-only reasons (nested, network share, unsupported format, not yet qualified, backend reason) | `ArchiveProviderWritingTests.Read_only_archives_explain_the_specific_reason_…` |
| A13 | F4 on a duplicate name explains before any review or password and offers "Extract this copy…" | `ArchiveMemberCommandUiTests.F4_on_a_duplicate_name_…` |
| A14 | Background "Create folder" works in writable native archives, like F7 | `ArchiveContextMenuTests` |
| A15 | Retained files after a commit: plain message, the first path as the issue location and "Reveal locally" in issue details | native backend issue |
| A16 | A canceled check after a committed edit says the commit happened and that its new state was not confirmed (only when the job outcome proves the commit); an archive that cannot be prepared for a commit (stopped check, missing, moved, unsupported or no longer writable) keeps the working copy and session and offers Save copy or "Show working copy", which selects it in the active panel for a fresh, manually chosen workflow | `MainViewModel.EditSessions`; `ArchiveEditNativeUiTests.A_moved_archive_keeps_the_edit_and_offers_save_copy_or_the_working_copy` |
| B1 | `ArchiveWriteMessages` maps refusals to §11.7 text, never claiming "not saved" once saving may have begun; technical text kept as issue details | `ArchiveWriteMessagesTests` |
| B2 | Space refusal states needed and available amounts and the next step | `ArchiveWriteMessagesTests.Insufficient_space_…` |
| B3, B4, B5, B6, B7, B9 | Duplicate-edit sentence, platform Trash wording, "Replace N existing members", "…or choose ZIP.", late-cancel toast, escaped session paths | `ArchiveCreationWorkflowTests` (suffix), `ArchiveMemberNativeUiTests` (replace label) |
| B8 | "Apply edit to the current archive", "Add to this archive", "Pack into archive…", generic F4 hint; the post-commit read-back is titled "Check saved archive" | — |

A11 needs no separate state: the write revision is computed while the listing is built, so a listing is either still
loading or complete with its revision; "Refresh this archive…" appears only when writing became available after the
listing was built, and a refresh is exactly the remedy.

C items addressed on 11 November (live runs recorded in the implementation status):

| Item | Change | Test |
|---|---|---|
| C menu bar | A disabled menu-bar command shows its reason while disabled and carries it as help text; the keyboard reference says "Unavailable: <reason>" for an unavailable command | `MenuUnavailableReasonTests`, `KeyboardReferenceTests` |
| C Create dialog | The 7z options and "What is included" expanders are named; NVDA and Orca hear compression, "7z options" (expanded is "pressed" for both), the four 7z rows, password protection, "Protect contents" and both password fields | journey stage 0 (`win-nvda-archive-writing.ps1`, `linux-orca-archive-writing.py`) |
| C combo boxes | Up or Down in a closed combo box was silent with NVDA (Avalonia's Windows bridge raises no value change): the box's node now raises UI Automation's value change, as Linux already sent an announcement | `DescendantFocusAutomationTests.A_keyboard_change_of_a_closed_combo_box_raises_its_value_change_on_Windows`; NVDA stage 0 hears "Protect contents" |
| C held Enter | "Replace compressed contents" (old contents not kept) opens on Cancel in both routes; the format-scan modal has no default button and starts on its named progress, so the Enter that asked for it cannot cancel it; Escape cancels | `ArchiveScanOperationsTests`; `ArchiveStandaloneUiTests` (held Enter cancels both replacement questions; actual-worker lanes) |
| C in-job review | The choice focused first speaks the counts and the first example (a screen reader reads the focused button, not the message); a whole-operation archive review offers no "do the same" checkbox; the collision message counts in the singular for one | `BackgroundDecisionTests`; journey stage 4 (Ctrl+J, "Review decision…", the question with Skip focused, Escape cancels, archive unchanged) |
| C Operations keyboard | A waiting decision's notice says "Choose Review decision in Operations (Ctrl+J)", but Ctrl+J only showed the drawer and Tab switched panels, so NVDA and Orca users could not reach "Review decision…"; and with a job row focused, panel keys (Enter, Delete, F8) acted on the panel's file. From the keyboard Ctrl+J now takes the keyboard into Operations (the waiting decision first); there Tab cycles, only global shortcuts act, Esc returns to the panel and Ctrl+J closes Operations | `BackgroundDecisionTests.Ctrl_J_takes_the_keyboard_to_a_waiting_decision_and_back` |
| C UI budget | `--ui-budget` in the qualification host notes every UI-thread gap over 50 ms with its time; drivers for Windows and Ubuntu attribute gaps to idle, navigation, a 20,000-file pack, opening the archive and adding to it. Ubuntu: no gap in any archive window; Windows: none while packing (160 s) or opening, two in the add window of 65 and 62 ms of which 59 and 51 ms were garbage-collection pauses; startup and the first opening of a dialog account for the rest | `win-ui-budget-archive.ps1`, `linux-ui-budget-archive.py` |
| C themes | `eng/Screenshots` (Debug) rendered the Create dialog as shipped (ZIP, the only writable route) in all eight themes: every label, field, button and the danger/accent fills legible, including High Contrast and DOS Commander. The captures showed the "Pack … into:" line, the filename and the estimate 50 px right of the path box whenever a long path widened the dialog (a stretched text block narrower than its row is centered); those lines, and the other archive dialogs' text, are now left-aligned | `ArchiveDialogViewportTests.The_create_dialog_text_starts_where_its_path_box_starts_in_a_wide_window` (fails at 51 px without the fix); contrast of every palette in `ThemePickerTests` |

Still open from C: macOS VoiceOver, moot while macOS writing cannot be qualified.

## A. Missing or partial behavior

| # | Plan | Current behavior | Test |
|---|---|---|---|
| A1 | §11.5 phases (Preparing, Reading archive, Rebuilding, Checking, Saving, Finishing safely) | Every archive job shows "Packing"; only generic Verifying/Finishing exist (`FileCat.App/ViewModels/OperationCenterViewModel.cs:219-244`). The native backend reports no phases (`FileCat.Core/Archives/ArchiveNativeJobBackend.cs:70,114`) | No test |
| A2 | §11.5 publication boundary: Cancel unavailable, "Finishing this archive update safely" | Cancel stays enabled (`Views/OperationsView.axaml:43,211`); a late cancel ends a committed job as Canceled (`FileCat.Core/Jobs/JobManager.cs:512,529`); the result survives only as a warning issue | No test |
| A3 | §11.5 announce phase changes and final result | Successful archive jobs send no notification (`ViewModels/MainViewModel.Operations.cs:706-711`); no live region for phases | No test |
| A4 | §11.5 operation center usable during a format scan | Pre-queue full reviews run in a modal overlay (`MainViewModel.ArchiveTargetReview.cs:27-33`); commands are ignored while it is open; its progress bar is unnamed | No test |
| A5 | §11.1/§11.3 one combined review of collisions, exclusions and precision | Up to three sequential questions (`ArchiveNativeJobBackend.cs:85-93`); no count of additions | Collision choice only |
| A6 | §11.3 summary with planned/skipped/verified/committed counts | Only created/updated plus excluded/skipped (`ArchiveNativeJobBackend.cs:114-119`) | No test |
| A7 | §11.2 steps 4/6: checked count, estimated extra space, exclusions before submission | Fixed free-space sentence (`Views/ArchiveCreationDialog.cs:94-98`); estimate used only in the job; exclusions asked after queueing | No test |
| A8 | §11.2 step 5: offer "Add to this archive" | With ZIP selected an existing small target goes straight into the legacy add path (`MainViewModel.Archives.cs:328-330`, `:141-159`) | Non-ZIP only |
| A9 | §11.2 step 7: explicit "Open archive" after completion | No such action (`Views/OperationsView.axaml`) | No test |
| A10 | §11.4 F8 must not select a different same-name occurrence | Native remove sets no focus target; refresh restores by name/kind (`FileCat.Core/Listing/ListingModel.cs:602,665`) | Ordinal only |
| A11 | §11.6 pending assessment state | None; F5/F7 enabled from profile availability (`FileCat.Archives/ArchiveProvider.cs:130-132`); F2/F8/F4 say "Refresh this archive…" | Stale refusal only |
| A12 | §11.6 specific refusal reasons; F4 "Save copy only" | One generic sentence (`ArchiveProvider.cs:137-139`); availability/UNC/nested reasons dropped; F4 just disabled | No test |
| A13 | §11.6 duplicate-row F4: explain and offer extract/edit/save copy | Refused only after a full review and password prompt, by toast, without an action (`MainViewModel.ArchiveEdits.cs:47-55`) | No test |
| A14 | §11.6 all surfaces; refused commands have accessible help | Background "Create folder" refused in writable native archives while F7 works (`Views/ContextMenuFactory.cs:21-22,43-46`); the item menu hides unavailable commands | No archive case |
| A15 | §11.7 retained backup: "Show retained file" | Count without path or action (`ArchiveNativeJobBackend.cs:124-126`); recovery has no restore action (known) | — |
| A16 | §12.2 commit states and a fresh manual workflow | Post-commit read-back opens another modal review; cancelling it is swallowed silently (`MainViewModel.EditSessions.cs:790`); "Archive edit kept" offers only Save copy | Happy path only |

## B. Wording and microcopy

1. No mapping from `ArchiveWriteReason` to §11.7 text: raw messages reach users, for example "…changed after the queued
   review." (no "not saved", no next step), BCJ2/coder internals, "The archive worker sent an invalid or over-budget message."
   (no "No replacement was published"), and implementation terms such as "fixed protocol-2 endpoint".
2. Space refusals name "stage, evidence, known snapshots and 64 MiB safety margin" without needed/available amounts or an action
   (`FileCat.Core/Archives/ArchivePreparationSpace.cs:54`).
3. Duplicate edit wording differs from the plan sentence (`FileCat.Core/Edit/ArchiveEditSource.cs:25`).
4. Native F8 hard-codes "Recycle Bin" on every platform (`MainViewModel.ArchiveMemberCommands.cs:183`); legacy ZIP delete/rename
   omit the "whole archive is rewritten" note below 64 MiB.
5. "Replace N existing members" appears as "Replace reviewed unique names" or "Replace" without a count; archive reviews carry
   an unrelated "Do the same for similar items…" checkbox (`Views/OperationDialogs.cs:626-627`).
6. Suffix conflict ends "…or choose another format." instead of "…or choose ZIP." (`Views/ArchiveCreationDialog.cs:207`).
7. A late cancel after commit reads "canceled. Archive updated.. Steps…" (double period; "canceled" for a committed update).
8. Labels: "Apply edit to current archive", "Add to existing archive", the read-back titled "Check archive for update",
   "Pack into ZIP…" and "F4 on a member of a ZIP archive…" become wrong once non-ZIP writers are registered.
9. The "Unsupported edit session" dialog shows working paths unescaped (`MainViewModel.EditSessions.cs:363,414`).

## C. Accessibility runs still required (NVDA, Orca, VoiceOver)

Create dialog (format, path, compression, 7z options, password rows, polite problem region); disabled-command reasons on the
menu bar (tooltip only); in-job review dialogs (initial focus, counts and samples read); preparation modals (unnamed progress,
Cancel as default, held Enter and Escape across chained modals, focus restoration, tab switching); operation-row phase and
outcome announcements; read-only/destructive states in high contrast; 100–200 % scaling, 640×480, long sample paths, themes and
the 50 ms UI budget during large scans.
