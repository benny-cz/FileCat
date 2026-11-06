# Fresh restoration from a retained archive snapshot

6 November 2026. Follow-up to `8b7a47f0`. This remains an unregistered backend; no writer profile or production recovery
action is enabled by this checkpoint.

## Implementation

`WindowsArchiveRestorationLease` retains the actual NTFS journal, archive parent, current target and original-byte
snapshot. The target must still match the recorded published/private output, and the snapshot must match its sealed
cleanup-catalog identity, bytes, metadata, private security and origin. A changed target or snapshot, a stale/busy
journal or an already-started cleanup refuses restoration. The lease is single-use.

`ArchiveJournaledSnapshotPreparation` separately binds the current destination baseline and the old byte source.
A fresh confined assessment reads the retained snapshot, the parent copies its exact bounded bytes into a new private
stage, and a different confined verifier checks the entire archive/member catalog and whole-file digest. The private
stage receives the canonical strongest zone before verification. The final existing target retains its full original
origin fields and raises its declared zone when required. Unmarked Unix preparation is supported; marked Unix and
Unix update publication remain separate unfinished routes.

Restoration always uses a fresh job and the existing explicit NTFS transaction recipe. Before overwriting anything,
that transaction seals a new rollback snapshot of the current version. Its issued review replaces the ordinary target
lease consumed at transaction start. The source snapshot and old journal stay independently retained and rereviewed.

The old journal receives an `archive-content-restoration` v1 intent linking the fresh job/journal/native transaction,
old immutable content/catalog hashes, before version and freshly verified expected restored version/metadata/origin.
This append is durable before native commit. A recorded committed outcome follows the normal publication coordinator's
post-commit source/stage/native checks. A failed attempt remains reviewable; retry requires another fresh job and native
transaction, with a bounded attempt count, and never replays the old mutation.

Cleanup reconciliation schema 2 can classify a fresh exact restored target against that restoration link, including an
intent whose outcome append was interrupted. This classifies current bytes, not historical commit. The original
content/catalog histories remain frozen. Existing schema-1 cleanup histories stay readable and keep their version;
cleanup already in progress cannot acquire restoration authority. Current-version and original-version snapshots belong
to separate catalogs and are not implicitly interchangeable or deleted by the other job's cleanup.

Read-only recovery summaries now distinguish recorded restoration from interruption and show the linked fresh journal,
before/expected restored digests and the separate rollback-copy explanation. Paths remain escaped and explicitly
uninspected. These reports do not grant replay, deletion or publication authority.

## Validation

| Actual lane | Passed | Skipped |
| --- | ---: | ---: |
| Windows genuine standard account, four formats × real app/probe restoration and four refusal cases | 12 | 0 |
| Windows complete native lane, administrator token | 187 | 0 |
| Windows complete native lane, genuine standard account with loaded profile | 182 | 5 |
| Windows common archive/edit regression | 1,406 | 216 |
| Windows recovery/archive UI | 245 | 0 |
| Windows storage regression, opt-in native tests disabled | 128 | 188 |
| Mac common archive/edit regression | 1,557 | 65 |
| Mac recovery/archive UI | 245 | 0 |
| Ubuntu common archive/edit regression | 1,570 | 52 |
| Ubuntu recovery/archive UI | 245 | 0 |
| Ubuntu native containment | 61 | 1 |
| Latest-source Mac focused journal/origin/native preparation | 48 | 0 |
| Latest-source Ubuntu focused journal/origin/native preparation | 48 | 0 |
| Latest-source recovery report, each Unix platform | 3 | 0 |
| Latest-source Windows-only guard, each Unix platform | 0 | 119 |

The positive native cases retain the exact original TAR/gzip/bzip2/zstd bytes, first publish different contents, then
restore those original bytes with a fresh current-version rollback copy. They check unchanged file identity,
security/creation fields and attributes, strongest origin with full target fields, distinct reaped worker roles,
separate catalog cleanup and final journal closure. Modification time can advance because restoration is a new write.
The negative cases retain all paths/hashes when target, snapshot, journal revision or active-writer ownership differs.
All 469 actual runtime files match the host manifest; all 261 actual app files also match the SPDX inventory.

Seventeen new common journal checks cover immutable scope, fresh retry identity, unsupported/corrupt records,
restored-state reconciliation and schema-1 compatibility. Four actual Unix preparation cases preserve exact bytes and
leave a separate destination untouched; three report checks distinguish outcomes and escape paths. Broad Unix runs
precede the Windows-only callback/origin fixes and the expanded Windows refusal cases; the final focused runs use
the latest source on both Unix platforms. All listed runs have zero failures. The complete Windows run finishes
with a process census proving that its FileCat/test children have exited; the Unix runners also check idle state.
The Mac's temporary until-reboot keep-awake assertion remains active.

### Retained evidence identities

Raw evidence lives in the private worktree's ignored `.tmp` directory. SHA-256 values identify development/test
artifacts, not qualified release packages.

| Artifact | SHA-256 |
| --- | --- |
| `snapshot-restoration-source-v7.tar.gz` (16 code/test files over `8b7a47f0`) | `07EC3B63AC713F8B2BB773507CED5A5CE2DB5C87C5C7D3F8EFE888C276B0179A` |
| `snapshot-restoration-runtime-v6.json` (469 complete app/probe files) | `3D3B37BDA9526A5C72A3D3376428B3DA91708EB75F106B924F8EACA73CC7D7BD` |
| `snapshot-restoration-artifacts-v7/application.spdx.json` (261 app files) | `773C9DA2C9399EF4A4D3BAF51D8472F026FE8E9EE97FCDF3893B1A088E81DC1F` |
| `snapshot-restoration-windows-smoke-evidence-v7.tar.gz` | `CA2395CBABDE97D42B6FBE515D2829CDF7100256D324212BD9980E96EBF9CE05` |
| `snapshot-restoration-windows-evidence-v7.tar.gz` | `3F6AED3C0FEF7F7684BB55ACE7913DED5B087B34EC27E50DBC09000C43414165` |
| `snapshot-restoration-mac-evidence-v3.tar.gz` | `34AED609E1D8C321AFE3D89CDF7E20DD04058C0F040ED19A82B7C33FD35129F9` |
| `snapshot-restoration-ubuntu-evidence-v3.tar.gz` | `140DFCE319AD6A8871C27AA2ACDBA9D176E88C16C7F1FC843B19C3B4577BD781` |
| `snapshot-restoration-mac-final-v7.tar.gz` | `191BD5E2ECE4569DA7CB0841D35085C23B4523B3B67E7EDCA1C7BE8E90D2729B` |
| `snapshot-restoration-ubuntu-final-v7.tar.gz` | `92145D23A503BAE42E3B88F38949FE885BBEEAAA934B21B5A0835A00F368277F` |

The loop caught and fixed missing provenance-source counts, the compressed-stage disk-budget floor, premature disposal
of a borrowed native stage, canonical scratch/rollback marks versus full target URL evidence, and the consumed target
lease's handoff to the native transaction. Failed runs remain diagnostic evidence. No dependency was added.

## Remaining work

Actual process termination during restoration, cancellation/failure matrices, broader metadata/security-policy,
resource/alias/durability qualification, production recovery UI/actions and remaining format/platform/workflow gates
are unfinished. The explicit NTFS tests do not establish a Unix restoration backend or universal filesystem support.
RAR analysis and feasible implementation remain before the final independent UX/UI/functional/security review after
the implementation loop. The full goal remains active.
