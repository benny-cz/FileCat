# Assessment checkpoint evidence, 4–5 November 2026

Status: **partial implementation, no new writer registered**. The
[assessment contract](ASSESSMENT_PROTOCOL.md) describes what is implemented; the
[implementation ledger](IMPLEMENTATION_STATUS.md) retains the complete unfinished scope.

## Results

| Native environment | Affected archive/edit-session regression | Early entry | Confined assessment/authority lane |
|---|---|---|---|
| Windows x64 host | 592 passed, 28 skipped, 0 failed; 620 total | 7 passed | Not run on host |
| Windows 11 x64 VM, build 26340 / NTFS | 592 passed, 28 skipped, 0 failed; 620 total | Covered by the actual application worker | 25 passed, 0 failed, 0 skipped |
| macOS ARM64, 27.0.1 / APFS | 601 passed, 19 skipped, 0 failed; 620 total | 7 passed | Not qualified; portable child tests do not establish containment |
| Ubuntu VM | Postponed while another tester's FileCat remains active | Not run | Not run |

The new portable subset has 23 cases: canonical/aggregate manifest bounds, record fragmentation/poisoning, exact strings
and options, real child TAR assessment/corruption/source-change refusal, forbidden message phases, cancellation/stall,
queue-through-reaping, final diagnostics failure and final CPU accounting. These are regression/adversarial cases, not
instrumented fuzz campaigns or satisfaction of any CPU-hour release floor. The prior foundation cases remain included.

The affected regression filter is
`FullyQualifiedName~Archive|FullyQualifiedName~EditSession|FullyQualifiedName~BackingArchiveTarget`.
Raw results are retained under the ignored worktree `.tmp/test-results/archive-writing` and owned guest roots.

- Host: `assessment-regression-host-complete.trx` and `archive-entry-win-bootstrap.trx`.
- Windows guest: `results/assessment-regression-vm620-fixed1.trx`, with facts:
  `{"parentPid":5204,"runtime":"10.0.14","before":12288,"after":8192,"childPid":3380,"exitCode":0}`.
- Windows native lane:
  `assessment-native-results/containment-20261104T232134Z-79b2f77c3ea648918467f134b952a964.trx`, with facts:
  `{"parentPid":4976,"runtime":"10.0.14","before":12288,"after":8192,"childPid":1456,"exitCode":0}`.
- Mac: `assessment-results/assessment-regression-mac-complete.trx` and `archive-entry-mac-final.trx` under
  `/Users/benny/FileCatValidation/archive-writing-42829297`. The complete-source transfer had SHA-256
  `91B85340D1696683D1AF31407567450DA77C5C5CAD2CF05CC54658874E5DD590`; the final CPU-accounting source/test correction was
  subsequently applied and is included in the complete 620-case replay.

SDK is 10.0.101. Windows guest execution uses the installed .NET 10.0.14 host with the private SDK VSTest console. Its
VMware login token was explicitly lowered to medium integrity; this is not proof of an ordinary UAC-filtered interactive
session. Mac uses its existing private SDK/runtime installation (10.0.1 and 10.0.12 runtimes were inventoried); a final
serviced-runtime release candidate is not claimed.

## Actual Windows application worker

The 25-case lane retains the previous 22 checks and adds two healthy real-TAR assessments plus a forged-startup denial.
One healthy assessment uses the non-shipping probe endpoint; the other launches the self-contained `FileCat.exe` through
its real early entry. Both perform complete source hashing, strict raw TAR scan, independent runtime-decoder comparison,
canonical metadata/catalog transfer and parent validation. The forged privilege identity fails the actual token check
before any source-opening callback runs. The prior 64 MiB IO, memory/CPU, denied file/network/children/registry/profile,
handle inheritance, stderr flood, repeated stop and parent-death tests remain green.

These are development self-contained candidates, updated with the freshly built components below, not installed or
signed release packages. Modified component SHA-256 values for the successful final native run:

| Component/location | SHA-256 |
|---|---|
| Application FileCat.dll | `3E2B0E4F67AE4E6DF15970746296E949D91CEF90FC2D4681494F3341D087E8C1` |
| Application FileCat.Platform.Windows.dll | `0CD428C0A259B56A5983154BB75C3CC148067DB2934CEC6415842D5DB0264CA0` |
| Application FileCat.Core.dll | `566BE36590EBE28051760298B88BFDC29E4FB51772FF3676286BCA8EFFFBECCB` |
| Application FileCat.Archives.dll | `3F098DD8F67FAEC50EC25B2D62787426830C81185C7E8D9A3AB1741E093AFBCD` |
| Native parent FileCat.Platform.Windows.dll | `A5B356129503A8DCFC70442A8DE13842A24E842963EE94F6F837914890CFF8EC` |
| Native parent FileCat.Core.dll, including final acceptance accounting | `B6ED550A0407512862CD54ECA9C308BA148702273A4529831B61F372FABF8AF1` |
| FileCat.Platform.Windows.Tests.dll | `ACC71AB9FBA46E1383143FFC2843A6E9D9D88EC3E227B235461A1366156EBBD2` |
| Self-contained FileCat.WorkerProbe.dll | `A59E91D039C40D6B553D1A513BEDE71F3D87295527755A681559A2EB10D45BC3` |

This is a component evidence list, not an artifact SBOM. The application child does not execute the parent client; its
assessment engine is the same code path exercised through the native lane. Final candidate-wide build/signing/SBOM and
all required workloads must still be replayed together before enabling a profile.

## Defects and setup failures distinguished

- The old framing ceiling confused one physical file's size with cumulative multi-pass wire traffic. Separate finite
  traffic policy now accommodates bounded rereads without raising the file/output ceiling.
- Final stderr classification originally could arrive during disposal after success had already been chosen. Acceptance
  now follows the completed drain/reap and checks final error and OS CPU facts; dedicated regressions cover both.
- The Mac test-only process wrapper queried CPU after Unix process reaping removed those statistics. It now caches live
  samples and preserves exit facts. The subsequent entire 620-case replay passes. This wrapper is not a Mac sandbox.
- LPAC refused privilege-name service calls during worker boot. Empty privilege lists need no name lookup, but the real
  worker retains the required traverse-check privilege. Attempts to remove it prevented runtime startup (C0000022), and
  were discarded. The trusted parent now resolves its local LUID and independently checks the actual suspended token;
  the child repeats the check against that immutable startup fact. No privilege/capability grant or permissive fallback
  was added. The wrong-fact native test refuses before source IO.
- Native process command-line buffers now include the required terminating NUL. Archive working directories are fixed
  to their approved asset directories, while legacy launch defaults retain their behavior.
- One parent-death run used a stale Core harness dependency after the new process interface was introduced. Matching
  assemblies fixed setup. A separate complete replay began during a still-running file copy; no tests started in that
  attempt. The copy was awaited, setup was retried with fresh evidence paths, and only the terminal successful runs above
  are counted.

## Remaining gates

There is no production AppServices transform registry, source-lease/capture workflow, cooperative cancellation channel,
rebuild/finalization protocol, fresh verifier, shared publication/recovery implementation or user workflow in this
checkpoint. TAR.GZ assessment/update and remaining required formats still need integration and qualification. Source
feature inventories, scale/allocation measurements, independently implemented format oracles, coverage-guided fuzz floors,
ordinary-user/package/RID matrices, metadata/origin races, accessibility and all-platform live user journeys are unfinished.

Mac hard memory containment remains unqualified as recorded in the earlier spike report. Ubuntu remains occupied by the
other tester's existing instance; it was not stopped. All FileCat/test workers launched by this task are reaped after
testing, and the Mac's until-reboot keep-awake assertion is intentionally retained. No packet or overall goal is complete.
