# Proposed EFS pathname API concurrency boundary

Status: **approved by the user, 17 November 2026**. The user selected “Accept the disclosed pathname boundary”. This is separate from the earlier all-stream decompression consent. Approval changes this concurrency boundary only; EFS mutation remains unavailable until its implementation and remaining gates pass.

The user-required EFS implementation currently meets an unresolved requirement in §6.3 of the [implementation plan](FILECAT_NTFS_COMPRESSION_EFS_IMPLEMENTATION_PLAN.md): FileCat must never follow a redirected pathname to change a replacement object. Supported EncryptFileW/DecryptFileW accept a pathname, without a supplied object handle or no-follow option. The [investigation](../validation/filesystem-details/2026-11-17-efs-api-investigation.md) has not identified a supported protocol meeting that invariant. Sharing guards do not exclude attribute-only reparse conversion. Generic oplocks do not establish the missing exclusion, and before/after identity equality cannot prove what happened between those observations. This is an unresolved contract issue, not proof that every possible alternative is impossible.

Proposed change: permit the supported pathname EFS APIs under an explicitly disclosed **cooperative namespace** boundary. Retain exact captured membership, live review pins, a fresh case-aware no-follow walk, local-volume/protected-tree checks, and continuously held leaf/ancestor guards excluding competing deletion/rename. Observe and reject reparse/cloud/multiple-link objects and stale bindings before each call. Recheck the held objects and captured spelling after actual return, stop on detected churn, and report an unestablished effect when it cannot be established. No pathname retry after failed binding is allowed.

The changed guarantee is specific: **an equal-privilege process converting a held object in place into a reparse point after the final check is outside the exclusion guarantee. Windows may then resolve that path to another object before FileCat detects the change.** Detected churn is reported rather than undone. Pre/post checks and stress tests must not be described as continuous wrong-object prevention. Ordinary deletion/rename/substitution still must be excluded by the qualified held guards; that requirement is not waived.

The review must state this limitation beside the EFS action/scope and require a separate initially unchecked acknowledgment. Proposed wording:

> Windows applies EFS through a file path. FileCat holds the reviewed items and blocks competing renames/deletions, but cannot prevent another program from converting an item in place into a link during the call. In that case Windows could change a different item before FileCat detects it. Close other tools that change these items' links or storage backing before proceeding.
>
> I accept this concurrent link-change limitation for the displayed EFS scope.

This acknowledgment is action/scope/revision/identity specific, captured in typed approval and durable intent evidence. Refusing it leaves EFS unavailable under this policy. It is independent of the approved all-stream decompression consent.

Native qualification must test files and folders with the exact retained access/sharing recipe, existing writers, leaf/ancestor replacement, case-distinct names, cancellation and in-place attribute-only reparse attempts. Record each competing operation and every observed effect. A wrong-object effect from ordinary name replacement fails qualification. In-place reparse results are reported against the disclosed boundary, never presented as proof of prevention. No earlier stress result is relabeled as passing this protocol.

All other gates remain: Windows edition/policy and actual volume eligibility, no elevation/account switch/ACL changes, no implicit key setup/export, declared key recovery and post-call association checks, bounded stream inventory, all-stream decompression consent, destination/provenance/output admission, exact outcomes/journaling/recovery and architecture-specific native/UI qualification. This proposal alone enables no product action and closes none of those gates.

Decision: adopt this boundary and qualify a supported pathname implementation. The user also requested analysis of decompression; qualify held-handle NTFS decompression separately from pathname EFS decryption, without borrowing either mechanism's evidence for the other.
