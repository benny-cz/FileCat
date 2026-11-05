# Preparation space admission, 5 November 2026

Partial checkpoint over `ec634012`; the complete archive-writing plan remains unfinished. No profile is enabled.

## Implemented behavior

Journaled TAR preparation now checks destination capacity before appending archive intent or creating any native
stage/evidence file. The conservative additional-space estimate includes the complete physical stage ceiling,
all reserved assessed/expected/observed/provenance sidecars, caller-declared known snapshot bytes and a 64 MiB margin.
Compression is not assumed to shrink the source. The original remains allocated; its length contributes to the total
footprint reported separately and is not charged twice against currently available bytes. Invalid/beyond-policy
components refuse, with checked total arithmetic.

Only the reservation's retained directory supplies the space observation. Both native adapters verify the same
parent identity/namespace/volume before and after querying. `FileSystemCapacity.QueryHeldDirectory` queries the
held object, without reopening a path or guessing a containing mount from a path prefix. Windows uses caller-available
allocation units from FileFsFullSizeInformation, including the caller's quota; Unix uses fstatfs allocation units.
The result's volume/device identifier comes from that same held directory identity. Queries support the existing
64-bit native development lanes; unsupported API/layout/query results remain explicitly unknown.

Known available bytes below the estimate refuse with BudgetExceeded before creation or transform launch. Equality
is sufficient. Unknown capacity stays unknown and does not substitute zero or manufacture a capacity reservation;
actual native write/flush failures still fail the operation and retain its evidence. The unpublished preparation
returns the immutable estimate, capacity/unknown state and observation time for later workflow presentation.
Neither that observation nor sufficient free space grants publication authority or prevents later disk exhaustion.

Current retained-file inputs create no private snapshots, so their known-snapshot charge is zero. Future folder/
snapshot preparation must supply its actual additional costs and extend accounting for any additional publication
artifacts. Full source capture, aggregate resources and UI presentation of estimates/unknown capacity remain open.
This is not a filesystem quota reservation, full ENOSPC/crash qualification or completed user-facing space workflow.

## Native and regression evidence

| Environment | Affected Core suite | Additional native suite |
|---|---|---|
| Windows 11 x64 VM / NTFS | 1,141 passed, 143 skipped; 1,284 total | Storage: 92 passed, 21 skipped; 113 total. LPAC: 33 passed |
| macOS ARM64 / APFS | 1,237 passed, 47 skipped; 1,284 total | Independent native capacity observer and actual journaled create/update included |
| Ubuntu x64 VM / ext4 | 1,250 passed, 34 skipped; 1,284 total | Independent capacity observer plus confined lane: 44 passed, one skipped |

No final failures. Seventeen new contract cases cover complete accounting, invalid/maximal components, exact capacity
boundaries, known-snapshot costs, unknown capacity and destination/estimate mismatches. Known-insufficient cases prove
there is no archive intent, stage/evidence creation or worker startup. Unknown capacity reaches the checked creation
path and retains intent on injected creation failure. Native create/update assertions inspect every reserved evidence
bound and actual capacity fact. The Windows native case independently compares the held NT query with GetDiskFreeSpaceEx
through the existing path-query API, under the same medium-integrity test parent.

The new Unix C observer is compiled against installed native headers and independently confirms pointer/structure sizes,
field offsets, unit size, destination device and available bytes. Observed tuples `(structure bytes, unit offset,
available-units offset)` are `(2168, 0, 24)` on Mac and `(120, 72, 32)` on Ubuntu; both report 4096-byte allocation units.
Separate observations allow 64 MiB of concurrent filesystem allocation/free activity; they are not an atomic equality
claim. Compilers: Apple clang 21.0.0 with explicit CommandLineTools MacOSX SDK, and Ubuntu GCC 13.3.0. Mac's first observer
compile lacked an SDK header search path and stopped before tests; adding the explicit SDK path fixed setup without
changing global toolchain selection. The initial 17-case host smoke passed; two analyzer warnings were corrected before
the final zero-warning builds.

Broad Core filter: `Archive|EditSession|BackingArchiveTarget|PaxTimestamp` as FullyQualifiedName terms. Native checks use
the tracked Windows/Linux containment runners. Existing eight exact-time independent-reader fixtures per platform also
pass again (Mac/Ubuntu guest Python, Windows guest outputs checked with host Python). These do not complete fuzz/scale,
bidirectional format-oracle, GUI or release qualification. No UI behavior changed and no new GUI acceptance is claimed.

SDK 10.0.101 builds/publishes completed with zero warnings/errors. Actual test-host versions remain macOS 27.0.1 /
.NET 10.0.12, Windows build 26340 / .NET 10.0.14, Ubuntu 24.04.5 / .NET 10.0.14. Windows launch facts record integrity
12288 before / 8192 after, exit 0, with administrator membership/privileges retained. Mac worker hard-memory containment
remains unqualified; native IO and ordinary child tests do not supply that boundary. Other native filesystem/RID and
installed-package routes and configured-quota behavior remain unqualified. Linux service metrics describe the driver,
not worker peak memory.

Final runner censuses found no FileCat/test instance left by this task. Another tester started on Windows after our
completion marker; no further tests were launched there. Completed evidence was transferred/read and packaged with
tar, without starting another FileCat/test/PowerShell process or touching that tester's files. Mac caffeinate processes
1339/6428 remain. Shared `E:\FileCat` stays tracked-clean on `main`.

## Evidence artifacts

Raw TRX, launch facts, C observer binaries/output, oracle fixtures and logs are retained under ignored
`.tmp/test-results/archive-writing/space-v1` and the task-owned guest roots.

| Transfer/result archive | SHA-256 |
|---|---|
| Eleven-file source overlay | `CDB5E260B35CDCE35546B7F4986BA63A098F8759362E333761CE638EA1C2AB69` |
| Windows 84-file managed overlay | `48062D470BEE4153D9A51BEC1517F6A242CB7718BA56EB35F8B737453A2DE80F` |
| Mac results/observer/fixtures | `F7696B055D22B1187683378D0B13051016E45B95F57561227AF1DD51285DD804` |
| Ubuntu results/observer/fixtures | `E2A036DABABD2A31402621C9DFDDF67C5CF9AD47D93AD6AFA05D2559F7938701` |
| Windows results/fixtures | `4DDABAB6289BDB51885377D978ADEE3BA0C5D089F8B2DFB2612BD7CB071C0447` |

Native API references: [Microsoft caller-available allocation units](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddk/ns-ntddk-_file_fs_full_size_information),
[Apple statfs layout](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/mount.h).
No third-party source or runtime package was added or upgraded. Full metadata/origin application, scalable capture,
publication/recovery, production/UI integration, other formats, qualification and the final independent whole-goal
UX/UI/functional/security review remain unfinished. RAR licensing analysis remains deferred until that loop completes.
