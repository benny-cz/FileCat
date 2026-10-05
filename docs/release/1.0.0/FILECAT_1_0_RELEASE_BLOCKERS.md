# FileCat 1.0.0 — open release blockers and required decisions

Only unresolved items are listed; closed items move to the [execution report](FILECAT_1_0_RELEASE_EXECUTION_REPORT.md)
history. Engineering defects are tracked in the [issue register](FILECAT_1_0_RELEASE_ISSUES.md); every issue there that
is not Closed also blocks GO (plan §12.6: no unresolved blocker at any severity). Current recommendation: **NO-GO**
(no candidate exists; mandatory evidence is missing).

## A. Decisions only the product owner can make

| ID | Decision needed | Why it blocks | Plan |
|---|---|---|---|
| DEC-01 | Name the release owner, security owner, validation owner and the human GO approver | Preflight gate; risk acceptance and GO need named people | §14 step 2, §12.6 |
| DEC-02 | Platform Support Decision per row (W64, MAC, LNX, WA): minimum and current OS versions, tier, stable or preview label, artifact set | Contract freeze; decides which V19/FQ work is mandatory. D-48 proposes stable ARM64, which needs a physical ARM64 device | §4.4 |
| DEC-03 | macOS distribution policy: Developer ID signing and notarization, or a separately labelled non-notarized preview | Gatekeeper/TCC behavior of the shipped app and the Mac FQ path | §10.4 step 7 |
| DEC-04 | Framework-dependent package and the privileged helper: supported placement and runtime rules | The FDD ZIP contains the helper without a portable marker | §3.2 item 7 |
| DEC-05 | VIEW-004 and D-56 claim scope (I05): what media and record fields are promised | Public claims must match evidence | §3.2 items 1–2 |
| DEC-07 | The unsigned public preview release (D-40's first step): approve publishing a clearly labelled prerelease | SignPath Foundation requires an existing release before signing | §10.4 step 1 |
| DEC-08 | GitHub private vulnerability reporting (I01): enable it as SECURITY.md already promises, and name who answers within 7 / 14 days | The advertised private route does not exist | §7 I01, V22 |
| DEC-09 | Release branch and protection model (I18): a `release/1.0` branch with required checks, protected `v1.0.0-rc.*`/`v1.0.0` tags, immutable releases, a human-gated publisher | Freezes are not enforceable today; agents push to `main` directly. Read 2026-10-01 (E-DPI B09): no rulesets, `main` unprotected, `v*` tags unprotected, immutable releases off, any action allowed without SHA pinning | §12.1 |
| DEC-10 | A release-owner-controlled, read-only store for raw evidence and packages | REP sealing needs retained raw evidence | §12.5 |
| DEC-11 | ~~Markdown rendering for 1.0 (I25): whether, and whether through a new dependency~~ **Decided 2026-10-01:** required for 1.0.0, low priority; built-in renderer (no new dependency) | — | I25 |

## B. External parties and credentials

| ID | Needed | Blocks |
|---|---|---|
| EXT-01 | SignPath Foundation project acceptance, roles and policy (after DEC-07) | I02; every signed Windows artifact; SAC evidence |
| EXT-02 | Provenance determination of SharpCompress 0.50.4's RAR decoder (upstream or legal) | I14; OSI-only eligibility, signing, notices |
| EXT-03 | Apple Developer ID and notarization credentials, if DEC-03 chooses them | Notarized Mac artifact |

## C. Hardware and environments

Owner-requested overnight execution queues interactions until 2026-10-06 08:40 CEST.
Same-chat continuation and a one-time check-in are configured. Supported Windows
Computer Use import was rechecked and still crashes before input; desktop/tool
restoration, phone-lock interaction, reference hardware and A/B decisions/credentials
remain queued. No current question is pending. Mac ordinary SSH works; temporary
pinned power support rearmed on AC with root restorer PID 19109 at 22:39:22Z, with
12-hour/disconnect/explicit-stop restoration. Prior completed restorations remain
historical; current restoration is due when testing ends (E-ENV-MAC-1).

I144–I146 are preliminarily remediated at cb85f0a: affected/full host, clean
Windows/macOS/Ubuntu inventories with automatic temp cleanup, all six admission
controls and all four exact-source CI jobs independently pass. Four server digests
and six complete inventories verify; original worker-demand/Unix cleanup/Windows
CI and Mac collector failures remain retained. All-consumer bitmap memory, Unix
containment, wider native UI/frame/AT/hardware and candidate evidence remain open
(E-I144/E-I145/E-I146).

I136 is verified preliminarily at clean a1c265f. The numbered-volume gap warning preserves
independent ISO/Joliet/UDF 1.02 and complete RAR 54 search/72 content controls plus safe refusals.
Affected host checks, four clean CI jobs and all 81 cases per VM pass. Four server digests/six
complete TRX inventories and native source/fixture/payload/case/cleanup pins independently verify.
Other variants/native desktop/AT/candidate remain. E-V09-M1 retains 86 Mac passes and an admission
refusal before device opening. Owner-authorized IDE/ServiceHub shutdown exposes the remaining
kernel/zombie census defect I137. Its narrow correction passes affected/full host, four clean CI
jobs and Mac 44/7 skips plus five native controls; actual admission succeeds. The subsequent
whole-file driver failure selects an intentionally partial fixture (I138). Its test-only correction
passes host, four clean 593583e CI jobs and the complete native Mac session; recovered bytes and
unchanged source/cleanup verify. Broader I106 remains open. Owner v2 calibration captures all
18 known operations/three native TIDs/FDs; sparse bytes and owned cleanup verify. All 18
positional offsets fail. Actual v5 verifies parent/child/name/FD/burst controls within its
selector limits; v6 verifies eighteen raw 64-bit offsets and finite controls, but mmap source
FD/offset remains unresolved. Neither is whole-source/zero-loss qualification. Actual v8/v9
full sessions fail census admission with zero opens; source/detachment/owned cleanup verify.
V9 preserves normal groups and records four EACCES reads of the sudo executable. Its mode
04511/ordinary read failure and later production census false independently verify. No guard
is weakened. V10/v11 preflight/marker failures are retained. V12 actual complete session
passes 1/1, exact 60 bytes/70-second wait/source/detachment/cleanup. All 104 pins/18 tracked
plus fifteen derived child absences verify; finite raw controls/ordinary source FD reads
verify. Supervisor failure and uncaptured recorder command exit remain. All 79 shared
nonanonymous mappings lack backing FD; full source-write/authopen/native/candidate remain.
Root syscall provider unavailable under SIP; process v4 compile failure retained. V5 root/ordinary
control passes all three known FD/offset/address mappings, whole bytes/32 pins/three absences,
SIP enabled. New v13 actual execution verifies 1/1/whole recovered bytes/source/cleanup,
1,197 payload pins/117 retained pins and clean worker/recorder/decoder exits. All 77 shared
mapping FDs/offsets resolve, including 75 filesystem paths/two shared-memory objects; no
source-FD writes/aliases/forks/mappings are observed. 43 unmatched private maps and broader
helper/authopen/topology/native/candidate qualification remain (E-V09-M5). Native component
refusal now passes with owner cancellation: actual not-approved/no source/no timeout,
raw EACCES/helper cancellation, clean command exits, 42 retained/197 input pins, source
bytes/detachment and seven absences verify. First owner-approved attempt remains failed
against its refusal expectation, with original failure/shortened trace preserved. Fresh
approval/returned-FD/seven-range case now passes independent bytes, O_RDONLY/closed EBADF,
six bounded native reads, clean exits/42 pins/source/detachment/seven absences (E-V09-M6).
Actual held approval now returns the equal-size replacement at the same raw path, exposing
I140. Native identities/flags/closure, unchanged sources/detachment, 88 retained/200 input
pins and eight absences verify. Committed 8f75856 guard and clean Mac six new cases/19 passes verify;
fresh corrected local-Terminal v3 and owner-requested v4 held approvals both reject the
approved equal-size replacement before source construction. Inferred received FD fstat/close,
no size ioctls/content reads, 200 input/88 retained pins each, unchanged detached images,
eight absences and clean recorded exits verify. SSH v2 interaction failure stays retained.
Clean 6197592 CI 37359106547 now passes all four required lanes/four server digests/six full
TRX inventories/60 affected viewer cases; ARM64 App 351/17 declared skips/startup/installer
pass by logs. No per-case ARM64 TRX or physical/candidate qualification is established.
Fresh current-guard unchanged approval is prepared: 197 inputs/20 retained pins, two native
direct controls/seven independent ranges each, unchanged detached source/four absences and
launcher verify. V2 reaches cancellation despite owner-reported approval, no descriptor;
54 retained/197 input pins/source/cleanup/seven absences verify; cause unknown. Fresh v3
component rights/seven ranges/closure/source/cleanup and independent raw pairs/six bounded
reads/55 retained and 197 input pins/seven absences pass (E-V09-M8). Temporary awake/sudo
and removed GUI-session control verify (E-ENV-MAC-1). Connectivity/closed-lid SSH now verify;
fresh desktop-driver preparation and actual owner-cancelled refusal now independently pass
(E-V09-M9): no source/no timeout, two EACCES opens, native cancellation/channel closure,
201 input/55 retained pins/source/detachment/agent removal/nine absences verify.
Temporary sleep-disable/root restorer and caffeinate restoration are due when testing ends.
Fresh native device-removal preparation verifies 201 inputs/42 retained pins, regular/raw
seven-range/read-only controls, safe missing-source refusal, desktop contexts, source/
detachment/two removed agents/eight absences. Actual owner-approved held removal verifies no source/no timeout, helper ENOENT after
detachment, channel closure, 201 input/57 retained pins/source/cleanup/nine absences.
Reporting defect I142 is committed at 348cbc7; clean Mac 23/4 declared skips, Ubuntu
25/2 declared skips and final owner-approved native removal now pass no source,
truthful IOException and independently verified trace/cleanup. CI attempt-one hosted
runner acquisition failure is retained; exact-source retry now passes all four
required lanes, four server digests/six full inventories and affected recovery
controls. Broader/native workflow/physical ARM64/candidate qualification remain (E-V09-M10/E-I142). Drawn workflow/full-helper/adverse topology/candidate remain.
Both VMs remain running and G: is untouched/HOLD (E-I136/E-V09-M1/E-I137;
[E-I138](evidence/E-I138-recovery-trace-fixture-selection.md);
[E-V09-M2](evidence/E-V09-M2-macos-trace-calibration.md);
[E-V09-M3](evidence/E-V09-M3-macos-full-session-context.md);
[E-V09-M4](evidence/E-V09-M4-macos-recorded-session.md);
[E-V09-M5](evidence/E-V09-M5-macos-combined-mapping-session.md);
[E-V09-M6](evidence/E-V09-M6-macos-authopen-preparation.md);
[E-V09-M7](evidence/E-V09-M7-macos-authopen-device-binding.md);
[E-I140](evidence/E-I140-unix-device-authorization-identity.md)).

I134/I135 are verified preliminarily at clean 3caf480: metadata-only no-stream handling preserves
real encrypted data/header refusal; test-only fixture canonicalization retains all assertions.
The identical 78 search/57 content controls, affected host checks, four clean CI jobs and all 71
SDK-free cases per Windows/Ubuntu guest pass. Four server digests/six TRX inventories and all native
source/fixture/payload/output/case/process/temp pins independently verify. Failed attempts stay
retained; shared-profile throughput/cancellation passes. Native desktop/AT, independent remaining
format variants, reference hardware and candidate qualifications remain (E-I134/E-I135; E-I136).

I133 unknown-length size criteria now pass verified preliminary revalidation at clean 578a0ed.
Twelve baseline failures/six controls, eighteen corrected production observations, affected/full
host coverage, four CI jobs and 24 SDK-free cases per Windows/Ubuntu lane pass. Four server digests/
six TRX inventories and all guest source/input/output/process/temp pins independently verify.
Failed setup/test/cleanup observations stay retained; repaired private cleanup observers do not
change tests or permissions. Additional formats are under independent validation; native desktop/
AT/exact-candidate obligations remain (E-I133).

I132 is a test-only quick-search cancellation checkpoint repair. Original macOS CI failure/full
server-bound artifact inventory and one controlled observer failure/three controls are retained;
exact historical timing is unavailable. Nine affected/full App 322 with 21 declared skips pass,
including forced before/after Escape completions. The earlier long-temp Git fixture failure remains
retained; a short owned temp root fixes setup and is removed. Clean 5a11100 passes all four CI jobs;
four server artifact digests/six complete TRX inventories verify, with all nine quick-search cases
passing without skips in each Windows/Ubuntu/macOS App inventory (E-I132). ARM64 App/package checks
pass with log totals; no per-case TRX is claimed there. No production change; native UI/AT/candidate remain.

Clean fdb17b4 completes preliminary I129-I131 revalidation: four required CI jobs pass; three
artifact server digests and six full TRX inventories verify. Windows has 74 affected passes; each Unix
App lane has 58 passes/sixteen declared Windows factory/ACL/drive-letter/Shell skips. Original sixteen
retired-count cases, two new forced-refresh controls and all twelve caption cases pass per platform.
The SDK-free Windows guest passes all 74 controls with zero skips. All 381 payloads/382 ZIP members,
twenty-seven canonical source files, exact case inventories and owned process/temp cleanup verify.
Earlier f341dfd 41/43 and 3a408eb 70/72 guest runs remain failed and retained; controlled reproductions
do not identify their exact historical event triggers. Native Esc/frame/AT, slow/cloud/hung hardware,
other native environments and final-candidate qualification remain required (E-I129, E-I130, E-I131).
Native sparse/hard-linked/mixed data now passes six cases without skips per Windows 26300, Ubuntu
26.04.1/ext4 and macOS 27.0.1/arm64 lane (E-V12-N1). Exact logical counts, 32 marks/focus, complete
quick-view/status labels and refresh reconciliation pass. All input/output pins, 1,248 before/after
native rows, twelve sparse files/twelve link groups and process/temp cleanup independently verify.
Failed harness attempts stay retained; no production defect/new issue was found in this slice.

The saved successful Mac result was retrieved after owner confirmation following its final test transport
timeout; exact interruption cause is unknown. Physical Mac work was then owner-deferred. Authorized
minimal discovery found no address: cached neighbors, one known-host DNS query and one Bonjour packet
with a four-second response window. No subnet sweep or Mac command/test ran during that discovery. Python is present;
dotnet is not on the SSH PATH. Only owned self-contained validation files were added; no global
dependency installation/system-setting change occurred. Needed installations remain authorized.
Mandatory Mac artifact/native/distribution work remains. Both VMs stay running; G: is untouched and
its historical source-change gate remains held. Computer Use's recorded startup failure still blocks
native input.

I128 clean c453925 correction retains viewer line/page/Info source calls through close. Four actual-production
failures/four controls are retained; identical corrected probe, affected/full host suites, four clean CI jobs
and 143 SDK-free guest controls pass. All 143 affected Windows/41 Unix App cases pass without affected skips.
Inputs/697 payloads/698 ZIP members/twenty-five canonical sources/case inventories/owned cleanup verify
(E-I128). Native/hardware/aggregate/candidate remain. Elevated Codex restart still fails before any UI target: Node reports Windows sandbox
setup refresh errors; shell commands still have an unelevated token. VIX guest commands remain usable.
The owner again authorizes the disposable USB at G:; inventory matches serial 2F2000129618/FCTEST/NTFS,
non-boot/non-system. Original source-change evidence stays held; no USB mutation has occurred in this slice.

I127 is remediated preliminarily: inaccessible counts retain lower-bound labels/state across refresh and allow
retry after restored access. Actual-production baseline failures/corrected probes, affected/full host suites,
four clean 9d33282 CI lanes and 122 SDK-free guest controls pass (E-I127). All 122 affected Windows CI cases pass;
each Unix App lane has 50 affected passes/13 declared Windows-fixture skips. Two failed fixture CI attempts are
retained; diagnostics identify only Windows' DACL auto-inheritance marker, with every ACE still checked.
Inputs/702 payloads/703 ZIP members/thirty canonical sources/artifact digests/XML/owned process/temp cleanup
verify. Native/AT/exact-candidate qualification remains required.
Owner restart did not restore Computer Use: initialization exits before
selecting any host/VM window; guest command execution remains available and independent V12 work continues.

I107 is closed for preliminary remediation: the owner confirms corrected host and 1a9f1ba guest success.
Native VM trace identifies unchanged theme application removing the open menu; a palette guard fixes two
baseline regressions. Complete App inventory passes 233/248 with 15 platform skips; copied host App DLL matches
the working fix. All four affected CI/manual lanes pass. Guest input/module are reverified; native trace has 13
pointer-driven closes, no logical detach and a menu open for 47.6 seconds. Exact-candidate interaction remains pending (E-I107). Agent Windows UI
automation is independently unavailable: JavaScript health fails with Windows sandbox setup refresh errors.
No agent-driven Windows input was sent. Owner restored the Windows snapshot; guest access works and VM must
remain running. I106's wider native audit reproduces a renamed-apphost false absence; identity correction under
validation. Refined root census/ordinary and root positive cases pass; ordinary-account absent cases remain
unknown. Corrected Windows guest menus are now verified preliminarily. Continue
unblocked recovery visibility/race work; latest affected d8c6f3b CI passes all four lanes.
Confirmation-window audit also reproduces stale admission on all four device routes; repeating full safety after
acceptance passes all twelve elevated guest admission cases. Host App 242 pass/21 skips; guest full guards 29 pass/
three skips. Native Ubuntu and both strict Unix CI inventories pass 74 cases/22 declared skips each; all four
cc1acf2 CI lanes pass. These are recording-reader/component checks, not source-device tracing;
broader I106 remains open and no candidate exists.

The owner has now connected the authorized USB serial 2F2000129618 to the host and permits disposable use.
Its strengthened identity/backing-disk preflight passes. Clean production census still reports unknown on the host
and elevated Windows guest after the owned FileCat fixture exits; this reproduces a Windows scan availability limit
(E-V09-G1). No census bypass. VMware briefly exposes this exact USB in the guest, then disconnects it before the
native physical preflight starts; wrapper refusal and routing evidence are retained. The USB is again on host G:.
The owner launches the verified clean 1df5dff host payload: preflight and FAT32/exFAT/NTFS component cases pass,
325 generated deleted files recovered exactly per filesystem. The byte-checker audit then reproduces false
acceptance beside missing ranges; the corrected checker passes controlled/native checks and all four CI lanes.
85bb17d adds per-item hash capture. Its subsequent physical attempt overlaps an older campaign on the same USB;
both complete Failed and neither overlapping attempt qualifies. The interprocess guard correction passes controls,
and the clean exclusive 090a2b6 host run passes preflight and all three formats, with 325 deleted files exact per
format and all 327/327/326 complete recovery claims independently matched (E-V09-G3).
Installed-helper tracing and production census remediation remain separate work.
The elevated Windows tracer's exact read/write and untouched-path controls now pass; full PML/CSV and independent
configuration restoration are retained (E-V09-G4). This instrumentation control never accesses the USB and does not
close physical source-write, installed-helper or candidate qualification.
The owner executed the prepared G5 launcher with clean f2f0141 inputs. Native preflight/read cases pass, but complete
physical hashes differ and the PML/CSV end before the positive controls and test. Independent inventory/native
parsing retains this failed source-write qualification (E-V09-G6). No process attribution or zero-write conclusion
is available. Hold further USB tests, investigate capture off-source, then address the unexplained source difference.
Six bounded off-source timing comparisons are prepared (E-V09-G7). Owner authorizes agent launch; Windows RunAs
succeeds and the setup gate clears. All six complete: B/C/D pass exact timed marker/child controls, A/E/F fail;
158 files/full PML/CSV and final thirty-value restoration/worker census independently verify (E-V09-G8).
No durable Procmon remedy or source-change attribution. Built-in WPR ordinary-file/disk marker controls now pass:
native/summary 2,561,544 events, zero reported loss, exact bytes/lifetime/disk writes and named cleanup independently
verify (E-V09-G9). Owned virtual-device raw read/write controls now pass with exact process/thread/device/offset/count
attribution and full 64-MiB fixture oracle; two pre-I/O setup failures remain retained. Native 2,057,617 events,
zero reported loss and independent 154-run/74-diagnostic-file inventory/cleanup verify (E-V09-G10).
No protected USB/product/source-write pass. Isolate the unexplained physical source changes before resuming FileCat
USB validation; its affected path remains held. A pure read-only observation retains two complete images matching
G6's after hash, but the long trace loses 51,216 events despite zero live counters; zero-write/attribution fails
(E-V09-G11). G6's before image is unavailable. Custom kernel short raw controls pass with 609,387 events and zero
reported loss (E-V09-G12). Nine-minute duration controls now pass after trace-location correction; metadata-only
failures and matched pilots remain retained, without a claim of exact Windows cause (E-V09-G13). Proof-gated
read-only source observation now passes matching complete images and 5,634,333 native events/zero loss, all
3,718 source reads and no source disk/file writes (E-V09-G14). This diagnostic does not resolve G6's historical
difference; FileCat USB validation remains held. Read-only process-structure snapshots independently verify
but leave five elevated image identities unavailable; no production exemption or absence pass (E-I106-P1).
Windows production lookup now uses the limited image API, with controlled standard/elevated permissions and
full affected host App/platform suites passing (E-I106-P2). Clean 36ee824 guest controls and all four CI lanes also
pass; complete absence/device/candidate qualification remains open. Preceding c162481 CI fails two I108 observer
assertions; corrected observer scopes now pass affected host Core/App and native helper controls (E-I108-P1).
Unsupported mock-pixel attempt retained; no rendered-pixel/source-write conclusion. Exact 1bd931b successor CI
passes all four lanes; all affected Windows XML cases independently verify (E-I108-P1). Candidate rerun remains required.
The physical direct/helper comparison now rejects equal short reads and logs counts/hashes (I110). Off-source
controls and the existing elevated E: reader pass, while the strict USB body remains held. Host C: native reads
return error 50 even with aligned direct Win32 controls; cause remains unavailable (E-I110). No C:/USB qualification.

V12/I92's large quick-search worker/leased-view remedy now passes exact working controls and full affected
host suites; model acknowledgement for one million long names is 0.017–0.494 ms (E-I92). This is not native
input-to-frame/reference qualification. Its affected regression exposes I111's suppressed delayed first rows;
four controlled baseline failures and corrected controls verify the batching fix (E-I111). I112's concurrent
cache observer now waits for actual queued loads, with a held-read negative control; cache policy is unchanged
(E-I112). Final Core 714 pass/46 skips and App 249 pass/21 skips; source/payload/XML inventories independently
verify. Clean b7d2e8 passes all four CI lanes and 27 Windows guest component/headless cases, without skips;
payload/XML/process/temp cleanup independently verify. Native frame/reference/candidate and AT remain required. Computer-use initialization
again exits before application selection/input with the Windows sandbox setup-refresh error; no native input sent.
Both VMs remain required; no shutdown requested. USB validation remains held at the historical G6 gate.

V12 slow quick-view controls expose I113's stale result/error and reader/demand lifetime defects. Six baseline
failures become seven passing controls with request ownership/cancellation and the existing device scheduler;
ten held opens become two active/eight abandoned queued demands, while a healthy device completes. Full App
256 pass/21 skips. A separate GnuPG test-override interference control leads to I114's exclusive fixture;
corrected full Core 714 pass/46 skips. Exact inputs/XML/skip inventories verify (E-I113/E-I114). Clean 7497acf
passes four CI lanes and seven Windows guest controls; exact payload/XML/cleanup verifies. Candidate and native
presentation checks remain open; no change to USB hold or source-safety status.

V13 archive-result narrowing exposes I115's silent omission of all non-file-system references. Four valid baseline
failures become eleven passing Core and two headless Find/content/log/navigation controls; full Core 725/46 skips
and App 258/21 skips pass. Exact working inputs/XML verify (E-I115). Clean ff8746a passes four CI lanes, affected
Windows/Linux/macOS App XML and 13 guest cases; exact input/artifact/cleanup proofs verify. Other-format/native
and candidate checks remain required. No change to USB hold, source-safety disposition or **NO-GO**.

Initial archive discovery loses damage/duplicate-name warnings at its adapter (I116). Two valid baseline failures
and two TAR/gzip positive controls lead to warning forwarding/deduplication; 32 affected and full Core 729/46
skips/App 258/21 skips pass. Exact inputs/XML verify (E-I116); clean 6ecf4a8 passes four CI lanes and 34 guest cases.
Input/artifact/cleanup proofs verify; disk-full failed outputs are retained in the second workspace. Other-format/
native/candidate and sealed-store gates remain required.

ARM64 aed64a7 CI's late-name discovery fixture finishes after one second with an empty list (I117).
Controlled metadata-admitted cutoff passes eight network/full Core 730/46 skips and seven related App controls;
the coupled-cancellation negative control fails twice. Original failure/input/XML evidence is retained and
verified (E-I117). Public timeouts/policy are unchanged. Clean da3a3d6 passes four CI lanes and 15 guest cases;
exact inputs/artifacts/XML/process/temp cleanup verify. Candidate and real-device discovery remain.
The intervening saved-search/duplicates commit ab919ed passes all four CI lanes (E-V13-F2).

V12 metadata invalidation exposes I118: actual checksum-sidecar changes permit in-flight old Matches values
to restore the cache; explicit Compute also returns stale Available. Four original failures and four intermediate
missing-retry failures lead to coordinated publication/invalidation and completion notification. Six demand
controls and full Core 736/46 skips/App 263/21 skips pass; 477 inputs/direct XML verify (E-I118).
Clean 2896108 passes four CI lanes and ten guest cases, zero skips; exact source/payload/artifact digests/XML
and process/temp cleanup verify. Native demand/frame/AT and exact candidate remain.

V12's page-load lifetime controls expose I119: closing a reader disposes its actual local-file source during
an active read/revision call, while closed readers still issue page/refresh demand. Eight baseline failures;
deferred disposal and request retirement pass nine controls/38 affected cases and full Core 745/46 skips,
App 263/21 skips. All 267 inputs/source/active assemblies/direct XML verify (E-I119). Clean de1fd71 passes
45 guest cases, zero skips; exact payload/cases/cleanup verify. Three CI lanes pass; Windows fails the existing
live NTFS fixture I120, while all 45 affected cases pass. Split MFT/live-history controls declare actual unavailable
history skipped; full host platform 161/38 skips and nine golden cases pass (E-I120). Clean successor 9074cf6
passes four CI lanes and 20 elevated guest cases, zero affected skips, including complete live-history assertions
in both CI and guest. Exact inputs/payloads/source/artifact digests/XML/process/temp cleanup verify.
Direct Source/picture use, wider queue/device controls and native/candidate remain. Native automation import
again exits before input with a trusted-Node/kernel-reset error; component and VIX guest execution remain usable.

The direct picture-feed audit exposes I121: four held actual-file controls dispose sources during decoder
reads, and closing a loaded F3 retains its bitmap. Actual feeder borrows and bitmap retirement pass six
controls/20 affected App cases, full App 269/21 skips/Core 745/46 skips. All 1,809 inputs/fourteen sources
per stage/active assemblies/direct XML verify (E-I121). Clean a550fcd passes four CI lanes and 34 guest
cases, zero affected skips; exact payload/source/artifact digests/XML/worker/decoder-child/temp cleanup verify.
Wrong original harness count is retained, with identical payload passing the corrected inventory.
Per-device feed bounds, other direct Source consumers, native frame/AT and exact candidate remain.

Two further device controls expose I122: F3 and quick view each hold three real picture-file calls on a
two-worker device while a healthy device completes. Provider-keyed scheduled feeding passes both controls,
22 affected App cases and full App 271/21 skips/Core 745/46 skips. All 1,812 inputs/fifteen sources/active
assemblies/direct XML and scoped call-count traces verify (E-I122). Clean 82f7488 passes 36 guest cases;
all 58 affected Windows and twenty App cases per Unix CI lane pass. Original CI Windows fails the separate
directory-synchronization test (old content/AwaitingDecision); that run is retained. Clean successor 08acc2f
passes four CI lanes and all affected cases (E-I123).
Exact clean payload/source/artifact digests/XML/worker/decoder-child/temp cleanup verify. Watchdog/hard-cap,
aggregate decoder memory/processes, other direct Source/native/candidate remain.

The synchronization fixture omits the shipping application's Windows platform registration (I123).
A held target reproduces old content/AwaitingDecision/error-access with its portable adapter; a scoped native
adapter passes all 17 comparison/operation controls and full App 271/21 skips. All 2,202 inputs/thirteen raw
sources per stage/unchanged production DLLs/direct XML verify (E-I123). Intermediate culture/path/location/space
harness failures are retained. The original CI request is unavailable, so its precise mechanism remains unknown.
Clean 08acc2f passes four CI lanes and 17 guest cases, zero affected skips. Exact 367-file payload/368 ZIP
members/thirteen sources/server artifact digests/XML/worker/temp cleanup verify. This test-only remedy does not
change production replacement policies; native/candidate checks remain.

I124: an isolated probe using the original production scheduler DLL terminates on an unhandled timer/list-mutation
exception when a hung call gains a replacement. A separate controlled Watch run admits three simultaneous calls
despite a two-worker cap. Enqueue now enforces the cap and Watch scans its initial worker count. Both corrected
probes, two new controls, 48 affected Core/37 App cases and full Core 747/46 skips/App 271/21 skips pass.
All 1,084 working inputs/nineteen sources/probe DLLs/observations/direct XML/process cleanup verify (E-I124).
Clean 18006cc passes four CI lanes and 85 guest controls without affected skips; all 691 payloads/692 ZIP members,
canonical sources/server artifact digests/XML/owned process/temp cleanup verify. Actual hung hardware, wider
shutdown/queue lifetimes, aggregate decoders and native/candidate remain unqualified.

I125: the original scheduler's public Run can pass the initial disposed check, wait on admission, then enqueue to
a closed queue (task never completes) or insert a new queue after the disposal snapshot (callback runs after
disposal). Owned synthetic probes retain both failures. Locked queue/owner shutdown checks cancel both races;
identical corrected probes, two controls, 50 Core/37 App affected cases and full host suites pass. All 1,084 inputs,
nineteen sources/probe DLLs/XML/owned process cleanup verify (E-I125). Clean 749f55f passes four CI lanes and 87
guest controls without affected skips; 691 payloads/692 ZIP members/canonical sources/artifact digests/XML/owned
process/temp cleanup verify. Wider resource lifetimes and physical/native/candidate remain unqualified.

I126: activation and page-refresh revision calls were not retained by the comparison's close/reopen lifetime.
Four actual-production/owned-file controls dispose during the held call and fail on its released handle. The view
now counts revision/length/read calls and activation uses those views. Identical corrected probe and App-only DLL
swap, four regressions, 41 App/50 Core affected and full App 275/21 skips pass; exact inputs/source/DLLs/fixtures/XML/
identity-aware cleanup verify (E-I126). Clean e406c96 passes four CI lanes and 91 guest controls without affected
skips; 693 payloads/694 ZIP members/twenty-one sources/artifact digests/XML/owned process/temp cleanup verify.
Wider/native/candidate remain. Updated Computer Use 26.930.41038 import and a separate plain JavaScript startup
both crash before any app input. Live Windows UI validation needs the Node runtime restored; current desktop
state was not observed. At 12:25–12:26 UTC the resumed plain call and one reset/retry both report a Windows sandbox
helper setup-refresh failure, exit code 1. Exact diagnostics retained; full Codex restart requested as a setup
recovery attempt before live UI can proceed. Both VMs remain running; the USB source-change gate remains held.

| ID | Needed | Status |
|---|---|---|
| ENV-01 | Physical Apple Silicon Mac (MAC) | Reachable again at 192.168.0.199; prior two bounded timeouts retained. Temporary native sleep-disable/root restorer and four finite closed-lid SSH/sensor checks on AC verify; first AC-disconnect restoration and fresh rearm/final restoration are independently sealed. Original system/custom/runtime sleep state restored, both exact root restorers and owned caffeinate absent; current Mac tests complete. Clean 1559933 icon controls/getter/native pins/cleanup pass (E-I143). GUI preparation verifies 201 inputs/21 retained pins/seven ranges/normal desktop context/cleanup. Actual current-component owner-cancelled refusal independently verifies no source/no timeout, native cancellation/channel closure, 201 inputs/55 retained pins, source/detachment/agent removal/nine absences (E-ENV-MAC-1/E-V09-M9). Earlier staging and completed native proofs retained. I137/I138 and v12 actual 593583e complete session/bytes/source/cleanup pass at their identities. V2 formatted offsets fail; v5 selectors/v6 raw controls retain limits (E-V09-M2). V8/v9 census/v10 elevation/v11 marker failures retained. V12 104 pins/18 tracked +15 derived absences, 2,669,238 events/430 controls/ordinary FD reads verify; 79 shared mmap backing FDs unresolved and supervisor failure/recorder exit unknown retained. Root syscall provider unavailable under SIP; v5 process calibration passes three known FD/offset/address mappings, 32 pins/three absences/whole bytes/SIP enabled. New v13 all 77 shared mapping FDs resolve; 1,197 payload/117 retained pins, recovery/bytes/source/cleanup and every recorded command exit zero verify. 43 unmatched private maps retained; full-source/helper/authopen/topology/native/clean Mac/candidate remain (E-V09-M5). Native component refusal now verifies with owner cancellation/no source/no timeout, 42 retained/197 input pins, source/detachment/seven absences and clean command exits. First approved attempt remains a failed refusal expectation. Fresh approval now verifies actual O_RDONLY/closed EBADF, seven independent ranges/six bounded native reads, clean exits/42 retained pins/source/detachment/seven absences. Actual held equal-size replacement exposes I140; all 88 retained/200 input pins/source/cleanup verify. Committed guard/clean Mac 19 passes, corrected native v3/v4 replacement rejection and four-lane successor CI pass. V2 SSH authorization failure stays retained. Current unchanged approval v2 timeout retained; fresh v3 component approval/read-only/seven ranges/six native reads/pins/source/cleanup pass (E-V09-M8); current desktop refusal now passes (E-V09-M9); removal/broader qualification remain (E-V09-M7/E-I140). Device removal/drawn workflow/full-helper/candidate remain (E-V09-M6). Personal installation, not clean qualification; earlier E-V12-N1/E-ENV-05/E-I129 |
| ENV-02 | Physical Windows 11 ARM64 device (WA) for D-48 | None available |
| ENV-03 | Physical Windows 11 x64 on a GA serviced release for final W64 qualification | Execution host is Insider 26220 (preliminary only) |
| ENV-04 | Fresh Ubuntu 24.04 and 26.04 desktop VMs (LNX) | **Environment available:** owner authorized updates/reinstalls; clean snapshots and actual GNOME Wayland sessions retained (E-ENV-07). Dev.539 full package matrix passes both SDK-free baselines (E-V19-P2); archives verified. I106 ordinary-name native after and successor CI pass; dev.549 three formats pass successor native checks on existing 26.04 (E-V19-P3). Renamed-apphost audit reproduces a further discovery gap; identity correction, wider recovery/availability audit and exact candidate remain open |
| ENV-05 | Disposable Windows VM matrix: standard user, administrator, Administrator Protection, UAC prompts, HKLM/WOW64 roots | Lent Windows 11 Insider 26300 VM (partial): administrator elevated/unelevated runs; UAC without prompts; no standard-user account yet. Historical cold-boot hangs on 2026-10-01 are recorded in E-ENV-05. **2026-10-02: owner restarted the VM; running and guest access verified** (E-ENV-06), so the immediate stopped-VM gate is cleared. ReFS/SMB copies resumed with `eng/validation/Invoke-ReFsCloneVm.ps1` (E-V03-CLONE-1). Remaining account/UAC/Admin Protection matrix and GA qualification still needed |
| ENV-06 | Controlled SFTP, FTP, FTPS and SMB servers (at least two implementations each where applicable) | Two SFTP/FTPS implementations were set up and exercised: OpenSSH/vsftpd and ProFTPD; Samba supplies one SMB implementation (E-V08-L2). Ubuntu VM is running and guest access verified on 2026-10-02; current server availability not revalidated in this turn. Additional applicable SMB implementation and candidate reruns remain open |
| ENV-07 | Identity-bound disposable media and devices: USB stick G: (serial 2F2000129618), Android and iOS devices in `FileCat-test` folders | Earlier positive run retained; overlapping attempts invalidated. Exclusive clean 090a2b6 component successor passes preflight/formats and independent recovery-byte checks (E-V09-G3). Subsequent G6 complete disk hashes differ and trace controls fail; no attribution. Further USB tests held pending off-source capture investigation/source-change resolution. Installed-helper tracing/census remain open; recheck interlocks before use (§8.2). Phones used in owned fixtures (E-V21-M1/I1/U1); locking mid-transfer needs owner |
| ENV-08 | The reference performance machine (4 cores, 16 GiB, NVMe, 1080p) exclusive during V16 | Not available; the host is a 12-core 64 GiB developer machine shared with other work |

## D. People

| ID | Needed | Plan |
|---|---|---|
| PPL-01 | Two Commander-style users (covering Salamander, Total Commander, FAR), two independent newcomers, an OS-familiar user per platform | V17 |
| PPL-02 | Screen-reader operators and an experienced screen-reader user: NVDA, Narrator, JAWS, VoiceOver, Orca | V18 |
| PPL-03 | Human attestation of UAC, polkit, authopen and TCC consent and of physical device identity | §12.3 |
