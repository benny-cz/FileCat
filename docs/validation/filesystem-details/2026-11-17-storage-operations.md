# Storage Operations and review checkpoint — 2026-11-17

This continues the [executor checkpoint](2026-11-17-storage-executor.md) on `main`, from parent `68a435dc2a1d6122f5961f867bcb653b0e6968aa`. These are implementation and focused verification results, not completion of either roadmap. The product Windows transform adapter remains disabled. EFS remains required by the active user objective and is not implemented by this checkpoint.

## Changes

Storage rows now bypass the transfer estimator. Compact/history/details/taskbar projections omit percentages, ETA, rates, byte totals and speed graphs. Running activity is indeterminate; Pause/Stop/decision states have state text and zero taskbar fractions. Typed counts include successful AlreadySatisfied outcomes, incomplete/unknown scope, errors despite cancellation, and actual uncertainty/persistence causes. Stop explains that an admitted call finishes and completed changes remain. Selected current-path text and its accessibility/tooltip retain the complete escaped path.

The ordinary report captures one immutable storage result and lists all selected paths, identities, root statuses, complete reason totals, the durable sequence/digest, and bounded no-call examples separately. It explicitly says it does not contain the full native-call evidence. Storage notifications use the same typed projection, including successful no-op results.

JobManager publishes one same-session review candidate after writer/native/review ownership retires, before finished observers. Observer failures are contained. Needs review merges startup arrivals without replacing current-session entries, and clearing finished history leaves those entries intact. Storage rows reject generic cleanup, continuation, elevation, Undo and generic mark-reviewed/close routes. Their finished-row review action selects the same journal candidate. The drawer reveal path recognizes the storage review action.

The immutable history snapshot window distinguishes Summary, Call details and Examples. It retains every readable selected-root identity and the last unresolved call fact, labels absent facts and durable lower bounds, and makes no automatic crash claim. Exact call evidence can be streamed to an optional export. Core compares the complete owned journal snapshot before export and hashes all exported bytes; stale review and cancellation do not acknowledge history. The caller publishes the export only after success and removes its temporary on failure where possible.

Acknowledgment uses Core's existing exact-snapshot report/acknowledgment pair. The report explains that detailed call evidence may later be pruned and offers export first. Storage never uses the generic save/close/rewrite sequence or treats ClosureConfirmed as effect certainty. The window refuses actions until its exact snapshot is displayed. Current-session reviewed rows preserve historical uncertainty, and a late legacy startup load cannot erase a newly acknowledged storage row.

The executor also now retires both the child enumerator and held-object path when one close fails, stops further mutation, preserves observed changes, and marks ownership review pending. A close error is no longer mistaken for a parent enumeration error.

## Verification

Builds succeeded with zero warnings/errors. Tests ran on the provided disposable Windows 11 x64 VM at medium integrity, Ubuntu VM, and Apple Silicon Mac; no test suite was executed on the user's workstation.

| Selection | Windows | Ubuntu | macOS |
|---|---:|---:|---:|
| Core storage/scheduler/lifecycle/Undo/archive, 277 discovered | 276 passed / 1 existing fixture skip | 275 passed / 2 existing platform/fixture skips | 275 passed / 2 existing platform/fixture skips |
| App Operations/recovery/Undo, 164 discovered | 163 passed / 1 existing case fixture skip | 162 passed / 2 existing platform skips | 161 passed / 3 existing platform/fixture skips |
| Final StorageOperationsUiTests + OperationCenterTests, 25 discovered | 25 passed | 25 passed | 25 passed |

All selections had zero failures/errors/not-run tests. The final 25-case selection includes the startup-reviewed-row fix and three additional report/window cases after the broader 164-case run. Its 17 storage cases cover every pending/terminal display state, state-only taskbar behavior, complete severity without JobIssue samples, no-op summaries, exact escaped paths and immutable capture, recovery capabilities/merge/history clearing, unresolved/missing history facts, and a 640×420 headless report window whose actions wait for displayed evidence and remain focusable. Headless layout checks do not constitute the outstanding native UI qualification.

Core additions exercise close-failure accounting, exact export/stale-snapshot/cancellation behavior, and same-session publication after ownership retirement, including a throwing observer. The existing 37-case native compression qualification is recorded in the preceding receipt; it was not rerun for these report projections.

Transport scripts/logs are retained under `.tmp/filecat-storage-operations-*`. The qualified source archive includes all changed Core/App/platform/test source, excluding bin/obj. Final source has the reviewed-row and narrow-window additions. Windows binary patches were applied to the prepared guest test directories; Mac/Ubuntu rebuilt their prepared checkouts. Both test runners completed with explicit finish markers and successful exits.

| Artifact | SHA-256 |
|---|---|
| filecat-storage-operations-source.tar.gz | `6A653C08515F0D296F288B521229F0E2CD3E71A128C01BFF3424B7C1D0E1E9F4` |
| filecat-storage-operations-core-binaries.tar.gz | `75427FC4B72150B765209E9ADE2E96ED0A2AE6B70538171C4221FE024C4B7EE2` |
| filecat-storage-operations-ui-patch.tar.gz | `971F479BB8A849C6664946AB2C9348458AAEAF7C0597E360A539F4DE7586CF31` |
| filecat-storage-operations-final-source.tar.gz | `27FDE86AD1771ED39E739E2DC18136881E7A2378B1B509496A0A99B3716D9E96` |
| filecat-storage-operations-final-ui-patch.tar.gz | `12DBF7795B3176773362EFC2FF2508AAEAD7AB8BBDE11DB3A5DA5EA3D46D432F` |

## Remaining gates

Startup/incremental storage history is not yet integrated into MainWindow. The 64-row descriptor limit, More storage history flow, and native retained reviewed-report discovery still need App integration. The current same-session collection must receive that bound before enabling transforms. Live ordinary summaries identify the durable prefix but do not yet page/stream its exact call records through the live report. Recovery export streams the complete inactive evidence only. Fresh current-object observations remain separate work; historical IDs are never mutation authority.

The command/review dialog, revision-safe preparation/Retry/Enter, captured origin and mark-preserving refresh, full high-scale/narrow drawer native UX, and wider native/provider/device/performance/crash/retention matrix remain open. The snapshot acknowledgment UI needs a full native end-to-end qualification. EFS requires resolving the supported binding/conflict protocol, key/recovery flow, native mutation and all output-admission routes. The companion filesystem roadmap still has incomplete collectors and release gates. No entire T-REG/T-REC/T-OUT or roadmap-completion claim follows from these focused passes.
