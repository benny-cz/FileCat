# Read-only archive recovery UI checkpoint

Date: 5 November 2026. Follows the [journal contract checkpoint](ARCHIVE_TRANSACTION_JOURNAL.md). This closes the UI's
direct staged-intent review path for archive journals; it does not implement native archive recovery actions.

All recovery entry points reread bounded archive facts through the existing off-UI IO service. A stale row cannot
authorize generic archive cleanup, transfer retry/new-transfer preparation, or closing an unresolved archive journal.
The generic staged-intent review returns without opening archive artifacts. Core's reader is now public for this
read-only use; its decoding/transition behavior is unchanged from the preceding journal commit.

Operations hides generic cleanup and transfer actions for archive journals. It also hides “Mark reviewed” while
archive facts are unresolved or damaged. Known terminal facts/header-only interruptions can still use the existing
deliberate tracking-closure review. “Hide for now” retains the journal and files for the next startup.

The report shows recorded archive, stage, retained-original, manifest and unresolved-artifact paths; object identities,
lengths and complete SHA-256 values; the security snapshot digest; and recorded phase/profile. It quotes paths and
identities with JSON escaping, uses the existing report budget, and explicitly states that current file existence,
bytes, identities and security metadata have not been inspected. Reports can be copied without closing tracking.
An unconfirmed transaction takes precedence over a committed cleanup warning when several transactions coexist.

The nine added headless cases cover reserved, committed-with-retained-files, unsupported and mixed outcomes; visible
actions/accessible report labels; copy-all reporting; and cleanup, mark-reviewed, retry, new-transfer and report entry
points supplied a stale row. A controlled provider proves zero content opens of the stage/retained original. Exact
journal/stage/retained bytes remain unchanged and recovery stays open. These tests do not qualify native object-bound
deletion/restoration, sidecar validation, native accessibility, installed packages or the complete archive UI workflow.

## Regression evidence

Filter: `FullyQualifiedName~Interrupted|FullyQualifiedName~JournalPlace|FullyQualifiedName~MtpMoveJournal`.

| Environment | Final result | Evidence |
|---|---|---|
| Windows x64 host, SDK 10.0.101 | 253 passed, 2 skipped, 0 failed (255 total) | `.tmp/test-results/archive-writing/archive-recovery-ui-final-host.trx` |
| macOS ARM64 / APFS, SDK 10.0.101 | 249 passed, 6 skipped, 0 failed (255 total) | Owned Mac `assessment-results/archive-recovery-ui-final-mac.trx` |
| Windows 11 x64 VM, runtime 10.0.14 | 253 passed, 2 skipped, 0 failed (255 total) | Owned Windows `results/archive-recovery-ui-vm2-final.trx`, copied to host evidence |
| Ubuntu VM, later authorized replay | 249 passed, 6 skipped, 0 failed (255 total) | [Ubuntu source-build checkpoint](UBUNTU_REGRESSION_CHECKPOINT.md) |

Earlier intermediate 254-case runs passed: host/Windows VM 252 passed with two POSIX skips; Mac 248 passed with six
Windows skips. The final extra case checks mixed outcomes. The Windows VM uses host-built assemblies and the test-only
medium-integrity launcher. This is headless execution on the native platforms, not native desktop interaction or
qualification of an ordinary filtered-user installed package.

Final Windows facts: parent PID 6924, child PID 4740, runtime 10.0.14, integrity 12288 before / 8192 after, exit 0.
Owned test processes have exited; the Mac keep-awake assertion remains until reboot.

Final Windows assembly SHA-256:

| Artifact | SHA-256 |
|---|---|
| FileCat.Core.dll | `8ABDEF47D83252AC1C77FECDB1A5B891BDA635DA283EB0F8B8E9A92658AB90F7` |
| FileCat.dll | `603CB5A219B2AD3EE8DFF3BF6A86807E46BF2DBEC47738318D21750788F49BAA` |
| FileCat.App.Tests.dll | `ACF6EC636718374E2A2B38C8979423780CE063BEB3F1DEFF4A75ED2C04023264` |

All new write profiles remain refused by production dispatch. Native publication/capabilities, typed provenance,
private sidecar IO, archive-specific bounded object review/actions, real crash qualification and the full UI/write
pipeline remain unfinished. The broader implementation goal and the later conditional RAR analysis remain pending.
