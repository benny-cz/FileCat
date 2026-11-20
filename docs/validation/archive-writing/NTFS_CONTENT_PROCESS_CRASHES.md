# NTFS content-update process-crash validation

6 November 2026. Follow-up to `16c60b46` and the
[journaled NTFS implementation](WINDOWS_TRANSACTIONAL_CONTENT_INTEGRATION.md). The archive-writing goal remains
incomplete and new writer profiles remain disabled.

## Implementation and actual termination

The existing coordinator now lives in Core behind typed native source, stage, transaction and issued-review contracts.
The Windows entry point supplies its actual held native directory's creator. The common code still accepts only the
explicit Windows NTFS transactional-data recipe on Windows. Windows adapters require their own actual native files,
parent scope and exact live issued review. This adds no generic overwrite fallback, source-sharing relaxation or
production crash hook.

Test-only interface wrappers pause the actual coordinator at native operation boundaries. The CoreCrashHarness
creates a real TAR original, assesses it, rebuilds its replacement and freshly verifies the private stage using three
distinct actual controlled child processes. All three must exit successfully and be reaped before the crash points.
These controlled children do not qualify confinement; the separate existing LPAC/application cases remain applicable.

The driver observes a flushed checkpoint, verifies the parent is alive, kills that process and its tree externally,
waits for actual exit, then opens fresh recovery scans and native object observations. No thrown exception substitutes
for the process kill. Rollback cases inject a test failure after private preparation to enter the actual coordinator's
rollback path, then still terminate the actual parent externally at the stated boundary.

| External kill point | Last durable content phase | Observation after process exit |
| --- | --- | --- |
| Snapshot reserved, before creation | SnapshotReserved | Original unchanged; snapshot absent |
| Snapshot created, before identity observation | SnapshotReserved | Original unchanged; empty snapshot retained |
| Fresh snapshot identity/security observed, before append | SnapshotReserved | Original unchanged; empty snapshot retained |
| Snapshot identity recorded | SnapshotCreated | Original unchanged; identified empty snapshot |
| Native transaction opened, before GUID append | SnapshotCreated | Original unchanged; identified empty snapshot |
| Native transaction GUID recorded | TransactionOpened | Original unchanged; identified empty snapshot |
| Snapshot sealed, before its version append | TransactionOpened | Complete old bytes and canonical original zone retained |
| Snapshot version recorded | SnapshotSealed | Complete recorded snapshot; original unchanged |
| Private output prepared and pinned, before Ready append | SnapshotSealed | Kernel rollback restores old public bytes/mark |
| Ready recorded | Ready | Kernel rollback restores old public bytes/mark |
| Commit intent recorded, before native commit | CommitIntent | Old public bytes/mark remain |
| Native commit/adoption complete, before outcome append | CommitIntent | New bytes/mark visible; no Published fact exists |
| Actual publication recorded | CommittedWithRetainedArtifacts | New bytes/mark visible; all evidence retained |
| Before native rollback | SnapshotSealed | Old public bytes/mark remain after process teardown |
| After native rollback, before its outcome append | SnapshotSealed | Old public bytes/mark remain |
| Rollback outcome recorded | RolledBackWithRetainedArtifacts | Old public bytes/mark remain; all evidence retained |

Every case checks original native identity, the appropriate exact metadata digest and origin, actual TAR payload,
verified stage checksum, sidecar checksums, snapshot existence/length/checksum/zone and the unchanged unrelated sentinel.
It distinguishes identified, unrecorded-complete and recorded snapshot versions. Repeated zero-age/zero-retention
recovery scans and stale closure attempts preserve every surviving path/hash and the journal, without adding an end
record. Identified snapshots reopen through native parent/object identity checks as read-only objects and cannot
acquire new content-transaction authority. No recovery scan restores, publishes, deletes or infers NotCommitted merely
from a missing Published record.

## Validation

| Actual environment/lane | Passed | Skipped |
| --- | ---: | ---: |
| Windows 11 VM / NTFS / .NET 10.0.14, final native lane | 104 | 0 |
| Windows common archive/edit regression | 1,355 | 212 |
| Windows recovery/archive UI | 242 | 0 |
| Windows storage control | 128 | 105 |
| Mac arm64 / .NET 10.0.12, common archive/edit regression | 1,502 | 65 |
| Mac recovery/archive UI | 242 | 0 |
| Ubuntu x64 / .NET 10.0.14, common archive/edit regression | 1,515 | 52 |
| Ubuntu recovery/archive UI | 242 | 0 |
| Ubuntu native containment | 61 | 1 |

The 104 Windows cases comprise the prior 88 native/LPAC/application cases and all sixteen new actual process kills.
The test parent recorded integrity 12288 before lowering and 8192 afterward, exit 0. This retains the launcher's
admin-membership limitation; it does not establish standard-user qualification. The first native run passed the prior
88 cases but all sixteen new cases failed in test setup: reflection omitted OpenSource's optional physical-length
argument. The corrected harness supplies the actual finite length argument; the full 104-case replay passes. Failed
evidence is retained.

Both Unix runs independently read back 12 zstd, 11 bzip2, 11 gzip and 8 exact-timestamp fixtures. Both then compiled the
final Windows-only harness correction; all 36 Windows-specific cases correctly skipped. Those guards do not qualify
an NTFS transaction on Unix. Ubuntu initially deferred for another live tester and ran only after that process exited
and a fresh census was idle. No own FileCat/test process remained after completion; Mac keep-awake remains temporary
until reboot.

All builds completed without warnings/errors. The refreshed development app's SPDX 2.3 inventory includes 261 exact
files, all SHA-256 values rechecked against disk, with 34 reviewed NuGet packages and one runtime pack (235 restored
files matched to archives). No dependency was added. This is a dirty-source development artifact.

## Evidence identities

Ignored raw evidence is retained in `.tmp/test-results/archive-writing/content-crash-v1` and
`content-crash-artifacts-v1`, with corresponding guest archives in this task's validation directories.

| Artifact | SHA-256 |
| --- | --- |
| Initial ten-file source delta | `BFE0082B2CBDFF68FCAA65AEF9AEA3F88BFCA79688382149416829D807510B2E` |
| Final ten-file source delta | `7D0332DA86BE1EC4476647BE23893BE293CA7A82C98D4F5FC95453E7FF245BF3` |
| Final Windows binary/runner overlay | `BD09674917786EAF5A9A6DA261532CBA22B25F6F997E2520AB5D66ECDB69A0FC` |
| Windows first run, including setup failures | `DACA15886974B1A110728DC8EBC573F9565C1D01BD353D514F600A800D76038E` |
| Windows corrected native replay | `E5E8FAEAB16EA0DB9CB82C0D663A6A02F6C5F2C516D1B8691D5DF9930B5FFA64` |
| Mac broad/oracle evidence | `A8283A991061B18C71671F85856B12EE224DB8A7225C4A2224E54BF47B58573B` |
| Ubuntu broad/native/oracle evidence | `1D05BDFABBDA1A9A86DD4F9FA391771D27BB9EF591B699997C63AE9226BC74B3` |
| Mac final harness compile/guard | `D1197B949EDE9EAB7458B649636D5DF42A23990BE1ACCD7ABB74F508C7038F91` |
| Ubuntu final harness compile/guard | `326F7D8439306F790084E4562D720AFB6D833DB02B7AB2AAD746BC10A818E31C` |
| Final app SPDX inventory | `2126E7FF3D66426DFF17C3181BD27B81939739462302B5AFFD974B85E14B88C3` |

## Remaining work

These are process-crash tests using real TAR data. They do not establish power-loss durability, partial/ENOSPC IO,
volume removal, all timeout/resource/alias races, hidden policy coverage or the full hard-link requirement. Active
object-bound recovery/restoration/cleanup and the artifact-deletion crash matrix remain unfinished. Snapshot facts
currently bind original bytes; exact recovery cleanup also needs the sealed snapshot metadata observation and native
deletion authority. Production routing, Unix update backends, complete capture/global budgets, remaining formats and
the rest of the controlling plan remain open. RAR analysis/feasible implementation remains before the final independent
review, following the implementation loop.
