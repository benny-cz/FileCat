# Durable provenance references and legacy journal retention

2026-11-05, over `9e9e1fa8`. Archive transaction facts now use schema 2. This connects the bounded provenance evidence
contract to durable journal references and read-only interrupted-operation reporting. It does not connect a production
archive executor, grant path-based cleanup authority, or qualify native publication. No new write profile is enabled.

## Contract and conservative recovery

Each complete snapshot can reserve the exact absolute provenance sidecar path, opaque object identity and reviewed
source count together, before its contents are written. These three fields cannot change or disappear in a later
transition. Its finalized object length and SHA-256 may be added once and then cannot change either. The reservation
cannot alias the archive, stage, retained original or manifest path/identity. Finalized length must fit the 128-MiB
metadata ceiling and the complete sidecar framing for the declared source count. Creation permits zero through
100,000 sources; update includes its original archive and permits one through 100,001.

Ready-to-publish and subsequent publication/rollback phases require finalized provenance evidence alongside verified
stage, manifest and security facts. These are recorded claims: a native coordinator must still acquire the exact
object, check its full version, and stream-review its complete scope/count/checksum against the reviewed source plan.
A journal checksum or recorded identity does not authenticate an artifact or authorize any mutation.

Schema 1 remains readable with its exact historical fields and recorded artifact paths. Missing provenance is never
upgraded to absent evidence. Legacy records are read-only and keep the journal/source manifest visible for
archive-specific review, including a fully recorded historical commit/end. Header/fact version mismatches, mixed
versions, removed required evidence, duplicate/late headers, early/repeated end records, damaged records or changed
evidence retain review rather than accepting a later valid-looking commit. A new schema-2 header with no transition
can still finish normally after refusal before mutation.

The snapshot constructor bounds the complete escaped JSON against the existing 4-MiB CRC-line limit before any append.
All sixteen ordinary retained-artifact paths remain supported; maximal individual path/identity fields cannot be
combined without also fitting that total limit. This avoids introducing an append failure after a caller relied on
individually valid fields. Native callers must persist each intent before its corresponding namespace mutation.

Unresolved provenance cleanup can be named in `CommittedWithRetainedArtifacts`; normal end/pruning/generic closure
remain refused. Recovery scans/reporting never open, delete or inspect the recorded sidecar. The UI exposes facts
version, exact quoted path, identity, declared count and finalized version separately, and preserves the distinction
between committed-with-leftovers and unknown outcome. Stale generic actions re-read the journal before refusing.

## Validation

43 added Core cases (80 archive-journal cases total) cover incomplete/type-confused/aliased/impossible evidence,
create/update count boundaries, immutable reservations/finalization, legacy completed and headerless histories,
legacy write refusal, schema confusion, retained provenance cleanup, and header/end ordering. Three added headless UI
cases cover legacy history, legacy completed history and retained provenance evidence; their read-only reports do not
open artifact content or alter the journal/files.

Affected Core filter:
`FullyQualifiedName~Archive|FullyQualifiedName~EditSession|FullyQualifiedName~BackingArchiveTarget|FullyQualifiedName~JobJournal|FullyQualifiedName~ReplacementRecoveryJournal|FullyQualifiedName~StagedRecovery|FullyQualifiedName~BulkRename|FullyQualifiedName~JobEngine|FullyQualifiedName~UndoLifecycle`.
Headless UI filter: `FullyQualifiedName~Interrupted|FullyQualifiedName~JournalPlace|FullyQualifiedName~MtpMoveJournal`.

| Environment | Core | Recovery UI | Evidence |
|---|---|---|---|
| Windows 11 x64 VM / NTFS, runtime 10.0.14, medium integrity | 955 passed, 30 skipped, 0 failed (985 total) | 256 passed, 2 skipped, 0 failed (258 total) | `archive-origin-journal-{core,ui}-vm2.trx` |
| macOS ARM64 / APFS, source build, SDK 10.0.101 | 964 passed, 21 skipped, 0 failed (985 total) | 252 passed, 6 skipped, 0 failed (258 total) | `archive-origin-journal-{core,ui}-v2-mac.trx` |
| Ubuntu x64 / ext4, source build, SDK 10.0.101 / runtime 10.0.14 | 980 passed, 5 skipped, 0 failed (985 total) | 252 passed, 6 skipped, 0 failed (258 total) | `archive-origin-journal-{core,ui}-v2-ubuntu.trx` |

The initial Windows run failed seven newly added test assertions because `File.ReadAllBytes` did not share the live
journal writer's write access. The assertions now read through an explicitly shared read handle and still compare the
exact before/after bytes. The final replay above passes. The initial Unix replay passed before four further ordering
regressions and the corresponding recovery fix; the final results above use the complete v2 evidence. These are reader/state-machine
and headless UI checks, not the required real native archive publication crash/power-loss or GUI/package qualification.

Exact five-file source delta applied over the provenance-aggregation-updated owned Unix trees:
`D9CD23460C46C9758292DA1DB0A2ED9FD16A62B9EF072C7B5735CF019947B27A`.
Windows binary/symbol transfer archive: `59CA5C6A14C59232BD7BCE10A1C7322FEFDA46DAA65BE06D01F4468D419901CF`.

| Artifact | SHA-256 |
|---|---|
| FileCat.Core.dll | `CFD83E108F824F4FB20A23A08F9C7FD88F1A0308E7855E6D6DFE7C7F61C1F3DC` |
| FileCat.Core.Tests.dll | `F4B6FF8BFA8587CA9E23D1D6E06175ED2BAF174DBED2C7925196D924FFC90558` |
| FileCat.dll | `6CA2C6888D358EA3FCB76A9F6A1DFD3F8DD276BB5DF65D4F982CB5CDB565D9C4` |
| FileCat.App.Tests.dll | `150FFD3521BD157CAC27114C9E7262C8864746BB9E2235A693B03DBA786DD4E0` |

Windows parent/child PIDs: Core 1584/5684, UI 4288/4364; both runtime 10.0.14, integrity 12288 before / 8192 after,
exit 0. This launcher retains administrator memberships/privileges and is not ordinary filtered-user/package evidence.
No FileCat/test processes remained on the Windows guest after completion. Mac's until-reboot keep-awake assertion
remains active. Ubuntu was postponed for another tester, then resumed only after it became idle.

Still required: actual native reservation/finalization/retention integration, shared job-wide evidence budgets,
metadata application, journaled publication/reconciliation/rollback/exact cleanup, actual crash matrices and complete
all-platform application/package qualification. The separate final independent review and conditional RAR licensing
analysis remain pending within the active full implementation goal.
