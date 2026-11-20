# NTFS interrupted-cleanup recovery

6 November 2026. Follow-up to `ac6c3a44` and [live NTFS cleanup](LIVE_NTFS_ARTIFACT_CLEANUP.md). This remains an
unregistered backend; new writer profiles and production recovery actions are not enabled.

## Retained journal and native recovery

`WindowsArchiveRecoveryJournal` pins the actual local NTFS journal directory and opens its regular single-link file
relative to that handle, without following a reparse point. Its read/write handle denies another writer and namespace
replacement. Core consumes that exact stream; it never reopens a display path for append.

Reopening checks the caller's reviewed SHA-256, strict UTF-8, complete final line, every CRC, bounded record count/file
size and supported standalone archive history, then checks the checksum again. Damaged, stale, mixed, unsupported or
finished histories refuse append authority. The retained writer can append only recovery observations/outcomes and
closure, with native lease checks around durable writes. It cannot rewrite live preparation/content/cleanup facts or
append generic job effects. Recovery closure does not delete a source-manifest pathname.

`WindowsArchiveCleanupRecovery.Open` then pins the actual archive directory, compares its native identity, opens the
target with its normal deny-write source lease and classifies the current bytes/metadata as either the intact original
or the verified published output. A changed target refuses. It opens every remaining catalog artifact by exact native
identity, compares byte/metadata/origin digests, checks private security and complete stream inventory, and retains the
handles. Only the specific native name-not-found status establishes absence; access, sharing, type and other errors
remain refusals. Already processed names must still be absent; another occupant is kept for review.

Opening the scope performs no archive or artifact mutation. Explicit `ResumeCleanup` durably records a separate
`archive-content-recovery` version-1 history bound to the exact original content and cleanup records. Those histories
are frozen once recovery starts. The target is rereviewed between operations. Present objects receive a durable intent
before deletion through their held native handles. Files found absent receive an Absent outcome, not a claim that
FileCat confirmed their unlink. Separate bounded masks preserve that distinction. The original-byte snapshot remains
last in the inherited catalog order.

The recovery path has its own fresh native action checks. It does not turn a reopened artifact into a creator-owned
stage or grant fresh publication authority. Ordinary recovered read-only artifacts still fail live creator cleanup.
Cancellation and uncertain failures retain a review hold. A new scope can recheck an unfinished recovery; the recorded
target, catalog, cursor and outcomes must remain consistent. Once every catalog entry is accounted for, Completed
releases that scope's recovery hold and explicit journal closure succeeds. The archive bytes are never rewritten by
this cleanup operation. Restoration is a separate unfinished operation.

## Validation

| Actual lane | Passed | Skipped |
| --- | ---: | ---: |
| Windows 11 VM / NTFS / .NET 10.0.14, native/LPAC/application | 144 | 0 |
| Windows common archive/edit regression | 1,389 | 212 |
| Windows recovery/archive UI | 242 | 0 |
| Windows native storage control | 128 | 145 |
| Mac arm64 / .NET 10.0.12, common archive/edit regression | 1,536 | 65 |
| Mac recovery/archive UI | 242 | 0 |
| Ubuntu x64 / .NET 10.0.14, common archive/edit regression | 1,549 | 52 |
| Ubuntu recovery/archive UI | 242 | 0 |
| Ubuntu native containment | 61 | 1 |

The 144 native cases include the previous 131 and thirteen new cases. Eight kill a real updating parent externally,
then open a fresh recovery scope and finish cleanup: before publication/private-ready, native commit before its outcome
append, recorded commit, and interrupted deletion intents/unlinks/outcomes including the final snapshot unlink. They
verify unchanged current archive bytes, exact cleanup completion, honest absence outcomes and journal closure. Five
refuse a changed target, changed artifact metadata, stale journal checksum, active journal writer or missing catalog,
preserving all remaining paths/hashes and journal bytes.

Sixteen new common checks cover strict retained reopening, unsupported/torn/mixed/finished histories, immutable scope,
target/metadata/count/mask changes, live-reference authority and deletion/absence accounting. All builds completed
without warnings/errors. The Windows launcher recorded integrity 12288 before lowering and 8192 afterward, exit 0;
its admin-membership limitation remains and does not establish standard-user qualification.

A test-only follow-up explicitly compares the absent/deleted masks with the filesystem before recovery. Its first
49-case replay passed all recovery assertions but failed three fixture teardowns with transient Windows sharing
violations. A second replay caught the same condition during the first evidence read and recorded another cleanup
wait of 33 ms. The crash fixture now waits at most five seconds for an exclusive lease on every remaining evidence
file after reaping the killed process, and uses the same bounded handling for teardown. Only sharing/lock violations
are retried, delays are reported, and persistent locks still fail. These changes affect the test driver, not production
recovery admission or artifact deletion.
The final 49-case replay passed without skips, recording one 91 ms wait during the exclusive evidence-readiness check.
Its actual launcher again lowered integrity from 12288 to 8192 and exited successfully. Final test-only guard builds
on Mac and Ubuntu retain the same platform refusals; they do not supply native Windows qualification.

Mac and Ubuntu initially deferred for other live testers and started only after fresh idle checks cleared. Each Unix
run independently read back 12 zstd, 11 bzip2, 11 gzip and 8 exact-timestamp fixtures. Each correctly skipped 76
Windows-specific cases; those skips do not qualify an NTFS recovery backend on Unix. Final own test/FileCat processes
were closed and the Mac's temporary until-reboot keep-awake helper remains active.

The refreshed development app has an SPDX 2.3 inventory of 261 exact files, all SHA-256 values rechecked against disk,
with 34 reviewed NuGet packages and one runtime pack (235 restored files matched to their archives). No dependency was
added. This is a dirty-source development artifact, not a release-qualified package.

## Evidence identities

Ignored evidence remains in `.tmp/test-results/archive-writing/content-recovery-v1` and `content-recovery-artifacts-v1`,
with corresponding guest archives in this task's validation directories.

| Artifact | SHA-256 |
| --- | --- |
| Broad-run eleven-file source delta | `4E1BD2FB135F06523FF35536E1E394F1B67856FEDCB0D258D7F4E5059CFCA857` |
| Final source delta, including explicit masks and bounded crash-fixture checks | `1E58DBC3E0EA9A5A6E3748A385CA975E47448AD92B2D0152ACF06EFBA11FD174` |
| Windows binary/runner overlay | `6F71F357B2EB2CD84291755F3A514BB9DD68B46AA0BE1031EC3B9EEFC8790C35` |
| Final test-only DLL/PDB overlay | `8FC9D4547257B4F7C82AF2DD580FC009B58378120EE4BE3D356F935AC48FEC57` |
| Windows broad/native recovery evidence | `091D498048532BF095AE6501FD95C1C2C10B097FA7BE573A179E7B63B18489F0` |
| Windows final 49-case crash/mask replay | `47D3FD5ADC86DA6CB80AE04839BFDFF30EDE2E4C3558471BCB8B30660B4D06A6` |
| Mac broad/oracle evidence | `25832CC40BA3AC708A4DDEA93C4B94334F0916884376F038AD01E138B2D2E744` |
| Mac final test-only build/guard evidence | `5381674386025FC0ACB90F6EE2CE8D0CF63C21EEC332C908C0349AD3B3C13302` |
| Ubuntu broad/native/oracle evidence | `790659B5FE0A0D76D0B92B0747E63224F72C1AAC160478DEC25439EC2EDBA70E` |
| Ubuntu final test-only build/guard evidence | `549FA1D97D4700629313B7A8ED76C80A58654E31AFF95D84C62C8CA5D99364E3` |
| Final app SPDX inventory, all 261 payload hashes rechecked | `162C53EE5DFC22024D67A8C10BA22049CBC9EBDF572CDCC84E99582B28BCF99B` |

## Remaining work

Actual process termination during the new recovery operations themselves, restoration of retained archive bytes,
production recovery UI/actions, early incomplete catalogs and the rest of the plan remain unfinished. Unsupported or
incomplete evidence stays available for read-only review. No power-loss durability, universal filesystem/policy
coverage, complete resource/timeout/alias qualification or full hard-link support is claimed. Unix update/recovery,
complete capture/global budgets, other formats and production writer workflows remain open. RAR analysis/feasible
implementation still precedes the final independent review after the implementation loop.
