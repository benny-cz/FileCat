# FileCat UX/UI continuation — implementation and test results

This report continues the first high-severity implementation. The original audit
retains its findings. The earlier [execution report](UX_UI_TEST_RESULTS.md)
remains a historical record of the original and first-batch runs; its failures
are addressed and rechecked here rather than erased from that evidence.

## Scope and priority

The continuation first resolves the reproduced test failures, completes the
primary text-comparison presentation for **FCUX-009 (S1)**, and then implements
the next shared **S2** foundations: **FCUX-011** dialog reachability and the
coupled **FCUX-012 / FCUX-076 / FCUX-073** viewer geometry, source-copy and
semantic-state contracts. These coupled viewer changes share source-relative
selection, decoded-byte mapping and committed content ownership. Other audit
findings retain their own backlog and acceptance requirements.

The implementation retains the pinned .NET SDK 10.0.101, net10.0 target, Avalonia
12.1.1 and xUnit 3.2.2. Package versions, lock files and product privilege guards
are unchanged. All desktop UI execution in this environment uses Avalonia's
headless backend on Linux.

## Failure correction

### Original 32 failures

The original Core device-input assertion expected a byte-budget exception even
though the production reader rejects a non-regular device before parsing. Its
assertion now verifies that earlier, safer admission contract. Four exact-budget
rows replace the previous single regular-file case: ASCII and multibyte names,
ordinary and cross-chunk input, the exact complete byte count, and refusal of a
one-byte-short budget. The device guard remains enabled.

The original 31 App failures comprised 30 picture-worker cases that require a
real ordinary identity and one journal-permission case whose denial is bypassed
by root. They all pass under the verified ordinary-user runner with their
production guards and outcome assertions intact. The runner changes kernel IDs,
drops supplementary groups and every capability set, enables NoNewPrivs, and
proves that the process cannot regain root or bypass protected-file permissions
before starting .NET. It never creates a system account or changes the checkout's
permissions. See [the runner and its security evidence contract](LINUX_UNPRIVILEGED_TESTS.md).

### Additional defect found by ordinary-user execution

A complete ordinary-user Core sweep exposed a Linux trash-location defect: a
valid absolute `XDG_DATA_HOME` was rejected when the account had no usable profile
directory. Linux now resolves the configured data directory first and requires a
profile only for the default fallback. Relative and malformed paths cannot turn
the working directory into a trash location. macOS continues to use its profile
location. Existing owner, symbolic-link, volume and move/restore safeguards remain
in place. All 25 new pure resolver cases and seven existing public behavior cases
passed together under the verified ordinary identity.

### UI fixture and lifecycle verification

Existing dialog fixtures now realize the shared scrollable body before querying
its visual controls. The command-palette fixture dispatches the deferred text
change before inspecting the new search results. Prompt collection tests now
exercise a shown, focused window and advance the renderer past retired frames;
they keep the old window, host and service alive through the original weak-reference
assertions. These changes preserve the meaningful user-visible and lifetime
assertions while testing the actual presentation lifecycle.

The new comparison and viewer tests also exposed presentation, reuse and resource
defects during implementation. Their diagnostic runs are retained separately
from the final gates. An incomplete, aborted or failing test process is never
counted as a successful suite.

### Integrated App sweep and correction

The first integrated ordinary-user App run completed all 2,072 records: 1,893
passed, 57 failed and 122 were explicitly skipped. Its frozen source commit,
compiled assembly hash, complete failure inventory, build receipt, source-input
snapshot and process observations are retained as a diagnostic.

Of those failures, 55 shared a dialog-fixture presentation boundary: the directly
attached footer was available before the new scrollable body had been realized.
The affected unpack/admission, mutation, recursive comparison and interrupted
transfer fixtures now update the shown window's layout before inspecting or
editing that body. Additional arrangement and positive-size assertions ensure
that this boundary represents actual controls. The original complete-path,
option, approval, filesystem, ownership and lifetime assertions remain intact.

One PathLine fixture assumed that every Linux account has a profile directory.
It now asserts the actual production contract: Home has the exact resolved
profile target when one exists, and is absent otherwise. Its separate test of
source-panel activation uses the always-available Computer place. The test does
not manufacture a profile or alter the ordinary identity.

The remaining failure exposed a real viewer initialization regression. A cached
Hex semantic snapshot could request page zero before the window opened, while
encoding detection acquired another device worker and blocked waiting for that
same page. This consumed both device slots for one underlying read and delayed
another viewer's metadata. Opening now checks the page cache and awaits its
completion notification asynchronously, without acquiring a second device slot.
Subscription precedes the cache check, cancellation detaches the waiter, and
encoding/polling publication remains owned by the current window. Global I/O
shutdown retires the opening read-only viewer and its surface callbacks; ordinary
read failures retain their visible evidence.

Fourteen new `ViewerHeaderAdmissionTests` cases cover initial Text/Hex modes,
already-cached completion, real pointer demand on a second page, UTF-16 detection
and manual encoding choice, held reads, queued cancellation, source disposal,
read errors and synchronous closure during encoding publication. The original
two-device-slot and source-integrity assertions remain unchanged.

The next completed 426-case correction gate passed all 274 viewer/device cases,
including those fourteen additions, but exposed one remaining Link field lookup
under a different test ordering. The shared mutation fixture now realizes and
checks every Pack/Link path field at its common lookup boundary. The equivalent
attribute and edit-session message checkpoints also verify their arranged body
before inspecting controls. This keeps the original outcome assertions and
theory rows while making the presentation boundary explicit. An expanded
479-case gate then passed 477 cases and exposed two more recovery-review text
lookups at their shared footer-only checkpoint. Every interrupted-transfer review
now realizes and verifies its body through the common wait helper, preserving
the original explicit focus dispatch and filesystem/journal assertions. Both
completed correction diagnostics remain separate from the final passing gates.

The corrected 479-case gate then passed all 479 cases, with no failures or skips
and actual runner/wrapper exit codes of zero. Its 120.541-second invocation used
the freshly built `9df6b1a` source, the unchanged serial configuration and managed
heap cap. All 1,107 source/test inputs and all 141 App runtime files agreed with
the successful build before and after execution. Startup observations recorded
a 659,956 KiB app high-water mark without an observed kernel OOM-event increase.

The next full App attempt left a complete 2,086-record TRX with 1,964 Passed,
zero Failed and 122 NotExecuted outcomes, but its runner and wrapper completion
receipts were missing. The actual process exits and the final source/runtime
boundary checks cannot be recovered from the test log. That invocation is
therefore retained as an interrupted diagnostic, including its exact TRX bytes,
the observed resource interval and the baseline comparisons. The shared kernel
OOM/kill counters did not increase over the retained interval; the interruption's
cause is not established. Its passing case records do not qualify the invocation
as a successful full suite.

Subsequent final runs use a bounded supervisor independent of the tool's launch
connection. It records the exact owned process identity and actual waited wrapper
exit, allows only one supervised .NET invocation at a time, and limits timeout
cleanup to verified owned process groups. The wrapper also writes and syncs its
authentic preflight build, source/runtime and command evidence before starting
the ordinary runner. Missing completion remains a failure to qualify the run;
startup evidence never substitutes for final execution receipts.

### Owned terminal fixture publication

The subsequent supervised Core sweep completed all 4,536 records with 4,425
Passed, one Failed and 110 NotExecuted outcomes. The failing writable-PATH
security fixture waited only for a shell completion file to exist. Shell
redirection creates that file before `printf` writes its exit status, so the
assertion could observe a transient empty file. Its actual test, runner and
independently waited wrapper exits were nonzero; the supervisor recorded a
failure. That complete run is retained as a diagnostic.

The fixture now waits for an observed nonempty status and then asserts that the
captured status is exactly zero. A nonzero command status still fails; it is not
treated as a reason to keep waiting. The ten-second deadline, all three original
shell-selection rows, the actual control-command output, the writable-PATH
decoy refusal and directory cleanup assertions remain intact. Two deterministic
controls exercise the missing-file, empty-file and published-file states with
both successful and nonzero statuses. They reproduce the publication boundary
without relying on timing or arbitrary sleeps. Production shell selection is
unchanged. All five cases passed in the focused ordinary-user gate in 2.826
seconds, with actual runner/wrapper exits of zero and exact source/runtime
agreement before and after execution. All four final suites below use the
subsequent common solution build.

### Windows-native test admission

Running the complete Windows-platform project on Linux exposed test-admission
errors: fixtures attempted to load Windows file-identity, CopyFile2, Shell and GDI
entry points that are unavailable on this host. The retained diagnostic records
show the missing native libraries directly, including the operation issues behind
the downstream staging assertions. These are not simulated as successful Windows
operations. Native-only methods use the project's existing runtime skip convention
with an explicit prerequisite reason. Test bodies, theory data and native
assertions remain enabled on Windows, and every discovered row is reconciled in
the final comparison. The 78 matching portable Core backing/staging cases already
executed successfully in the complete Core run.

A second review found 69 existing whole-test early returns in eight files. Those
methods represented 70 rows previously reported as Passed on Linux without
reaching a native assertion. They now report an explicit native prerequisite skip.
Only the first admission statement changed; portable methods in the same files
and all native bodies remain intact. Across the three earlier complete Windows-project
runs, the exact 466 class/method/display-name identities are unchanged. The final
record has 105 reported Passed, zero Failed and 361 explicit NotExecuted outcomes:
79 former loader failures and 70 native no-op passes became skips, while the
original 212 skips remain visible.

The resulting Linux record is not native Windows qualification. Windows filesystem,
registry, Shell, device and clipboard acceptance still requires the existing
Windows/ARM64 CI and desktop environments. The recorded comparison includes
the original failure run and the reviewed platform-admission changes.

## Implemented behavior and focused evidence

| Area | Contract | Coverage |
| --- | --- | --- |
| Text comparison | Virtualized aligned rows; shared horizontal scrolling; optional wrapping with matched pair heights; actual changed-glyph reveal; explicit complete-line detail for exceptional lines; source and layout retirement. | 96 passed: 23 new primary-surface cases, 20 complete-line cases and 53 existing equality, navigation, source-change, reuse and retirement cases. The width/length matrix includes 640/980/1,200 DIPs and 60/80/200/10,000-character lines. |
| Shared dialogs | Header and actions retain space; body scrolls within actual owner dimensions; responsive fields and wrapping actions; exact caret/focus reveal; complete safely displayed proposed conflict names. | All 173 affected cases passed, including 21 new viewport cases at 640 × 420 DIPs and font sizes 14/21/28, plus existing dialog/Settings/operation scope and lifetime assertions. |
| Viewer geometry and selection | Source-relative byte boundaries survive resize, font changes and wrapping; long Text/Info and all Hex columns remain reachable; source/encoding replacement retires incompatible selection explicitly. | The combined focused gate passed all 257 cases. It includes 29 new source-mapping cases and 60 new geometry/semantic cases, with resize, font, wrap, multibyte, wide-offset and reentrant ownership boundaries. |
| Copy and semantic state | Source characters and real logical line endings are distinct from the displayed representation; limits and short reads remain explicit; asynchronous publication belongs to the current source/request; accessibility reflects committed content and edit state. | The same 257-case gate includes 26 new source-copy and seven new Hex editor semantic cases: 122 new cases in total, plus 135 protected viewer/editor/search/checksum/report/admission/lifetime cases. These are one overlapping gate, not additional suite totals. |
| Viewer opening and device admission | Await an already-admitted cached page without occupying another worker; detach on cancellation and retire shutdown ownership; retain visible read errors. | 14 new header-admission cases passed with all 257 protected viewer cases and three picture/device-admission cases: 274 passed in the combined correction gate. |
| Integrated fixture and ownership corrections | Review actual arranged dialog fields/body, honor the resolved profile contract, and preserve recovery/edit-session and device-worker assertions. | 479 passed together, including the 274 viewer/device cases and all affected unpack, metadata, mutation, interrupted-transfer, profile and edit-session cases. |
| Linux trash and test runner | Valid XDG data location works without a profile; invalid paths stay invalid; complete per-case results reconcile omitted aggregate skips without hiding incomplete runs. | 32 focused .NET cases and 31 Python runner tests, with actual kernel identity and permission-denial evidence. |
| Owned terminal security fixture | Observe a published status before asserting exact success; preserve actual control-command execution and writable-PATH decoy refusal. | Five focused cases passed: the three original shell-selection rows and two deterministic missing/empty/published status controls, including a nonzero status. |

Focused runs overlap the complete suites below and must not be added to those
totals. Detailed contracts and native acceptance boundaries are documented in
[dialog notes](UX_DIALOG_VIEWPORTS.md), [comparison notes](UX_COMPARE_LINE_DETAILS.md)
and the viewer continuation notes included with this change.

## Final complete regression runs

| Complete project | Discovered | Passed | Failed | Explicitly skipped | Recorded test interval |
| --- | ---: | ---: | ---: | ---: | ---: |
| Core | 4,538 | 4,428 | 0 | 110 | 548.251 s |
| App, Avalonia headless | 2,086 | 1,964 | 0 | 122 | 679.940 s |
| Remote | 2,526 | 2,463 | 0 | 63 | 62.693 s |
| Platform.Windows, Linux admission | 466 | 105 | 0 | 361 | 2.452 s |
| **Total** | **9,616** | **8,960** | **0** | **656** | — |

All four are fresh, complete, unfiltered invocations against the common solution
build from commit `e3403237d048177c82ec2ff5337abda63ab67890`. The pinned-SDK build
completed with actual exit zero, zero warnings and zero errors in 65.485 seconds.
All 1,107 source/test inputs stayed unchanged during the build. Every final
invocation verified those inputs and its complete compiled runtime inventory
before and after execution, with exact retained preflight evidence. Actual test,
runner and independently waited wrapper exits were all zero. No supervisor timed
out or terminated a remaining test group. The supervisor intervals, which also
include staging and verification, were 551.425 / 684.224 / 64.275 / 4.825 seconds
for Core / App / Remote / Platform.Windows respectively.

The runs used verified ordinary UID/GID 65534, the unchanged 1.5 GiB per-process
managed-heap cap and the retained serial runsettings. The final skip-accounting
report reconciles all **656 skip records**, with **167 distinct exact reasons**
and **zero missing reasons**. Core/App skips cover explicit native, desktop,
service, image/device and optional benchmark prerequisites. Remote's 63 skips
comprise 41 unavailable optional FTP/FTPS fixture cases, 21 disposable external-lab
opt-in cases and one benchmark. Platform.Windows keeps its 361 native-prerequisite
skips visible; this Linux invocation does not qualify Windows-native behavior.

The machine-readable [results and case comparisons](UX_UI_BATCH2_TEST_RESULTS.json)
retain exact case counts, display-name aliases, failures, skips, timestamps and
TRX hashes. The ZIP retains complete original TRX bytes in a single internal XZ
stream with per-file paths, byte lengths and SHA-256 values. No XML, passed-test
output, failure/skip reason, stack, case identity or timestamp is rewritten or
discarded. Its standalone Python 3.11+ helper verifies without writing, or restores
the exact originals into a new directory with bounded decoding and no overwrite.
Readable summaries, actual exit codes, identity/build receipts and logs remain
ordinary files alongside that compact evidence. Result-summary paths refer to
the restored evidence root.

### Discovery and outcome reconciliation

| Suite | Original baseline | Verified first batch | Final discovery | Added vs original | Added vs first batch |
| --- | ---: | ---: | ---: | ---: | ---: |
| Core | 4,360 | 4,508 | 4,538 | 178 | 30 |
| App | 1,733 | 1,906 | 2,086 | 353 | 180 |
| **Combined** | **6,093** | **6,414** | **6,624** | **531** | **210** |

No baseline cases were removed, and all added cases passed. The original one Core
failure and 31 App failures now pass. The only other baseline outcome changes
are two reviewed Core and three reviewed App prerequisites that now execute and
pass under the ordinary identity. Their original admission and outcome contracts
remain recorded and reviewed.

Core reconciliation keeps both checksum-case aliases explicit and preserves all
three PE cross-check rows, including two rows sharing a truncated display name
for copied SDK paths. Against the earlier complete 4,536-case serial Core run,
only the two deterministic terminal-publication cases were added, both passing.
The final App case multiset and outcomes exactly match the prior qualified
2,086-case App run. Platform.Windows retains all 466 identities, with the reviewed
79 native loader failures and 70 native no-op passes represented honestly as
prerequisite skips. No new or removed Windows rows are hidden in that adjustment.

The runner requires a nonempty complete set of distinct per-case execution
records, accepts only finished Passed/Failed/NotExecuted outcomes, and reconciles
the executed counts with the summary. Some xUnit/VSTest reports retain native
skips as individual NotExecuted records while reporting an aggregate skip count
of zero. The reconciled and original reported counters are both retained. A
completed failing run retains its counts and returns a nonzero exit status.

### Resource and concurrency evidence

The complete Core suite retains its original resource-intensive inputs, including
recovery fuzzing and internally concurrent page-cache tests. A four-collection
diagnostic run completed all 4,536 records but one unchanged binary-navigation
case failed with an allocation exception while other heavy collections overlapped
under the 1.5 GiB per-process managed-heap cap. The same 19-case comparison class
then passed in isolation under the unchanged cap and four-thread configuration.

The earlier complete serial Core rerun used
`docs/validation/ux-resource-bounded.runsettings`, scheduling one xUnit collection
at a time. All 4,536 records were retained: 4,426 passed, zero failed and 110 had
explicit skip records. Its maximum overlap between executed case intervals was
one. That rerun used the same test assembly as the resource-pressure diagnostic;
the discovered case multiset matched after three documented copied-assembly
display-path aliases. The final rebuilt Core sweep uses the same configuration
and passed all 4,428 executed cases, with 110 explicit skips and no failures.
The two added terminal-status controls account for its new 4,538-case total.
Its maximum executed-case overlap remained one. All 1,107 source inputs and
83 runtime files matched the fresh build before and after the invocation.
The observer retained 544 main-host samples, with maximum sampled RSS of
1,544,480 KiB, maximum observed process high-water mark of 1,633,260 KiB and
no increase in the shared kernel memory-event counters. Internal
test concurrency, input sizes, assertions and deadlines were not reduced. The
original four-thread configuration remains available. The final runner retains
the exact settings bytes and hash used by each invocation.

The viewer resource correction was measured on the same four Text/Info source-copy
cases with 40,000-character logical lines and both wrap modes. The source strings,
clipboard assertions, headless backend and 1.5 GiB heap cap were unchanged. Owned
segment layouts are reused across unchanged arrangements and retired when their
source or interpretation changes. Selection and search decoration bounds are
computed for intersecting visual runs before painting, including mixed-direction
text, while the logical selection remains source-relative.

A second allocation problem was localized to preparation of Bold report text.
Inspection of the exact pinned font implementation, together with phase-separated
measurements, strongly supports repeated synthetic-font fallback through uncached
family aliases as the cause. The headless font has minimal glyph coverage, so the
ordinary ASCII fixture reliably exercises the missing-glyph path. The viewer now
resolves its already selected regular and Bold primary system faces once in the
normal font-measurement lifecycle. It retains those faces, styles and sizes and
uses the font manager's global Unicode fallback afterward. The explicit priority
of secondary aliases in the old hardcoded platform-alternative list is no longer
used for subsequent character fallback; native fallback acceptance remains open.

| Same four-case profile | Before the final font correction | After |
| --- | ---: | ---: |
| Passed / failed | 4 / 0 | 4 / 0 |
| Sampled peak RSS of the actual test app | 1,237,304 KiB | 267,148 KiB |
| Unwrapped Info preparation: cumulative managed allocations | 6,163,074,600 bytes | 62,965,536 bytes |
| Wrapped Info preparation: cumulative managed allocations | 2,506,545,304 bytes | 25,555,360 bytes |

The sampled app peak fell by 78.41%; preparation allocations fell by approximately
98.98% in both Info modes. These are different measures from live managed-heap
size and from the entire dotnet/testhost process tree. The corrected run's separate
child-rusage high-water mark was 266,744 KiB. Kernel OOM/kill counters did not
increase during the corrected run. The historical comparison captured the original
and corrected profiles, exact-source diagnosis, hashes and upstream license.
The source-derived allocation arithmetic is labeled as an inference, rather
than claiming that individual native allocations were traced. These measurements
do not constitute native font or rendering qualification.

The first complete App diagnostic also showed substantial aggregate process
memory: its sampled high-water mark reached approximately 4.28 GiB. The first
sample was already late in that run, so its overlapping test cohorts do not
establish one responsible feature or a leak. The new primary comparison and
geometry classes ran after memory was already high. The run completed normally
with assertion failures and no increase in the observed kernel OOM/kill counters.
The retained timeline distinguishes sampled RSS, process high-water marks and
shared-container pressure. The completed supervised App sweep before the terminal
fixture correction sampled from startup and retains 714 App process observations.
Its largest sampled RSS was
4,453,920 KiB (about 4.25 GiB), and its largest observed process high-water mark
was 4,456,632 KiB. The largest sampling gap was 1.071 seconds. Kernel OOM and
OOM-kill counters did not increase, while the shared cgroup's maximum-pressure
event counter increased by 4,646. These observations do not identify one
responsible feature or establish a leak. The 1.5 GiB configuration applies to
the managed heap, not total process RSS. The four-case viewer improvement above
is not presented as a guarantee for full-suite RSS or native application memory.

The final App sweep from the corrected common build retains 676 session samples,
including 675 observations of App-named processes (674 of the main execution
process and one of its earlier discovery process). Sampling began before the
first case and reached the terminal session observation after the last case.
The maximum sampled App RSS was 4,532,472 KiB (about 4.32 GiB), and the maximum
observed process high-water value was 4,535,172 KiB. The largest sampling gap was
1.063188 seconds. Executed-case interval overlap remained one. Shared kernel
OOM/OOM-kill deltas were zero; the shared cgroup's maximum-pressure counter rose
by 7,414. The same sampling and attribution limits above apply: the measurements
do not isolate a feature or establish a memory leak, and the managed-heap cap
does not bound total process RSS.

## Reproduction and Git verification

From the source root, use the pinned SDK and an ordinary user:

```sh
dotnet restore FileCat.slnx --locked-mode --disable-parallel
dotnet build FileCat.slnx -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test tests/FileCat.App.Tests/FileCat.App.Tests.csproj -c Release --no-build --no-restore --settings docs/validation/ux-resource-bounded.runsettings
dotnet test tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj -c Release --no-build --no-restore --settings docs/validation/ux-resource-bounded.runsettings
dotnet test tests/FileCat.Remote.Tests/FileCat.Remote.Tests.csproj -c Release --no-build --no-restore --settings docs/validation/ux-resource-bounded.runsettings
dotnet test tests/FileCat.Platform.Windows.Tests/FileCat.Platform.Windows.Tests.csproj -c Release --no-build --no-restore --settings docs/validation/ux-resource-bounded.runsettings
python3 -B -m unittest discover -s eng/validation -p test_run_linux_unprivileged_tests.py -v
```

Run the projects sequentially on a memory-constrained host. For a root-only Linux
container, use the already-built-assembly invocation in
[the ordinary-user runner instructions](LINUX_UNPRIVILEGED_TESTS.md). That path
uses the same pinned SDK, exact runsettings and a 1.5 GiB per-process managed-heap
cap, and retains the actual kernel identity/probe evidence. It does not inherit
live-device, external-service or benchmark opt-ins.

The integrated build receipt records the real successful process exit, source
commit, unchanged source/test input snapshot, build log and complete output
inventories for all four test projects. Each final invocation retained exact
copies of those three build records. A separate task-local wrapper hashes the
source inputs and every regular runtime file before and after calling the
ordinary runner. Primary test assemblies, production dependencies, native
libraries, runtime configuration, fixture data, file modes and complete path
sets must agree with the successful build. Repeat those checks against the
current Git checkout when validating a new implementation.

This binding covers the original runtime tree at the invocation boundaries.
The unchanged ordinary runner makes an independent, contained private copy;
the wrapper does not claim a separately hashed staging inventory or continuous
monitoring of intermediate mutations. The historical source records and wrapper
implementation made that scope explicit. Documentation-only commits were
recorded separately from the build's source commit.

### Test-runner checks

All 144 checks in the five runner-related Python groups below passed in the
recorded batch. These are separate from the .NET suite totals; the focused .NET
gates are overlapping regression selections.

| Python group | Passed | Important boundaries |
| --- | ---: | --- |
| Ordinary-user runner | 31 | Identity/capability validation, permission-probe evidence, private staging, complete result reconciliation and exact settings retention. |
| Source/runtime binding | 13 | Changed, missing, extra or linked runtime files; file modes; source-input drift; documentation-only changes. |
| Durable preflight evidence | 25 | Exclusive paths, synced publication before invocation, exact retained scripts, abrupt Python exit, failed/mutated build evidence and mismatched exit receipts. |
| Owned finite supervisor | 27 | Bounded regular identity reads, exact process ancestry, foreign/PID-reused refusal, owner-lock contention, real Python success/failure/timeout processes and cleanup failure receipts. |
| Skip accounting | 48 | Four matching complete builds/runs, exact untrimmed reasons and identities, bounded captured TRX parsing, input replacement, preserved history and atomic publication. |

The former source-archive delivery and evidence-compaction checks are no longer
part of this audit workflow. Continue from the Git checkout, rerun the relevant
tests, and commit and push each verified change as described in the
[session workflow](UX_UI_SESSION_WORKFLOW.md).

## Acceptance that requires a native environment

### Windows 11 VMware pointer check — 19 October 2026

The current Git build was copied as ordinary build files into an isolated
guest validation folder and launched on the Windows 11 desktop. The
[`FileCat.NativeAcceptance` runner](../../tests/FileCat.NativeAcceptance/README.md)
sent a real guest pointer drag to the lower-right **Find files** corner. It
measured 1,056 × 759 pixels before, 1,226 × 879 expanded, and 1,056 × 759
restored. The same drag passed with Advanced criteria open. The eight checked
Advanced inputs and actions were still visible in the guest's native UI
Automation tree after the resize. The runner's local WinAppDriver endpoint
and VMware screen capture kept the host workstation available. This covers a
repeatable native pointer path for UXBUG-007; display-scaling and assistive-
technology acceptance remain open.

Headless geometry and automation tests do not establish native screen-reader
behavior, physical monitor scaling, native clipboard interoperability or
Windows/macOS filesystem and device behavior. The recorded skips remain visible;
platform guards and explicit live-device/service opt-ins are preserved.

Use the audit's native acceptance cases at supported minimum/wide window sizes,
100%, 150% and 200% display scaling, and the specified content fonts. Check actual
pointer/keyboard routing, focus announcements, complete path/name inspection,
clipboard exchange, close/cancel behavior and assistive-technology bridges on
each supported desktop. Remaining S2/S3 findings are listed in the unchanged
audit; this batch does not claim that the entire 76-finding plan is complete.
