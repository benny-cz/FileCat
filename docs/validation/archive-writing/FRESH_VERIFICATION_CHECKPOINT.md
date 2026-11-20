# Fresh TAR verification checkpoint, 5 November 2026

This is a partial implementation of the [archive writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md).
No new writer profile is registered or enabled. The [implementation ledger](IMPLEMENTATION_STATUS.md) retains the
unfinished requirements; this checkpoint does not qualify a complete archive update or publication workflow.

## Implemented contract

`ArchiveVerificationClient` launches a separate read-only worker with a new task nonce and a distinct verification-role
handshake. Assessment and verification endpoints reject each other's handshake before granting source access. The
worker receives the fixed numeric stage grant, physical length/digest and explicit profile. It never receives the
expected manifest or selects the native stage identity.

The current TAR verifier hashes the complete physical stream, checks the strict raw USTAR/PAX catalog and compares the
runtime decoder's complete view. It derives ordered members, exact duplicate occurrences, content hashes, semantic
metadata and format features independently of the expected manifest. The parent additionally requires contiguous
complete stage-read coverage before accepting the catalog. A matching receipt without those reads is refused.

Acceptance requires complete framing, catalog/member/digest agreement, terminal EOF, successful process exit, final
CPU and diagnostic checks, and actual disposal/reaping while holding the worker queue reservation. Only then does the
parent compare the full/content/metadata manifest digests with its frozen expectation. Changed physical bytes, invalid
TAR framing, altered member names/order/content/metadata/features and a substituted worker role refuse without a
verification result. Unsupported formats refuse before worker launch or stage access.

The result carries the parent-supplied stage version and semantic digests. It grants no pathname or publication
authority and does not replace native identity/content/metadata rereview. A trustworthy expected manifest must still
come from reviewed parent inputs, rather than being accepted from the transform worker.

The Windows application dispatches `--archive-verify-worker` before UI/single-instance startup and repeats the actual
LPAC token and immediate job checks before opening input. Its fixed parent launcher shares the existing bounded
native boundary. The Linux development probe dispatches verification only after its native launcher/worker controls;
the production Linux application remains unavailable. No ordinary process fallback was added. Mac native confinement
remains unqualified.

## Observed validation

| Environment | Assessment/verifier protocol and lifecycle | Application entry guards | Native confinement regression |
|---|---|---|---|
| Windows 11 x64 VM / NTFS | 27 passed | 7 passed, both roles exercised per case | 27 passed, including fresh verification through both probe and actual application |
| macOS ARM64 / APFS | 27 passed | 7 passed, both roles exercised per case | Unqualified; ordinary child compatibility tests only |
| Ubuntu x64 VM / ext4 | 27 passed | 7 passed, both roles exercised per case | 41 passed, 1 platform-inapplicable case skipped; 42 total |

There were no failures. Builds used SDK 10.0.101 with zero warnings/errors and tests used serviced runtime 10.0.14.
The portable filter was `FullyQualifiedName~ArchiveAssessment|FullyQualifiedName~ArchiveVerification|FullyQualifiedName~ArchiveWorker`.
The entry filter was `FullyQualifiedName~ArchiveWorkerEntryTests`. The tracked native runners selected their complete
Windows containment and Linux archive development lanes. Native fresh-verification tests assert distinct process IDs,
successful exit and reaping; Linux additionally checks both owned controllers were removed. These are bounded
regression tests, not a coverage-guided fuzz campaign, scale qualification or independent-implementation release gate.

Windows facts record actual test-parent integrity changing from 12288 to 8192. The account retains administrator
membership/privileges; this is not ordinary UAC-filtered interactive-user or installed-package qualification. The Linux
driver runs through the existing delegated user-manager subtree; its service metrics are not worker-memory metrics.
The existing native memory, CPU, parent-death, descriptor and syscall policy cases also passed in that lane.

Owned source overlay: 13 files, SHA-256 `07BCA950E01A82D32CA0DADCEF492229567D538B757302F58471AC6157E8FE3A`.
Windows managed overlay: 84 files, SHA-256 `7E93E5C8BABDB5C014C226631634EE5504718E813741FC6E7F56E547B7F6B81A`.

| Tested artifact | SHA-256 |
|---|---|
| Windows Core.Tests.dll | `CC3D50E657E099BE8D07533DAB1BDBC2E843E483102E4B36920CCE7ACEA976BA` |
| Windows Platform.Windows.Tests.dll | `695E38AB0D9DD8BB8EA6D1D82C97E5CAF7651D0CF08794D482606CF7BF073A16` |
| Windows application FileCat.dll | `1F5B9B7004E347956316308AB9015CDD5A9EE6A9F325755CB7789248FD807926` |
| Windows probe FileCat.WorkerProbe.dll | `CFBDB5DCD26C2A3DB2AEBF92F8F3B24DD0BDC53981272C3CE6FA3F0DFF55FD20` |
| Mac Core.Tests.dll | `FCC42843ABE83126ADBF5CE48F52BFED45CD1BE171ADDDA9D989B7713DBB0D29` |
| Mac Core.dll | `ABEC397619695704E552809494CEDBC94C875CC8F617EF49F92E04C17AC62794` |
| Ubuntu Core.Tests.dll | `602BE15BCC048CCB73BDCCFB8B40735CD4B0E39AFAB3CD5E978DBDA3904B7653` |
| Ubuntu native probe FileCat.WorkerProbe.dll | `09CDC887A9BEB572DF201EECCA1B8A9FCA591408D4BA8C67F529D879A67248D8` |
| Ubuntu native probe Core.dll | `DEA9B80A2A8C54D9E769C5AA708278E4C7733F536A21F5DFD47B47D2CBAED479` |

Raw logs, TRX, launch facts and overlays remain under the ignored worktree `.tmp/test-results/archive-writing/fresh-verifier-v1`
and task-owned guest directories. Tests started after idle censuses; final censuses found no FileCat/test process left
by this task. The Mac's temporary until-reboot caffeinate assertions were retained. No other tester was stopped.

Still required: transform wire/worker and trusted expected-manifest construction, other format verification,
cooperative cancellation with a dedicated channel and bounded grace, full native/package/RID qualification, reviewed
input/stage leases and publication/recovery integration, complete UI workflows, all remaining release evidence and
the independent whole-goal UX/UI/functional/security review after the implementation loop. Cancellation currently
uses immediate termination and reaping; this checkpoint does not claim the planned cooperative cancellation contract.
