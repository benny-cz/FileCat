# Proposed EFS compression-conflict amendment

Status: **approved by the user, 17 November 2026**. This does not enable EFS or close its identity, key, output, recovery or native acceptance gates.

The active objective explicitly requires EFS. Section 6.3 of `FILECAT_NTFS_COMPRESSION_EFS_IMPLEMENTATION_PLAN.md` currently forbids EFS encryption from silently decompressing any named stream and says: “A different conflict-consent policy would be a separate explicit design change, not an implementer’s escape hatch.”

The [24-case disposable-machine experiment](../validation/filesystem-details/2026-11-17-efs-api-investigation.md#native-share-guard-counterexample) gives a concrete counterexample: a new ADS can be created and compressed under the main-file guard, then `EncryptFileW` succeeds and changes that ADS from LZNT1 compression to uncompressed. Supported Windows behavior therefore needs a policy decision before implementation can use it. The tested share-only recipe remains rejected under the existing contract.

Proposed amendment: **permit Windows to remove compression from any data stream as part of EFS encryption only after explicit consent to that effect for the immutable reviewed scope**. Consent covers streams present at review and streams created before the encryption call completes. It does not assert that a bounded inventory freezes the stream namespace.

Proposed review wording:

> EFS encrypts file contents, including named streams. Windows may remove NTFS compression from these streams, including streams created while this operation runs.
>
> Allow Windows to remove compression as part of EFS encryption for these selected items and the displayed folder scope.

The consent starts unchecked, is action-specific, and becomes invalid when the action, scope, review revision or invoking identity changes. It is captured in typed approval and durable intent evidence. Refusing it leaves encryption unavailable for this policy; it does not trigger a hidden decompression job. Compression actions continue to leave named-stream compression alone. No guaranteed space or size estimate is introduced.

The amendment changes the compression-conflict policy only. It does **not** permit path substitution, loss of continuous lifetime binding, following a reparse point, unknown cloud/storage backing, elevation, ACL/owner changes, rekeying, key creation or export without their own specified approval, unsupported output routes, automatic replay, or enabling a target without native evidence. In particular, the observed directory guard failures still require a supported protocol; they are not waived by conversion consent.

Two available decisions:

1. Adopt this amendment and continue EFS qualification against the explicit conversion consent plus all remaining gates.
2. Retain the current no-decompression contract and continue protocol research; EFS stays disabled until that contract can be proved.

The user selected “Adopt explicit consent (recommended)”. This is the authority for the amendment.
