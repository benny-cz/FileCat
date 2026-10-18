# FileCat UX/UI Audit and Implementation Brief

**Audit date:** 17 October 2026  
**Document revision:** 2 — second source review, 17 October 2026  
**Audience:** Product owner, interaction/visual designer, implementation engineer, and QA engineer  
**Scope:** The FileCat implementation supplied in `FileCat(1).zip`; desktop interaction, information presentation, accessibility, and the user-facing consequences of implementation behavior  
**Status:** Independent audit and proposed implementation requirements. The application has not been modified by this review.

## Reading guide

This document is designed to support both prioritization and individual implementation tasks. Every finding is self-contained; the shared specifications collect constraints that would otherwise be repeated across many tasks.

| Your task | Start here | What to take into the task |
| --- | --- | --- |
| Choose the next work | [High-severity index](#index-s1), then [minimum corrections](#minimum-corrections) | The user consequence, a contained first fix, and its dependencies. |
| Implement a specific finding | Open its stable `FCUX` ID in the [full index](#findings-index) | Source symbols, current safeguards, proposed behavior, and applicable acceptance criteria. |
| Change a shared control or model | [Shared UX specifications](#shared-specifications) | Ownership, selection, feedback, layout, and resource constraints that all affected callers must retain. |
| Verify a fix | [Verification gates and fixtures](#verification) | Separate deterministic behavior from component layout and native UX evidence. |
| Review what changed in this edition | [Revision record](#revision-record) | New findings, strengthened evidence, and the one changed severity. |

**Closure rule:** Correct the consequential behavior and verify the applicable acceptance criteria. Architecture examples describe viable approaches; they are not a requirement to build every later refinement before a contained fix can ship. Record deferred refinements explicitly. Preserve the documented existing safeguards throughout.

<a id="executive-assessment"></a>

## 1. Executive assessment

FileCat has a capable commander-style foundation: multiple panels and tabs, explicit transfer targets, a central command registry, streaming file lists, background operations, recovery records, and extensive task-specific tools. The strongest improvement opportunity is to make the interface preserve and explain user intent consistently across these capabilities.

The priorities are: preserve the original scope and options when an operation is retried; bind clipboard actions and delayed work to the correct intent and destination; track Undo as a real mutation; make affected items unmistakable; keep reports and feedback accurate; expose decisive file/comparison information at narrow widths; and complete accessibility for the custom controls. Visual refinement should reinforce these behaviors through stable layout, readable state colors, fewer competing controls, and clearer hierarchy.

This report distinguishes **facts about the supplied implementation** from **UX judgments and proposed changes**. Severity describes the consequence for the user; it is not a claim that a failure was reproduced on a running desktop. Each finding identifies source evidence, a concrete scenario, implementation direction, and acceptance criteria. Proposed test scenarios are written for the implementer to execute against the current working revision.

### 1.1 Evidence and limits

| Evidence | What it establishes |
| --- | --- |
| `FileCat(1).zip`, SHA-256 `7a0cde5c72f7d39aeef3c00edde80a31b14a8ca1062cc5966f32078ea9536c63` | The exact archive reviewed. The archive has 2,055 files, including 359 C# source files under `src`, 127 of them in `FileCat.App`, and 184 C# files in `tests/FileCat.App.Tests`. These are inventory counts, not claims that every file received the same depth of review. |
| `README.md`, `global.json`, `Directory.Packages.props`, application and test project files | Product intent and stack: a keyboard-first desktop file manager using C#, the pinned .NET SDK `10.0.101`, and Avalonia `12.1.1`. Windows has additional native integration; portable surfaces also target Linux and macOS. |
| Application XAML, custom controls, view models, relevant core services, and existing tests | Concrete wiring, data flow, rendering choices, command semantics, state transitions, and existing regression protections. Source inspection can establish deterministic branches even when their full desktop consequence still needs runtime validation. |
| Eight JPEGs in `docs/screenshots/windows/` and `docs/screenshots/windows/CAPTURE.md` | Visual evidence from Windows captures dated 9 October 2026, showing four panels and three tabs per panel. Capture provenance names compiled source `a2f6cd9109a6b3dfddad7e92e74c5875147c78f9`; animations were disabled. This is a historical visual sample, not proof that every pixel represents the supplied source snapshot. |
| Primary Microsoft, Avalonia, and W3C guidance listed in section 10 | Reference principles for command presentation, dialogs, automation semantics, contrast, keyboard equivalence, and announcements. Recommendations for this desktop application are not a formal WCAG conformance certification. |

**Execution boundary:** No .NET SDK or native desktop execution environment was available for this review. The application was not built or launched, and its automated tests were not run. Existing tests were inspected as specifications and evidence of covered behavior; their presence does not establish that they pass. Live keyboard behavior, assistive-technology output, operating-system integration, animation, and actual layout at the proposed sizes remain explicit validation work.

All source paths below are **relative to the root of the supplied repository**. Line references refer to this archive; symbols are included so findings remain usable after lines move. The document requires no earlier conversation, unpublished design decision, or external task context. The source locations and screenshots named in a finding are sufficient starting points for implementation.

### 1.2 Severity, evidence, and effort conventions

| Severity | Meaning | Recommended handling |
| --- | --- | --- |
| **S0 — Critical** | Demonstrated, immediate catastrophic loss or inability to use the application at all, with no practical containment. | Stop the affected release or workflow. **No finding in this audit is assigned S0.** |
| **S1 — High** | A credible route to operating on the wrong items, misleading a consequential decision, losing important intent, or preventing a major user group from completing a core workflow. | Resolve before treating the affected workflow as reliable. Prioritize over cosmetic work. |
| **S2 — Medium** | Substantial ambiguity, hidden information, inconsistency, avoidable work, or impaired discoverability in an otherwise usable workflow. | Address in the next coherent UX improvement batch; elevate if runtime validation shows a greater consequence. |
| **S3 — Low** | Localized friction, presentation debt, or a recoverable preference inconsistency. | Bundle with related work after higher-severity behavior is stable. |

**Evidence labels:** `SC` means the relevant implementation is source-confirmed; `VC` means the visual characteristic is also visible in a supplied capture; `UX` identifies a design assessment grounded in the implementation. `RV` marks a consequence or rendering detail that specifically requires live verification. These labels can coexist: source-confirmed layout constraints do not establish the exact pixel extent of runtime clipping.

**Effort labels:** `S` means a contained change in an existing component; `M` means coordinated changes across several components and their tests; `L` means a shared model/control contract or migration with several consumers. These are relative scopes, not delivery-time estimates. The implementation team should size against its current revision and test infrastructure.

**Ranking rule:** Findings are physically ordered by severity, then by consequence and reach. IDs are permanent references, not scores or sequence numbers for the current ranking. New IDs and a promoted existing finding can therefore appear between older IDs. A single underlying problem can have several examples; related examples are grouped to avoid inflating the issue count.

**Requirement language:** A minimum correction and an acceptance criterion define the outcome to verify. A “recommended” interaction is the proposed starting design and may be replaced by an equally usable implementation that meets that outcome. A “full target,” optional alternative, or later refinement is additional scope. Effort labels describe the finding's complete proposed scope; the minimum-correction table identifies useful smaller slices. Live verification must resolve an `RV` question before it is reported as an observed failure or a successful native fix.

### 1.3 Terminology

| Term | Meaning in this report |
| --- | --- |
| Active/source panel | The panel whose current context supplies a command's input. |
| Target panel | The designated destination panel for transfer commands. With two panels it is the other panel; with three or more, explicit pairing applies. |
| Focused item | The row under the keyboard cursor. This is distinct from the marked selection. |
| Marked items | The set of items selected for batch commands. Some can remain marked while a view filter hides them. |
| Result set / working set | References to original items, potentially from multiple folders or providers. Removing a reference and deleting an original are different operations. |
| Provider | An implementation of a navigable resource, such as a local folder, remote server, archive, Registry view, recovery source, or result set. |
| DIP | Device-independent layout unit. Proposed DIP dimensions are not a promise about physical pixels at every OS scale. |
| Complete result | A result whose requested scope was fully evaluated. A stopped, bounded, unreadable, or partially processed result must remain distinguishable from a complete negative result. |

<a id="revision-record"></a>

### 1.4 Revision record and change boundaries

Revision 2 rechecks the same archive and historical screenshots. It adds eight distinct findings and strengthens existing ones; it does not represent a new application version or newly executed desktop tests. All 68 original IDs remain stable. The revised total is **76 findings: 13 high, 51 medium, and 12 low**.

| Change | Findings | Why the document changed |
| --- | --- | --- |
| New high-severity clipboard findings | [FCUX-069](#fcux-069), [FCUX-070](#fcux-070) | A remembered Cut can be applied to a newer clipboard publication; an awaited Paste can use the original tab's later location. Both concern the submitted operation, not visual polish. |
| Expanded finding and severity raised from S2 to S1 | [FCUX-018](#fcux-018) | Ordinary file Undo has no owned active lifecycle or per-job admission claim, and dependent rename Undo omits the runner's journal. The review also needs to describe actual inverse actions and incomplete reversibility. Existing mutation guards limit the consequence; interruption damage was not reproduced. |
| Expanded high-severity evidence | [FCUX-006](#fcux-006), [FCUX-007](#fcux-007) | Settings can discard additional tool fields and leading-comment-marker commands. Backward search has a UTF-16 progress defect, and the hex editor shares its hidden range limitation. |
| New medium-severity findings | [FCUX-071](#fcux-071), [FCUX-072](#fcux-072), [FCUX-073](#fcux-073), [FCUX-075](#fcux-075), [FCUX-076](#fcux-076) | Mixed-selection export omissions, binary comparison keyboard access, viewer semantic events, unsupported drop feedback, and Copy representation/completeness. |
| New low-severity finding | [FCUX-074](#fcux-074) | Command search advertises a Help gesture that its focused-input routing does not dispatch. |
| Expanded workflow/geometry requirements, severity unchanged | [FCUX-012](#fcux-012), [FCUX-021](#fcux-021), related operation/accessibility findings | Source-relative selection through reflow, whole-synchronization Stop scope, exact recovery policy, reason-aware results, asynchronous focus ownership, and consistent fractional scrolling. |
| Clearer implementation handoff | Sections [6](#implementation-sequence), [7](#shared-specifications), and [8](#verification) | Separate minimum corrections from larger targets; define consent revisions, coherent clipboard snapshots, bounded semantic updates, and measurable completion gates. |

The synchronization execution issue remains **S2**: Cancel is attached to a specifically titled child job, other approved jobs remain visible/cancelable, and target guards remain. The missing whole-task Stop and partial-failure policy should be fixed without claiming that an existing child Cancel is a broken global cancellation or that later approved removals are unauthorized.

<a id="findings-index"></a>

## 2. Findings index

**76 findings: 13 high, 51 medium, and 12 low. No critical finding is claimed.**

The index links to the full implementation brief for each finding. Effort describes relative scope, not calendar duration. Related findings can be delivered together; section 6 groups shared dependencies.

<a id="index-s1"></a>

### 2.1 High severity — S1

| ID | Finding | Area | Effort |
| --- | --- | --- | --- |
| [FCUX-001](#fcux-001) | “Run again” does not preserve the interrupted operation's naming, layout, or filter | Recovery | L |
| [FCUX-069](#fcux-069) | Cut intent can survive clipboard replacement and turn a later Copy into Move | Clipboard intent | M |
| [FCUX-070](#fcux-070) | A delayed clipboard read can redirect Paste to a newer location | Paste destination | M |
| [FCUX-002](#fcux-002) | A numeric range anchor can survive a change of tab or visible ordering | Selection | M |
| [FCUX-003](#fcux-003) | Bulk rename includes hidden marks without explaining or offering control of that scope | Bulk rename | M |
| [FCUX-004](#fcux-004) | Background decisions can take focus directly onto destructive defaults | Decision dialogs | M |
| [FCUX-005](#fcux-005) | Synchronization confirmation hides the full source and target identities | Synchronization | S |
| [FCUX-018](#fcux-018) | File Undo lacks a tracked lifecycle and a complete account of what it can reverse | Undo lifecycle and review | L |
| [FCUX-006](#fcux-006) | Saving Settings can change or discard existing tool configuration | Settings correctness | M |
| [FCUX-007](#fcux-007) | Previous text search can miss earlier content or fail to finish | Viewer search | M |
| [FCUX-008](#fcux-008) | Viewer checksums can use the wrong selection scope and hide complete results | Checksum results | M |
| [FCUX-009](#fcux-009) | Text comparison hides the very characters the user is trying to compare | Text comparison | L |
| [FCUX-010](#fcux-010) | The custom file list lacks virtual item and complete selection semantics | Accessibility | L |

<a id="index-s2"></a>

### 2.2 Medium severity — S2

| ID | Finding | Area | Effort |
| --- | --- | --- | --- |
| [FCUX-011](#fcux-011) | Modal layout cannot keep essential actions reachable at supported small sizes or with long filenames | Shared dialogs | M |
| [FCUX-012](#fcux-012) | Viewer geometry can hide content and change selected text after reflow | Viewer geometry and selection | L |
| [FCUX-076](#fcux-076) | Viewer Copy can change text or copy only part of the selected bytes | Content copying | M |
| [FCUX-072](#fcux-072) | Binary comparison has no keyboard route to select an arbitrary right-side byte range | Binary comparison access | M |
| [FCUX-073](#fcux-073) | Viewer accessibility events can describe stale content and omit editing state | Viewer semantics | M |
| [FCUX-013](#fcux-013) | Theme contrast checks miss destructive buttons and custom-drawn selected content | Theme state contrast | M |
| [FCUX-014](#fcux-014) | Separate extension cells bypass safe filename display | Filename identity | S |
| [FCUX-071](#fcux-071) | Clipboard and drag export silently omit non-filesystem items from mixed selections | Selection export | M |
| [FCUX-075](#fcux-075) | Unsupported drop targets advertise Copy or Move and then silently reject the drop | Drop feedback | S |
| [FCUX-015](#fcux-015) | The selected operation's error report stops updating until the user changes selection | Live operation reports | S |
| [FCUX-016](#fcux-016) | Progress text calls a requested pause “Paused” and canceled/failed work “100%” | Operation status | S |
| [FCUX-017](#fcux-017) | Ordinary operation reports do not expose complete paths or a complete remediation record | Operation evidence | M |
| [FCUX-019](#fcux-019) | Clearing operation history also removes undo availability without explaining that consequence | Undo retention | M |
| [FCUX-020](#fcux-020) | “Dismiss” permanently ends recovery tracking instead of merely hiding the interruption | Recovery dismissal | S |
| [FCUX-021](#fcux-021) | Sync option changes reset choices, and submitted steps have no action to stop the whole plan | Synchronization review | M |
| [FCUX-022](#fcux-022) | An older asynchronous navigation can override the user's newer destination | Navigation intent | M |
| [FCUX-023](#fcux-023) | Free-space information can remain stale and can describe the wrong mounted filesystem | Capacity feedback | M |
| [FCUX-024](#fcux-024) | Saving a workspace defaults to replacing an existing workspace without review | Workspace saving | M |
| [FCUX-025](#fcux-025) | Opening a workspace promises recovery of previous tabs but cannot restore the previous arrangement | Workspace restoration | M |
| [FCUX-026](#fcux-026) | Listing columns can extend beyond the reachable viewport | Listing geometry | L |
| [FCUX-027](#fcux-027) | An explicit sort can leave the retained focused item offscreen | Focus continuity | M |
| [FCUX-028](#fcux-028) | Invalid filters lose the editable draft and share unrelated error state | Filter validation | M |
| [FCUX-029](#fcux-029) | Notifications can inherit the previous severity and lack a persistent review route | Notifications | M |
| [FCUX-030](#fcux-030) | Panel layout uses an estimated workspace budget and lets headers crowd the location | Panel layout | M |
| [FCUX-031](#fcux-031) | Panel proportions have no equivalent keyboard adjustment | Keyboard resizing | M |
| [FCUX-032](#fcux-032) | Breadcrumb actions lack a complete keyboard and accessibility route | Breadcrumb accessibility | M |
| [FCUX-033](#fcux-033) | Oversized breadcrumb roots and leaves can be clipped beyond inspection | Path presentation | M |
| [FCUX-034](#fcux-034) | Settings closes and reopens after validation errors instead of preserving field context | Settings validation | M |
| [FCUX-035](#fcux-035) | Read-only Settings mode says the wrong thing and still accepts changes that will not persist | Settings persistence | M |
| [FCUX-036](#fcux-036) | Advanced Settings fields force users to author multiple mini-languages without editing support | Settings editing | L |
| [FCUX-037](#fcux-037) | Toolbar commands look available when the same commands are disabled in menus | Command availability | M |
| [FCUX-038](#fcux-038) | Bulk rename can be blocked by an error in a row the UI cannot display | Bulk rename review | M |
| [FCUX-039](#fcux-039) | Bulk rename does filesystem validation on the UI thread after every edit | Bulk rename responsiveness | M |
| [FCUX-040](#fcux-040) | Invalid bulk-rename counter input silently becomes a valid default | Bulk rename validation | S |
| [FCUX-041](#fcux-041) | Invalid transfer destinations close the form and discard the user's operation options | Transfer validation | M |
| [FCUX-042](#fcux-042) | The operations drawer has too little adaptive space for recovery rows, reports and controls | Operations layout | M |
| [FCUX-043](#fcux-043) | Recovery preparation offers no visible busy state or user cancellation while reviewing | Recovery preparation | M |
| [FCUX-044](#fcux-044) | Find falls back to a local home folder when the current provider is unsupported | Find scope | S |
| [FCUX-045](#fcux-045) | Find hides completion qualifications and uses generic empty-result feedback | Find completion | M |
| [FCUX-046](#fcux-046) | Find context shortcuts bypass the configurable command mapping | Context shortcuts | M |
| [FCUX-047](#fcux-047) | Custom scrolling rounds away fractional input and can stall one-row paging | Scroll input | S |
| [FCUX-048](#fcux-048) | Listing whitespace has no background context menu | Background context menu | M |
| [FCUX-049](#fcux-049) | Open text and hex viewers retain the old palette when the rest of the application changes theme | Theme lifecycle | M |
| [FCUX-050](#fcux-050) | Psychedelic effects cross functional content boundaries and can weaken menu readability | Decorative effects | M |
| [FCUX-051](#fcux-051) | Reduced-motion promise is implemented only on Windows | Motion preferences | M |
| [FCUX-052](#fcux-052) | Snapshot viewer modes detect change but make the user close and reopen the file to refresh | Viewer refresh | M |
| [FCUX-053](#fcux-053) | Quick view has a blank loading interval and silent picture-to-hex fallback | Quick-view feedback | M |
| [FCUX-054](#fcux-054) | Viewer Go to dialog has no visible action buttons and dismisses invalid input before explaining it | Viewer navigation | S |
| [FCUX-055](#fcux-055) | Picture “Actual size” remains checked for every manual zoom level | Picture zoom state | S |
| [FCUX-056](#fcux-056) | Text comparison cannot select/copy source text or search for a known term | Comparison interaction | L |
| [FCUX-057](#fcux-057) | Privacy setting promises no network requests but only controls automatic update checks | Privacy wording | S |

<a id="index-s3"></a>

### 2.3 Low severity — S3

| ID | Finding | Area | Effort |
| --- | --- | --- | --- |
| [FCUX-058](#fcux-058) | Dense command chrome gives too many actions equal visual weight | Main command hierarchy | M |
| [FCUX-074](#fcux-074) | Command search advertises F1 help while its key routing prevents Help dispatch | Command search help | S |
| [FCUX-059](#fcux-059) | Readability controls are fragmented across the file list, viewers and comparison | Readability preferences | M |
| [FCUX-060](#fcux-060) | Viewer/editor toolbars give advanced actions the same visual priority as frequent work | Viewer command hierarchy | M |
| [FCUX-061](#fcux-061) | Truncated listing cells have no direct full-value inspection | Cell inspection | M |
| [FCUX-062](#fcux-062) | Colliding tab labels and lock presentation obscure tab identity | Tab identity | M |
| [FCUX-063](#fcux-063) | Dependent options and list-editor actions remain enabled when they cannot do anything | Dependent controls | S |
| [FCUX-064](#fcux-064) | Window restoration ignores saved position and does not preserve a distinct normal-size geometry | Window placement | M |
| [FCUX-065](#fcux-065) | A valid one-panel workspace is saved as one panel but restored as two | Workspace consistency | S |
| [FCUX-066](#fcux-066) | A rejected last-tab close can still alter recently closed history | Tab history | S |
| [FCUX-067](#fcux-067) | About’s support actions dismiss the dialog and “Show data folder” has an ambiguous target | Support actions | S |
| [FCUX-068](#fcux-068) | “Move up” appears on finished jobs even though it cannot do anything | Queue actions | S |


<a id="findings-s1"></a>

## 3. High-severity findings (S1)

<a id="fcux-001"></a>

### FCUX-001 — “Run again” does not preserve the interrupted operation's naming, layout, or filter

**Severity:** S1 — High · **Area:** Recovery · **Effort:** L

**Confidence:** Source-confirmed request reconstruction defect; native end-to-end reproduction still required. **Affected workflows:** interrupted copy/move, especially filtered folder transfers, renamed single-file copies, and result-set transfers retaining relative folders.

**Evidence**

- `src/FileCat.App/ViewModels/MainViewModel.Interrupted.cs:75–120`, `RunInterruptedOwnedAsync`: reconstructs sources with `ItemRef.ForFileSystemPath`; confirmation says existing arrivals are skipped and “only the rest” is moved/copied; submits only kind, sources, destination, source reviews, `Conflicts = Skip`, and the *current* default verification setting.
- `src/FileCat.Core/Jobs/JobJournal.cs:54–65`, `JobJournal.Create`: persists `newName`, but does not persist filter/flatten or per-source relative-folder mappings.
- `src/FileCat.Core/Jobs/JobJournal.cs:294–308, 415–425`, `InterruptedJob`/`JournalRecovery.Scan`: recovery model and parser do not load even the already-persisted `newName`.
- `src/FileCat.Core/Jobs/Executors.cs:400, 441–459, 759–780`: normal transfers do use the filter, `RelativeFolder`, `Flatten`, and `NewName`; these are meaningful parts of the original action.
- `src/FileCat.App/ViewModels/OperationCenterViewModel.cs:261–262`: `CanRunAgain` requires known paths/destination, but does not require reconstructable original semantics.

**Current behavior and user impact**

Recovery is careful about which bytes may be deleted or changed, but reconstructs a different operation. “Run again” communicates continuity with the previously approved action. A filename mapping can be lost, folder placement can change, and omitted filter criteria can broaden the set of files affected. For a move, files originally excluded by a mask may now be moved. Conservative conflict skipping protects preexisting target files; it does not preserve the requested scope.

**Proposed reproduction**

1. Prepare a small source folder containing `photo.jpg` and `notes.txt` and an empty target. Keep total content below the recovery review limits.
2. Start F6 on the folder with “Only files matching” set to `*.jpg`.
3. Simulate an interruption using the journal fixture approach already present in `InterruptedOperationUiTests` (rather than forcibly crashing a real user process).
4. Invoke Run again. Inspect the reconstructed request and resulting tree: `Filter` is null.
5. Independently test copying `draft.txt` as `approved.txt`, then recovery. `NewName` is absent from the new request.
6. Independently test a result set with entries from two relative subfolders. Reconstructed source references no longer carry those relative folders.

**Desired UX**

An action labeled “Continue copy/move” must retain source-to-target mapping and scope. If a legacy journal cannot reconstruct those properties, explain exactly what is missing and open a prefilled transfer review requiring a new decision. Do not describe a broadened default request as “the rest.”

**Implementation guidance**

1. Define a versioned recoverable transfer intent containing kind, exact destination, per-root destination mapping, `NewName`, relative folders, filter text/semantics, flatten mode, and verification intent. Keep resource bounds and parse validation.
2. Reconstruct and preview that intent through the existing dialog/view-model path. Treat applying `Skip` during recovery as an explicit documented recovery policy; do not quietly replace unrelated options with today's settings.
3. Compute `CanRunAgain` from sufficient recoverable intent, not merely known source paths. For old/incomplete records, keep cleanup available and offer “Review a new transfer…” with a clear explanation.
4. Preserve all current source/content/identity/ownership checks and all in-use-journal protections. New UI must not bypass them.

**Acceptance criteria**

- A filtered interrupted move never moves an item excluded by the original filter.
- A renamed copy resumes to the same intended name; result-set copies preserve their approved hierarchy/flatten choice.
- Changing default verification settings after a crash does not silently change the recovered intent.
- Old journals either reconstruct safely or produce a reviewable new transfer; no misleading one-click continuation.
- Tests cover each altered parameter and retain all existing changed-file/partial-file recovery tests.

#### Recovery policy and conflict edge cases

Restoring original intent must not silently restore an original Replace policy in place of recovery's conservative Skip choice. `src/FileCat.App/ViewModels/MainViewModel.Interrupted.cs:107–120` deliberately submits `ConflictPolicy.Skip`; keep that documented recovery policy unless the user reviews a deliberate change. A legacy record with missing filter, name, hierarchy or verification information must not obtain those values from current settings or a newly edited saved filter and then claim equivalent continuation.

Explain the limits of automatic Skip during recovery. `src/FileCat.Core/Jobs/Executors.cs:1375–1407`, `ResolveConflict`, automatically applies Skip only in its normal non-same-item, non-type-mismatch branch. Same-item aliases and file/folder conflicts can still require the existing safe decisions. Existing directories can merge and expose missing descendants; their mere presence does not mean the whole root arrived. `src/FileCat.Core/Jobs/Executors.cs:475–485` separately makes same-item moves a no-op. Skipping an existing file does not certify matching content or authorize deleting a move's remaining source.

**Additional acceptance:** cover an ordinary preexisting file, a same-item hard-link alias, a case-only move, an existing destination directory with missing descendants, and an interrupted copy-then-delete move where both versions remain. The mapping and source disposition must retain original intent; special conflict decisions remain available, and skipped destinations are reported as retained rather than verified arrivals.

[Back to S1 index](#index-s1) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-069"></a>

### FCUX-069 — Cut intent can survive clipboard replacement and turn a later Copy into Move

**Severity:** S1 — High · **Area:** Clipboard intent · **Effort:** M

**Confidence:** Source-confirmed operation-kind decision defect. Native cross-application behavior has not been executed; the failure can first be reproduced with a controlled clipboard payload.

#### Evidence

- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:923–947`, `PutFilesOnClipboardAsync`, publishes a file list and remembers Cut only in `_clipboardCut`, a case-insensitive path set.
- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:949–969`, `PasteAsync`, selects Move when every current clipboard path appears in that remembered set: `paths.All(_clipboardCut.Contains)`.
- This branch has no current clipboard ownership/generation check, exact-set check, or transfer-action metadata check. The field is assigned by FileCat's clipboard publication and cleared after FileCat submits a cut paste; clipboard replacement elsewhere does not invalidate it through this code.

#### User scenario and impact

Cut file A in FileCat. Before pasting, use another application to **Copy** A, or copy only A from a previously cut set A+B. Paste into FileCat. The new file payload still satisfies the remembered path-membership test, so FileCat submits `JobKind.Move` even though the newer clipboard action was Copy. The original can be relocated contrary to the latest requested action.

The hardcoded case-insensitive set can also equate distinct-case paths on a case-sensitive filesystem. Restarting FileCat loses the remembered action entirely, demonstrating that the intended transfer action is not attached to the published payload. Engine conflict handling and ordinary mutation protections remain in place; this finding concerns the operation kind chosen before submission.

#### Minimum correction

Bind FileCat-owned Cut intent to the specific current clipboard publication. Store a generation/ownership token together with the exact resource identities and intended action, and invalidate that ownership when the clipboard is replaced. Path equality or subset membership alone must never authorize Move. Use filesystem/provider identity rules instead of unconditional case-insensitive comparison.

Coordinate with [FCUX-070](#fcux-070): capture the clipboard publication and paste invocation coherently, so an asynchronous read cannot combine an older file list with a newer Cut state.

#### Full UX target / safeguards

Import and export copy/move intent through the supported platform's actual file-transfer contract, with an explicit supported fallback when the intended action cannot be established. Verify the pinned Avalonia backend and native desktop counterpart rather than assuming a file-list API preserves all shell metadata. Avoid adding confirmation to every ordinary Copy as a substitute for correct intent.

Clear or update Cut state according to the actual lifecycle, including partial failure and retry; do not infer success merely because a job was submitted. Keep the user-facing action and operation record consistent with the current clipboard intent.

#### Acceptance criteria

1. Cut A → external Copy A → Paste submits Copy and leaves the source A present.
2. Cut A+B → external Copy A → Paste does not inherit the prior Move intent.
3. Intervening text/image/file clipboard publications invalidate ownership appropriately; a later same-path payload cannot revive obsolete Cut intent.
4. Distinct-case paths are not conflated on a case-sensitive host.
5. Ordinary FileCat Cut/Paste, application restart, canceled/failed moves and partial success have explicit, tested state transitions.
6. Tests inspect submitted job kind, source-file presence and destination manifest. Use a controllable clipboard generation in deterministic tests, then validate actual interoperation with Explorer, Finder and the supported Linux file manager. Record native limitations separately.

[Back to S1 index](#index-s1) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-070"></a>

### FCUX-070 — A delayed clipboard read can redirect Paste to a newer location

**Severity:** S1 — High · **Area:** Paste destination · **Effort:** M

**Confidence:** Source-confirmed asynchronous ownership gap. The failure requires clipboard retrieval to complete after the original tab changes location; no native timing reproduction was performed.

#### Evidence

- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:949–967`, `PasteAsync`, captures `var tab = ActiveTab`, awaits `TryGetFilesAsync`, and then checks and submits using the mutable `tab.Location`.
- The comment at line 966 promises destination capture at invocation, but the earlier code captured the tab object rather than an immutable destination.
- The text-path branch at `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:972–984` similarly awaits clipboard text and then uses the current location as its relative-path base.
- `src/FileCat.App/ViewModels/MainViewModel.Commands.cs:153–164`, `ExecuteAsync`, does not serialize subsequent navigation until clipboard completion.
- `tests/FileCat.App.Tests/OperationScopeTests.cs:25–89` checks an already-submitted F5 copy while panels change. It does not cover a paste still awaiting its clipboard payload.

#### User scenario and impact

Invoke Paste while a tab shows destination A. Hold the clipboard read, navigate that same tab to B, and then complete the read with a file list. The request uses B. For a cut paste, originals can move to a destination the user did not choose when invoking Paste.

Simply switching to another already-loaded tab is not this reproduction: the critical step is changing the location of the captured tab object. A slow clipboard owner or a deliberately delayed test double provides the required asynchronous boundary.

#### Minimum correction

Capture an immutable destination `Location`, tab/panel identity and paste invocation before the first await. Submit using that captured destination. If it disappears or becomes ineligible, explain the failure instead of redirecting to the current location.

Pair this capture with [FCUX-069](#fcux-069)'s clipboard publication identity. Do not combine an older payload with a newer destination or transfer action.

#### Full UX target / safeguards

Define closure and supersession policies separately for pending file paste and pending text-path navigation. File paste should preserve the invocation destination according to the documented contract or cancel explicitly. A stale text-path completion must not override a newer navigation intent; resolve any relative path against the captured base and apply the same ownership principles as [FCUX-022](#fcux-022).

Preserve existing operation scope after submission. The repair should not freeze panel navigation while a clipboard owner responds or turn later navigation into an implicit change of destination.

#### Acceptance criteria

1. Hold a file clipboard read, navigate the original tab from A to B, then release it. The job targets captured A or cancels explicitly; it never silently targets B.
2. Repeat for Copy and Cut, and inspect actual request destination and filesystem manifests.
3. Switching panels, moving/closing the invoking tab and replacing the clipboard while retrieval is pending follow documented policies without redirecting the job.
4. Hold a text clipboard read containing a relative location, issue a newer navigation, and release the old read. It cannot overwrite the newer navigation or resolve against the wrong base.
5. Existing F5/F6 submitted-operation scope tests remain valid. New tests exercise the pre-submission asynchronous boundary rather than only changes after job creation.

[Back to S1 index](#index-s1) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-002"></a>

### FCUX-002 — A numeric range anchor can survive a change of tab or visible ordering

**Severity:** S1 — High · **Area:** Selection · **Effort:** M

**Confidence:** source-confirmed. **User impact:** unintended files become marked after a tab/view change; a later copy, move, or delete uses a different set than the user intended. This is a selection-model correctness problem rather than a request to replace Commander-style marking with Explorer-style selection.

#### Evidence

- `src/FileCat.App/Controls/FileListControl.cs:63–64`: `_anchorRow` and `_extendState` live on the reusable list control.
- `src/FileCat.App/Controls/FileListControl.cs:328–357`, `OnPropertyChanged(TabProperty)`: switching a tab replaces `_listing`, clears text cache and scrolling, but does **not** reset or restore `_anchorRow` or `_extendState`.
- `src/FileCat.App/Controls/FileListControl.cs:523–530`, `OnListingChanged`: `_anchorRow = -1` happens only for `ListingChange.Reset`.
- `src/FileCat.App/Controls/FileListControl.cs:1083–1092`, `OnPointerPressed`: unmodified clicking stores the current **visible row number**; Shift-click uses that number as the start of `SetMarkRange`.
- `src/FileCat.Core/Listing/ListingModel.cs:160–189`: sorting/filtering/show-hidden changes call `PushSpec()`.
- `src/FileCat.Core/Listing/ListingModel.cs:624–704`, `ApplyResult`: rebuilds visible rows and emits `Rows`, normally retaining the focused **store item**. This is not a navigation `Reset`.
- `src/FileCat.Core/Listing/ListingModel.cs:784–796`, `SetMarkRange`: clamps an out-of-range anchor to the current list bounds, silently producing a valid but unintended range.

#### Current behavior and concrete reproductions

**Cross-tab case:** preload tab A and tab B. In A, click its 100th row. Activate already-loaded B, which has 10 items. Shift-click B's third item. The control can use A's anchor row 100; the range is clamped to B's end, marking items from the third item to the last. The expected range should be based on B's own remembered anchor or its currently focused item. It must never inherit a coordinate from A.

**Re-sort case:** click `large.bin` while sorted by name at visible row 2. Sort by size so that the same item moves to row 80. Shift-click row 84. `_anchorRow` remains 2, so a much broader and unrelated range is marked. The model correctly retains focused item identity; the mouse-selection layer does not.

**Filter case:** click an anchor, filter/reveal/hide items so its visible position changes, then Shift-click. The anchor resolves to whichever item now occupies the old integer row.

#### Recommended behavior

Associate the anchor with the owning tab/listing generation and a stable item identity. Resolve that item into the **current visible ordering** when extending a range. A store index is sufficient only while the same `EntryStore` instance remains active; a refresh replaces stores, so retaining an anchor across refresh requires the repository's item identity/name-kind-ordinal machinery, or an intentional reset. If the anchor is filtered away or deleted, use a documented fallback (the current focused visible item is reasonable), rather than a clamped stale integer.

At minimum, clear both anchor and extension gesture state on a tab/listing replacement and reset gesture state when the control loses keyboard focus. A stronger solution stores each tab's anchor in its view state. Do not make every `Rows` event clear the anchor: normal streaming/metadata updates should not unpredictably break a gesture; distinguish ordering changes and resolve the identity.

#### Implementation touchpoints

`FileListControl` anchor fields, `OnPropertyChanged`, `OnListingChanged`, `OnPointerPressed`, `OnKeyUp`/focus-lost handling; `ListingModel` stable identity and view-change notification contracts; optionally a per-tab selection view-state model. Coordinate with [FCUX-027](#fcux-027) because both need an explicit distinction between reordering and background refresh.

#### Acceptance criteria

1. With two preloaded tabs of different lengths, a Shift-click in B never marks a range starting from A's last clicked index.
2. After sorting the same listing in the opposite order, extending from an existing anchor uses that item's new position.
3. Removing/filtering out the anchor follows the documented fallback; reintroducing it cannot silently restore a stale index.
4. Refresh, file creation, deletion, and streaming enumeration cannot make an anchor refer to a different item.
5. Mouse Shift-click, Ctrl-click, Shift+arrows, and releasing Shift outside the window have explicitly verified state transitions.
6. Existing persistent marks and Commander-style Shift+movement semantics remain intact; only the anchor identity defect changes.

**Existing test coverage:** no UI range-selection anchor test was found in `tests/FileCat.App.Tests`. Core listing tests exercise focus/marks through sorting, but do not cover the custom control's `_anchorRow` lifetime.

[Back to S1 index](#index-s1) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-003"></a>

### FCUX-003 — Bulk rename includes hidden marks without explaining or offering control of that scope

**Severity:** S1 — High · **Area:** Bulk rename · **Effort:** M

**Confidence:** Source-confirmed inconsistent scope handling. **Affected workflow:** Ctrl+M after marking items and narrowing the panel filter.

**Evidence**

- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:124–139`, `SourceSelection`: requests `includeHiddenMarks: true` and supplies a separate hidden count.
- `src/FileCat.App/ViewModels/MainViewModel.BulkRename.cs:23–27`: materializes all those items but never reads `sel.Value.HiddenMarked`.
- `src/FileCat.App/ViewModels/MainViewModel.BulkRename.cs:109–143`: dialog lists a total count and preview; it has no hidden-mark disclosure or scope switch before submitting all items.
- Contrast with the already-implemented copy/delete controls: `src/FileCat.App/Views/OperationDialogs.cs:144–149, 279–284`.

**Scenario and impact**

Mark 100 files, narrow the panel to `*.jpg`, and invoke Ctrl+M intending to rename the now-visible photos. Items hidden by the filter are included. The total may reveal that there are more items, but does not explain why or offer the same scope control the user learned from F5/Delete. With more than 2,000 entries, some hidden items may not even appear in the preview ([FCUX-038](#fcux-038)).

**Severity rationale and safeguards:** This is ranked S1 because the review can approve a bulk mutation whose hidden scope is not explained, including entries beyond the preview limit. It is not an unconfirmed rename: total counts, the first 2,000 old/new-name previews, explicit acceptance and guarded Undo mitigate the consequence. Persistent marks across filters are a valid Commander behavior and must remain supported.

**Desired UX and implementation**

- Surface “X marked items are hidden by the current filter” prominently above the rename preview.
- Reuse the explicit include/exclude scope control already present in transfer/delete reviews. Those current defaults differ: transfer includes hidden marks by default, while deletion excludes them. Choose the bulk-rename default deliberately and display it clearly. Preserve the underlying Commander marks regardless of the subset included in this review.
- Recompute counter ordering and names when scope changes; the user must approve the exact resulting preview.
- Maintain a captured selection and never let later panel changes silently change the approved set.
- For mixed-parent result sets, display a folder/path column alongside old/new name so identical basenames can be distinguished. This is particularly useful for verifying hidden/mixed-source scope.

**Acceptance criteria**

- Filtering a marked list cannot cause unannounced renaming of hidden items.
- Include/exclude changes the item total, counter sequence, preview and submitted sources consistently.
- A test based on `OperationScopeTests.HiddenMarks` covers Ctrl+M in both states.
- No mutation runs with a preview stale relative to a scope change.

[Back to S1 index](#index-s1) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-004"></a>

### FCUX-004 — Background decisions can take focus directly onto destructive defaults

**Severity:** S1 — High · **Area:** Decision dialogs · **Effort:** M

**Confidence:** Source-confirmed focus/default/queue behavior; accidental keystroke scenario is a runtime scenario to validate. **Affected workflows:** background copies, moves with metadata loss, read-only deletion.

**Evidence**

- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:702–718`, `EnqueueDecision`/`ProcessDecisionsAsync`: incoming decisions are immediately displayed; the queue serializes decisions against each other but does not defer them behind an existing user dialog or ongoing typing.
- `src/FileCat.App/Views/OperationDialogs.cs:397–410`, `ConflictAsync`: Replace is the default when allowed; it is not marked dangerous. The default is passed with no explicit non-action initial focus.
- `src/FileCat.App/Views/OperationDialogs.cs:453–470`, `ConfirmAsync`: the first supplied action becomes the default and no dangerous class is set for Proceed.
- `src/FileCat.Core/Jobs/Executors.cs:911–913`: metadata-loss confirmation puts `Proceed` (“Move anyway”) first. `src/FileCat.Core/Jobs/Executors.cs:1601–1602` puts “Delete anyway” first for a read-only file.
- `src/FileCat.App/Views/OverlayDialogService.cs:93–98, 338–359`: adds a new overlay and posts focus to the default button; Enter confirms. It supports nested sessions without preventing this automatic interaction.
- Existing safer precedent: `src/FileCat.App/Views/VerificationResultsDialog.cs:35–36, 150–167` intentionally guards Enter/Escape when an automatically opening result may interrupt typing.

**Existing safeguards:** Replace is offered as the default only when replacement is allowed; type mismatches default to Skip, and same-item conflicts default to Keep both. Apply to all begins unchecked. The concern below is exposure to accidental approval after spontaneous focus transfer, not a reproduced overwrite.

**Current behavior and impact**

A background conflict can replace a user's current input context with a dialog whose focused action overwrites a target file. Enter intended to open a file, accept a typed path, or finish another prompt can instead resolve the new destructive decision. Metadata-loss/read-only prompts similarly reward a quick Enter with the action the warning is meant to reconsider. The issue is the combination of automatic focus transfer, destructive default, and absence of an arrival guard.

**Proposed reproduction**

Start a delayed copy that will encounter a conflict. While it runs, type in a different prompt or path box and press Enter at the time the conflict appears. Repeat with an already-open rename dialog. Confirm whether focus changes and Replace is executed. In a deterministic UI test, raise the pending decision immediately before queued Enter input.

**Desired UX and implementation**

- Represent background decision needs as persistent operation attention (“1 decision required”) and let the user deliberately open them, or at least defer modal presentation while another user dialog is active.
- Place initial focus on the conflict description or a safe decision such as Skip, rather than Replace. Keep destructive actions clearly labeled and visually identifiable.
- Add a narrowly scoped input guard for spontaneously presented decision dialogs; the existing verification guard is a starting point. Do not add delays to ordinary user-invoked F5/F6 dialogs.
- Add explicit default-action metadata to confirmation requests instead of deriving safety policy from array order.
- Preserve the current per-operation apply-to-all scope, source/target comparison, and safe cancellation semantics.

**Acceptance criteria**

- A queued Enter intended for another surface cannot replace/delete a file or approve metadata loss when a background dialog arrives.
- Opening a background decision does not stack over an unfinished user prompt without an explicit policy.
- A deliberate keyboard-only user can still efficiently choose Replace/Skip/Keep both; focus and shortcuts are documented and testable.
- Read-only deletion and metadata-loss prompts have explicit safe initial focus/defaults.

#### Ownership of queued initial focus and focus return

`src/FileCat.App/Views/OverlayDialogService.cs:58–109` posts initial focus and focus restoration after changing the dialog-session stack. As part of the focus/default redesign, validate ownership **when each posted callback executes**: the intended session is still current, the target is attached/enabled/visible, and no newer session or deliberate user interaction superseded it.

Closing a nested or superseded dialog must not restore focus into content behind another active modal layer. Preserve the invoking input and caret where meaningful; use a sensible current fallback if that element no longer exists. Do not blindly restore a previous control after the user intentionally moved elsewhere.

This is a robustness requirement for the correction, not a claim that a specific native focus race was reproduced. Test open/close before dispatcher drain, nested prompts, a new decision arriving during close, disabled/removed return targets, and explicit user focus movement. A queued Enter/Escape must affect only its intended active session.

[Back to S1 index](#index-s1) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-005"></a>

### FCUX-005 — Synchronization confirmation hides the full source and target identities

**Severity:** S1 — High · **Area:** Synchronization · **Effort:** S

**Confidence:** Source-confirmed information omission. **Affected workflow:** one-way Update/Mirror, especially matching folder names on different disks or servers.

**Evidence**

- `src/FileCat.App/ViewModels/MainViewModel.Tools.cs:378–380, 410–412` supplies full display paths in `SyncContext` and captures the real locations.
- `src/FileCat.App/Views/SyncWindow.cs:50, 56, 192`: title is just “Synchronize folders”; direction labels pass both full paths through `Short`, which keeps the last path component.
- `src/FileCat.App/Views/SyncWindow.cs:79–98, 157–179, 203–225`: remaining UI shows mode, warnings, relative paths/reasons and aggregate counts; no source/target full-path surface or tooltip is created.
- `src/FileCat.App/ViewModels/MainViewModel.Tools.cs:443–454` displays the full route only after jobs are submitted.
- `src/FileCat.App/Views/DirectoryDiffWindow.cs:203–209` opens sync as an owned window, so the comparison view may exist behind it. That is context the user can seek elsewhere, not an unambiguous confirmation in the active surface.

**Scenario and impact**

Compare `C:\Work\Project` and `E:\Backup\Project`. Sync directions read “Project → Project” in either direction. In Mirror, selecting the wrong direction can remove target-only items and overwrite approved target differences. “Left/right” is not enough once the window is detached from the panel layout, screens are rearranged, or another sync preview is open.

**Desired UX**

Show two persistent, selectable endpoint rows: “Source — C:\Work\Project” and “Target — E:\Backup\Project”. Direction changes must visibly swap those roles. Place the one-way arrow between roles; retain exact full paths in a tooltip or selectable detail if middle truncation is needed. Mirror warnings should name the target receiving removals.

**Implementation steps**

1. Render full `SyncContext.LeftName`/`RightName` in the sync window, with source/target roles derived from `SourceIsLeft`.
2. Prefer stable panel identifiers plus disambiguating path segments in the compact direction selector, but do not rely solely on basenames.
3. Update accessible names to include full endpoints and selected direction.
4. Keep the submitted captured `Location`s unchanged; this is a review/identity fix, not retargeting.

**Acceptance criteria**

- Two endpoints with identical basenames remain distinguishable before submission, by sight and screen reader.
- Full paths can be selected/copied; long paths preserve drive/server and final folder identity.
- Changing direction immediately updates source/target labeling, deletion explanation, and summary.
- Existing overlap, target revision, case collision, and recycling gates pass unchanged.

[Back to S1 index](#index-s1) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-018"></a>

### FCUX-018 — File Undo lacks a tracked lifecycle and a complete account of what it can reverse

**Severity:** S1 — High · **Area:** Undo lifecycle and review · **Effort:** L

**Confidence:** Source-confirmed lifecycle, confirmation and report behavior. Native interruption consequences require controlled validation. **Scope:** ordinary filesystem Undo; Registry Undo already uses the job engine.

#### Evidence

- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:748–764`, `UndoJobAsync`, checks eligibility and takes a step snapshot, opens confirmation, then executes `Task.Run(() => UndoService.Undo(job, fs))`. It does not reserve the steps or create visible active work. Only after the worker returns does it call `job.MarkUndone()` and show `report.Take(30)` plus an omitted-count sentence.
- `src/FileCat.Core/Jobs/Job.cs:208–214` leaves `CanUndo` true while a finished job has recorded steps; `428–432` clears all steps after an attempt. `src/FileCat.App/Views/OperationsView.axaml.cs:30–34` directly invokes Undo from an asynchronous click handler, and `src/FileCat.App/Views/OperationsView.axaml:112` binds only visibility to `CanUndo`, with no pending/disabled state.
- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:81–88` bases immediate closing and active-work review on JobManager work and unsaved hex editors. `src/FileCat.App/Views/MainWindow.axaml.cs:815–839` uses that gate; `src/FileCat.App/App.axaml.cs:62` shuts down on main-window close. A finished original job does not represent its running Undo.
- `src/FileCat.Core/Jobs/Executors.cs:1822–1835` calls `BulkRenameRunner.Run` for batch Undo with a no-op checkpoint and no journal. The optional journal-before-temporary-renaming protocol is implemented in `src/FileCat.Core/Operations/BulkRename.cs:167–222`; ordinary forward bulk rename supplies it at `281–285`.
- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:757–758` describes every ordinary inverse as “FileCat restores” and names only `Path.GetFileName(step.To)` for a single step. However, `src/FileCat.Core/Jobs/Executors.cs:1859–1901` also removes created files, empty folders and links. Link inverse records use `From` for the created entry and `To` for its target/original (`src/FileCat.Core/Operations/Links.cs:129–130`).
- `src/FileCat.Core/Jobs/Executors.cs:620–695`, `MoveByRename`, records `MoveBack` even after a same-volume move replaces an existing destination. It can move the incoming file back; it does not record overwritten destination content for restoration. The copy-then-delete move route at `1173–1247` does not record that inverse. `CanUndo` therefore cannot mean every effect of the job is reversible.
- `src/FileCat.Core/Jobs/Executors.cs:1822–1909`, `UndoService.Undo`, returns mixed success/failure strings, usually containing basenames. Failures are not prioritized and there is no complete retained ordinary Undo report.
- Registry Undo at `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:771–793` submits guarded inverse changes as a new job and already describes an irreversible deleted-key remainder. Preserve that separate behavior.

#### Scenarios and user impact

Start Undo for a large rename batch or a slow filesystem. After approval, the original row still looks finished and undoable while its inverse runs. A second Undo request can be approved before the first returns. Closing FileCat can pass the ordinary active-work gate. For dependent rename Undo, a process interruption can occur during temporary-name handling without the runner's normal journal; its native consequences must be validated with controlled fixtures.

Separately, undo a 100-item move/rename whose first 30 report entries succeed but later entries are locked, changed or blocked by newly occupied original names. The alert shows successes and “70 more”, with no outcome totals or route to the complete report. Closing it removes the only presentation of the result after the original steps have been consumed. Identical basenames in different folders compound the ambiguity.

A single generic “restores” explanation also misdescribes Undo of creating a link or empty item, whose inverse removes the created entry. For a move that replaced an existing target, availability of Undo can imply a complete reversal even though only moving the incoming file back is recorded.

**S1 rationale and safeguards:** This finding includes overlapping mutation admission and missing active-work/shutdown ownership, in addition to hidden incomplete results. These create credible loss of mutation intent or recovery context. This is not a claim of observed overwritten bytes: ordinary MoveBack checks occupied destinations and changed regular files, batch moves avoid replacement, and hard-link Undo preserves an entry that might hold the last remaining name of its data. Those checks substantially bound the consequences and must remain intact.

#### Desired behavior and implementation

1. **Reserve one attempt before the first await.** Introduce a per-original-job Undo claim that covers confirmation and execution. Release it when the user cancels approval. Once approved, admit exactly one captured inverse plan; do not have concurrent workers reread mutable `job.UndoSteps`. Keep reserved, attempting and consumed states explicit so a failed handoff cannot discard the only recorded steps.
2. **Track the inverse as active work.** Prefer a typed inverse request through existing job lifecycle, mutation admission, close/wait handling and report infrastructure. A smaller tracked-action implementation must still provide those guarantees, not only a spinner. Show which action is being reversed and its current phase. Preserve the runner's safe cancellation boundary and supply durable records when temporary names are used. Closing or hiding the original history row cannot retire an active inverse.
3. **Preview actual inverse actions.** Show full affected paths and verbs such as Move back, Restore from Recycle Bin, Remove created empty folder and Remove created link. For hard links, identify both the created name to remove and the original name that retains the data. Explain the original effects outside the recorded inverse, including overwritten destination contents. Record per-effect reversibility at execution time where needed; an initial Ask/Replace policy or job kind cannot reliably reconstruct it later.
4. **Return and retain structured outcomes.** Record inverse kind, affected path, intended resulting path where applicable, outcome, reason and any explicitly supported retry eligibility. Lead with restored/removed/kept/failed counts, put failures and unresolved items first, and provide full selectable details, Copy report and an appropriate Show affected files action. Retain a bounded report after its initial dialog closes and after the attempt is consumed. Record a useful failure result even when the worker exits unexpectedly.
5. **Preserve inverse-specific safety rules.** Keep the current changed-file, occupied-original-name and platform recycle checks. Hard-link safety depends on shared recorded identity and another surviving name; editing its shared content does not by itself make removing one created name unsafe. Directory MoveBack is not a promise of a recursive unchanged-content snapshot. Do not replace all these rules with a single blanket content-equality test.
6. **Do not add automatic replay.** Retry is optional. If provided, select only explicitly retryable unfinished effects after revalidation and a fresh review where required; never replay succeeded steps or invent restoration of data that was not retained. Keep existing canceled/failed-job, cross-volume and truncated-Undo eligibility unless a separate product change explicitly revises it.

#### Acceptance criteria

- Hold a fake filesystem call after approval. Repeated row and keyboard Undo requests produce exactly one active attempt and one eventual report. Canceling approval before mutation leaves Undo available.
- With only ordinary Undo active, app closing enters the intended active-work policy. A dependent rename inverse retains the existing safe cancellation boundary and produces discoverable journal recovery evidence in a controlled interruption fixture.
- An Undo failure beyond row 30 is visible and identifiable by full path. Outcome totals account for every attempted inverse and distinguish guarded keeps from operational failures. The report can be reopened and copied after its initial presentation closes.
- Link Undo identifies the created entry being removed; the original/target path is never presented as the entry being restored or deleted. Hard-link last-name protection remains valid, including the existing fixture in `tests/FileCat.Platform.Windows.Tests/WindowsFileOperationsTests.cs:145–178`.
- Undo of a same-volume replacement move explains that moving the incoming file back will not resurrect overwritten target contents. Mixed-effect jobs disclose the supported inverse subset and irreversible remainder using known data.
- History cleanup cannot dispose active inverse ownership or conceal its eventual result. Bounded retention and current mutation guards remain; no unsafe automatic retry is introduced.

[Back to S1 index](#index-s1) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-006"></a>

### FCUX-006 — Saving Settings can change or discard existing tool configuration

**Severity:** S1 — High · **Area:** Settings correctness · **Effort:** M

**Confidence:** Source-confirmed serialization and validation defects; the proposed desktop reproductions have not been executed. **Affected workflow:** opening Settings and accepting either unchanged values or an unrelated preference change.

#### Evidence

Several parts of the tool configuration pass through representations that cannot preserve their supported values:

- **Editor argument boundaries:** `src/FileCat.App/Views/SettingsDialog.cs:78-79,212-216` displays `Editor.Arguments` using `string.Join(" ", ...)` and saves them using `Split(' ', RemoveEmptyEntries)`. Spaces within one argument become separators; quoting is not interpreted; empty arguments disappear.
- **Association grammar:** `src/FileCat.Core/Tools/Associations.cs:29-74` splits a row on every pipe at line 37, arguments on spaces at line 67, and formats without escaping at lines 73-74. The exclusion pipe in a valid mask such as `*.txt|secret*` conflicts with the row delimiter. Pipes in arguments can create extra columns that are discarded. A valid exclusion mask can instead prevent an unchanged configuration from passing the dialog's validation because its second mask component is interpreted as the action.
- **Unrepresented launch properties:** `src/FileCat.Core/State/StateModels.cs:8-24` defines the supported `ToolDefinition` properties. Settings reconstructs Editor using only Name, Executable and Arguments (`src/FileCat.App/Views/SettingsDialog.cs:212-216`); association parsing reconstructs only Name, Mask, Intent, Executable and Arguments (`src/FileCat.Core/Tools/Associations.cs:59-68`). Nondefault WorkingDirectory and ShellMode values are lost. These values affect launch behavior at `src/FileCat.Core/Tools/ToolLauncher.cs:119,174`.
- **Unconditional reconstruction:** `src/FileCat.App/Views/SettingsDialog.cs:154-227` reparses and replaces these definitions on every OK. An untouched tool definition does not bypass the incomplete display representation.

The existing user-command grammar is a useful starting point, but it also needs correction:

- `src/FileCat.Core/Tools/UserCommandsText.cs:111-168` implements quote-aware splitting/tokenization without rejecting an unterminated quoted field. `Parse:54-105` reads the first four columns and does not reject additional columns. For example, `Run | tool.exe | "unterminated` is accepted without a parse error, while the fifth column in `Run | tool.exe | {file} | shell | ignored` is discarded.
- A command entered with the valid quoted name `"#Build"` survives its first parse. On reopening Settings, `QuoteName:44-46` does not quote its leading `#`; `Format:27-31` emits a line beginning `#Build`, and `Parse:58-61` ignores it as a comment. The next OK removes the command. A top-level group beginning with `#` has the same problem.
- `src/FileCat.App/Views/SettingsDialog.cs:179-187` rejects input only when these parsers return an error, so accepted malformed text and silently skipped commands are not caught there.

#### User scenario and impact

A user has an editor argument vector containing `--user-data-dir` followed by the single argument `C:\My Editor Profile`. They change only the theme and press OK. The spaced path becomes several arguments, and a previously working F4 invocation fails or opens with different options. A configured working directory or explicit shell mode can also reset despite never being edited.

Another user saves a command called `#Build` using the grammar's quoted-name syntax. The command disappears after the next unrelated Settings save. A third user cannot save a valid association exclusion mask because the same pipe character means both a mask exclusion and a table separator.

These are failures to preserve accepted configuration. The absence of a generic dirty indicator is not itself the defect: the consequential problem is rewriting unchanged structured values through an incomplete or permissive text format.

#### Minimum correction

1. Preserve every supported property of untouched Editor, association and user-command definitions. An unchanged OK and a theme-only change must leave their semantic configuration intact. When the user edits only one field, preserve the remaining supported fields.
2. Use a documented, lossless argument grammar for edited argument fields, including empty arguments, literal quotes, spaces and pipes. Harden the existing tokenizer before sharing it with more callers.
3. Reject unmatched quotes and unexpected columns with a field/row location. Quote command/group names that would otherwise be parsed as comments. Keep intentional comment lines supported.
4. Resolve the association mask/delimiter conflict with structured fields or an unambiguous escaped representation. A valid mask must survive formatting and parsing without changing its meaning.
5. Validate before accepting the Settings result; preserve the draft and focus the failing field as specified in [FCUX-034](#fcux-034). Do not commit a partially parsed configuration.

A complete new tool-management UI is not required to close this correctness defect.

#### Implementation details and full target

Extract and harden the valid-input rules in `UserCommandsText` into a shared parser/formatter with structured parse errors. Preserve compatibility with the existing two-, three- and four-column forms that are valid under the documented grammar; do not silently reinterpret malformed additional columns. Decide and document escaping once, rather than giving editor arguments, associations and user commands different rules.

Use typed draft values or equivalent field-level change tracking so untouched properties never depend on a lossy formatted string. If a form does not expose a supported property, carry it through unchanged. The form should not reset WorkingDirectory or ShellMode merely because it only presents program and argument fields.

The structured list/detail editors in [FCUX-036](#fcux-036) are the fuller interaction target. They can expose program selection, argument tokens, association ordering and a nonexecuting preview of the actual argument vector. Display each argument boundary explicitly in that preview; another joined string would hide the same ambiguity.

Maintain `ToolLauncher`'s structured `ArgumentList` invocation, token-substitution rules and shell safeguards. Preserve the protected-profile behavior discussed in [FCUX-035](#fcux-035); the fix must not overwrite unsupported newer-schema settings.

#### Acceptance criteria

1. Open Settings and press OK without edits; repeat after changing only Theme. Compare the full supported tool definitions before and after, including argument order, empty arguments, literal quotes, `a|b`, `{files}`, non-ASCII paths, WorkingDirectory and ShellMode.
2. Verify actual planned process argument vectors for spaced paths. Merely comparing displayed strings or item counts is insufficient.
3. A valid association using `*.txt|secret*` round-trips and retains the same matching behavior. Argument pipes and an empty argument remain distinct from record separators.
4. Save a quoted `"#Build"` command and a top-level group beginning with `#`. Reopen Settings and accept it twice; neither the command nor group disappears. Intentional comment lines remain comments.
5. The unmatched-quote and fifth-column examples above keep the same Settings session open with exact field/row feedback. No configuration is committed until corrected or explicitly canceled.
6. Include an integration-style Settings round-trip fixture in addition to parser tests. Confirm that Cancel retains the original configuration and existing theme-preview rollback.
7. Preserve valid legacy forms, current execution capability checks and launch safeguards.

#### Existing safeguards and test coverage

`tests/FileCat.Core.Tests/ToolAssociationTests.cs:9-38` already tests a full valid user-command round trip with nested groups, quoted paths, pipes and an empty argument, plus missing-column and unknown-option errors. Retain that useful coverage and extend it with the counterexamples above. The association checks at lines 43-64 cover simple arguments and compare round-tripped item count; they do not establish complete semantic preservation.

The defect does not imply that every existing tool definition is corrupted: simple program paths and single-word arguments can round-trip. Correctness must also hold for the supported values that the current forms cannot represent safely.


[Back to S1 index](#index-s1) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-007"></a>

### FCUX-007 — Previous text search can miss earlier content or fail to finish

**Severity:** S1 — High · **Area:** Viewer search · **Effort:** M

**Confidence:** Source-confirmed search-range, traversal and starting-position behavior; native interaction and cancellation presentation remain verification work. **Affected workflows:** backward text search in the viewer and hex editor, including UTF-16 files in the viewer.

#### Evidence

**The backward range is silently restricted.** `src/FileCat.App/Views/ViewerWindow.cs:734-788`, `FindAsync`, starts its backward text search at `Math.Max(0, from - 4 * 1024 * 1024)` and keeps the last hit before `from` (`769-780`). A miss is reported as the pattern not being found “before this position,” with no 4 MiB boundary (`786-789`). The same viewer method searches Info reports as UTF-8 text. Byte-pattern backward search uses the separate `ContentSearch.FindBytesBackward` function and should not be described as having this text-search window.

`src/FileCat.App/Views/HexEditorWindow.cs:567-605` duplicates the hidden 4 MiB backward text window and reports “not found ... before the cursor.” It uses Latin-1 for text search, so it shares the completeness defect but not the UTF-16 issue below.

**UTF-16 traversal can repeatedly find the same match.** The viewer advances its loop using `p = hit + 1` (`src/FileCat.App/Views/ViewerWindow.cs:777-778`). `src/FileCat.Core/Content/ContentSearch.cs:58-80,88` aligns each starting position down to the encoding unit; `src/FileCat.Core/Content/TextDecoding.cs:103` makes that unit two bytes for UTF-16. A match at byte 100 produces `p = 101`, which aligns back to byte 100, returning the same earlier match again. The loop can continue until cancellation. This work runs inside `Task.Run`; it is a stalled search task, not a UI-thread or whole-application hang.

**The viewer can continue from an obsolete hit after manual navigation.** `src/FileCat.App/Views/ViewerWindow.cs:749` uses `_lastHit` whenever it is nonnegative. Text movement raises `PositionChanged`, which is connected to `UpdateStatus` at line 233; ordinary movement does not reset that hit. `src/FileCat.App/Controls/TextViewer.cs:501-508,532-537` can move to the beginning and clear visual selection without clearing the viewer's search continuation. Consequently, a changed query after manual movement can still start after the previous hit. The editor provides a useful narrower precedent at `src/FileCat.App/Views/HexEditorWindow.cs:577-580`: it continues from a hit only while the corresponding byte range is still selected.

#### User scenario and impact

A user opens a large log near its end and searches backward for an error more than 4 MiB away. FileCat reports that no earlier occurrence exists even though the file contains one. The hex editor can produce the same misleading conclusion for text search.

For a UTF-16 file, even a small input with an earlier match can leave Previous search running indefinitely until canceled. After finding a term and moving back to the beginning, a user can also change the query and receive a result based on the previous hit's offset rather than the position they intentionally chose.

These behaviors affect the truth of search results and the user's ability to inspect a file. A completed negative result must describe the actual exhausted range, and every traversal must make progress.

#### Minimum correction

1. Search the complete requested preceding range until a match, the range's start, cancellation or an explicit read failure. A bounded buffer must not silently become a bounded search scope.
2. Guarantee progress on a valid encoding boundary. Every loop iteration must return, cancel, or move to a different search boundary. Fix both viewer and editor callers so the duplicate range defect does not remain in one surface.
3. Define when a search is a continuation and when it starts from the current position. Manual navigation, query/encoding/options changes and source/report replacement must reconcile the continuation state deliberately.
4. Present distinct completed-no-match, canceled, unreadable and deliberately limited results. If the user chooses a limited range, display that range before reporting its result.
5. Give long searches a visible Cancel/Stop action and enough direction/position feedback to understand that work is continuing.

These changes do not require a large search-results panel.

#### Implementation details and full target

Extend or reuse `ContentSearch` with a reverse text operation that takes explicit range bounds and returns an outcome distinguishable from cancellation/read failure. Use bounded reverse windows or complete forward scans over successively earlier windows with encoding-aware boundaries and sufficient overlap. Avoid scanning beyond an explicit upper bound merely to discover that the next hit lies after the cursor.

Do not replace `hit + 1` mechanically with `hit + patternByteLength` without deciding whether overlapping matches are supported: that can skip legitimate overlaps. Advance by an appropriate next character boundary or use another traversal with proven progress and documented match semantics.

Represent the continuation as belonging to a query, mode/encoding/options, source/report revision and chosen start. Retain it only while those conditions still describe the user's search. A manual reposition must not be mistaken for the programmatic scroll that reveals a just-found hit. The editor's selected-hit check is an internal precedent, not a complete substitute for this ownership decision.

Preserve existing cancellation-token and request-ownership rules. An old worker must not publish a hit or status into a newer search. Coordinate horizontal/wrapped match reveal with [FCUX-012](#fcux-012), without making the reverse-search correctness fix wait for a complete content-surface redesign.

#### Acceptance criteria

1. A file over 12 MiB with matches near 1 MiB and 8 MiB finds both in the correct reverse order from EOF. Include a match across a chunk boundary. Exercise both ViewerWindow and HexEditorWindow for the encodings each supports.
2. A small UTF-16LE file with two earlier matches completes Previous search and returns the nearer match. Repeat with UTF-16BE, supported BOM/no-BOM cases and non-ASCII surrounding characters.
3. A deterministic progress fixture catches revisiting the same search boundary. Use a timeout as a failure guard so the old UTF-16 loop cannot make the test suite hang.
4. Repeat relevant cases with match case on/off and explicit overlapping-match fixtures. Verify the chosen policy rather than changing it accidentally.
5. Find a term later in the file, move to the beginning, change the query and search again. The actual start agrees with the visible/current start policy. Mode, encoding and Info-report changes do not reuse incompatible continuation state.
6. “Not found before this position” or its replacement appears only after the complete specified preceding readable range is exhausted. Read failure and deliberate bounds remain visible qualifiers.
7. Cancel, replacement and close remain responsive to their supported cancellation boundaries; an abandoned request cannot replace the active request's status. Native keyboard and pointer checks verify the visible cancel route.

#### Existing safeguards and test coverage

`src/FileCat.App/Views/ViewerWindow.cs:219-231` already cancels a search when Escape is pressed while the Find textbox is focused; replacing or closing the viewer also cancels work. There is no visible Stop button in this viewer. Escape from content ordinarily closes the viewer, except during a Go-to-line scan (`662-669`). Preserve these existing paths while adding discoverable cancellation; do not describe the proposed Stop control as already present.

`tests/FileCat.App.Tests/ViewerSearchLifetimeTests.cs:152` and `tests/FileCat.App.Tests/HexEditorSearchLifetimeTests.cs:162` invoke `FindAsync(true)` in their helpers. They cover important forward-search lifetimes, not these reverse-text loops. `tests/FileCat.Core.Tests/ContentSearchCancellationTests.cs:28-31` checks backward bytes and forward UTF-8 text; `tests/FileCat.Core.Tests/ContentAndToolTests.cs:64-73` covers UTF-8 chunk-boundary matching and backward bytes. Retain these tests and add range/progress/continuation fixtures.


[Back to S1 index](#index-s1) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-008"></a>

### FCUX-008 — Viewer checksums can use the wrong selection scope and hide complete results

**Severity:** S1 — High · **Area:** Checksum results · **Effort:** M

**Evidence: source-confirmed.** `src/FileCat.App/Views/ViewerWindow.cs:144-146` labels the action Checksum… but immediately starts `ChecksumAsync:974-1011`. At `980`, a selection is used only when Length>1; an explicit one-byte selection falls back to the entire file. `src/FileCat.App/Controls/HexView.cs:300-309` supports an explicit one-byte selection. At `1002-1010`, the complete hash/byte count and early-end warning are put in a clipboard string; the visible status shows only the first 16 SHA-256 characters and CRC-32, and any early-end warning is omitted from that visible status. It automatically overwrites clipboard contents.

**Impact.** The user may intend a selected-byte hash and instead receive a whole-file hash. They cannot inspect or compare the complete SHA-256 without pasting elsewhere. A partial read warning may only appear after pasting, while the visible status looks successful. Computing a checksum unexpectedly destroys clipboard content that the user may be keeping for another file operation. Existing checksum revision and cancellation tests address important lifecycle behavior; preserve them.

**Desired design.** Present scope explicitly before or as the computation begins: whole file versus selected range with start/end/count. Honor a real one-byte selection distinctly from a cursor with no selection. Show a stable results pane/dialog with the complete selectable SHA-256, CRC-32, bytes actually read, source identity/revision and completion status. Copy hash/Copy all are explicit actions. For large inputs, show progress and Stop without closing the viewer.

**Implementation touchpoints.** Use Selection.Length>0 when selection scope is chosen, not >1. Keep “whole file” an explicit option. Bind result state separately from transient cursor status so moving the caret cannot erase evidence. Preserve immutable captured scope and source revision during work; surface truncated reads in the visible result and prohibit an unqualified “whole file complete” state for them.

**Acceptance criteria.** One-byte selection hashes exactly one byte; no selection hashes the whole file only when that scope is selected. Full hashes remain visible and selectable. Clipboard contents remain unchanged until a Copy action. Early end/read failure is visible without clipboard access. Cancellation leaves a clear canceled result, and source mutation cannot publish a hash from an unqualified mixed revision. Existing lifecycle tests continue to pass.

[Back to S1 index](#index-s1) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-009"></a>

### FCUX-009 — Text comparison hides the very characters the user is trying to compare

**Severity:** S1 — High · **Area:** Text comparison · **Effort:** L

**Evidence: source-confirmed; native clipping thresholds require runtime check.** `src/FileCat.App/Views/CompareWindow.cs:1038-1077`, symbols `BuildRow` and `AddCell`. Each comparison row uses columns `56,*,56,*`; each side is a `TextBlock` with `TextTrimming.CharacterEllipsis`. Inline-diff spans are painted only within these trimmed text blocks. Only lines longer than 80 characters receive a plain-text tooltip. The text view has no horizontal-scroll implementation, wrapping option or expansion of an individual row. `OnNavigationKey:291-320` navigates difference blocks, not concealed columns.

**User scenario and impact.** Two configuration lines share a long prefix and differ only in a value at character 200. The window correctly reports a difference and moves to its row, but both visible lines look the same followed by an ellipsis. The hidden suffix is the information needed to choose a file. At a narrow width, a line shorter than 81 characters may also be clipped and receives no tooltip. A tooltip for a very long line loses inline highlighting, is not an aligned two-sided comparison, and does not provide a reliable keyboard solution. The same issue affects long paths, JSON, generated code and log entries.

**Desired design.** Source content must remain inspectable. Provide synchronized horizontal scrolling for unwrapped text, plus an optional Wrap long lines setting. Difference navigation must reveal the first changed span horizontally as well as vertically. Preserve line numbers and alignment when a wrapped line occupies multiple visual rows. If limits force a summarized row, display an explicit expandable representation rather than silently treating ellipsis as the complete evidence.

**Implementation touchpoints.** Replace the comparison cells' presentation contract rather than merely increasing window width. A virtualized two-sided text surface can share horizontal offset and variable row-height mapping. Keep `BuildText`, exact equality and heuristic summary logic independent from rendering. Preserve full source strings for selection/export. If a staged implementation retains TextBlocks temporarily, add an accessible per-row detail action showing both complete lines and highlighted spans; that is an interim fallback, not the final long-line experience.

**Acceptance criteria.** At 640, 980 and 1200 logical pixels, compare lines of 60, 80, 200 and 10,000 characters with their only difference at the last character. The difference can be revealed and identified using both keyboard and pointer without copying to another program. Next/Previous difference scrolls the changed span into view. Wrap mode keeps left/right line numbers and blank gaps aligned. Existing exact-vs-text equality, first-difference, shifted-byte and source-change tests still pass. Add a meaningful UI test that asserts the changed span is actually within the viewport, not merely present in the data model.

[Back to S1 index](#index-s1) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-010"></a>

### FCUX-010 — The custom file list lacks virtual item and complete selection semantics

**Severity:** S1 — High · **Area:** Accessibility · **Effort:** L

**Confidence:** source-confirmed implementation gap; screen-reader compatibility and exact spoken output are not runtime-confirmed. **Affected users:** screen-reader users, braille users, speech-control users, and automation clients that depend on the accessibility tree.

#### Evidence

- `src/FileCat.App/Controls/FileListControl.cs:123–170`: the visual/logical children are the vertical scrollbar, rename editor and rename error box. File rows and column headers are drawn, not child controls.
- `src/FileCat.App/Controls/FileListControl.cs:512–516`: the `OnListingChanged` branch calls `AnnounceFocus()` for focus/marks/reset changes. Git status completion also calls it at line 599. These name-change announcements are useful but do not implement virtual item or selection semantics.
- `src/FileCat.App/Controls/FileListControl.cs:867–896`, `RenderEmptyState`: loading, access errors, empty results and filter explanations are drawn directly into the canvas.
- `src/FileCat.App/Controls/FileListControl.cs:1197–1215`, `DescribeFocus`: returns either `Empty list` or a concatenated description of the currently focused row. It does not include panel identity/path, total selected set, hidden-selection count, filter state, sort state, cloud status, verification state, provider-specific type, or the current failure/loading explanation.
- `src/FileCat.App/Controls/FileListControl.cs:1219–1240`, `FileListAutomationPeer`: subclasses `ControlAutomationPeer`; overrides type, class, name and keyboard-focusable flag. It implements no item/selection/scroll provider interfaces and no custom virtual row children. The list's `Name` is the current row description rather than a stable description of the list.
- `src/FileCat.App/Views/PanelView.axaml:130–132`: `AutomationProperties.Name="File list"` is present, but the custom peer overrides `GetNameCore` with `DescribeFocus()`.
- `tests/FileCat.App.Tests/AccessibilityTests.cs:20–22` explicitly describes this as summarized accessibility with manual screen-reader runs still outstanding.
- `tests/FileCat.App.Tests/AccessibilityTests.cs:31–43`: `Unnamed()` only inspects a whitelist of standard controls; custom file rows, headers, path links, and click-handling borders are absent from this check.
- `tests/FileCat.App.Tests/AccessibilityTests.cs:137–155`: the list test checks type `List`, presence of `a.txt`, `of 3`, and `marked` in one name. It does not verify selection patterns, item children, focus events, live state, or an actual assistive-technology workflow.

#### Present behavior and scenario

A person can move with arrows and may hear the name-change announcement, which is a useful existing baseline. Separate accessible Location, role and status elements also provide fallback context (`src/FileCat.App/Views/PanelView.axaml:56–71,82–98`). This finding identifies a core semantic barrier; it does not claim that screen-reader users cannot use FileCat or that native conformance failure was tested. However, assistive technology cannot obtain the list's selected item set through standard selection semantics, invoke a specific file through an item peer, inspect a row's bounds, or distinguish a focused item from the list container. Two panels focused on `a.txt` have essentially the same list name without path/role context. A failed or loading list with no focused row is described as `Empty list`, even though the visual canvas gives a very different reason and recovery action. If a parent row remains focused, only that parent-row description is available through this peer.

For example, mark several items, apply a filter hiding one mark, then switch panels and prepare an operation. The visual status includes total marks and hidden marks; the current custom peer provides only whether its one focused row is marked. A name-only unit test cannot establish that the person can reliably inspect the actual operation scope.

#### Recommended behavior

Preserve the efficient custom renderer. Build a separate, **lazy accessibility model** keyed to real items. Do not instantiate a million Avalonia controls or peers eagerly. Expose a stable panel/list name such as `Panel 1, source, C:\\Projects`; provide item peers on demand for the visible window/focused item and supported virtualized access. Implement selection-container and selection-item semantics, item invocation and scroll-into-view where applicable. Represent keyboard focus and marked selection as distinct concepts, matching the app's actual command model. Give assistive technology a discoverable way to review the complete selected set, including marks hidden by a filter.

Expose current sort/filter/selection summary and actionable listing states through appropriate state/help/live-region channels. Use provider-specific nouns: a Registry value is not simply a file; a drive is not simply a folder. A synthetic `..` row must be identified as parent navigation. Expose the same essential verification/cloud/unavailable information that is visible through badges or columns, without forcing an excessively long announcement on every arrow key. Put stable names in names, changing state in appropriate state/value/description channels, and batch repetitive background updates.

**Platform guidance:** Microsoft documents selection/scroll semantics for a selectable, scrollable `List` and selection/invoke/scroll-item patterns for appropriate `ListItem`s. It explicitly says `ITableProvider` is not for `List`; if full table semantics are chosen, select a suitable DataGrid semantic role rather than mixing incompatible patterns. This is desktop API guidance, not a WCAG legal-conformance verdict.

#### Implementation touchpoints

`FileListAutomationPeer`, a new lazy `FileListItemAutomationPeer`, listing identity/view-generation handling, `DescribeFocus` replacement or decomposition, `OnListingChanged`, `PanelView` list labeling, shared status announcement service, `AccessibilityTests` and a native manual test protocol. Check support against the pinned Avalonia 12.1.1 APIs in `Directory.Packages.props:4–8` before choosing exact interfaces.

#### Acceptance criteria

1. An accessibility inspector identifies each panel by number, role and path, independently from the focused filename.
2. A list with files exposes retrievable item semantics and a correct selected set; focus and marks remain distinct.
3. Mark/unmark, focus, sort, filtering, refresh and navigation update the correct semantic state without stale peers or incorrect bounds.
4. Empty folder, no filter matches, loading, canceled/incomplete listing and permission failure are distinguishable and their applicable recovery controls are discoverable.
5. Core workflows are exercised with native screen readers (NVDA or Narrator on Windows, VoiceOver on macOS, Orca on supported Linux), not just by calling peer getters in headless tests.
6. A million-item synthetic listing retains bounded UI-thread work and memory; idle or background updates do not generate unbounded announcements.
7. Automated checks assert provider behavior and dynamic state in addition to nonempty control names.

#### Primary references

- [Microsoft: List control type](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-supportlistcontroltype), inspected 2026-10-17.
- [Microsoft: ListItem control type](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-supportlistitemcontroltype), inspected 2026-10-17.
- [Avalonia: Accessibility](https://docs.avaloniaui.net/docs/app-development/accessibility), inspected 2026-10-17.

These references support the API mapping, not claims about tested FileCat runtime behavior.

#### Complete selection access and backend boundaries

A peer model covering only the visible window does not by itself expose marks that are offscreen or hidden by a filter. Define and test a discoverable complete-selection review route with stable identities, provider-correct item semantics, explicit hidden-mark information and bounded/lazy access. Do not represent the focused fallback item as marked when the user has made no mark.

The complete-selection requirement must not make a million-item accessibility query synchronously create a million controls or perform filesystem I/O. Use supported lazy provider behavior and, where backend limits require it, an accessible paged selection-review surface. Test both the standard selection semantics actually exposed and the person's ability to inspect the complete command scope.

Compile a small prototype against the pinned Avalonia/backend before committing to exact provider interfaces. Windows UIA pattern names are guidance for that backend, not a portable C# API inventory. Choose roles and patterns that the actual toolkit can expose faithfully; never attach incompatible table patterns merely because rows have columns. Viewer-specific event and edit-state requirements are covered separately in [FCUX-073](#fcux-073).

[Back to S1 index](#index-s1) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="findings-s2"></a>

## 4. Medium-severity findings (S2)

<a id="fcux-011"></a>

### FCUX-011 — Modal layout cannot keep essential actions reachable at supported small sizes or with long filenames

**Severity:** S2 — Medium · **Area:** Shared dialogs · **Effort:** M

**Confidence:** Source-confirmed incompatible layout constraints; exact clipping/overflow appearance requires native rendering validation.

**Evidence**

- `src/FileCat.App/Views/MainWindow.axaml:9` allows a 640 × 420 window.
- `src/FileCat.App/Views/OverlayDialogService.cs:76–79, 112–136`: 24-DIP outer margins, an unconstrained vertical StackPanel for header/body/footer, `ClipToBounds = true`, and a horizontal nonwrapping footer.
- `src/FileCat.App/Views/OverlayDialogService.cs:338`: custom cards are capped at 760 DIP.
- `src/FileCat.App/Themes/Styles.axaml:236–266`: body/footer add 36 DIP horizontal padding.
- `src/FileCat.App/ViewModels/MainViewModel.BulkRename.cs:52, 109–127`: preview has minimum width 700 and fixed height 280, with multiple editor rows above and summary/footer below. This cannot fit the 640 × 420 supported client area.
- `src/FileCat.App/Views/SettingsDialog.cs:19,58–98,234–257`: Settings also combines a 720-DIP child minimum, several 460-DIP input minima and a page scroller capped at 520 DIPs. Individual page scrolling exists; the shared frame does not bound it to the available owner viewport.
- `src/FileCat.App/Views/OperationDialogs.cs:397–407`: conflict footer can contain six actions; “Keep both” embeds the entire suggested filename, without a length bound.

**Scenario and impact**

Resize FileCat to its minimum allowed size, open Bulk Rename, or trigger a conflict involving a long but valid filename. At large text sizes/high scaling, the same constraints arise in larger windows. The layout has no mechanism to scroll the dialog body while reserving the footer, or to wrap the footer. A decision surface can therefore hide the action needed to finish or cancel; keyboard focus may move to controls the user cannot see.

**Mitigations and severity:** Escape cancellation remains available globally, and enlarging/maximizing the owner can make the controls reachable. This is S2 for inaccessible visible controls within supported small layouts, not a claim that the user is trapped with no cancellation path.

**Desired UX and implementation**

1. Make the shared card a grid with header, bounded scrollable body, and always-visible footer. Derive maximum dimensions from the actual host client area, including all margins/padding.
2. Use adaptive form grids or wrapping rows; avoid a child's minimum width larger than the dialog's available space.
3. Let secondary conflict actions wrap or move to a secondary menu, while preserving clear primary/safe decisions. Keep the generated filename in the body with selectable full text, not in the button's width.
4. Use bounded/virtualized list height that consumes remaining space. Make dialog focus scroll controls into view.
5. Reuse `VerificationResultsDialog`'s constrained sections as an internal precedent, but implement at the card level so every caller benefits.

**Acceptance criteria**

- At 640 × 420 client size, primary and cancel actions remain visible/reachable for transfer, delete, conflict and bulk-rename dialogs.
- Test 100%, 150%, 200% scaling and larger fonts in the native platform environment, with long local/network paths and 200-character filenames.
- No horizontal control overlap, clipped buttons, or tab focus on permanently off-screen controls.
- Footer order and keyboard semantics remain consistent when buttons wrap.


[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-012"></a>

### FCUX-012 — Viewer geometry can hide content and change selected text after reflow

**Severity:** S2 — Medium · **Area:** Viewer geometry and selection · **Effort:** L

**Confidence:** Source-confirmed geometry and selection-coordinate behavior; exact clipping widths depend on native font metrics and rendering. **Affected workflows:** reading long lines, inspecting Info, enlarging hex content and preserving a text selection through layout changes.

#### Evidence

**Horizontal access is missing in affected modes.** `src/FileCat.App/Controls/TextViewer.cs:175-185,286-291,376-410,525-547` creates only a vertical scrollbar, draws each unwrapped line from x=8 and handles vertical navigation. `src/FileCat.App/Views/ViewerWindow.cs:65,427-439` creates Info with wrapping off and hides its wrap control. `src/FileCat.App/Controls/HexView.cs:19,33-52,187-197,217-288,319-327,393` fixes rows at 16 bytes, creates only a vertical scrollbar and scrolls the cursor only by row.

The viewer allows a 480-DIP-wide window (`src/FileCat.App/Views/ViewerWindow.cs:109-112`); the hex editor allows 620 DIPs (`src/FileCat.App/Views/HexEditorWindow.cs:73`). Content font size can increase to 32 (`src/FileCat.App/Controls/TextViewer.cs:90-98`; `src/FileCat.App/Controls/HexView.cs:122-129`; viewer shortcuts at `709-719` and editor shortcuts at `326-327`). At eight-digit offsets, the HexView content end is approximately `8 + 76 × characterWidth` before scrollbar clearance. It can exceed the available width, particularly at larger content font sizes.

**Text selection belongs to rendered coordinates rather than the source.** `src/FileCat.App/Controls/TextViewer.cs:40-41` stores `_selStart` and `_selEnd` as row/column pairs. Wrap and FontSize invalidate/reflow without reprojecting or clearing those pairs (`79-98`), and arranging a different width also leaves them unchanged (`181-185`). `Layout:352-370` rebuilds the rows for the new width; `GetSelectedOrVisibleText:594-605` then reads the stored coordinates against those new rows. `SetReader/SetEncoding:103-119` similarly lack a selection-reconciliation step.

For a deterministic example, `ABCDEFGHIJKLMNOPQRSTUVWXYZ` at ten columns has zero-based row 1 `KLMNOPQRST`. Selecting column range `[2, 5)`, with an exclusive end, selects `MNO`. At twenty columns, row 1 becomes `UVWXYZ`, so the same stored coordinates select `WXY`. The native dimensions at which those column counts occur require measurement; the coordinate-identity defect follows from the code.

#### User scenario and impact

A long symbol/import/path in Info extends beyond the right edge. In Hex mode, a user narrows the window or enlarges text for readability and loses the rightmost bytes and text column. Navigation can move the active editable byte offscreen because `EnsureVisible` changes only the row.

A user selects a value in Text mode and resizes the window, changes font size or toggles Wrap before copying. The selection can now identify a different substring. A readability change should neither conceal the current byte nor change which source characters the user selected.

#### Minimum correction

1. Make complete unwrapped Text/Info content horizontally reachable and expose a usable Info wrap option. A long match or Go-to target must be revealed at its actual source position.
2. Give Hex horizontal scrolling or adaptive byte grouping, and reveal the caret on both axes during navigation and editing. Either approach is acceptable if every byte and required column is reachable at the chosen font.
3. Preserve selection through reflow using source-relative endpoints with a source-to-display mapping. A geometry change must not reinterpret an old rendered row/column as different source text.
4. Reconcile selection deliberately when the source/report or encoding changes. An old range cannot silently select unrelated new content.
5. Keep the chosen content font and supported window sizes meaningful. Silently shrinking text until content fits defeats the user's readability choice.

An interim selection invalidation needs an explicit scope transition. Simply clearing a range and allowing the next Ctrl+C to copy all visible rows would replace one surprise with another.

#### Implementation details and full target

Introduce a horizontal offset into TextViewer rendering, hit testing, selection geometry and highlighted-match reveal. Maintain source-relative endpoints and per-visible-segment character mappings through wrapping, tab expansion and control-character display. Do not build an unbounded whole-file line index merely to preserve a selection.

For Hex, a standard horizontal viewport is a contained solution. Adaptive 16/8/4-byte grouping is an optional alternative. If choosing adaptive rows, `src/FileCat.App/Controls/HexCompareView.cs:209-224` already provides a width-aware grouping and top-byte-preservation precedent. Share geometry only where it fits the controls' needs; do not require a large common renderer before correcting access.

Whichever approach is selected, drawing, hit testing, cursor movement, row count, offset descriptions and selection must use the same geometry. Preserve the approximate visible source anchor across font/width changes. Keep Info's report styling while giving it the same complete-content navigation contract.

Coordinate source-copy fidelity with [FCUX-076](#fcux-076). Correct selection identity and correct copied representation are related but separate checks: a highlighted range can remain stable while its copied tabs or logical line endings are still changed.

#### Acceptance criteria

1. At the supported 480-DIP viewer and 620-DIP editor widths, and at a wider 980-DIP case, every byte and full Info line is reachable with 13/18/24/32 content font sizes. Evaluate each application's actual supported minimum rather than forcing the editor below its minimum.
2. Navigate and edit across all byte positions in an original 16-byte row. The active byte and active text/hex column remain visible; horizontal scrolling or regrouping preserves source offsets.
3. Include a source over 4 GiB so wider offset labels are measured. Verify hit testing and column clearance with actual font metrics.
4. Select a unique substring, resize the window, change font size and toggle Wrap. The selection and copied source substring remain the same. Include tab expansion and a bounded giant-line segment.
5. Change source/report and encoding with a prior selection. Preserve it only under a documented valid correspondence; otherwise indicate that the prior selection ended and keep subsequent Copy scope explicit.
6. A long search match is revealed at the actual column/wrapped segment. Text wrap changes preserve the approximate source position and do not silently omit content in copy or automation descriptions.
7. Native keyboard/pointer checks confirm that scroll controls, caret and selection are visible and usable. Model-only assertions that complete strings exist do not establish viewport access.

#### Existing workarounds and safeguards

Text mode already offers Wrap. Ctrl+minus can reduce Text/Info/Hex font size, and enlarging the window can expose more content. `src/FileCat.App/Controls/TextViewer.cs:589-612` can also copy rendered row strings that include an unwrapped offscreen suffix. Those strings are sanitized for display and can include synthetic segment line breaks; this is an inspection workaround, not source-faithful export. [FCUX-076](#fcux-076) addresses that distinction.

Info specifically hides Wrap, so sufficiently long lines still lack an in-view horizontal/wrapped route. Hex may fit after font reduction. These mitigations support S2; this finding does not claim that every Text-mode file is unreadable.

Ordinary `ScrollRows` and `ScrollToOffset` clear selection (`src/FileCat.App/Controls/TextViewer.cs:458-461,501-508`). The stale-range claim concerns layout/encoding/source transitions that do not reconcile it; it does not describe every scrolling action. Preserve the application's bounded decoding, paging, word wrapping and content-lifetime protections.


[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-076"></a>

### FCUX-076 — Viewer Copy can change text or copy only part of the selected bytes

**Severity:** S2 — Medium · **Area:** Content copying · **Effort:** M

**Confidence:** Source-confirmed representation and copy-bound behavior; exact clipboard results across native backends remain verification work. **Affected workflows:** copying text/Info from the viewer and copying a large selected byte range from ViewerWindow's Hex mode.

#### Evidence

**Text Copy uses the display representation.** `src/FileCat.App/Controls/TextViewer.cs:193-250` reads a logical line or a bounded segment, then calls `Sanitize` before storing its text. `Sanitize:253-278` expands tabs to four-column display stops, renders NUL as a middle dot and replaces other controls with control-picture characters. `Layout:352-370` stores these transformed strings in `Row.Text`.

`GetSelectedOrVisibleText:589-612` copies `Row.Text` rather than reading the selected decoded source range. It creates line endings with `StringBuilder.AppendLine`. `src/FileCat.App/Views/ViewerWindow.cs:961-965` uses this method for Text/Info and reports only “Copied text.”

There is also a logical-line boundary loss. `src/FileCat.App/Controls/TextViewer.cs:240-247` splits giant lines into bounded 16 KiB segments. At line 369, the final visual row of each segment is labeled `IsLastRowOfLine`, even when no source newline ended that segment. Copy can therefore insert a platform newline between adjacent parts of a single long source line. This can occur with Wrap off; ordinary word wrapping is not the underlying cause.

**Viewer hex Copy limits the range without an explicit partial-result qualifier.** `src/FileCat.App/Views/ViewerWindow.cs:949-959` reads at most 1 MiB from the selected range. At `963-965` it copies that prefix and reports the number of bytes copied, without the requested count or a first-part/limit explanation. A larger range is selectable through the existing Shift+Ctrl+End behavior (`src/FileCat.App/Controls/HexView.cs:330-354`). If a read returns less than requested, the same success-shaped count does not explain the shortfall.

The following exceptions are important:

- `src/FileCat.App/Views/HexEditorWindow.cs:448-473` already says “the first 1 MiB” when its copy is capped.
- `src/FileCat.App/Controls/HexCompareView.cs:629-654` already states first X of Y selected and distinguishes unread remaining content.
- `src/FileCat.App/Views/ReportWindow.cs:295-300` copies the original complete report string for Copy all. Its selected/visible-row Copy uses TextViewer, but the report already has a full-copy route.

The missing cap qualifier is therefore specific to ViewerWindow, not every hex surface.

#### User scenario and impact

A user copies a tab-separated row into a spreadsheet and receives spaces instead of separators, or copies a Makefile recipe and loses the source tab. A long single-line JSON/log record copied while unwrapped gains line breaks at internal segment boundaries. The displayed text may be readable, but a command presented simply as Copy does not explain that the representation changes.

In Hex mode, a user selects two MiB and pastes a one-MiB prefix. The copied count is accurate, but recognizing truncation requires remembering and comparing the original selected count. The transient result does not explicitly say that bytes were omitted.

This is S2: the copied result can have different meaning or scope, while the original file remains unchanged. The user deliberately invoked Copy, so replacing the clipboard is expected; the problem is what gets copied and how its limits are communicated.

#### Minimum correction

1. Define Copy source text and Copy displayed text explicitly. Ordinary source selection should preserve its decoded characters, tabs and logical line boundaries. Keep the sanitized display form as a named alternative where useful.
2. Make no-selection behavior explicit, for example Copy visible text. Do not silently change an existing selection into a whole-visible-region copy after reflow; coordinate selection identity with [FCUX-012](#fcux-012).
3. Distinguish real source line endings from wrapped rows and bounded-segment continuations. Display layout must not manufacture source newlines.
4. Retain clipboard memory bounds. When only a prefix is copied, report both counts and the limit, for example “Copied the first 1 MiB of 2 MiB selected.” Keep the result available long enough to inspect.
5. Distinguish a deliberate copy cap from a read shortfall or cancellation. A short read is not an unqualified complete copy.
6. Define limitations of the active clipboard representation. Source text means characters decoded using the selected encoding; it is not a promise of original-byte fidelity. If a character cannot be represented faithfully by a supported clipboard backend, explain the limitation and offer a suitable escaped display or byte-export route.

A complete large-selection export can be a separate streamed Save selected range action. Unlimited clipboard allocation is not required to correct the disclosure defect.

#### Implementation details and full target

Keep a source-to-display mapping for the selected/visible segments so tabs, control pictures and wrapped positions can resolve back to the intended decoded source range. Preserve source-relative selection as specified in [FCUX-012](#fcux-012). Store actual newline/continuation metadata separately from whether a segment is the last visual row produced by a bounded read.

Read and decode the selected source range with bounded work. Define line-ending behavior explicitly; preserve original logical endings by default or make any normalization a deliberate named choice. Do not include rendered line numbers, decorative markers or artificial segment boundaries in source copying.

Use a result model or equivalent data carrying requested range, copied count, representation and completion reason. Reuse the truthful prefix wording already present in HexCompare/HexEditor. A deliberate limit and an actual I/O shortfall should have different messages, even if they happen to yield the same byte count.

The fuller target is a consistent source/display copy policy across viewers, editors and comparisons, with an explicit large-range export route where supported. The controls can keep different task-appropriate toolbars. [FCUX-056](#fcux-056) covers adding text selection/copy to comparison; [FCUX-008](#fcux-008) separately covers checksum scope and result presentation.

#### Acceptance criteria

1. Copy `A`, U+0009, `B` from a UTF-8 text file. Default source-copy output retains the tab; explicitly chosen displayed-text copy agrees with the displayed spaces.
2. Exercise NUL and other control characters under each supported representation. Preserve or explicitly explain the clipboard backend's limitation; do not describe substituted characters as a faithful source copy.
3. Copy a 40,000-character single logical line with Wrap off and across a segment boundary with Wrap on. No newline is introduced by the 16 KiB segmentation or visual wrapping.
4. Test CRLF, LF and a final line without a newline. Exact clipboard assertions match the documented line-ending policy.
5. Copy ranges of 1 MiB, 1 MiB + 1 byte and 2 MiB in ViewerWindow Hex mode. The copied result either covers the complete chosen range or clearly states requested/copied counts and the deliberate cap. Existing HexEditor/HexCompare disclosure remains intact.
6. Simulate a read shortfall before the selected range ends. The visible result identifies incomplete reading separately from the cap, rather than only reporting a generic copied-byte count.
7. Cancel, close or replace a copy request during delayed I/O. Preserve current request ownership; an abandoned request cannot replace the newer clipboard result or status.
8. If Save selected range is offered, stream the complete captured range within bounded memory, preserve source identity and report any shortfall. Do not add it as a compulsory architecture dependency for the initial truthful-prefix correction.
9. The original source remains unchanged throughout all copy/export tests.

#### Existing safeguards and test coverage

`tests/FileCat.App.Tests/ViewerCopyLifetimeTests.cs:24-42,50-67,88-92` covers important copy cancellation/replacement/close behavior and exact bytes in smaller ranges. It does not establish a >1 MiB selected-range contract or TextViewer's source-versus-display fidelity. Retain those lifecycle assertions.

`tests/FileCat.App.Tests/TextViewerWrapTests.cs` verifies that `Segments` preserves the characters of its input string during ordinary wrapping. That is useful existing behavior. It does not cover the earlier sanitization step or the later synthetic logical-line separators described here.

Keep the intentional paging and copy budgets, the ReportWindow Copy all route, and the editor/comparison truncation disclosures. The correction should make copy meaning and completeness explicit while preserving those protections.


[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-072"></a>

### FCUX-072 — Binary comparison has no keyboard route to select an arbitrary right-side byte range

**Severity:** S2 — Medium · **Area:** Binary comparison access · **Effort:** M

**Confidence:** Source-confirmed interaction and semantic gaps. The exact native accessibility tree and spoken output have not been exercised.

#### Evidence

- `src/FileCat.App/Controls/HexCompareView.cs:517–542` uses arrows, PageUp/PageDown and Home/End for viewport scrolling, not byte-caret or range selection.
- Pointer handling at `src/FileCat.App/Controls/HexCompareView.cs:458–494` establishes a side and byte range. Ctrl+A at `556–561` selects the whole last-used side and defaults to the left; it cannot choose the right side from a fresh keyboard-only session.
- The context menu at `src/FileCat.App/Controls/HexCompareView.cs:597–620` offers Copy and Select all, with no side selection or start/end range action.
- The control's only visual/logical child at `src/FileCat.App/Controls/HexCompareView.cs:56–74` is the scrollbar; the class implements no custom automation peer for its drawn bytes.
- `src/FileCat.App/Views/CompareWindow.cs:134–136` supplies the useful static name “Bytes of both files.” Difference navigation and the chooser at `291–320,342–353` are real keyboard entry points, but do not establish a byte selection.

#### User scenario and impact

Navigate to a difference and try to copy four bytes from the right file without a pointer. Ctrl+C initially has no selected range; Ctrl+A selects the whole left file. A pointer user can select the precise right-side range directly.

Opening the original files separately is an indirect workaround. The comparison surface should support inspecting the exact bytes that explain a difference. This is separate from the text-selection/search gap in [FCUX-056](#fcux-056) and the clipped textual evidence in [FCUX-009](#fcux-009).

#### Minimum correction

Add a documented keyboard-accessible action that selects the side and exact byte range, such as “Select byte range…”. Show source side, start and length explicitly; validate against the current content and selection revision. Provide a route to inspect the selected bytes and their comparison meaning through accessible controls.

This smaller correction can coexist with the current arrow-to-scroll behavior. It does not require immediately replacing every navigation key or introducing an editable merge surface.

#### Full UX target / safeguards

A focusable byte caret and range extension are a richer alternative. If implemented, distinguish keyboard focus, active side, selected range and current difference, and preserve established difference navigation.

Expose side, offset, length, byte values, comparison relation, gaps and not-loaded state through an appropriate bounded semantic model. Do not create a peer/control per byte or assume a blank alignment gap represents a zero byte. Preserve bounded nonblocking reads.

Retain existing copy-limit disclosure: `src/FileCat.App/Controls/HexCompareView.cs:625–654` caps copied bytes and explicitly reports truncated/unread selection content. This surface must not be described as silently copying an unlimited range.

#### Acceptance criteria

1. From a fresh keyboard-only comparison, select and copy an exact four-byte range on the right; repeat on the left.
2. Repeat in same-offset and aligned views, across a row boundary and adjacent to an alignment gap.
3. Visible selection, clipboard bytes, accessible side/range and inspected source identities agree.
4. Invalid/outdated range input preserves the draft and explains the error without copying from a different side or revision.
5. Preserve F8/Shift+F8, Alt+arrow difference navigation, existing copy limits/disclosure and meaningful scrolling.
6. Inspect the native semantic representation and complete the range-selection/copy workflow with a supported screen reader; a static control name is insufficient evidence.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-073"></a>

### FCUX-073 — Viewer accessibility events can describe stale content and omit editing state

**Severity:** S2 — Medium · **Area:** Viewer semantics · **Effort:** M

**Confidence:** Source-confirmed event ordering and state omissions. Their exact spoken consequences depend on native clients and remain untested.

#### Evidence

**An accessibility baseline already exists.** `src/FileCat.App/Controls/ViewerAutomation.cs:7–55` gives TextViewer and HexView named Document peers with read-only `IValueProvider` values. `tests/FileCat.App.Tests/AccessibilityTests.cs:178–209` inspects that baseline after reading/rendering content. This finding does not claim the controls have no accessibility implementation.

**Text events can precede the data they describe.**

- `src/FileCat.App/Controls/TextViewer.cs:54–72` announces content on `PositionChanged`.
- `ScrollRows`, `ScrollToOffset` and `GoToEnd` at `src/FileCat.App/Controls/TextViewer.cs:458–522` change the source position, invalidate rendering, and then raise that event.
- The announced Value reads `_rows` through `GetSelectedOrVisibleText` at `src/FileCat.App/Controls/TextViewer.cs:590–612`. Those rows are rebuilt only by `Layout` during `Render` at `352–380`. The event's value can therefore describe the previous viewport.
- `src/FileCat.App/Controls/TextViewer.cs:129–137`, `OnPageLoaded`, redraws without announcing current content. Pointer selection changes at `550–572` likewise redraw without notifying the changed Value.

**Hex/editor state is incomplete.**

- `src/FileCat.App/Controls/HexView.cs:98–107`, `DescribeCursor`, supplies byte/row text but omits selected range and active hex/text column.
- `src/FileCat.App/Controls/ViewerAutomation.cs:43–54`, `HexViewAutomationPeer`, always reports read-only and suppresses repeated cursor descriptions.
- The same HexView is the editable surface in `src/FileCat.App/Views/HexEditorWindow.cs:35–38`. Active column and pending nibble have visual state in `src/FileCat.App/Controls/HexView.cs:259–295`; selection and pointer column handling are implemented at `src/FileCat.App/Controls/HexView.cs:330–398`. The editing host must expose the effective input/eligibility state as well.
- Changing active column raises CursorMoved at `src/FileCat.App/Controls/HexView.cs:66–75`, but returns the same cursor description and can be suppressed by the peer. Page loading at `146–154` only redraws.

#### User scenario and impact

A user scrolls a text file while relying on content-change announcements. The event can carry the prior visible rows; after a delayed page arrives, no corresponding content announcement is issued. A getter queried later can be correct, so a test that merely waits for rendering and reads Value misses the event-ordering defect.

In the hex editor, toggling from hex typing to text typing changes what the next key will do, while the cursor description can remain identical. Selected range, pending first nibble and blocked editing also require deliberate semantic presentation. Separate visual status text and the window title are useful mitigations, but do not establish that a user at the content surface can determine the effective editing state.

#### Minimum correction

Publish semantic changes from a committed current content/selection model after relevant asynchronous data becomes available. Do not read the previous paint buffer at navigation time. Track content and view generations so an older load cannot announce into a newer file or viewport; deduplicate stable state and avoid announcing every repaint.

Give editing hosts a deliberate way to expose typing mode, selected range, pending nibble, unsaved state and blocked editing. Preserve the useful read-only viewer baseline and nonblocking content access.

#### Full UX target / safeguards

Separate read-only inspection semantics from editable byte operations. Do not simply make `IValueProvider.SetValue` overwrite a binary file or imply that a descriptive cursor string is editable.

Validate the actual pinned Avalonia 12.1.1/backend support before selecting richer document, range, selection and edit contracts. Windows UIA contract names are not proof that matching portable C# interfaces are available. Use a small compiled/native prototype or a supported accessible inspection/editing surface where necessary; do not substitute unbounded eager content extraction.

Preserve Commander/editor keyboard behavior. In particular, `src/FileCat.App/Views/HexEditorWindow.cs:299–336` intentionally uses Tab to switch hex/text and provides Ctrl+F to the search field. This is not evidence of an absolute keyboard trap. Verify a usable route from content to toolbar/recovery controls and back.

#### Acceptance criteria

1. Record event payloads and getter values around a known scroll. A new-content event never carries the previous viewport's text.
2. Delayed first-page loading produces discoverable current content after completion without requiring another navigation key.
3. Selection changes, source switches and canceled old loads update only the current semantic state.
4. In the editor, a user can identify hex versus text typing, exact selected range, pending nibble, unsaved changes and blocked editing before acting.
5. Verify focus and content state after returning from search, toolbar and recovery controls without changing established Tab behavior accidentally.
6. Extend event/state tests beyond nonempty names and Value getter checks, then complete native reading/editing journeys with supported assistive technology.
7. Idle painting and repeated equivalent events do not flood speech output or cause synchronous filesystem reads.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-013"></a>

### FCUX-013 — Theme contrast checks miss destructive buttons and custom-drawn selected content

**Severity:** S2 — Medium · **Area:** Theme state contrast · **Effort:** M

**Evidence: source-confirmed numeric calculation, not screenshot estimation.** `src/FileCat.App/Themes/Styles.axaml:272-275` sets Button.danger foreground to White on FcError. These buttons are used for confirmation and stop actions (`src/FileCat.App/Views/OverlayDialogService.cs:217-242,313-317`; `src/FileCat.App/Views/HexEditorWindow.cs:48`). `src/FileCat.App/Services/ThemeManager.cs:55-163` defines the exact opaque Error colors. Applying the standard sRGB contrast formula to white text and each error fill yields:

| Theme | Danger fill | White text contrast |
| --- | --- | ---: |
| Classic | #C42B1C | 5.661:1 |
| Classic Dark | #FF6A5C | 2.812:1 |
| Cyberpunk | #FF817E | 2.416:1 |
| Psychedelic | #FF5C8A | 2.937:1 |
| Steampunk | #E0573A | 3.754:1 |
| High Contrast | #FF4040 | 3.466:1 |
| DOS Commander | #FF5555 | 3.143:1 |

These are intended normal-state opaque color pairs; hover/pressed/disabled compositing requires render checks. Six of seven explicit palettes fall below a 4.5:1 normal-text target, including High Contrast. Existing tests cover OnAccent/OnTarget, not danger text (`tests/FileCat.App.Tests/ThemePickerTests.cs:84-115`).

Additionally, `src/FileCat.App/Controls/TextViewer.cs:140-148,399-410`, `src/FileCat.App/Controls/HexView.cs:157-166,245-288`, and `src/FileCat.App/Controls/HexCompareView.cs:167-174,369-414` use FcFocusBackground for selection but continue drawing FcText rather than FcFocusText. In DOS Commander, this means #55FFFF over #00AAAA: **2.337:1**, although the palette supplies black FocusText. Text compare uses hardcoded translucent red/green/yellow/gray fills (`src/FileCat.App/Views/CompareWindow.cs:31-35,1029-1075`), including inline differences, outside the semantic theme-state matrix. Text comparison does not provide an inline underline/shape equivalent to the binary compare's cue.

**Additional essential-text state.** `src/FileCat.App/Controls/FileListControl.cs:779–785` draws unmarked, unfocused hidden/system/unavailable rows with the dim-text token. For ordinary hidden files on the active opaque panel, the exact pairs are Classic `#8A8A8A` on `#FFFFFF` (**3.452:1**), Classic Dark `#7C7C7C` on `#232323` (**3.765:1**), and DOS Commander `#5555FF` on `#0000AA` (**2.613:1**). Hidden files remain actionable, so their names are not automatically exempt as disabled controls. Include this state in the palette fix while retaining a distinguishable hidden-item treatment. The ratios were independently recomputed from source colors using sRGB relative luminance; they are not JPEG-pixel estimates.

**User scenario and impact.** A user choosing a high-contrast theme still struggles to read an important destructive action. In DOS-themed viewers, selected text becomes much harder to read just when the user is examining/copying it. Users who cannot distinguish red/green/yellow have insufficient per-span detail in text comparison. This is a failure of state styling, not a reason to eliminate themes.

**Desired design.** Define foreground/background pairs for danger, selection, changed content, inserted/removed content and search hits. Select foregrounds based on their actual rendered backgrounds. Add noncolor markers for text diff rows and changed spans. Maintain strong, distinct focus outlines. Keep decorative color rotation separate from semantic fills.

**Implementation touchpoints.** Add FcDangerFill/FcOnDanger or an equivalent semantic pair; apply it consistently for normal, pointer-over, pressed and focused destructive buttons. Do not repurpose FcError indiscriminately, because it is also text on cards. Custom-drawn views must draw selection spans with their selected foreground rather than painting a background behind a single full-line foreground. Theme-aware diff styles should replace raw static brushes where contrast/noncolor semantics require it.

**Acceptance criteria.** A state-matrix check covers all seven palettes and System-resolved modes: normal text, muted text, selection, search hit, changed span, primary, danger, focus, current difference and hover/pressed states. Normal-sized essential text meets at least 4.5:1; meaningful boundaries/icons meet the selected nontext target. Selected DOS bytes are readable. In grayscale, text diff inserted/removed/changed states remain distinguishable by signs/labels/underline/pattern, and inline changed characters remain identifiable. Existing primary/target and tooltip tests stay intact.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-014"></a>

### FCUX-014 — Separate extension cells bypass safe filename display

**Severity:** S2 — Medium · **Area:** Filename identity · **Effort:** S

**Confidence:** source-confirmed bypass; the exact native glyph result needs screenshot verification. **No claim:** this does not demonstrate code execution, extension-handler confusion, or a filesystem identity collision.

**Severity boundary:** This is S2 for identity presentation. Raw filesystem identity and execution remain unchanged in the reviewed evidence; no exploit or mistaken operation was demonstrated. Native glyph behavior remains a validation target.

#### Evidence

- `src/FileCat.App/Controls/Columns.cs:95–100`: the default Windows Details profile splits `Name` and `Ext`.
- `src/FileCat.App/Controls/FileListControl.cs:906–910`, `CellString`: `ColumnField.Extension` returns raw `NameParts.GetExtension(e.Name)`.
- `src/FileCat.App/Controls/FileListControl.cs:920–927`, `DisplayName`: when an extension column is present, it strips the extension and calls `Formatters.SafeName` only on the remaining stem.
- `src/FileCat.Core/Resources/Entries.cs:205–225`, `NameParts`: intentionally returns raw name portions and does not normalize/escape them. This is correct for a model-level helper; the missing boundary is presentation.
- `src/FileCat.App/Services/Formatters.cs:194–223`: `SafeName` visibly escapes hidden controls, bidi controls and nonfunctional invisible characters.
- `tests/FileCat.App.Tests/FormattersTests.cs:37–51`: tests explicitly require a trailing zero-width-space after `x.iso` to remain visibly detectable.
- `src/FileCat.App/Controls/FileListControl.cs:1214–1215`: spoken name uses raw `e.Name`, so this also needs a deliberate accessible presentation strategy.

#### Scenario and impact

Prepare distinct filesystem names `x.iso` and `x.iso` followed by U+200B. In Details, both stems are `x`; the second extension contains `iso` plus an invisible character, but the extension cell bypasses `SafeName`. The visual list can therefore make the two identities look identical. In Brief, the entire filename is passed through `SafeName`, and the distinguishing escape becomes visible. The formatter's unit tests exercise `SafeName` in isolation and do not cover this split-column presentation bypass; they were inspected, not executed during this audit.

Bidi controls or line/paragraph separators placed in the extension follow the same bypass. Do not solve this by removing legitimate Unicode or normalizing raw filenames; that would change identity rather than presenting it faithfully.

#### Recommendation and implementation

Apply the shared safe-display policy to **every user-controlled displayed name component**, including extension cells, full-name tooltips and accessible descriptions. Keep `NameParts` raw for operations and sorting. If a shortened cell cannot fit the escape, provide the full safe text on hover/focus and an explicit distinguishing cue; a different icon alone is insufficient to prove identity. Preserve valid emoji sequences and shaping behavior already protected by the formatter tests.

Implement in `CellString(ColumnField.Extension)` and centralized display helpers; ensure the accessibility overhaul uses an appropriate spoken description of invisible characters instead of silently passing them through. Review provider-specific filename columns for the same omission, but do not turn this into an unaudited security claim about all text fields.

#### Acceptance criteria

1. `x.iso` and `x.iso<U+200B>` are distinguishable in Details, Full, Brief and custom profiles.
2. A control/bidi character in an extension is shown safely and cannot break the row's layout or conceal the fact that the raw name contains it.
3. An integration test obtains the actual rendered cell text/presentation model, rather than testing `SafeName` in isolation.
4. Copy, rename, open and operation snapshots still use exact raw identities.
5. Existing legitimate non-ASCII, emoji/joiner and variation-selector test cases remain visually correct.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-071"></a>

### FCUX-071 — Clipboard and drag export silently omit non-filesystem items from mixed selections

**Severity:** S2 — Medium · **Area:** Selection export · **Effort:** M

**Confidence:** Source-confirmed selection projection defect. Native application-to-application transfer was not executed.

#### Evidence

- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:925–944` filters `GetSelection()` to non-null `FileSystemPath` values. It reports unsupported items only when no local paths remain, then also discards null storage-provider conversions.
- `src/FileCat.App/Views/MainWindow.axaml.cs:221–257` filters drag selection to local paths and calls staging for the original selection only when that filtered list is empty. A mixed selection bypasses staging/refusal and exports only its filesystem subset. Null storage-item conversions are skipped again.
- Mixed selections are supported data, not hypothetical malformed input: `src/FileCat.Core/Search/ResultSetProvider.cs:8–12,59–79,243–255` retains arbitrary original `ItemRef` parents in result/working sets.
- `src/FileCat.Core/Resources/Entries.cs:181–184` supplies `FileSystemPath` only for filesystem parents. `src/FileCat.Core/Listing/ListingModel.cs:1035–1066`, `GetSelection`, preserves marked original references, including hidden marks.
- `tests/FileCat.Core.Tests/DragStagingTests.cs:67–114` tests staging directly, not the UI's mixed-selection branch that bypasses it.

#### User scenario and impact

A working set contains one local file and one archive member. Mark both and Copy or drag them. Only the local file is exported. “Copied 1 item” is numerically truthful but does not explain why the selected second item was omitted; drag provides no omission count.

An all-archive drag would instead use supported staging. Adding one local item therefore changes the fate of archive members already in the selection. Similar omission occurs when a storage-provider conversion fails for one selected path.

#### Minimum correction

Preflight the complete captured selection and account for every item before replacing the clipboard or starting a drag. Refuse an unsupported mixture with a clear explanation and a supported alternative, or export all eligible items within the existing policy.

If partial export is offered, disclose the exact excluded count and reasons and let the user choose that scope. A refused operation must leave the previous clipboard intact. Do not report a success count without explaining which requested items were excluded.

#### Full UX target / safeguards

Support mixed local/staged export only where the existing provider and quota policies allow it. Preserve the 5,000-item drag admission in `src/FileCat.App/Views/MainWindow.axaml.cs:201,225–231`, and the 100-item/64-MiB staging limits, download-origin propagation, partial-recovery refusals and cleanup in `src/FileCat.Core/Operations/DragStaging.cs:14–35,67–89`.

Unbounded remote staging is not required. A clear refusal and F5 alternative can be the correct behavior. Keep hidden marks in the captured-scope accounting and release selection/staging resources along every success, refusal and cancellation path.

#### Acceptance criteria

1. Cover local-only, archive-only, local+archive, local+remote, unsupported-only and failed storage-item conversion.
2. The requested count equals exported plus explicitly excluded items; no original reference silently disappears.
3. Refusal leaves the existing clipboard unchanged and does not begin an incomplete drag.
4. Partial export, if supported, requires a reviewable scope choice and identifies omitted items/reasons.
5. Verify actual transferred identities and contents, including marks hidden by a filter.
6. Existing archive staging limits, origin marks, partial-recovery refusal and cleanup remain intact. Tests cover the UI admission branch as well as the staging helper.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-075"></a>

### FCUX-075 — Unsupported drop targets advertise Copy or Move and then silently reject the drop

**Severity:** S2 — Medium · **Area:** Drop feedback · **Effort:** S

**Confidence:** Source-confirmed mismatch between advertised effect and acceptance. Native drag-cursor appearance and desktop interoperation remain untested.

#### Evidence

- `src/FileCat.App/Views/MainWindow.axaml.cs:203–220`, `AttachDragDrop`, allows drops over every PanelView and advertises Copy/Move whenever the transfer contains file data. It does not check the displayed destination's provider or accepted transfer capabilities.
- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:1001–1005`, `DropFilesAsync`, activates the panel and silently returns when its active location is not filesystem-backed.
- Ordinary filesystem drops do have an explicit transfer review at `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:1012–1020`; this finding does not claim that valid drops bypass it.

#### User scenario and impact

Drag filesystem files over an archive, Registry or working-set panel. The handler advertises an affirmative Copy/Move effect, but dropping then does nothing apart from potentially changing the active panel. The user cannot tell whether the transfer was accepted, is waiting, failed or is unsupported.

#### Minimum correction

Determine advertised effects from the target location, supported provider capabilities and incoming payload before accepting the drop. Revalidate on Drop because location/capabilities may change after DragOver. Return no allowed effect for unsupported destinations and provide a concise explanation and supported alternative when an attempted drop needs feedback.

Do not activate an unrelated panel as a side effect of an operation that was never accepted.

#### Full UX target / safeguards

Keep accepted drop destination and operation kind explicit. Preserve ordinary filesystem transfer review, source scope and engine protections. Supporting every provider is not required: a truthful refusal can be the complete correction.

Use consistent capability rules with [FCUX-048](#fcux-048)'s location actions, but do not conflate a background context menu with a drag target. Maintain the complete export scope required by [FCUX-071](#fcux-071).

#### Acceptance criteria

1. Hover feedback and actual acceptance agree for local, archive, Registry, working-set and disconnected targets.
2. A target that changes location/capabilities between DragOver and Drop is revalidated without silently redirecting the operation.
3. Unsupported drops explain the limitation and supported alternative; they do not appear to start work or unexpectedly change active-panel scope.
4. Valid local drops retain the transfer review and the captured destination.
5. Native tests record the offered effect, cursor feedback, resulting review/job and actual destination, rather than only the presence of file data.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-015"></a>

### FCUX-015 — The selected operation's error report stops updating until the user changes selection

**Severity:** S2 — Medium · **Area:** Live operation reports · **Effort:** S

**Confidence:** Source-confirmed binding defect. **Affected workflow:** opening Ctrl+J while a job runs, then seeing new errors/warnings.

**Evidence**

- `src/FileCat.Core/Jobs/Job.cs:139–143` returns a new copied list from `Issues`; it is not an observable collection.
- `src/FileCat.Core/Jobs/Job.cs:380–386` appends an issue and raises `Changed`.
- `src/FileCat.App/ViewModels/OperationCenterViewModel.cs:284–287` handles `JobChanged` by refreshing the job VM and summary, without notifying `SelectedIssues`.
- `src/FileCat.App/ViewModels/OperationCenterViewModel.cs:307–309`: `SelectedIssues` reads the snapshot and raises its property change only in `OnSelectedChanged`.
- `src/FileCat.App/Views/OperationsView.axaml:144–157` binds the issue ListBox to that property.

**Scenario and impact**

Select a running job before it has problems. Let it encounter failures or skip conflicts, then finish. Counts/state can show issues while the report remains empty or displays only its initial subset. Closing/reopening the drawer does not necessarily fix it because its selected job is retained. Choosing another job and back forces a refresh. This undermines the pane's role as the authoritative explanation of incomplete work.

**Fix**

In the UI-thread `JobChanged` handler, invalidate the selected report when the changed job is selected and its issue version/count changed. Alternatively expose a bounded observable report model on `JobViewModel`, updated at an appropriate rate. Avoid copying all 10,000 issues at every 250-ms progress tick. On selection/open, immediately reconcile current report state. Preserve the selected issue/scroll position when appending unless the user is following the newest entry.

**Acceptance criteria**

- With the same job selected throughout, a newly added issue appears without selecting a different row or reopening the drawer.
- The final report matches issue count/state after completion, including failures received between UI dispatches.
- A focused test appends two issues after selection and observes the bound list updating; it must not validate only the getter's latest value.
- Progress polling does not cause unbounded report allocation or reset the user's scroll position.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-016"></a>

### FCUX-016 — Progress text calls a requested pause “Paused” and canceled/failed work “100%”

**Severity:** S2 — Medium · **Area:** Operation status · **Effort:** S

**Confidence:** Source-confirmed state-to-text mapping.

**Evidence:** `src/FileCat.Core/Jobs/Job.cs:252–256` sets `JobState.Pausing` immediately when pause is requested, before the safe point. `src/FileCat.App/ViewModels/OperationCenterViewModel.cs:167–173` maps `Paused or Pausing` to the same “Paused” label. `src/FileCat.App/ViewModels/OperationCenterViewModel.cs:101, 118, 167` assigns 100 and “100%” to every `IsFinished` state. `src/FileCat.Core/Jobs/JobModel.cs:70` includes Canceled, Failed and Interrupted as finished states. `src/FileCat.App/Views/OperationsView.axaml:127–131` displays terminal `PercentText` beside `PhaseText`, even when no progress bar remains.

**Scenario and impact:** Pause a slow/native file operation: the visible label promises it has stopped although it may still be writing. Cancel after 10%: the details say “Canceled 100%”, mixing lifecycle completion with completion of requested work. Counts and error text are available elsewhere, but the prominent progress metric is misleading.

**Implementation:** Distinguish “Pausing — waiting for a safe point” from “Paused” and expose Resume only as appropriate to requested/effective state. For cancellation/failure/interruption, retain the last meaningful processed fraction or omit percentage and lead with “Stopped: X done, Y skipped, Z failed, W not processed” where counts are known. Keep task completion and data completion separate in the view model; avoid making a canceled bar appear successful. Preserve the existing growing-total/ETA uncertainty and verification accounting.

**Acceptance criteria:** A delayed pause remains “Pausing” until the worker acknowledges the safe point. Failed/canceled/interrupted jobs never show an unqualified 100% completion claim. Fully completed jobs may show 100%; completed-with-issues must retain clear outcome counts. No regression to estimating ETA before totals are final.

#### Preserve the distinction between settled work and transferred bytes

A presentation fix must retain the engine's deliberate accounting model. `src/FileCat.Core/Jobs/Job.cs:64–89` separates actual `DataBytesProcessed` from logical byte credit, verification work and `SettledBytes`. A skipped conflict receives logical byte credit at `src/FileCat.Core/Jobs/Executors.cs:884–889`; a same-volume move also credits the item's logical size at `691–697`. `src/FileCat.App/ViewModels/OperationCenterViewModel.cs:224–236` correctly uses actual processed data for the throughput graph. `tests/FileCat.Core.Tests/JobDataPaceTests.cs:10–62` protects that distinction.

Keep a processed/settled fraction if it remains useful for progress, but label its meaning. Do not describe that value as bytes physically copied or as proof of complete mutation success. Prefer a neutral terminal indicator where no accurate completion fraction exists, and do not invent an exact not-processed count while enumeration was incomplete. Correct the phase/terminal wording without destabilizing ETA, verification totals or speed measurement.

**Additional acceptance:** expected conflict skips and same-volume renames cause no transfer-speed spike or claim that their logical bytes were copied. Canceled partial discovery retains an explicit unknown/growing denominator. Terminal labels and actual outcome counts remain readable without changing the underlying safe completion accounting.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-017"></a>

### FCUX-017 — Ordinary operation reports do not expose complete paths or a complete remediation record

**Severity:** S2 — Medium · **Area:** Operation evidence · **Effort:** M

**Confidence:** Source-confirmed UI/report limits.

**Evidence:** `src/FileCat.App/Views/OperationsView.axaml:144–157` renders issue message/path as TextBlocks; path is ellipsized, without tooltip/selectable detail/copy actions. `src/FileCat.App/ViewModels/OperationCenterViewModel.cs:307` exposes only raw issues, and `src/FileCat.App/Views/OperationsView.axaml.cs` provides no report export or issue-navigation action. `src/FileCat.Core/Jobs/Job.cs:380–386` silently retains at most 10,000 issues, while `IssueCount` reports only retained list length. Compare the existing complete report copy and failed-file panel support in `src/FileCat.App/Views/VerificationResultsDialog.cs:130–148`.

**Scenario and impact:** Copy a deep directory tree with skipped/failed files and inspect Ctrl+J. A long path is cut off exactly where the useful filename might be. The message cannot be selected/copied; the user has to transcribe it and navigate manually. Above 10,000 issues the UI gives no indication that details are incomplete, despite the application supporting very large listings.

**Implementation:** Add a selected-issue detail region with full selectable source/target paths, Copy path/message, and reveal/open containing folder where valid. Add Copy report for the complete retained report and summary totals. Maintain an independent total issue counter plus a truncation indicator; when memory caps apply, explicitly say “Showing first N of M issues” or stream a bounded/durable report through the existing job journal/report infrastructure. Do not remove the cap or allocate a million in-memory controls.

**Acceptance criteria:** A deeply nested failed file can be located and its exact error copied without transcription. Report totals and omission indicators are truthful. Mixed local/remote paths are represented accurately. All retained outcomes can be exported; the UI remains virtualized/bounded.

#### Preserve the effect and reason behind each outcome

The complete report must distinguish expected policy exclusions, guard refusals, partial effects and failures. `src/FileCat.Core/Jobs/JobManager.cs:266–273` classifies any skipped item as CompletedWithIssues, including the user's chosen Skip policy; a skipped item is not automatically an error. `src/FileCat.Core/Jobs/Executors.cs:1082–1095` can count an item as skipped after successfully copying it because the original or followed link must be kept. At `1175–1203`, a published copy can count as done while a warning explains that source removal did not complete. “Done” therefore cannot always be rendered as “fully moved,” and “skipped” does not always mean nothing changed.

Represent requested action, committed effect, source/target disposition, reason and certainty in structured report rows. Preserve the original severity while presenting precise text such as “Copied; source kept.” Keep totals independently of the bounded detail list, with an omission indicator when not every detail is retained. Where exact counts are unknown, say so. This is reporting work; it does not require a Retry action. If a retry route is later provided, it must select only explicitly eligible unfinished effects after appropriate review. Retrying every skipped root can contradict the chosen policy or repeat an effect that already copied data.

**Additional acceptance:** ordinary policy Skip remains distinguishable from an IO failure; Move/Keep source says both versions remain; a refused source deletion is distinguishable from a failed copy. Full paths, summary counts and report exports preserve these distinctions even after the retained-detail cap is reached.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-019"></a>

### FCUX-019 — Clearing operation history also removes undo availability without explaining that consequence

**Severity:** S2 — Medium · **Area:** Undo retention · **Effort:** M

**Confidence:** Source-confirmed; related misleading Ctrl+Z wording also confirmed.

**Evidence**

- `src/FileCat.App/Views/OperationsView.axaml:79, 119` labels actions “Clear finished” and “Remove”, without explaining loss of undo/report state.
- `src/FileCat.App/Views/OperationsView.axaml.cs:52–59`, `src/FileCat.App/ViewModels/OperationCenterViewModel.cs:393–412`: removes the rows and corresponding manager jobs immediately.
- `src/FileCat.Core/Jobs/JobManager.cs:79–89`: removes the job from `_jobs`; `src/FileCat.Core/Jobs/JobManager.cs:33, 121–130` also bounds retained finished jobs to 50.
- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:729–744`: global Undo searches only retained jobs, choosing the most recent *undoable* one. Its only special guard for a newer non-undoable operation is `UndoTruncated`.
- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:758`: confirmation calls the selected eligible job “the last operation” even if a more recent copy/permanent deletion was not undoable. It does show the job title, which mitigates but does not remove the misleading wording.

**Scenario and impact**

Complete a move, clear finished rows to declutter, then press Ctrl+Z. Undo is gone even though the file state has not changed. Separately, complete a rename followed by a copy and press Ctrl+Z; the confirmation calls the rename “the last operation”. Users need an accurate relationship between history, undo eligibility, and bounded retention.

**Implementation**

Choose and disclose the intended model. Minimum fix: rename the action “Clear finished history…” and explain that eligible undo actions and reports are removed; require a specific confirmation only when useful undo records or unresolved failure reports would be discarded. Preferred if small enough: separate visual-history hiding from the bounded undo stack while preserving memory ownership limits. Change Ctrl+Z wording to “Undo [operation title]” / “most recent undoable operation”, and say when newer actions are not undoable. Do not expand retention indefinitely.

**Acceptance criteria**

- Users can predict whether clearing history removes undo.
- Generic Undo never describes an older action as the literal last action.
- The exact action and full source/target scope are reviewable before undo.
- Existing retirement/GC tests still pass; retention remains bounded.

#### Bound retention without losing an active inverse

`src/FileCat.Core/Jobs/Job.cs:208–214` defines current eligibility as a completed/completed-with-issues job with recorded inverse steps. Keep that policy explicit: availability means some guarded inverses exist, not full restoration of every original effect. The Undo workflow described in [FCUX-018](#fcux-018) must retain its own active lifetime even if the original finished row is hidden or removed. History cleanup may retire eligible unused records according to the disclosed limit, but must not cancel or discard a running inverse or its eventual result.

**Additional acceptance:** clearing the original finished history during an admitted inverse does not dispose its active ownership, erase pending outcome details or make the same inverse available for a duplicate attempt. Unused Undo records and completed reports remain bounded; this change does not implicitly enable Undo for canceled jobs, unsupported effects or records that exceeded the existing step limit.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-020"></a>

### FCUX-020 — “Dismiss” permanently ends recovery tracking instead of merely hiding the interruption

**Severity:** S2 — Medium · **Area:** Recovery dismissal · **Effort:** S

**Confidence:** Source-confirmed.

**Evidence:** `src/FileCat.App/Views/OperationsView.axaml:65–68` offers “Dismiss” beside Run again/Clean up. `src/FileCat.App/Views/OperationsView.axaml.cs:94–99` removes the row once `DismissInterruptedAsync` succeeds. `src/FileCat.App/ViewModels/MainViewModel.Interrupted.cs:195–202` immediately writes `JournalRecovery.TryClose(..., "Dismissed by the user.")`, without explaining the consequence or confirming unresolved work. `src/FileCat.Core/Jobs/JobJournal.cs:379–380, 403–408` treats an ended journal as finished, excludes it from the interrupted list and eventually prunes it.

**Scenario and impact:** After an interrupted bulk rename, a temporary name or partial copy still needs review. A user clicks Dismiss to declutter, expecting the message to return after reopening FileCat or be available in history. The journal becomes finished and automatic recovery no longer offers it. No file is deleted by Dismiss itself; the harm is losing the discoverable recovery workflow.

**Implementation:** Distinguish “Hide for now” from “Mark reviewed and stop reminding”. If only the latter is supported, label it explicitly and explain that it leaves files unchanged but removes the recovery action from subsequent startup. Show unresolved-step count and provide a copyable report/location before closing tracking. For known remaining rename intents, use a deliberate confirmation. Retain current in-use journal admission and same-action serialization.

**Acceptance criteria:** Dismissing an interruption cannot be mistaken for merely closing a banner. Temporary/partial paths remain discoverable from a review record or explicit report. Hide-for-now, if offered, does not write an end record. Mark-reviewed does not mutate the files and preserves concurrency protections.

#### Keep diagnostic history separate from eligibility for automatic recovery

Do not interpret this finding as requiring every kept partial file to leave its journal open indefinitely. `src/FileCat.App/ViewModels/MainViewModel.Interrupted.cs:179–187` can alert about retained or unreviewable partial files and then close the reviewed journal. `tests/FileCat.App.Tests/InterruptedCopyCleanupTests.cs:31–61` explicitly covers that intentional closure. Unresolved temporary rename intents have a different rule and retain the journal (`src/FileCat.App/ViewModels/MainViewModel.Interrupted.cs:173–183`; `tests/FileCat.App.Tests/InterruptedRenameTests.cs:141–148`). Preserve both distinctions.

Retain a bounded, inspectable review record of known deleted, kept, unreviewed and unresolved effects without making an already reviewed record runnable again. `src/FileCat.App/ViewModels/InterruptedJobText.cs:10–24` already discloses bullet-list and review limits; add a complete retained-detail/copy route so the first ten paths are not the only inspectable evidence. Explicitly label unexamined remainder instead of presenting an invented complete count. Journal closure, diagnostic retention and file mutation must remain separate actions.

**Additional acceptance:** intentional cleanup-with-kept-partials still closes or retains the journal according to its existing policy, while known retained paths remain inspectable. Unresolved rename intents remain eligible for their current review workflow. Viewing a closed diagnostic record never reenables automatic mutation or broadens the previously reviewed deletion set.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-021"></a>

### FCUX-021 — Sync option changes reset choices, and submitted steps have no action to stop the whole plan

**Severity:** S2 — Medium · **Area:** Synchronization review · **Effort:** M

**Confidence:** Source-confirmed review-state and job-submission behavior. **Scope:** the existing one-way synchronization workflow, including review and control of its submitted steps.

#### Evidence

- `src/FileCat.App/Views/SyncWindow.cs:60–63, 140–145` rebuilds `_items = SyncPlanner.Propose(...)` when direction or mode changes, discarding the user's changes to individual `Include` values.
- `src/FileCat.App/Views/SyncWindow.cs:39, 63, 161–167, 183–189` creates the permanent-delete checkbox once. Direction/mode changes can hide and show it without resetting its checked state; the value is reused to permit permanent deletion on the current target.
- `src/FileCat.Core/Compare/Sync.cs:124–191`, `BuildRequests`, separates the plan into up to four queued jobs: new copies, replace-if-newer, replacements and removals. There is no shared synchronization identity or dependent-outcome policy in these requests.
- `src/FileCat.App/ViewModels/MainViewModel.Tools.cs:443–454`, `StartSync`, submits every request immediately and does not retain the returned jobs as one group. Its notification names one synchronization and points to Ctrl+J for the operations.
- `src/FileCat.App/Views/OperationsView.axaml.cs:28` cancels only the selected job. `src/FileCat.Core/Jobs/JobManager.cs:141–207` schedules according to active/queued scope and device blockers; finished predecessors no longer block. Completion calls `Schedule` at `263`, including after a failed or canceled preceding stage.
- Existing safeguards are substantive: `src/FileCat.Core/Compare/Sync.cs:173, 187–188` retains compared-target and folder-content guards; `src/FileCat.App/Views/SyncWindow.cs:161–189` gates actual removal requests on recycling capability or permanent-delete consent. `src/FileCat.Core/Compare/Sync.cs:92` leaves newer target files unchecked even in Mirror unless the user includes them.

#### Scenarios and impact

In Mirror, exclude a target-only folder, briefly switch to Update to compare the plan, then return to Mirror. The folder is included again without notice. If permanent deletion was selected for one target, swapping direction can carry that acknowledgment to another target. The controls look like deliberate per-item exceptions, so silent regeneration is easy to overlook.

After approving a plan with both new copies and target-only removals, cancel the copy stage in Ctrl+J. Other submitted stages remain queued and can run after it stops. A failed copy also does not automatically stop later removals. The user must reconstruct which rows belong to the reviewed task and stop each relevant unfinished job separately.

**Severity boundary:** S2 remains appropriate. Each current Cancel acts on its specifically titled child-job row; it does stop that job. Other child jobs remain visible and individually cancelable, and the original sync approval included their actions. The finding is missing whole-plan control and an explicit partial-failure policy, not unauthorized deletions or proof that a child Cancel fails. Existing compared-state guards also continue to protect individual targets.

#### Desired behavior and implementation

1. **Preserve or visibly reset review choices.** On mode/direction changes, retain exclusions only where the same source, target and action identity remains meaningful. Otherwise state “Plan rebuilt; exclusions reset” and require review of the changed plan. Keep action counts and the included preview aligned; opening details, sorting rows or changing a display-only filter must not change inclusion.
2. **Tie deletion consent to its meaning.** Name the current target and bind acknowledgment to the reviewed target/removal scope. Invalidate it when direction or another change moves beyond that scope. Do not let a hidden checked control authorize a new target. Define whether re-including an already acknowledged item remains covered; an expanded or different removal set must not silently inherit stale acknowledgment. Inline review state is sufficient; avoid a modal on every exploratory option change.
3. **Carry one synchronization identity into execution.** Add a lightweight group record containing captured endpoints, reviewed plan revision, child-job IDs and aggregate state. Register group ownership before activating children, so an early stop or failure cannot be followed by an untracked later removal. Keep the existing ordinary executors and resource scheduling.
4. **Offer an explicit Stop this synchronization action.** Stop all unfinished children of that group at their existing safe boundaries. If individual cancellation remains, label it Cancel this step and keep other pending stages visible. Show one group outcome with completed, retained/skipped, failed and unfinished effects; do not describe an incomplete group as entirely synchronized.
5. **Choose and disclose the partial-failure policy.** A reasonable proposed policy is to hold pending removals for review after unexpected transfer failure or cancellation. This is an intentional product behavior change, not a fix to an allegedly broken scheduler. Expected policy skips require different treatment from failures; use actual reasons and effects. Do not make an unrelated job's failure stop a sync or make a group Stop cancel unrelated work.
6. **Preserve operation boundaries.** Keep current target-revision, folder-content, overlap, case-collision and no-recycle protections. Never broaden the approved item set, transfer consent to another target, infer a need to overwrite newer target files, or automatically roll back committed work. A new two-way or transactional synchronization engine is unnecessary.

#### Acceptance criteria

- Excluded deletions do not silently reappear as approved removals after toggling modes. A reset plan is visibly identified and its submitted actions match the reviewed version.
- Direction changes do not transfer permanent-delete acknowledgment to another target. Checked rows, runnable counts and the resulting requests agree; existing no-recycle gating remains.
- Group Stop during a deliberately delayed copy prevents every unstarted child removal from beginning; the active child stops safely and unrelated jobs continue.
- Cancel this step, if offered, accurately states that other group steps remain queued. A transfer failure produces a coherent group outcome and the documented disposition of pending removals.
- Two synchronizations between identical endpoints retain separate review identities, Stop scopes and reports. Existing newer-target, changed-target and changed-folder protection fixtures remain valid.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-022"></a>

### FCUX-022 — An older asynchronous navigation can override the user's newer destination

**Severity:** S2 — Medium · **Area:** Navigation intent · **Effort:** M

**Confidence:** Source-confirmed missing ownership/generation check; timing-dependent user scenario requires a controlled delayed provider test.

#### Evidence

- `src/FileCat.App/ViewModels/TabViewModel.cs:302–324`, `Navigate`: SFTP home resolution or provider preparation starts before changing the listing. Both preparation and SFTP leasing receive `CancellationToken.None`.
- `src/FileCat.App/ViewModels/TabViewModel.cs:388–403`, `PrepareThenNavigateAsync`: after awaiting preparation, updates the banner or calls `Navigate(ready, ...)` if the tab is not disposed. It does not check whether a newer navigation occurred.
- `src/FileCat.App/ViewModels/TabViewModel.cs:405–422`, `ConnectThenNavigateAsync`: successful connection likewise checks only disposal before navigating. The error path posts a banner without either a navigation-generation check or a disposed check inside the posted action.
- `src/FileCat.Core/Network/UnixNetworkProvider.cs:165–175` is a real deferred preparation path: it mounts/finds a share, then returns a filesystem location.
- `src/FileCat.App/ViewModels/TabViewModel.cs:445–454` implements Back/Forward history navigation separately. It does not invalidate pending preparation/connection requests.

#### Current behavior and user impact

A user starts opening a slow network share or SFTP home, changes their mind, and navigates the same tab to a local folder. When the original preparation completes, FileCat navigates back to the older request. A late failure can instead replace a relevant banner with an error for a destination the user has already abandoned. Unexpected location changes can discard the current listing's marks, alter Back history and cause the next user action to operate in an unexpected context.

**Existing safeguard/caveat:** `ListingModel` does use generations and cancellation to reject old enumeration results (`src/FileCat.Core/Listing/ListingModel.cs:41–42, 381–389, 483–497`). Those protections apply after `Load` starts. The older *preparation* later invokes a fresh `Navigate`/`Load`, so it becomes a new valid listing request. This finding must not be described as missing enumeration isolation generally. Other tab callbacks already guard captured location, e.g. `LeaveVanishedFolder` at `src/FileCat.App/ViewModels/TabViewModel.cs:286–293` and `ShowLinkTarget` at `371–383`.

#### Reproduction

Use a fixture provider whose `PrepareAsync` is held by a task-completion source. Request location A, navigate to B or invoke Back, then release A. Test both successful preparation and failure. Repeat with an SFTP home connection delayed by a fake connector. Observe location, focus, history and banner after completion. Include disposal while the callback is queued.

#### Recommended implementation

Introduce a per-tab navigation-intent generation and linked cancellation token. Increment/invalidate it at every new user navigation, including Back, Forward, parent/root, replacement and disposal. Capture generation before preparation. Before changing the location, history, banner or focus, verify that the tab and generation still own the request. Separate public “begin navigation intent” from internal “commit prepared navigation” so the same intent does not accidentally invalidate itself. Cancellation must discard late UI effects even when native mounting/connection cannot stop immediately. Keep shared connection pooling and other tabs' leases alive as required.

#### Acceptance criteria

- Completing or failing A after B has been requested never changes B's location, banner, marks or history.
- Back/Forward and explicit cancellation supersede pending preparation just as direct navigation does.
- Closing the tab prevents every late presentation side effect, including queued error messages.
- A current successful preparation still navigates once with correct focus/history.
- Existing SFTP connection, refused-link navigation, and listing-generation tests remain valid.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-023"></a>

### FCUX-023 — Free-space information can remain stale and can describe the wrong mounted filesystem

**Severity:** S2 — Medium · **Area:** Capacity feedback · **Effort:** M

**Confidence:** Source-confirmed cache and volume-selection logic. No actual disk-capacity measurements were made.

#### Evidence

- `src/FileCat.App/ViewModels/TabViewModel.cs:565–574, 595–617`, `RequestFreeSpace`: successful listing completion requests space, but a cached nonnegative value is reused indefinitely when `device == _freeBytesDevice`. There is no expiry or forced-refresh parameter.
- `src/FileCat.App/ViewModels/TabViewModel.cs:498–503`, `Refresh`, and `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:639–656`, `RefreshAffected`: refresh listings after explicit refresh/operations without invalidating the capacity cache.
- `src/FileCat.App/ViewModels/TabViewModel.cs:606–607`: queries `new DriveInfo(Path.GetPathRoot(loc.Path)).AvailableFreeSpace`.
- `src/FileCat.Core/FileSystem/LocalFileSystemProvider.cs:86` delegates the device key to `PathUtil.GetDeviceKey`; `src/FileCat.Core/FileSystem/PathUtil.cs:142–148` uses the lexical root on Unix, so `/media/usb/...` and `/home/...` both group under `/`.
- `src/FileCat.Core/Platform/IPlatform.cs:75–77`: the Unix portable provider does not refine that key.
- Existing suitable concept: `src/FileCat.Core/FileSystem/UnixFiles.cs:158–174`, `MountOf`, finds the longest containing mount point. The panel's capacity query does not use it.

#### Scenario and impact

View a destination, complete a large copy to the same drive, then refresh. The displayed “X free” value can remain the pre-copy value. On Linux/macOS, navigating to a separate mounted volume can show the root filesystem's capacity instead, because the query is made against `/`. Users use this number to decide where data will fit, so it must describe the current destination rather than a stale/shared IO grouping.

**Additional caveat:** Windows provider device keys intentionally group network shares/mapped drives by SMB server (`src/FileCat.Platform.Windows/WindowsFileSystemProvider.cs:8–22`), so the scheduler's device key is not a reliable capacity-cache identity there either. Preserve scheduler grouping; do not “fix” IO isolation merely to correct free-space caching. The existing location-match guard (`src/FileCat.App/ViewModels/TabViewModel.cs:613, 654–657`) correctly suppresses stale results for a different currently displayed location, but it does not refresh an old value reused under a matching key.

#### Recommended implementation

Introduce a provider/platform capacity query for the filesystem that actually contains the displayed path, returning capacity identity and available-to-user bytes. On Unix, reuse/extend mount resolution or a suitable path-based filesystem query; account for resolved links and mount changes. Separate that identity from the IO scheduler key. Refresh after relevant completed mutations and explicit user Refresh, and use a short expiry/on-activation refresh for external changes. Run queries asynchronously through existing IO scheduling, coalesce concurrent requests and discard obsolete responses. If unavailable, show unknown or clearly stale information rather than presenting a cached number as current.

#### Acceptance criteria

- Copy/delete followed by Refresh produces a newly queried value for that destination.
- Two mounted Unix filesystems with different free capacities display their own values.
- Different shares on one Windows server do not reuse each other's capacity solely because they share a scheduler key.
- A late result from a previous location/mount never appears on the new location.
- Slow/disconnected capacity queries do not freeze navigation or flood the device with requests.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-024"></a>

### FCUX-024 — Saving a workspace defaults to replacing an existing workspace without review

**Severity:** S2 — Medium · **Area:** Workspace saving · **Effort:** M

**Confidence:** Source-confirmed default and overwrite flow.

#### Evidence

- `src/FileCat.App/ViewModels/MainViewModel.Workspaces.cs:20–25`, `NamedWorkspaces`: sorts existing names alphabetically using current-culture case-insensitive order.
- `src/FileCat.App/ViewModels/MainViewModel.Workspaces.cs:30–45`, `SaveNamedWorkspaceAsync`: prepopulates the first existing name, validates only nonblank input, then immediately saves to that name. No existence/replace confirmation is performed.
- `src/FileCat.App/ViewModels/MainViewModel.Workspaces.cs:14–18`, `WorkspacePath`/`SanitizeName`: replaces invalid filename characters with underscores. Distinct typed names can therefore map to the same backing filename.
- `src/FileCat.Core/State/JsonFileStore.cs:116–176`: replacing an existing file is supported by design, including a `.bak` of the previous version.

#### Scenario and impact

Save a carefully arranged workspace named “Accounting”. Later arrange another project and invoke “Save workspace as…”. The prompt defaults to “Accounting”, not an unused name or explicitly selected current workspace identity. Pressing Enter replaces it, despite “as” suggesting a new saved arrangement. Even a deliberately changed name can collide: “Client/A” and “Client_A” sanitize to the same path on platforms where slash is invalid. Windows has additional invalid-character collisions and case-insensitive filename identity.

The user loses a named arrangement, not document data. A one-generation `.bak` mitigates recovery but is not a clear user-facing version history, is overwritten on subsequent saves, and does not make an unannounced replacement appropriate.

#### Recommended implementation

Distinguish **Save changes to [current workspace]** from **Save workspace as…**. Track the current named workspace explicitly if offering in-place Save. For Save as, suggest an unused name and show when a submitted name already exists. Require a concrete “Replace ‘Accounting’” choice, ideally displaying saved tab/panel count and current replacement count.

Validate names before saving instead of silently converting them into an ambiguous storage identity. Either reject unsupported characters with an inline explanation or decouple display name from a stable internal ID. If retaining sanitized filenames, compare canonical backing paths and show collisions explicitly, including case-only names on relevant filesystems. Recheck the target immediately before publication so a concurrent save is not silently replaced. Preserve atomic save, backup and state-size safeguards.

#### Acceptance criteria

- Opening Save as and pressing Enter cannot silently replace the alphabetically first saved workspace.
- Existing-name replacement requires an explicit choice naming the workspace being replaced.
- Two accepted distinct display names cannot silently resolve to the same saved record.
- Canceling an overwrite decision leaves both main and backup files unchanged.
- Successful saves still use atomic publication and retain bounded backup behavior; failure preserves the form/name for correction.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-025"></a>

### FCUX-025 — Opening a workspace promises recovery of previous tabs but cannot restore the previous arrangement

**Severity:** S2 — Medium · **Area:** Workspace restoration · **Effort:** M

**Confidence:** Source-confirmed bounded partial recovery.

#### Evidence

- `src/FileCat.App/ViewModels/MainViewModel.Workspaces.cs:82–86`, `OpenNamedWorkspaceAsync`: puts each current tab into RecentlyClosed, loads the new workspace, then says the previous tabs can be reopened with Ctrl+Shift+T.
- `src/FileCat.App/ViewModels/WorkspaceViewModel.cs:254–259`, `RememberClosed`: the list is bounded by `Math.Max(5, Services.Settings.RecentlyClosedTabs)`.
- `src/FileCat.Core/State/StateModels.cs:117`: default capacity is 25. It is configurable; 25 is not an unconditional hard limit.
- `src/FileCat.App/ViewModels/WorkspaceViewModel.cs:262–268`, `ReopenClosed`: reopens a single saved tab in whichever panel is currently active. It does not retain original panel grouping, split tree, active panel or target assignments.
- `src/FileCat.App/ViewModels/WorkspaceViewModel.cs:299–305`, `LoadState`: disposes old tabs and clears all old panels.

#### Scenario and impact

Work in four panels with different copy targets and 40 tabs, then open a saved workspace. At default settings, only 25 previous tab entries fit in RecentlyClosed. Repeated Ctrl+Shift+T puts retained tabs into the current active panel and cannot reconstruct the original layout or target relationships. Even fewer than 25 tabs lose their spatial grouping. The reassurance suggests the switch is easily reversible, while recovery is partial and laborious.

**Caveat:** tab persistence intentionally excludes temporary result sets and raw recovery-device locations (`src/FileCat.App/ViewModels/TabViewModel.cs:888–906`). Any new restore feature must explain unsupported/transient items and preserve their safe lifecycle.

#### Recommended implementation

Before replacing a workspace, capture one bounded **previous workspace** snapshot with the complete supported panel/tab/tree/target/active-state structure. Keep this separate from ordinary RecentlyClosed, so one switch does not evict useful recent tabs. Expose “Restore previous workspace” in the completion notification and workspace menu. One last-switch snapshot is enough to address this finding; an unlimited workspace history is unnecessary.

Capture and validate the snapshot before disposing the old arrangement. If some transient views cannot be restored, disclose that limitation before replacement or preserve an appropriate in-session reference with correct ownership. Until full restoration exists, change the notification to accurately state how many tabs were retained and that their panel arrangement was not saved.

#### Acceptance criteria

- A 40-tab/four-panel fixture can switch away and restore all supported tabs in their original order, panels and split proportions.
- Active panel/tab, locked roots and target assignments return correctly.
- The switch-restore record does not depend on RecentlyClosed's default 25-item capacity.
- Transient/unrestorable views are disclosed and never restarted in a way that bypasses an approval.
- Repeated switches keep memory/storage bounded and dispose retired tab resources correctly.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-026"></a>

### FCUX-026 — Listing columns can extend beyond the reachable viewport

**Severity:** S2 — Medium · **Area:** Listing geometry · **Effort:** L

**Confidence:** source-confirmed. **Present behavior:** columns can extend beyond the pane; the renderer clips them; the user has no horizontal scrollbar/scroll state/overflow affordance to reach them.

#### Evidence and sizing

- `src/FileCat.App/Controls/FileListControl.cs:43,123–145`: only `_vbar` exists and is vertical.
- `src/FileCat.App/Controls/FileListControl.cs:662–697`, `ArrangeOverride`/`LayoutColumns`: fixed widths retain their scaled width; fill columns receive `Math.Max(80, totalWidth - fixedSum)` even when fixed columns already consume the available width.
- `src/FileCat.App/Controls/FileListControl.cs:718–744`: row rendering is clipped to the available content width.
- `src/FileCat.App/Controls/FileListControl.cs:1176–1185`: wheel handling changes only `_topRow`.
- `src/FileCat.App/Controls/Columns.cs:106–115`: Windows Full has fixed widths 56 + 96 + 128 + 128 + 110 + 70 + 50 = **638 DIPs** at reference scale, plus at least **80 DIPs** for Name. Thus its minimum occupied width is **718 DIPs before a scrollbar**. The non-Windows Full profile is wider still (756 + 80 = 836 DIPs at reference scale).
- `src/FileCat.App/Controls/FileListControl.cs:423–445`: larger fonts and wider date formats increase column widths, which is good for readable values but worsens overflow without an overflow strategy.
- `src/FileCat.App/Views/MainWindow.axaml:9`: the default window is 1280 DIPs wide, so two equal panes are considerably narrower than 718 DIPs after borders/gaps.

#### Scenario and recommendation

Switch a normal dual-pane window to Full. Rightmost Version/Streams/Attr or other metadata may lie outside the panel and cannot be inspected by horizontal scrolling. On a larger font, even Details can overflow. Changing profile or maximizing a pane is a workaround, but requiring a layout change merely to reach selected columns undermines a configurable multi-pane manager.

Implement a horizontal viewport with an explicit scroll extent, synchronized header/row hit testing and an appropriate scrollbar/keyboard route; a pinned Name column is a reasonable design option. Alternatively design responsive column prioritization with an explicit overflow/details surface, but do not silently hide selected columns. Keep at least a useful filename width; 80 DIPs includes the icon/gutter and can leave very little actual name text. Do not auto-resize all columns on every file focus. Preserve user widths and existing font/date scaling.

#### Acceptance criteria

1. Every configured column can be reached at 320, 480, 640 and 800-DIP pane widths in supported profiles.
2. Verify reference and enlarged font sizes, long dates, long metadata headers, vertical scrollbar visible/hidden, and multiple fill columns.
3. Header sorting and resizing hit the displayed column after horizontal scrolling; the rename editor remains aligned to Name.
4. Switching tabs/profiles and restoring saved state does not strand the viewport beyond the extent or lose stored widths.
5. Mouse, touchpad and keyboard can reach all columns and return to Name; assistive technology receives matching scroll/visibility state.

**Existing coverage:** `tests/FileCat.App.Tests/ColumnProfileTests.cs:67–101` tests width persistence in an 800-DIP standalone list, and `115–148` tests date scaling in a 1400-DIP standalone list. Neither establishes that all columns remain reachable inside a normal pane.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-027"></a>

### FCUX-027 — An explicit sort can leave the retained focused item offscreen

**Severity:** S2 — Medium · **Area:** Focus continuity · **Effort:** M

**Confidence:** source-confirmed. **Existing behavior to preserve:** background refresh deliberately respects a user's manually scrolled viewport.

#### Evidence and scenario

- `src/FileCat.Core/Listing/ListingModel.cs:624–704`, `ApplyResult`: a sort replaces visible order but keeps `_focusStore` when it is still present. If its identity is unchanged, the change can remain `Rows` rather than `Focus`.
- `src/FileCat.App/Controls/FileListControl.cs:544–552`, `OnListingChanged`: only `Focus|Reset` calls `EnsureFocusVisible`; ordinary `Rows` does not.
- `src/FileCat.App/Controls/FileListControl.cs:615–624`: the method to bring the focused item into view exists.
- `src/FileCat.App/Controls/FileListControl.cs:1045–1050`: clicking a column requests a sort.
- `src/FileCat.App/ViewModels/TabViewModel.cs:531–535`: `SortBy` changes `Listing.Sort` only.
- `tests/FileCat.App.Tests/FileListSmokeTests.cs:39–110`: explicitly verifies that refresh does not pull a manually scrolled viewport back to its focused item.

In a long directory, focus a file visible near the top. Sort by size/date so the same file moves to a distant index. The focus store remains valid, but no visible-row focus outline need remain in view. The status continues describing that file, and the next arrow key or file action operates relative to it. The user loses visual continuity precisely after a deliberate navigation action.

#### Recommendation

Carry an explicit view-change reason or a `RevealFocusedItemAfterSort` intent through the asynchronous sort. When the current user's sort completes, reveal the same focused item (or an explicitly chosen replacement if it disappeared). Preserve the current background-refresh scroll rule. Do not simply call `EnsureFocusVisible` for every `Rows` notification; that would reintroduce a tested regression while streaming, refreshing or calculating metadata.

#### Acceptance criteria

1. With no newer viewport, focus, tab or navigation intent, a sort that moves the focused file from the first page to a later page leaves that same file visible and focused after the sort is applied. A newer manual scroll suppresses the stale reveal while preserving valid focused-item identity.
2. Ascending/descending toggles and metadata sorts behave consistently.
3. If a sort finishes after the user has moved focus or switched tabs, its stale reveal request cannot override the new intent.
4. Existing manual-scroll refresh preservation test still passes.
5. Pointer range anchors follow [FCUX-002](#fcux-002)'s identity rules after the sort.

#### Guard against newer viewport intent

The proposed asynchronous sort reveal must also respect a manual scroll made **after** the sort request, not only a later focus or tab change. Otherwise a delayed sort can pull the viewport away from content the user deliberately began inspecting while it ran.

Capture or otherwise distinguish the viewport intent that authorized the reveal. If the user scrolls, changes focus, navigates or switches tabs before the sort applies, suppress a stale reveal according to the documented policy. Do not equate every programmatic scrollbar update with a new user intent.

Add a regression sequence that holds sort completion, manually scrolls elsewhere, and then completes the sort. Preserve the newer viewport while retaining valid focused-item identity; the existing no-newer-input sort case should still reveal focus. This complements the current background-refresh preservation rule.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-028"></a>

### FCUX-028 — Invalid filters lose the editable draft and share unrelated error state

**Severity:** S2 — Medium · **Area:** Filter validation · **Effort:** M

**Confidence:** source-confirmed. **Affected workflow:** applying a complex mask directly in the address-row filter field.

#### Evidence

- `src/FileCat.App/Views/PanelView.axaml.cs:69–75`: filter loses focus → `ShowFilter()`.
- `src/FileCat.App/Views/PanelView.axaml.cs:417–422`, `ShowFilter`: overwrites field text with the last applied mask or all-items mask once it is no longer focused.
- `src/FileCat.App/Views/PanelView.axaml.cs:425–437`, `OnFilterKeyDown`: Enter calls `SetFilter(FilterBox.Text)` and **unconditionally** focuses the file list.
- `src/FileCat.App/ViewModels/TabViewModel.cs:537–555`, `SetFilter`: parse failure puts an error in the generic `Banner` and returns without applying the filter. Success for a nonempty restrictive filter applies the mask but does not clear a preceding parse-error `Banner`; only the all-items branch explicitly clears it.
- `src/FileCat.Core/Selection/Mask.cs:184–185`: unclosed quote or regular expression is an explicit parse error; `@missing` can also produce a missing-saved-filter error at `197–204`.
- `src/FileCat.App/ViewModels/TabViewModel.cs:458–466`, `LocationBanner`: the same `Banner` contains the working-set safety explanation that F8 deletes originals, whereas removing a reference does not. `183` also assigns Registry-notification failure to `Banner`, `570` assigns the latest listing issue, and `783` assigns a partial-metadata-sort explanation.
- `tests/FileCat.App.Tests/PathLineTests.cs:104–141`: verifies valid `*.txt` and `*.*`, not the validation correction loop.

#### Scenario

Enter a long mask with a missing closing quote, such as `"quarterly report*`, and press Enter. An error banner appears, but focus leaves the field and the draft is replaced by the last valid filter. The user must re-enter the mask rather than repair one character. After retyping a valid restrictive mask, the new filter applies but the old error banner can still say the filter is invalid.

There is also a cross-feature consequence: in a working set, applying `*`/`*.*` clears the shared `Banner`, removing the explanation that F8 deletes the originals even though the location has not changed. An invalid mask can overwrite that explanation with a parse error. The same shared slot can replace Registry-monitoring or incomplete-listing warnings. Operation confirmations are separate safeguards, so this is not evidence of unconfirmed deletion; it is loss of relevant persistent context from an unrelated filter action.

#### Recommendation and touchpoints

Separate draft text, committed filter and validation error. Make `SetFilter` return a validation/apply result, or use an explicit validation model. On parse failure keep keyboard focus, preserve all text/caret and show a field-associated message; do not change the applied result set. On success update the committed filter and clear **only the filter-owned validation message**. Avoid clearing unrelated provider/listing errors with a generic `Banner = null` side effect. Escape restores the previous applied mask; clicking elsewhere should follow a stated draft-discard policy without masquerading as a successful apply.

#### Acceptance criteria

1. Invalid quote, regex and missing saved-filter reference leave draft text and focus in the field; applied file results remain unchanged.
2. One-character correction plus Enter applies successfully and removes the validation message.
3. Escape reverts the draft without changing the last applied filter.
4. Error text is linked to the field and announced accessibly.
5. Applying a valid filter does not clear an unrelated I/O failure, and a repaired filter does not leave a stale parse error.
6. Existing all-items (`*`, `*.*`) and folder-preserving semantics still pass their tests.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-029"></a>

### FCUX-029 — Notifications can inherit the previous severity and lack a persistent review route

**Severity:** S2 — Medium · **Area:** Notifications · **Effort:** M

**Confidence:** source-confirmed timer/data-flow issue and absence of deliberate notification semantics; exact screen-reader announcement behavior is untested.

#### Evidence

- `src/FileCat.App/Views/MainWindow.axaml:87–92`: notification is a `Border` with a `TextBlock`, with no `LiveSetting`, status peer, explicit dismissal control, or selectable message surface.
- `src/FileCat.App/Views/MainWindow.axaml.cs:263–271`: reacts to `Notification` property changes and sets a 12-second error / 6-second normal timer; clicking anywhere clears the notification.
- `src/FileCat.App/ViewModels/MainViewModel.cs:159–173`: `Notify` assigns **`Notification` first, `NotificationIsError` second**; errors are logged. `ClearNotification` only clears the current message.
- `src/FileCat.App/Views/MainWindow.axaml.cs:485–494`: executing a mapped command clears the previous notification.
- `src/FileCat.App/Views/MainWindow.axaml.cs:557–564`: Escape clears a current notification, so dismissal is not exclusively mouse-only.
- `src/FileCat.App/App.axaml.cs:70–71,121`: this mechanism also carries administrator/portable-state warnings and unhandled-error feedback, illustrating that the channel includes more than disposable success acknowledgments.
- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:605–609`: operation failure messages can point to Ctrl+J, which is a valuable existing route for operation details.

#### Concrete defect

Property-change notifications from the generated observable property setter are synchronous. `Notify("failure", true)` first notifies `Notification`; the window handler reads the **previous** `NotificationIsError`. The first error or an error following an info message gets the normal six-second duration. A normal message following an error gets twelve seconds. Repeating an identical message can also skip the `Notification` change event, so the lifetime is not necessarily renewed even if severity changes.

#### UX problem and recommendation

A short-lived, click-anywhere banner can disappear while a user is still finding the relevant panel or reading a long explanation. There is no deliberate live-region announcement here; an accessibility name being nonempty is not equivalent to signaling a new status message. Diagnostics logs retain errors but are not a convenient user-facing notification history.

Represent a notification as one coherent value carrying text, severity, identity/time and optional action. Apply severity before triggering the render/lifetime update, or pass the object directly so timing cannot depend on previous state. Use an explicit close button, preserve Escape behavior, announce status politely and truly urgent errors appropriately, and pause dismissal while the user focuses/hovers a notification or is selecting/copying its content. Keep actionable failures available until dismissed or provide a keyboard-accessible recent-message history. Success acknowledgments can still auto-dismiss. Retain Ctrl+J links when the relevant details are operations; do not send every type of error to an unrelated operations pane.

#### Acceptance criteria

1. Info → error, error → info, repeated identical error and successive errors each use their own intended lifetime.
2. A new meaningful notification or state change is announced once after the chosen coalescing policy, without stealing focus. Equivalent repeated failures retain reviewable occurrence counts without requiring repeated speech for every notification identity.
3. Keyboard users can locate, copy, dismiss and activate an associated action, if present.
4. Important explanatory text remains accessible after an unrelated command or automatic timeout through a suitable persistent surface/history.
5. Clicking text for inspection does not silently destroy the only user-facing copy of it.
6. Existing Escape priority is explicit: dismissing a message should not simultaneously cancel an operation or clear a filter.

**Related guidance:** [Avalonia accessibility](https://docs.avaloniaui.net/docs/app-development/accessibility) documents live setting and custom peer mechanisms. Treat exact timing/persistence choices above as this audit's design recommendation.

#### Separate retained history from announcement volume

A coherent notification identity should not force repeated speech for every identical background failure. Deduplicate semantically equivalent messages over an appropriate interval while preserving occurrence counts and reviewable history; announce meaningful changes, actionable failures and updated consequences.

Keep explicit close/copy/action controls keyboard-accessible without moving focus automatically or inserting every arriving message into the user's mandatory tab sequence. Preserve Escape priority and current operation ownership. Test repeated failures, error-to-info transitions, focus on a notification action, and continued file navigation: messages remain inspectable, yet speech and focus are not repeatedly interrupted by equivalent updates.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-030"></a>

### FCUX-030 — Panel layout uses an estimated workspace budget and lets headers crowd the location

**Severity:** S2 — Medium · **Area:** Panel layout · **Effort:** M

**Confidence:** source-confirmed layout decisions; the precise point at which controls disappear depends on measured text, theme, and scaling.

#### Evidence and present behavior

- `src/FileCat.Core/State/PanelLayout.cs:54–58, 300–315`, `PanelLayout.MinimumSize/Fits`: every panel has a fixed minimum of 260 × 140 logical units, with 5-unit splitters. Nested minima add according to split direction.
- `src/FileCat.App/Views/MainWindow.Layout.cs:92–133`, `BuildNode`: recursive grid definitions enforce those minimum widths/heights.
- `src/FileCat.App/ViewModels/MainViewModel.Layout.cs:82–95`, `RoomForPanel`: adding a panel checks `top.Bounds.Width - 8` and `top.Bounds.Height - 130` rather than the measured workspace host.
- `src/FileCat.App/Views/MainWindow.Layout.cs:75–88`, `UpdateLayoutStrip`: the subsequent cramped-layout warning uses the actual `WorkspaceHost.Bounds`, so admission and warning use different budgets.
- `src/FileCat.App/Views/MainWindow.axaml:9–30, 56–68`: the window can shrink to 640 × 420. The menu, wrapping toolbar, operations pane, optional command line, and key bar consume variable vertical space.
- `src/FileCat.App/Views/PanelView.axaml:45–71`: the address row allocates `Auto,Auto,*,Auto,Auto,Auto`. Only the path receives the remainder; free-space text, a fixed 96-unit filter, and a potentially verbose target-role button compete around it.

#### User scenario and impact

A user keeps several stacked panels open, expands operations, and narrows the window until the toolbar wraps. “Add panel below” can judge the layout using space that the actual workspace no longer has. Conversely, hiding chrome can leave more usable space than the fixed subtraction allows. The minimum-size tree is internally consistent but does not establish that each panel's own chrome and list remain usable.

At narrow widths the address row favors surrounding controls over the user's location. A minimum-width panel can devote most of its address row to free-space, filter, and role text. This increases the effort required to identify the source or target while operating files. Treat the exact collapse threshold as a runtime verification item, not an asserted screenshot defect.

#### Recommended behavior and implementation

Use the same measured available rectangle for panel admission, drag previews, restore validation, and cramped-layout detection. Expose workspace bounds through the existing view abstraction or a layout service, without coupling core tree mathematics to a particular window. Handle the unmeasured startup state explicitly and re-evaluate after layout settles.

Define a compact hierarchy for each panel. Keep location identity, target state, and a usable list viewport available. At agreed thresholds move optional free-space information and expanded filter entry into a secondary row or an accessible overflow/popover; retain a clear active-filter indicator. Role wording may compact while its full meaning remains available by keyboard and accessibility APIs. Derive meaningful minimum sizes from this compact arrangement. Preserve the existing F11 maximize/restore escape route and its explanatory strip.

#### Acceptance criteria

1. Add-panel availability and the resulting cramped warning agree for the same measured host, including wrapped toolbar and expanded operations.
2. At the supported window minimum, at 100%, 150%, and 200% display scaling, every supported panel arrangement either preserves the defined minimum content or offers an explicit reversible recovery state.
3. At minimum panel width, the current location remains identifiable and editable; target state and active filtering remain discoverable without overlapping hit targets.
4. Hiding chrome releases space immediately; adding chrome never silently displaces essential controls beyond reach.
5. Extend `tests/FileCat.Core.Tests/PanelLayoutTests.cs:78–93` with host-aware UI cases. Existing tests validate tree arithmetic, which should remain intact; they do not prove control fit.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-031"></a>

### FCUX-031 — Panel proportions have no equivalent keyboard adjustment

**Severity:** S2 — Medium · **Area:** Keyboard resizing · **Effort:** M

**Confidence:** source-confirmed.

#### Evidence and present behavior

`src/FileCat.App/Views/MainWindow.Layout.cs:101–108` creates each `GridSplitter` with `Focusable = false`. Its pointer `DragCompleted` handler records proportions through `KeepSizes` at lines 139–146. `src/FileCat.Core/Commands/CommandRegistry.cs:336–349` provides move, rotate, swap, maximize, and equalize actions, but no incremental or absolute size command. `src/FileCat.App/ViewModels/MainViewModel.Layout.cs:28–58` implements those actions without a resize alternative.

Keyboard layout support is substantial: Alt+Shift+arrow movement, rotation, and equalization are exercised in `tests/FileCat.App.Tests/DockingTests.cs:25–64`. The remaining gap is specifically the ability to choose unequal proportions.

#### User scenario and impact

A keyboard-only user needs a wide source panel for long filenames and a narrow destination panel for a few short folders. Equalizing gives both the same width; maximizing temporarily removes the comparison. Pointer users can make the desired split directly, while keyboard users cannot reach the resizing control or issue an equivalent command. This also limits users who find precision dragging difficult.

#### Recommended behavior and implementation

Provide an explicit “Resize panel” interaction discoverable in the command palette and menus. It can focus the adjacent splitter, enter a temporary resize mode, or open a numeric size/proportion control. In splitter mode, arrows should change the relevant boundary, a documented modifier should provide a larger step, Enter should commit, and Escape should restore the previous proportions. Expose the splitter's orientation, adjacent panel identities, and current value to assistive technology.

Do not repurpose ordinary arrows while the file list owns focus. Keep existing Commander navigation and layout shortcuts. Support nested splits: the user must know which boundary is being changed, and a minimum-size constraint should yield understandable feedback. Keyboard and pointer adjustments must update the same saved layout model; a visual resize that disappears on workspace reload is incomplete.

#### Acceptance criteria

1. Without a pointer, change a two-panel layout from equal widths to an unequal proportion, then restore it.
2. Resize both a horizontal and vertical boundary in a nested layout, with a visible focus indicator and understandable accessible name/value.
3. Bounds respect the same minimum policy as pointer dragging. Escape restores the initial proportions only while the same layout interaction remains current; changed or replaced layouts invalidate obsolete resize state.
4. Reloading a saved workspace preserves the committed keyboard adjustment.
5. Ordinary file-list arrows, Tab panel switching, Alt+Shift+arrow movement, and F11 retain their existing behavior outside the explicit resize interaction.

#### Bound the resize mode to its active context

Enter proportional resizing through an explicit command or focused splitter interaction and provide an equally clear exit. Keep ordinary file-list arrows, Commander Tab panel switching and existing move/rotate shortcuts unchanged outside this mode.

Restore canceled proportions only for the same live layout interaction. If panels are closed/moved or a workspace is replaced while resizing, discard obsolete resize state rather than applying old weights to a different tree. Keyboard and pointer commits must update the same saved proportions.

Test entering/leaving a nested splitter, canceling, committing, changing the layout during the interaction and restoring the workspace. Announce the boundary/orientation/value while resizing without making every splitter another unexpected stop in routine file-list navigation.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-032"></a>

### FCUX-032 — Breadcrumb actions lack a complete keyboard and accessibility route

**Severity:** S2 — Medium · **Area:** Breadcrumb accessibility · **Effort:** M

**Confidence:** source-confirmed. **Mitigation already present:** the named `PathBox` supports Ctrl+L/editing, so users are not completely prevented from entering or copying a full location.

#### Evidence

- `src/FileCat.App/Controls/PathLine.cs:19–38`: custom-drawn `Control`; constructor does not set focusable and defines no standard navigation children.
- `src/FileCat.App/Controls/PathLine.cs:239–276`: navigate/copy/new-tab/edit/menu actions are implemented solely through pointer presses/releases and modifiers.
- No keyboard handler, `OnCreateAutomationPeer`, segment peers or command-binding equivalents exist in `src/FileCat.App/Controls/PathLine.cs`.
- `src/FileCat.App/Views/PanelView.axaml:55–59`: the overlaid custom `PathLine` has only static accessible name `Path`; the text editor underneath has the useful accessible name `Location`.
- `src/FileCat.App/Views/PanelView.axaml.cs:493–522`: segment-specific copy/go/new-tab menu is only opened from `PathLinks.MenuRequested`.
- `tests/FileCat.App.Tests/PathLineTests.cs:19–102`: exercises pointer segment navigation/copy/new tab, not keyboard/automation segment access.

#### Scenario and recommendation

A pointer user can jump directly from a deep directory to a named ancestor, copy exactly that ancestor path, or open it in a new tab. A keyboard/screen-reader user must enter editing and manually manipulate the text or repeatedly navigate up. This is a disparity in a visible navigation control, not a missing generic feature request.

Provide a compact keyboard mode for the breadcrumb (for example an explicit focusable breadcrumb group with arrow navigation among ancestor segments, Enter to navigate and an accessible context menu), while preserving Ctrl+L as direct edit. Expose segment labels, full target paths and actions as proper peers or standard buttons. Avoid making every ancestor an extra stop in the ordinary Commander Tab cycle; use one group stop and intra-group navigation, or a keyboard-accessible ancestor menu. Do not show an actionable `Path` name if automation cannot actually invoke its offered actions.

#### Acceptance criteria

1. From a focused panel, reach the breadcrumb/ancestor menu through an explicit documented keyboard route.
2. Choose an ancestor, open it here/new tab and copy its path without pointer use or manually retyping the path.
3. Assistive technology identifies the current location and ancestors by meaningful name and target, not just `Path`.
4. Canceling the breadcrumb interaction restores the previous list focus only while its owning tab/location is still current and the item remains valid; otherwise use the appropriate current fallback. Successful ancestor navigation focuses the new listing state.
5. Archives, Registry and server paths use provider parents exactly as pointer navigation already does.
6. The existing editable PathBox and completion behavior remain available.

#### Preserve the existing navigation and focus model

Reach breadcrumb actions through an explicit documented group/menu route. Do not insert one ordinary Tab stop per ancestor into the Commander panel-switching cycle. Provide a visible intra-group focus indicator and an explicit route back to the listing.

On cancellation, restore the previous list item only while its owning tab/location is still current and the item remains valid. After successful ancestor navigation, focus the appropriate current listing state; do not restore a stale row coordinate from the old directory. Test cancellation and successful navigation separately, alongside direct Ctrl+L editing and native assistive-technology traversal.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-033"></a>

### FCUX-033 — Oversized breadcrumb roots and leaves can be clipped beyond inspection

**Severity:** S2 — Medium · **Area:** Path presentation · **Effort:** M

**Confidence:** source-confirmed layout branch; exact pixel appearance needs a native screenshot or headless rendering check.

#### Evidence and scenario

- `src/FileCat.App/Controls/PathLine.cs:104–105`: creates a `TextLayout` with infinite maximum width.
- `src/FileCat.App/Controls/PathLine.cs:111–129`, `Arrange`: only shortens if there are at least three segments; repeatedly removes intermediate parents while retaining root and all of the suffix. When root plus the final segment still cannot fit, it keeps the oversized string.
- `src/FileCat.App/Controls/PathLine.cs:34–37,132–138`: `ClipToBounds = true`; text is drawn from the left and clips at the control boundary.
- `src/FileCat.App/Controls/PathLine.cs:224–226`: hovering the final segment shows only generic edit/copy instructions, not the complete current path.
- `src/FileCat.App/Views/PanelView.axaml:45–71`: a single remaining-star path area shares the row with fixed navigation, free-space, filter and role controls, making long leaf names an ordinary case in narrow panes.

Two locations with final segments sharing a long prefix can look identical because their distinguishing suffixes lie outside the path area. A root + leaf path with only two segments never attempts any shortening. Existing middle-ancestor ellipsis is helpful but does not solve an oversized leaf/UNC root.

#### Recommended behavior

After ancestor elision, apply a bounded final fallback that visibly indicates truncation and prioritizes the current folder identity. Preserve a recognizable root/server cue and meaningful leaf tail according to available width, but make the complete safe-display path available immediately on hover and via keyboard/accessibility. Keep hit-testing aligned with what is displayed: an ellipsis must not secretly navigate to a hidden segment, and any visible segment's target must remain correct. Avoid truncating through escape sequences or Unicode grapheme clusters.

#### Acceptance criteria

1. Paths with one, two and many segments remain intentionally elided at 120, 240 and 480-DIP path widths.
2. Test a long leaf, long UNC/server component, Unicode/emoji, escaped bidi/control characters and provider paths.
3. Distinct leaf suffixes can be inspected without navigating or resizing the pane.
4. Full-path tooltip/accessibility value exposes the real safe-display path; copy still yields exact raw path when requested.
5. Hit-testing and existing pointer/keyboard ancestor actions target the segment shown.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-034"></a>

### FCUX-034 — Settings closes and reopens after validation errors instead of preserving field context

**Severity:** S2 — Medium · **Area:** Settings validation · **Effort:** M

**Evidence: source-confirmed close/validate/reopen flow; precise focus/flicker needs native verification.** `src/FileCat.App/Views/SettingsDialog.cs:150–187` places a generic error below the tab control, awaits `ShowCustomAsync`, and only then parses keyboard bindings, saved filters, associations and user commands. On error it sets a tab index and continues the loop. `src/FileCat.App/Views/OverlayDialogService.cs:303–308,323,338–360` closes the current session before returning its result, and a newly opened session defaults focus to the default button when no initial focus is supplied.

**Current behavior and impact.** Enter malformed configuration in a multiline Settings field and choose OK. The modal session closes, validation fails, and the same body is reopened on a selected tab with a generic error below all pages. Existing control values are retained, so this is not a claim that all Settings edits are lost. The correction flow still separates feedback from the offending field, resets the modal/focus context and makes a one-character repair require finding the correct position again. Repeated failures can produce repeated close/reopen transitions. Hardcoded tab indices make validation routing fragile as pages change.

**Desired behavior.** Validate before accepting the modal result and keep the current session open. Identify the actual item and field, scroll it into view, retain draft/caret and move focus deliberately to the offending value. Show a concise inline error and, when useful, a summary that links to invalid fields. A valid correction clears only the corresponding error. Cancel must retain the existing theme-preview rollback.

**Implementation touchpoints.** Settings needs an accepting command or validation callback that can return a structured failure without closing. The shared `ShowCustomAsync` already accepts a cheap availability predicate, so retain that feature; do not perform filesystem or expensive parsing inside a repeatedly queried enablement predicate. A dedicated validate-and-accept command can perform the work and leave the modal open. Route to stable page/item identifiers rather than numeric tab positions. Shared size/scrolling constraints are covered by [FCUX-011](#fcux-011) and should be fixed there, rather than duplicated as a separate Settings-only dialog frame.

**Acceptance criteria.** Invalid bindings, filters, associations and user-command quoting leave the same Settings session open with all valid edits retained. The field and exact row are identified, visible and focused; one-character correction can be submitted directly. Repeated correction attempts create no extra overlay layers or unintended theme save. Cancel restores the pre-dialog theme. At the supported viewport, the error and accepting/cancel controls stay reachable through the shared layout contract.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-035"></a>

### FCUX-035 — Read-only Settings mode says the wrong thing and still accepts changes that will not persist

**Severity:** S2 — Medium · **Area:** Settings persistence · **Effort:** M

**Evidence: source-confirmed.** `src/FileCat.App/Views/SettingsDialog.cs:151-152` always says “These settings were written by a newer FileCat and are read-only in this version” when SettingsReadOnly is true. `src/FileCat.App/Services/AppServices.cs:222` includes NewerSchemaReadOnly, OversizedReadOnly and UnavailableReadOnly. The same dialog still offers editable controls and OK; `src/FileCat.App/Views/SettingsDialog.cs:190-227` mutates runtime settings, while `AppServices.SaveSettings:297-304` silently skips saving in read-only mode. `src/FileCat.App/ViewModels/MainViewModel.Settings.cs:14-40` applies the mutations before attempting save.

**Impact.** The user changes a preference and sees it take effect, but after restart it disappears. The message may falsely attribute an access/size problem to a newer version. The interaction does not explain whether “read-only” means fields cannot change, cannot be saved, or apply only for this session.

**Desired design.** Display the actual load/persistence reason and make the chosen behavior explicit. Either truly disable editing with a Close-only action, or intentionally allow session-only changes with a clearly labeled “Apply for this session” action and persistent notice. Offer a safe, separately explicit route to export the draft or choose a writable profile if supported; do not overwrite unknown newer-schema settings.

**Implementation touchpoints.** Replace the boolean-only UI decision with a presentation model derived from StateLoadStatus. Preserve existing file-protection gates. Let the dialog receive the save result and show whether changes were persisted, temporary or failed. Do not show routine success feedback on a skipped save.

**Acceptance criteria.** Each of the three read-only states produces accurate reason text. A user can tell before accepting whether changes survive restart. Unknown-version files remain unchanged. If temporary edits are permitted, the action says so and reopening Settings preserves the notice; normal writable Settings still applies and persists exactly once.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-036"></a>

### FCUX-036 — Advanced Settings fields force users to author multiple mini-languages without editing support

**Severity:** S2 — Medium · **Area:** Settings editing · **Effort:** L

**Evidence: source-confirmed; design-priority judgment.** `src/FileCat.App/Views/SettingsDialog.cs:56-75` stores saved filters in a multiline `name = mask` box; `82-107` stores user commands and associations in pipe-delimited text; `114-124` stores keyboard overrides as command IDs/gestures. Tools explanation at `105` is a dense paragraph covering groups, quoting, options, tokens and shell restrictions. Program path at `78` has no Browse action or invocation preview. `src/FileCat.App/Views/ColumnProfilesEditor.cs:24-99` shows that the application already has a structured list/detail editor pattern.

**Impact.** Common tasks such as adding an editor, changing an association's priority or adding a saved image filter require remembering grammar and separators. Syntax errors in unrelated multiline fields block all Settings changes. First-match association ordering is described but reordering requires cutting and pasting source lines. Empty arguments, quoting and exclusion syntax are especially error-prone; [FCUX-006](#fcux-006) is the correctness defect, whereas this finding is the ongoing interaction cost even after the parsers are fixed.

**Desired design.** Give filters, commands and associations list/detail editors with Add, Duplicate, Remove, Move up/down and per-item validation. Program Browse should return a full path; arguments should offer token insertion and a preview of the resolved argument vector. Preserve an Advanced raw-text editor/import/export for experienced users. Expose keyboard remapping directly through the existing reference/remap UI rather than requiring users to leave Settings and remember F1/F2.

**Implementation touchpoints.** Reuse ColumnProfilesEditor's draft-state transaction approach, but improve its button-state behavior (see [FCUX-063](#fcux-063)). Model these settings as typed draft objects, and serialize only on successful Apply/OK. Add filtered lookup/search across Settings labels and explanations so users do not need to inspect all tabs. Keep shell mode as a deliberate explicit option with a short contextual explanation; move low-level details into expandable help.

**Acceptance criteria.** A user can add an editor with a spaced path and arguments, create a filter with exclusions, and reorder two overlapping associations using visible controls. The editor identifies which file/action the first match will use through a non-executing preview. Errors identify an item and field. Raw/structured modes round-trip losslessly and Cancel restores the entire draft. Add and editing actions have readable names and keyboard paths.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-037"></a>

### FCUX-037 — Toolbar commands look available when the same commands are disabled in menus

**Severity:** S2 — Medium · **Area:** Command availability · **Effort:** M

**Evidence:** SC for command wiring; UX for the cost of conflicting availability cues. **Affected surfaces:** the main toolbar, command menus, function-key bar, and command palette.

#### Evidence

- `src/FileCat.App/Views/MainWindow.axaml.cs:784–806`, `BuildToolbar`, creates enabled `Button` instances from `MainMenuModel.Toolbar`, sets icons, tooltips, accessible names and checked styling, and directly calls `Execute`. It does not bind `IsEnabled` to command availability.
- In the same file, `407–421`, menu opening calls `_vm.GetAvailability(cid)`, sets `IsEnabled`, and supplies a reason when unavailable.
- `src/FileCat.App/ViewModels/MainViewModel.cs:155` uses availability when building the function-key bar.
- `src/FileCat.App/ViewModels/MainViewModel.Commands.cs:26–134,154–160`, `GetAvailability` and `Execute`, define context-sensitive capability checks and recheck them before execution. This is an existing protection: a misleading enabled toolbar button does not by itself bypass the command gate.
- `src/FileCat.App/Views/MainWindow.axaml.cs:69–79` updates checked styling for several visibility settings, but this is not a general availability subscription.

#### Scenario and impact

Open a fresh tab with no Back history, focus an empty or read-only location, or change the active panel's provider. A toolbar action can still look actionable while the corresponding menu entry is disabled. Clicking becomes a way of discovering restrictions through rejection feedback. This creates a conflicting model of the same command and makes a busy toolbar harder to understand. A button that is merely named accessibly still needs an accurate enabled state.

#### Recommended behavior and implementation

Derive label, icon, checked state, availability and unavailability reason from one presentation model for each command in its current context. Reuse the existing command registry and capability rules. Update affected presentations when active panel, location, focus/marks, clipboard capability, history or relevant preferences change. Do not execute filesystem or network probes on every pointer movement or bind a large visual tree to an expensive continuously recomputed property.

Use a cached snapshot or targeted notifications so all command surfaces agree promptly. Continue checking availability at execution because context can change after rendering. Keep unavailable controls discoverable where they explain a stable capability; provide a readable reason through a tooltip or adjacent help. If the UI framework prevents tooltips on disabled controls, expose the reason on a suitable accessible wrapper or help route. Hiding every temporarily unavailable action would destabilize the toolbar and remove useful learning cues.

#### Acceptance criteria

1. Back/Forward states reflect the active tab's actual history immediately after tab switches and navigation.
2. Copy/Move/Delete and provider-specific actions expose the same availability in toolbar, menu, function-key bar and palette for the same captured context.
3. An unavailable action has a useful reason; keyboard and assistive-technology users can obtain it without attempting a destructive command.
4. A change between presentation and activation is still rejected safely by the existing execution gate.
5. Availability updates do not introduce synchronous remote I/O or visible lag during rapid focus changes in a large listing.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-038"></a>

### FCUX-038 — Bulk rename can be blocked by an error in a row the UI cannot display

**Severity:** S2 — Medium · **Area:** Bulk rename review · **Effort:** M

**Confidence:** Source-confirmed. **Affected workflow:** large batches, accepted up to 50,000 items.

**Evidence**

- `src/FileCat.App/ViewModels/MainViewModel.BulkRename.cs:21, 33` allows 50,000 items.
- `src/FileCat.App/ViewModels/MainViewModel.BulkRename.cs:66–75`, `Refresh`: checks all rows but binds only `rows.Take(2000)` as plain strings. Summary discloses that only the first 2,000 are shown.
- `src/FileCat.App/ViewModels/MainViewModel.BulkRename.cs:130–137`: any problem in any row disables all renaming.
- `src/FileCat.Core/Operations/BulkRename.cs:75–82`: collision checks can assign a problem to a later row independently of the displayed first 2,000.

**Scenario and impact**

Select 3,000 files and apply a rule whose only external-name collision occurs at row 2,500. The dialog says one problem blocks renaming, but scrolling cannot reach it. There is no problem filter, jump-to-error or complete export. The user must abandon the dialog, reduce/reorder the selection, or move to the editor without knowing which file failed. Even error-free batches cannot be fully reviewed in the advertised preview.

**Implementation**

Bind the full bounded row collection to a virtualized item source, using a structured row with old name, new name, folder and problem. Add “Problems (N)” and “Changed (N)” filters, search, and “Next problem”. Virtualization should bound realized UI elements, rather than truncating the underlying data. If paging is chosen, show explicit ranges and allow every page. Clicking the blocking count should open the problems view.

**Acceptance criteria**

- A collision after item 2,000 is discoverable in one direct action and its complete old/new path can be inspected.
- Every accepted item can be reviewed; the displayed and submitted row universes agree.
- Large lists remain responsive; controls do not realize 50,000 visual rows at once.
- All current collision/invalid-name blocking and stale-preview checks remain active.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-039"></a>

### FCUX-039 — Bulk rename does filesystem validation on the UI thread after every edit

**Severity:** S2 — Medium · **Area:** Bulk rename responsiveness · **Effort:** M

**Confidence:** Source-confirmed synchronous work; degree of delay is a performance hypothesis to measure.

**Evidence**

- `src/FileCat.App/ViewModels/MainViewModel.BulkRename.cs:66–79`: the dispatcher timer calls `Refresh`, which calls `BulkRenamePlanner.Preview` synchronously with `File.Exists || Directory.Exists`.
- `src/FileCat.Core/Operations/BulkRename.cs:35–84`: iterates all items and performs an existence probe for changing names not renamed away.
- `src/FileCat.App/ViewModels/MainViewModel.BulkRename.cs:28–33`: only requires filesystem paths, so UNC/mapped-network paths are not excluded; allows up to 50,000 items.
- The 150-ms timer debounces keystrokes; it does not move work off the UI thread.

**Scenario and impact**

Rename thousands of files on a slow network share and type a prefix. The dialog may freeze while probing target names, preventing correction, cancellation and repainting. On a disconnected share the wait can be much longer than normal local use. This is particularly harmful in a preview workflow where users are expected to experiment with rules.

**Implementation**

Split pure rule expansion from filesystem admission checks. Snapshot inputs and selection, compute on a cancellable background task using the existing device-aware IO scheduler, and tag each revision. Present “Checking names…” and disable Rename only until the current generation has been validated. Discard results from older revisions. Cancel queued work when fields change or the dialog closes, retaining safe ownership until active native IO returns. Do not remove the final execution-time safety checks.

**Acceptance criteria**

- UI input, Cancel and window movement stay responsive during a deliberately delayed target-existence check.
- Rapid rule changes cannot display or run an older generation's preview.
- Closing the dialog prevents late UI updates and releases work after active IO has safely returned.
- Rename is never enabled while the current rule/scope generation is unvalidated.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-040"></a>

### FCUX-040 — Invalid bulk-rename counter input silently becomes a valid default

**Severity:** S2 — Medium · **Area:** Bulk rename validation · **Effort:** S

**Confidence:** Source-confirmed.

**Evidence:** `src/FileCat.App/ViewModels/MainViewModel.BulkRename.cs:60–64`, `Rules`: invalid start/step become 1; invalid digits become 1 and out-of-range digits are silently clamped to 1–12. `src/FileCat.App/ViewModels/MainViewModel.BulkRename.cs:130–137` gates only on generated rule equality and row problems, so parser fallback remains executable.

**Scenario and impact:** A user enters `10x` or an overflowing number as the counter start. The form continues to show that text but previews/runs a sequence starting at 1. This splits what the input claims from what the operation means. The first visible rows may allow detecting it, but in a dense form the user should not have to infer that validation replaced their value.

**Implementation:** Model field validation separately from `RenameRules`; render an inline error, keep the typed value, and disable Rename until each numeric field parses into its supported range. For digits expose a bounded numeric control or explicit range. Explain whether negative/zero steps are supported and validate consistently; do not change accepted domain behavior without deciding it. Preserve the last valid preview as visibly stale while input is invalid, or show an empty unavailable state.

**Acceptance criteria:** Invalid/overflowing start, step, and digits never produce a runnable fallback plan. Empty intermediate text is tolerated while editing without misleading red errors before interaction. Valid negative/zero input behaves according to documented policy. The displayed input and submitted rule are identical in meaning.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-041"></a>

### FCUX-041 — Invalid transfer destinations close the form and discard the user's operation options

**Severity:** S2 — Medium · **Area:** Transfer validation · **Effort:** M

**Confidence:** Source-confirmed validation flow. **Affected workflow:** copy/move setup with malformed or unsupported destination.

**Evidence**

- `src/FileCat.App/Views/OperationDialogs.cs:154–184`: interpretation reports unrecognized locations but does not disable submission.
- `src/FileCat.App/Views/OperationDialogs.cs:201–249`: only empty destination is rejected inside the form; returns the result and closes otherwise.
- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:186–201`: resolves destination after the dialog has returned; on error it emits a notification and exits.
- `src/FileCat.App/ViewModels/MainViewModel.Operations.cs:270–295, 311–315`: parser/capability/new-name errors are available only in the caller's resolution path.

**Scenario and impact**

Set verification, a filter, a speed limit and conflict policy, then type a syntactically invalid path or a recognized provider that cannot receive transfers. Press Copy. The dialog disappears and the failure is shown elsewhere. Reopening F5 starts from default form values; the user must reconstruct the setup. The UX invites correcting a field but discards the field's surrounding work.

**Implementation**

Create a shared destination interpretation/validation result used by both inline presentation and submission. Keep syntax/capability errors within the open form, associated with the destination box, preserving all values and scope. Disable both Start and Queue for deterministic invalid states. For network/slow-path checks use asynchronous validation with visible status; do not introduce filesystem calls on each UI-thread keystroke. When execution-time checks fail due to a later change, provide a clear retry-with-settings route.

**Acceptance criteria**

- Invalid destination does not close the form or reset filters, verification, speed and conflict choices.
- The error identifies the destination field and focus returns there; fixing it allows resubmission.
- Start/Queue share validation rules; the inline interpretation matches the eventual destination/new-name mapping.
- Network latency never freezes typing.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-042"></a>

### FCUX-042 — The operations drawer has too little adaptive space for recovery rows, reports and controls

**Severity:** S2 — Medium · **Area:** Operations layout · **Effort:** M

**Confidence:** Source-confirmed fixed layout; exact overflow/reachability requires native layout validation.

**Evidence:** `src/FileCat.App/Views/OperationsView.axaml:52–55` fixes drawer height at 240 DIP and places every interrupted job in an Auto row above normal operations. `src/FileCat.App/Views/OperationsView.axaml:55–74` uses a non-scrolling ItemsControl for all interruptions. `src/FileCat.App/Views/OperationsView.axaml:108–120` uses one horizontal action row; `123–143` docks full route/current file/timing/count/data/speed and a 260-DIP graph above the issue list. Only the column divider at line 104 is resizable; there is no vertical drawer resize or separate full-window report.

**Scenario and impact:** Open Ctrl+J after several interrupted operations or while displaying a long network route and transfer statistics. Recovery banners consume the fixed drawer, leaving little or no space for the actual operation list/report. Narrowing the window also forces long action rows and the fixed graph against essential details. The user opens Details specifically to get context but cannot allocate it more room.

**Implementation:** Add a bounded vertical resize handle with persisted usable height; constrain the drawer relative to main client height. Give interruption items a scrollable/collapsible “Needs review (N)” section with one selected item's detail rather than growing the Auto row indefinitely. Use wrapping/adaptive actions, and hide/move the graph before compressing the actionable report. Provide a full report surface when content cannot fit. Preserve the compact strip for ordinary monitoring.

**Acceptance criteria:** At supported small size, at least one complete operation row plus report/action context remains available. With 1, 5 and 20 interruptions, every recovery action is reachable by scroll and keyboard. User can resize to read long reports without moving the horizontal splitter only. Graph and secondary controls never displace all diagnostic content.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-043"></a>

### FCUX-043 — Recovery preparation offers no visible busy state or user cancellation while reviewing

**Severity:** S2 — Medium · **Area:** Recovery preparation · **Effort:** M

**Confidence:** Source-confirmed missing per-action UI state; latency is a runtime hypothesis to measure on delayed IO.

**Evidence:** `src/FileCat.App/Views/OperationsView.axaml.cs:78–90` awaits cleanup/rerun without updating button/busy state. `src/FileCat.App/ViewModels/MainViewModel.Interrupted.cs:75–106, 146–162` may perform many sequential source/tree/content reviews before a confirmation appears. `_interruptedActions` prevents duplicate work but provides no visible feedback. `src/FileCat.App/ViewModels/MainViewModel.MutationPreparation.cs:66–70` only schedules the IO. `src/FileCat.App/ViewModels/MainViewModel.EditSessions.cs:377` makes `CheckSessionAction` cancel only when the broader edit/session service stops, not when this user action is canceled. Recovery is bounded (1,000 items/64 MiB review budgets), but slow or unavailable devices can still take time.

**Scenario and impact:** Click Run again on a network-backed interruption. Nothing visible happens while FileCat reads/reviews the journal and sources. Buttons remain clickable but repeated requests silently return false. The user cannot tell whether the command was accepted, the app is waiting on a drive, or there is nothing to do; canceling the review requires a broader lifecycle action.

**Implementation:** Expose recovery-action state on the interrupted-job row: “Reviewing sources…”, current phase/progress when meaningful, and Stop review. Disable conflicting actions for that journal and provide a tooltip/status explaining why. Use a per-review cancellation token linked with session shutdown; stopping preparation keeps files/journal unchanged. Once mutations have started, distinguish stopping safely from preflight cancellation and retain the report.

**Acceptance criteria:** A deliberately delayed review immediately shows accepted/busy state, has a functioning cancel control, and does not launch duplicate work. Canceling before approval changes neither source/target data nor journal completion state. Late callbacks do not reopen a canceled review or close its journal.

#### Separate stopping review from stopping admitted work

Use a visible state transition from Reviewing to Awaiting approval, then Applying reviewed cleanup or Submitted operation. `src/FileCat.App/ViewModels/MainViewModel.Interrupted.cs:107–122` rechecks source review around cleanup and submits a new job before closing the old journal; cleanup at `171–185` can commit some effects before later checks or cancellation. Preserve those admission boundaries.

Stop review before approval must invalidate its UI generation, cancel queued preparation, and leave file data and journal completion unchanged. After approved cleanup begins, use Stop after the current safe step and retain a report of already committed effects; do not promise rollback. Once a new job has been submitted, its cancellation belongs to the tracked job rather than an obsolete review token. Keep the old journal when submission/closure does not complete according to the current safeguards. Late source checks, alerts or callbacks must not reopen a canceled review or add newly discovered paths to an older approval.

**Additional acceptance:** cancel during initial review, while approval is open, after one cleanup effect, and immediately around new-job admission. Each case has one clear owner and truthful result; no canceled preparation closes the journal, no completed cleanup is falsely described as rolled back, and a submitted job remains independently visible and controllable.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-044"></a>

### FCUX-044 — Find falls back to a local home folder when the current provider is unsupported

**Severity:** S2 — Medium · **Area:** Find scope · **Effort:** S

**Confidence:** source-confirmed. The problem is an unexplained context change, not an automatically launched search.

#### Evidence and present behavior

`src/FileCat.App/ViewModels/MainViewModel.Tools.cs:53–78`, `FindFilesAsync`, handles Registry separately at lines 56–73. It then uses the current filesystem path or defaults to the user's local home directory at line 74. Result-set locations receive a separate `within` scope at lines 75–76. Other providers, including an SFTP directory or an archive's internal directory, reach the home-directory fallback.

`src/FileCat.App/Views/FindWindow.cs:129–149` correctly gives result-set searches a distinct title, snapshots their contents, labels their scope, and makes that scope read-only. For a normal Find, “Look in” displays the supplied root. Opening the window does not start a search; `StartSearch` at lines 602–613 validates criteria when the user acts. A deliberately configured `LoadOnOpen` preset can overwrite criteria at line 190 and should be respected as a separate, explicit preference.

The general command description, `src/FileCat.Core/Commands/CommandRegistry.cs:471`, says Find searches “in this folder and below or on any drive,” without explaining the provider limitation.

#### User scenario and impact

From an SFTP project directory, a user presses Alt+F7, types a filename, and presses Enter. Their attention remains on the name criterion because they invoked a folder-scoped command. Unless they notice the changed “Look in” value, they search their local home and can conclude that the remote file is missing. The same expectation mismatch occurs within an archive directory. It wastes time and weakens trust in search results; it does not establish remote data loss.

#### Recommended behavior and implementation

Keep the originating location in a scope model that expresses whether it is searchable by the chosen engine. When the current provider cannot be searched, explicitly explain this in the Find window and require a deliberate local-scope choice before running. An alternative is an unavailable command with a useful reason and an explicit “Search local files…” action. Do not solve the issue by merely hiding all search functionality.

If an archive can be searched through the existing filesystem/archive-members engine, label the actual scope accurately: searching the containing archive or its parent is not necessarily searching only the open internal folder. This finding does not require implementing a new recursive remote search engine. Preserve existing Registry search, result-set refinement, and intentional saved-search behavior.

#### Acceptance criteria

1. Invoke Find from local, SFTP, archive-internal, Registry, and result-set locations; each clearly identifies what will be searched before execution.
2. An unsupported scope never becomes local home without an explicit explanatory transition and deliberate local selection.
3. Registry keeps its dedicated search and result sets keep snapshot-based scope.
4. A load-on-open preset is visibly identifiable as the source of changed scope, rather than indistinguishable from automatic provider fallback.
5. Add coverage beyond the local-folder open/search case in `tests/FileCat.App.Tests/FindWindowTests.cs:50–77`.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-045"></a>

### FCUX-045 — Find hides completion qualifications and uses generic empty-result feedback

**Severity:** S2 — Medium · **Area:** Find completion · **Effort:** M

**Confidence:** source-confirmed structure; actual truncation varies with width and text measurement.

#### Evidence and present behavior

- `src/FileCat.App/Views/FindWindow.cs:91` defines a small, muted, character-ellipsized `_status` text block. Lines 284–291 place it in the horizontal space remaining beside Find, Stop, and Skip.
- `UpdateStatus`, lines 846–868, concatenates state, folder/file counts, matches, refinement counts, not-searched counts, warnings, and regex timeouts. The latter qualifications come last.
- During search, the state starts with `Searching {CurrentFolder}` at line 854. **After completion, it becomes “Finished” or “Stopped”; the long current-folder prefix is no longer present.** Narrow-window clipping of trailing qualifications can still occur, but the completed status must not be described as retaining the long path.
- Lines 867–868 expose a distinct `Log (N)` button; lines 172–177 put it in the bottom bar. Lines 888–901 list inaccessible, ignored, skipped, warning, and missing paths with reasons. Line 826 can open this log automatically when the user enables `SearchLogOnErrors`.
- `ShowSet`, lines 665–675, hides the introductory placeholder when results first appear. The generic renderer in `src/FileCat.App/Controls/FileListControl.cs:867–887` describes an empty completed listing as “This location is empty.” It has no search-specific no-match explanation.

#### User scenario and impact

A search returns no matches after skipping an unreadable directory. The user sees an empty result area and a prominent “Finished” prefix, while qualifications sit late in a crowded status sentence. `Log (N)` is a valuable mitigation, but it does not itself say whether the entries were intentional exclusions, transient warnings, or an incomplete search. Users need that distinction before treating zero matches as evidence of absence.

#### Recommended behavior and implementation

Represent search phase and completeness as structured state. Give a short completion summary a stable, non-ellipsized place: for example, “No matches,” “Finished — 3 folders not searched,” or “Stopped — partial results.” Keep the current folder as secondary, independently elidable progress text. Link issue counts directly to the existing log. Distinguish deliberate ignore rules from access failures and cancellation; not every log entry means an error.

Expose a concise accessible status update on completion or material state transitions, avoiding an announcement every 250 ms. Render an explicit zero-result state with scope, current criteria, and useful actions such as edit criteria, inspect exclusions, or retry. During an active search with zero results, communicate that searching continues. Preserve useful result rows and existing log detail.

#### Acceptance criteria

1. At the minimum supported Find width, completion, partial/stopped state, and existence of issues remain visible without reading an ellipsized suffix.
2. Cover successful zero matches, active zero matches, cancellation, access denied, deliberate ignores, and regex timeout with distinct accurate summaries.
3. A keyboard user reaches the log directly from the issue summary; an assistive-technology user receives the completion qualification without continuous progress chatter.
4. The zero-result view mentions search criteria/scope instead of claiming that the underlying location itself is empty.
5. Retain `Log (N)` and optional automatic opening. Existing `tests/FileCat.App.Tests/FindWindowTests.cs:59–61` checks result names and the “Finished” text, not the visibility or meaning of completion qualifications.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-046"></a>

### FCUX-046 — Find context shortcuts bypass the configurable command mapping

**Severity:** S2 — Medium · **Area:** Context shortcuts · **Effort:** M

**Confidence:** source-confirmed; the defaults are intentional and tested.

#### Evidence and present behavior

`src/FileCat.App/Views/FindWindow.cs:49–50` reserves Ctrl+I, Ctrl+S, Ctrl+W, Ctrl+D, and Ctrl+H. `OnPreviewKeyDown`, lines 1006–1031, handles Ctrl+I/S/W as intersect/subtract/append and Ctrl+D as Advanced before consulting the central key map. Lines 1062–1072 handle Ctrl+H in results, then resolve the remaining keys in `CommandContext.Panel`.

The Find menus and help repeat fixed gestures: lines 368–405, `ActionItem` at 445–450, the placeholder at 59–61, and the Find tooltip at 286. `CommandItem` at 454–460 deliberately suppresses an inherited gesture if Find reserves it. This is a useful safeguard; the menus are not simply advertising every conflicting panel binding.

`src/FileCat.Core/Commands/CommandRegistry.cs:4–11` has no Find context. Lines 13–15 describe stable command IDs shared across surfaces. `tests/FileCat.App.Tests/FindWindowTests.cs:86–104` explicitly verifies the legacy keep/remove/add shortcuts, so changing those defaults outright would itself be a compatibility decision.

#### User scenario and impact

A user customizes a panel command to Ctrl+S and expects the same command when operating found items. In Find, Ctrl+S starts subtraction instead, and there is no corresponding Find-specific remapping entry to resolve the conflict. Ctrl+W similarly adds new finds here while closing a tab in the main panel. These operations modify the result set; subtraction does **not** delete the original files. The harm is unexpected work, lost result context, and unreliable keyboard customization.

#### Recommended behavior and implementation

Register the Find actions with stable IDs and explicit contexts, such as Find criteria and Find results, and resolve shortcuts through the same conflict-aware keymap service. Retain the current defaults in a compatible profile unless the product deliberately changes them. Derive menus, tooltips, introductory instructions, and discoverability surfaces from the effective bindings.

Make conflicts understandable in shortcut settings: “Ctrl+S: Remove items found again in Find; [other command] in panels.” Allow the user to override or clear the contextual binding. Preserve text-editor behavior in criterion fields, path-completion keys, and modal-dialog priority. Prefer structured context resolution over growing a separate list of reserved strings.

#### Acceptance criteria

1. Every reserved Find action appears in shortcut configuration with its context, default, and current binding.
2. Remapping or unbinding Find subtraction changes actual dispatch and every gesture label consistently.
3. Conflicting panel and Find bindings resolve predictably and are explained before saving configuration.
4. Existing keep/remove/add behavior remains correct with the compatibility defaults; searches still affect result membership rather than deleting original files.
5. Tests cover focus in criteria, results, path suggestions, and an open dialog, alongside the existing refinement tests.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-047"></a>

### FCUX-047 — Custom scrolling rounds away fractional input and can stall one-row paging

**Severity:** S2 — Medium · **Area:** Scroll input · **Effort:** S

**Confidence:** source-confirmed numerical behavior; whether a particular mouse/touchpad emits the triggering event series is a runtime hypothesis requiring native hardware or synthetic input checks.

#### Evidence and scenario

`src/FileCat.App/Controls/FileListControl.cs:1176–1185`, `OnPointerWheelChanged`, calculates `_topRow -= (int)Math.Round(e.Delta.Y * 3)` independently for every event. No fractional remainder is retained. Therefore ten events of delta `0.1` produce ten rounded zeros and no scrolling, while one event of delta `1.0` moves three rows. Deltas near rounding thresholds can conversely over-scroll. The same total user input gives different motion according to how the device batches events.

This is observable on a high-resolution input stream and can make slow scrolling seem broken or sticky. A standard detented mouse that emits integer deltas may never reveal it.

#### Recommendation and acceptance criteria

Accumulate fractional row/pixel distance with correct sign and consume full movement when enough arrives; alternatively use a proper scroll-viewport contract that supports fractional offsets. Reset the accumulator at sensible discontinuities (tab change, navigation), preserve it across normal event batches, and clamp cleanly at ends. Do not assume every platform uses the same physical wheel increment. Coordinate with horizontal scrolling in [FCUX-026](#fcux-026).

1. Ten `0.1` events produce the same aggregate movement as one `1.0` event, subject only to the intentionally retained fractional remainder.
2. Positive, negative and direction-reversal sequences do not lose or invent movement.
3. Slow precision touchpad input, a conventional wheel, inertial input where available, and ends of the list behave predictably.
4. Scrolling during refresh retains the current refresh-preservation behavior and does not jump to focus.
5. Inline rename remains aligned while its row scrolls; no canceled edit is accidentally committed.

**Related small defect:** `src/FileCat.App/Controls/FileListControl.cs:946–947` uses `cap - 1` for PageUp/PageDown; when `VisibleRowCapacity == 1`, page movement is zero. The scrollbar already uses `Math.Max(1, cap - 1)` at `643`. Use the same minimum for keyboard paging and verify a one-visible-row test; include this additional one-row edge case in the same input correction and its acceptance fixture.

#### Additional affected viewer surfaces

The same independent event rounding occurs in `src/FileCat.App/Controls/TextViewer.cs:543–547`, `src/FileCat.App/Controls/HexView.cs:413–419`, and `src/FileCat.App/Controls/HexCompareView.cs:503–507`. Fixing only the file list would leave the same precision device feeling inconsistent as the user moves into viewers.

TextViewer's PageUp/PageDown at `src/FileCat.App/Controls/TextViewer.cs:534–535` and HexView's at `src/FileCat.App/Controls/HexView.cs:342–343` also produce zero movement at one visible row. HexCompareView already uses `Math.Max(1, VisibleRows - 1)` at `src/FileCat.App/Controls/HexCompareView.cs:522`; preserve that positive case.

Apply a shared tested fractional-input policy where the surfaces have equivalent semantics, while retaining each surface's intentional cursor-versus-viewport behavior. Add the ten-small-events/one-large-event and one-visible-row fixtures to TextViewer, HexView and HexCompareView. Verify ordinary wheel, precision/inertial input, direction reversals and list boundaries on native hosts; a particular device's event stream is not established by source inspection.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-048"></a>

### FCUX-048 — Listing whitespace has no background context menu

**Severity:** S2 — Medium · **Area:** Background context menu · **Effort:** M

**Confidence:** source-confirmed. **Mitigations:** Paste, make-directory, refresh and terminal commands are available elsewhere through the command system; this finding is about the missing contextual route, not missing underlying capabilities.

#### Evidence and scenario

- `src/FileCat.App/Controls/FileListControl.cs:1054–1065`, `OnPointerPressed`: after calculating `RowAt`, an out-of-row press is marked handled and returns.
- `src/FileCat.App/Controls/FileListControl.cs:1067–1076`: context-menu invocation occurs only after that return, so a right-click below the last row does nothing.
- `src/FileCat.App/Views/PanelView.axaml.cs:803–854`: context-menu opening is item/focus oriented; Windows native context menus use selected file paths.
- `src/FileCat.App/Views/ContextMenuFactory.cs:11–24`: current menu is explicitly an item menu (Open, View, Copy/Move/Delete, clipboard operations and item Properties), not a location-background menu.

Copy files to the clipboard, enter an empty destination folder, and right-click the empty list area expecting to paste. No menu appears. In a partly filled folder, a user must right-click an existing item to find a Paste command, introducing irrelevant item-selection behavior into a location operation. The synthetic `..` row does not make the rest of the folder background an actionable target.

#### Recommendation and touchpoints

Distinguish item context from location background context explicitly. For whitespace, offer a small location-specific menu: Paste when supported, New folder, Refresh, open terminal here when applicable, and location properties/copy path as useful. Use provider capabilities to disable/explain or omit inapplicable actions consistently. Preserve Commander marks when opening a background menu unless the product deliberately specifies a selection-clearing action; do not silently act on the previously focused item. Any native Windows background menu should be invoked with its actual folder/background contract, not by passing the previous file selection.

`FileListControl` should publish a location/background context request separately from the current item request. `PanelView` should capture the correct location and panel scope, and `ContextMenuFactory` should have a distinct background builder. Keep the keyboard item-context command's focused-row behavior; provide a documented keyboard location-context route if the same location actions are not otherwise obvious.

#### Acceptance criteria

1. Right-clicking whitespace in an empty and nonempty filesystem folder opens the location menu at the pointer.
2. Paste and New folder target that folder, regardless of stale focused rows, marked items, or a different active panel before the press.
3. No destructive item action appears with an implicit target from an unrelated focused/marked row.
4. Read-only, archive, Registry, working-set and disconnected locations expose only appropriate actions with meaningful availability feedback.
5. Dismissing the menu does not unexpectedly clear marks or move file focus.
6. Item right-click and keyboard item context menus keep their current selection semantics.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-049"></a>

### FCUX-049 — Open text and hex viewers retain the old palette when the rest of the application changes theme

**Severity:** S2 — Medium · **Area:** Theme lifecycle · **Effort:** M

**Evidence: source-confirmed event/data flow; live native observation still required.** `src/FileCat.App/Controls/TextViewer.cs:140-163` and `src/FileCat.App/Controls/HexView.cs:157-174` resolve brush references on attachment only. Neither subscribes to ThemeManager.ThemeChanged, and source search finds no viewer/quick-view host forwarding the change. `src/FileCat.App/Services/ThemeManager.cs:201-216` replaces the merged dictionary with newly created brushes. In contrast, `src/FileCat.App/Controls/HexCompareView.cs:155-179` subscribes/unsubscribes and re-resolves brushes correctly. The viewer chrome uses DynamicResource and updates independently. `src/FileCat.App/Views/ReportWindow.cs:97-105,130` is another correctly implemented exception: it forwards ThemeChanged to its TextViewer.

**Impact.** Open a viewer in Classic, then change the main window to Classic Dark or turn on system high contrast. The chrome can change while the cached content surface keeps its original text/background/selection colors. This produces a visibly mixed interface and can prevent a readability preference from taking effect in the active content. It also affects the shared TextViewer/HexView inside quick view and the hex editor.

**Desired behavior and implementation.** Make custom-drawn controls own their theme lifecycle, using a common attach/detach subscription pattern or an equivalent resource-change invalidation mechanism. Re-resolve all semantic brushes and redraw on palette replacement. If decorative edge brushes may mutate, invalidate only affected drawing as needed. Preserve content, scroll offset, selection, pending nibble, edit state and focus; do not reconstruct the viewer window to change colors. Ensure subscriptions cannot retain closed windows.

**Acceptance criteria.** Keep text, Info, hex, hex editor, quick view and binary comparison open; switch Classic → Dark → High Contrast → DOS and back. Every surface updates immediately and consistently. Selection/caret remain visible and unchanged semantically. Enter and leave system high contrast while windows remain open. Extend theme tests to pre-existing custom controls; include lifecycle release checks to avoid static-event leaks.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-050"></a>

### FCUX-050 — Psychedelic effects cross functional content boundaries and can weaken menu readability

**Severity:** S2 — Medium · **Area:** Decorative effects · **Effort:** M

**Evidence: supplied screenshot plus current source.** In `docs/screenshots/windows/Psychedelic.jpg`, the File/Mark area has white/light text and small icons over a bright green luminous background, unlike the uniformly dark menu in System.jpg. The capture is static with animations disabled, so this is not merely a transition frame. `src/FileCat.App/Services/ThemeManager.cs:46-47,101-115` leaves Psychedelic MenuBackground at the transparent default. `src/FileCat.App/Controls/ThemeEffects.cs:194-222` draws moving Screen-blended neon blobs with up to 215 alpha behind it. `ThemeLayers.Over:88-110` places `ThemeGlitchOverlay` above functional content. `EffectArt.Glitch:225-259` snapshots and shifts horizontal strips of the rendered surface, including text and pictures. `src/FileCat.App/Services/ThemeAnimation.cs:66-84` schedules a 100–180 ms glitch approximately every 18–35 seconds when enabled.

**Impact.** Menus can become less legible depending on the bright region underneath; text/pixels can appear to shift while the user compares bytes or evaluates an image. Decorative artifacts may look like corrupted file content or rendering faults, and visual attention is pulled away from rows. The user deliberately choosing an expressive palette does not imply that displayed source evidence should deform.

**Desired design (design judgment grounded in source).** Keep the palette and its distinctive backgrounds, but place menus, toolbars, status text and all content viewers on contrast-controlled surfaces. Restrict motion and glitch effects to decorative areas outside source text, byte grids, image pixels, selection and command hit regions. Offer an explicit effects intensity or separate glitch preference only if maintaining the feature is valuable; default to a readable static treatment.

**Implementation touchpoints.** Give Psychedelic an opaque or sufficiently opaque MenuBackground similar to other decorative themes. Split decorative layers from content layers and clip effects to intentionally ornamental regions. Use a frozen deterministic background when animation is disabled. Do not test only a single time-zero frame: sample the full color/position cycle and theme transition states.

**Acceptance criteria.** Menu text and command icons remain readable throughout animation; essential text meets the chosen contrast targets at sampled worst-case bright positions. No effect changes the pixels of displayed pictures, comparison cells or text/hex content. Disabling effects leaves a stable image across invalidations/resizing, except for layout changes. A chosen decorative theme remains visually recognizable without competing with work content.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-051"></a>

### FCUX-051 — Reduced-motion promise is implemented only on Windows

**Severity:** S2 — Medium · **Area:** Motion preferences · **Effort:** M

**Evidence: source-confirmed.** `src/FileCat.App/Views/SettingsDialog.cs:45-46` tells users that the system's reduced-motion setting stops animations. `src/FileCat.App/Services/ThemeAnimation.cs:36-41,90-95` combines the application setting with SystemPrefersReducedMotion, but returns false for every non-Windows OS. This is a multiplatform application. The application checkbox is a workaround, not honoring the claimed platform preference.

**Desired behavior.** Respect the supported operating system's motion preference across all target platforms; when a platform cannot supply it, make the application's explicit setting authoritative and describe the limitation accurately. Also respond to preference changes during the session where the platform exposes change notifications. Preserve high-contrast precedence and pausing when the application is inactive.

**Implementation touchpoints.** Isolate a platform motion-preference service and use platform-supported APIs/events rather than a Windows-only conditional hidden in theme animation. Unit-test the policy using injected preference values, then verify the actual native preference integration on Windows/macOS/Linux desktop targets. Do not infer motion preferences from unrelated light/dark settings.

**Acceptance criteria.** Start with reduced motion enabled on each officially supported desktop environment: no drift/glitches run without overriding the user's preference. Change the preference with FileCat open: animations stop/start according to documented behavior. Explicit Animate theme effects off always wins. Documentation does not claim native support on desktop environments that are unverified.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-052"></a>

### FCUX-052 — Snapshot viewer modes detect change but make the user close and reopen the file to refresh

**Severity:** S2 — Medium · **Area:** Viewer refresh · **Effort:** M

**Evidence: source-confirmed.** `src/FileCat.App/Views/ViewerWindow.cs:577-609` states “reopen the viewer to refresh” for changed pictures, Info reports and rendered pages. `CheckForChanges:621-646` tracks source generation and correctly keeps the snapshots marked stale. `OnWindowKeyDown:654-724` contains no refresh command, while `CompareWindow` has Compare again/F5 and a reopen factory.

**Impact.** A user edits a document/image externally, returns to FileCat and sees a stale snapshot. They must close the window, locate the same file again and reopen it, losing view state and potentially confusing one of several similarly named viewer windows. The stale warning is valuable and should remain; the missing part is the recovery action.

**Desired design.** Add Refresh/F5 and a persistent “File changed — Refresh” banner for snapshot modes. Preserve the current mode, fit/zoom when feasible, approximate scroll position and selection. Require an explicit refresh when re-decoding is expensive; automatic refresh can be optional for appropriate content. When a provider cannot reopen the exact source, state why and offer the nearest honest action.

**Implementation touchpoints.** Pass a provider-aware reopen/refresh factory into the viewer launcher, analogous to CompareWindow.Open's reopen contract; avoid assuming LocalPath. Use new demand/revision tokens and retain the old valid snapshot until the replacement succeeds, clearly labeling it stale. Release old sources/decoded data after active work retires using existing lifetime patterns.

**Acceptance criteria.** After external modification, Refresh updates Text/Info/Picture/Page as applicable without closing the window and clears the stale notice only when the current revision is successfully displayed. Missing/replaced/unavailable files yield a readable error and preserve the last snapshot's stale label. Local, archive and remote provider cases follow capability rules. Zoom/scroll remain sensible, and existing revision/retirement tests pass.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-053"></a>

### FCUX-053 — Quick view has a blank loading interval and silent picture-to-hex fallback

**Severity:** S2 — Medium · **Area:** Quick-view feedback · **Effort:** M

**Evidence: source-confirmed; duration depends on storage/provider.** `src/FileCat.App/Controls/QuickViewPane.cs:136-151,165-183` clears visible preview content as focus changes; `185-255` awaits initial content/provider work without showing a loading message after header metadata. Native picture decoding is capped to a 256 logical-pixel target (`27,288-304`) and only attempted automatically for files up to 64 MiB (`42-43,255-256`). Decode failures at `315-319` are swallowed, leaving bytes in Hex; no explanation is shown until the user independently presses F3. Automatic preview limitations are legitimate but currently underexplained.

**Impact.** Over slow storage a selected file appears to have an empty preview, so the user cannot tell whether it is empty, loading or unsupported. A recognized picture may become raw bytes with no immediate reason. The full-height quick-view region can look underused with a small 256-pixel thumbnail; this is a deliberate performance tradeoff, but should be visible and offer an explicit higher-detail action.

**Desired design.** After the existing debounce and a small no-flicker delay, show “Loading preview…” with the file name and a cancellable demand tied to selection. Use explicit empty-file, unsupported, too-large, permission and decode-failed states. Provide “Open full viewer (F3)” as a visible action. For image quick view, size the preview target to the pane within a documented budget or provide a user-requested larger preview; retain small automatic thumbnails on slow sources.

**Implementation touchpoints.** Add a preview state model rather than inferring state from several IsVisible flags. Reuse existing request ownership and do not let an abandoned load update the new file's message. Catch results with a reason category instead of silently swallowing all decoder exceptions; avoid leaking technical stack traces. Keep cloud-placeholder protection and the explicit folder-size-demand policy.

**Acceptance criteria.** On an artificially delayed source, a loading state appears after a documented threshold and resolves into content/error. Rapid selection never shows another file's name or stale preview. Empty files say empty; image decode failure and size-policy skips explain themselves and offer F3. The full viewer remains reachable by keyboard/pointer. Resource budgets and existing QuickViewLifetime/FolderDemand/PictureAdmission tests remain valid.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-054"></a>

### FCUX-054 — Viewer Go to dialog has no visible action buttons and dismisses invalid input before explaining it

**Severity:** S2 — Medium · **Area:** Viewer navigation · **Effort:** S

**Evidence: source-confirmed.** `src/FileCat.App/Views/ViewerWindow.cs:816-860`, `GoToAsync`, creates a 360-pixel nonresizable top-level window containing only a label and TextBox. Enter closes it immediately; parsing happens afterward. Invalid input writes a message into the parent status bar and requires reopening Go to. There are no Go/Cancel buttons and the long format description is not explicitly wrapped. This differs from the application’s reusable prompt dialogs and from other editor commands.

**Impact.** Pointer-first users do not see how to submit. A typo closes the dialog, moves feedback away from the input and forces repetition. The user may miss the status error among offsets and encoding evidence. At large text sizes the explanatory label may clip.

**Desired design.** Use the shared accessible prompt pattern with an explicit input label, Go and Cancel, concise examples and inline validation. Keep the dialog open on invalid/out-of-range input, select the offending part and maintain the original value. Distinguish byte offset, percentage and line number clearly; optional segmented input modes can make prefixes unnecessary while preserving power-user syntax.

**Implementation touchpoints.** Move parsing to a reusable pure validator before closing the modal. Keep line scanning asynchronous with existing progress/cancel behavior. Use the owner viewport bounds and standard dialog styles. Set an automation name and ensure Enter/Space/Escape follow the shared pattern.

**Acceptance criteria.** The user can complete Go to using only the pointer or only keyboard. Invalid `0xZZ`, negative/unrepresentable values and invalid line prefixes remain in the dialog with a specific error; valid decimal/hex/percentage/line inputs go to the intended location. Large labels wrap without hiding actions. A remote nonseekable source reports its capability limitation before starting an unsupported scan.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-055"></a>

### FCUX-055 — Picture “Actual size” remains checked for every manual zoom level

**Severity:** S2 — Medium · **Area:** Picture zoom state · **Effort:** S

**Evidence: source-confirmed.** `src/FileCat.App/Views/ViewerWindow.cs:51,132-134,194-205` creates an Actual size ToggleButton but synchronizes its checked state to `!_picture.Fit`. `src/FileCat.App/Controls/PictureView.cs:159-164,206-224` leaves Fit mode for arbitrary wheel/key zoom values. Thus 125%, 200% or 50% manual zoom all mark Actual size as checked, although actual size means 100%. The numeric status at `src/FileCat.App/Views/ViewerWindow.cs:580-585` may show the actual zoom correctly, leaving the two controls semantically inconsistent.

**Impact.** When inspecting images/icons for pixel fidelity, users can mistake a manual zoom for actual pixels because the emphasized button says Actual size. In this application this distinction matters for icons, screenshots and image detail.

**Desired design.** Expose distinct Fit and 100% commands/states plus a visible zoom percentage. A three-state presentation (Fit, 100%, Custom) or Fit/100% buttons with a zoom dropdown avoids overloading a single toggle. Checking 100% should mean the displayed zoom is 1 within a defined tolerance; fitted small images may naturally also be at 100%, so separate mode from numeric scale.

**Implementation touchpoints.** Keep PictureView.Zoom's device-pixel-aware calculation and existing panning/zoom mechanics. Change only the state model/controls around it; avoid regressing EXIF and actual-pixel fidelity. Provide accessible names for Fit, 100% and the zoom value and use the existing RichTip for keyboard hints.

**Acceptance criteria.** Wheel to 125%/200%/50%: the UI says that value and does not claim 100%. Clicking 100% produces one source pixel per device pixel subject to the documented decoder-resolution cap, which must remain visibly disclosed. Fit resizes with the window. Verify 100%, 150% and 200% OS scaling and a small image where Fit and actual scale coincide.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-056"></a>

### FCUX-056 — Text comparison cannot select/copy source text or search for a known term

**Severity:** S2 — Medium · **Area:** Comparison interaction · **Effort:** L

**Evidence: source-confirmed.** `src/FileCat.App/Views/CompareWindow.cs:47,140-145,1038-1077` uses a ListBox of ordinary TextBlocks. `OnNavigationKey:291-320` offers block navigation but no Find or Copy implementation; there is no text-mode context menu or selectable cell implementation. Binary compare explicitly supports selection/copy (`src/FileCat.App/Controls/HexCompareView.cs:101-103,369-414` and its BuildMenu/Copy methods). This is distinct from [FCUX-009](#fcux-009): even a fully visible line cannot be selected as text in the comparison surface.

**Impact.** A user sees a changed value and wants to copy one side into a review note, terminal or editor, or find all differences containing a known function name. They must reopen the file outside comparison or navigate through every difference. Keyboard users cannot extract the displayed evidence directly.

**Desired design.** Add per-side text selection and Copy selected text, Copy line and Copy difference actions. A compact Find bar should search both sides or a chosen side and optionally only changed content; clearly indicate which scope is active. Keep difference-block navigation separate from text match navigation. A full merge editor is not required to solve this.

**Implementation touchpoints.** Address this with [FCUX-009](#fcux-009)’s comparison surface contract, preserving row/side identity and full original text. Define copy semantics for gaps, line endings and line-number inclusion; default source copying should omit UI line numbers. Selection must coexist with choosing the current difference. Expose selected side/range to accessibility and maintain virtualization.

**Acceptance criteria.** Select and copy part of a visible line, a long offscreen suffix, several aligned rows and a one-sided inserted block. Clipboard text matches the selected source, with documented line-ending behavior. Find can reach a term beyond the first visible page and announce no result only for its defined scope. Keyboard selection/copy and context menu actions are discoverable. Existing block-navigation tests still work.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-057"></a>

### FCUX-057 — Privacy setting promises no network requests but only controls automatic update checks

**Severity:** S2 — Medium · **Area:** Privacy wording · **Effort:** S

**Evidence: source-confirmed wording contradiction.** `src/FileCat.App/Views/SettingsDialog.cs:127-128` says “Check for new versions (only notifies; never downloads). Off means FileCat makes no network requests.” `src/FileCat.App/ViewModels/MainViewModel.Commands.cs:560-578` explicitly runs the manual Help → Check for updates request even when the automatic preference is off. Network/remote file access and optional network Shell handlers also exist; the latter are described immediately below the update checkbox (`src/FileCat.App/Views/SettingsDialog.cs:136-144`). The application legitimately needs user-requested network operations, so this is a scope/copy defect, not a recommendation to globally disable networking.

**Impact.** Users interpret the checkbox as an offline/privacy switch and receive a misleading model of what is controlled. “Never downloads” also needs a clear object: no update package download, rather than no data received by a check request.

**Desired wording and behavior.** Label it “Automatically check for updates” with concise helper text such as “Checks periodically and notifies you. FileCat does not download or install update packages. Manual update checks and network locations still connect when requested.” Show separately what optional preview/network services can do. Avoid a blanket no-network promise unless an actual global offline mode is implemented.

**Implementation and acceptance.** Update the label/help and any mirrored documentation. Tests should assert the automatic check gate and manual explicit-check behavior remain as designed. A user can understand the difference among automatic updates, manual checks and remote file access from the visible labels, with no need to infer it from source. Do not add redundant permissions to routine user-requested operations.

[Back to S2 index](#index-s2) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="findings-s3"></a>

## 5. Low-severity findings (S3)

<a id="fcux-058"></a>

### FCUX-058 — Dense command chrome gives too many actions equal visual weight

**Severity:** S3 — Low · **Area:** Main command hierarchy · **Effort:** M

**Evidence:** SC + VC + UX. This is a design recommendation, not a measured claim about task completion time. **Affected surfaces:** main toolbar, repeated per-panel Places bars and menu grouping.

#### Evidence

- `src/FileCat.App/ViewModels/MainMenuModel.cs:52–66` defines 32 toolbar commands across navigation, panel management, clipboard, file operations, archives, marking, search/comparison and settings/help.
- `src/FileCat.App/Views/MainWindow.axaml:19–23` hosts them in a `WrapPanel`; `src/FileCat.App/Views/MainWindow.axaml.cs:784–806` renders icon-only 16-DIP images. Accessible names and rich tooltips already exist.
- `src/FileCat.App/Views/PanelView.axaml.cs:164–198,240–265`, `BuildPlaceButtons` and place-button construction, repeat available Places in each panel. Many use an icon without a visible label. `PlaceBarPanel` already supplies overflow, so the problem is not a complete absence of overflow.
- `src/FileCat.App/Themes/Styles.axaml:382–415` uses small toolbar/place icons, tight padding and 11-point place labels. `src/FileCat.Core/State/StateModels.cs:83–91` enables the toolbar, drive buttons, command line and function-key bar by default.
- The supplied `docs/screenshots/windows/Classic.jpg`, `docs/screenshots/windows/ClassicDark.jpg`, `docs/screenshots/windows/HighContrast.jpg` and `docs/screenshots/windows/Psychedelic.jpg` all show the cumulative density in a four-panel layout. These captures support the visual assessment; they do not establish how often an individual user needs each command.

#### Scenario and impact

A user looking for one frequent action must distinguish a long run of similarly sized icons. Adding panels repeats navigation chrome, reducing the visual prominence of the current location, source/target role and file contents. In a narrow window the global toolbar wraps, changing content height and command positions. Rich tooltips help after hovering a candidate; they do not create a clear hierarchy before the user knows which candidate to inspect.

#### Recommended design

Retain a compact Commander profile with complete keyboard access. Introduce a deliberate primary toolbar group and a stable More menu for secondary commands. As an initial design hypothesis to validate, start with roughly 8–12 primary actions drawn from navigation, Copy/Move, New folder, Find and Operations; final membership should follow actual usage research and the established command model. Put labels on ambiguous primary actions or offer an Icons and labels option. Do not move commands dynamically according to short-term frequency, because stable spatial memory is valuable here.

Let users pin a small set of Places while keeping the complete location menu and existing overflow. Show meaningful compact labels for ambiguous or repeated destinations. Avoid making each panel repeat a full global navigation catalog when the user prefers a lean panel header. Preserve independent panel targets, keyboard location menus, accessible names, full-path tips and the option to keep the existing dense arrangement.

Menu changes should group related tasks without renaming stable command IDs or removing shortcuts. The File menu currently mixes basic file operations, archive tools, inspection, working sets and Registry operations; make group labels and secondary submenus explicit where that helps scanning. Retain the palette's existing menu-path information so experts can still find commands immediately. A larger menu hierarchy is not automatically better: test representative tasks before moving frequent actions another level deep.

#### Implementation and acceptance criteria

1. Define a declarative primary/secondary toolbar model separate from command behavior; preserve existing registry IDs, gestures and user overrides.
2. At minimum, default and maximized widths, primary actions remain in a stable order and secondary actions remain available through an accessible menu.
3. Four- and six-panel arrangements keep current location and target identity visually prominent; reducing Places density does not remove any location's reachability.
4. Test pointer discovery of Copy, New folder, Find, compare and Settings, and keyboard completion of the same tasks. Record observed errors and hesitation before deciding the final grouping.
5. Existing rich tooltips continue to show current shortcut and behavior. Icon-only mode remains available for users who prefer it.
6. Related viewer/editor hierarchy work should use the same grouping principles while respecting their different primary tasks; see [FCUX-060](#fcux-060).

[Back to S3 index](#index-s3) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-074"></a>

### FCUX-074 — Command search advertises F1 help while its key routing prevents Help dispatch

**Severity:** S3 — Low · **Area:** Command search help · **Effort:** S

**Confidence:** Source-confirmed routing mismatch. The command-search popup's broader native announcement and geometry behavior remains a separate validation question.

#### Evidence

- `src/FileCat.App/Views/CommandSearchBar.cs:122–128` tells a user with no matching commands that F1 lists every shortcut.
- `src/FileCat.App/Views/MainWindow.axaml.cs:460–463` returns before command resolution whenever `CommandSearchBox.IsFocused`.
- `src/FileCat.App/Views/CommandSearchBar.cs:131–161`, `OnKeyDown`, handles Escape, arrows, paging, Enter and F2, but no F1 action.
- The global Help binding is defined in `src/FileCat.Core/Commands/CommandRegistry.cs:414`; no separate F1/help binding on the search box supplies that omitted dispatch.
- `tests/FileCat.App.Tests/CommandSearchTests.cs:78–125,134–170` checks hint text and Enter/F2/Escape, not following the F1 instruction.

#### User scenario and impact

Type an unmatched query and follow the displayed F1 recovery instruction. The normal keyboard-reference command is not dispatched while the search box retains focus. Users reach a dead end precisely where the interface offers help, although pressing Escape and then opening Help remains an indirect route.

#### Minimum correction

Route the effective Help binding explicitly from command search while preserving its query and selection, or provide a real accessible “All commands / Keyboard reference” action with accurate instructions. Derive displayed gesture text from the effective keymap.

Do not allow every panel shortcut through the focused search field: its existing input boundary correctly prevents typed commands and F2 from acting on the file list.

#### Full UX target / safeguards

Preserve the implemented F2 shortcut-editor return path and its restored query/selection. The popup uses a named standard ListBox with selection while input focus remains in the TextBox (`src/FileCat.App/Views/CommandSearchBar.cs:51–58,111–128,163–167`). Native tests should confirm that current result, count, shortcut and unavailable reason are conveyed and that Enter's target is known. Source alone does not establish that these standard list semantics are wholly inaccessible.

The fixed 600-unit popup width and 420-unit list maximum at `src/FileCat.App/Views/CommandSearchBar.cs:55,64–77` warrant small-screen/scale checks. Actual popup repositioning and constraints are backend behavior; do not claim universal clipping without rendering evidence.

#### Acceptance criteria

1. No results → invoke the advertised Help action → close Help returns to the same search query, selection and focus.
2. Remapping Help updates both actual dispatch and the displayed hint.
3. Help, F2, Enter and Escape remain distinct; none unexpectedly acts on the underlying file selection.
4. The added Help action and return route are discoverable to keyboard and screen-reader users.

**Related native screening, not an additional closure prerequisite for the dispatch fix:** inspect current-result/unavailable-reason announcements and popup reachability at the supported minimum size and enlarged text. Record a separate demonstrated defect if one is found; a static maximum dimension alone is not proof of a runtime failure.

[Back to S3 index](#index-s3) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-059"></a>

### FCUX-059 — Readability controls are fragmented across the file list, viewers and comparison

**Severity:** S3 — Low · **Area:** Readability preferences · **Effort:** M

**Evidence: source-confirmed; priority is design judgment.** `src/FileCat.App/Views/SettingsDialog.cs:35,45` exposes only List font size 9–24. src/FileCat.App/ViewModels/MainViewModel.Settings.cs:20 applies it to ListFontSize. Viewer content starts at 13 and supports hidden Ctrl+plus/minus shortcuts; HexCompareView fixes `_fontSize=12` (`src/FileCat.App/Controls/HexCompareView.cs:38,71-73`) with no equivalent controls. `src/FileCat.App/Themes/Styles.axaml:27-28,130-140,158-170,363-367,405-415,451-473` includes fixed small text values from 10.5 to 14. The actual theme screenshots demonstrate a dense, small-text commander interface; this density is useful for experienced users and should remain available.

**Desired refinement.** Keep the compact default but add an application Readability/Density preference with named Compact and Comfortable choices, and a separate content font setting shared by text/hex/compare. Do not reinterpret the existing “List font size” silently. Surface viewer zoom in a menu/status control so users discover it; persist their preferred content size. Allow relative UI text scaling without forcing larger file icons. Use a small typography token set so status/secondary text remains subordinate yet readable.

**Acceptance criteria.** A comfortable preset enlarges chrome labels, targets and spacing consistently without clipping dialogs or destroying usable list density. Text/hex/comparison start at the chosen content font; reopening preserves it. Ctrl+plus/minus remains available and has a visible menu equivalent. Validate logical sizing at 100/150/200% OS scaling and respect the user's chosen system scaling. Avoid claiming every 11.5-pixel label is intrinsically inaccessible; measure actual render/readability and essential contrast.

[Back to S3 index](#index-s3) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-060"></a>

### FCUX-060 — Viewer/editor toolbars give advanced actions the same visual priority as frequent work

**Severity:** S3 — Low · **Area:** Viewer command hierarchy · **Effort:** M

**Evidence: source-confirmed composition; design judgment, not a claimed measured usability failure.** `src/FileCat.App/Views/HexEditorWindow.cs:40-51,73-88` puts Save, Save As, Stop, Export patch, Apply patch, Undo, Redo, Replace bytes, Go to and search options into one WrapPanel. `src/FileCat.App/Views/ViewerWindow.cs:124-153` similarly interleaves modes, encoding, wrapping, following, search, checksum and Edit bytes. At narrower widths WrapPanel prevents simple horizontal overflow, but reorganizes rows without preserving task groups; wrapping already exists and should not be reported missing.

**Desired refinement.** Use a first row for mode plus core navigation/view controls, a focused search bar on demand, and an Advanced/More menu for patch/import/export and infrequent inspection actions. Keep Save/Undo/Redo stable in the editor and make current mode visually explicit. Keep essential controls labeled, with icons as reinforcement. Use RichTip to show shortcuts and concise behavior; avoid placing multi-sentence implementation details in the default toolbar. Preserve keyboard shortcuts even when an action moves to a menu.

**Acceptance criteria.** At minimum, normal and maximized widths, logical groups stay together and the content retains a useful height. All existing commands are still reachable through keyboard and visible menus. Patch actions are discoverable but do not crowd the main editing path. Search state is retained when the bar is hidden/reopened. Current mode is unambiguous independently of color.

[Back to S3 index](#index-s3) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-061"></a>

### FCUX-061 — Truncated listing cells have no direct full-value inspection

**Severity:** S3 — Low · **Area:** Cell inspection · **Effort:** M

**Confidence:** source-confirmed. **Mitigations:** selected/focused name is also available in the right-hand status and its tooltip; column resizing and panel maximize exist.

#### Evidence

- `src/FileCat.App/Controls/CellText.cs:47–55,127–143`: long formatted and glyph-run text is end-ellipsized.
- `src/FileCat.App/Controls/FileListControl.cs:1141–1157`, `UpdateTip`: tooltips are only supplied for verification icons/Verified metadata cells. Truncated Name, Ext, Folder and other metadata cells have no general tooltip.
- `src/FileCat.App/Views/PanelView.axaml:96–98`: focused item status does have a full-content tooltip.
- `src/FileCat.App/ViewModels/TabViewModel.cs:704,707–733`: status describes the focused item, offering a useful but indirect full-name route.

#### Scenario and recommendation

In a directory of `Annual_financial_report_2026_final...` variants, comparing only the suffix requires repeatedly focusing a row, then looking at/hovering the status or changing layout. In search/result/provider columns, the status may not expose every clipped cell at all. Add delayed hover tooltips only when text is actually truncated, plus an accessible keyboard “inspect focused item/cell” route. Reuse cached raw/safe strings and measured clipping flags rather than calculating expensive metadata in pointer move. Name tooltips should include the extension and preserve invisible-character escapes. Ordinary short cells should remain quiet.

#### Acceptance criteria

1. Hovering a truncated name/extension/folder/metadata value reveals the complete safe text near that cell.
2. Untruncated cells do not generate redundant tooltips.
3. Keyboard/assistive-technology users can inspect the same content without pointer hover.
4. Tooltips close/rebind correctly after scroll, sort, refresh and tab switches; they never display a previous row's identity.
5. Performance remains bounded; hovering does not trigger uncapped filesystem metadata work.

[Back to S3 index](#index-s3) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-062"></a>

### FCUX-062 — Colliding tab labels and lock presentation obscure tab identity

**Severity:** S3 — Low · **Area:** Tab identity · **Effort:** M

**Confidence:** source-confirmed; full-path tooltips and the all-tabs chooser mitigate the ambiguity.

#### Evidence and present behavior

`src/FileCat.App/ViewModels/TabViewModel.cs:123–133` builds `TabHeader` from an optional lock emoji plus the sanitized provider title. `UpdateTitle` at 587–592 asks the provider for a display name. Local subfolders use their basename in `src/FileCat.Core/FileSystem/LocalFileSystemProvider.cs:50–54`; archive subfolders similarly use their final segment in `src/FileCat.Core/Archives/ZipProvider.cs:92–99`.

`src/FileCat.App/Views/PanelView.axaml:20–24` displays this header with a 220-unit maximum and ellipsis. The button tooltip contains the full `DisplayPath`. `src/FileCat.App/ViewModels/MainViewModel.Lists.cs:366–373` also provides a searchable all-tabs chooser with full paths and panel numbers, so location identity is recoverable.

The two lock options at `src/FileCat.App/Views/PanelView.axaml.cs:712–725` have meaningfully different behavior but the same header glyph. In `src/FileCat.App/ViewModels/TabViewModel.cs:325–327`, a normal locked tab opens navigation in new tabs. In `src/FileCat.App/ViewModels/PanelViewModel.cs:40–48`, a return-to-root tab returns to its saved root when revisited. `TabHeader` ignores that distinction.

#### User scenario and impact

Tabs for `ClientA/build`, `ClientB/build`, and an archive's `build` folder all read “build.” The user must hover or open the chooser before switching confidently. Identically marked locked tabs can then behave differently when entered or revisited. Frequent source/target switching makes this recurring friction, although it does not make the underlying operations intrinsically unsafe.

#### Recommended behavior and implementation

Add collision-aware location qualifiers using the shortest parent, drive, archive, or provider/account cue needed to distinguish visible tabs. Keep ordinary unambiguous titles compact. Apply safe-name formatting to every new qualifier. For exact duplicate locations, retain a consistent instance distinction only where useful rather than forcing full paths everywhere.

Represent the two lock modes distinctly and expose plain-language descriptions in the tab's accessible name/help and tooltip, including the saved root where applicable. A decorative symbol alone should not carry that behavioral distinction. Recompute labels after tab creation, closure, movement, rename/navigation, or lock-mode changes. Preserve existing overflow navigation and the useful all-tabs chooser.

#### Acceptance criteria

1. Same-named local, archive, and remote subfolders are distinguishable in the tab strip with a compact meaningful qualifier.
2. The full target and lock mode can be obtained by keyboard or assistive technology without a pointer hover.
3. Both lock modes have distinct visible/accessible meaning, and changing mode updates it immediately.
4. Long names, sanitized invisible/bidirectional characters, and tab overflow remain readable and do not regress tab switching or reordering.

[Back to S3 index](#index-s3) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-063"></a>

### FCUX-063 — Dependent options and list-editor actions remain enabled when they cannot do anything

**Severity:** S3 — Low · **Area:** Dependent controls · **Effort:** S

**Evidence: source-confirmed.** `src/FileCat.App/Views/SettingsDialog.cs:53-54` makes “also on network/removable/cloud” independent of SizeFolderOnSpace; `136-140` enables the slow Shell picture checkbox solely when the helper exists, irrespective of the main Shell pictures checkbox. `src/FileCat.App/Views/ColumnProfilesEditor.cs:57-75,122-158` exposes New/Remove/Up/Down/Add even when limits, first/last position, duplicate column or protected Name column make the action return without feedback. This is recoverable but creates inert-looking controls.

**Desired refinement.** Indent subordinate options under their parent and disable them while the parent is off, retaining their chosen value for reactivation. Bind action availability to the draft state. For protected actions, use explanatory helper text or a tooltip (“The Name column is required”; “Maximum profiles reached”) rather than a silently ignored click. Do not rely on gray alone to convey why unavailable.

**Acceptance criteria.** Dependent option state follows the parent immediately and does not erase the saved subordinate choice. Up is disabled for the first row, Down for the last, Remove for the sole profile/required Name, New at the profile limit, and Add for existing columns. All disabled actions expose a reason and regain availability when the state permits. Cancel still discards the draft.

[Back to S3 index](#index-s3) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-064"></a>

### FCUX-064 — Window restoration ignores saved position and does not preserve a distinct normal-size geometry

**Severity:** S3 — Low · **Area:** Window placement · **Effort:** M

**Confidence:** Source-confirmed unused position/current-bounds serialization; maximized restore behavior requires native platform verification.

#### Evidence

- `src/FileCat.App/Views/MainWindow.axaml.cs:91–95`, constructor: applies saved Width, Height and Maximized but never X or Y.
- `src/FileCat.App/Views/MainWindow.axaml:9–10`: declares a default size and `WindowStartupLocation="CenterScreen"`.
- `src/FileCat.App/Views/MainWindow.axaml.cs:844–854`, `SavePlacement`: writes `Bounds.Width/Height` and current `Position.X/Y`, plus the current maximized flag. No separately remembered normal restore rectangle is maintained in the main-window sources.
- `src/FileCat.Core/State/StateModels.cs:160–167`, `WindowPlacement`, has fields for position, size and maximized state, so position is explicitly persisted but unused on initialization.

#### Scenario and impact

Place FileCat beside another tool or on a secondary monitor, close and reopen it. Saved coordinates do not determine the new location; centering/window-manager policy does. If the app is closed while maximized, the source saves current bounds rather than explicitly retaining the last normal-sized rectangle. Depending on Avalonia/backend behavior, restoring from maximized after relaunch can therefore return to an unintended large geometry. The latter is a validation target, not a claimed observed Windows result.

#### Recommended implementation

Track normal-window bounds while the window is Normal; keep maximized state separately. Apply saved normal geometry on startup, validate it against available monitor work areas, then apply maximization. Restore monitor/location where possible, but clamp or recenter if a monitor was removed, DPI changed or the rectangle lies off-screen. Treat intentional first launch/reset differently from a valid saved placement. Use the platform-supported restore geometry API if available rather than relying on assumptions about maximized `Bounds`.

#### Acceptance criteria

- A normal window reopens at approximately the same usable position and size, including on a secondary monitor.
- After closing maximized, relaunching and restoring returns to the prior normal rectangle on supported backends.
- Monitor removal and scaling changes never leave the title bar or resize controls unreachable.
- Native tests cover Windows plus supported Linux/macOS window-management behavior; document any platform limitation instead of claiming exact placement universally.

[Back to S3 index](#index-s3) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-065"></a>

### FCUX-065 — A valid one-panel workspace is saved as one panel but restored as two

**Severity:** S3 — Low · **Area:** Workspace consistency · **Effort:** S

**Confidence:** Source-confirmed round-trip inconsistency.

#### Evidence

- `src/FileCat.App/ViewModels/WorkspaceViewModel.cs:196–198`, `RemovePanel`: permits removing panels until one remains; only removal of the final panel is blocked.
- `src/FileCat.App/ViewModels/WorkspaceViewModel.cs:279–295`, `ToState`: serializes the exact current panel list and tree, including a one-panel workspace.
- `src/FileCat.App/ViewModels/WorkspaceViewModel.cs:325–330`, `LoadState`: unconditionally creates panels until there are at least two, even after successfully loading one valid saved panel.
- `src/FileCat.App/ViewModels/WorkspaceViewModel.cs:331–336`: a resulting two-panel arrangement has implicit target pairing, and the saved one-panel tree no longer matches the resulting panel ID set.

#### Scenario and impact

Close the second panel for a focused one-panel task and save the workspace, or exit normally. Reopening introduces another panel, usually This PC, and changes the layout and target affordances. The user has to remove it again each time. A deliberate supported arrangement does not survive its own save/load cycle.

#### Recommended implementation

Separate the fallback for missing/invalid/empty state from the minimum for a valid saved state. Keep the two-panel first-launch default, but restore a valid one-panel state exactly. Validate panel/tree/active-target references after loading without creating a second panel merely to satisfy the default. If product policy truly requires two panels, enforce that consistently when removing panels and explain it; preserving the existing one-panel capability is the narrower fix.

#### Acceptance criteria

- `LoadState(ToState(...))` preserves the panel count and tree for valid one-, two-, and multi-panel workspaces.
- Missing or wholly unusable state still opens the intended two-panel default.
- One-panel mode shows appropriate no-target behavior; F5/F6 can request an explicit destination.
- Invalid target IDs are cleared safely without introducing an unintended destination.
- Existing panel-removal retirement and target-pairing tests continue to pass.

[Back to S3 index](#index-s3) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-066"></a>

### FCUX-066 — A rejected last-tab close can still alter recently closed history

**Severity:** S3 — Low · **Area:** Tab history · **Effort:** S

**Confidence:** source-confirmed.

#### Evidence, scenario, and impact

`src/FileCat.App/ViewModels/PanelViewModel.cs:76–88`, `CloseTab`, calls `Workspace.RememberClosed(tab.ToState())` at line 80 **before** checking `Tabs.Count == 1` and returning at lines 81–84. `src/FileCat.App/Views/PanelView.axaml.cs:667–673` calls this method directly on middle-click.

The main command correctly disables last-tab closing with “A panel always keeps one tab” in `src/FileCat.App/ViewModels/MainViewModel.Commands.cs:131–132`, and the context-menu action similarly checks the count at `src/FileCat.App/Views/PanelView.axaml.cs:698`. The discrepancy is therefore specific to an entry point that bypasses those guards.

A user middle-clicks the only tab. Nothing closes, but Ctrl+Shift+T then creates another copy of that location. Repeating the attempted close can fill recently closed history with phantom entries and evict genuine closed tabs because `WorkspaceViewModel.RememberClosed` at lines 254–259 inserts at the front and trims the list. `ReopenClosed` at lines 262–269 opens whichever state was recorded. No file content is lost; navigation history becomes misleading.

#### Recommended behavior and implementation

Apply the final-tab eligibility guard before any history mutation. Keep the invariant inside `CloseTab`, even when the UI disables an action, so all callers behave safely. Centralize or reuse eligibility across command, context menu, and pointer paths. Preserve the current policy that every panel retains one tab; replacement by a fresh tab is not required by this finding. Update the stale method comment at line 75 to reflect the chosen behavior.

#### Acceptance criteria

1. Middle-clicking the only tab changes neither its state nor recently closed history.
2. Repeating a rejected close never evicts genuine history entries.
3. Closing an eligible tab through command, menu, or middle-click records exactly one entry; reopening restores that actual tab state.
4. Command availability and pointer behavior share the same last-tab policy, including tab moves between panels.

[Back to S3 index](#index-s3) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-067"></a>

### FCUX-067 — About’s support actions dismiss the dialog and “Show data folder” has an ambiguous target

**Severity:** S3 — Low · **Area:** Support actions · **Effort:** S

**Evidence: source-confirmed; low-impact design judgment.** `src/FileCat.App/Views/AboutDialog.cs:47-57` implements Copy details and Show data folder as dialog-result buttons, so both close the dialog. The latter navigates the active file panel to SettingsDirectory, although Facts displays distinct Settings and Local data paths (`93-95`). On normal installations these may differ. Selectable individual facts are already present and should be preserved.

**Desired refinement.** Keep Copy details inside the open About dialog with a brief inline “Copied” confirmation. Name folder actions precisely: Open settings folder and Open local data folder when distinct; one Open data folder when portable paths coincide. Open the chosen location in a new tab or an explicitly named target to avoid unexpectedly replacing a working tab. Keep Close as the dialog dismissal action. The existing practical version/platform facts should retain priority over cosmetic embellishment.

**Acceptance criteria.** Copy details leaves the facts visible and copies exactly the documented support content. Folder labels resolve to the path displayed beside them. Portable/nonportable profiles have appropriate actions. A user can inspect, copy and open a folder without reopening About repeatedly, and ordinary active work is not unexpectedly replaced.

[Back to S3 index](#index-s3) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="fcux-068"></a>

### FCUX-068 — “Move up” appears on finished jobs even though it cannot do anything

**Severity:** S3 — Low · **Area:** Queue actions · **Effort:** S

**Confidence:** Source-confirmed.

**Evidence:** `src/FileCat.App/Views/OperationsView.axaml:117` shows Move up when `!IsActive`, including finished jobs. `src/FileCat.App/Views/OperationsView.axaml.cs:62–65` forwards it to `JobManager.MoveInQueue`; `src/FileCat.Core/Jobs/JobManager.cs:107–115` operates only on Queued jobs and returns for every other state. There is no disabled state or explanation for the queue's first item.

**Scenario and impact:** Select a completed copy and see Move up next to Undo/Remove. Clicking it has no effect. This suggests rearranging history is supported when the control actually changes execution order. A queued top item also offers an ineffective action.

**Implementation:** Add `CanMoveUpInQueue` derived from queued state and actual queue position. Label “Move earlier in queue” (or keep short label with tooltip). Hide it for finished/running jobs and disable it with explanation for the first queued job. Do not repurpose it to reorder history silently.

**Acceptance criteria:** Every visible enabled queue-reorder action changes queue ordering; finished operations never offer it. Queue changes are announced and preserve job destinations/sources.

[Back to S3 index](#index-s3) · [Minimum corrections](#minimum-corrections) · [Verification](#verification)

<a id="implementation-sequence"></a>

## 6. Implementation sequence and dependency plan

Severity determines what needs attention; shared components determine how to implement it efficiently. Several high-severity findings have small, focused fixes, while accessible virtual rows and a readable comparison surface require coordinated design. Start the latter early without making deterministic correctness fixes wait for a broad visual redesign.

<a id="minimum-corrections"></a>

### Minimum corrections and full interaction targets

The middle column describes a reviewable outcome that can contain or resolve the main consequence. The right column gives the fuller interaction direction. This is a staging guide, not permission to omit a finding's relevant acceptance criteria. A contained alternative must work for keyboard, pointer, supported viewport, and the actual large-content or failure case that made the defect consequential.

| Finding / outcome | Minimum reviewable correction | Full interaction target and boundary |
| --- | --- | --- |
| [FCUX-001](#fcux-001) — reliable recovery intent | Decline equivalent continuation when the record lacks necessary scope/options; retain valid review/cleanup and offer a newly reviewed transfer with the missing intent explained. | Versioned complete intent supports equivalent continuation. Keep recovery's conservative Skip policy explicit; do not silently revive an original Replace setting. |
| [FCUX-069](#fcux-069), [FCUX-070](#fcux-070) — predictable Paste | Capture immutable destination/owner before awaiting; resolve transfer action from the same current clipboard publication as the payload; never infer Move from stale path membership. | A coherent publication/action/lifecycle contract with native exchange qualification and explicit partial-move outcomes. No confirmation on every ordinary Copy is required. |
| [FCUX-002](#fcux-002) — stable range selection | Reset invalid/tab-external anchors and prevent stale row coordinates from changing a batch scope. | Stable identity-based anchors re-resolve across ordering changes while preserving Commander marks. |
| [FCUX-003](#fcux-003), [FCUX-005](#fcux-005) — explicit mutation scope | Show included/hidden counts and exact preview scope; show both full synchronization endpoints and the deletion target. | Shared identity/scope review with complete inspection. Preserve each workflow's deliberately chosen default. |
| [FCUX-004](#fcux-004) — deliberate background decisions | Ensure spontaneous decisions cannot inherit an unsafe default/queued activation; retain safe cancellation and operation identity. | Persistent operation attention and deliberately opened reviews with session-owned focus. |
| [FCUX-018](#fcux-018) — owned, accurate Undo | Prevent duplicate admission, track the inverse through shutdown/mutation scheduling, retain required rename recovery evidence, and preview exact inverse verbs/limitations. | Typed inverse requests, coherent history and complete retained result inspection. A spinner alone does not close the lifecycle defect; full backup/transaction rollback is outside this fix. |
| [FCUX-006](#fcux-006) — lossless Settings | Preserve untouched structured definitions; edit supported fields losslessly; reject malformed grammar in the same form. | Structured list/detail tool editors and a nonexecuting invocation preview ([FCUX-036](#fcux-036)). The larger editor is not a prerequisite for preserving settings. |
| [FCUX-007](#fcux-007) — trustworthy previous search | Search the complete declared preceding range in both callers; guarantee encoding-aware progress; keep current query/start ownership; expose visible cancellation and truthful completeness qualifiers. | A richer search-results/navigation surface is optional. A full search-results redesign is not needed to correct the current scope, continuation and cancellation presentation. |
| [FCUX-008](#fcux-008) — accurate checksums | Honor a one-byte selection; expose full hashes and completion scope; make copying an explicit action. | A reusable result surface with selectable values and current-revision status. |
| [FCUX-009](#fcux-009) — inspectable comparison evidence | Provide keyboard/pointer-accessible detail for both full lines and exact changed spans, with bounded segment access where needed. | Synchronized horizontal navigation, aligned wrapping, text selection/search ([FCUX-056](#fcux-056)). A merge editor is separate scope. |
| [FCUX-010](#fcux-010) — usable listing semantics | Prototype the pinned backend's list/item/selection semantics and an accessible complete-selection review; validate the core journey natively. | Stable virtual peers, lazy offscreen/hidden-mark access, and coalesced state events. A renamed container or eager peer per file is insufficient. |
| [FCUX-011](#fcux-011), [FCUX-034](#fcux-034), [FCUX-041](#fcux-041), [FCUX-054](#fcux-054) — repairable forms | Keep footer/actions reachable, validate before dismissal, retain the draft/caret, and expose the exact invalid field. | A shared viewport and typed-draft contract with incremental caller migration. |
| [FCUX-012](#fcux-012) — reliable content geometry | Provide horizontal access at the chosen font and preserve source-relative selection through reflow. | Adaptive byte rows are optional; one correct coordinate map is required whichever geometry is chosen. |
| [FCUX-076](#fcux-076) — trustworthy Copy | Copy the stated source/display representation and explicitly disclose any cap or unread portion. | A shared bounded copy policy and optional streamed range export. Unlimited clipboard memory is not required. |
| [FCUX-071](#fcux-071), [FCUX-075](#fcux-075) — honest transfer affordances | Account for every selected item before export and refuse unsupported target/payload combinations consistently on hover and drop. | Capability-based export/staging with existing budgets and cleanup retained. A new provider transfer engine is separate scope. |
| [FCUX-072](#fcux-072), [FCUX-073](#fcux-073) — usable content semantics | Expose right/left byte-range selection by keyboard; publish current content/selection/editing state after it is committed. | Bounded document/range semantics verified against the actual framework/backend. Avoid synchronous whole-file reads and announcement floods. |
| [FCUX-021](#fcux-021) — coherent synchronization | Preserve/reconcile exclusions and plan consent; expose group identity and Stop scope; make pending-step disposition explicit after unexpected failure. | A lightweight group over existing child jobs. Already completed work remains; automatic rollback is not required. |
| [FCUX-013](#fcux-013), [FCUX-049](#fcux-049) — readable live themes | Fix the proven essential-text pairs and redraw existing affected controls through correct subscription lifetimes. | Rendered semantic-state matrix and reusable resource lifecycle where it reduces duplication. |
| [FCUX-035](#fcux-035) — clear persistence | Show the actual reason changes cannot persist and label the accepting action accurately. | Export/alternate-profile recovery only when the product intentionally supports it. |
| [FCUX-053](#fcux-053), [FCUX-055](#fcux-055) — understandable previews | Explain delayed loading/fallback and make 100% reflect actual scale. | Pane-aware previews within a resource budget and accessible numeric zoom. |

For each patch, copy the relevant row into the implementation task along with the full finding. Record the selected approach and any deferred refinement. A finding may be closed by an effective alternative inspection route when that route passes the same consequential scenarios; it should not remain blocked solely by an optional larger control rewrite.

### 6.1 Proposed work packages

The package names below are implementation groupings, not additional findings. A package is complete only when its visible behavior and relevant acceptance criteria are verified.

| Priority / package | Findings | Concrete deliverable | Dependencies and release evidence |
| --- | --- | --- | --- |
| **P0 — Preserve operation intent** | [FCUX-001](#fcux-001), [FCUX-069](#fcux-069), [FCUX-070](#fcux-070), [FCUX-002](#fcux-002), [FCUX-003](#fcux-003), [FCUX-005](#fcux-005), [FCUX-004](#fcux-004) | Recoverable transfer intent; coherent clipboard action; immutable Paste destination; tab-owned range anchor; explicit hidden-mark review; complete sync endpoints; deliberate background decisions. | Reuse existing mutation guards. Prove the submitted request matches the reviewed scope and mapping, including after interruption, clipboard replacement, reordering, filtering and delayed prompts. |
| **P0 — Own the Undo lifecycle** | [FCUX-018](#fcux-018), with [FCUX-019](#fcux-019) | Exclusive inverse admission, active-work/close ownership, correct inverse review and complete retained results. | Retain kind-specific guards and dependent-rename recovery/cancellation boundaries. Clearing the original history row must not detach active work. |
| **P0 — Correct factual results and configuration** | [FCUX-006](#fcux-006), [FCUX-007](#fcux-007), [FCUX-008](#fcux-008) | Lossless settings argument/association handling; complete backward search; explicit checksum scope and visible full results. | Focused fixtures can verify argument vectors, match positions and known hashes independently of a large UI rewrite. Keep existing cancellation and revision ownership. |
| **P0 — Make decisive comparison content inspectable** | [FCUX-009](#fcux-009), with [FCUX-056](#fcux-056) and [FCUX-012](#fcux-012) | A text surface that can reveal the exact changed span and supports full-line inspection, followed by selection/copy/search. | Define row/column/source-offset mapping first. A clearly labeled full-line detail view can contain the immediate issue while the shared virtualized surface is built. |
| **P0 — Complete the core accessibility model** | [FCUX-010](#fcux-010), then [FCUX-032](#fcux-032), [FCUX-031](#fcux-031), [FCUX-029](#fcux-029) | Stable list/container semantics, virtual item peers, focus versus selection state, listing-state announcements and keyboard equivalents. | Start with a native inspector prototype against the pinned Avalonia version. Validate actual supported assistive technology; do not replace this gate with string-only tests. |
| **P1 — Stabilize shared dialogs and validation** | [FCUX-011](#fcux-011), [FCUX-034](#fcux-034), [FCUX-041](#fcux-041), [FCUX-054](#fcux-054) | Viewport-bounded dialog frame, visible footer, validation that keeps the draft and focus, field-associated errors. | Dialog infrastructure supports the P0 scope/decision work. Migrate callers incrementally; do not require every dialog to share identical content layout. |
| **P1 — Make batch reviews complete and responsive** | [FCUX-038](#fcux-038), [FCUX-039](#fcux-039), [FCUX-040](#fcux-040), [FCUX-021](#fcux-021) | Full bulk-rename review and error navigation, background validation, explicit counter errors, and synchronization choices tied to the current plan. | Preserve captured scope and draft options; option changes must reconcile exclusions and invalidate plan-specific permanent-delete acknowledgment. |
| **P1 — Make operation evidence trustworthy** | [FCUX-015](#fcux-015), [FCUX-016](#fcux-016), [FCUX-017](#fcux-017), [FCUX-019](#fcux-019), [FCUX-020](#fcux-020), [FCUX-042](#fcux-042), [FCUX-043](#fcux-043), [FCUX-068](#fcux-068) | Updating reports, truthful phase/effect states, deliberate history/recovery retention and bounded drawer sections. | Use versioned/coalesced report updates rather than full copies on each progress tick. Verify displayed effects, source disposition, recorded limits and actual job result agree. |
| **P1 — Make external transfer behavior coherent** | [FCUX-071](#fcux-071), [FCUX-075](#fcux-075), with [FCUX-069](#fcux-069) and [FCUX-070](#fcux-070) | Whole-selection export accounting, honest drop effects, and actionable refusals. | Preserve existing staging limits, origin propagation and cleanup. Verify native exchange after deterministic payload/admission tests. |
| **P1 — Complete viewer copying and semantics** | [FCUX-076](#fcux-076), [FCUX-072](#fcux-072), [FCUX-073](#fcux-073), with [FCUX-012](#fcux-012) and [FCUX-056](#fcux-056) | Source-faithful or explicitly labeled display Copy; visible bounds; keyboard byte-range selection; current semantic state. | Share source/visual mapping and request ownership. Verify exact clipboard output separately from visible and spoken selection. |
| **P1 — Stabilize panel state and geometry** | [FCUX-022](#fcux-022), [FCUX-023](#fcux-023), [FCUX-024](#fcux-024), [FCUX-025](#fcux-025), [FCUX-014](#fcux-014), [FCUX-026](#fcux-026), [FCUX-027](#fcux-027), [FCUX-028](#fcux-028), [FCUX-030](#fcux-030), [FCUX-031](#fcux-031), [FCUX-033](#fcux-033), [FCUX-047](#fcux-047), [FCUX-048](#fcux-048) | Current navigation ownership, accurate capacity, safe filename/extension display, reversible workspace replacement, reachable columns, predictable filters and panel resizing. | Distinguish user-triggered reorder/navigation from background updates. Preserve persistent marks and tested refresh scroll behavior. |
| **P1 — Make settings and command behavior consistent** | [FCUX-035](#fcux-035), [FCUX-036](#fcux-036), [FCUX-037](#fcux-037), [FCUX-063](#fcux-063), [FCUX-046](#fcux-046), [FCUX-057](#fcux-057) | Typed drafts, accurate persistence state, shared command presentation/context mapping and truthful privacy text. | Fix lossy parsing before exposing structured editors. Preserve raw editing/import for expert use and existing execution capability gates. |
| **P1 — Make search and previews self-explanatory** | [FCUX-044](#fcux-044), [FCUX-045](#fcux-045), [FCUX-052](#fcux-052), [FCUX-053](#fcux-053), [FCUX-055](#fcux-055) | Explicit search roots/completeness, refreshable snapshots, preview loading/fallback states and accurate zoom mode. | Reuse source/revision ownership. Every visible result identifies the file, scope and completion state to which it belongs. |
| **P1 — Repair theme behavior and readability** | [FCUX-013](#fcux-013), [FCUX-049](#fcux-049), [FCUX-050](#fcux-050), [FCUX-051](#fcux-051) | Complete semantic foreground/background pairs, live updates in already-open controls, effects outside work content and accurate motion integration. | Numeric palette checks plus rendered states. Keep current high-contrast precedence and existing primary/target/tooltip fixes. |
| **P2 — Refine hierarchy and persistence details** | [FCUX-058](#fcux-058), [FCUX-074](#fcux-074), [FCUX-059](#fcux-059), [FCUX-060](#fcux-060), [FCUX-061](#fcux-061), [FCUX-062](#fcux-062), [FCUX-064](#fcux-064), [FCUX-065](#fcux-065), [FCUX-066](#fcux-066), [FCUX-067](#fcux-067) | Stable primary commands, functional search Help, coherent readability controls, direct full-cell inspection, distinguishable tabs and reliable window/workspace restoration. | Use validated compact/comfortable layouts. These changes should not rename command IDs or disrupt expert keyboard sequences. |

### 6.2 Suggested engineering slices

**First reviewable patch set:** fix lost recovery parameters or disable misleading continuation for incomplete journals; bind Cut/Paste to coherent clipboard intent and an immutable destination; repair range-anchor ownership; make settings round-trips lossless; correct complete, terminating backward search; honor one-byte checksum selections; display full sync endpoint identities. Start the owned Undo lifecycle in parallel because it crosses admission, history and shutdown. These changes have concrete before/after fixtures. An implementation should never present a reconstructed transfer as an equivalent continuation when the original intent cannot be recovered.

**Shared controls patch set:** establish the dialog frame, semantic list/viewport contract, and theme-state pairs. Add the smallest representative consumers first: one complicated operation dialog, one custom file list, one long-line comparison, and one selected hex/text surface. Validate those foundations before migrating every caller.

**Workflow patch set:** finish operation report retention, search completeness, filter correction, workspace restoration, preview states and keyboard context mapping. These are user journeys spanning several components, so review them as complete sequences, including cancellation and failure.

**Visual refinement patch set:** adjust hierarchy, optional density and secondary controls after state semantics are reliable. Keep the command system and panel model stable. A more attractive screenshot is not sufficient evidence that selection, scope and result interpretation improved.

### 6.3 Decisions to make before implementation branches diverge

| Decision | Recommended starting position | Why it matters |
| --- | --- | --- |
| Hidden marks in batch mutations | Preserve Commander marks as real selection; disclose hidden counts and provide the same explicit scope control in every affected review. | Changing the default silently would create a different inconsistency. The submitted preview must identify the exact approved set. |
| Background decisions | Persistent operation attention plus deliberate opening; if automatic presentation remains, prevent queued input from accepting a consequential action. | Focus/default policy must be shared rather than derived from button order. |
| Recoverable intent for older journals | Mark incomplete intent as requiring a newly reviewed transfer, while retaining supported cleanup/review actions. | A schema migration cannot invent a missing original filter or relative-folder mapping. |
| Clipboard intent when exchange metadata is unavailable | Use a clearly defined non-moving fallback or refuse an unsupported move intent with an explanation; never infer Move from historical path membership. | Current payload, action and ownership must agree. A normal supported Copy remains direct. |
| Undo scope and eligibility | Preview actual typed inverse actions and their limitations; admit one owned attempt. | Retained Undo steps do not imply complete reversal, especially after replacing an existing destination. |
| Synchronization Stop and partial failure | Offer a whole-group Stop; explicitly name any child-only Cancel. Recommended starting policy: hold pending removals after unexpected transfer failure/stop until reviewed. | Queue serialization alone is not a dependency/failure policy. Expected policy skips need separate treatment. |
| Workspace replacement recovery | One complete bounded prior-workspace snapshot, separate from RecentlyClosed. | It provides a useful undo without building unlimited workspace history or evicting ordinary recently closed tabs. |
| Horizontal content access | Preserve all source content; support horizontal reveal when unwrapped and explicit wrapping/adaptive rows where suitable. | Ellipsis is appropriate for summaries and labels, not the sole presentation of decisive file evidence. |
| Settings in a protected profile | Either disable editing clearly, or explicitly allow changes for this session. | The action label must tell the user whether changes will persist before they commit them. |
| Density | Retain Compact and offer a coherent Comfortable option. | The app serves expert, information-dense workflows; readability should be adjustable without replacing that foundation. |
| Shortcut conventions | Context-aware central registration, visible hints and user remapping; retain a documented legacy profile if desired. | A broad shortcut change can break expert habits. Context and conflict policy need an explicit product decision. |

<a id="shared-specifications"></a>

## 7. Shared UX specifications for implementation

These specifications consolidate recurring requirements. They are proposed contracts derived from the findings, not descriptions of features that already exist. Names of proposed models/fields are illustrative; use the repository's conventions and existing equivalent types.

### 7.1 Capture and present the exact operation intent

Treat **intent**, **review**, and **execution progress** as different objects. Intent describes what the user asked to happen. Review describes the current resource identities/capabilities and the specific action being approved. Progress reports what execution actually did. A later progress event must not silently rewrite intent; a new option or direction must invalidate the relevant review.

| Information | Capture requirement | Visible review requirement |
| --- | --- | --- |
| Operation kind | Copy, move, rename, delete, synchronize or the actual supported mutation. | Use a concrete verb; distinguish copying from moving originals and recycling from permanent removal. |
| Sources | Stable provider/location/item identities, with complete relative destination mapping when applicable. | Count, representative names, full source identity, mixed-parent information and a route to inspect the complete set. |
| Focus versus marks | Record the explicit selection resolution used by the command. | Explain when the focused item is used because no marks exist, and disclose marked items hidden by filtering. |
| Destination | Exact captured destination and any per-source new name/relative path. | Full source/target endpoints in a persistent header; identical basenames must remain distinguishable. |
| Filtering/layout | Original matching semantics, flatten/retain hierarchy choice and any saved-filter expansion needed to reproduce the operation. | Show applied restrictions and destination structure in the review. A later edit to a saved filter must not alter an already-approved operation. |
| Conflict policy | The chosen policy and its scope. | “For this operation” stays explicit. Recovered Skip policy must be disclosed if deliberately different. |
| Verification | The original requested verification intent and supported effective behavior. | Explain any capability-based change. Today's default must not silently replace a recovered request's setting. |
| Review revision | The source/target/plan state to which the decision applies. | If a changed option or external change invalidates approval, clearly require an updated review. |
| Recovery sufficiency | Versioned record completeness, including supported legacy fallback. | Offer continuation only when equivalent intent is recoverable; otherwise offer a new review with the missing information explained. |

A proposed compact review header could show:

> **Move 21 marked items**  
> Source: `C:\Projects\Alpha`  
> Target: `E:\Archive\Alpha`  
> 3 marks are hidden by the current filter and excluded. Included in this review: 21 visible items out of 24 total marks.  
> Files matching: `*.jpg` · Keep relative folders · Existing files: Ask

The values above are illustrative copy, not data taken from the attachment. With the displayed scope, the primary action is “Move 21 items.” If the user includes the three hidden marks, change it to “Move 24 items” and update the count, full preview and counter sequence together. The final action must agree with the current review. Generated destination names belong in the body, where they can be inspected, rather than expanding a button to the length of a filename.

For synchronization, name the endpoint receiving deletes in Mirror mode. Retain user exclusions by stable action identity only where they remain valid after a plan change; show removed/changed actions explicitly. Tie permanent-delete acknowledgment to the reviewed plan revision, not a checkbox that outlives the selected direction and scope.

**Recovery policy boundary.** Capturing original intent does not authorize silently replacing the current conservative recovery policy with an original Replace choice. `src/FileCat.App/ViewModels/MainViewModel.Interrupted.cs:107–120` deliberately submits Skip. Ordinary existing destination files are kept, not certified as verified arrivals. Same-item aliases and incompatible types can still require the current safe decisions; existing directories merge and may have unfinished descendants (`src/FileCat.Core/Jobs/Executors.cs:475–485,1375–1407`). A remaining source after an interrupted cross-volume move must not be removed merely because a destination name exists. Explain a deliberate policy change before accepting it.

**Which changes invalidate a review.** A form generation protects presentation ownership; it is not an atomic snapshot of the filesystem. Preserve execution-time conflict, expected-target and directory-content guards even after introducing a plan revision.

| Change after an initial preview | Required transition |
| --- | --- |
| Rename rule, explicit-name import, included source scope, or actual counter order changes | Advance the semantic plan revision; mark validation pending; submit only that revision's complete mapping. |
| Preview scrolling, display sorting, details, or a Problems-only display filter changes | Preserve source identities, inclusion and counter mapping. A presentation change must not renumber destinations or change mutation scope. |
| Synchronization direction, endpoint or removal set changes | Rebuild/reconcile action identities; invalidate acknowledgment tied to an older deletion scope; disclose exclusions that no longer apply. |
| An excluded synchronization item is re-included | Evaluate whether it expands the acknowledged removal scope. Do not let a hidden old checkbox approve the expansion. This need not open a modal on every toggle. |
| A destination becomes occupied after preview | Preserve fail-if-exists/conflict admission and expected-target guards. An explicitly approved fixed spelling cannot silently become an overwrite or a different fixed mapping. If the approved Keep-both policy permits a fresh unique name, preserve that behavior and report the actual final name. |
| The review closes while preparation is in flight | Invalidate that presentation owner, cancel pending work when possible, and discard late results. Already admitted jobs retain independent ownership. |

Existing rename gating and final destination checks are useful foundations (`src/FileCat.App/ViewModels/MainViewModel.BulkRename.cs:55–95,130–143`; `src/FileCat.Core/Operations/BulkRename.cs:239–258`). Sync's expected-target and folder checks remain necessary at publication/removal (`src/FileCat.Core/Jobs/Executors.cs:1057–1070,1322–1326`). None of these UI revisions proves that every descendant is an immutable content snapshot.

### 7.2 Make operation and result state truthful

Separate task phase, measured progress, completion quality, and available action. A finished task is not necessarily a successful task, and a pause request is not yet a paused worker. The exact enum names can differ from the following presentation contract.

| State shown to the user | Progress treatment | Primary useful action / explanation |
| --- | --- | --- |
| Queued | No fabricated transfer progress; optional queue position. | Move earlier only while actually queued; cancel queued work. |
| Preparing / Reviewing | Indeterminate or truthful reviewed-item count; show what is being prepared. | Cancel preparation when supported, or clearly show that the request is pending. |
| Running | Known bytes/items with a valid denominator; indeterminate where total work is unknown. | Pause/Stop as supported, inspect current route. |
| Decision required | Retain progress and show why work is waiting. | Review decision; keep the operation identifiable if another dialog is open. |
| Pause requested | Keep the latest measured values. | “Pausing after the current safe step…” until the worker confirms it is paused. |
| Paused | Freeze meaningful progress; no continued-running wording. | Resume or cancel. |
| Cancel requested | Retain progress; distinguish it from terminal cancellation. | Explain that completed work remains and the current safe boundary is pending. |
| Completed | 100% only where the known work was completed; show actual counts. | Open destination/report; offer available Undo with its actual scope. |
| Completed with issues | Show completed/skipped/failed counts and a prominent report action. | Review affected items; do not hide the qualifier in clipped text. |
| Canceled / Failed / Interrupted | Preserve the last actual progress, or use a neutral terminal indicator. | Review what completed, what remains and what recovery is actually available. |

An issue/report model should carry a stable item identity, full safe-display path, action attempted, outcome category and readable reason. Retained-record limits must be visible: for example, “Showing 10,000 recorded issues; additional issues were not retained,” if that is the actual policy. Do not invent a complete export when the underlying details were never retained. If full evidence is important, write a bounded or streamed report during execution and expose its availability deliberately.

Update the selected operation's report when its issue revision changes. Keep scroll/selection stable while appending, unless the user has chosen to follow the latest record. On completion, reconcile one final snapshot so counts and rows agree. A large stream must not trigger a complete 10,000-row copy on every progress tick.

Keep report retention separate from Undo retention. The user should understand which action removes visual history, which retires an undo record, and which stops tracking interrupted work. Recommended distinctions are “Hide from history,” “Forget undo information,” and “Stop tracking this interruption” only where these accurately match the implemented policy. A label such as “Dismiss” is too ambiguous for a record that will no longer appear after restart.

**Describe committed effects, not just processed counters.** The existing model deliberately separates logical work, actual data processing and verification (`src/FileCat.Core/Jobs/Job.cs:64–89`). Skipping an existing file can settle logical work with zero bytes copied; an outcome counted as skipped can also mean a copy was published but the source was deliberately kept (`src/FileCat.Core/Jobs/Executors.cs:884–889,1082–1095,1175–1203`). Preserve these accounting distinctions and give the report enough structured information to explain the effect.

| Actual outcome | Suitable user-facing interpretation | Detail that must remain distinguishable |
| --- | --- | --- |
| Approved existing-file Skip; no transfer occurred | “Kept existing file” / “Skipped by your conflict policy” | Expected policy exclusion versus an error; settled work versus bytes copied. |
| Same-volume rename/move completed | “Moved” with the exact route | No claim that the logical file size was physically copied. |
| Copy completed but source removal was declined or refused | “Copied; source kept” | Both source and target remain; requested Move is only partly achieved. |
| Target/source identity guard refused a later mutation | “Kept; review required,” with the exact affected action and the actual changed, unavailable or unknown reason | A guarded non-action versus a failed copy or an approved policy skip; a refusal is not proof that a change was successfully measured. |
| Read/write failed or ended with uncertain partial output | “Failed” / “Incomplete” with known committed effects | Confirmed result, unknown remainder, retained partials and available review. |

A useful outcome record contains requested action, committed effect, source disposition, target disposition, reason category, and any uncertainty. Do not classify every Info issue as an error or every processed item as a fully completed move. Counts must remain consistent when detailed records hit a retention limit. Any later Retry action needs explicit eligibility and a new review when intent/identity changed; a blanket “Retry every skipped item” would contradict deliberate Skip decisions and can repeat already committed effects.

**Own Undo through its whole attempt.** Reserve the original action before presenting approval so another route cannot reserve it concurrently. Canceling that review releases the reservation without erasing the recorded steps. An approved attempt must be admitted to tracked mutation/shutdown ownership before its steps are considered consumed; it then publishes one result even if the original history row is hidden. Dependent temporary-name sequences keep their existing safe cancellation boundary and durable recovery protocol. A stop request does not promise rollback. Generate review and result wording from the actual inverse kind and full paths, including effects that cannot be reversed; see [FCUX-018](#fcux-018) and [FCUX-019](#fcux-019).

**Preserve the synchronization as one identifiable task.** A small group record can retain endpoints, reviewed plan revision, child IDs and aggregate state while continuing to use the current executors. “Stop this synchronization” covers all unfinished children and does not affect unrelated jobs. If child Cancel remains, name that limited scope and display pending steps. Record the policy for unexpected copy failure/cancellation before releasing later removals; distinguish those conditions from expected policy skips. Completed effects remain visible in the group result. This is not an automatic rollback requirement; see [FCUX-021](#fcux-021).

**Retain review evidence without reopening completed journals.** Cleanup can deliberately close a reviewed journal even when some partials were kept; unresolved temporary rename intents follow a different rule (`src/FileCat.App/ViewModels/MainViewModel.Interrupted.cs:173–187`; `tests/FileCat.App.Tests/InterruptedCopyCleanupTests.cs:31–61`). Preserve a bounded retained report of what was reviewed/kept, with explicit unreviewed scope when capped. Diagnostic visibility must not make a closed record runnable again. Canceling preparation before approval leaves journal completion and files untouched; stopping already started cleanup reports the effects that occurred.

### 7.3 Define selection, identity and navigation ownership separately

| State | Meaning | Ownership / invalidation rule |
| --- | --- | --- |
| Focus | Current keyboard item. | Belongs to one tab/listing; preserve item identity across explicit sort where possible. |
| Marks | Explicit batch selection. | Belongs to the listing/model; filtering may hide marks without deleting them. Scope review must disclose them. |
| Range anchor | Item from which an extension gesture starts. | Belongs to the tab and a stable item identity. Re-resolve into the current ordering; never reuse another tab's row coordinate. |
| Gesture state | Whether a range-mark extension is active and what it is doing. | Ends or resets on documented input/focus transitions; must not remain latched after a modifier is released elsewhere. |
| Visible ordering | Current sorted/filtered index mapping. | May change without changing item identities. Row numbers are presentation coordinates, not durable identity. |
| Viewport | What the user is inspecting. | Preserve manual scroll during background refresh; reveal focus for explicit navigation/sort according to intent. |
| Navigation intent | Latest location the user requested. | A new intent supersedes older preparation/connection callbacks, including their error banners. |
| Committed location | Current successfully admitted navigation target. | Existing source/target operation captures remain independent of later panel navigation. |

Maintain raw resource identity separately from safe display text. Use the raw path/name for provider operations and intentional Copy path; use escaped safe-display components for visible names, extensions, breadcrumbs, tooltips, accessibility descriptions and review labels where invisible/control characters would otherwise conceal identity. Never mutate an actual filename merely to make it display safely. When splitting stem and extension, format both components through the same display policy, and preserve grapheme/escape integrity when eliding.

A robust regression test needs two preloaded tabs, not just two newly created tabs. A new tab's Reset can conceal anchor leakage. Similarly, a sort test must inspect visible focus after the completed reorder; asserting that the model retained the same focused store index is insufficient.

An explicit scroll after a sort request is newer viewport intent. Completion may restore the focused identity without yanking the viewport back over that later manual inspection. Track source/order, focus and viewport generations separately enough to make this choice deterministic. For clipboard Paste, an old tab object is not an immutable destination: capture its `Location` value before awaiting and apply the policy in section 7.9.

### 7.4 Use a viewport-bounded dialog contract

The owner client viewport is the source of truth for dialog size. A maximum card width of 760 DIPs cannot justify a child minimum of 720 plus labels/padding in a 640-DIP owner. Derive the available width/height after margins and reserve space for persistent header/footer before measuring the scrollable body.

Recommended structure:

1. **Header:** title, concise consequence and close behavior where appropriate. Do not make long source paths the only title.
2. **Body:** one bounded scrollable region for the active form, review or content. Lists may virtualize inside it, with an explicit ownership policy for nested scrolling.
3. **Footer:** visible primary, safe/cancel and necessary secondary actions. Group or wrap predictably; move infrequent alternatives into an accessible secondary menu where justified.

Keep actions visible at the application's supported minimum client area. If the product changes that supported minimum, make it a deliberate product decision and still support enlarged text. Simply raising every minimum width would not resolve long paths, scaling or multi-panel space pressure.

Validation belongs to the accepting command before dismissal. An invalid destination, filter, argument vector, counter or Go to value must retain the user's draft, relevant selection/caret and focus. Link the message to the field, explain the actual fault and scroll it into view. On successful correction, clear only that field's validation state. Keep provider failures, partial-listing warnings and working-set deletion semantics in their own state channels.

Initial focus must depend on invocation context. A user-invoked text prompt can focus/select its input. A spontaneously arriving operation decision must not turn a queued Enter into replacement/deletion. Explicit request metadata should determine safe default/cancel roles; button order alone is insufficient. Existing Escape cancellation remains available and should be tested separately from visible footer access.

Posted focus callbacks need session ownership at execution time. Check that the intended dialog remains current, the focus target is attached/enabled/visible, and no newer dialog or deliberate user interaction superseded it. Closing an inner dialog must not focus content behind an outer one. Preserve the actual invoking field/caret where meaningful, while respecting a later deliberate focus move. These are robustness requirements for the redesigned contract; the audit did not reproduce every native focus race.

### 7.5 Treat content inspection as a complete control contract

File lists, text viewers, hex views and comparisons all draw efficiently, but they must expose more than their first visible pixels. Define the following for each custom surface before adding isolated scrollbars or tooltips:

| Contract | Required behavior |
| --- | --- |
| Geometry | One mapping among source identity/offset, logical row/column, visual layout and viewport offsets. Rendering, hit testing, selection, caret, inline editing and accessibility use the same mapping. |
| Reachability | Every supported column, line suffix, byte and changed span is inspectable at the selected font/viewport. Wrap, adaptive rows and horizontal scrolling have explicit modes. |
| Reveal | Go to, next match, next difference and cursor movement bring the relevant position into view on both axes. |
| Selection | Selection retains exact source meaning across wrap, resize, zoom and comparison gaps. Copy behavior is documented and excludes UI line numbers by default. |
| Full text | Ellipsized labels have direct full-text inspection; source-content ellipsis has an explicit detail path until replaced by full navigation. |
| Accessibility | Stable container identity plus lazily exposed actionable items/ranges and state transitions. Visual loading/error states have matching semantic representation. |
| Performance | Virtualization and bounded metadata/I/O remain intact. Avoid constructing a million visual controls or querying files synchronously during accessibility enumeration. |
| Lifecycle | Theme/resource updates redraw without losing content state; stale asynchronous work cannot publish into another file or selection. |

For hex adaptation, derive bytes per row from available width and font metrics, then update row count, cursor navigation, offset labels and hit testing consistently. Alternatively provide horizontal access with a pinned offset column. A font-size preference should not be “fixed” by silently shrinking the font until bytes fit; that defeats the user's readability choice.

For two-sided text comparison, synchronized horizontal offsets and aligned wrapping need deliberate row-height mapping. Difference navigation should reveal the earliest changed span of the selected difference. Keep exact-byte versus decoded-text equality, encoding/BOM/newline differences and approximate-match qualifiers from the existing implementation. Selection/search are useful inspection actions; adding a merge editor is outside the minimum change required by these findings.

**Preserve source meaning through display transformations.** Store selection against the loaded source revision and original byte/decoded-text positions with a mapping for each needed visible segment. Tab expansion, control-picture substitution, wrapping and bounded giant-line segmentation can change display coordinates; they must not reinterpret the selected source. Build the mapping from decoder consumption/source spans and layout, rather than re-encoding decoded text to infer original byte offsets. Even unsanitized decoded text can contain replacement characters: UTF-8 bytes `41 80 42` can decode to `A�B`; B begins at original byte 2, although re-encoding the decoded prefix `A�` yields four bytes. The source has an existing decoder-consumption precedent in `src/FileCat.Core/Content/ContentSearch.cs:90–131`. Character Copy follows the selected decoder's policy; a byte export reads the original source bytes. Define boundary behavior for multibyte characters, malformed sequences and an encoding change. A source replacement invalidates an old selection unless an explicit correspondence is retained. Clearing selection alone is not a complete fix when the next Copy silently falls back to the entire visible viewport.

**Copy has two independent promises: representation and extent.** Source text and displayed text may both be useful, but the action/feedback must say which is copied. Preserve original logical line boundaries for source Copy; wrapping and internal 16 KiB segments are not source newlines. Preserve byte/copy budgets and disclose requested versus copied amount whenever capped or short-read. ReportWindow's original-string Copy all and the editor/comparison's explicit partial-byte notices are existing precedents, not missing features ([FCUX-076](#fcux-076)).

**Publish semantic state from the current model.** Text/hex viewers already have useful named read-only value peers (`src/FileCat.App/Controls/ViewerAutomation.cs:7–55`). A richer implementation should publish committed content, selection, side/range and editable-state changes without depending on a previous paint buffer. Coalesce updates by source/view generation and notify after asynchronous data becomes available. Native patterns must match actual capabilities; declaring an editable value must not make a descriptive byte-cursor string a whole-file overwrite interface. Confirm the pinned Avalonia/backend before choosing APIs [R12, R13].

Preserve Commander keyboard behavior when adding focus routes: list Tab switches panels and hex-editor Tab changes typing column today. Breadcrumb/resize mode should have an explicit entry/exit and sensible group navigation. The full-content journey must reach search, error/recovery actions and the toolbar, but a new Tab stop for every ancestor/byte is not required. Complete marked-selection inspection must remain lazy/bounded, including marks hidden by filters; a visible-peer window alone cannot represent that entire scope.

### 7.6 Establish a coherent visual hierarchy and theme-state matrix

The existing commander layout can support clearer hierarchy while remaining dense:

| Layer | Give highest emphasis to | Keep secondary |
| --- | --- | --- |
| Application | Current workspace and primary global actions. | Full command catalog, help and infrequent administration tools. |
| Panel | Current location, active/source and target role, current tab identity. | Optional Places shortcuts, free-space detail and less-used panel options. |
| Listing/content | Focused row, marked scope, readable names and decisive metadata/content. | Decorative surfaces and nonessential columns. |
| Operation attention | Decisions required, failure/incomplete state and concrete recovery action. | Detailed throughput graphs until the user expands them. |
| Form/review | The choice being made, affected identity, draft value and correction message. | Syntax manuals and implementation explanations. |

Keep Compact as a supported configuration. A Comfortable option should change a coherent set of typography, row height, padding and target metrics rather than enlarging only file-list text. At narrow panel widths, move secondary header detail into an accessible overflow or second row before allowing the location field to collapse. Use actual workspace bounds after visible toolbars/drawers are measured.

A theme needs paired semantic colors for text-on-surface, selected text-on-selection, text-on-danger, changed content, search hit and focus. Compute contrast for the rendered background, including transparency where used. This report adopts **4.5:1 for normal-sized essential text** and **3:1 for necessary nontext boundaries/state indicators** as design acceptance targets based on W3C guidance [R4, R5]; these are desktop design benchmarks here, not a formal web conformance assertion. Large-text and inactive-control exceptions do not justify low contrast for enabled destructive labels or selectable file names.

Keep state information redundant. Mark stripes, weight, focus outlines and target labels already help. Add equivalent cues for text differences; a red/green fill alone should not be the only way to locate changed characters. Decorative color drift and glitch layers must not alter displayed file text, byte grids or image pixels. Measure the whole animation cycle, not only a favorable static frame.

### 7.7 Make search, previews and notifications describe their true scope

| Situation | Recommended visible message shape | Persistent detail / action |
| --- | --- | --- |
| Local Find invoked from an unsupported provider | “Search local folders” plus “Current location cannot be searched by this tool.” | Visible chosen root and an explicit supported-location choice; do not silently imply a search of the current remote/archive location. |
| Find completes with unreadable/skipped locations | “Finished with warnings — N locations not searched.” | Review log action and counts kept separate from a long current-folder string. |
| Complete search has zero matches | “No matches in [scope] with [criteria].” | Edit criteria, broaden scope or clear the filter. |
| Incomplete search has zero matches | “No matches found in the portion searched.” | Explain stopped/unreadable/bounded scope before suggesting absence from the whole source. |
| Backward viewer search exhausts the full preceding range | “No earlier match.” | Direction, pattern and defined range remain inspectable. |
| A search is deliberately bounded | “No match in bytes/lines [range].” | Explicit bound and a route to search the remaining range. |
| Preview is waiting | “Loading preview for [file]…” after the chosen no-flicker delay. | Current request ownership and Stop/Open full viewer as supported. |
| Preview cannot decode or exceeds automatic-preview policy | “Preview unavailable” with a concise actual reason. | Open full viewer (F3) or a supported alternative. Hex fallback should identify itself. |
| Source changed while a snapshot is displayed | “File changed — showing the earlier snapshot.” | Refresh action; clear stale state only after a successful current revision loads. |
| Actionable failure notification | Concrete failed action and reason. | Accessible recent-message/detail route or persistent dismissal; optional action tied to the correct workflow. |

The quoted copy is proposed language. Final wording should reflect actual available metadata and capabilities. Do not show exact counts that the underlying process does not know. Do not label a partial or canceled check as complete merely because the worker is no longer running.

Represent notifications as coherent values with identity, severity, time and optional action, so repeated identical messages and info/error transitions behave intentionally. Announce new status without unnecessarily moving focus, using native-accessible semantics appropriate to the control [R1, R6]. Preserve the existing Escape priority. Clicking or selecting the message text should not erase the only user-facing explanation.

A persistent history does not require announcing every duplicate background failure. Coalesce semantically identical bursts, retain their occurrence count and detail, and announce meaningful state/action changes. Avoid an automatic focus move to a notification; provide an explicit close/review action with an appropriate accessible name. Feedback for one source or operation must not replace another owner's current status merely because an older request finishes later.

### 7.8 Use settings drafts and truthful persistence outcomes

Settings should edit a typed draft. Previewed themes can remain transactional as they already are; Cancel must restore them. Parse raw configuration only when needed, retain the original structured values for untouched fields, and validate before committing. A theme change should not rewrite the semantic structure of editor arguments or associations.

Preserve every supported field of a definition, including fields that a particular form does not expose. `ToolDefinition` includes working directory, shell mode and other launch/menu metadata; reconstruction from a smaller displayed subset must not reset them (`src/FileCat.Core/State/StateModels.cs:8–24`). Harden a shared parser before extending its use: report unmatched quotes and excess columns, retain empty arguments, and quote leading `#` command/group names so a later save cannot reinterpret them as comments (`src/FileCat.Core/Tools/UserCommandsText.cs:27–46,54–168`). Parsing failure must return a structured row/field error and leave the whole draft uncommitted.

Structured editors should cover the frequent tasks: add/duplicate/remove a filter or tool, choose a program path, edit arguments, reorder associations and inspect a nonexecuting invocation preview. Preserve an Advanced text mode for expert users, with a documented lossless grammar and row/field error locations. An argument preview should show the actual argument vector, including an empty argument and a spaced path, rather than another ambiguous joined string.

Return a save outcome to the dialog: saved, applied only for this session, protected by newer schema, oversized, unavailable, or failed as appropriate to existing state status. Do not present a reason unrelated to the actual load/persistence state. Keep protections against overwriting unreadable/newer settings intact. If settings cannot persist, the accepting action should say so before the user uses it.

Dependent controls should retain their stored values while disabled. A subordinate option can be inactive because its parent is off; that does not mean the user's preference should be erased. Profile/column list actions should expose boundary/limit reasons and update availability as the draft changes.

### 7.9 Keep clipboard, export and drop intent coherent

The relevant defects are [FCUX-069](#fcux-069), [FCUX-070](#fcux-070), [FCUX-071](#fcux-071) and [FCUX-075](#fcux-075). The common requirement is to capture and account for the operation the user actually invoked while asynchronous platform data and navigation can change.

| Boundary | Required captured state or check | User-visible result |
| --- | --- | --- |
| Copy/Cut publication | Captured original selection; provider-aware identities; every admitted/excluded item; current action and a publication/ownership token where supported. | The clipboard represents the stated scope. A refused export preserves the previous clipboard; a partial export is a deliberate disclosed choice. |
| Paste invocation before awaiting | Immutable destination `Location`, tab/panel identity, invocation lifetime, and enough clipboard generation context to obtain one coherent publication. | Later navigation cannot redirect the operation to a different destination. A vanished/ineligible target produces an explicit outcome. |
| Clipboard read completion | Payload, transfer action and ownership refer to the same publication; mismatched/superseded reads are discarded or explicitly retried. | A stale Cut does not turn the current Copy into Move. Paths alone are not evidence of current Cut intent. |
| Mutation admission and completion | One admitted transfer owns the captured scope. Completion may update only its own still-current clipboard publication/state. | Partial/failed moves explain remaining originals. An older job cannot clear or relabel a newer clipboard operation. |
| Text-path Paste | Capture the relative-path base and navigation generation separately from file-transfer intent. | An old text read cannot replace a newer deliberate navigation. Relative paths are resolved from the documented captured base. |
| Drag export | Preflight the full captured selection and preserve provider/staging budgets and origin metadata. | Local-plus-archive selection is handled or refused coherently, without silently dropping one provider's items. |
| Drag over and Drop | Compute supported effects from the displayed destination and payload; revalidate at Drop. | An unsupported archive/Registry/result-set target does not advertise an accepted Copy/Move then silently do nothing. |

When a backend cannot atomically expose all clipboard formats, use a generation/ownership check around the reads or another supported coherent-snapshot mechanism. Do not combine file data from one publication with a remembered action from another. The precise API is a platform integration decision to verify against the pinned backend. Windows has explicit preferred/performed transfer-effect formats [R11]; their existence does not establish what Avalonia currently transports or justify hand-coding the same protocol for every OS.

Use the actual provider/filesystem identity policy rather than one global case-insensitive path set. If transfer intent cannot be established, use the documented non-moving fallback or explain why the requested transfer cannot be completed. Preserve ordinary direct Copy behavior. After a partial move, show what remains and provide a deliberate supported next action; automatic replay of every original source is not part of this correction.

Drop feedback must be cheap enough for pointer movement. Use cached/known capabilities during hover, with conservative effects when capability is unknown; perform authoritative validation at admission. Do not trigger repeated network calls, staging or modal prompts on every DragOver event.

<a id="verification"></a>

## 8. Verification plan and acceptance fixtures

The following work is **proposed verification**, not a record of tests executed during the audit. Use disposable fixture data and an isolated test profile. Existing regression tests should be run in the implementation environment; this review inspected their source only.

### Verification gates

| Gate | Appropriate evidence | What that evidence cannot establish |
| --- | --- | --- |
| **1 — Deterministic semantics** | Exact source/destination manifests, job kind, inverse admission count, preserved argument vectors, returned match offsets, copied characters/bytes, and mapped selection identities. Use controllable clipboard/provider tasks for asynchronous races. | Native clipping, pointer/focus timing, OS exchange behavior or spoken output. |
| **2 — Component state and layout** | Measured bounds; visible/enabled controls; source/view/review ownership; selection after reflow; event payloads before/after committed content; draft retention. | Every platform's font/backend behavior or actual assistive-technology interpretation. |
| **3 — Native user journey** | Keyboard/pointer completion at recorded viewport/scale, clipboard exchange with the supported OS file manager, live theme/motion behavior and accessibility inspection plus spoken interaction. | Unexercised parser, large-file or race correctness merely because one screenshot or happy path looks right. |

Use the smallest meaningful test that resolves the remaining risk. A getter that becomes correct after rendering does not verify the event payload sent before rendering. A parser test comparing only item count does not prove that `WorkingDirectory` survived. A visible selection rectangle does not prove that Copy returned the same source text. Run the relevant existing lifetime and mutation-guard tests alongside the new scenario, then stop broadening verification when the required gates and remaining risk are covered.

### 8.1 Minimum environment matrix

| Dimension | Cases | Evidence to record |
| --- | --- | --- |
| Build | Exact current source revision, pinned .NET/Avalonia versions, platform backend. | Commit/build identifier, dependency lock/version and test runner. |
| Operating system | Windows; the supported Linux desktop/backend(s); macOS where shipped. | OS/version, window manager and any platform-specific limitation. |
| Size | Main minimum 640×420; 800×600; default layout; maximized; two/four/six panels. | Client DIPs and actual measured workspace area, not only screenshot pixels. |
| Scale/readability | 100%, 150%, 200% OS scale; default and enlarged UI/content fonts. | Physical scale and logical dimensions separately. |
| Themes | Classic, Classic Dark, System light/dark, High Contrast, DOS Commander and all expressive palettes. | Normal/focus/selection/danger/search/diff states; before/after live theme change. |
| Input | Keyboard only, conventional mouse, precision wheel/touchpad, modifier release outside the window. | Exact sequence and resulting focus/marks/viewport. |
| Assistive technology | Windows inspector plus NVDA/Narrator; VoiceOver and Orca on supported native hosts. | Names, roles, child items, selected set, focus events, state announcements and ability to complete the workflow. |
| Resource providers | Local disk, read-only/denied location, archive, result/working set, Registry on Windows, delayed/disconnected remote source. | Advertised capabilities, captured scope and actual visible feedback. |
| Resource size | Empty/parent-only, 10, 1,000, 20,000 and synthetic million-item listings; large files and long lines. | Interaction latency, bounded allocations and actual content reachability. |

Do not demand exhaustive Cartesian execution of every row. Use pairwise coverage for general layout and focused combinations that trigger each risk: long filenames plus small dialogs; DOS plus selected viewer text; loaded unequal tabs plus Shift-click; remote delay plus a newer navigation; protected settings plus an unrelated theme change.

### 8.2 Controlled fixture catalog

| Fixture | Construction | Required observation | Findings |
| --- | --- | --- | --- |
| A — Interrupted filtered transfer | Folder with small JPG and TXT files, filtered move, fixture-generated interrupted journal. | Recovered intent still excludes TXT; legacy missing intent requires a new review. | [FCUX-001](#fcux-001) |
| B — Interrupted destination mapping | Renamed single-file copy and a result set with two relative subfolders. | Original destination name and hierarchy/flatten intent are preserved or explicitly unrecoverable. | [FCUX-001](#fcux-001) |
| C — Anchor ownership | Two already-loaded tabs with roughly 100 versus 10 entries; click late row in A, Shift-click early row in B. | No range derives from A's integer coordinate. Repeat after sorting/filtering/deleting the anchor. | [FCUX-002](#fcux-002) |
| D — Hidden bulk marks | Mark a mixed-extension set, filter visible rows, open bulk rename. | Visible hidden-mark disclosure and scope control; count, preview and final names all match the selected scope. | [FCUX-003](#fcux-003) |
| E — Delayed decision | Hold a conflict/metadata-loss decision until another editable prompt is active and Enter is queued. | The queued key cannot approve a newly arrived consequential decision. | [FCUX-004](#fcux-004) |
| F — Ambiguous sync endpoints | `C:\Work\Project` versus `E:\Backup\Project`, plus long remote/local names. | Source/target and deletion endpoint remain distinguishable in the active review. | [FCUX-005](#fcux-005) |
| G — Lossless tool configuration | Arguments with spaces, empty strings, quotes, Unicode and `a\|b`; masks/exclusions; nondefault WorkingDirectory/ShellMode; quoted `#Build` command/group; unmatched quotes and a fifth column. | Open/OK and theme-only edit preserve complete supported tool semantics and order. Malformed edits remain in the same session with exact field/row errors and no partial commit. | [FCUX-006](#fcux-006), [FCUX-034](#fcux-034), [FCUX-036](#fcux-036) |
| H — Backward search boundary | A file over 12 MiB with distant/chunk-boundary matches, plus small UTF-16LE/BE files with known earlier matches at even offsets. Exercise viewer and Latin-1 editor callers separately. | Return the nearest earlier match over the complete declared range; every iteration advances or ends; an explicit timeout catches repeated-hit loops. Verify overlap policy, changed query/manual navigation and cancellation ownership. | [FCUX-007](#fcux-007) |
| I — Checksum scope/completeness | Known short byte sequence, explicit one-byte selection, no selection and a source that ends early. | Hash scope/count is exact; full hash and incomplete warning are visible; clipboard changes only on Copy. | [FCUX-008](#fcux-008) |
| J — Decisive long suffix | Lines of 60, 80, 200 and 10,000 characters differing only at the final character. | Difference navigation reveals the changed span; keyboard and pointer can inspect/copy it. | [FCUX-009](#fcux-009), [FCUX-056](#fcux-056) |
| K — Small dialog | Minimum owner size, longest relevant button/path text, bulk rename and invalid Settings data. | Header/footer remain usable, focused field scrolls into view, correction does not close/discard the draft. | [FCUX-011](#fcux-011), [FCUX-034](#fcux-034), [FCUX-041](#fcux-041), [FCUX-054](#fcux-054) |
| L — Live issues | Select a running job, then append warnings/failures and complete it. | Same selected report updates; final counts and rows agree without toggling selection. | [FCUX-015](#fcux-015), [FCUX-017](#fcux-017) |
| M — Undo evidence | More than 30 results, including success, kept/changed destination and failed rollback. | Full outcome remains inspectable; Undo retirement is explicit and cannot hide failures beyond the visible preview. | [FCUX-018](#fcux-018), [FCUX-019](#fcux-019) |
| N — Navigation supersession | Held provider preparation A; navigate/back to B; complete or fail A. | B's location, banner, focus, marks and history stay current. | [FCUX-022](#fcux-022), [FCUX-070](#fcux-070) |
| O — Capacity identity | Two Unix mounts and two Windows shares on one server with different reported capacity. | Each destination shows its own fresh value; explicit refresh invalidates the appropriate cache. | [FCUX-023](#fcux-023) |
| P — Workspace recovery | Four panels/40 supported tabs with distinct targets and proportions; switch workspaces. | Restore previous workspace recreates supported structure independent of RecentlyClosed capacity. | [FCUX-024](#fcux-024), [FCUX-025](#fcux-025) |
| Q — Validation ownership | Invalid filter then correction; clear filter in a working set or while an unrelated warning exists. | Draft/focus retained, filter error clears on repair, unrelated persistent context remains. | [FCUX-028](#fcux-028) |
| R — Live theme state | Keep Text/Info/Hex/editor/quick-view controls open while changing palettes/high contrast. | Every rendered surface changes consistently without losing selection/edit state; no event subscription leak. | [FCUX-013](#fcux-013), [FCUX-049](#fcux-049), [FCUX-050](#fcux-050), [FCUX-051](#fcux-051) |
| S — Fractional scroll | Ten events of 0.1, one of 1.0, reverse direction and one visible row across FileListControl, TextViewer, HexView and HexCompareView. | Equivalent aggregate movement under the chosen device policy, retained remainder, and nonzero keyboard page movement. Preserve the binary comparison's existing one-row minimum. | [FCUX-047](#fcux-047) |
| T — Protected settings | Newer-schema, oversized and unavailable profile fixtures. | Correct reason and explicit persistent/session-only outcome; protected input remains untouched. | [FCUX-035](#fcux-035) |
| U — Clipboard publication | Cut A; externally Copy A. Repeat Cut A+B then Copy only A, intervening text, case-distinct paths, restart and partial failure. Use a fake clipboard with controllable generations. | Submitted Copy/Move follows the current coherent publication; manifests and original-file presence match it. Native exchange is a separate gate. | [FCUX-069](#fcux-069) |
| V — Delayed Paste ownership | Hold file/text clipboard reads; navigate the original tab from A to B, switch panels, close the tab, or issue a newer navigation before releasing the read. | File Paste uses captured A or explicitly cancels, never silently B. Text navigation cannot supersede a newer navigation and uses its captured relative base. | [FCUX-070](#fcux-070) |
| W — Mixed selection export | Local-only, archive-only, local+archive, local+remote, unsupported-only and failed storage-item conversion; include a hidden mark. | Every requested item is exported or explicitly excluded/refused. No silent subset; refused publication preserves the old clipboard; staging limits remain enforced. | [FCUX-071](#fcux-071) |
| X — Keyboard binary range | Select four right-side bytes, then left-side bytes, across a row boundary and aligned comparison gap without a pointer. | Side, byte offsets, selection extent, Copy output and semantic description agree; difference navigation and copy limits remain correct. | [FCUX-072](#fcux-072) |
| Y — Current semantic content | Record events and getters around a scroll, delayed first page, selection, source replacement and stale load; toggle editor hex/text mode and pending nibble. | Events describe the committed current view, reveal loading completion, and expose meaningful selection/edit state without a repaint flood. Native speech is verified separately. | [FCUX-073](#fcux-073) |
| Z — Command-search Help | Enter a no-results query; invoke the advertised Help binding; close Help; repeat after remapping the binding. | The advertised action opens, typing-field safety remains, and the query/selection/focus return predictably. | [FCUX-074](#fcux-074) |
| AA — Drop capability | Hover/drop file payloads over local, archive, Registry, result-set and disconnected targets; change target capability between hover and drop. | Advertised effect and accepted operation agree; authoritative refusal is explained; valid local review and captured destination remain intact. | [FCUX-075](#fcux-075) |
| AB — Copy representation and bound | Tab/NUL/control text; malformed UTF-8 `41 80 42`; CRLF/LF/no-final-newline; a 40,000-character logical line across a 16 KiB segment; Hex selection 1 MiB+1 and 2 MiB; deliberate short read. | Source/display label matches exact copied content and decoder policy; byte export retains original bytes; layout adds no source newline; capped and unread portions are explicit. Existing lifetime cancellation holds. | [FCUX-076](#fcux-076) |
| AC — Selection through reflow | Select a unique substring, then resize, change font or toggle Wrap; repeat through a tab expansion/giant-line segment, after malformed UTF-8 bytes, and after replacing the source. | Reflow preserves source endpoints and Copy result, including B at byte 2 in `41 80 42`. Source replacement invalidates old selection deliberately without an unintended viewport Copy. | [FCUX-012](#fcux-012) |
| AD — Owned inverse attempt | Hold an approved slow Undo; request it again; close the app; hide/clear the original row. Include dependent rename, created link/hard-link, and same-volume replacement. | One owned attempt, correct close policy, safe rename recovery/cancel boundary, one retained result, exact inverse paths and truthful irreversible remainder. | [FCUX-018](#fcux-018), [FCUX-019](#fcux-019) |
| AE — Synchronization lifecycle | Approve new copies plus target-only removals; stop the group or fail a transfer with later children pending. | No unstarted child escapes group Stop; unrelated jobs continue; pending removals follow the declared failure policy; aggregate result includes committed work. | [FCUX-021](#fcux-021) |
| AF — Recovery conflict distinctions | Existing file, hard-link alias, case-only move, existing directory with missing descendants, and interrupted cross-volume move with both copies present. | Recovery Skip remains conservative and visible; same-item/type decisions and descendant handling remain accurate; existence alone does not authorize source deletion. | [FCUX-001](#fcux-001) |
| AG — Effect-aware results | Policy Skip, same-volume rename, metadata-loss Keep source, source-deletion guard refusal and a detail-retention cap. | Physical throughput, settled progress, expected skips, copied-but-kept sources and errors remain distinguishable; no incomplete Move is labeled fully moved. | [FCUX-016](#fcux-016), [FCUX-017](#fcux-017) |
| AH — Review revision | Change rename rules/counter scope, sort only the preview, reverse sync direction, exclude/re-include a deletion, then introduce a destination conflict. | Display changes preserve mapping; semantic changes invalidate only relevant consent; final execution guards remain authoritative. | [FCUX-003](#fcux-003), [FCUX-021](#fcux-021), [FCUX-038](#fcux-038), [FCUX-039](#fcux-039) |
| AI — Queued focus ownership | Queue initial/restore callbacks; open/close nested reviews or deliberately move focus before callbacks execute. | Only the current owning session can apply focus; no focus lands behind a modal or overwrites a newer deliberate action. | [FCUX-004](#fcux-004), [FCUX-011](#fcux-011) |

The synthetic values above are acceptance fixtures. They are not claims about the user's files. Where a filename is invalid on one platform, use the supported platform/provider fixture or test the formatter independently and then validate its integration with representable names.


### 8.3 Native accessibility protocol

Start with one two-panel scenario and record the actual automation tree. The list container should identify its location/role stably; focused and marked items should be distinguishable. Inspect row names, bounds, item actions, selection retrieval and scrolling without causing synchronous I/O. Confirm that a failed/loading list is not announced as a genuinely empty folder.

Then complete a keyboard/screen-reader journey: navigate to a folder, inspect a long filename/extension, mark several files, hide one mark with a filter, review the scope, cancel the operation, switch panels, inspect a background error and return to the prior row. Include an empty folder's location actions and an ancestor breadcrumb action. Record what was spoken and which actions were available, not only whether a name string existed in the peer.

Add a content journey: read an asynchronously loaded page, scroll once, select a range, change font/width, copy it, and inspect the exact clipboard output. In binary comparison, choose the right side and a short exact range; in the hex editor, identify active typing column, selection, pending nibble and unsaved/blocked state. Compare event payloads with the current visible model before judging native speech. Complete-selection retrieval must account for hidden marks without forcing an unbounded synchronous traversal.

Avalonia exposes platform-specific accessibility backends; a passing Windows inspector test does not qualify macOS/Linux behavior [R1]. On Windows, List and ListItem patterns should follow their actual control-type contracts [R2, R3]. If tabular semantics require DataGrid-style behavior, choose that role deliberately; do not attach incompatible provider patterns to a List merely because the surface draws columns.

### 8.4 Visual QA protocol

Capture the full window and the affected control at known client dimensions and scale. For source-content reachability, assert the actual changed span/caret/byte lies within the viewport; a screenshot containing an ellipsis and a model that still owns the complete string is not a pass. For overlays, verify the footer and focused element remain visible after validation messages wrap.

Recompute opaque palette pairs from source tokens and inspect composed/translucent states in renders. Include normal, hover, pressed, selected, inactive focus and disabled cases separately. In expressive themes, sample bright/dark positions and glitches through a complete cycle; ensure work content itself remains unaltered. Check grayscale/noncolor interpretation for text differences and selection, alongside the existing redundant cues.

Do not infer contrast numerically from compressed JPEG pixels when exact opaque palette tokens are available. The audit's table is calculated from those tokens. Native rendering still determines effective styling overrides, alpha composition and platform differences.

### 8.5 Definition of done for a finding

A finding is ready to close when the implementer can provide:

1. The specific behavior change and affected source symbols, linked to the finding ID.
2. A before/after fixture or manual sequence demonstrating the original risk and corrected visible outcome.
3. Confirmation that relevant existing safeguards still work, especially scope capture, source revision, cancellation, recovery ownership and persistent Commander marks.
4. Appropriate automated evidence for deterministic model/binding/geometry behavior, plus native evidence for focus, layout, device input or assistive technology where required.
5. The exact remaining limitation, if any, visible to the user and documented without an unqualified success claim.
6. For a staged fix, the chosen minimum correction, the larger refinement deliberately deferred, and evidence that the alternative route meets the affected user's needs.

Do not write tests that merely restate assigned constants or the implementation's internal sequence. Test the user-visible invariant: which files were selected, which bytes were searched/hashed, which path was approved, whether the complete result can be inspected, and whether a person can finish the task through the supported input route.

## 9. Coverage, strengths and boundaries

### 9.1 Coverage map

| Surface / workflow reviewed | Primary evidence inspected | Resulting focus |
| --- | --- | --- |
| Main window, toolbar, menus, panel layout and tabs | MainWindow XAML/code, layout partials, MainMenuModel, PanelView and panel/workspace view models; supplied theme captures. | Command availability/hierarchy, usable space, keyboard resizing, identity and state restoration. |
| File listing, marks, filters, paths and accessibility | FileListControl, CellText, Columns, PathLine, ListingModel, TabViewModel; relevant accessibility/list/path/input tests. | Identity/anchor correctness, reachable content, semantic peers, validation and input continuity. |
| Clipboard, drag export and drop admission | MainViewModel.Operations clipboard/DropFiles branches, MainWindow drag handlers, result-set identities, DragStaging and scope tests. | Current transfer intent, immutable destination, full-selection accounting and honest capability feedback. |
| Transfers, delete/conflicts, bulk rename, operation center and recovery | OperationDialogs, OverlayDialogService, operation/interrupted view models, Job/JobJournal/Executors; operation/recovery tests. | Captured scope, safe decisions, recoverable intent, reports, progress and Undo. |
| Compare directories and synchronize | DirectoryDiffWindow, SyncWindow, MainViewModel.Tools, Compare/Sync, JobManager and relevant plan/execution safeguards. | Endpoint identity, plan changes, exclusions, consent, child/group cancellation and aggregate outcome. |
| Find and working/result sets | FindWindow, FindFiles entry point, command dispatch and status/result-set handling. | Search-root expectations, completeness visibility, context shortcuts and contextual actions. |
| Text, Info, hex, picture, comparison and quick view | ViewerWindow, HexEditorWindow, CompareWindow, TextViewer/HexView/HexCompareView/PictureView/QuickViewPane and lifetime/revision tests. | Content reachability, result truthfulness, copy/search, refresh, zoom, loading and theme lifecycle. |
| Settings, tool configuration, themes and support | SettingsDialog, Associations/UserCommandsText, ColumnProfilesEditor, ThemeManager/ThemeAnimation/ThemeEffects, AboutDialog and state saving. | Lossless editing, validation, persistence reason, contrast/motion, hierarchy and precise copy. |

This was a broad UX/UI implementation audit, not a complete correctness review of every provider, filesystem operation, image codec, network protocol, shell extension, archive format or recovery engine. No penetration test, performance benchmark, installation qualification or real-user usability study was performed. Areas with no listed finding should not be interpreted as certified defect-free.

### 9.2 Existing strengths to preserve

- **Explicit source/target operation scope.** Existing operation-scope tests cover captured selection/destination behavior. Later panel navigation should continue to leave submitted jobs' scope intact. See `tests/FileCat.App.Tests/OperationScopeTests.cs` and `src/FileCat.App/ViewModels/MainViewModel.Operations.cs`.
- **Substantial recovery and mutation guards.** Source/content reviews, partial-file ownership, changed-file checks, bounded recovery admission and in-use journal protection are important safeguards. The missing recovery intent in [FCUX-001](#fcux-001) is separate from those controls.
- **Deletion and conflict protections.** Recycling classification does not silently become permanent deletion; type mismatches and same-item conflicts have distinct choices. Conflict comparison and per-operation apply-to-all scope already exist. Preserve them when changing focus/defaults.
- **Safe-boundary cancellation and exit choices.** Completed work remains meaningful after Stop; the UI should describe this accurately rather than discard the engine's careful lifecycle.
- **Verification result handling.** `src/FileCat.App/Views/VerificationResultsDialog.cs` and `src/FileCat.App/Views/ReportWindow.cs` provide useful examples of structured results, deliberate arrival handling and theme forwarding. Reuse proven behavior rather than starting a second unrelated result system.
- **Efficient listing architecture.** Drawn rows, streaming enumeration, asynchronous metadata/search and bounded work allow large listings. The accessibility and geometry fixes should preserve that architecture rather than inflate the visual tree to one heavy control per file.
- **Focus and marks have redundant cues.** The file list uses stripes/weight, focus background/outline and an inactive dashed outline. It should not be described as relying only on color. Contrast and semantic selection remain separate improvements.
- **Refresh scroll preservation.** Existing tests intentionally prevent background refresh from dragging a user back to offscreen focus. [FCUX-027](#fcux-027) requires an intent-aware sort fix, not revealing focus on every row update.
- **Path editing and completion.** Direct location editing, completion, ancestor pointer actions and keyboard navigation already exist. [FCUX-032](#fcux-032) addresses parity for segment actions rather than claiming there is no keyboard location input.
- **International quick search and request ownership.** TextInput/AltGr handling, asynchronous search sequencing and stale-request rejection are present. Rejecting an unmatched typed character is deliberate and tested; it was not treated as an implementation bug in this audit.
- **Tab overflow and locks.** An all-tabs route, active-tab visibility, close/reopen commands and distinct lock behaviors exist. The remaining issues concern identity, a no-op close path and persistence consistency.
- **Theme and tooltip infrastructure.** Semantic palettes, high-contrast precedence, reversible preview, readable primary/target foreground selection and rich tooltips have focused tests. Extend their state coverage rather than replace them wholesale.
- **Comparison evidence distinctions.** Byte equality, decoded-text equality, encoding/BOM/newline differences and approximate regions are communicated. These distinctions are valuable and must survive a new text surface.
- **Picture fidelity fundamentals.** Fit/zoom, pointer-centered zoom, panning, EXIF transforms and color profiles already have implementation and tests. The audit calls for truthful mode controls and refresh/fallback feedback, not recreation of those fundamentals.
- **Viewer/editor lifecycle and recovery.** Search/copy/checksum revision tests, worker retirement, overwrite-only editing disclosure, Undo/Redo, Save As and interrupted-save recovery are substantive. Presentation changes must retain their ownership and capability gates.
- **Existing content semantics and copy disclosure.** TextViewer/HexView have named read-only value peers; ReportWindow can copy the original complete report; HexEditor and HexCompare disclose their selected-byte limits. The new findings target stale/incomplete semantics or particular callers, not the absence of these baselines.
- **Bounded drag staging.** Existing admission, size/item limits, download-origin handling and cleanup should survive mixed-provider export changes. Partial-recovery content must not be exported as if it were verified complete data.
- **Useful support facts.** About already exposes version/commit, environment/profile paths and selectable details. Small action-label improvements should preserve that practical support value.

### 9.3 Product work that is not required to resolve these findings

The findings do not require a new cloud backend, a full merge editor, a replacement desktop toolkit, a generic web-style file manager, universal remote content search, unlimited undo/version history, or removal of expressive themes. Where a capability is absent or bounded by design, an honest scope explanation and a clear supported alternative can be the appropriate UX fix.

Optional future research could study onboarding, terminology comprehension, toolbar frequency and preferred density with both experienced Commander users and newcomers. Such studies would help choose among the proposed hierarchy alternatives. They are not prerequisites for fixing deterministic wrong-scope, false-negative, lossy-save or inaccessible-content behavior identified in the source.

## 10. Sources and reference guidance

### 10.1 Primary implementation evidence

The supplied `FileCat(1).zip` is the primary source. Its SHA-256, implementation stack and screenshot provenance are recorded in section 1. Source paths and line ranges in each finding point into this archive. They are locators rather than line-stable APIs; use the accompanying symbol names after code moves.

Visual evidence comes from the actual JPEGs in `docs/screenshots/windows/`, particularly `docs/screenshots/windows/Classic.jpg`, `docs/screenshots/windows/ClassicDark.jpg`, `docs/screenshots/windows/System.jpg`, `docs/screenshots/windows/HighContrast.jpg`, `docs/screenshots/windows/DosCommander.jpg` and `docs/screenshots/windows/Psychedelic.jpg`. `docs/screenshots/windows/CAPTURE.md` describes how and when they were captured. No new application screenshot was produced in this audit environment.

Tests cited in the findings establish what existing source intends to cover, not a pass result. The report deliberately distinguishes inspected tests from proposed regression work and from native qualification still to be done.

### 10.2 External primary references

These references informed specific design/acceptance principles. The report's diagnoses come from the supplied implementation. Public documentation can evolve; verify exact APIs against the pinned Avalonia version before implementing a control pattern.

| Ref | Primary source | Relevance |
| --- | --- | --- |
| **R1** | [Avalonia — Accessibility](https://docs.avaloniaui.net/docs/app-development/accessibility) | Automation peers, accessible properties, live state and native platform backends; custom controls require deliberate semantics. |
| **R2** | [Microsoft — UI Automation Support for the List Control Type](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-supportlistcontroltype) | Stable list role/name, item descendants and the supported selection/scroll pattern contract. |
| **R3** | [Microsoft — UI Automation Support for the ListItem Control Type](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-supportlistitemcontroltype) | Item-level identity, state and supported actions within an accessible list. |
| **R4** | [W3C — Understanding Contrast (Minimum)](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html) | The normal-text 4.5:1 design target and contrast calculation basis. Used here as a desktop readability benchmark. |
| **R5** | [W3C — Understanding Non-text Contrast](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html) | Contrast for necessary control/state visuals against adjacent colors; assess the actual state and background. |
| **R6** | [W3C — Understanding Status Messages](https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html) | Making important status changes programmatically available without requiring a focus move. |
| **R7** | [W3C — Understanding Keyboard](https://www.w3.org/WAI/WCAG22/Understanding/keyboard.html) | Equivalent keyboard functionality, including relevant movement/resizing operations. It does not require every visible control to be a separate Tab stop. |
| **R8** | [W3C — Understanding Use of Color](https://www.w3.org/WAI/WCAG22/Understanding/use-of-color.html) | Redundant visual cues for differences and state; color alone should not carry necessary meaning. |
| **R9** | [Microsoft — Command bar](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/command-bar) | Deliberate primary versus secondary commands, labels and overflow as useful desktop design patterns. FileCat need not adopt a Windows-only implementation. |
| **R10** | [Microsoft — Dialog controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs) | Explicit dialog actions/default behavior and focused decision design. The safe background-arrival policy is this audit's recommendation based on FileCat's wiring. |
| **R11** | [Microsoft — Shell Clipboard Formats](https://learn.microsoft.com/en-us/windows/win32/shell/clipboard) | Windows distinguishes transferred file data from preferred/performed transfer effects. Verify what the actual Avalonia backend supports before choosing a publication/action exchange mechanism. |
| **R12** | [Microsoft — Document Control Type](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-supportdocumentcontroltype) | Native Windows document semantics, patterns and events. The document role alone does not establish usable content-range interaction. |
| **R13** | [Avalonia — IValueProvider](https://docs.avaloniaui.net/api/avalonia/automation/provider/ivalueprovider) | Framework orientation for the existing value-provider baseline. Verify the exact pinned API/backend; do not assume that a Windows-specific pattern has an equivalent portable provider. |

External references were consulted on 17 October 2026. New second-pass behavior claims remain grounded in the unchanged supplied source, with native consequences qualified in the associated findings.

### 10.3 Handoff conventions

Use the stable **FCUX** identifier in issue titles, patch descriptions and verification records. A practical implementation issue should include the current source revision, the finding's scenario, the chosen scope, the visible behavior change, the relevant existing safeguards, and evidence for its acceptance criteria. If investigation disproves a timing/rendering hypothesis on a particular backend, record the evidence and narrow the finding rather than silently treating source inspection as a reproduced defect.

The following compact task template can be copied into an issue or implementation brief:

```text
Finding ID and title:
Current implementation revision / platform:
User scenario and consequential failure:
Source symbols and evidence to recheck:
Chosen minimum correction and visible behavior:
Shared contracts / dependent findings:
Existing safeguards to retain:
Acceptance criteria and fixture IDs:
Evidence required: deterministic / component / native:
Deferred refinement and remaining visible limitation:
```

Use exact observed outcomes in completion notes. “Implemented a scrollbar” describes a change; “the selected right-side byte remains visible and copies the same offset after resizing” demonstrates the intended result. Link remaining refinements to the same stable finding without implying that unexecuted native checks passed.

The audit is complete as an implementation brief. It intentionally leaves product-choice details—such as the final primary toolbar membership and exact comfort preset metrics—to reviewable design decisions, while specifying the user-visible invariants that every acceptable implementation must preserve.
