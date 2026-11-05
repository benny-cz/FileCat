# Archive transaction journal contract checkpoint

Date: 5 November 2026. AW2 foundation only. No production write profile is enabled by this change.

## Durable facts

`ArchiveTransactionFacts` schema 1 is an immutable complete parent snapshot. Each `archive-transaction` record repeats:

- A nonempty operation nonce, contiguous sequence, explicit versioned format/options and publication phase.
- Absolute archive/stage paths, held parent identity, exclusive stage identity and optional original identity/length/SHA-256.
- Final verified stage identity/length/SHA-256, reserved retained-original path, manifest path and object version,
  and the security/provenance snapshot digest before publication is permitted.
- Actual observed published/retired object versions and up to sixteen exact unresolved-artifact paths.

The profile, original, parent and stage evidence cannot change across transitions. Verified stage, manifest, retention,
security and observed-object facts cannot be removed or changed after introduction. A committed update must reconcile
the exact original version; a different retired version cannot be declared committed. The publication must be the
verified stage object and bytes. Paths/identities remain evidence, never authority to open, publish or delete a file.

Allowed normal phases are Reserved → Rebuilding → Verifying → ReadyToPublish → Publishing → Reconciling → Cleaning →
Committed. A known no-effect failure can record NotCommitted. A reconciled restoration can record RolledBack. Neither
is inferred from an exception alone. Unknown outcomes record NeedsReview; successful publication with retained cleanup
artifacts records CommittedWithRetainedArtifacts. Both remain unresolved. A native archive-specific resolution protocol
is still required; the generic “mark reviewed” action cannot resolve them.

Every append validates its transition before writing and synchronously flushes through the existing CRC-line journal.
An append failure holds recovery open. Archive holds and existing manual temporary-item holds are separate: finishing an
archive transaction cannot release another hold. `Finish` refuses an ordinary end record while either hold exists.
Disposed or already finished writers cannot acknowledge further archive transitions.

Bounds: sixteen transactions per journal; sixty-four transitions per transaction; sixteen unresolved paths per snapshot;
32,000 UTF-16 units per path; 4,096 per opaque identity; exact UTF-8 and uppercase SHA-256. Object lengths are bounded by
the archive physical-byte ceiling, and the manifest by the metadata ceiling. A regression checks that the maximum
escaped fields plus all sixteen artifacts still fit `MaxJournalLineBytes` (4 MiB). Catalogs/payloads are not placed in
these records. The subsequent [streamed sidecar codec](MANIFEST_SIDECAR_CHECKPOINT.md) provides bounded encoding and
review; native private storage/leases and transaction integration remain unfinished.

## Conservative recovery

Explicit-profile job headers declare `archiveJournalVersion: 1`, protecting even an unreadable first transaction record.
A clean header with no transition can finish normally after refusal/cancellation before mutation. Unknown header/fact
versions, fields, record types, invalid scalar types, duplicate JSON keys, CRC failures, torn records, sequence gaps,
replays or changed evidence retain the journal for review. A later valid commit/end cannot erase that uncertainty.
The scanner keeps only a bounded set of latest accepted facts, and retains the raw journal for inspection.

Unresolved archive facts override ordinary end markers, retention count and age pruning. Their source manifest remains.
Archive records without a begin header are retained as an unknown interruption. `IsInterrupted` uses the same rules;
generic `TryClose` rereads the current journal instead of trusting a stale scan. Generic Core staging, rename and
incomplete-copy discovery excludes archive journals. None of these reads performs artifact cleanup or replays edits.

The subsequent [read-only UI checkpoint](ARCHIVE_RECOVERY_UI.md) adds an explicit archive guard and archive-fact
disclosure to the UI's direct staged-intent review loop. Native object-bound review/deletion, private sidecar storage,
typed provenance/outer metadata and archive recovery actions remain separate unfinished work. Do not treat either
read-only guard as qualification of a native publication or cleanup adapter.

CRC checks detect torn/damaged lines; they are not authentication. These facts do not establish original-data
authenticity, defeat an equivalent-privilege process, prove a stage is valid merely because its name exists, or prove
file/directory-entry durability across power loss. Native leases and filesystem/crash qualification remain required.

## Observed regression evidence

The new contract adds 37 journal cases. They exercise every nonterminal phase under a spurious end/pruning attempt,
successful create/update and abort closure, retained-cleanup warnings, immutable evidence, stale closure, first-record
damage, unsupported/replayed/changed facts, duplicate JSON keys, field/record bounds and generic cleanup exclusion.
These are contract/reader tests, **not** the required actual archive publication process-crash/power-loss matrix.

Affected Core filter: `FullyQualifiedName~Archive|FullyQualifiedName~EditSession|FullyQualifiedName~BackingArchiveTarget|FullyQualifiedName~JobJournal|FullyQualifiedName~ReplacementRecoveryJournal|FullyQualifiedName~StagedRecovery|FullyQualifiedName~BulkRename|FullyQualifiedName~JobEngine|FullyQualifiedName~UndoLifecycle`.

| Environment | Final result | Evidence |
|---|---|---|
| Windows x64 host, SDK 10.0.101 | 696 passed, 30 skipped, 0 failed (726 total) | `.tmp/test-results/archive-writing/archive-transaction-host-qualified-checkpoint.trx` |
| macOS ARM64 / APFS, SDK 10.0.101 | 705 passed, 21 skipped, 0 failed (726 total) | Owned Mac `assessment-results/archive-transaction-mac-checked.trx` |
| Windows 11 x64 VM, runtime 10.0.14 | 696 passed, 30 skipped, 0 failed (726 total) | Owned Windows `results/archive-transaction-vm3-checked.trx`, copied to host evidence |
| Windows host headless recovery UI | 244 passed, 2 skipped, 0 failed (246 total) | `.tmp/test-results/archive-writing/archive-transaction-recovery-ui-host-checked.trx` |
| Ubuntu VM, later authorized replay | 721 passed, 5 skipped, 0 failed (726 total) | [Ubuntu source-build checkpoint](UBUNTU_REGRESSION_CHECKPOINT.md) |

The Core filter is broader than the earlier 620-case archive checkpoint; totals are not directly additive. Skips include
existing platform-specific controls and opt-in scale/native fixtures. The earlier Mac attempt failed two existing real
undo crash harness launches because `dotnet` was absent from the SSH session PATH. Adding `/Users/benny/.dotnet` to that
session PATH fixed the environment, and the complete final replay above passes. No application change was made for it.

The Windows VM executes host-built assemblies through the owned medium-integrity test launcher, not an installed
package or an ordinary filtered-user release qualification. Final facts: parent PID 2884, child PID 5396, runtime
10.0.14, integrity 12288 before / 8192 after, exit 0. The Mac uses the owned source checkout. No new GUI, sandbox,
format, destination filesystem or RID is qualified by these journal tests.

SHA-256 of the final Windows assemblies used for the checked Core replay:

| Artifact | SHA-256 |
|---|---|
| FileCat.Core.dll | `53E3CC2FE19257721EBEBC05543C8F73F6B235AA209B4A0086A930DB7BDD9328` |
| FileCat.Core.Tests.dll | `3D290D743740CBC21CF9A5A678DA0EDD8E1C2D65B0040F406413C6A3D1A3B98D` |

Native test process cleanup/availability is checked after each replay. The temporary Mac keep-awake assertion is
intentionally retained until reboot. The isolated worktree remains on `codex/archive-writing`; shared `E:\FileCat`
remains on `main`.
