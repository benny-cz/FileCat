# Private creation process-crash checkpoint, 5 November 2026

Test-only follow-up to `1c732ab8` and [private new-file publication](PRIVATE_NEW_FILE_PUBLICATION.md). Production source,
profile availability and recovery permissions do not change. The full archive-writing plan remains incomplete.

## Actual process termination and recovery

The shared CoreCrashHarness now prepares a real empty TAR in a private native stage. Two distinct actual controlled
child processes run transformation and fresh verification and must exit successfully and be reaped before the parent
can signal a publication checkpoint. These ordinary test children do not qualify a confinement boundary; the earlier
Windows LPAC/Linux confined-worker replays remain separate evidence.

The harness wraps the existing native interfaces, observes the actual coordinator's durable journal facts, signals a
checkpoint and waits. The test driver verifies the parent remains alive, terminates it externally with its process
tree, waits for actual exit, then runs fresh recovery scans. Production code has no new crash hooks.

Ten boundaries per platform are covered:

- Creation-metadata reservation before native creation; identified empty file; one flushed byte; sealed complete file
  before its version append; sealed/read-back/recorded version.
- Durable ReadyToPublish; durable Publishing before the claim; successful native claim before observation/journaling;
  durable Reconciling; durable CommittedWithRetainedArtifacts before ordinary journal closure.

Recovery assertions distinguish an absent, empty, partial, complete-unrecorded and recorded metadata artifact. They
check exact framing, recorded hashes/lengths, the stage's verified bytes, the actual old/final name, published-version
facts and security-sidecar binding appropriate to the checkpoint. An empty TAR is read back as empty. All other
sidecars and an unrelated sentinel remain. At the claimed checkpoint the final name exists even though no Published
version has yet been journaled; the test requires retention rather than inferring NotCommitted from the missing fact.

Repeated zero-retention/zero-age scans and stale generic closure attempts preserve every surviving file's path/hash
and the journal without appending an ordinary end record. Each platform then reopens the actual surviving stage and
identified sidecars through held native directory/identity checks, compares every recorded version and verifies write
access is closed. A recovered stage cannot obtain fresh creation-publication authority.

This does not cover power loss, failed directory sync, replacement/retirement/rollback, exact cleanup or user-facing
recovery resolution. There is no artifact-removal action between the current Cleaning and retained-warning records;
no cleaning-operation crash qualification is claimed. Those paths and their complete crash matrix remain unfinished.

## Validation and evidence

| Environment | Result |
|---|---|
| Windows 11 x64 VM / NTFS, runtime 10.0.14 | 23 passed, no skips/failures |
| macOS ARM64 / APFS, runtime 10.0.12 | 23 passed, no skips/failures |
| Ubuntu x64 VM / ext4, runtime 10.0.14 | 23 passed, no skips/failures |

Each 23-case replay comprises the ten new creation boundaries and the existing three stage and ten manifest/origin
creation crashes. SDK 10.0.101 builds completed without warnings/errors. The Windows lane uses the existing actual
medium-integrity launcher, retaining its admin-membership limitation. No production code changed, so the prior broad
Core/UI/native qualification checkpoints remain the applicable regression evidence.

Mac/Ubuntu initially postponed for another tester and subsequently ran when idle. Final runners found no remaining
test processes. A different Mac GUI instance started after completion from `dsa-20261105-r47-navigation/app`; it was
preserved while completed evidence was collected read-only. Mac keep-awake assertions remain until reboot. Shared
`E:\FileCat` remains tracked-clean on `main`.

Raw TRX, terminal markers, build/run logs and Windows launch facts are retained under ignored
`.tmp/test-results/archive-writing/creation-crash-v1` and the owned guest roots.

| Transfer/result archive | SHA-256 |
|---|---|
| Seven-file test/harness source overlay | `436BD6577D37933012340D31221FD044A42AAFBC82872604FB1BEA9E4404455F` |
| Four-file Windows test/harness overlay | `315B00D0A8EF21FEF99F36066E3F77F5EB910C8BDA87FD791395452960928DC0` |
| Mac results/logs | `AF3E467DD25EDFA0DF22FB9740B99ACFA3EDBB7E13451B9573AF8C517D37B5EE` |
| Ubuntu results/logs | `5C8938E8248728A30E2CEB9BC8C404DD271C7D0774252F371B1FFFEA062DDB26` |
| Windows results/launch facts | `7CE72510943794B5C7397B01C117ED395A219F6E4E76B1777A87E33DCE6619FA` |

No new GUI/package/RID, full AW packet or release qualification is claimed. Whole-goal implementation/testing,
the subsequent independent UX/UI/functional/security review and the final RAR licensing analysis remain unfinished.
