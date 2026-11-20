# Live NTFS archive artifact cleanup

6 November 2026. Follow-up to `34921973` and [NTFS process-crash validation](NTFS_CONTENT_PROCESS_CRASHES.md).
New writer profiles remain disabled; the full archive-writing plan is still incomplete.

## Implemented lifecycle

After the old archive's byte snapshot is sealed, the common coordinator durably writes a separate
`archive-content-cleanup` version-1 catalog before privately modifying the target. Its immutable content-update scope
binds the preparation, original metadata, transaction GUID, retained snapshot and required provenance. Each catalog row
records the exact private artifact path/kind, native identity, byte length/checksum, sealed metadata digest and origin
digest. The catalog includes the rebuilt stage, every reserved sidecar and the original-byte snapshot, in that order.
This persists the snapshot metadata that the content-update record's byte version alone did not capture.

The live result retains its native transaction/published read pin. Explicit cleanup first rereviews the committed
target and every remaining artifact, then records DeleteIntent before each native unlink and Deleted afterward. It
checks the target between deletions. The snapshot is last. Once an unlink may have happened, the outcome append and
target rereview ignore cancellation; a cancellation before an operation retains files and records NeedsReview.

Windows deletion requires the actual live creator-owned private object, its retained directory, unchanged name,
original artifact kind, sealed write state and exact catalog observation. It rereads private permissions and complete
data/origin stream inventory, then uses reviewed deletion through that object's held native handle. The deleting handle
closes before cleanup is recorded as complete. No path-based deletion, public-archive deletion or overwrite fallback
is added. Recovered read-only artifacts cannot obtain this live creator authority.

Cleanup and disposal serialize on the result's lifetime lock. Completed cleanup is idempotent while the same live
journal facts remain current. Stale/deserialized facts, changed metadata, closed artifacts and uncertain deletion
phases refuse further effects and retain the journal. The result still owns only the published pin and snapshot;
preparation continues to own the stage/sidecar leases, whose later disposal safely closes already-unlinked objects.

The bounded cleanup history permits a catalog, one intent/outcome pair per artifact and a final Completed record.
Unknown fields/versions, changed scope/catalog, reordered/skipped transitions, aliases, altered bytes or metadata,
early deletion before confirmed publication and inconsistent deletion counts are refused. Cleanup resolves its frozen
preparation/content history only after every unlink and Completed are recorded, while the content outcome is still
committed. A later content NeedsReview hold remains visible. The read-only report lists sealed metadata and confirmed
versus remaining artifact locations; an unresolved cleanup still blocks generic closure.

## Actual deletion crashes

Twenty additional externally killed parent cases exercise a real verified TAR update:

- Cataloged cleanup before any deletion.
- For each of six artifacts: durable intent before unlink, successful native unlink before its outcome append, and
  recorded outcome before the next operation.
- Completed cleanup before ordinary journal closure.

The driver checks exact physical absence separately from the recorded deletion count. In particular, a missing file
after an interrupted intent is not silently converted into a confirmed deletion. Every surviving artifact reopens by
native directory/object identity and matches its byte, metadata and origin digests. Such recovered objects remain
read-only and refuse live creator deletion. The original-byte snapshot survives until its own final unlink. The
committed archive's identity, bytes, metadata, origin and actual TAR payload remain correct at every point, and an
unrelated sentinel is unchanged.

Repeated zero-age/zero-retention scans and stale closure attempts preserve incomplete histories and all surviving
files. Once every deletion and Completed are recorded, the recovery hold clears and ordinary journal closure succeeds.
These are actual externally terminated processes, not simulated exceptions. Controlled test workers are separate from
the existing LPAC/application confinement qualification.

## Validation

| Actual lane | Passed | Skipped |
| --- | ---: | ---: |
| Windows 11 VM / NTFS / .NET 10.0.14, native/LPAC/application | 131 | 0 |
| Windows common archive/edit regression | 1,373 | 212 |
| Windows recovery/archive UI | 242 | 0 |
| Windows native storage control | 128 | 132 |
| Mac arm64 / .NET 10.0.12, common archive/edit regression | 1,520 | 65 |
| Mac recovery/archive UI | 242 | 0 |
| Ubuntu x64 / .NET 10.0.14, common archive/edit regression | 1,533 | 52 |
| Ubuntu recovery/archive UI | 242 | 0 |
| Ubuntu native containment | 61 | 1 |

The 131 Windows cases comprise the previous 104 plus four successful cleanup formats (TAR, gzip, bzip2, zstd), three
pre-effect refusal cases (cancellation, closed snapshot, changed snapshot time) and twenty actual cleanup crashes.
The test parent records integrity 12288 before lowering and 8192 afterward, exit 0. Its admin-membership limitation
remains; this does not establish standard-user qualification. All builds completed without warnings/errors.

Both Unix runs independently read back 12 zstd, 11 bzip2, 11 gzip and 8 exact-timestamp fixtures. Each correctly skipped
63 Windows-only cases; those skips do not qualify a Unix cleanup/update backend. No own FileCat/test processes remained
on the guests afterward. The Mac's temporary keep-awake assertion remains until reboot. Shared `E:\FileCat` stays on
`main`, with this work in the isolated branch/worktree.

The development Windows app inventory contains 261 exact files, all SHA-256 values rechecked against disk: 34 reviewed
NuGet packages and one runtime pack, with 235 restored files matched to their archives. No dependency was added. This
is a dirty-source development artifact, not release qualification.

## Evidence identities

Ignored raw evidence is retained in `.tmp/test-results/archive-writing/content-cleanup-v1` and
`content-cleanup-artifacts-v1`, with guest copies in this task's validation directories.

| Artifact | SHA-256 |
| --- | --- |
| Eleven-file source delta | `55A35DF777C28EBD9EDB75BF285DE2229C2F26768E6AC0E2E3F28475FA16CA0F` |
| Windows binary/runner overlay | `B82A0C137874614206B6D0CEF688E8EB690012B60A2AC6E1B722F441C69F776A` |
| Windows broad/native/crash evidence | `AB8B7761D36E5D4F3FBE7D2629DE5B2B64DAA8808757F617FB7CE5D9EF0F0C68` |
| Mac broad/oracle evidence | `FBDF7A0D506C038371FCFCF807E6920669B39E86DF195CDF80F731C67331D25D` |
| Ubuntu broad/native/oracle evidence | `8B09EF483AACC378ED197E4FF7D935C77E75D9C413BDE690FFA37F9518DFFDB0` |
| App SPDX inventory | `1075126A7B128736F8658F3D013BC1C6C0B2781E734543E3A5F88B1D9AD268D9` |

## Remaining work

This implements live cleanup after a confirmed publication. Native recovery inspection, restoration and resumption of
an interrupted cleanup still need fresh object-bound action authority and durable journal reopening/reconciliation;
the read-only recovery report does not grant those effects. Power loss, partial/ENOSPC IO, volume removal, all resource/
timeout/alias races and the full hard-link requirement remain unqualified. This does not broaden the earlier security
policy coverage. Unix update/cleanup routes, complete capture/global budgets, remaining formats, production workflows
and the rest of the controlling plan remain unfinished. RAR analysis/feasible implementation still precedes the final
independent review, after the implementation loop.
