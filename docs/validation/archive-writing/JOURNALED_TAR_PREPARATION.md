# Journaled native TAR preparation checkpoint, 5 November 2026

Status: partial implementation over `c0909f71`. No new writer profile is registered. The complete
[archive writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md) remains controlling.

## Implemented flow

`ArchiveJournaledTarPreparation` connects retained native sources, private native sidecars, the confined transform,
fresh verification and the shared durable job journal. All blocking native hashing, evidence IO and journal callbacks
run away from the UI thread. A prepared result owns its stage/evidence handles; the caller still owns its source leases.

1. Freeze and rereview the exact source/input bindings. Refuse native identity aliases and a direct output-as-input.
   An update must bind the retained original's name and native parent identity to the actual destination. New directory
   capture is not supplied by this bridge and refuses before artifact creation.
2. Compute exact encoded sidecar sizes and the reviewed stage output bound. The assessed source manifest (updates),
   expected manifest, observed manifest and raw origin evidence share a 128 MiB physical evidence budget. Reserve their
   exact names/roles/bounds before creating a file. These checks do not implement the remaining global/job heap, lease,
   source-snapshot, free-space or cross-parent scheduler budgets.
3. Flush Reserved intent with every sidecar name. Exclusively create and record the stage identity. Create each
   assessed/expected/provenance sidecar, flush its identity before data writing, encode it, natively seal/flush it,
   read its complete canonical framing/checksums through the retained read view, rereview its native version/metadata,
   and flush the sealed version into the same transaction. Creation uses the existing private-at-create native stores.
4. Rereview evidence and flush Rebuilding before claiming stage writes/starting the transform. Wait for the worker's
   completed finalizers, receipt, actual exit and reaping. Flush Finalizing before parent sealing/flush.
5. Capture the sealed stage's complete native version and flush it in Verifying before starting a fresh read-only
   verifier. Finalized and verified stage versions are distinct facts. The latter must equal the former.
6. Retain the verifier's actual complete immutable observed manifest, not only its three digest strings. Encode that
   manifest in its pre-reserved VerifiedOutput sidecar, with its own identity-before-write and complete native/codec
   readback. Rereview the exact stage, every source and every sidecar again.
7. Flush the verified stage version while still in Verifying and return an unpublished preparation. No security
   snapshot, ReadyToPublish, final namespace name, replacement, deletion or successful job end is manufactured.

Failure after intent requires recovery. Returned partial handles have writes stopped and are disposed; exact files
and their durable evidence remain. A failed/incomplete evidence writer never manufactures a sealed version. No
filename/prefix cleanup or in-process archive codec fallback is introduced. Source and evidence observations remain
queried metadata, not proof of complete security admission or an equivalent-identity snapshot guarantee.

## Journal and native contracts

Schema 4 adds a frozen sidecar reservation set and an explicit finalized-stage version. Roles distinguish assessed
source, expected output, verified output and origin evidence. Each role/path is unique, native identities cannot alias
another transaction object, byte bounds and provenance source counts are scoped/immutable, and identity then sealed
version advance monotonically. Initial reservations cannot later disappear, reorder, change bounds or acquire another
role. Each reserved identity/version step uses a separate bounded transition. Verifying may acquire only the reserved
observed manifest and then the verified stage; the final metadata/publication gates remain separate.

Required preparation sidecars must be sealed before rebuilding; the observed sidecar is reserved for after fresh
verification. A current in-memory parent snapshot must be the exact already-flushed journal fact before native
sidecar creation. Deserialized/lookalike/stale facts cannot authorize that action. The finalized stage must identify
the reserved object, fit its physical bound, remain immutable and match any later verified version. Finalizing uses
explicit enum value 12, preserving existing phase IDs. Earlier schemas remain readable/read-only under their existing
retention rules; schema 1 still lacks complete provenance evidence.

Native `IArchiveRetainedArtifact` distinguishes creation review, evidence writing/sealing and stage rebuilding.
The retained stream/handle stays parent-owned. Evidence claim refuses a stage; stage claim refuses evidence roles.
Private permissions, current namespace/name identity, exact creation kind/bound and fresh empty state are checked.
Data writes close before complete hashing/readback. Sealing uses the previously qualified native flush calls; it
does not claim directory-entry or power-loss durability or remove every OS right from the trusted parent handle.

The recovery report lists all reserved sidecar roles, paths, bounds, recorded/unknown identities and sealed versions,
as well as a finalized-stage version when present. It performs no filesystem inspection or mutation and retains
incomplete/unknown evidence. Generic finish, closure and pruning cannot hide an unpublished preparation.

## Crash/regression coverage

The real external-termination matrix covers both manifest and provenance files at five acknowledged checkpoints:
reservation flushed before creation; identity flushed before writing; after exactly one data byte; native seal/full
flush before the sealed-version append; and after that append. Every killed process is waited/reaped. Fresh native
recovery handles compare known identities and recorded complete versions without becoming writable. Partial/unknown
versions stay unresolved, sentinel bytes stay unchanged and no final archive appears. Existing three stage-creation
crash checkpoints remain covered. These are process-crash checks, not power-loss qualification or the complete
publication/rollback/recovery crash matrix.

Actual create/update smoke paths independently read every native sidecar back and compare complete canonical
manifest digests and provenance records with the retained sources. Test launch callbacks inspect the on-disk journal
to verify Rebuilding precedes transform startup and a finalized version precedes fresh verifier startup. Failure cases
cover empty creation, worker startup failure, cancellation at verifier startup, a changed completed sidecar, exact
source/destination binding, direct self-inclusion, malformed/aliased/mutated reservations and old-schema inspection.
Native smoke cases record the actual test-host runtime and OS in TRX output.

Remaining work includes full capture/folder/snapshot/scalable input contracts and declared source defaults, complete
outer metadata/security admission/application and strongest Unix origin aggregation, publication/replacement/retirement,
reconciliation/rollback/exact cleanup/actionable recovery, all other format/ZIP migrations, production registry/session/UI
integration, cooperative cancellation/global budgets, runtime/package/RID/filesystem/fuzz/scale/oracle/GUI qualification,
and the whole-goal independent UX/UI/functional/security review. RAR licensing analysis remains deferred until that loop
is complete.

## Observed validation

| Native environment | Core regression | Headless UI | Additional native checks |
|---|---|---|---|
| Windows 11 x64 VM / NTFS | 1,095 passed, 138 skipped; 1,233 total | 219 passed | Storage/crash: 90 passed, 21 skipped; 111 total. LPAC lane: 33 passed, no skips |
| macOS ARM64 / APFS | 1,186 passed, 47 skipped; 1,233 total | 219 passed | All ten sidecar and three existing stage process-crash cases passed; Mac confinement remains unqualified |
| Ubuntu x64 VM / ext4 | 1,199 passed, 34 skipped; 1,233 total | 219 passed | Confined lane: 44 passed, one platform-only skip; 45 total. Ten sidecar/three stage crash cases passed |

All final v4 checks above passed. Skips are explicit `NotExecuted` outcomes and are not passes. The broad filter was
`FullyQualifiedName~Archive|FullyQualifiedName~EditSession|FullyQualifiedName~BackingArchiveTarget`; native lanes used
the tracked Windows/Linux runners. SDK 10.0.101 builds completed with zero warnings/errors. Actual test-host evidence
now records macOS 27.0.1 / .NET 10.0.12, Windows build 26340 / .NET 10.0.14 and Ubuntu 24.04.5 / .NET 10.0.14. The Ubuntu
test host resolves `/home/benny/.dotnet/shared/Microsoft.NETCore.App/10.0.14/`, separately from the private SDK runner.
Windows launch facts record integrity 12288 before / 8192 after and exit 0; administrator membership/privileges remain.
These are not ordinary filtered-user, installed-package or live GUI acceptance claims. Mac native IO/ordinary child
compatibility supplies no missing hard worker memory boundary. Linux service metrics describe the driver, not worker RSS.

Initial smoke: Ubuntu's 17 focused schema/native create/update cases passed. Mac smoke postponed before testing when
another FileCat instance appeared after its build. Later broad v1 checks passed before destination binding and explicit
Finalizing/finalized-stage evidence were added. In v2 two Unix refusal tests failed during assessment because an unused
TarWriter produced a zero-byte fixture; canonical 1,024-byte TAR end markers corrected the fixture so it reaches the
actual name/parent refusal. v3 passed with those corrections and finalization records. Final review then prohibited a
phase change from concealing missing identity/seal facts; v4 includes its regression and supersedes the earlier counts.
No test counts are combined across retries. Earlier artifacts/results remain in the task-owned validation roots.

The final runner censuses and evidence collection found no FileCat/test helper left by this task. The Mac's temporary
caffeinate processes 1339/6428 remained running, and the shared `E:\FileCat` checkout remained tracked-clean on `main`.

32-file final source overlay SHA-256:
`80BB911E2DA31D660ED9FC1F450FCD1A2D9DA2A397F84A554551DD6E3452C6A9`.
84-file final Windows managed overlay SHA-256:
`05639599574A2BA57AABE194938BB8430FBD663868F234140F866129133458E8`.

| Tested artifact | SHA-256 |
|---|---|
| Windows Core.dll | `C4A912EE31F07D95221E046440DFAB6BE6337D76D986FBAA527DD92A9CA695EC` |
| Windows Core.Tests.dll | `5B360A74CB7794D84334012434303013D0ECE12CE5BB7665EBD374604FCFF040` |
| Windows CoreCrashHarness.dll | `FA057F0450CE83D4C620CD0E3BA4FAF11B1B136A42A673750C0E7D8F231BEA5D` |
| Windows Platform.Windows.Tests.dll | `116E5359B4E17455F0768FD3EBA4BDFCCB375373CFA9DAB76ABE047EC2FA98EB` |
| Windows application FileCat.dll | `27729399AA94DB99A8DE13E6332C5AA91E74AFFA5AAFBB06E16DCB2BD7F65DE5` |
| Windows probe FileCat.WorkerProbe.dll | `ECB8816101825BAA3E6E82F2677FE37C58D6CA2B9C4311E264BFA52DF2126E28` |
| Mac Core.dll | `9ED41809191ED8E80E3F44809C8AC597C6135259CECFE65AFAEC2D4921A4FC4D` |
| Mac Core.Tests.dll | `01E7A662C17EACE9C10121F5F8D5EE2876DC5B725ABAABD2F90C1EDAD5FF15DC` |
| Mac CoreCrashHarness.dll | `C4BEF5481BC7C5D327E566C9414415B4F889C8637A07912A97296941F1EF83CE` |
| Ubuntu native probe Core.dll | `4852F6787B392819D9CF3B85288764D9382A93C96D9DE4F7783E8215C714EEF0` |
| Ubuntu native FileCat.WorkerProbe.dll | `DBC3106D7769CBB6B31B1D1C1E15188159617BDCE3FFA71FE9C7E1FA1BAE4A46` |
| Ubuntu Core.Tests.dll | `36BAF1CB26D211C2ABBB353415E42B0C9DDFEC71C628E79E05AAA17FAAA00079` |
| Ubuntu CoreCrashHarness.dll | `0D020A9B7F2F8888579A523ED3F5271AC9D4A0D55F5BB34153E74E8224FEA6B6` |

Raw overlays, TRX, launch facts and evidence archives are retained under ignored
`.tmp/test-results/archive-writing/journaled-preparation-v4` and the task-owned guest roots. No new third-party source
or runtime package was added or upgraded. Complete release/feature qualification and the final independent review are
still unachieved.
