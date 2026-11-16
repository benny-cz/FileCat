# NTFS recovery-process crash qualification

6 November 2026. Follow-up to `1fc8d354` and [interrupted cleanup recovery](NTFS_CLEANUP_RECOVERY.md).
This is a component checkpoint; writer profiles and production recovery actions remain disabled.

## Implementation

`ArchiveContentCleanupRecovery` now owns common durable reconciliation and journal closure. Its internal platform
lease supplies the retained journal, fresh target/candidate/catalog and operations on actual held artifacts.
`WindowsArchiveCleanupRecovery` keeps its NTFS-specific admission and native object checks. A recovered file still
cannot acquire creator-owned stage or publication authority. The native adapter closes an unlinked artifact before
the common coordinator appends its outcome; a crash in that gap is reconciled as absence by a fresh scope.

The actual-process harness wraps those lease operations only in test code. It uses the production coordinator and
native adapter, pauses after a flushed checkpoint and is killed externally by the test driver. Production code has
no checkpoint callbacks. Every case first kills a real updating parent to leave a retained journal/catalog, then
starts and kills a different actual recovery parent. The driver waits for bounded Windows handle rundown before
reviewing the remaining objects.

The 31 cases cover opening/review, all six deletion intents, all six unlinks before outcome append, all six recorded
outcomes, absence, completion and journal closure. Additional cases retain an intact original and kill recovery after
injected failures before/after unlink have durably recorded a NeedsReview hold. A final fresh native scope must
preserve the current archive bytes, account for exact remaining artifacts, distinguish confirmed deletion from
absence, retain the old-byte snapshot until last and close only after reconciliation. Finished journals refuse
reopening. The unchanged sentinel provides an independent unrelated-file check.

## Validation

| Actual lane | Passed | Skipped |
| --- | ---: | ---: |
| Windows 11 VM / NTFS / .NET 10.0.14, native/LPAC/application | 175 | 0 |
| Windows common archive/edit regression | 1,389 | 212 |
| Windows recovery/archive UI | 242 | 0 |
| Windows native storage control | 128 | 176 |
| Mac arm64 / .NET 10.0.12, common archive/edit regression | 1,536 | 65 |
| Mac recovery/archive UI | 242 | 0 |
| Ubuntu x64 / .NET 10.0.14, common archive/edit regression | 1,549 | 52 |
| Ubuntu recovery/archive UI | 242 | 0 |
| Ubuntu native containment | 61 | 1 |

All 31 new actual recovery-process cases passed inside the 175-case native run. The driver reported three bounded
sharing waits after process exit, at most 96 ms. The actual Windows test parent recorded .NET 10.0.14, integrity 12288
before lowering and 8192 afterward, and exit 0. Its administrator-group membership limitation remains; this is not
ordinary standard-user qualification. All builds completed without warnings/errors.

Mac and Ubuntu each independently read back 12 zstd, 11 bzip2,
11 gzip and 8 timestamp fixtures. Each skipped all 107 Windows-only cases, including the 31 new recovery-crash cases;
these skips do not qualify an NTFS backend on Unix. All three runners completed their own process-idle checks;
final Windows census found no FileCat/dotnet/testhost instances. The Mac's temporary until-reboot keep-awake helper
and assertions were retained.

The refreshed development Windows app has an SPDX 2.3 inventory of 261 exact files, all SHA-256 values rechecked
against disk, with 34 reviewed NuGet packages and one runtime pack (235 restored files matched to their archives).
No dependency was added. This remains a dirty-source development artifact, not a release-qualified package.

## Evidence identities

Ignored raw evidence is retained in `.tmp/test-results/archive-writing/recovery-crash-v1` and
`recovery-crash-artifacts-v1`, with guest archives in this task's validation directories.

| Artifact | SHA-256 |
| --- | --- |
| Six-file source delta over `1fc8d354` | `5C8901DC956D179555AC4897DC3C55FD822E58A453FB88FE7DFBD16FBDFA05E6` |
| Windows binary/runner overlay | `9CAF4421FEB4B75B60295CF39D35D62E6EE8A241585A42F1366897C4C2AD0F6C` |
| Windows broad/native evidence | `5C6F98B137CF22D3C37C48254EF580D806B9C29197243E450F1D4291DAC917D7` |
| Mac broad/oracle/guard evidence | `4FE2CEA49A84DDB71637743D24BBA93A4438D88456E1F091B5EF9BD53287A4DC` |
| Ubuntu broad/native/oracle/guard evidence | `4C3DA4267FFC622691DED57F311ED016186148D8B1E50916CD7156586296AE79` |
| App SPDX inventory, all 261 payload hashes rechecked | `70BD747A07283AC99751298D793E38F2245210FC64A03C6623A0FB7BA704241A` |

## Remaining work

Restoration of retained archive bytes, production recovery actions, early incomplete catalogs and the remaining plan
gates remain unfinished. These process-crash checks do not establish power-loss durability or general filesystem/policy
qualification. No new writer profile is enabled. RAR analysis and feasible implementation still precede the final
independent UX/UI/functional/security review after the implementation loop.
