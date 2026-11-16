# Retained native stage pipeline checkpoint, 5 November 2026

Status: partial implementation, no new writer profile registered. This builds on `9d7f01ab` and the
[confined TAR transform](TAR_TRANSFORM_WORKER.md). The full
[archive writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md) remains controlling.

## Implemented bridge

The unregistered `IArchiveRetainedFile`/`IArchiveRetainedStage` adapters connect existing held NTFS/ext4/APFS file
objects to the worker clients. `ArchiveFileObservation` binds the native object/content version, held parent identity,
single leaf name, queried outer metadata digest, raw origin observation and representable new-member mode/time.
Observations are parent-only and are never sent to the codec. They are not complete security/metadata admission or
permission to publish.

Observation brackets a full content hash with outer metadata and current directory/name identity checks. Multiple
links are refused in these bindings. Directory/name rereads compare native identities rather than relying on a saved
pathname or prefix. Windows name observation uses an attribute-only relative open with compatible sharing; actual
data reads continue through the original held handle. No new data writer or worker file handle is granted.

Read views have independent bounded cursors and are read-only. Disposing a proxy view closes only that view, preserving
the parent's native file lease. Owner disposal, changed length, premature/invalid reads and invalid seeks refuse.
Views check the reviewed object/name/metadata at admission; complete worker hashes and final native rereviews remain
necessary. This is not a simultaneous file/tree snapshot or protection from equivalent-identity ABA races.

`ArchiveRetainedTarPipeline.RebuildAndVerifyAsync` now:

1. Freezes the captured binding map and checks the exact reviewed source/input versions and new-member metadata.
2. Refuses stage/source/input identity aliases and repeated captured IDs disguising the same native object; repeated
   payload references use the existing single captured ID and wire aliases.
3. Rereviews all retained inputs before launching, then claims only a fresh empty artifact created as a Stage with
   exactly the parent-computed output bound. Manifest/provenance/recovery/renamed/reused artifacts are not fresh stages.
4. Runs the confined transform through disposable read views, waits for actual exit/reaping, irreversibly closes the
   stage's data-write capability, performs native sealing/flush and captures its complete version/queried metadata.
5. Verifies that real sealed file in a fresh read-only worker against the parent expectation.
6. Rereviews the stage and every retained input again, including zero-byte inputs which required no payload reads.
7. Returns `ArchiveVerifiedNativeStage`, an unpublished observation/result only. Every exit through the preparation
   body, including cancellation and failed worker setup, closes stage writes and retains its exact artifact.

The existing native fsync/F_FULLFSYNC/Windows flush paths are used. Sealing and native hashes/rereviews run away from
the UI thread. The pipeline chooses no publication name, performs no rename/delete, writes no ReadyToPublish record
and applies no final outer metadata or origin aggregation. The caller still owns leases, artifact reservations,
journaling and recovery. Closing managed data writes does not claim to remove every OS-level right from the trusted
parent's private native handle.

At this checkpoint Unix inputs finer than 100 ns and special mode bits refused. Later
[permission-default corrections](CAPTURED_MEMBER_DEFAULTS.md) and [exact timestamp capture](EXACT_TAR_TIMESTAMPS.md)
supersede those input restrictions: ordinary Unix bits and Windows 0644 are the new-file defaults, and PAX inputs
retain native nanoseconds. Full native metadata remains reviewed. These member defaults do not copy complete native
ownership/ACL/xattr metadata into an archive member.

Review caught a consistency gap before this checkpoint: the native digest was compared without separately matching
the exposed derived member fields. Native review/view admission now matches version, scope, digest, raw-origin digest,
member mode and timestamp together. Forged derived metadata beside an unchanged digest is rejected on both backends.

## Observed validation

| Native environment | Affected Core regression | Affected headless UI | Additional native lane |
|---|---|---|---|
| Windows 11 x64 VM / NTFS | 1,051 passed, 112 skipped; 1,163 total | 217 passed | Storage subset: 69 passed/21 skipped; confined lane: 33 passed/0 skipped |
| macOS ARM64 / APFS | 1,116 passed, 47 skipped; 1,163 total | 217 passed | Actual held-file/native-stage IO, controlled ordinary child compatibility; Mac confinement remains unqualified |
| Ubuntu x64 VM / ext4 | 1,129 passed, 34 skipped; 1,163 total | 217 passed | Confined lane: 44 passed/1 platform-only skip; 45 total |

There were no failures. Skips remain visible in TRX and are not passes. The broad regression/storage filter was
`FullyQualifiedName~Archive|FullyQualifiedName~EditSession|FullyQualifiedName~BackingArchiveTarget`; native lanes used
the tracked Windows containment and Linux archive runners. SDK 10.0.101 builds completed with zero warnings/errors;
Windows/Ubuntu test runtime was 10.0.14. The Mac runtime version was not independently captured for this checkpoint;
the later [reservation checkpoint](STAGE_CREATION_RESERVATION.md) corrects the earlier blanket runtime claim.
Windows launch facts record integrity 12288 before/8192 after and exit 0. Administrator
membership/privileges remain; this is not ordinary filtered-user or installed-package qualification. Linux service
metrics describe the driver, not sibling worker memory. Headless tests are not live GUI acceptance.

The new tests cover independent view ownership/cursors/short reads, owner disposal, real create/update stages, wrong
artifact roles/loose bounds, setup failure, early cancellation, native identity aliases, derived metadata forgery,
input changes after copying (including empty input), and stage mutation after fresh verification/reaping. Windows
create/update runs through both the LPAC probe and actual guarded application; Linux uses the actual native lane.
Distinct worker PIDs and exit/reaping are asserted, with Linux controller cleanup. Source files remain retained and
no final archive name is claimed. Full durability/crash, fuzz/CPU-hour, scale and independent oracle gates are pending.

The Windows v1 attempt postponed at its initial busy check; no tests were started and no instance was stopped. Earlier
Mac/Ubuntu v1 runs passed but precede the derived-metadata consistency fix. Final v2 artifacts/counts above supersede
them; counts are not combined. Final censuses found no FileCat/test process left by this task, and the Mac's temporary
until-reboot awake assertions were retained. The shared checkout remains on `main`.

14-file source overlay SHA-256: `4A9CDC8A95AA3954A9815369C9C1CD67EC9E35F21897589C4500E5172A3570B7`.
84-file Windows managed overlay SHA-256: `F8DAFE38907114B55127BF71084E55A11B2417427D1802D459708BC9BF61AB39`.

| Tested artifact | SHA-256 |
|---|---|
| Windows Core.dll | `8BF51686966F985C60CB67972101D05496C34969EC851C607F7FDF3451C2E36C` |
| Windows Core.Tests.dll | `B4E409C33ABB7222B1C57FAF24317C8FD20E06C0766808B888F69DE182632B1D` |
| Windows Platform.Windows.Tests.dll | `20C02559292963508038D506969A1B91507DDDFEF2A9F48003E9A47C84EEED3E` |
| Windows application FileCat.dll | `F0866284B4C59090E404981983318E640BB495BE299C2DA9C11CC9FE8C164F17` |
| Windows probe FileCat.WorkerProbe.dll | `617E0052A83C22CAE7F23E3CFD375586DE8636A4F301B0D69B864E72E2B3753B` |
| Mac Core.dll | `0C0B463CEDFA4F5307CF86EB76B1F4E0B0FEEBFAA9D32D9D9043CE6EB994F7A5` |
| Mac Core.Tests.dll | `7838BCE4C261B0E70BB0E99F9C212C7E19191EBD0F66C236F39621806BC31C78` |
| Ubuntu native probe Core.dll | `420A2F9B378BC752CEEF7A519012FF90A7B2B6C2F8B107EEF7A60D4BAF03728D` |
| Ubuntu native FileCat.WorkerProbe.dll | `B47B64B3196E93398004C5409A7B3354DE410B4E0FF43E353E66F4D1206EAAA4` |
| Ubuntu Core.Tests.dll | `E88A66D05A2161965BA3CB0ABADD2DD674C5FB21A59FA5A2444517D26EF05785` |

Raw overlays, logs, TRX and Windows launch facts remain under ignored `.tmp/test-results/archive-writing/retained-stage-v2`
and owned guest directories. Source/binary overlays are also retained in ignored `.tmp`.

Still required: production capture/contracts/registry, folder snapshots/aliases/self-inclusion and scalable input
storage, global/job lease and heap/space budgets, complete security/metadata admission/application and strongest origin
aggregation, durable sidecars/journal transitions, actual publication/reconciliation/rollback/exact cleanup/recovery and
process-crash integration, cooperative cancellation and cross-parent scheduling, every remaining format/ZIP migration,
complete runtime/package/RID/filesystem/fuzz/scale/oracle/GUI qualification, then the whole-goal independent review and
deferred RAR analysis. Only the existing per-directory lease limits apply at present; this bridge does not qualify
global leases or a 100,000-input snapshot repository. The goal is not complete.
