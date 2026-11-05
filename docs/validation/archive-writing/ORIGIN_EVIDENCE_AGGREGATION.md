# Streamed provenance evidence and Windows aggregation

2026-11-05, over `936b64be`. This adds a scoped bounded sidecar codec, complete-input Windows declaration aggregation,
fresh checksum/EOF review, and private NTFS file integration. No new write profile is enabled. Durable transaction
references/pruning, reviewed source-plan integration, global budgets, native Unix representations, final metadata
application and post-publication mark reconciliation remain pending.

## Evidence contract

`ArchiveOriginEvidenceScope` binds a task nonce, exact profile/options, create/update operation and reviewed source count.
Creation has at most 100,000 captured sources; update has the original archive first and at most 100,000 added sources.
Each record contains a typed original/file/directory role, object identity, consumed-content length/SHA-256 and complete
typed raw origin snapshot. Directories have zero payload and the empty-content digest. Added files retain the 8-GiB
individual/64-GiB added-input ceiling; combined original-plus-addition logical budgets still belong to preparation.

The writer is stream-only, starts on an empty seekable output, appends no more than 128 MiB, bounds complete records
before IO, and cannot resume after failure/cancellation. The reader validates scope and lengths before allocation,
strict UTF-8 identities, roles/order, enum/reserved/presence rules, source limits, complete EOF and the external file
checksum. Rows returned before EOF remain unverified evidence. Early completion, corruptions, unsupported fields and
IO/cancellation poison review. Neither direction retains the source set, chooses paths, closes caller streams or
deletes partial artifacts. Per-file limits do not substitute for shared job metadata/heap accounting.

Wire schema 1 (little endian except RFC-4122 nonce): `FCOEVID1` magic (8), ushort version (2), operation byte, reserved zero,
nonce (16), profile four int32 fields (16), source count int32 (4); total header 48 bytes. Each framed record has int32
body length, then kind int32, identity-byte-length int32, source length int64, SHA-256 (32), origin format/state/failure/
reserved bytes (4), origin-byte-length int32, exact identity UTF-8 and raw mark bytes. Fixed record body is 56 bytes;
identity is at most 4,096 UTF-16/16,384 UTF-8 units and raw mark at most 64 KiB. End is `FCOE`, followed by exact EOF.
An empty create vector is 52 bytes with SHA-256 `B31CEABF2D61625DC973948D7121CE2BEFC06D076EC4267DBBA27F1B36CFC193`.
Its scope/file checksum is not native ownership, authentication or cleanup authority.

## Aggregation

`ArchiveWindowsOriginAggregation` records every raw source mark and consumes every declaration. It never stops after
finding zone 4: a later unreadable/unsupported/foreign input must refuse the result. Missing/extra sources and ordering
errors also refuse completion. All positively absent inputs produce absence; present zero remains a present mark.

Creation emits the greatest supported declared zone as a zone-only mark, with no invented origin URL. Update retains
the existing outer mark, including its raw URL/opaque evidence, and only increases its declared zone when necessary.
If the original is absent, update uses a zone-only mark too. All input URLs/raw bytes remain available in the private
evidence, including those not selected for the outer declaration. Fresh `Review` re-derives the result and returns only
after the complete evidence checksum and EOF validate. This is declared-zone propagation, not an effective Windows
trust/policy decision. Source consumption/version capture and actual retired/published-object rereads are separate.

Windows native storage adds a `Provenance` artifact kind with exclusive random `.origin` names, the existing protected
user/SYSTEM ACL-at-create and the 128-MiB metadata ceiling. The native integration captures two real files/marks, writes
and seals the evidence, reopens by recorded object identity, verifies length/SHA, reviews the entire sidecar, confirms
both raw URLs, copies the aggregate to a sealed owned stage, verifies the stronger declaration and unchanged source,
then removes only the reviewed evidence object through the mandatory metadata/content guard. This is component
integration, not a journaled archive publication workflow.

## Validation

113 new Core cases cover all archive profiles/native mark formats, opaque/unavailable raw preservation, a golden
independent binary vector, original/file/directory ordering, every truncation of a small sidecar, repaired-hash
structural/preallocation attacks, source/metadata limits, partial retention, cancellation/IO poisoning, collection of
discarded decoded rows, every absent/zone pair for creation/update, retained URL evidence, no lowering and late failures
after maximum taint. The affected Core filter includes the previous 42 cases (155 total).

| Environment | Core provenance filter | Native Windows archive filter | Evidence |
|---|---|---|---|
| Windows 11 x64 VM / NTFS, runtime 10.0.14, medium integrity | 155 passed, 0 skipped/failed | 63 passed, 1 skipped, 0 failed | `archive-origin-evidence-core-vm1.trx`, `archive-origin-evidence-native-vm1.trx` |
| macOS ARM64 / APFS, source built, SDK 10.0.101 | 155 passed, 0 skipped/failed | 24 passed, 40 Windows-only skips, 0 failed | `archive-origin-evidence-core-mac.trx`, `archive-origin-evidence-native-mac.trx` |
| Ubuntu x64 / ext4, source built, SDK 10.0.101 | 155 passed, 0 skipped/failed | 24 passed, 40 Windows-only skips, 0 failed | `archive-origin-evidence-core-ubuntu.trx`, `archive-origin-evidence-native-ubuntu.trx` |

The native filter is all `WindowsArchive*` classes (64 cases), including metadata, origin oracles and the new private
evidence integration. The Windows skip is the separately tested elevated-refusal lane. Windows uses host-built
assemblies, not a guest source build or ordinary filtered-user/package qualification. Core parent/child PIDs 5932/2960;
native 6156/4756; both runtime 10.0.14, integrity 12288 before / 8192 after, exit 0. Builds have zero warnings/errors.

Exact seven-file source delta applied over the declared-zone-updated owned Unix trees:
`9E1F14982F282233107812A223D286EAFEE7F2DD0ACE63BC101575A781F7E4AE`.
Windows binary/symbol transfer archive: `8A8920C559A166F6230CDB31315A6F1FE88ACBEA79535CFD20A304EC7EA9682D`.

| Artifact | SHA-256 |
|---|---|
| FileCat.Core.Tests.dll | `71101F31303DB0CD242CCC286B02EF602FC53C601DB43F8805C4C5A5DD7D033D` |
| FileCat.Core.dll | `05FFD593B9BDA5A60C50AA1F6A7E1CD2C9996FD27735B0964FA38F9DD1EC0B8B` |
| FileCat.Archives.dll | `C4AB93DF0DED885C3E0AB3FEE246C61E2FC4EA1FB82E92C779A67C7750E324D1` |
| FileCat.Platform.Windows.Tests.dll | `089D086AB7EB143A4CE6867ECA6D637853ADFD1A69F15990BB8727495944CF84` |
| FileCat.Platform.Windows.dll | `069A8ECF63908DC258A0985831896CED15815C6918AEEF9AB681AFE15B191051` |

Raw TRX/facts remain in ignored evidence. All used guests have no remaining FileCat/test instances; Mac until-reboot
keep-awake assertions remain active. Shared `E:\FileCat` stays tracked-clean on `main`. No dependency/third-party source
was added. Full goal completion, independent final review and conditional RAR analysis remain pending.
