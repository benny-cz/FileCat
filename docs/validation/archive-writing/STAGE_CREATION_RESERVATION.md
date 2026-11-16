# Durable stage creation reservation checkpoint, 5 November 2026

Status: partial implementation over `2a7f242a`; no new write profile is registered. The full
[archive writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md) remains controlling.

## Implemented behavior

`ArchiveArtifactReservation` chooses an immutable nonce-scoped random leaf, artifact kind, native parent identity and
physical byte limit before any file is created. The reservation belongs to one retained directory instance. Native
NTFS/ext4/APFS stores accept that exact reservation for one exclusive creation attempt. A collision never opens,
overwrites or deletes the existing object and never silently substitutes another random name. The existing convenience
creation method uses the same reservation mechanism; this alone does not journal its callers.

`ArchiveJournaledStage.Create` checks the current schema, initial phase/sequence, nonce, parent, exact path, stage kind,
destination directory and physical bound before appending anything or calling the native factory. It synchronously
flushes a complete Reserved fact with no stage identity, attempts native creation, reviews the fresh empty private
stage through its retained handle and synchronously records the actual identity in a second Reserved fact. Exceptions
after durable intent require recovery, stop writes and close any returned stage; no evidence is deleted.

Creation review is deliberately distinct from sealed content/metadata observation. It checks the native stage role,
fresh writable/empty state, exact reserved name/parent/bound, native namespace and private security. Full metadata and
content observation still requires sealing. The first live Unix run exposed an incorrect use of that full observer
during creation; the corrected implementation uses this narrow creation check and leaves the seal requirement intact.

Archive transaction schema 3 adds an immutable `stageMaximum` and permits an unknown stage identity only in Reserved
or NeedsReview. It can become known once, cannot change or disappear, and cannot be used to advance an unbound stage
to rebuilding or a resolved outcome. Verified lengths cannot exceed the recorded bound. Identity binding consumes a
normal bounded transition and permits exactly the Reserved-without-identity to Reserved-with-identity repetition.
All other ordering, line-size, checksum, identity, provenance and publication gates remain in force.

Schema 1 remains read-only and requires conservative provenance review. Schema 2 retains its prior readable completed
history and retention behavior; it cannot be appended as new history. Neither schema can be upgraded in place or mixed
with schema 3 under one transaction/header. The recovery report explicitly explains an unrecorded identity and displays
the reserved byte limit. Generic closure and pruning retain unresolved reservations, including one whose path does
not exist. A recorded path or prefix grants no cleanup or publication authority.

## Crash and regression scope

Tests terminate a real child process externally at three positively acknowledged checkpoints:

1. Reserved intent flushed, before native creation: journal retained, no stage file.
2. Native private stage created, before its identity append: exact path retained as evidence, identity remains unknown.
3. Identity fact flushed: fresh parent/native recovery leases can compare the recorded identity; stage remains read-only
   on recovery and the journal remains unresolved.

Every child is waited/reaped. Recovery is read after actual process termination. Tests assert unchanged sentinel bytes,
no final publication, no generic closure/pruning, and no name-based cleanup. Additional tests cover collisions, repeat
attempts, wrong directory instances, mismatched durable reservations, immutable identities/bounds, malformed bounds,
older schemas, creation failure, post-create journal failure and read-only UI reporting. These are process-crash checks;
they establish neither power-loss nor directory-entry durability.

Sidecar reservations/durable creation, manifest/provenance integration with preparation, all remaining journal phases,
final metadata/origin application, publication/replacement/reconciliation/rollback/exact cleanup/recovery and the full
crash matrix remain unfinished. Production capture/registry, global budgets, cooperative cancellation, other formats,
package/RID/filesystem/fuzz/scale/oracle/GUI qualification and the final whole-goal independent review are also pending.
RAR licensing analysis remains deferred until that full implementation and review loop is complete.

## Observed validation

| Native environment | Core regression | Headless UI | Additional native checks |
|---|---|---|---|
| Windows 11 x64 VM / NTFS | 1,067 passed, 121 skipped; 1,188 total | 218 passed | Storage/crash: 78 passed, 21 skipped; 99 total. Confined lane: 33 passed, no skips |
| macOS ARM64 / APFS | 1,141 passed, 47 skipped; 1,188 total | 218 passed | All three external crash checkpoints passed again with the final harness |
| Ubuntu x64 VM / ext4 | 1,154 passed, 34 skipped; 1,188 total | 218 passed | Confined lane: 44 passed, one platform-only skip; 45 total. Three crash checkpoints passed again with the final harness |

All final checks above passed. The Core/UI/storage regression filter was
`FullyQualifiedName~Archive|FullyQualifiedName~EditSession|FullyQualifiedName~BackingArchiveTarget`.
Windows/Linux confinement used the tracked native runners. Skips are explicit `NotExecuted` test outcomes, not passes.
SDK 10.0.101 builds completed with zero warnings/errors; Windows/Ubuntu test runtime was 10.0.14. The Mac runner used
`/Users/benny/.dotnet`; its runtime inventory contains 10.0.1 and 10.0.12, with 10.0.12 selected by normal patch roll-forward.
The Mac tests did not independently record a runtime attestation; they do not establish testing on 10.0.14.
Earlier checkpoint documents' blanket Mac 10.0.14 claims have been corrected to preserve that uncertainty.
Windows facts show integrity
12288 before/8192 after and exit 0. Administrator membership/privileges remain: this is not ordinary filtered-user,
installed-package or complete security qualification. Headless UI is not live GUI acceptance. Mac confinement remains
unqualified; its child compatibility and native file/crash tests do not supply that missing boundary.

The first Mac/Ubuntu attempt had three failures from the premature full metadata observation; v2 corrected it.
The first Windows storage attempt had two failures from a test-only reflection lookup that missed an internal method;
v3 corrected it. The final harness was then rebuilt and all three Unix crash cases rerun on both machines. Unchanged
Core/UI suites were not repeated for that Windows-only harness lookup change. The first Ubuntu native invocation
passed the package directory instead of its executable, producing fourteen configuration failures; the corrected v3
invocation passed. The native runner now requires a regular executable file, and an explicit directory-refusal check
returned 65 before creating results or starting the test service. Earlier failed attempts remain alongside final
evidence; counts are not combined across retries.

Every test/helper process started by this task exited or was explicitly terminated and reaped. Final Windows/Ubuntu
checks were idle. The Mac crash runner's final census was idle; another tester subsequently started FileCat (PID 18065),
which was left untouched. Only saved evidence and awake assertions were read afterward. The existing caffeinate
assertions still showed UserIsActive, PreventUserIdleDisplaySleep and PreventUserIdleSystemSleep. The shared checkout
remained tracked-clean on `main`.

19-file implementation source overlay v2 SHA-256:
`420244E1A94D7028C2A04809E9C32D0197EDBB75EF33A73432DA8B2764BAA041`.
20-file final source overlay v3 (adds the corrected Linux runner and final Windows-only harness lookup):
`310D731B98B15145894A998EBC6CE537379B77BBD89FE83AF722E88A47B9285A`.
Windows managed overlay v2/v3, 84 files each:
`B3701994BA3CB3C14BC5ECEB504ED515574ED6EF33450256C3EAAA94D5DD16FF` /
`4069ADA93E4482E7EAA0DBCB2A125F96986078EA9778631C2E4449C60BB71D78`.

| Tested artifact | SHA-256 |
|---|---|
| Windows Core.dll | `A5D0456E852A1C9304DF9100F0EC849C37B30BC3EA11718C151EDB6F16388736` |
| Windows Core.Tests.dll | `D41F9F391468FDB0B1A43322AA73CAF9BDE33C91EC2BB8B109738A1A29233E28` |
| Windows Platform.Windows.Tests.dll | `0B105AAB58F13182EE89E1D6B05646947C165A4AADC0BE0644E2D524C3BBE979` |
| Windows final CoreCrashHarness.dll | `363C4E3831F8D568B0008A2EB404318B7623C6A2B2ED7595F3FD6B9DED70D793` |
| Windows application FileCat.dll | `5D112445483953BC2A37C0DE935F8C40F6CF2CD65AEEE76DE026E2900B8BC62D` |
| Windows probe FileCat.WorkerProbe.dll | `41BFC34AB3E4FDDC6AB4A21C6480E68050137F32C0E12535562D3FC31E10380B` |
| Mac Core.dll | `44EA903997C438C151656846D1649F3E4F95F285208591590A189A48D78F61FC` |
| Mac Core.Tests.dll | `048BB78236902FBD754009FA00A54FD21639B35204A287D9F1D42C5A840CB85A` |
| Mac final CoreCrashHarness.dll | `24CAABC85C60F8F275B5C199923CE71535C5FAA38666C3F55D8BBA048FF3BA35` |
| Ubuntu native probe Core.dll | `20F181E1400A4340FC2901F2D857990C2DD9CCEC03180C890EA71DC69D39FA4F` |
| Ubuntu native FileCat.WorkerProbe.dll | `B327453798851AF07B8104F8E09EC6934D3809BC4EC0639CC08156E9F6A30C0C` |
| Ubuntu Core.Tests.dll | `A424088D72CF7425CE81E561BAAD862EAFDC81CF8FD9D35AA24896B638D9F67D` |
| Ubuntu final CoreCrashHarness.dll | `2BFACDD7BD0170870AA4657400761EEEE203719E9578048BE5FE522B85C1C7EC` |
| Corrected Linux runner | `4F5ECB54EC007B5B4DB23605CF0713FDB7EDB24A35E8682154DE30DC29D8D547` |

Raw TRX, launch facts, failed attempts and collected evidence archives are retained under ignored
`.tmp/test-results/archive-writing/stage-reservation-v2` and the owned guest directories. Overlays are retained in
ignored `.tmp`. No third-party source or runtime dependency was added or upgraded.
