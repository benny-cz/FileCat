# FileCat 1.0.0 — release execution report

Operational plan: [FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md](../../design/FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md)
(with its review disposition). Companion records: [issue register](FILECAT_1_0_RELEASE_ISSUES.md),
[evidence index](FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md), [open blockers and decisions](FILECAT_1_0_RELEASE_BLOCKERS.md).
Candidate-specific evidence will live in `docs/release/1.0.0/<candidate-id>/` once a candidate exists.

## Current state (updated 2026-10-06)

I18 package prerequisites and asset selection are partially corrected: all three
package jobs now require ARM64 and both upload routes share an exact versioned
list and byte manifest at b9526b9. Eight controls pass on each of four hosted lanes.
Development dispatch passes all test lanes/Linux and Mac package jobs; actual
Windows eight-output compilation/selection/935 raw sources/tool restoration and
all retrieved package bytes verify. Original main CI remains failed on one separate
cold picture checkpoint. Fixture-only capacity/diagnostics passes 64 before/64
after with identical production files and full App 386/23/409. Committed 4b2b9d7
native repeats and all four successor CI lanes/fourteen digests/inventories verify
(E-I18-A1/E-I146). A read-only reference gate now refuses stable producer inputs;
sixteen host controls/five exit-code probes/static dependencies pass; hosted
successor execution continues (E-I18-P2). I18 stays Open.

I03 native compiler provenance is partially improved at 573ed2c with exact
927-source/four-mode/local input byte verification and four green CI lanes,
ten digests/fourteen inventories/two native report-map sets (E-I03-NATIVE).
I160 atomic nonce correction at 37c88c8 passes clean 931-source/four-mode/native
controls and all fourteen new x64/ARM64 CI cases. Its exact CI run remains failed
on one Mac picture-demand case; the failed original is retained (E-I160).
New I161 independently reproduces three simultaneous viewer header reads, then
finds visible page demand bypassing the header-only correction. Both paths now
use shared device admission; identical final observer/only App DLL changes,
healthy device/cancellation/source lifetime/cleanup and full host suites pass.
Committed 8975fdf clean Windows 26/0 and Mac/Ubuntu 25/1 each pass; 932 raw sources,
four native modes/pins/cleanup and all four successor CI lanes/ten digests/fourteen
inventories/two native report-map sets/eight new cases verify (E-I161).
Current checkpoint: 139/161 preliminary, one Closed, 21 remaining issue
remediations; all campaigns/final candidate/human GO remain gated.

I158 shared-runtime trust is preliminarily corrected at 69603ec: four clean
native modes/924 raw sources/receipts, protected SC/FDD handoff and unsafe FDD
block with exact runtime ACL/content restoration verify. All four CI lanes/ten
digests/fourteen inventories/two native byte/source receipts pass (E-I158).
I159 pre-execution executable trust is preliminarily corrected at 8b4be9d:
caller verifies executable/ancestors before ShellExecute. Identical final
observer/only caller DLL differs; file/parent grants refuse without a process
while healthy native SC/FDD handoff passes. Clean 927-source/four-mode publishes,
all four CI lanes/ten digests/fourteen inventories/new x64/ARM64 test executions
and native receipts verify. No physical device or real consent is exercised.
Broader I17/I03, limited caller/races/candidate/human stable GO remain open.

I156 ACL trust is preliminarily remediated at ce8189e: clean 918-source/four-mode
native publishes, protected handoff/four adverse controls/profiler regression and
all four exact-source CI lanes/digests/inventories/native byte receipts verify.
I157 read-pipe identity is preliminarily remediated at b4b6e1b: the held-live
runas PID is checked against the kernel pipe server before Info. Clean SC/FDD
controls reject counterfeit peers with zero protocol bytes and accept separate
synthetic same-helper-PID regular-file positives; pins/cleanup/four green CI
lanes/ten digests/fourteen inventories/two native test executions verify.
No physical source opens; real consent/limited caller/full I17/candidate remain.

I155 native profiling is preliminarily remediated at fc5e706/7183268: actual
committed 917-source/four-mode native publishes, SC/FDD profiler rejection and
separate synthetic CLR handoff verify. Two positives/431 inputs/cleanup pass;
four exact-source CI lanes/ten digests/fourteen inventories and both downloaded
native Windows executables/source/compiler receipts verify. Broader I17/I03,
limited-caller/consent/candidate remain; no human stable GO (E-I155).

I152 is preliminarily remediated at e3c99d5: unchanged native probe/only clean App
DLL changed eliminates 14 SMB flows/126 packets and preserves ordinary badge/
exact controls/zero reported loss/cleanup. Identical final seven controls/721
inputs, full host 385/23/408, native Windows 28/3 and Mac/Ubuntu 15/16 verify.
I153's unsuitable timestamp oracle is corrected at fd1d780: actual complete
snapshots pass even with a delayed writer, while both deliberate unbounded
debounce cases still fail. Host affected 3/full Core 819/56/875 and all three
native repeats pass; all four exact-source CI lanes/five digests/six complete
inventories now seal, including ARM64 startup/drawing/installer. Original CI
scheduling history remains unknown.
Mac SSH resumed autonomously and its queued I151 14/10/350-pin repeat passes.
Current 137/159 preliminary, one Closed, 21 remain; all campaigns need final
qualification, no candidate/human GO; NO-GO (E-I152/E-I153).

I03/I18 SDK/action/checkout correction is committed at 78a0716: exact 10.0.401 without
roll-forward, official immutable action commits, explicit hosted OS labels,
unstored checkout credentials and pre-build clean source/compiler/runtime/image
receipts. Two positive/three negative owned controls and host build pass;
portable/ARM64 Core/Remote/App inventories now retain TRX. All four current CI
receipt steps and full run seal pass: ten server digests/four receipts/fourteen
complete execution inventories verify. Truncated/repeated theory displays are
retained with distinct execution IDs; full arguments remain unavailable. Broader
provenance/release controls remain open (E-I03-SDK-ACTIONS).

I154 preliminarily remediated at 99e54b3: actual protected SC/FDD broker components load an owned managed hook
before Main/plan/consent. Already-administrative caller; no unelevated/UAC bypass
claimed. Configuration-only and actual working four-mode publish/native controls
block hooks, with two positives/all pins/owned cleanup. Helpers are bounded and
stopped after six seconds; UI/healthy plans remain unobserved. Clean committed
native repeat and all four CI lanes/ten digests/fourteen execution inventories
now seal. Wider I17 loader/profiler/runtime qualification remains open (E-I154).

I03/I18 compiler provenance is preliminarily corrected at 0646053: exact official
Inno package/119 frozen inputs, complete 122-file native inventory, installed exact
license and installer recipe/output receipts. Final-source nine native controls and
all 19 retained output hashes/cleanup pass. Recipe controls use inert payloads;
all four CI lanes/five digests/six inventories and actual ARM64 recipe receipts
verify on 37410913442 (E-I03-INNO). Broader I03/I18 stay open.

Earlier I151 checkpoint (Mac repeat since completed above): unchanged native probe/only App DLL
changed removes all 14 SMB flows/126 packets; ordinary badge/two exact controls
and zero reported loss verify. Six host baseline failures correct; full App
378/23 skips/401, clean Windows 21/3 and Ubuntu 14/10 pass with source/payload/
temp/process/listener pins (E-I151). Mac SSH timeout is queued for owner help
after 08:40 CEST; all four exact-source CI lanes/four digests/six inventories/
401 exact App names and ARM64 startup/drawing/installer verify. Historical I151 count 129/151 preliminary, one Closed, 21 remain;
all 24 campaigns need final qualification, no candidate/human GO; NO-GO.

Earlier sealed I150 slice (historical counts): preliminarily remediated at 874b7ae/52df3d7: identical final native tests
fail six before and pass six after; unchanged probe blocks both original external
filter executions while retaining ordinary badges/positive controls. Clean native
Windows/Mac/Ubuntu and all four successor CI lanes/four digests/six inventories/
394 exact App names verify. Original ARM64 NUL failure, Ubuntu missing Git and
observer failures remain retained (E-I150). 128/150 preliminarily remediated, one
Closed, 21 remain; all 24 campaigns still require final qualification. Owner
needs stay queued until 08:40 CEST; no candidate/human GO; NO-GO.

Fresh I16/V24 process/file/share-contact component controls independently pass on
actual 313d40b: 66 process events/26 verified descendants, 594 file-open attempts/
249 exact paths, and 118 pcap packets with only the two known endpoint control
flows. Independent positives, reported zero loss, source/payload/temp/process and
owned receiver/recorder cleanup verify. Original setup and decoder/time limitations
remain retained. Wider indirect/reparse/environment/global-config, protected-file
effects and native/candidate scopes remain open; continuation needs no interaction
(E-V24-D2).

I149 is preliminarily remediated at committed 313d40b: eight RAR recognition
failures/six malformed positives, identical before/after bytes with only archive
DLL changed give eight failures/22 positives then 30 passes. Affected host 77/0
and full Core 818/56 declared skips/874 cases independently verify. Clean Windows/
Mac/Ubuntu each pass 77/0, including all 30 new controls; 885 raw source blobs and
unchanged 331/330/331 payloads, inventories/temp/process checkpoints verify. All four
exact-source CI lanes/four server digests/six complete inventories pass. All 77
affected Core and 388 App names match; six native PE input paths remain explicit
differences. Initial compile and timed-out artifact-read failures remain retained.
The wider legacy probe passes 42 search/42 exact member reads, with damaged reads
explicitly refused and source/artifact/cleanup pins unchanged (E-I149).

I148 is preliminarily qualified at committed 9da5738: six legacy secondary-volume
failures are corrected under explicit format opening. Clean Windows/Mac/Ubuntu
each 63/0, 882 raw Git blobs, unchanged 331/330/331 payloads and temp/process
checkpoints verify. All four exact-source CI lanes/four server digests/six complete
inventories pass; all 63 affected Core names/16 new cases and 388 App names are
exact. Six native PE-path Core names differ explicitly; originals and the failed
strict/successor observer attempts remain retained, no names normalized. Pure UDF
six revisions match final read-only native/independent oracles, 36 search/132 exact
reads and six durable native revision controls. Wider/native UI/candidate remain.
Counts 127/149 preliminary Remediated, one Closed, 21 remaining issues; 24/26
checklist steps remain partly/fully open, with no individual test-item denominator.
Owner needs remain queued until 08:40 CEST. Mac awake restoration is independently verified
(E-I148/E-V13-UDF2/E-I149).

I147 is preliminarily remediated at 6cf17e5 after four reproduced Windows icon
failures. Identical before/after controls, three portable queue/retry/stale/LRU
controls and full host 365/23 declared skips/388 cases pass. Clean source/native
Windows 16/1 capture skip and Mac/Ubuntu 10/7 explicit skips, unchanged payloads/
owned temp and process checkpoints independently verify. All four exact-source
CI jobs pass; four server digests/six full inventories/388 exact App names and all
eight new expected outcomes verify. The timed-out GitHub read stays retained before
successful bounded read/collection. Broader consumer/native/frame/AT/hardware and
candidate scopes remain. Disc-image/legacy RAR work continues; no interaction is
currently needed (E-I147).

I144–I146 are preliminarily remediated at cb85f0a. Shared decoder admission is
four workers/32 waiters, Unix child runtime diagnostics are disabled to avoid
orphan debugger FIFOs, and the normal-admission fixture has its own watchdog
threshold. Original eight-worker demand, Unix cleanup and Windows CI failures
remain retained. Affected host 25/full host 357 with 23 declared skips pass;
clean exact-source Windows 25/0 and Mac/Ubuntu 24/1 inventories, unchanged payloads,
automatic owned-temp cleanup and command/test-process checks independently verify.
All four required CI lanes at 37387554116 attempt one pass; four server ZIP digests,
six complete TRX inventories, all 380 App case names and all six new admission
cases verify. The Mac collector's receipt-variable failure remains retained before
a fresh corrected observer. Historical CI watchdog attribution remains inferred.
Displayed bitmap memory, Unix containment, wider native/frame/AT, hardware and
candidate scopes remain open (E-I144/E-I145/E-I146).

The owner requested unattended execution until 2026-10-06 08:40 CEST (06:40 UTC),
with interaction gates queued for later. Temporary same-chat continuation and a
one-time 08:40 check-in are created and their saved configurations verified. No
interaction is currently requested. Windows Computer Use import was rechecked and
still fails with the trusted Node process exiting before input. Native desktop/tool
repair, owner decisions/credentials, reference hardware and phone-lock interaction
remain queued; all other executable work continues. Mac SSH UID 501 is available;
fresh bounded awake support is applied on AC at 22:39:22Z using the identical pinned
controller. Root restorer PID 19109 is verified before application; restoration is
completed at 00:53:09Z after this archive slice: original absent-key/system/custom
settings and runtime sleep are restored, all three restorers and the owned
caffeinate are absent. No new closed-lid observation is claimed (E-ENV-MAC-1).

I143 committed remediation at 1559933 follows a real Ubuntu production icon-budget failure:
50,000 requests retain 50,000 entries and observe a 43,795 waiting-queue peak. Linux/Mac
now use a 4,096-entry LRU, nonblocking 256-request queue and entry identity for stale
completion rejection. Six new controls and full host App 351/23 declared skips pass.
Clean raw Git/source manifests and native Mac/Ubuntu 7 pass/2 declared skips each
verify all six new cases, 350 payload/six retained pins and process/temp cleanup.
Identical Ubuntu 50,000-entry getter demand now retains 2,538 entries with queue peak
256 and zero remaining; 261 input/four retained pins and byte-identical App/Core
references verify. Exact-source CI 37379380371 now passes all four required lanes/four server digests/
six complete TRX inventories; affected icon Windows 8/1 and Unix 7/2 declared skips
verify all six new cases. ARM64 App 357/17/374 and drawing/installer checks pass by
logs; per-case physical/frame/candidate limits remain (E-I143). No interaction needed.

Mac connectivity is restored at 192.168.0.199 after owner input; prior timeouts are
retained. At the owner's request, temporary system sleep-disable and a root restorer
were active; four SSH/native-sensor samples over 62.868 seconds verify closed-lid AC
operation. The restorer exits after AC disconnect at 21:30:26Z; independent native
readback now verifies the originally absent key removed, exact original system/custom
preferences, native sleep reenabled and root restorer absent. The original timed
ordinary caffeinate remained active for the clean Mac check. AC returned and a fresh
verified controller rearmed bounded support; at 22:05:25Z the exact root restorer was
signaled, original settings independently verified restored, and the exact owned
caffeinate stopped. Both prior root restorers and caffeinate were verified absent. No temporary Mac
power change remained when that earlier slice ended (E-ENV-MAC-1). Fresh gui/501 refusal-driver preparation verifies 201 input/21
retained pins, normal UID/groups, seven golden ranges, read-only closure, agent removal,
source/detachment and three absences. Actual desktop-session refusal now independently passes on the committed component:
no source/no timeout, two EACCES source opens, native helper cancellation/channel closure,
201 input/55 retained pins, unchanged detached source and nine absences verify. Raw
duration is independently measured; finite tracing/full-workflow/candidate limits remain
(E-ENV-MAC-1/E-V09-M9).
Actual owner-approved device removal independently verifies: both owned paths disappear
before the cue, helper ENOENT occurs 75.100791 seconds later, no source/no timeout,
channel closure/reaping, unchanged detached image/agent removal/nine absences and
201 input/57 retained pins pass. Failure reporting says not approved despite approval;
I142 is committed at 348cbc7. Clean physical Mac 23 pass/4 declared skips and Ubuntu
26.04.1 guest 25 pass/2 declared skips verify all four new cases, exact raw Git source,
payload/result pins and process/temp cleanup. A fresh owner-approved removal repeat
verifies no source/no timeout and the correct changed-or-removed IOException. Native
helper ENOENT occurs 54.651673 seconds after removal; subsequent path stat, channel
closure/reap, 201 input/57 retained pins/source/cleanup/nine absences verify. Original
8f75856 failure is retained. CI 37373490704 attempt one has a hosted-runner acquisition
failure; exact-source attempt two now passes all four required lanes/four server
digests/six full inventories and affected recovery controls. ARM64 App 351/17 declared
skips, package drawing and installer compilation pass by logs. Per-case native CI
inventory/physical ARM64/candidate limits remain. No further Mac interaction is queued
(E-V09-M10/E-I142).

I141's feeder-based fixture correction passes clean 6197592 CI 37359106547 in all
four required lanes. Four server ZIP digests/six complete TRX inventories and 60 affected
viewer cases independently verify. ARM64 App 351/17 declared skips, native startup/drawing
and installer compilation pass; per-case ARM64 TRX/physical/candidate limits remain.
The original 8f75856 ARM64 failure and controlled three host failures stay retained.
Host affected 20/full App 345/23 declared skips and every original lifetime/deadline/byte
assertion pass. Production behavior is unchanged; historical CI ordering is unknown (E-I141).

I140's committed 8f75856 guard passes two fresh native held-approval replacement checks,
v3 and the owner-requested v4 repeat. Selected inodes 887/907 become 891/911 at the same
raw path/equal size. Both approved opens reject changed identity before source construction,
with no returned source, timeout or probe content read. Native inferred FD 62 fstat/close,
zero size ioctls, 200 input/88 retained pins per case, unchanged detached images/eight
absences and clean recorded command exits verify. Descriptor inference/finite mapping,
source-write/drawn-workflow/candidate limits remain explicit (E-I140/E-V09-M7).
Clean native six new cases/19 passes with four declared skips and original source/payload
pins verify. Earlier SSH authorization failure is retained separately. New unchanged-source
approval setup verifies 197 input/20 retained pins, two native direct controls/seven golden
ranges each, source/detachment/four absences and launcher. V2 receives no descriptor during its 90-second wait despite owner-reported approval;
the failed worker and shortened trace remain retained, source/cleanup verified, cause unknown.
Fresh v3 unchanged approval component passes read-only rights/seven ranges/closure and
source/detachment; independent 55 retained/197 input pins, six bounded native reads,
source-FD closure/EBADF and seven absences verify (E-V09-M8). Temporary awake support and
benny sudo verify; a harmless desktop launch-agent/session control passes and is removed
(E-ENV-MAC-1). Native desktop refusal independently verifies (E-V09-M9); actual removal safety/reporting verifies on committed I142 (E-V09-M10);
broader native/candidate qualification remain. VMs stay running; G: untouched/HOLD.
Progress: **125/147 issue rows preliminarily remediated**, **one Closed**, **21 remain for
remediation**; **24/26 checklist steps partly or fully open**, all 24 campaigns still need
final qualification. No candidate or human GO; overall **NO-GO**.

The 34112ac CI failure is retained; clean e7a1e7e CI 37321377008 now passes all four
required jobs. Four server digests/six complete inventories/24 affected cases independently
verify. ARM64 App 347/17 declared skips and package checks pass by logs; physical
qualification and ARM64 per-case TRX remain unavailable.
I139's follow-up finds the timer starts before initial page detection completes. Four
controlled HTML/Markdown timeouts reproduce; page availability fixes all eight cases with
original assertions/deadlines. The exact corrected placement, eight actual host cases and
full App 341/23 declared skips pass with command exits zero. Test-only correction;
clean successor CI verified (E-I139). Historical CI timing remains unknown.

I139 corrects a scheduling dependency in the deliberately blocking HTML/Markdown viewer
validation worker. The 63e7af8 native ARM64 CI lane times out before read entry; the other
three lanes pass. A private four-worker saturation reproduces that entry timeout, and the
same case passes with a dedicated worker and every original deadline/assertion preserved.
Eight affected host cases and full App 341/23 declared skips pass with command exit zero.
The historical CI scheduler state is unknown; failed setup/supervisor attempts remain retained.
Clean 6509eff successor CI passes all four required lanes, including native ARM64 App
347/17 declared skips, startup/drawing and installer compilation. Four server digests/six
full inventories and 24 affected cases verify; ARM64 has log totals, no per-case TRX.
Physical ARM64/candidate qualification remains (E-I139). Progress: **119/141 issue rows remediated**,
one separately Closed; **24/26 checklist steps partly or fully open**. No candidate/GO; NO-GO.

I138 repairs the whole-file trace driver's fixture choice and finished-state wait. I137's clean
Mac session now opens the source but selects intentionally partial frag-a.bin; FileCat correctly
reports lost bytes and CompletedWithIssues. Independent generated prefix/zero-fill bytes verify;
the original session remains failed. Driver now requires a Recoverable file and the shared job
finished-state predicate while still demanding Completed. Host affected 40/12 declared skips,
Core recovery jobs 10/0 skips pass. Clean 593583e's Mac complete session passes 1/1 without skips:
60 independently generated bytes match, 70-second timed-save wait completes, writable source
image stays byte-exact and detached. Four required CI jobs/server artifact digests, six full TRX
inventories, 844 exports/1,196 payloads/1,197 ZIP members and owned cleanup verify (E-I138).

I137 corrects a Mac recovery-availability defect. After identity-checked, owner-authorized SIGTERM
of Visual Studio and ServiceHub, the unchanged census still returns null with no FileCat matches:
managed kernel/zombie entries lack executable identities. Complete native records now exclude
only those nonexecuting tasks; failed/short/inconsistent records and unavailable executable
identities still refuse. Host affected/full App, four clean f623297 CI jobs and physical Mac
44/7 Windows-only skips plus five native controls pass; all 19 Mac census cases pass. Four
server digests/six full TRX inventories, 843 exports/1,516 payloads/1,517 ZIP members and owned
cleanup verify. Actual admission succeeds; I138 is the later driver failure. Broader I106 remains open (E-I137).

The owner executes v2, v5 and v6 instrument controls. V2's 18 formatted positional offsets
fail; no correction is guessed. V5 captures future parent/child/name/FD/burst controls:
432 selected known operations, 24 complete files, 72 pins and 17 owned absences verify.
Parent-only names omit the differently named child; shortened names omit the full-name parent.
V6 raw capture verifies 233 known calls, five processes/seven native TIDs, all eighteen full
64-bit sparse offsets, six files/six ranges, 46 pins and nine owned absences. Three known
mmap/msync pairs lack backing FD/offset; MAP_UNIX03 is not a packed FD. Loss markers are
absent in that finite capture, not a whole-source/zero-loss qualification (E-V09-M2).

Actual v8 and v9 full-session attempts both fail conservative census admission with zero
source opens. V9 preserves the normal SSH account/groups; its four /usr/bin/sudo read
attempts fail EACCES. Independent ordinary-user control verifies mode 04511/read errno 13;
the same current production census is false after the sudo launchers exit. This is a
concrete blocker, not proof of the sole historical cause. All 77 v9 pins/15 owned absences,
source bytes and detachment verify. V8's 68 pins/twelve owned absences remain retained.
No production guard is weakened and neither failure supplies recovery/trace qualification.
V10 elevation preflight fails before App/device/trace; v11 records but its root-only marker
prevents the worker. Those failures and cleanup verify. V12's atomically benny-owned marker
lets the full ordinary-user session pass 1/1, exact 60-byte recovery, 70-second timed save,
unchanged source/detachment/temp and 104 pins/18 tracked plus fifteen derived child absences.
The supervisor's identity refusal occurs before App start; its exact cause remains unknown.
The independent worker/recorder continue. Owner-scoped result-access seal permits retrieval;
original supervisor failure and unknown recorder command exit remain retained.

Raw 2,669,238 events/115.488 seconds, 430 known calls/twelve control files and 33 App native
thread births match. Two native read-only source lifetimes have eight reads/264,192 bytes,
no observed writes/truncations/aliases/forks while open. Fifteen diskutil children have
syscall observations and are absent. All 79 nonanonymous shared App mappings initially
request PROT_READ but omit backing FD; full source-write qualification stays open.
Root syscall inventory returns header-only exit zero/SIP failure; no control/capture runs.
Process-level v4 inventory succeeds but numeric formatting fails before mappings. V5
root recorder/ordinary worker exit zero, all three known FD/offset/address mapping controls,
whole 32-KiB bytes, 32 native pins/three absences and SIP enabled verify (E-V09-M4).
Historical v12 mappings remain unresolved. New v13 combined capture passes actual recovery
1/1/whole bytes/source/cleanup, with 1,197 payload/117 retained pins and every recorded
worker/recorder/decoder exit zero. Raw 3,171,197 events/136.463 seconds, 430 controls/twelve
files and 33 App native TIDs match. All 77 shared mapping FDs/offsets resolve: 75 observed
filesystem paths/two shared-memory objects. Two read-only source lifetimes have eight
reads/264,192 bytes and no observed writes/aliases/forks/mappings. 43 unmatched private
maps precede the source opens and stay retained. Broader helper/authopen/topology/native/
candidate qualification remains (E-V09-M5). Twenty-one tracked owned, fifteen derived
children and one image-attachment daemon are absent. Clean e7a1e7e CI is verified above.
Mac native component authorization refusal now passes with owner cancellation (E-V09-M6).
The first owner-approved attempt remains a failed refusal expectation, not a production
defect or an approved read-range pass. Fresh v2 returns not-approved/SourceOpened=false,
without timeout. Raw source-open EACCES pairs/helper cancellation, clean worker/recorder/
decoder exits, 42 retained/197 input pins, unchanged source/detachment and seven absences
verify. The separate fresh approval case now passes seven independent golden-image ranges,
actual read-only F_GETFL/closed EBADF, six bounded native preads, clean exits/source/cleanup
and all pins/absences. Device replacement/removal, drawn workflow/helper/full source/
candidate qualification remains open. The equal-size native device replacement rehearsal and
four metadata controls pass with preserved preparation pins/source/cleanup. Actual held
authorization exposes I140; working correction/native cases verify above, while committed
authorization revalidation remains (E-V09-M7/E-I140). Docs-only ce28282
CI 37346405163 passes all four required jobs/four server digests/six full inventories/24
affected cases independently verified; ARM64 per-case/physical limits remain.
Both VMs stay running and G: stays untouched/HOLD. Progress: **119/141 issue rows remediated**,
one separately Closed; **24/26 checklist steps partly or fully open**. No candidate/human GO;
overall NO-GO.

I136 is remediated and verified preliminarily at clean a1c265f. Independent ISO/Joliet/UDF 1.02
and complete numbered-RAR controls preserve all 54 search/72 content results; seven missing-part
warnings now pass while affected content safely refuses. Ten new regressions/affected host checks,
four clean CI jobs and all 81 SDK-free cases per Windows/Ubuntu VM pass. Four server artifact
digests/six complete TRX inventories, all 77 affected Core cases and four Find cases per available
lane verify; native source/fixture/payload/output/case/process/temp pins independently verify.
Other format variants, native desktop/AT/reference hardware and candidate qualification remain
(E-I136). Progress: **114/136 issue rows remediated**, one separately Closed; **24/26 release steps
partly or fully open**. The later Mac resumption is recorded above. No candidate/human GO;
overall NO-GO.

I134/I135 are remediated and verified preliminarily at clean 3caf480. Empty 7z no-stream metadata,
encrypted data/header refusal and canonical test identities pass the identical 78 search/57 content
controls, four new regressions and affected host checks. All four clean CI jobs pass; four server
digests/six full TRX inventories verify. All 67 affected Core cases pass in Windows; all four Find
cases pass per Windows/Ubuntu/macOS inventory. Both SDK-free VMs pass all 71 cases with zero skips;
all source/fixture/payload/output/case pins and owned process/temp cleanup verify. Original failures
remain retained. Shared-profile search throughput/cancellation also passes; native/AT/reference-
hardware/candidate obligations remain (E-I134/E-I135). At that checkpoint Mac was deferred after limited
discovery found no address. No candidate/human GO exists; overall NO-GO.

I133 is remediated and verified preliminarily at clean 578a0ed: twelve independent baseline failures/
six controls, eighteen corrected production observations, twenty Core/four Find cases and full host
coverage pass. All four clean CI jobs pass; four server digests/six full TRX inventories verify.
All twenty new Core cases pass in Windows' inventory; all four affected Find cases pass per Windows/
Ubuntu/macOS inventory. SDK-free Windows and Ubuntu each pass all 24 cases without skips. Every
payload/input/output/source pin and corrected owned process/temp cleanup verifies. Original test/
setup/cleanup failures remain retained. Additional archive formats are under independent validation;
native desktop/AT/candidate obligations remain (E-I133). No candidate/human GO exists.

I132 corrects the quick-search Escape fixture's early focus checkpoint; production source is unchanged.
The original 545d627 macOS CI failure is retained with its full artifact/case inventory. An independent
held-continuation probe demonstrates one invalid early-observer failure and three passing controls;
it does not identify the historical CI event timing. All nine affected/full App 322 with 21 declared
skips pass after a short-temp Git fixture precondition correction. Clean 5a11100 passes all four CI
jobs; four server artifact digests/six full TRX inventories verify. All nine quick-search cases pass
without skips per Windows/Ubuntu/macOS App inventory; ARM64 App/package checks pass with log totals.
The following records retain their own exact baselines; native data remains qualified preliminarily.

Before the resumption above, Mac work was deferred at the owner's request. Authorized limited address discovery inspected
cached neighbors, one known-host DNS query and one Bonjour packet with a four-second response window.
No Mac address was found and no command ran there during that discovery. Earlier connectivity observations and discovery
records remain private; no subnet sweep or new Mac result is claimed.

Clean fdb17b4 completes the preliminary I129-I131 validation slice. The test-only I131 correction
observes count progress before refresh replaces a partial row, retaining all retired-result oracles.
Controlled failures/controls and unchanged-production probe remain retained. Host affected 74/full App
320 pass with 21 declared skips. All four required CI jobs pass; three server digests/six full TRX
inventories verify. Windows has 74 affected passes; each Unix App lane has 58 passes/sixteen declared
Windows-only skips. All eighteen retired-count and twelve caption cases pass per platform.

The SDK-free Windows guest passes all 74 cases with zero skips. All 381 payloads/382 ZIP members,
twenty-seven canonical source files, exact case inventories and owned process/temp cleanup verify.
Earlier f341dfd 41/43 caption and 3a408eb 70/72 fixture runs remain failed and retained. Controlled
reproductions do not identify their exact historical event triggers (E-I129, E-I130, E-I131).

Native sparse/hard-linked/mixed counts now pass six cases without skips on each of Windows 26300,
Ubuntu 26.04.1/ext4 and the owner's macOS 27.0.1/arm64 Mac (E-V12-N1). Exact logical totals, 32 marks,
focus, completed/quick-view state and refresh reconciliation pass against independently enumerated
native files. All input/output pins, 1,248 before/after snapshot rows, twelve sparse files/twelve link
groups and post-run process/temp cleanup verify. Windows/Linux retain the original seven production
DLLs; the Mac uses an actual RID producer with 826 source exports independently matched to Git.
The disconnected Mac run was retrieved after owner confirmation; failed harness attempts stay retained.

Both VMs stay running. Computer Use's recorded startup failure still blocks native input; the tool
shell is unelevated. G: stays untouched and its historical source-change gate remains held. Physical Mac work is
resumed up to the local administrator/admission gate above. Mac validation added only owned self-contained files; no global dependency
installation or system-setting change occurred. Needed Mac installations remain authorized.
No candidate or human GO exists; overall NO-GO.

- **Readiness: NO-GO.** No candidate, release tag, signed artifact, final qualification or human GO exists.
  Work remains in preliminary validation and remediation; the historical source baseline is retained below.
- **Resumption input:** clean `main` at `08c2e2dee4e04c936a34cd867770d05d758af686`, with prior work through I95.
  The seven existing planning documents were preserved and pushed unchanged as `3316f15` with owner authorization.
  Current validation changes and exact build/harness identities are in E-ENV-06/07, E-I96/98, E-V03-CLONE-1 and E-V19-P2.
- **CI:** all four lanes passed at `08c2e2d` and `a5a3c0c`; the intervening documentation commit's ARM64 lane failed
  on a transient native Recycle Bin query (I96). Its bounded test remedy `5503262` and UNC fix `ca1afe0` pass all four
  lanes. Manual run 36994087185 at d14199b passes all four lanes and Linux/macOS package jobs; development packages
  are under native validation (E-V19-P2). Later CI through fa3a02a passes all four lanes. Manual run 37036698071 at
  exact fa3a02a also passes Linux/macOS packaging; dev.539 bytes/hash provenance retained for both clean baselines.
  I105 at 1cd803c also passes all four lanes and manual development packaging (37055272672); dev.545 retained,
  strict Unix inventory independently verifies 35 pass/four skips per lane. I106 ecd61f3 and successor ea4a2ac
  pass all four lanes; ea4a2ac manual 37068909015 also packages dev.549. Strict Unix 47 pass/four skips per lane.
  Checker 2e6dffe and observed-manifest successor 85bb17d both pass all four CI lanes. Direct Windows inventories
  independently verify all thirteen new checker cases; package jobs skip (E-V09-G2).
  Guard 1883eb4 and successor 090a2b6 also pass all four lanes. Later b5ce744/a50b3b8 documentation/title inputs
  fail only the Windows synthetic lease helper readiness; an older bb2d748 macOS run exposes a timed progress
  observer race. I108 synchronization correction 6cad380 passes all four lanes in run 37119313116, with original
  failures and successful Windows archive/affected-case inventory retained (E-I108).
  I109 cfcc9e6 and documentation successors 087917d/f2f0141 also pass all four lanes; exact metadata/logs retained.
  These results do not qualify release packages or replace
  the candidate's skip inventory.
- **VMs:** guest access works after owner clarification (E-ENV-06). Windows Insider 26300 was gracefully shut down
  after completed copy cases, as authorized. Fresh Ubuntu 24.04.5 and 26.04.1 full desktops each have a powered-off clean
  baseline snapshot and actual GNOME Wayland session (E-ENV-07); current guest is 26.04. Preliminary 24.04 packages/native
  suites pass with recorded skips. Unmodified dev.526 Debian install on 26.04 fails on
  ICU dependency choices (E-I04); producer corrected and rebuilt 26.04 install/desktop/lifecycle pass. 26.04 native suites
  pass with recorded skips after native setup correction and I99's socket-path remedy. Separate-session GUI launch
  reproduces I100; profile-local lock/actual endpoint remedy passes native process, GUI, FAT/exFAT and affected App
  checks. I101–I103 affected native/process checks and CI pass. Final 26.04 raw archive independently hash-verified,
  owned Samba/loop fixtures cleaned before restoration. Dev.539 package/desktop/lifecycle, long temporary-path and
  session-forwarding checks pass on both restored clean SDK-free baselines; both raw archives independently verified.
  Continued audit exposes I105: a portable probe misses per-user fallback owners and portable profile roots.
  Hash-bound working fix passes ordinary/independent native GUI and Windows affected checks (E-I105), then all CI
  lanes. Separate portable installation exposes I106; corrected process guard passes ordinary/--data native GUI
  and CI checks. Dev.549 all three Linux formats pass successor checks on SDK-free Ubuntu 26.04 (E-V19-P3).
  Wider audit reproduces a renamed apphost missed by the name-only census; executable-identity correction under
  validation, refined root native census and App checks pass; ordinary-account visibility/races remain open
  (E-I106). I107 native trace identifies unchanged theme rebuilding the open menu; palette guard,
  affected App/CI and owner host check pass. Owner confirms clean 1a9f1ba guest menus; I107 is preliminarily closed,
  while exact-candidate interaction remains pending. Owner restored the
  Windows snapshot and guest access works; keep both VMs running. Computer Use runtime remains unavailable:
  updated 26.930.41038 import and separate plain JavaScript startup both crash before input (E-I126).
  Full recovery write-location audit continues. GA Windows/reference hardware remain open.
- **Physical recovery:** authorized USB host preflight and FAT32/exFAT/NTFS component runs pass at clean 1df5dff,
  325 generated deleted files recovered exactly per filesystem. Byte-checker audit exposes false acceptance beside
  missing ranges; corrected checker passes thirteen controls and both affected CI runs. Clean 85bb17d includes
  observed-file hash capture; its native checker/guard inventory passes 27/27. The stronger attempt overlaps an older
  campaign and is invalidated. Interprocess correction and exclusive clean 090a2b6 successor pass controls and
  physical preflight 1/1 plus formats 3/3 without skips. Each format recovers 325 generated deleted files exactly;
  all complete claims and recorded input/output hashes independently verify (E-V09-G3).
  Installed-helper/source-write tracing and I106 production census availability remain open.
  Pinned Windows tracer positive-read/write and untouched-path controls now pass; full PML/CSV and independent
  file/event/configuration verification retained. This control does not access the USB (E-V09-G4).
- **Native copy case:** guarded identity-bound ReFS/Dev Drive and same-server SMB harness added (`a5a3c0c`, `2a58fdb`,
  `021a885`). Local 1 GiB copies pass the clone-space, SHA-256 and copy-on-write checks. The first SMB run stopped
  before copying because its UNC volume root lacked the trailing separator (I97, fixed `ca1afe0`). Corrected local
  and SMB cases each pass 1/1 without skips; all three copy paths pass bytes/copy-on-write and use 0 reported MiB.
  Successful VHDX/share cleanup independently checked. Candidate reruns remain mandatory.
- **Prior campaign status:** fuzz campaigns collected (work log item 109); Windows and Linux recovery write traces
  are recorded (E-V09-T1/T2). macOS/installed-helper/device and candidate cases remain open. The issue register,
  rather than the historical checkpoint below, controls current defect disposition; non-Closed issues still block GO.
- **Controls and people:** private reporting, release protections, signing, retained REP storage, support decisions
  and human validation/GO gates remain open. No release controls were changed and nothing was published.

## Progress snapshot (2026-10-05)

After verified preliminary committed/native/CI remediation through I143 in its recorded preliminary scope, the §14 checklist has the following conservative gate status.
Grouped steps are expanded individually: 11 and 13 are in progress, 12 requires human execution, and
15–26 are blocked by preceding gates. “Done” here refers to the recorded preliminary scope.

| Release checklist state | Steps | Count |
|---|---|---:|
| Complete in preliminary scope | 1, 3 | 2 |
| Partial or in progress | 4, 6, 8, 9, 10, 11, 13 | 7 |
| Blocked or not started | 2, 5, 7, 12, 14–26 | 17 |
| Partly or fully remaining | All except 1 and 3 | 24 of 26 |

The plan contains **24 validation campaigns (V01–V24)**. Each still needs applicable final qualification
or documented case-level reuse against the final release artifacts; no candidate exists. Many preliminary
cases already pass. The register explicitly marks **121 of 143 issue rows remediated** and one closed in
preliminary scope; this does not close their native/candidate obligations. Nine owner decisions and three
external prerequisites remain and are already represented in the checklist.

There is no frozen, defensible total of individual remaining test cases yet: platform/support/package
decisions, the case catalog and candidate impact determine that denominator. New defects can also add
regressions. Do not turn counts of repeated passing executions into a release-completion percentage.
The earlier completed I129-I131 slice passes all 74 SDK-free guest cases and four clean CI jobs.
Its following native-data slice passes six cases on each of Windows, Ubuntu and macOS (E-V12-N1),
without closing a whole campaign or changing those gate counts.
Exact source/input/artifact/case inventories and owned cleanup verify. Both earlier failed guest runs
remain retained; controlled reproductions do not establish their exact historical event triggers.

The checklist remains 24 partly/fully open steps; its I128 derivation is retained. Latest evidence is
in E-I140 (clean committed Mac tests/two native replacement passes), E-V09-M8 (fresh
unchanged approval passed), E-V09-M9/M10 (native refusal/removal) and E-I142
(working failure-reporting remedy), alongside E-I141 (host fixture correction/four passing CI lanes) and E-V09-M5 (actual Mac combined
raw/mapping session, all shared mapping FDs resolved and retained qualification limits).
Earlier records keep their own provenance and limits.
Overall **NO-GO** remains.

## Historical checkpoint (2026-10-01; superseded by the current state above)

- **Readiness: NO-GO.** Release readiness is not established. No release candidate, tag, signed artifact or qualified
  package exists. Phase: A–F (baseline, reconciliation and preliminary validation with remediation).
- **Candidate identity:** none.
- **Source:** `main` at `1ec9d13` (plan baseline `4f6b062` plus the campaign's commits listed in the evidence index).
- **Defects found and fixed so far:** I19 (High, data loss), I15 (Critical where it happens, data loss), I20 (Medium,
  false forensic finding), I17's consent display (potential High, privileged boundary), I21 (Medium, Registry views
  without administrator rights), I22 (Medium, replacing an open file on Windows), I23 (Low, discovery naming), I28
  (Medium, a damaged NTFS field made recovery give up on the whole volume). All are remediated and verified by
  targeted and affected regressions; closure awaits re-audit and final-candidate evidence. Also fixed since: I24
  (seconds in the Modified column), I26 (progress and time left, confirmed Medium), I28's second finding, I29 (a CI-red
  race in shell previews).
- **Done since:** I30 (how running operations show, `67f70f9`; the taskbar still to be seen on a real desktop), I32 (a
  folder's counted size vanishing at a refresh, `6e9ee75`), I33–I41 and I43–I51 (remote transfers against real
  servers of two implementations, recovery allocation, state-folder permissions, a second FileCat seeing a running job,
  moves deleting a source whose copy was gone or deleting what was never copied, Synchronize acting on targets changed
  since the comparison, a link's read-only set through it, FAT32 recovery guessing where an entry was whole: I52, the
  hex editor's patch and Save As: I53, I54, a .reg backup restorable into the wrong Registry view: I55)
  and I25 (Markdown drawn as a page); I56, I57 (V23 B05), I58 (a damaged TAR header made .NET's TAR reader take up
  to 2 GiB, found by the archive damage campaign, `325aa63`) I59 (a Registry key's rename could be redirected
  through a link put in its place, V23 B07, `b02a01f`) I60 (the AppImage's runtime came unchecked from a moving
  release, V23 B09, `84b847a`), I61 (`--workspace` and `--list` ignored, `dcd81a1`), I62 (two names for one profile
  ran as two instances, `2cd313f`; both V23 B12) I63 (the update check opened whatever page its answer named, V23
  B13, `9bedead`) I64 (a folder's name could turn the shown location around, V23 B14, `e6e9ad0`), I65 (a
  damaged PE made the inspector throw, found by its damage campaign, `8cb0737`) and I66 (a Linux-deleted FAT file was
  called empty and recoverable, `1477de3`). **Open, measured:** I42 (per-file round trips of remote copies; owner decision).
  The queued Low issues are done: I31 (viewer windows only partly themed, `99a6ae4`) and I27 (Linux icons under
  Adwaita 41, `4a4349f`).
  **I09** (recovery scanned a disk FileCat itself writes to; destinations behind loop devices, disk images, VHDs and
  shares served by the same computer were taken for other disks; Potential Critical) remediated preliminarily
  (`27256f6`, `7418c04`, `0a52b7b`), live-checked on Ubuntu, macOS and the Windows VM; V09's write trace pending.
  **Running:** a fuzz campaign of the recovery scanner over millions of rounds on both VMs, the host and the Mac (E-I28-C1).

## Execution baseline

| Property | Observed (2026-09-30) |
|---|---|
| Plan baseline | `4f6b062fa8548fc8fd417a50262a72c0b804461f`, equal to `origin/main` when execution began; no delta to review |
| Working tree | Clean apart from the seven untracked planning documents in `docs/design` (not committed by this campaign) |
| Submodules, tags, releases, issues, PRs | None |
| CI at the baseline | Run 36722039034 green; package jobs skipped (E-A01) |
| Private vulnerability reporting | Disabled (I01) |
| Execution host | Physical Windows 11 Pro Insider 26220, elevated shell (E-ENV-00) |

## Checklist progress (plan §14)

| Step | State | Notes |
|---|---|---|
| 1 Refresh baseline | **Done** | Historical E-ENV-04; resumption at the progressed campaign source, CI and guest access refreshed in E-ENV-06 |
| 2 Owners, resources, provider/licence preflight | **Open (people)** | DEC-01, DEC-07, EXT-01, EXT-02; resource status in the blockers file |
| 3 Collect CI/validation evidence and skip inventory | **Done (preliminary)** | E-A01 (TRX lanes), E-A02 (every lane from the log, reasons from source; 37 tests run on no lane, all gated; the ARM64 lane's missing Remote tests added, `98bc539`); early-return audit (E-S01, `be6ca25`). To repeat on the candidate's run |
| 4 Reconcile manifest and registers against source | **Partial** | E-R04: every code name the plan's rows cite exists (137 in 421 rows; 8 rows explained), every capability has a route; whether each claim holds is left to the V cases |
| 5 Contract questions (I05, I06, PSD, Mac, FDD, I14) | **Open (owner)** | DEC-02…DEC-05, EXT-02; I06's page caches meet the planned shared budget (`61b028f`), so DEC-06 is closed |
| 6 V23 source review, test-guard audit, case catalog | **Partial** | DPI P01–P06, P08–P12, P14–P16 reviewed, P07 in part (I15, I19, I40, I44, I48–I51, I53–I55; E-DPI); B04 consent display audited (I17); B05 (I56, I57), B06, B07 (I59), B08 (I09), B09 (I60), B10 (I16), B11, B12 (I61, I62), B13 (I63) and B14 (I64) reviewed; B01 (I68) and B03 source passes; B02 by the damage campaigns (I58, I65); P14 corrected to I09; P07's loader audit (V06) and P13 remain |
| 7 Reporting, signing, dependency approach, preview preparation | Not started | I01/I02/I03/I14/I18 |
| 8 Fixtures and harnesses | Partial | VMware VMs lent (E-ENV-02/05/06); the owner's M1 Mac (E-ENV-05); two SFTP/FTPS implementations and Samba on the Ubuntu VM (E-V08-L2; current server availability not revalidated); consent UI Automation harness (E-I17); new guarded ReFS/SMB clone harness (E-V03-CLONE-1); Windows Sandbox unusable (E-ENV-01) |
| 9 S10 suites with native setup | **Partial** | E-L01 (Windows lane locally); E-X01 (unelevated Windows 11 VM, Ubuntu 22.04 VM, M1 Mac; CI for every commit) — preliminary |
| 10 High-risk preliminary cases and remediation | **In progress** | V03-PARTIAL (I19), V19-UNINSTALL (I15), V06-CONSENT display (I17) done preliminarily; I20, I21, I22, I23 from test runs; preliminary V19 package checks on Ubuntu 22.04 and macOS (E-V19-P1) |
| 11–13 V01/V12/V13/V16, human V17/V18, remediation loop | **Started** (V13's content search and file comparison) / blocked | V13: content search against an independent corpus (E-V13-S1, I74); file comparison against a generated corpus (E-V13-C1, I81, I82); Find's criteria against a generated tree (E-V13-F1, I83); V01's operation scope (E-V01-S1); V12's counted sizes and churn (E-V12-C1, I85); V16's harness inventory (E-V16-H1); V01's and V12's other parts and V16's acceptance runs not started. Human and reference-hardware work blocked (PPL-01…03, ENV-08) |
| 14 Pipeline, docs, release controls, preview | Not started | DEC-07, DEC-09 |
| 15–26 Freezes, candidate, FQ, REP, GO, publication | Not reachable | Depend on everything above |

## Work log

1. Read the launcher, the operational plan, its disposition record, the planning specification and the product plan in
   full. Refreshed the baseline; `4f6b062` unchanged, no delta review needed.
2. Downloaded the A01 TRX artifacts and inventoried outcomes and skip reasons (E-A01). Ran the Windows S10 lane locally
   on the physical host: 0 failures (E-L01).
3. Static audit of DPI P03 found I19 (the plan had flagged the heuristic; the audit found two further mechanisms: fill
   records keyed by destination only, and no revalidation). Reproduced with new tests, fixed (`f87ad32`), regressed.
4. The CI run of `f87ad32` failed on Windows ARM64 in an unrelated D-56 test. Instead of re-running, reproduced it
   locally (3 of 15), traced it to I20, fixed (`45efc09`), regressed; the formerly flaky test passed 15 of 15 and CI was
   green on all four lanes.
5. Static audit of DPI P15 confirmed I15 and removed the recursive `[UninstallDelete]` (`5b061cc`). Windows Sandbox was
   unusable (0x80070780, E-ENV-01); the owner lent snapshotted VMware VMs; on a Windows 11 VM the baseline installer's
   uninstall deleted a user's files and the fixed one kept them (E-I15-V1).
6. Recorded the CI installer-compiler provenance gap (I03/I18, E-ENV-03) and foreign-architecture WebView2 loaders in
   the x64 payload (E-I15-V1 side observation).
7. Started the owner's MacBookPro access (public-key SSH; key authorization pending) and a manual CI run
   (36759624490) to produce Linux and macOS packages for preliminary V19 checks on the lent Ubuntu VM and the Mac.
8. Early-return audit (step 3): 28 tests passed without testing when their platform or environment was missing; they
   now skip with their reasons (`be6ca25`, E-S01).
9. Ran the suites unelevated on the Windows 11 VM, on the Ubuntu 22.04 VM and on the owner's M1 Mac (E-X01). The
   unelevated run found I21 (Registry explicit views failed without administrator rights); reproduced under a restricted
   token on the host, fixed (`47c27b9`), rerun clean in the VM. The Mac run exposed a test that failed instead of
   skipping when the keychain cannot be unlocked over SSH (`64ed037`).
10. Audited B04's consent display: I17 (steps after the 60th hidden; HKU hives mislabeled), reproduced, fixed
    (`33b7de2`). A runtime check of the installed helper in the VM, driven by UI Automation, found two defects in that
    fix (the page text replaced the plan's title; pages too tall for the screen); fixed in `5c54181` and checked again:
    all 130 steps of a test plan shown, Cancel ran nothing (E-I17).
11. Preliminary package checks (E-V19-P1): the Linux `.deb`, tarball and AppImage installed, ran and uninstalled cleanly
    on Ubuntu 22.04; the ad-hoc-signed macOS app is rejected by Gatekeeper and bundles universal libraries.
12. Set up SFTP, FTP/explicit and implicit FTPS, and SMB servers on the Ubuntu VM for V08 (E-ENV-05).
13. The Synchronize test failed intermittently (VM once, CI ARM64 once). Traced to I22: a closed comparison held its
    files until the UI thread ran, and Windows refuses to replace an open file even when it is shared for deletion;
    FileCat then reported "Access denied … Controlled Folder Access". Reproduced deterministically, including a copy onto
    a file open in FileCat's own viewer. Fix in progress.
14. A discovery test failed once on the Ubuntu VM under host contention: I23 (a device's name lookup was canceled with
    the search window). Fix in progress.
15. The owner reported I24 (no seconds in the Modified column); queued.
16. Fixed I22 (`63d5fc4`: POSIX-semantics replace fallback, "in use" instead of "access denied", comparisons release
    files at once) and I23 (`d40e510`: name lookups outlive the search window). Both reproduced deterministically first;
    host, unelevated VM, Ubuntu VM and CI regressions green.
17. Ran the tests CI can only run in synthetic sessions inside the Ubuntu VM's real GNOME session (E-X01 U3): the page
    engine works with WebKitGTK 4.0 (the `.deb`'s alternative); Samba browsing through GVFS works; under the session's
    Adwaita 41 theme FileCat finds no file-type icons (I27, queued).
18. A 100,000-round fuzz run on the Mac found I28 (NTFS: a negative `$Bitmap` size made recovery give up on the whole
    volume). Reproduced (round 8842), located, fixed with its neighbors (`98fb594`), and the failing round saved as a
    permanent test. A fuzz campaign over millions of rounds of the fixed build is running on four machines.
19. The owner reported I25 (Markdown shown as plain text) and I26 (100% shown while still working; unconfirmed);
    queued.
20. Fixed I24 (`2197074`): seconds in the Modified column by default, checked in a screenshot.
21. CI went red on ARM64 in a shell-preview test (I29): a request race, reproduced deterministically, fixed (`7175a41`),
    CI green again.
22. The fuzz campaign found a second NTFS defect (round 56958: the scan threw on a damaged root record); fixed
    (`bb977d0`); NTFS rounds 0–99,999 pass; the VMs' NTFS runs restarted on the fixed build.
23. I26 confirmed (a verified copy showed 100% for 63% of its time) and fixed (`d40fda0`) with a new progress model and
    an honest, steady time-left estimator, as the owner asked (likely and pessimistic values, no jumps); pictured with a
    new screenshot mode.
24. The owner asked for the best possible way to show running operations (I30, middle priority): the strip now shows
    phase, percentage and the large file's own step; the details open on the running operation with its facts and a
    speed graph; Windows shows the progress on the taskbar button (`67f70f9`).
25. A Space/folder-size test began failing 9 in 10 on the busy host; diagnosed as a real race (a size tied to a
    modification time the listing had read mid-write) and fixed (`6e9ee75`, I32).
26. The owner reported I31 (viewer windows only partly themed); assessed and queued as polish.
27. Observation: one run of `ListingModelTests.Untouched_cursor_stays_on_the_first_row_while_entries_stream_in` timed
    out in a full-suite run while both VMs saturated the host; 30 isolated runs passed. Watched, not changed.
28. The taskbar progress (I30) was seen on the Windows VM's desktop: blue, amber, red and marquee states as intended.
29. CI's ARM64 lane timed out the new I29 test's first picture (a cold helper on a busy runner); the test now warms the
    helper and checks the sharing itself (`069732d`).
30. The owner decided the Markdown viewer is required for 1.0.0 (DEC-11); approach: a built-in renderer.
31. V08 started against real servers (E-V08-L1): trust, pinning, consent and byte-exact round trips over SFTP and both
    FTPS modes pass; a cancelled upload left its partial copy on the server (I33), fixed (`3ec60cc`).
32. V08 interruption (E-V08-L1): the server cut uploads off part way over SFTP and explicit FTPS; FileCat asked,
    reconnected on Retry, continued after checking the part on the server, and the files arrived byte for byte. The
    FTPS question read "see inner exception"; lost connections are now described by what happened (`5183cd3`).
33. V08 SMB (E-V08-S1) through Windows' client against Samba: round trip checked by the server's digests, times kept
    exactly, no Recycle Bin on a share, rename on the server, cancel, a dropped session (recovered by Retry once, by
    Windows' own reconnection once). A replace onto a file open in the viewer still said "Controlled Folder Access",
    and a share was called NTFS in the metadata question (I34), fixed (`6585024`).
34. Fuzz campaign status (E-I28-C1, in progress): Mac 1 M rounds each of fat12, exFAT and NTFS at `bb977d0` passed;
    Windows VM 1 M rounds of fat12 at `98fb594` passed; the other runs continue.
35. Read-back verification was ignored for uploads, downloads, extraction and copies to phones (I35); fixed
    (`53b0794`), with V08's altered-resume cases as tests.
36. V08 odd names: FTP refused, trimmed or redirected names (I36); fixed (`e50b9d4`); odd names now arrive exactly over
    SFTP and FTPS (checked against the bytes on the server's disk).
37. The Ubuntu VM ran out of memory: a fuzz round of a damaged FAT image sized a 1 GiB table, and a damaged NTFS
    `$Bitmap` was read at 512 MiB (I37); fixed (`02acee6`). The VM's runs restarted as user services, at the lowest
    priority and with a heap cap; guest operations to that VM now go over SSH (E-ENV-05).
38. CI on `a1be480` failed one recovery-review test on Windows: the test faked an interrupted copy whose creation time
    came from its source, which a stalled runner put outside the review's margin; reproduced and fixed in the test
    (`5b8b180`). The product logic is unchanged (an interrupted copy never gets its source's times).
39. I25 Markdown viewer implemented (`7abd0fe`): built-in renderer, drawn in the page engine, checked in WebView2; drawn
    by WebKitGTK and WKWebView in CI too (`ec5d475`).
40. I37 follow-ups: FAT and exFAT reviewed the way I28 reviewed NTFS (`b9c41eb`); the Ubuntu run's NTFS round 169883
    (1.6 GiB for a damaged compressed size, `0ec94f1`) and the Mac's exFAT round 5326394 (`9347070`) found, fixed and
    replayed; the runs resumed on the newest decoders.
41. V08 at a 100 ms round trip (E-I38-I39): correctness held, but an SFTP upload cut off by the server missed its time
    limit. FTP stats listed whole folders on vsftpd (I38, `f93f919`); SFTP uploads wrote one request at a time (I39,
    `2ba114e`); a cut-off upload on a slow link now starts again when that is quicker (`1dce2c2`).
42. P16 review: FileCat's state folders were readable by other local accounts on Linux and macOS (I40, `8b0dafd`).
43. A channel trace at 100 ms found SFTP held to SSH.NET's socket buffers after `ConnectAsync` (I41): fixed (`4c6b910`),
    32 MB at 100 ms now 11.6 MB/s down and 8.2 up instead of 1.2 and 1.6; the whole lab at 100 ms 19 of 19 in 13 min 8 s
    (33 min 52 s before I38/I39). The trace also measured what one small file costs (I42, open: owner decision).
44. A lab case renames, moves, sets aside, replaces and deletes links on the real server over SFTP and FTPS, checked
    with the server's own shell: links change themselves, never their targets (no defect; `4c6b910`).
45. CI red twice on unrelated tests (`2ba114e` ARM64 shell preview, `8b0dafd` x64 operations strip): the Shell helper
    now starts the Shell before it says it is ready, and the test waits as long for every state (`2a6f882`).
46. DPI P04 review: a second FileCat on the same profile showed a running job as interrupted on Linux and macOS (I44),
    reproduced on the Mac, fixed (`e399276`). The hex-save journal and edit sessions were checked for the same and are
    safe (exclusive journal while saving; commits refuse a changed target).
47. Uploads to FTP servers without MFMT silently kept the arrival time, and downloads took vsftpd's coarse listing time
    (I43): fixed (`e527a86`); the lab's tree case now checks every time both ways and fails before the change.
48. V08's second implementations (E-V08-L2): ProFTPD 1.3.7c with FTPS (MLSD) and `mod_sftp` beside the lab's servers
    (installing it removed vsftpd, which was put back beside it). 14 of 19 at first: over ProFTPD's SFTP, renaming or
    moving a link moved its target (I46, High; the server's behaviour, confirmed with OpenSSH's own client; fixed
    `3f1b554` by renaming links over SFTP only on OpenSSH), and an FTPS upload cut off never finished (I47: a refused
    APPE retried forever, a 60 s stall; fixed `111ebcd`). FTP listing times are now shown and compared as far as the
    server states them (I45, `111ebcd`). After: 19/19 on ProFTPD, 19/19 on OpenSSH/vsftpd, 7/7 on Samba.
49. CI red once more on the MFT-record test (x64, after ARM64 earlier): the record and its log read a moment before the
    time change reached the disk; the test now reads again as a user would (`b4d0f52`).
50. Fuzz campaign collected (E-I28-C1): the Windows VM's 1.1–2.1 M range passed for five images and NTFS; the Mac's
    5.1–6.1 M range passed (exFAT after its I37 fix); the idle Mac started 7.1–8.1 M of every image.
51. DPI review (E-DPI): P01 found I48 (a move deleted its source although its copy was gone; fixed `e72e3fc`), P09 found
    I49 (moves to and from servers could delete what was never copied; fixed `e72e3fc`, checked on three servers);
    P02, P08 and P14 held.
52. DPI P10: Synchronize removed or replaced target items edited while the plan was reviewed (I50, High; fixed
    `99145cf`). DPI P11: on Linux and macOS a link's read-only was set through it (I51, seen on the Mac; fixed `65a76f8`).
53. The lab VM's emulated network adapter hung under load from both clients at once ("Detected Tx Unit Hang"); the VM
    was reset, its offloads turned off; its fuzz runs restarted as one queue, four at a time (E-ENV-05, E-I28-C1).
54. V08 from the Windows VM as a client (E-V08-L2): 21/21 against OpenSSH and vsftpd, 21/21 against ProFTPD, 7/7 against
    Samba, after the lab's cut-off case was made independent of the drop command's speed (`d228632`).
55. V09 on disk images made by Windows' own drivers (E-V09-W1): NTFS 6/6 byte for byte; exFAT 4/4, the two files of a
    folder whose listing Windows reused rightly declared lost; FAT32 4/6, two files placed by a wrong guess though
    their entries were whole (I52, fixed `78a48ce`; 6/6 after). The raw bytes were read independently.
56. CI red from `99145cf` on: I50's folder check by the folder's own time failed both ways on NTFS (a folder's listed
    time lags its own; a second item within one clock tick leaves it unchanged). Reproduced on the host (10 and 5 of
    40 runs fail), redone by what the folder holds (`efc128f`; 0 of 40). Three test flakes fixed or made to explain
    themselves (`7791bda`): the move test's hook raced the copy job's counting thread on macOS.
57. DPI P05 (hex saves, recovery, Save As, patches): a patch that went over the limit of changed bytes was applied in
    part while the editor said it was not (I53, Medium); on Linux and macOS Save As could keep a copy mixing old and
    new bytes (I54). Both fixed `7a99f9d`, each test failing before (the second on the Mac).
58. The host's VM drive (V:) filled: the Windows VM's snapshot disk had grown to 87.7 GB, and the VM stopped on a
    disk-full question. It had no `filecat-before` snapshot after all; it was reverted to the owner's snapshot it was
    lent from (one unrelated file moved off V: for the revert and put back unchanged). Its unfinished fuzz ranges run
    again on the host; a watchdog pauses the Ubuntu VM's runs should V: run low again (E-ENV-05, E-I28-C1).
59. DPI P06 (Registry): a .reg backup from the 32-bit view could be imported into the default view (I55, fixed
    `cf92679`). DPI P07 in part: consent, scope and path handling held; the loader audit stays with V06.
60. I22/I34's last case: replacing an open file on FAT32 and exFAT, in the Windows VM on Windows-formatted virtual
    disks: "in use", kept, replaced on Retry once closed (E-I22-F1); the VM reverted to its lent state afterwards.
61. CI red once on ARM64 (`cf92679`): the discovery test judged "not followed to another address" by time; it now
    checks that nothing connects there, and fails when discovery is made to follow (`783c1b4`).
62. Step 3 completed (E-A02): every lane's skips from CI run 36821398706 with their reasons from source. 37 tests run
    on no lane, all gated on labs, devices, a phone or benchmarks (their evidence is this campaign's runs); the ARM64
    lane ran no Remote tests, now added (`98bc539`; 81 passed, 33 skipped, 0 failed on its first run).
63. I16's three named items: Git badges no longer touch a repository's linked paths before they are known to be on this
    computer, an icon named in a folder's desktop.ini goes through the helper's policy before anything is read, and every
    program FileCat starts is found by full path, never in the current directory (`2f35a6b`).
64. I12: the `$Secure` failure is held by `62bd88f`'s unit and live tests (Windows' own report as the oracle); the macOS
    page title by twelve lifecycles in the page engine smoke (`85d512d`; 12 of 12 on the Mac, none raising events after
    disposal).
65. Step 4, first pass (E-R04): the plan's code anchors all exist; each of C01–C29 has a menu or place route.
66. I31: the viewer, comparison, Find, hex editor, report and synchronize windows now have the theme's backdrop under
    their strips, their content on the card color (`99a6ae4`; pictures before and after).
67. I27: under Adwaita 41, whose type icons are symbolic only, files get those icons in the text color (`4a4349f`;
    the icon test passes on the Ubuntu VM where it skipped).
68. B06 reviewed (`34c4b9d`). The Windows VM ran every Windows suite of `ca91908` unelevated (W7, E-X01; its lent
    snapshot has no .NET 10, so a private runtime copy was used): 0 failures; then fuzz rounds 3.1–4.1 M of six
    images. The host's exFAT and NTFS re-runs of 6.1–7.1 M passed.
69. B08 reviewed against V09: **I09**. FileCat scanned a disk that holds its own folders after a warning (my P14 entry
    had accepted that; corrected); it now refuses, names the folders and gives `--data`, which keeps everything
    FileCat writes in one folder. Destinations and folders are placed through links, loop devices, disk images and
    shares served by the same computer; a device scan opens only from the drive's own command, at its chosen size
    (`27256f6`). Live checks on the Ubuntu VM found that an image written into still counted only as its backing
    file's disk (`7418c04`); macOS's `diskutil` answers are kept briefly so the checks take milliseconds
    (`0a52b7b`). All checks pass on Ubuntu, macOS and in the Windows VM (a VHDX on its system disk, `\\localhost\C$`).
70. B05 reviewed: **I56** (FTP data connections followed a routable address a PASV reply named; `ee476f0`, the remote
    lab still passes against OpenSSH, vsftpd and ProFTPD) and **I57** (discovery followed redirects from a device's
    metadata address; `a5c25d1`). Both reproduced by tests that failed before the fix.
71. B11 reviewed (two hardenings, `bc8e2af`, `5b786a9`). B02: a damage test for every archive format (`b0b2329`), 5,000
    rounds of each passed on the host, longer runs on the Ubuntu VM (in memory), the host and the Mac (RAM disk).
72. I09's write traces in the Windows VM (E-V09-T1): the safe topology left the dismounted source unchanged (hash) and
    only read; the system drive got no file of FileCat's (NTFS wrote its own pending metadata as FileCat read it,
    disclosed, wording corrected `deaf776`); FileCat's own files on the source: refused before any device access. The
    runs found a VHDX data disk refused as unknown (`1df5a21`) and the hold-off starting too late (`f241897`).
73. V: (the VMs' drive) fell to 24.7 GB as the Windows VM's change disk grew 59 GB this morning (Windows' own block
    rewrites; nothing large is visible in the guest); a stronger watchdog stops the VMs' fuzz below 15 GB and pauses the
    Windows VM below 8 GB. The host's re-run of the lost fuzz ranges and the Mac's 8.1–9.1 M all passed.
74. I09 on Linux (E-V09-T2): a loop device's image unchanged and only read; the whole system disk scanned with FileCat's
    files and `TMPDIR` in memory got not one disk write from FileCat's processes; FileCat's own files on the disk:
    refused. The trace found a write to `/tmp` at the moment a disk was chosen (a named-mutex lookup), fixed `d39c402`.
    The archive damage campaign's longer runs found TAR and RAR 4 rounds over the allocation budget that did not replay
    alone: the generated archives carried the run's time (fixed `c22c793`); the hunt goes on with replayable rounds.
75. The two archive rounds, replayed: **I58** — TAR round 97053's 512 MiB was .NET's `TarReader` renting the size a
    damaged PAX header gave before reading its data; a guard now checks such headers as they pass (`325aa63`; tests
    failed before, pass after; 20,000 TAR rounds then took at most 1 MB). RAR 4 round 248010's 517 MiB is the most PPMd
    model memory RAR 4 allows (256 MiB, as unrar takes too), rented by SharpCompress as a 512 MiB array: bounded by the
    format, accepted, budgeted. CI had been red on Linux and macOS since `d39c402`: a test asked the usual FileCat's
    pipe at once after its release, before the server stopped (`c5f7387`, checked on the Mac). A million rounds of every
    archive format now run on the fixed build (Mac, Ubuntu, host).
76. V: fell to 10.4 GB again: Windows Update in the Windows VM (a Visual Studio update among it) grew its change disk to
    74.7 GB and restarted the guest. The VM was reverted to its lent state (and once more by the owner); its updates and
    network are now off. Its unfinished fuzz ranges run again (E-ENV-05, E-I28-C1).
77. B07 reviewed (E-DPI): **I59** — a key's rename checked by name that the key was no link and renamed it by name;
    `RegRenameKey` follows a link and renames its target (an experiment on this Windows 11), so a link swapped in
    between could have had an elevated rename applied elsewhere. The key is now renamed through the handle it was
    checked by (`b02a01f`). The rest held: links refused on every open, values changed only while as seen, subtrees
    only while their digest matches, HKCU mapped to the requester's hive before elevation.
78. B09 reviewed (E-DPI): **I60** — appimagetool put whatever type2-runtime's "continuous" release held into each
    AppImage, unchecked; the runtime is now pinned (commit `8f39b89`, SHA-256) and passed explicitly, and the release
    action is used by commit (`84b847a`). Open for the owner (DEC-09) and for I03/I18: no branch or tag protection,
    immutable releases off, no NuGet lock files, a floating SDK and Inno Setup. CI's one red since `c5f7387` was a
    timing test on the ARM64 runner (`9fe6cea`).
79. B12 reviewed (E-DPI): every switch, worker mode and environment variable inventoried and classified. **I61** —
    `--workspace` and `--list` (plan §19.1) were read and forwarded, then ignored; now a named workspace opens first and
    a list file opens as a result set, its network paths left out uncontacted (`dcd81a1`). **I62** — two names for one
    profile's folders ran as two instances (`2cd313f`).
80. B13 reviewed (E-DPI): **I63** — the update check offered to open whatever page GitHub's answer named, through the
    system's association, and showed any tag text; now only a tag that reads as a version and a page among FileCat's
    releases, and at most 4 MiB read (`9bedead`). The log escapes control characters, so a server's text cannot pose as
    records. The archive damage campaign: a million rounds of each of the eleven formats passed on the fixed build (Mac,
    E-B02-A1); CI's red on macOS at `84b847a` was a test that assumed a copy could be watched (`a0a4bf5`).
81. B14 reviewed (E-DPI): **I64** — the file list escaped control and bidirectional characters in names, but a tab's
    title, the path line and the command line's path did not: a right-to-left override in a folder's name made the
    location read otherwise. All three escape now, as do the line and paragraph separators (`e6e9ad0`). With B14 every
    V23 boundary but B01–B03 (largely covered by the DPI rows, the fuzz campaigns, V07 and V10) has had its pass.
82. B02's inspectors got a damage campaign of their own (E-B02-I1): 200,000 rounds of fifteen formats passed; a PE round
    threw (**I65**, fixed `8cb0737`). B03's source pass found the containment as claimed (E-DPI). V09 through UDisks2
    (E-V09-T2, L5): a user who may not read the device got a read-only descriptor from UDisks2 and recovered a deleted
    file, nothing written to the source. On the way, **I66**: files Linux deleted from FAT32 showed as "0 bytes,
    recoverable: the file was empty", their entries cleared by Linux's driver; such entries now say what is known.
83. V09's refused approval on Linux (E-V09-T2, L6): with polkit saying no, nothing was opened or read. The scan first said
    only "Access is denied." because every refusal's reason was dropped by the listing (**I67**, fixed `134db5e`); it
    now says the system did not authorize reading the drive. The archive damage test's generated archives turned out to
    differ between runs and machines (the writing process's ID in PAX headers, native deflate's architecture-dependent
    bytes): made identical everywhere (`c4d81d7`), a failing round keeps its bytes (`cddce72`); one TAR+gzip round on the
    Mac over budget could not be rebuilt and stays open (E-B02-A1). Two recurring CI flakes fixed (`9b734da`).
84. V09 on macOS without administrator rights (E-V09-T3): a disk image the user attached, scanned directly, a deleted
    3 MiB file recovered identical to its original (hash), the image unchanged (hash). Every write of the processes
    (`fs_usage`) and authopen's paths need the owner's administrator rights.
85. B01 focused pass (E-DPI): **I68** (High) — on Linux and macOS a permanent delete reached into a file system mounted
    inside the deleted folder, emptying it; it now stops there and says so (`e5b4e3b`; a unit test and a live tmpfs check
    on the Ubuntu VM). Windows already treated a mounted volume's folder as a link.
86. V24 on the Git route (E-V24-G1): **I69** (High) — a downloaded repository's own configuration sent Git to a
    server while the folder was merely shown (`core.excludesFile` and the four others; 21.1 s per repository against an
    address that never answers, and an SMB session with the server on a packet capture). Fixed `aaee133`: such a
    repository is not read at all, decided from the text of the setting. The icon route of the same charter
    (`.url`, `.lnk`, `desktop.ini`) was then taken under the same capture and **held**: the three fixtures naming an
    icon on the share kept their type icon and contacted nothing, while the three naming one on this computer got it
    during the same run (E-V24-G1-I1). The gpg route **held** too (E-V24-G1-P1): a signature naming a key server, by a
    key the keyring does not have, in a home whose gpg.conf asks for missing keys to be fetched from it — FileCat
    answered "not in your keyring" in 0.1 s and contacted nothing, where a caller that does not pass
    `--no-auto-key-retrieve` sent 8 packets to that server. The launch routes (`SmbTools`, `ToolLauncher`, the Windows
    terminals) were read without a defect (E-V24-G1-S2); the tool route was then measured with a recording program
    (E-V24-G1-T2): fourteen names that mean something to a shell or an option parser each arrived once and unchanged.
87. V24 discovery (E-V24-D1): a damage campaign over the three parsers that read what anything on the network answers
    — WS-Discovery probe matches, a device's metadata, and mDNS answers. A million rounds each on the host: no
    exception, no round over 19 KB, 205 s for all three. The campaign's own CI failure led to **I70** (Medium): a
    Shell picture request that got no answer was remembered as the file having none, so after the helper died those
    files showed no picture for the rest of the session (fixed `3f647bd`, with a negative control). The same memo on
    the icon side kept every icon asked for during a recovery scan (pictures paused, I09) plain for the session
    (`855674a`); and a picture on screen is now asked once more when no helper answered (`c7a02e9`), which is what
    the ARM64 lane's failure needed.
88. V24, I16's process half (E-V24-D1-B1): a folder in which a repository's Git filter, an Internet shortcut and a
    customized folder all name the same program was listed and every icon asked for, under a trace of started
    processes. While the folder was shown, eight processes ran — two `git` runs (the ordinary repository's; the one
    naming a program is not read at all) with their console hosts, and one Shell helper — and **not** the program the
    three fixtures named.
89. V11 secrets (E-V11-S1): sentinel passwords through FileCat's remote stack as the app builds it on Windows, against
    the lab's OpenSSH and vsftpd — wrong three times, then right and saved, then a closed port — with diagnostic
    logging on and every failure logged with its whole exception chain. Saved passwords went to Credential Manager;
    none of the three secrets is in any file FileCat wrote, as UTF-8, UTF-16 or Base64. Found in passing: a
    password answered "save" was stored before the server accepted it, so a mistyped one was retried on every
    reconnect; it is now kept only once accepted (`f9adb51`).
90. V11 state files: **I71** (Medium) — the window layout was the one state file that did not honour plan §19.1. An
    older FileCat set a newer layout aside and then saved over it in its minute's autosave; two saves later no file
    held the newer layout, its backup included. Fixed `b70be07` (the layout is read-only then, as settings and history
    are; a reset does not overwrite it either).
91. Long paths on Windows, at the owner's request (E-V02-L1): every file operation FileCat offers — listing, copy,
    rename, attributes, move, new folder and file, checksum, alternate streams, hex editing, permanent delete — works
    on paths of 330 and 630 characters, on the host (long paths allowed) and on the VM with `LongPathsEnabled` 0, the
    Windows default. The Recycle Bin is the one refusal, Windows' own; FileCat says so and changes nothing.
92. V11 unwritable state (E-V11-S1-F1): a portable copy that cannot write beside itself already fell back to the
    profile and said why (now tested). A save failing during a session was only logged, so changes silently did not
    survive a restart; it is now told once per file per session (`0ade5a1`).
93. Cloud providers, at the owner's request (E-CLOUD-1): OneDrive's, Dropbox's and iCloud Drive's marks never showed
    (FileCat asked the Shell for overlays only inside Git repositories), and their folders were not among the places.
    FileCat now draws the states itself from the attributes a listing has — only in the cloud, on this computer, kept
    here always — for every Cloud Files provider, without running their Shell code or downloading anything, and lists
    the providers' folders with their icons. The owner's own folders showed an "excluded" state (both pin attributes)
    that a first version drew wrongly. Validated: looking at a cloud folder downloads nothing (42 online-only files on
    the owner's OneDrive, all still online-only after listing, icons, metadata, checksum checks, a content search and
    a size count); writing tests in a provider's folder wait for the owner to name one.
94. The application icon, at the owner's request (E-ICON-1): on the owner's dark taskbar at 100% (24 pixels) the new
    artwork's small frames all but disappeared; the 16- and 24-pixel frames are now drawn as pixel art in the
    artwork's own terms (`ecaa254`, with the owner's leave), 32 to 256 untouched. The About box needed nothing. Linux
    and macOS packages now take every size from the icon's frames instead of shrinking the 256-pixel PNG.
95. MTP on the owner's Android phone (E-V21-M1): seven device cases inside `FileCat-test` passed, and a thousand small
    files went up in 31 s and back in 8.3 s; the folder is gone afterwards. The disconnect and lock cases wait for the
    owner at the phone.
96. MTP on the owner's iPhone (E-V21-I1): **I72** (Low–Medium) — FileCat offered F7, renaming and copying onto the
    iPhone, which takes none of them: its storage says read-write, but its driver lists deleting as the only object
    command. FileCat now offers what the driver's commands and the storage's access allow, explains the rest, and the
    device jobs refuse before sending anything (`2e93339`). Checked on the iPhone without changing anything on it;
    reading a photo off it waits for the owner's leave.
97. Fuzz campaigns collected: the host's archive lanes (ZIP, TAR+gzip, gzip 5–6 M; TAR 4–5 M) and inspector lanes
    (PE, PNG, GIF, ELF 1.2–2.2 M), Ubuntu's archive lanes 3–4 M but RAR 4 and all sixteen inspector formats
    0.2–1.2 M, the recovery scanner's 3.1–4.1 M on the Windows VM, 9.1–10.1 M on the Mac and Ubuntu's GPT disk: all
    passed. RAR 4 round 3655801 on Ubuntu was still reading after 60 s and reads in milliseconds alone; not the VM
    stalling, not leftover pool memory, not leaked threads, and not the rounds before it: run again in one process with
    the same settings, all 655,802 up to it passed (E-B02-A1); and on the Mac the whole range in one process passed,
    with ZIP, TAR, TAR+gzip and gzip over 3–4 M, whose originals hash as the host's. The round is recorded as an
    unexplained one-off and repeated in every test run (`71bfd99`). The Windows VM, the Mac and Ubuntu now run ranges
    no machine had run.
98. V24, the file half of I16's gate (E-V24-D1-F1): the kernel's file events while FileCat shows the folder built to
    tempt it. Nothing on the network; the repository that names a program is only inspected by FileCat's own reader and
    never opened by Git; the program the fixtures name is read for its time and its icon, never started. One finding,
    **I73** (Low): the type-icon lookup made the Shell try to open `C:\file.url` from FileCat's own process; the
    placeholder now names no place anyone could fill (`f1b48de`), traced again with and without the change.
99. V13, content search (E-V13-S1): a differential corpus test — files of every encoding FileCat reads, words planted
    at chosen offsets, encodings and read boundaries, each query's answers compared with reading the whole file at
    once. **I74** (Low–Medium): regular expressions matched a read window's edges as the file's (`word$`, `^word`, a
    look-behind), an accent that began the next read was missed, and UTF-8 inside a UTF-16 file was not searched with
    Unicode on; fixed (`b0a2313`): 2/640 and 5, 4, 2/1,920 wrong answers before, none after. The test runs in every lane.
100. The application icon, the owner's second report (E-ICON-1, **I75**): on the lent VM's real taskbar a pinned item
    is the 32-pixel frame shrunk to 24, so `ecaa254`'s redrawn 24-pixel frame never showed; the artwork's dark edge melts
    on a dark taskbar. The frames the taskbar shrinks get a cyan edge, the small frames the artwork's proportions
    (`a9f48cf`); on the VM's dark taskbar it now reads as large as Salamander's.
101. Writing in a cloud folder, with the owner's leave for one test folder (E-CLOUD-1): FileCat's jobs in OneDrive —
    copy in, rename, move, free up space, copy out a file only in the cloud (it downloads), delete, recycle — all as
    on a plain disk. **I76** (Medium): FileCat could not delete the folders OneDrive keeps in sync (their read-only mark,
    which the Shell also sets on customized folders) and blamed Controlled Folder Access; fixed (`edd950a`). The test
    folder and its one recycled file were removed; nothing else touched.
102. The iPhone, reading (E-V21-I1): one photo read into memory with the owner's leave — a PNG, so sizes agree but
    nothing was converted; the phone lists its photos as JPEG (no HEIC), so one JPEG read would show whether converted
    photos come off whole. Asked.
103. The owner's request: the Recycle Bin among the places, beside Downloads, opening Windows' own window, since only it
    restores what it holds (`f9b0c13`): Windows' stock icon, empty or full by the fixed drives' bins; tests with a test
    platform's Shell (nothing opens on the desktop); the exact command run in the lent VM opened its Recycle Bin window.
    A view of FileCat's own (reading the bins' records, restoring) is the owner's decision.
104. CI (`f88300d`): the Registry jobs' tests waited ten seconds for a job and once ran out on a busy runner; they wait a
    minute now, the jobs themselves unchanged.
105. The owner's decision, (b) of three: FileCat's own read-only view of the Recycle Bin (`5a4161b`, E-BIN-1). The
    place beside Downloads opens it in the panel; Windows' own window is its second entry. Both record formats are read
    as untrusted input (a million damaged records: none crashed; the fuzz run found one crash before it shipped);
    deleted folders are entered, files viewed and copied out, nothing outside the bin reached (the test fails with the
    check taken out). Against the Shell's own listing, item by item: the owner's bin 40 of 40, later 38 of 38; the lent
    VM's (Czech) 6 of 6. Restoring and emptying stay with Windows' window.
106. **I77** (Low; `f95e4cd`, E-BIN-1): the view's first look at the owner's bin found a FileCat test's leftovers — 163
    records of its files and two items, one record per run of a test that recycles a file and undoes it on the computer
    it runs on. The test now removes its own items whatever happens, FileCat's undo removes the restored item's record,
    and the leftovers were removed from the owner's bin, only those. Checked afterwards on the lent VM: Windows' own
    Restore leaves that record too (three runs), as does emptying the bin with such records in it; the fix's comment and
    commit message had said otherwise, unchecked, and the records and comment are corrected.
107. V21, the cable pulled mid-transfer, with the owner at the phones (E-V21-U1): seven pulls, two on the iPhone copying
    photos off it (with the owner's leave for up to 50, deleted afterwards), five on the Motorola inside `FileCat-test`
    (one copying onto it, four off it). Nothing half-written was published or left on a phone, nothing hung, and Retry
    finished every copy whole. **I78** (Medium, `7ee8e92`): every pull during a copy off a phone was reported as the file
    "no longer exists": .NET raises the phone's "not found" as `FileNotFoundException`, which FileCat's device handlers,
    catching `COMException` only, let through; listings also passed a cut answer off as the folder. A trace of each step
    on the fourth pull found it; the fifth, after the fix, said "disconnected" and passed. **I79** (Low–Medium, same
    commit): after a reconnect the iPhone sends seven of the 50 photos with other bytes at the same size, which the check
    before resuming (the 64 KiB before the break) could not tell; the file's start is compared too now. The JPEG question
    of item 102 is answered: the iPhone sends its converted JPEGs at exactly their listed sizes. Locking a phone
    mid-transfer is not done. Suites: Core 719, Windows 159, App 206, Remote 116; 0 failed.
108. The owner's request (low priority): tooltips over icons styled by the selected theme (**I80**, `fab03b8`): tooltips
    take the theme's surface, solid, with contrast checked in every theme; icon buttons' tips laid out (title, key,
    description, how else used). App suite 209, 0 failed; the owner: "tested it on my own, works".
109. The fuzz campaigns collected: recovery (E-I28-C1) the Mac's 10.1–11.1 M of all seven images, the Windows VM's
    4.1–5.1 M of FAT12, FAT16, exFAT and both disks, and Ubuntu's MBR disk over 100,000–1,099,999 **passed**, so every
    image is now covered from 0 to 4.1 M and from 7.1 to 11.1 M, and between them as the campaign table lists (FAT32's
    4.1–5.1 M still running on the VM); archives (E-B02-A1) Ubuntu's 4–5 M of the seven formats it runs **passed**,
    nothing kept (RAR 4's 516 MB round is the PPMd model, recorded and accepted). Outputs copied off every machine and
    checked by hash; the fuzz archive's manifest grows from 64 to 80 files.
110. V13, file comparison (E-V13-C1): a generated corpus checked against references written in the test (20,000 text
    pairs, 2,000 aligned binary pairs, 20,000 positional runs past the list's limit). It found **I81** (Low–Medium,
    `db2e9b4`): one coincidental unique line misaligned a text comparison, an 11-line edit shown as 84 lines, presented
    as exact; regions up to 20,000 lines are now aligned exactly, larger ones labelled heuristic unless provably the
    best; and **I82** (Low): the summary's count disagreed with the list. Speed unchanged; Core 722, App 209.
111. V13, Find's criteria (E-V13-F1): a generated tree and random queries (masks, subfolders, hidden, sizes, times,
    attributes, ignored folders) against a reference written from the criteria's documentation: 15,400 queries, about
    293,000 results, all as meant; a mutation of the size bound is caught. Beside it **I83** (Low, `c67fa85`): a saved
    time range shown again in Find's dialog lost its end day's last minute. Core 723, App 213.
112. V13, the rest of the search and comparison: a runaway regular expression times out, says so, and the search goes on
    (`0b52e70`); directory comparison read for letter-case collisions and precision found **I84** (Medium, `bc65646`): of
    two names differing only in case one was dropped unseen, and a size or time a listing does not give counted as the
    same. One pairing rule and an undecided state for both comparisons; 3,000 random folder pairs against a reference.
    Core 728, App 213.
113. V01 begun (E-V01-S1, `49570df`): through the window, a copy waiting on a conflict keeps exactly its marked files and
    planned destination while files arrive, the target panel moves, the panels swap and a panel is added (direct
    manifests before and after); a mark the filter hides is said in F5's dialog and copied only while included. Passed
    as written; nothing found.
114. V12 begun (E-V12-C1): how a folder's size count is applied, read for "results never land on replacements", found
    **I85** (Low–Medium, `1ec9d13`): a folder deleted and made again, or replaced, while counted took the first one's
    size as counted. The count now compares the folder's file-system identity at its start and end. App 218. A folder
    churned with 12,000 changes at full speed ends as the disk is, marks and cursor kept (`4a156b5`); partial sizes are
    drawn with "…".
115. V16 begun (E-V16-H1): the harness inventory §9 asks for (what each benchmark measures, asserts or only prints)
    and preliminary runs on this machine, the historical regression profile, not the reference: comparison, search and
    archives within every asserted budget. Gaps before acceptance: ready-for-input and OS-input-to-present latency are
    not what the window's benchmark measures; no harness for the shared content cache. Huge hex got one (`6df923b`):
    a 4 TiB sparse file and 2 GiB of data through the viewer and the editor, first page at most 1 ms warm and seek p95
    at most 1.33 ms here (budgets 250 and 100 ms). Large copies got one (`5abf5b2`): against CopyFile2 with the job's
    own profile (a first run against a buffered CopyFile2 measured the write cache and is not a comparison), the
    median of five pairs was −0.3% to 19.6% over three runs on the 990 PRO; the job's own work outside the copy engine
    is 12–18 ms per 4 GiB job, and the spread is the disk's. ReFS cloning and SMB server-side copy are not covered:
    the check is written (`CloneCopyTests`, gated; a VM script making a ReFS volume and a share of it), but the lent
    Windows VM no longer starts from cold (ENV-05) and reverting it waits for the owner.
116. I06 (E-I06-P1, `61b028f`): every view's page cache was bounded by its own limit only, so the views open together
    were not: five viewers, three hex editors, two comparisons and the quick view held 148 MiB. Now one budget, 64 MiB
    by default (`ContentCacheMiB` in the settings file), shared by every reader: the least recently used page of all
    goes first, and each reader keeps its four most recent pages. Measured at 64 MiB for the same views; tests for the
    order across readers, the floor, disposal, collection and concurrent use, with three negative controls. This meets
    the plan's target, so DEC-06 (keep it or approve a change) is closed; the owner can still set another limit. I06
    stays open for other memory that grows with what is open (V12).
117. CI's red runs of the last day, each traced: the Shell-picture failure on ARM64 is I70; the recording-program test
    (`3a291e6`'s run) and the Registry jobs' wait (run 36904910785) were made robust in `a933ed6` and `f88300d`;
    the operations panel's "Clear finished" (run 36931084621, a records-only commit) was a real race, **I86** (Low,
    `506cc75`): clearing went by each row's state, the count by each job's. A test that holds the window's thread while
    a job ends fails on the old code.
118. I06, archive indexes (E-I06-A1, `6b37c41`, `319a38c`): both archive providers kept the last eight archives'
    indexes by count alone; one index of a million members holds 557 MiB (ZIP) or 291 MiB (TAR), measured. Each index
    now estimates its size (553 and 290 MiB for those), earlier archives are kept within 256 MiB per provider, the
    least recently used first, and the two used last stay whatever their size (two panels). A member being read keeps
    working when its index goes. Negative controls. I06 stays open for decoded pictures, icon caches and other
    materialized lists (V12).
119. V12, the watcher (E-V12-W1): forcing an overflow found **I87** (Medium, `3d2bb2e`): while a folder kept changing,
    the watcher's debounce put its reread off for as long as the changes went on (a file every 50 ms for six seconds:
    no reread until after the last; 100,000 changes in 30 s: none). Now every two seconds while changes come, and one
    after. Overflows of the system's buffer are counted and logged: unhindered, this machine's watcher kept up with
    100,000 changes; held up 2 ms a notification, 20,000 changes overflowed it four times, and a reread followed.
120. V12, counts and their tab (E-V12-C2): **I88** (High, `a9a9dcf`): a folder's count went on after its tab closed and
    posted to the disposed listing four times a second; each post threw on the window's thread, and the crash guard
    ends FileCat past five in three seconds (24 in five seconds measured, closing a tab 0.3 s into a 6-second count).
    Leaving the folder kept the tab "counting" in the next one. Now a count ends with the tab's stay in its folder;
    the same experiment raised none. The rest of the window's posted work, read for the same mistake: two paths loaded
    a closed tab's listing again (no exception); a disposed listing now ignores Load (`34042cc`). A comparison, a
    viewer, a hex editor, a search and the quick view closed while they work raise nothing (`f2b850b`; the same test
    sees I88's exceptions with the old count code). The same mistake in View → Analyze folder: **I91** (Medium,
    `800cd52`): an analysis outlived its folder, labelled and re-sorted the next one, or failed reading a closed tab's
    listing; now it ends with the tab's stay.
123. V12, large listings (E-V12-L1): a million synthetic entries list their first rows in 71 ms (names of 29
    characters) and 72 ms (240 characters), complete in 1.5–1.7 s; four such listings at once show rows at 621 ms.
    Long names cost spill space: 503.5 MiB of temporary disk for a million 240-character names. Regression profile.
    Type-to-find walks the names on the window's thread at each key: a miss over a million names holds the window
    43 ms to about 0.6 s (**I92**, Low, open). Many tabs (`1cf3af5`): forty tabs hold two watches (the active tab of
    each panel); a background tab catches up with its folder when it is active again.
121. CI run 36941532909 (red on a records-only commit): **I89** (Low–Medium, `2ad2cfa`): the change journal reader went
    on from the new oldest entry only once when the journal wrapped during a read; a busy runner wrapped it twice and
    the read failed. Now as often as needed, said in the view; tests with a stand-in journal fail under the old rule.
122. V13, result sets (E-V13-R1): refine and append read in the code (identity leaves size and time out; keep and
    remove matching search within the found items; append adds only new references), and **I90** found (Low–Medium,
    `54c33de`): a root typed in another letter case gave other items for the same files, so appending listed them
    twice. Searches now walk their roots as the disk spells them. Duplicates: **I93** (Medium–High, `bf395c6`): two
    names of one file (a hard link, a path through a junction) were grouped as copies, so "all but one" could mark the
    file itself for deletion; now names of one file count once, by file identity, and links are left out.
    Synchronize: **I94** (Medium–High, `65cee78`): folders inside each other (also through a junction) were offered,
    and Mirror toward the outer one removed the source; now not offered for them.
124. V15 begun (E-V15-G1): OpenPGP checked against keys and signatures an independent GnuPG made. **I95**
    (Medium–High, `cd37342`): with no trust line from gpg (trust-model always in gpg.conf), a good signature read as
    good for any key in the keyring; now as signed by a key gpg did not vouch for. No key server is contacted even when
    gpg.conf asks (a listener in its place saw nothing). minisign not covered (no independent tool here).
125. Execution resumed on 2026-10-02 (E-ENV-06): all four authority documents and the launcher-required planning
    prompt read in full; progressed campaign source verified; the seven planning documents retained unchanged and
    pushed (`3316f15`). Both VMware guests are running; owner-corrected Windows credentials work. Ubuntu remains
    22.04.5 and Windows Insider 26300. Reporting/release controls remain disabled/unprotected; no settings changed.
126. **I96:** documentation-only CI `3316f15` failed on ARM64's first Recycle Bin query (`0x800700B7`); the same
    test passed in CI at `a5a3c0c`. `5503262` retries only that response up to four times, 50 ms apart; persistent and
    other errors still fail, no new skip. Targeted test and full four-lane CI pass (E-I96); native cause unconfirmed.
127. ReFS/SMB preliminary copy validation resumed (E-V03-CLONE-1). The old fixed-Q:/fixed-share script was not run.
    New harness binds a unique VHDX to its disk/partition, rejects wrong guest/bundle controls, retains failed
    fixtures, and checks identity for format/cleanup. Windows PowerShell CIM bus-type handling and Storage cmdlet
    error propagation were corrected after safe setup failures. The local Dev Drive case passes all three copies,
    byte hashes and copy-on-write checks. **I97:** SMB's native query failed before copying because its UNC root
    lacked the required trailing separator; independently reproduced (123 without, success with it), fixed
    `ca1afe0`; corrected local/SMB runs each pass 1/1 with byte/copy-on-write checks and 0 reported MiB extra space
    for all three paths. Cleanup independently checked. No product copying defect demonstrated by the setup failures.
128. Owner authorizes updating/reinstalling the lent Ubuntu VM for the required 24.04/26.04 desktop matrix; its
    existing rollback snapshot is sufficient and its current contents need not be preserved. Fresh installs will
    be used sequentially, with OS snapshots for repeatable tests. Canonical checksum signatures have been verified
    against the documented CD-image signing fingerprint; ISO downloads/provisioning and Linux package checks remain
    in progress, not qualification evidence yet.
129. ENV-04 provisioning begun (E-ENV-07): Ubuntu NAT restores internet access; exact VMware UUID and sole 200 GiB
    SCSI disk identities recorded. Canonical desktop ISO signatures and hashes verified on host/guest. Guarded media
    built for 24.04/26.04; positive identity check and four negative controls pass. Installer selection corrected to
    use udev ID_SERIAL before any boot. Manual CI `36994087185` at `d14199b` passes all four lanes and Linux/macOS
    packaging; no tag/publication. Fresh installations and package/native validation remain in progress.
130. Fresh Ubuntu 24.04.5 installed and baseline snapshot retained before FileCat or test tools, GNOME Wayland/XWayland
    confirmed, no SDK present (E-ENV-07/E-V19-P2). Development .deb installs and displays the app; ordinary GUI Unicode
    copy passes byte/hash checks. Owner explicitly authorizes disabling idle lock/blanking and unlocking this disposable
    VM after auto-review requested specific security authorization. Completed Windows VM tests retained; guest shut down.
131. **I98:** actual tar desktop launch fails when its folder contains ampersand; unescaped sed replacement corrupts
    Exec/Icon. Independent native GLib/argv/icon harness passes 4/12 before and 12/12 after the separate escaped helper,
    including quotes/percent/newlines/Unicode (E-I98). Linux CI coverage added; rebuilt packages/26.04/candidate pending.
132. I98 fix pushed as ecf5349; push CI 36999606344 and manual CI 36999624175 pass all four test lanes. Manual Linux/macOS
    package jobs pass; dev.526 hashes retained (E-V19-P2). Rebuilt tar opens normally from ampersand/percent/Unicode path.
    Debian remove/purge/reinstall/update preserves state and unrelated install files; normal AppImage FUSE GUI works.
    Native FAT32/exFAT record test passes 1/1 without skips. Remaining native suites and 26.04 matrix continue.
133. Fresh 24.04 native suites complete (E-X02): Core 702/741, Remote 94/116 and App 203/230 pass, all remaining cases
    explicitly skipped. Required native keyring/WebKit/Samba/GVfs/FAT cases pass; readelf and Windows-formatted recovery
    corpus checks replace prerequisite skips. Initial keyring setup caused an aborted run; corrected isolated native
    service yields a full Core pass, with all failed inputs retained. Tar/AppImage corrupt-state and restart checks pass.
    Owned loops detached, test Samba stopped and raw evidence transferred/hash-verified on host before 26.04 overwrite.
134. I03's Windows inventory filename collision remediated while 26.04 installs (E-I03-RID): per-RID filenames retain
    both native package-list JSON outputs. Full artifact/native/runtime/helper SBOM and source/license gates remain open.
135. Fresh Ubuntu 26.04.1 full desktop and actual Wayland session verified; private installer evidence retained and
    clean powered-off baseline snapshotted (E-ENV-07). Existing dev.526 Debian install exits 100 because only ICU78
    is available. I04 reproduced and producer adds libicu78; rebuilt 26.04/24.04 verification pending (E-I04).
136. I04 remedy cc97a8d passes all CI lanes and manual Linux/macOS packaging (37015434145). Exact dev.531 Debian
    dependency/CLI/desktop/lifecycle cases pass on 26.04. Tar desktop GUI/native Unicode copy and normal FUSE AppImage
    pass; all rebuilt formats preserve corrupt settings and restart state, and packaged helper passes 12/12 native
    path cases (E-V19-P2). Private SDK/native prerequisites added only after clean-package cases.
137. Fresh 26.04 native Core 703/741 and Remote 94/116 pass with explicit skips; actual Secret Store/Samba/GVfs
    branches required. Root FAT32/exFAT 1/1 and Windows-formatted recovery corpus 3/3 pass. Initial private-bus GVfs
    setup failure retained; isolated runtime/keyring rerun passes (E-X02).
138. Full 26.04 App run reproduces I99 under a valid 73-byte TMPDIR: two socket address exceptions. Hash-bound remedy
    passes boundary/long/Unicode fresh-process checks and full App rerun 204/231 with 27 skips. Actual same-session
    GUI forwards/persists tabs. A separate-session launch exposes I100's Local mutex scope and socket replacement;
    retained independently, not counted as a pass. CI/rebuilt package validation and I100 remediation continue.
139. I99 pushed d38f915; CI 37024802245 passes all four lanes including the native Unix boundary harness. I100's
    production API harness reproduces distinct-session/cross-TMPDIR failures (1/5). Profile-local native lock and
    published actual endpoint remedy passes 6/6 cases, including two case-sensitive data folders, and 14 boundary
    tests with one expected skip. Affected native App 205/232 passes with 27 skips. Actual GUI across sessions/TMPDIRs,
    FAT32/exFAT state locks and native syscall oracle pass (E-I100). Setup/assertion failures retained separately.

140. I100 pushed 4746592; all four CI lanes pass (37030149306), including native Unix boundary/process harnesses on
    Linux and macOS. The continuing write-location audit reproduces I101: runtime TMPDIR omitted from recovery.
    Own runtime folder and published running-instance socket/runtime locations now guarded; invalid metadata refuses.
    Native before 1/1 fails, fixed guards 5 pass/1 expected skip, boundary 17 pass/1 expected skip, process 6/6 and full
    App 206/233 with 27 explicit skips pass. Both required WebKit cases pass. Windows controls 3 pass/3 Unix skips.
    Exact inputs, failed setup/build attempts and native results retained (E-I101). CI/rebuilt packages remain pending.

141. I101 pushed 369f55f and all four CI lanes pass (37032428169). Actual --new-instance Ubuntu GUI is missed by
    the recovery probe, and another-profile guard regression fails: I102. Independent lifetime locks/metadata stay
    in guarded state folders; all usual profiles checked. Fixed actual GUI is detected, then closed/cleaned correctly;
    8/8 process cases include last-owner/crash and normal-owner forwarding. Boundary 23 pass/1 skip, full App 208/235
    with 27 explicit skips, native paths 13 pass/1 skip. Windows guards 5/8, paths 13/14 with platform skips.
    Intermediate native/Windows failures retained and remedied (E-I102). CI/rebuilt packages/write tracing pending.

142. I102 pushed 5b69fba; all four CI lanes pass (37034741307). Windows profile case aliases reproduce a missed
    owner and portable/usual states collide (I103): regression 1/1 fails, process baseline only 1/4 passes. Actual
    state-directory identity remedy passes 4/4 process cases and Windows guards 6 pass/3 Unix skips; Ubuntu native
    boundary guards 23 pass/4 explicit skips. CI process harness/records added. Source/payload/results retained (E-I103).
143. Owner's Windows title report queued as Low-priority I104. Later clarification: the repeated word comes from the
    selected directory being named FileCat and is valid. Requested order is FileCat first, then selected path/location,
    then username/elevation. Both reported titles retained; no title code changed during safety validation.
144. I103 pushed fa3a02a: all four push CI lanes pass (37036329081). Manual run 37036698071 binds that same source,
    passes all four lanes and Linux/macOS package jobs; dev.539 Linux bytes downloaded/hashed (E-V19-P2).
    Windows tag-only packaging skipped; no publication or release controls changed.
145. Final Ubuntu 26.04 raw/native archive retained on host with matching guest SHA-256, 1,093,581,374 bytes and
    7,705 members. Independent streaming verifier checks 14 required TRXs/payloads/immutable fixture hashes,
    passes without missing/mismatched files. All failures/nonpasses retained; SDK download/source/selected built
    payloads retained separately from excluded extracted build/runtime trees. Identity-checked test-owned Samba
    stopped and three owned loop devices detached; immutable FAT16 bytes unchanged (E-X02). Snapshot restoration
    can now proceed without losing this evidence.
146. Restored clean Ubuntu 24.04 baseline; identity/pinned SSH/no FileCat/no SDK preflight passes. Exact dev.539 .deb,
    tar and normal FUSE AppImage pass GUI/corrupt state, 203-byte TMPDIR, separate-session Unicode-TMPDIR forwarding,
    saved workspace/restart and clean socket/lock/mount release. Debian/tar native desktop entries pass; F5 copy of
    the Unicode fixture matches 45-byte independent hash. GLib helper oracle 12/12; Debian remove/reinstall/purge
    preserves user state and unowned sentinel. Setup/oracle failures retained, no product remedy for them. Full
    324,638,274-byte archive copied/hash-verified and independently checked before shutdown/restoration (E-V19-P2).
147. Clean Ubuntu 26.04 restored via VMware-bound SSH key; identity/no FileCat/no SDK preflight passes. Exact dev.539
    repeats pass: 3/3 native GUI formats with long TMPDIR/forwarding/corrupt-state/restart, normal FUSE, both native
    desktop routes, Unicode F5 copy, 12/12 GLib helper cases and Debian lifecycle/state/sentinel preservation. Loaded
    ICU78 verified. Full 255,953,184-byte archive retained/hash-verified; independent verifier confirms exact inputs,
    copied bytes and result/state records. Both fresh Ubuntu package matrices now pass preliminarily (E-V19-P2).
148. Exact fa3a02a manual CI raw process artifacts retained: Windows 4/4, Linux/macOS 8/8 each; strict boundary runs
    pass all three temporary-path scenarios on each Unix lane. macOS dev.539 archive retained with exact byte/hash
    provenance; CI startup does not replace signed/native-desktop candidate qualification (E-I102/E-I103).

149. I105 reproduced on exact dev.539's SDK-free Ubuntu GUI: root-owned unwritable Data makes the window fall back
    to per-user state; independent lease busy, same-base probe false. Three Windows baseline regressions fail.
    Read-only portable/per-user candidate and profile-root discovery remediated; only actual live roots guarded.
    Native working-overlay ordinary/independent windows pass 2/2; Windows App 225 pass/15 skips, final guards 10
    pass/3 Unix skips and paths 13 pass/1 Unix skip. Full before/after archives independently verified (E-I105).
    CI/rebuilt packages, wider discovery and candidate/device tracing pending; NO-GO remains.

150. I105 all four lanes and development Linux/macOS packaging pass at exact 1cd803c (37055272672); raw dev.545
    packages/CI artifacts retained, each strict Unix result independently confirms 35 pass/four skips. Broader
    audit reproduces I106: second portable GUI PID 12176/lease busy, own-base probe true, other-base probe false.
    Native before bundle retained. Device gate gains read-only process census: Windows baseline guards 1/3,
    working guards 14/17 and final App 229/244 with explicit skips; native after/CI/rebuild still pending (E-I106).
151. Owner's Windows host menu failure persists after host tests finish (I107). Copied Program Files App DLL matches
    current working-build bytes; version stamp 1cd803c includes I106 working inputs, not proof of a clean tree.
    Live host test authorized, but Windows Computer Use initialization crashes twice and after reset/retry;
    no window selected or input sent. Cause unproved; preserve this as a UI-connection gate (E-I107).
152. I107 raised to High/must-fix at the owner's instruction. Same symptom reported in the running Windows VM and
    after Codex restart/compatibility launches. Headless menu press/release checks pass 1/1 with/without opt-in trace.
    Initial isolated diagnostic GUI execution is not established: state/log absent, although both version probes
    return 0. Startup capture and native diagnostic payload in preparation. Exact earlier fa3a02a/08c2e2d archives
    publish successfully for the owner's requested comparison; no native menu result or fix claimed (E-I107).
153. I107 second diagnostic and exact fa3a02a/08c2e2d comparisons staged and hash-verified in the Windows VM.
    Launchers use isolated state, invocation markers and pinned-runtime stderr capture; owner clicks requested
    because the Windows automation runtime cannot start. Full affected App regression passes 230/245 with 15
    explicit platform skips and no failures. Source/payload/test identity retained; native result pending (E-I107).
154. I107 owner-operated VM trace identifies repeated unchanged theme application removing the open menu;
    popup detaches/closes while the main window remains active. All three comparison GUIs started/exited 0;
    earlier failures are owner-observed. Two new regressions fail on baseline, then pass after the palette guard;
    targeted menu/theme/tooltip checks 14/14, no skips. Remaining App/native after/CI pending. Automation retry
    after Claude closes still fails, now with specific Windows sandbox setup refresh errors (E-I107).
155. I107 palette guard's disjoint targeted/remainder runs cover all 248 App cases: 233 pass/15 platform skips,
    zero failures. Owner confirms the host works and authorizes remaining release work; Program Files App DLL
    independently matches working-fix bytes. Host symptom cleared preliminarily; guest after/affected CI and
    candidate interaction remain pending. Resume I106 native process guard and wider recovery audit (E-I107).
156. I106 native after passes on SDK-free Ubuntu 26.04.1: separate portable and --data GUIs/independent leases are
    detected, both absent again after graceful close. Full before/after archives independently stream-verified.
    Exact ecd61f3 all four CI lanes pass; strict Unix inventory 47 pass/four skips per lane (E-I106).
157. Exact ea4a2ac passes all four push lanes and manual lanes plus Linux/macOS dev.549 packaging (37068909015).
    Strict Unix inventories again 47 pass/four skips each; raw artifacts retained. Rebuilt native package checks
    and wider recovery audit remain pending. Windows VM restarted after owner's keep-running instruction, then
    owner reverts snapshot; guest access verified. Preserve earlier evidence on host and keep VM running (E-I107).
158. Exact dev.549 .deb/tar/FUSE AppImage pass successor native startup, corrupt-state preservation, 209-byte
    temporary folders, separate-session forwarding and graceful restart on existing SDK-free Ubuntu 26.04.1.
    Full archive independently stream-verified; this is not another clean-install baseline (E-V19-P3).
159. I106 renamed-apphost audit reproduces false absence despite actual GUI/independent busy lease. Read-only
    executable-identity correction detects ordinary portable, --data and renamed native GUIs. Complete App
    inventory passes 235/250 with 15 explicit skips. Ordinary-account absent cases now return unknown rather
    than false; safely refused, but process-visibility/availability remains an open qualification issue. Full
    before/after archives independently verified; successor CI and exact-candidate tracing pending (E-I106).
160. I106 identity source 06c5791 passes all four CI lanes. Exact 1a9f1ba manual run 37073593358 passes all lanes
    and Linux/macOS dev.553 packaging; strict Unix independently verifies 53 pass/four skips per lane. Packages
    retained, not native-qualified; later availability change requires successor checks (E-I106).
161. I106 availability audit identifies kernel tasks/no executable and normal Python/snapd images above the
    initial bound. PF_KTHREAD-only exclusion and larger bounded/vectorized inspection pass native root absence,
    all three ordinary/root live cases and root absence on close; ordinary restricted census remains unknown.
    Complete App inventory 236/251 with 15 skips, no failures; raw archive independently verified. Correction
    d8c6f3b pushed; affected CI, rebuilt packages, broader visibility/races/candidate tracing pending (E-I106).
162. I107 clean 1a9f1ba self-contained Windows input staged after owner snapshot restoration. UUID/OS and all
    256 payload hashes verified, CLI version exits 0; Admin desktop launcher captures isolated menu trace/exit.
    Computer Use reset/import still fails before input; owner native click result requested and pending (E-I107).
163. Exact d8c6f3b passes all four CI lanes (37075680108). Strict Unix inventories independently confirm 56
    pass/four skips each, 60 outcomes. Raw run/results retained; no tag/release. Both VMs remain running, corrected
    guest menu check awaits owner input. I106 broader privilege/runtime-alias/race and candidate tracing open.

164. Owner confirms clean 1a9f1ba guest menus work. All 256 inputs and trace module reverified; 13 menu episodes
    close through pointer input, none through logical detachment; one remains open 47.6 seconds. Complete raw
    log/export collection retained and independently parsed (E-I107). App still running at collection; no exit
    result claimed. I107 closed for preliminary remediation; exact-candidate interaction remains mandatory.
    Windows Computer Use remains independently unavailable. Continue unblocked recovery safety work.

165. I106 confirmation-window gap reproduced: original admission checks precede the dialog, allowing a new
    writer before acceptance. Controlled elevated Windows guest baseline fails eight cases, four controls pass;
    all four routes covered. Recheck full safety after acceptance, before device authorization. Corrected guest
    RecoverySafety inventory 29 pass/three skips, all twelve new cases pass. Host complete App inventory 242/263,
    21 prerequisite/platform skips, no failures. Exact working source/bundles/outputs retained (E-I106). No actual
    device reader used; broader census/lifetime/physical tracing remains open. Native Unix/affected CI pending.

166. Confirmation fix 5388e5c and test prerequisite correction cc1acf2 pushed. Clean cc1acf2 passes all four CI
    lanes (37079952362). Independently verified strict Unix inventories: 74 pass/22 explicit skips per lane,
    96 outcomes. SDK-free Ubuntu 26.04.1 native self-contained test input passes the same matrix; all 350 inputs
    verified. Successful and earlier failed full archives retrieved and every regular member stream-verified.
    Source/runtime/device race limits remain explicit (E-I106); no source-device scan or candidate claimed.
167. Read-only serial-filtered media preflight finds no 2F2000129618 drive on the host, Windows VM or Ubuntu VM
    at 00:08–00:10 UTC. Owner asked to reconnect it to the host for remaining V09 source-device write checks;
    identity must be reverified before use. Both VMs remain running. I107 preliminarily closed; I106 and final
    qualification remain open, release NO-GO, no candidate/tag/publication.

168. Owner connects G: and authorizes necessary disposable USB use. Exact serial 2F2000129618, disk 5, Storage
    capacity 7,796,162,560 bytes, FAT32 volume GUID and non-boot/system partition verified. Audit before change
    finds narrow/incomplete physical test guards; 1df5dff pins identity/capacity/GUID/backing disks, rechecks mutation
    phases and retains expected file hashes off source. Fourteen refusal cases and real preflight pass 15/15;
    absent-opt-in skips independently verified. First progress-stream harness failure retained. No source mutation.
169. Clean 9257967 census returns unknown on the host and elevated guest after exact owned menu-fixture teardown;
    later independent guest inventory has no FileCat/dotnet name. Protected module identities reproduce Windows
    availability limit. Diagnostic limited-rights comparison retained without weakening production refusal
    (E-V09-G1). Prepare exact 1df5dff guest payload; raw-read/trace work needs USB guest routing or host elevation.
    Broader I106/physical/candidate gates remain open; both VMs remain running, no tag/publication.

170. Exact clean 1df5dff self-contained guest payload staged: all 300 input hashes verified, fourteen native guard
    cases pass, exit 0, direct XML and every output independently verified (E-V09-G1). Defaults to synthetic
    verification; separate physical phases await routing the authorized USB into this elevated VM. CI 37083622189
    passes Windows x64/Ubuntu/macOS so far, ARM64 pending, packages skipped. No actual physical scan/format claimed.

171. Final exact 1df5dff CI 37083622189 passes all four lanes, packages skipped. Full run/log retained; direct
    Windows TRX inventories independently verify App 248 pass/15 skips, Core 699/47, platform 148/33, Remote 88/28,
    no failures. New guard cases 14/14; hardware preflight explicitly skipped. Both Unix strict direct XML
    inventories independently confirm 74 pass/22 declared skips each. USB routing/elevated physical execution is
    the next setup gate; guarded guest phases are ready. No stable release/candidate or source-device pass claimed.

172. USB routing retry identifies the authorized stick on guest disk 1, E:, then VMware disconnects it before the
    native physical preflight starts. Wrapper refuses absent media; logs/identity snapshots retained (E-V09-G1).
    Host G: is available again. All 300 clean 1df5dff inputs rehashed unchanged; host launcher prepared with an
    explicit one-pass/no-skip preflight gate before FAT32/exFAT/NTFS disposable component scenarios. Parser checks
    pass; launcher not run. Host session lacks administrator rights: owner launch/UAC is the next setup gate.
    No physical scan/format or zero-source-write result claimed; I106 and candidate qualification remain open.

173. Owner executes the host launcher in an elevated shell. Native preflight passes one case and FAT32/exFAT/NTFS
    component recovery passes all three, 325/325 generated deleted files exact per format. All 300 inputs and
    fifteen recorded output hashes verified; actual XML/expected manifests retained. No installed-helper,
    independent source-write trace, partial-item or exact-candidate qualification claimed (E-V09-G2).
174. Physical byte-checker audit reproduces ten controlled false acceptances, three controls pass. 2e6dffe compares
    exact unmissing intervals, rejects wrong lengths/invalid ranges and requires complete Recoverable bytes.
    Thirteen cases pass; affected inventory 27 pass/seven explicit hardware skips. Initial empty-opt-in setup
    refusals retained separately. All four 2e6dffe CI lanes pass; direct Windows cases independently verified.
175. 85bb17d adds off-source observed per-item lengths/hashes/missing ranges and requires complete reads.
    Affected inventory again 27 pass/seven skips; all four successor CI lanes pass. Clean self-contained payload
    has 300 verified inputs/archive members; native checker 13/13 and guard 14/14. Stronger host launcher parses
    in Windows PowerShell 5.1 and preparation shell, not yet executed. Owner elevated launch is the next setup
    gate. Preliminary evidence inventory retains 378 files; no candidate/tag/publication (E-V09-G2).
176. Owner launch is followed by two overlapping USB campaigns, 1df5dff and 85bb17d, on the same source. Both
    finish Failed; the stronger attempt's passing cases are also invalid for qualification. All 28 wrapper-recorded
    output hashes and direct cases are independently verified; original guard/caller sources retained before fix.
    1883eb4 adds a fixed per-serial cross-process guard lease; 090a2b6 honors test cancellation. Affected checks pass
    29 with seven hardware skips; App compiles with one explicit live skip. Clean successor native checker/guard/lease
    controls pass 29/29; all 300 inputs/archive streams verify. Launcher mutex controls pass duplicate refusal,
    available success and abandoned-owner recovery. Legacy entry points retired with original bytes retained.
    Distinct exclusive launcher is ready; one elevated host launch is the setup gate. No successor physical pass or
    source-write qualification claimed. NO-GO remains (E-V09-G3).
177. Guard 1883eb4 and successor 090a2b6 CI both pass all four lanes, three package jobs skipped. Complete logs,
    exact run identities and original Windows test archives retained; archive/member hashes verify. Direct Windows
    TRX inventories independently confirm App 248 pass/15 skips, Core 699/47, Windows platform 163/33 and
    Remote 88/28, no failures; all 29 checker/identity/lease controls pass in each. Physical skips remain explicit;
    no hardware or candidate qualification inferred (E-V09-G3).

178. Owner reports failing GitHub CI. Exact b5ce744/a50b3b8 runs fail only Windows lease helper readiness at 15 s;
    complete logs, original archives and direct inventories retained. Older bb2d748 macOS failure is a live-copy
    observer timing race. I108 corrects readiness synchronization and holds the real copy before verification,
    preserving all substantive assertions. Affected local 29 USB synthetic and eight progress checks pass.
    Baseline full-solution host run retains a separate Windows records assertion failure; not claimed green.
    Successor CI validation is required before resuming the next physical trace setup (E-I108).

179. Exclusive elevated USB run at exact 090a2b6 completes: preflight 1/1 and FAT32/exFAT/NTFS 3/3, no failures or
    skips. Independent verification matches all 325 generated deleted files per format and all 327/327/326 complete
    claims, verifies 300 unchanged staged inputs and nineteen recorded output hashes. Pinned USB/partition identity,
    native phase/child records and scoped observer retained. Separate 25-file completed-run inventory preserves the
    original preparation inventory. This clears the earlier owner-launch gate; installed-helper/source-write/full
    source hash/candidate qualification remains open (E-V09-G3).
180. I104 formatting correction a50b3b8 puts FileCat first in Windows titles, then selected location and account/
    elevation. Existing assertions fail one case on baseline b5ce744, pass 2/2 corrected; existing headless window
    title case passes 1/1. Selected folder named FileCat remains valid. No live desktop or elevation-detection claim;
    exact candidate interaction remains pending (E-I104). CI failure at a50b3b8 is isolated to I108.

181. CI synchronization correction 6cad380 passes all four lanes (37119313116); three package jobs skip.
    Exact complete metadata/log and original Windows archive retained, GitHub archive digest/member checks pass.
    Direct inventories confirm App 248 pass/15 skips, Core 699/47, Windows platform 163/33, Remote 88/28,
    zero failures. All affected lease/oracle/guard/progress/title cases pass; lease helper readiness takes 9.784 s.
    Full local repaired Core also passes 700/46 explicit skips. I108 is remediated preliminarily; no candidate
    qualification or release GO inferred (E-I108). Owner keeps the USB connected; no current test touches it.

182. The separately retained host file-record assertion fails again. Independent native probe confirms error 50
    on a 300,000-byte NTFS fixture with metadata, data and Generic Read handles; the host driver declines the cluster
    query. I109 makes the unavailable layout explicit and checks native support without skipping the rest of the
    integration case. Updated assertion fails on the unchanged report; after correction full Windows 159 pass/
    37 hardware/platform skips, 196 total, and a native report probe confirms the explanation. Original sources,
    reports, failures and nineteen-file independent inventory retained. Successor CI remains required (E-I109).

183. Documentation successor c042d91 and I109 correction cfcc9e6 both pass all four CI lanes. Complete exact
    cfcc9e6 run/log and original Windows archive retained; direct inventory verifies the file-record case and
    earlier I108/I104 cases, no failures in four Windows project inventories. I109 is remediated preliminarily;
    native branch is not separately logged, candidate reruns remain required (E-I109).
184. Prepare the next V09 tracing prerequisite: pinned installed Process Monitor 3.95, existing accepted license,
    no active capture. A marked private launcher captures a known off-source temporary file's reads/writes and a
    never-accessed-path negative control, retains native PML/full CSV, and verifies restoration of the existing
    flat tracer configuration. Parser/preparation controls pass without capture or USB access. Real command-line
    capture/export remains unverified until the owner launches it elevated. The agent is unelevated; owner action
    requested with the concrete LaunchProcmonCaptureControl.cmd. This is an instrumentation control, not source-write
    qualification. Private preparation: artifacts/release-evidence/v09-usb-trace-20261003; runner SHA-256
    08535ab32b175b24c1d0fd9193dbdfdf1f6dec275a799b7b3b0d2926f44af319, launcher
    fc81ed7681853f16ffa61d7635f9a104fc00dfeb6534614923e71ba2e75460ce. USB remains idle and Windows VM kept running.

185. Owner launches the prepared tracer control elevated; run control-8a15fbb06c284156873117bfa820f3be succeeds.
    Independent streaming verification confirms 455,237 CSV events, exact 4,096-byte positive read/write and zero
    never-accessed-path events; seventeen files size/hash verified. Original native PML/full CSV retained.
    Independent registry read confirms all thirty saved values/types restored, no subkeys or remaining tracer.
    The first verifier's wrong expected file count is retained and corrected. USB is not accessed. This clears
    the instrumentation-control launch gate, not physical source-write or installed-helper qualification (E-V09-G4).
186. Prepare the bounded read-only USB component trace at clean self-contained f2f0141: all 300 native inputs and
    four embedded source versions independently verify. Selector-only control chooses one explicit prerequisite skip;
    no raw source access. Windows PowerShell 5.1 parser/hash/truncated-input/duplicate-campaign/available controls pass.
    Independent preparation inventory retained. The launcher will hash the complete physical disk before/after and
    retain the native case's full PML/CSV, controls, process/lease handovers and restored configuration. Current USB
    metadata is safe/present; owner elevated host launch is required. No live result or installed-helper/GUI/candidate
    qualification inferred. 087917d/f2f0141 CI metadata confirms four green lanes each (E-V09-G5).
187. Final owner-launch check catches malformed launcher line generation, then an inherited PowerShell 7 module
    precedence that makes Get-FileHash unavailable under cmd.exe/Windows PowerShell 5.1. Both failures/inputs are
    retained. The corrected five-line launcher sets system Windows PowerShell module precedence locally; its exact
    command with ValidatePreparationOnly exits zero without raw USB access/capture. Final independent inventory
    verifies 300 native inputs and 45 preparation files, including command control. Initial launcher-readiness
    inference is superseded; payload source stays exact f2f0141. Owner launch gate remains (E-V09-G5).
188. Owner executes the exact G5 elevated launcher. Preflight/native read case each pass once; all 300 inputs and
    43 run files independently verify. Before/after full 7,796,162,560-byte physical hashes differ. PML and full CSV
    independently contain only 104,749 events ending before the marker/test, so positive controls fail and no source
    attribution/zero-write evidence exists. Original verifier failure is retained; separate incomplete inventory
    explicitly refuses qualification. Thirty original configuration values restored; no workers/tracers remain.
    Hold further USB tests and investigate timing off-source. Known guest tracer/config paths absent after snapshot
    restoration; no new license accepted. Exact 81b352e CI has four passing lanes/three package skips (E-V09-G6).
189. Complete scanning of every G6 PML event/CSV row confirms its one-second range and no later events. Prepare six
    off-source capture comparisons varying runtime, working/temp directories and readiness, with exact early/late
    read/write markers, child lifetime controls and per-case configuration restoration. No USB/FileCat launch.
    PS5.1 parser, marked/unmarked child content/ownership controls and actual cmd.exe preparation invocation pass;
    nineteen preparation files independently verify. Old USB launcher bytes preserved; original path now exits one
    with a hold notice, verified by actual cmd.exe execution. Owner elevated host launch is required. Exact 43e520d CI has
    four passing lanes/three package skips. Live capture qualification remains pending (E-V09-G7).
190. Owner authorizes agent launch. Tool token is standard but Windows RunAs succeeds; native controller validates
    administrator token. Off-source A reproduces one-second incomplete capture; B/C sustain exact marker/child
    controls, independently matching PML/full CSV. Seventy-five completed-case files verify; remaining cases running.
    Runtime alone cannot explain failure. Initial cross-clock timestamp assertion fails by 91 microseconds and is
    retained; revised native-clock ordering/coverage checks preserve exact counts/ranges and report offsets.
    Actual 84615d9 CI passes four lanes/three package skips. No USB access/qualification (E-V09-G8).
191. All six timing comparisons finish: B/C/D pass exact marker/child controls; A/E/F fail with only about one second
    of events. Final independent 158-file/PML/CSV inventory and thirty-value registry/worker census verify. System
    temp association does not explain G6, and separate WaitForIdle does not correct F. No durable remedy inferred.
    Local WPR has DiskIO/FileIO profiles and reports no active recording; investigate off-source recorder controls
    and loss statistics. USB path remains held for unexplained G6 source difference (E-V09-G8).
192. Pinned built-in WPR/tracerpt scripts parse/preparation passes; agent RunAs executes the unique named DiskIO/FileIO
    control without USB access. Exact parent/child file bytes, typed file events, one corresponding physical disk
    write per marker and child lifecycle pass; native/summary agree 2,561,544 events, zero reported loss. Independent
    63-run/23-reader-file inventory and named-session/worker cleanup verify. Full ETL/XML/profile/reports retained;
    XML schema/timezone limitations disclosed, native cached TraceEvent 3.2.6 reader used. No product dependency or
    raw-device/source-write pass. Continue owned virtual-device visibility control before physical work (E-V09-G9).

193. Agent RunAs executes the owned raw virtual-device WPR controls. Two setup attempts stop before raw I/O and are
    retained with all-zero fixtures and independent detach/session/worker checks. Successor validates numeric CIM
    bus and empty-safe partition query before exact 64-MiB virtual access. Native FileIO/DiskIO capture all three
    raw reads and the deliberate 4-KiB write with exact process/thread/offset/count/call boundaries; two System
    attachment reads are separately disclosed. Full offline data oracle finds only the intended positive block.
    Native 2,057,617 events, zero reported loss; independent 154-run/74-diagnostic-file inventory and cleanup pass.
    No protected USB/product/candidate qualification. Exact c930c8f CI passes four lanes/three package skips.
    Continue source-change isolation while FileCat USB validation remains held (E-V09-G10).

194. Correct the physical comparison's equal-short-read acceptance gap (I110): each returned count must equal the
    request, counts/hashes and deleted-entry totals are logged, expected session ending required. No production
    changes. Native off-source controls pass 23/two declared skips; strict physical body remains held. Elevated
    existing host C: read fails error 50, independently reproduced by four aligned native controls; four E: controls
    succeed. Owned E: temporary fixture then passes the existing elevated reader case, one/no skips. Failed and
    incomplete attempts retained; 32-file inventory and final worker check verify. No C:/USB/candidate pass (E-I110).

195. Pure read-only source-change observation at c92d31a retains two full 7,796,162,560-byte physical images,
    each with 1,859 exact chunks; independent bytes/hashes match each other and G6's after hash. No FileCat launch
    or source writes requested. Native trace loses 51,216 events despite zero live counters, so source-write
    qualification fails; no historical attribution inferred. Sixty-seven run files, exact read events/lifetimes,
    cleanup/leases and post-compression logical hashes verify. G6 remains unresolved and USB held (E-V09-G11).
196. Custom single-kernel-collector short raw controls pass: exact four raw operations and full 64-MiB oracle,
    parent marker/lifetime, 61 run/74 diagnostic files and cleanup independently verify. Native 609,387 events,
    zero reported loss; initial verifier schema/name failures retained. Continue a duration control before another
    source observation. Exact c92d31a CI passes four lanes/three package skips; strict USB body remains held (E-V09-G12/E-I110).

197. Nine-minute custom kernel control initially saves only 32 metadata events, despite success/loss-zero reports.
    Matched short pilots associate the failure with trace temporary location; exact Windows cause remains unknown.
    Original captures and a pre-capture launcher failure are retained. Short-scratch successor passes 1,190,789
    native events/zero loss, exact early/late raw operations separated by 540.020002 seconds, full 64-MiB oracle,
    64 run/98 diagnostic files and independent cleanup. Exact 6267331 CI passes four lanes/three package skips.
    Continue proof-gated read-only source observation; FileCat USB path stays held (E-V09-G13).

198. Proof-gated read-only USB observation passes with the exact G13 profile and short trace scratch. Both full
    images match G6's after hash; 5,634,333 native events/zero loss contain all 3,718 source reads and no source
    disk/file writes. Exact images/chunks, positive visibility, lifetimes, 65 run files and independent cleanup
    verify. Initial profile-byte mismatch refuses before source access and remains retained. No FileCat launched;
    historical G6 difference remains unresolved and product USB validation held. Exact b477783 CI passes four
    lanes/three package skips (E-V09-G14).

199. Read-only Windows process-structure audit verifies standard/elevated snapshots and native self controls.
    Limited image paths remain unavailable for 140/412 standard and five/411 elevated processes. Four elevated
    rows have null PEB/protection flags; Idle never opens. No structural/name exemptions or absence claim added.
    Reserved-variable failure retained; corrected pins, accounting and worker cleanup independently verify.
    Continue I106 availability and other unblocked audits; no candidate or source-device pass (E-I106-P1).

200. Windows census now uses a limited read-only image query instead of requiring module/memory access.
    Owned-child error-5/positive image/negative query controls pass under standard and elevated launches;
    initial elevated fixture failure retained and repaired within its own impersonation scope. Full host App
    242 pass/21 skips and corrected Windows platform 161 pass/37 skips, no failures. Exact working source/DLLs,
    623 files, XML/skip inventories and cleanup independently verify. No exemption/absence/USB pass; native clean
    guest and successor CI pending (E-I106-P2).

201. Preceding documentation c162481 CI passes Windows x64/Ubuntu but fails ARM64's global thumbnail helper-start
    count and macOS's verified-copy totals checkpoint. Full failing run/log retained; I108 reopened for diagnosis.
    No source/candidate result is inferred from that run (E-I106-P2).

202. Clean 36ee824 Windows process-query payload passes both native permission controls in the elevated guest,
    zero skips. All 300 input pins/301 archive members, exact XML/control output and worker cleanup verify;
    first missing-marker retrieval refusal retained and corrected. Exact successor CI passes four lanes/three
    package skips. Broader I106 absence/device/candidate gates stay open (E-I106-P2).

203. Correct I108 observers: copy waits for finalized discovery totals at its held checkpoint; thumbnail case
    requires its helper answer/UI binding, while native reuse/containment assertions remain separate. Attempted
    rendered-byte control fails on the existing mock backend and is retained; no rendered-pixel claim. Full Core
    700 pass/46 skips, corrected full App 242 pass/21 skips, targeted UI 2/2 and native client 9/9 pass. Exact
    working source/XML/skip inventories independently verify; successor CI pending (E-I108-P1).
204. Exact 1bd931b successor CI passes all four required lanes; three tag/manual package jobs skip. Complete
    metadata/log and Windows artifact retained; direct XML independently verifies all four inventories, eight
    progress, two thumbnail UI and two limited-image cases. I108 is verified preliminarily; candidate and native
    rendered-pixel qualification remain open (E-I108-P1).
205. Resume V12/I92: fresh million-long-name baseline reproduces 202–345/737–1,168 ms blocking scans. Large
    quick search now leases captured rows/indexes and runs off the UI thread, with ordered keys, pending feedback,
    stale-view retry and immediate UI cancellation. Nine Core/seven new headless App controls pass; serialized
    model acknowledgement 0.017–0.494 ms. Initial fixture/observer failures retained; native frame/AT/reference
    and successor CI remain open (E-I92).
206. Affected full Core exposes I111: publishing an empty/parent-only view can suppress its delayed first file.
    Four controlled baseline timeouts reproduce it; revised geometric batching passes all four and original
    cursor/streaming controls. Exact failing/corrected inputs retained (E-I111).
207. Further full Core exposes I112's cache observer sampling active loads after a constant total. Held-read
    negative control proves that plateau is insufficient; bounded actual-load wait retains exact sum/ceiling/
    disposal assertions. Seven cache cases pass. Final full Core 714 pass/46 skips; App 249 pass/21 skips;
    twelve exact source files, 790 payloads, ten XML inventories and 2,480 files independently verify. Native
    computer-use initialization still fails before app selection/input; CLI work continues. Candidate/CI pending.
208. Clean b7d2e8 successor CI passes all four required lanes, three package jobs skip. Direct Windows XML
    verifies Core 713/47 skips, App 255/15, platform 165/33, Remote 88/28 and all 39 affected cases. Clean
    self-contained Windows VM run passes 20 Core/seven headless App cases with no skips. Independent payload,
    source-content/raw-byte, XML and cleanup checks verify; controller/workers and owned temp files are absent.
    Mixed line-ending observer corrections retained. Native frame/AT/reference/candidate remain open (E-I92–112).
209. V12 slow quick view exposes I113: six controlled baseline failures cover stale errors/repeated-key readers,
    failed initialization/empty reset cleanup, abandoned reads and ten concurrent held opens. Immediate request
    retirement, separate result ownership and existing per-device scheduling pass seven controls; two held opens
    bound eight canceled queued demands, while another device completes. Full App 256/21 skips passes (E-I113).
210. Affected full Core has one GnuPG test failure. Two parallel fixtures mutate the shared tool override (I114);
    a controlled unchanged signature is Good → UnknownKey → Good across tool swap/restore. Original interleaving
    remains untraced. Override fixture isolated; full Core 714/46 skips passes. Exact three-file overlay, 1,056
    final inputs and 3,329 retained files independently verify. Clean CI/guest/candidate pending (E-I114/E-I113).
211. Clean 7497acf passes all four required CI lanes; three package jobs skip. Windows XML independently verifies
    all four inventories and 47 affected cases (native GnuPG explicitly skips there; Git-GnuPG passes). Clean
    Windows guest passes seven quick-view controls without skips; 364 payloads/365 ZIP members/ten source-content
    files and XML/cleanup verify. Controller/test worker/temp files absent. Native presentation/candidate remain open.

212. Resume V13 archive-member results. Four valid controls fail because narrowing silently skips every non-file-system
    reference (I115). Revalidation now retains original identities/ordinals/relative paths and only the input subset;
    contents, unsupported scopes, partial/missing/unreadable listings are explicit. Eleven Core and two headless
    Find/content/log/navigation controls pass; full Core 725/46 skips and App 258/21 skips pass. Exact baseline/final
    input and direct XML inventories independently verify (E-I115). Two earlier fixture replacement faults and
    deferred-row observer failures are retained separately. Clean CI/guest execution is next; candidate/native remain open.

213. Clean ff8746a passes all four required CI lanes, three package jobs skip. Six direct XML inventories verify,
    including all 13 new Windows cases and both App flows on Linux/macOS; three artifact ZIPs match server digests.
    The exact clean self-contained payload passes 13 Windows guest controls without skips. All 683 payloads/684 ZIP
    members/eleven source copies and retrieved XML verify; controller/workers absent, owned temp empty. Additive
    guest-locator correction retained, payload unchanged. Other formats, initial listing warnings and candidate/native remain open.

214. V13 initial archive search exposes I116: the adapter discards damage/duplicate-name warnings from actual
    providers. Two valid baseline failures plus two positive TAR/gzip narrowing controls; warning forwarding and
    per-archive deduplication now pass 32 affected cases. Full Core 729/46 skips and App 258/21 skips pass. Exact
    input/active-assembly/direct XML inventories verify (E-I116). Detector-misconfigured and disk-case observer
    attempts are retained separately. Clean CI/guest is next; other formats/native/candidate remain open.

215. Clean 6ecf4a8 passes all four required CI lanes, three package jobs skip. Six direct XML inventories and
    three server-digest-matching artifact ZIPs verify, including all 34 affected Windows cases and both Find flows
    on Linux/macOS. The exact self-contained source passes 34 Windows guest cases without skips; 680 payloads,
    681 ZIP members, eight source copies and XML/cleanup verify. Controller/workers absent, owned temp empty.
    E: fills during initial publish; all 668 failed-output files transfer to the authorized second workspace and
    hash-verify before the redundant failed tree is removed. Clean package/CI evidence uses that workspace.
    No historical/source evidence discarded; other-format/native/candidate and sealed-store gates remain.

216. V13's remaining saved-criteria/duplicates component controls pass (E-V13-F2). Four actual headless Find
    flows save literal/regex/hex/Unicode criteria, reload settings from disk and reopen/search an independently
    specified positive/negative byte corpus. Duplicates within a six-file subset retain both known groups,
    original relative paths/source membership and correct extra-copy marking, excluding unselected identical
    files and same-size different bytes. Full App 263/21 skips passes; 106 inputs/ten sources/direct XML verify.
    No production defect found. Clean CI/guest is next; process restart/native/candidate remain open.

217. Exact aed64a7 ARM64 CI fails the late-name discovery test after one second with an empty host list
    (I117); other three lanes pass. Its timer starts before probe/request admission; original scheduling is
    untraced. Controlled cutoff after the fixture receives the metadata request passes immediate/delayed
    setup; a coupled name/probe cancellation mutation fails both controls. Public timings/policy remain.
    Full Core 730/46 skips and seven related App controls pass; 218 inputs/source/direct XML and original
    failure verify (E-I117). Clean successor CI/guest next. Clean ab919ed meanwhile passes four lanes,
    with all five new saved-search/duplicate App cases on Windows/Linux/macOS independently verified
    against three server-digest-matching artifacts (E-V13-F2).

218. Clean da3a3d6 passes all four CI lanes; six direct inventories and three server-digest-matching ZIPs
    verify, including all eight network and five saved-search/duplicate Windows cases and all five affected
    App cases on Linux/macOS. Exact self-contained source passes eight Core/seven App guest cases, zero skips.
    All 685 payloads/686 ZIP members/13 source copies and retrieved XML/output pins independently verify.
    Controller/workers are absent, owned temp empty (E-I117/E-V13-F2). Native/candidate and real-device gates remain.

219. V12 exposes I118's stale metadata publication after actual checksum-sidecar invalidation/Forget;
    four unchanged-production failures include explicit Compute. Validity records and coordinated cache
    publication reject obsolete values and preserve unrelated fields. Four stronger event controls expose
    the missing retry wakeup in an intermediate fix; completed remedy passes all six controls, including
    1,000 abandoned viewport requests, a healthy second device and controlled interactive priority.
    Full Core 736/46 skips and App 263/21 skips pass. All 477 inputs/source/direct XML verify (E-I118).
    Clean CI/guest is next; wider/native/candidate checks remain.

220. Clean 2896108 passes four CI lanes; six direct XML inventories and three server-digest-matching ZIPs
    verify, including eight metadata and two checksum UI cases on Windows and both App cases on Linux/macOS.
    The exact self-contained source passes ten Windows guest cases, zero skips. All 680 payloads/681 ZIP
    members/eight source copies and output pins independently verify. Controller/workers are absent, owned
    temp empty (E-I118). Native demand/frame/AT, concurrent workloads and candidate qualification remain.

221. V12 page-load controls expose I119: closing a reader disposes a held actual-file read/revision source,
    and later page/refresh requests access the closed source. Eight baseline failures plus one normal cached-byte
    control; coordinated source-use accounting defers disposal while immediately retiring cache/new demand.
    Nine controls/38 affected cases and full Core 745/46 skips/App 263/21 skips pass. All 267 inputs/eleven
    sources per stage/active assemblies/direct XML verify (E-I119). Clean CI/guest next. Direct Source/picture
    use and wider queue/device/native/candidate remain. Native automation import again ends with trusted-Node
    exit/kernel reset before any input; component and VIX guest execution remain available.

222. Clean de1fd71 passes 45 Windows guest cases, zero skips; 683 payloads/684 ZIP members/eleven sources,
    output pins and process/temp cleanup verify (E-I119). Additive private-working-directory wrapper and ten
    round-tripped xUnit string-name escaping mappings retained; payload/tests unchanged. Three CI lanes pass;
    Windows fails the existing NTFS fixture I120 while all 45 affected Windows/seven Linux/macOS App cases pass.
    Six direct XML inventories and three server-digest-matching artifact ZIPs independently verify.

223. Original I120 CI report says the fixture's last LSN is older than its circular log retains, but the MFT test
    dereferences its absent history table. Raw blocks/IO timing unretained; no unobserved attribution claimed.
    Separate current-MFT and bounded-live-history cases preserve positive assertions and declare unavailable
    live history skipped. Full host platform 161/38 skips and nine golden NTFS log cases pass; four record
    privilege skips do not qualify live history. All 130 inputs/source/active assemblies/original CI XML verify.
    Elevated guest and clean successor CI next (E-I120); production file-record code unchanged.

224. Clean 9074cf6 passes all four CI lanes, three package jobs skipped; six direct inventories and three
    server-digest-matching ZIPs verify. All 65 selected Windows/seven Linux/macOS App cases pass, zero
    affected skips, including complete live NTFS history. Exact self-contained source passes 20 elevated
    guest cases, zero skips, ending 06:44:27 UTC; 625 payloads/626 ZIP members/seven sources/output pins verify.
    Controller 13568/workers 5944/8176 absent, owned temp empty at 06:47:42 UTC. Guest system-volume metadata
    reads explicitly requested; no physical USB/recovery-source qualification. Original failed CI retained
    (E-I120/E-I119). Overall NO-GO; direct picture demand, wider/native/candidate checks and USB hold remain.

225. V12 direct picture-feed controls expose I121: four held actual-file reads are disposed by viewer/quick-view
    close; a fifth baseline case retains a loaded F3 bitmap, while normal quick view passes. Actual feeder
    borrows preserve source ownership after prompt cancellation; feed boundaries/exception observation and
    bitmap retirement prevent abandoned resources. Six controls/20 affected App cases/full App 269/21 skips
    and Core 745/46 skips pass. All 1,809 inputs/fourteen sources/active assemblies/direct XML verify (E-I121).
    Clean CI/guest next; per-device picture bounds, other direct Source use, native/candidate remain.

226. Clean a550fcd passes four CI lanes, three package jobs skipped; six direct inventories/three server-digest-
    matching ZIPs verify, including 56 affected Windows and eighteen App cases on each Unix lane, zero affected
    skips. Exact source passes 34 Windows guest cases, zero skips, ending 09:04:25 UTC; 686 payloads/687 ZIP
    members/fourteen sources/output pins verify. Controller 12360/workers 9500/12532 and owned decoder
    children absent, temp empty at 09:06:23 UTC. Original harness expects seventeen Core cases instead of
    actual sixteen; all sixteen pass but its guard stops App. Failed run/cleanup retained; exact corrected
    successor uses identical payload in a new root (E-I121). Per-device/native/candidate scopes remain.

227. V12 device-picture controls expose I122: F3 and rapid quick view hold three actual file reads despite
    two configured device workers; healthy device pictures complete in both baselines. Provider-keyed
    scheduled feeding retains actual callback ownership and drops canceled queued reads. Two controls/22
    affected App cases/full App 271/21 skips and Core 745/46 skips pass. All 1,812 inputs/fifteen sources/
    active assemblies/direct XML and scoped original/corrected call-count traces verify (E-I122).
    Clean CI/guest next; watchdog/hard-cap, aggregate decoder, other Source/native/candidate remain.

228. Clean 82f7488 passes all 36 elevated Windows guest picture/page-reader/budget controls, zero skips.
    Exact 687-file payload/688 ZIP members/fifteen sources/direct XML and worker/decoder-child/temp cleanup
    verify (E-I122). CI 37192262649 has three passing lanes and one Windows synchronization failure:
    changed.txt remains old, replacement job AwaitingDecision; its original request is unretained.
    All 58 affected Windows and twenty App cases per Unix lane pass. Server artifact digests/size and
    extracted XML inventories verify; failed CI is retained and synchronization investigation proceeds.

229. I123: the headless synchronization fixture omits native Windows adapter registration. A held target
    reproduces old content/AwaitingDecision/error-access with the portable adapter; the scoped native adapter
    passes 17 affected cases and full App 271/21 declared skips. All 2,202 inputs/thirteen sources per stage/
    unchanged production DLLs/direct XML verify (E-I123). Intermediate culture/path/location/space harness
    failures are retained, then identical captured assemblies pass in short outside-repository system temp.
    Production is unchanged; original CI request/interleaving remains unavailable. Clean CI/guest is next.

230. Clean 08acc2f passes four CI lanes and all 17 elevated Windows guest comparison/synchronization/operation
    cases, zero skips. Exact 367-file payload/368 ZIP members/thirteen sources/XML and worker/temp cleanup
    verify (E-I123). Three server artifact digests/sizes and extracted XML verify: all 75 affected Windows
    Core/App and 37 App cases per Unix lane pass, including all I122 controls. Complete live NTFS history passes.
    The original failed run and unavailable request/interleaving remain recorded; native/candidate remain.

231. V12 scheduler controls expose I124: the original production DLL's real timer throws an unhandled
    list-enumeration exception when adding a replacement; a separate controlled Watch probe exceeds a two-worker
    cap with three active calls. Enqueue now checks the cap and Watch scans the initial list count. Both corrected
    probes/two new controls/48 Core and 37 App affected cases/full Core 747/46 skips/App 271/21 skips pass.
    All 1,084 inputs/nineteen sources/probe DLLs/observations/XML/process cleanup verify (E-I124).
    Clean CI/guest next; synthetic callbacks do not qualify physical hung hardware or native/candidate scope.

232. I124 clean source 18006cc passes all four required CI lanes (37195976222); package jobs skip. All 85 affected
    Windows controls and 37 App controls per Unix lane pass. Six full XML/skip inventories, server digests and
    ZIP/extracted bytes verify. The self-contained SDK-free 26300 guest passes the same 85 Core/App controls,
    zero skips; 691 payloads/692 ZIP members/nineteen sources/pre-launch pins/direct XML/owned process and temp
    cleanup verify. Synthetic watchdog/cap qualification is preliminary; physical/wider/native/candidate remain.

233. V12 shutdown checks expose I125: original production Run admitted after disposal leaves an existing-queue
    task incomplete or starts a new-queue callback after the disposal snapshot. Both owned baseline probes exit 2;
    identical corrected probes cancel both tasks with zero callbacks. Enqueue now checks queue and owner shutdown
    under its lock. Two new controls/50 Core and 37 App affected/full Core 749/46 skips/App 271/21 skips pass;
    1,084 inputs/nineteen sources/probe DLLs/direct XML/owned process cleanup verify. Clean CI/guest next (E-I125).

234. I125 clean source 749f55f passes four required CI lanes (37198032750); package jobs skip. All 87 affected
    Windows controls and 37 App controls per Unix lane pass. Six full XML/skip inventories, server digests and
    ZIP/extracted bytes verify. The self-contained SDK-free 26300 guest passes the same 87 Core/App controls,
    zero skips; 691 payloads/692 ZIP members/nineteen sources/pre-launch pins/direct XML/owned process and temp
    cleanup verify. Admission/watchdog/cap qualification is preliminary; wider/physical/native/candidate remain.

235. V12/V13 comparison lifetime checks expose I126: four actual-production/owned-file activation/page revision
    cases dispose during their held call when closed or reopened, then fail on the released handle. Views now count
    revision/length/read calls and activation captures views. Identical corrected probe and App-only DLL swap pass
    all four; four regressions/41 App and 50 Core affected/full App 275/21 skips pass. All 1,434 inputs/twenty-one
    sources/24 fixtures/DLLs/XML/identity-aware cleanup verify. NU1015, inherited Core stamp and reused-PID observer
    records remain retained. Clean CI/guest next; updated Computer Use import still crashes before input (E-I126).

236. I126 clean source e406c96 passes four required CI lanes (37200743448); package jobs skip. All 91 affected
    Windows controls and 41 App controls per Unix lane pass. Three server artifact digests/ZIP bytes and six full
    XML/skip inventories verify. The self-contained SDK-free 26300 guest passes 50 Core and 41 headless App cases,
    zero skips; 693 payloads/694 ZIP members/twenty-one canonical sources/pre-launch pins/direct XML/owned process
    and temporary-folder cleanup verify. Comparison revision/length lifetime qualification remains preliminary.
    A plain Node call independently fails before loading Computer Use; exact records retained, no input/state
    observation. Live UI needs runtime setup restored; wider content/native/candidate remain (E-I126).

237. Resumed plain Node startup at 12:25:57 UTC fails before importing Computer Use. Kernel reset followed by
    one retry at 12:26:12 UTC fails identically: exit code 1 and Windows sandbox helper setup-refresh error.
    Complete tool diagnostics retained and pinned (E-I126). A full Codex restart is requested as the next setup
    recovery attempt. No desktop state/input observed, no USB action; both VMs remain running. Live interaction
    is gated; this does not qualify remaining native/reference/AT/candidate scopes.

238. Owner reports Codex restarted; fresh Computer Use initialization still exits before selecting a host/VM
    window (exact tool result retained with E-I126). Continue independent V12 checks. Actual clean e406c96 DLLs
    reproduce I127: two owned denied-subtree cases show/cache a 1,000-byte lower bound as exact and skip retry
    after access restoration; two accessible controls correctly count 1,234 bytes. Refresh preserves the defect.
    Valid failure XML/loaded-copy pins/unchanged file hashes/owned ACL restoration retained (E-I127). Intermediate
    harness failures remain separately recorded. Remediation/revalidation in progress; no USB action or native
    desktop qualification. Both VMs remain running; overall NO-GO.

239. I127 remedy carries lower-bound state through row and refresh/pending caches, marked stats and quick-view
    captions; Count can retry partial folders and a complete retry clears uncertainty. Identical probe with only
    App/Core DLLs replaced passes four cases; seven App and three Core regressions pass, including zero bounds and
    attached-pane updates. All 59 Core/63 App affected and full Core 752/46 skips/App 282/21 skips pass. Independent
    verification checks 1,800 inputs/nine sources/two properties files per capture, DLLs/XML case multiplicity and
    unchanged skip reasons, sixteen fixture payloads, restored ACLs and owned worker/temp cleanup. Original test
    encoding failure retained; corrected expectation passes identical production bytes. Clean CI/guest next.

240. Clean I127 source 2be20cf CI 37214155885 passes Linux/macOS and fails both Windows lanes: all seven new
    App controls fail the owned-fixture ACL-restoration check, including accessible controls. Complete direct
    TRX inventories and three server artifact digests verify; failure retained, not waived (E-I127). Descriptor
    diagnostics preserve the assertion and all seven host controls still pass. Corrected clean CI/guest pending.

241. I127 diagnostic b80f286 CI identifies Windows' auto-inheritance marker as the only descriptor difference
    in all seven direct Windows failures/seven ARM64 logged failures; every ACE matches. Corrected fixture
    comparison ignores only that marker and preserves all permission/cancellation/hash checks; seven host cases
    pass, 751 captured inputs verified. Clean b80f286 SDK-free guest separately passes all 122 affected cases
    without skips; 702 payloads/703 archive members/thirty canonical sources and owned process/temp cleanup
    verify. This precedes the fixture correction; successor clean CI/guest still required (E-I127).

242. I127 clean 9d33282 passes all four CI lanes, including ARM64 package startup/installer compilation, and
    122 SDK-free guest controls without skips. Complete server artifact digests/six TRX inventories verify:
    Windows Core 751/47 skips, App 288/15, Platform 166/33 and Remote 88/28; Unix App each 260/43. All 122 affected
    Windows cases pass; Unix App each 50 affected passes/13 declared Windows-fixture skips, including six
    unchanged Windows-factory reasons. Raw IDs/names/case multiplicity verify with only runner quote/backslash
    escaping accounted for. Native 702 payloads/703 ZIP members/thirty canonical inputs and owned process/temp
    cleanup independently verify (E-I127). Both earlier failed fixture CI attempts remain retained. Owner asks
    to finish this slice and stop before restarting Codex elevated; stop after evidence push, leave both VMs
    running. No USB action, native/AT/candidate qualification or stable publication. Overall NO-GO.

243. I128: actual clean 9d33282 production disposes an owned file during held line, HTML, Markdown and Info
    reads; the line task throws ObjectDisposedException. Four completed-read controls pass, hashes unchanged.
    Existing reader borrowing plus close cancellation/result retirement corrects all eight identical probe
    cases with only App/Core DLLs changed. Eight App/two Core regressions, 102 Core/41 App affected cases and
    full Core 754/46 skips/App 290/21 skips pass; 1,096 captured files/twenty-five source inputs/complete case
    inventories independently verify. Clean CI/guest pending, native/hardware/aggregate/candidate remain.
    Elevated restart still fails Node sandbox setup before any target/input; VIX works, both VMs stay running,
    no USB action. Initial probe compile error retained as harness-only evidence (E-I128).

244. I128 clean c453925 passes all four CI jobs (including ARM64 package startup/installer compilation),
    all 143 affected Windows/41 Unix App cases without affected skips and 143/143 SDK-free guest controls.
    Three server artifact digests/six full TRX inventories, 697 guest payloads/698 ZIP members/twenty-five
    canonical source/build inputs and exact owned process/temp cleanup independently verify. No native
    browser/input/frame, hung hardware, aggregate or candidate qualification is claimed. Owner reconnects
    and authorizes G: USB rewriting; identity alone verifies the same serial/non-system disk, original G6
    source-change evidence remains held and no USB mutation occurs. Both VMs stay running (E-I128).

245. I129: first eight held-worker count controls pass on clean c453925, but six already-queued UI
    result cases fail: canceled sizes become complete or overwrite/clear a newer 2,345-byte result.
    Current-lifetime checks at post application correct all sixteen identical probes with only App DLL
    changed. Sixteen cross-platform regressions, 43 affected/full App 306/21 skips pass; 742 captured
    inputs/twenty-one source/build inputs/full case inventories independently verify. Clean CI/guest
    next. No desktop input/USB access; both VMs stay running, source-change gate held (E-I129).

246. I129 clean f341dfd CI passes four jobs; three server digests/six TRX inventories verify. All
    sixteen new cases pass per platform; affected Unix lanes retain fifteen declared Windows factory/
    ACL/drive-letter skips. First affected guest is 41/43, two zero-byte refreshed I127 quick-view
    captions fail; all sixteen new cases pass. Raw failure/owned cleanup retained. Isolated unchanged-
    production seven-case diagnostic passes but does not explain the original. Caption investigation
    continues. Owner grants Mac SSH; read-only 26.6.2 arm64/eight CPUs/16 GiB/admin inventory succeeds.
    No remote mutation/USB access, both VMs stay running. Overall NO-GO (E-I129).

247. I130 controlled unchanged-row audit reproduces eight caption clearing/starvation failures with
    four quiet/genuine-change controls. Original six-case run and two timestamp-fixture failures retained;
    strengthened full-key/event probe is decisive. Pending folder key correction passes the identical
    twelve-case App-only swap; fourteen affected classes 72/72 and full App 318/21 declared skips pass.
    Inputs/748 captures/27 sources/XML/case inventories verify. I129 diagnostic process/temp cleanup
    verifies at 19:20:34 UTC; original guest 41/43 failure remains retained, exact attribution unknown.
    Clean committed CI/guest next. Both VMs remain running, no native input/USB access. NO-GO (E-I130).

248. Clean I130 3a408eb passes four CI jobs; artifact server digests/six TRX inventories/72 Windows
    affected and 56 Unix affected passes with sixteen declared skips verify. First guest 70/72 retains
    two I129 early checkpoint failures; all twelve I130/seven I127 cases pass. Controller 10956/worker
    10420 and temp files are absent at 19:39:57 UTC. I131 controlled refresh shows 1,000 partial bytes
    applied, then an unknown replacement row while the identity worker remains held: two original
    checkpoint failures/two controls. Fixture observes the actual callback; no production change.
    Corrected four-case probe/all 74 affected/full App 320 with 21 skips pass; 748 inputs/27 sources/
    complete inventories verify. Historical event trigger is not asserted. Clean CI/guest next.
    Mac read-only environment: Python available, dotnet not on the SSH PATH, data-volume free space sufficient for
    owned probes. Both VMs stay running, no native input/USB access/Mac mutation. NO-GO (E-I131).

249. Clean fdb17b4 passes all four required jobs in CI 37231232236. Three server digests/six full
    TRX inventories verify: affected Windows 74, each Unix 58 passes/sixteen declared skips; all
    eighteen retired-count/twelve caption cases pass per platform. Independent CI SHA-256
    `9492ed6584767b068a812381c22bc7d11c195481b5c46c10a50bde242e3167d9`. SDK-free Windows guest passes all 74 with zero skips at 20:20:11 UTC; controller
    5940/worker 4492, owned executable children and temp files are absent at 20:22:08 UTC. All 381
    payloads/382 ZIP members/twenty-seven canonical sources/case inventories independently verify,
    proof SHA-256 `2911e42af096558da0b5b72780fab68534aa825c317da789dd8f8af4bf94ce30`. Earlier 41/43 and 70/72 failed guest runs remain retained, exact
    historical triggers unasserted. I129-I131 preliminarily verified; native/hardware/AT/candidate
    obligations remain. Owner authorizes needed Mac installations; no remote mutation yet. Both
    VMs stay running, G: untouched, no stable publication/human GO. NO-GO (E-I129, E-I130, E-I131).

250. V12 native sparse/hard-linked/mixed data passes six cases without skips per Windows 26300,
    Ubuntu 26.04.1/ext4 and owner macOS 27.0.1/arm64 lane: eighteen executions. Actual Core/count/
    headless quick-view/32 marks/focus and refresh reconciliation retain exact logical totals and
    complete labels. Native before/after snapshots match all 1,248 rows, twelve sparse files and
    twelve hard-link groups. Full request/output ZIPs/manifests/XML/observations and owned fixture/
    process/temp cleanup independently verify; proof SHA-256 `854bbe2a2246df17913718166ef75a0c402a45b54274260eeb1324762d8affaa`. Mac uses its actual RID
    producer from 826 source exports independently matched to Git; x64 lanes retain original
    seven production DLLs. Original harness failures remain retained. Mac transport timed out;
    saved successful result retrieved after owner confirmation, exact interruption cause unknown.
    No production change/new issue; 109/131 remediated and 24/26 steps open remain. Both VMs stay
    running, G: untouched, native UI/hardware/AT/candidate remain, no human GO. NO-GO (E-V12-N1).

251. Subsequent 545d627 macOS CI exposes I132's early Escape focus checkpoint (1000 versus 0),
    raw artifact/server digest/full case inventory retained. Held actual-production continuation
    shows one controlled old-observer failure/three controls; cancellation timing corrects that
    observer, not production. Two forced before/after answer controls and all nine quick-search
    cases pass. Initial namespace compile and long-temp Git setup failures retained; unchanged
    build passes short-temp setup control/full App 322 with 21 skips, owned temp absent at
    22:26:43 UTC. Independent working proof SHA-256
    `567b8020982b2a5a966f69c73d25fbc04215c360dd9aa581b2ff4d2de5b99e28`.
    Exact historical CI timing unavailable. Test-only 5a11100 is pushed; clean CI in progress.
    110/132 remediated, 24/26 release steps partly/fully open. No candidate or human GO (E-I132).

252. Clean correction 5a11100 CI 37240337432 passes all four required jobs; three tag/manual package
    jobs are declared skipped. Four downloaded artifacts match server SHA-256/bytes/source identity;
    six complete TRX inventories match test IDs/outcomes/skip reasons and each full App inventory
    matches all 343 host case names. Nine quick-search cases pass without skips per Windows/Ubuntu/
    macOS inventory; full App totals are 328/15 declared skips on Windows and 300/43 on each Unix lane.
    ARM64 full App 328/15 and package startup/drawing/installer checks pass; no per-case TRX there.
    Independent CI proof SHA-256
    `6b50257c52ce896423855a40da26739856fe5440cf7f28ac89dd103a01dc431f`.
    Owner confirms Mac SSH access, but two fresh agent connections time out before login and LAN
    neighbor resolution is unavailable; owner asked to keep it awake/connected and reconfirm. No new
    Mac test/device access is claimed. Both VMs stay running; G: untouched. Progress remains 110/132
    remediated, 24/26 checklist steps partly/fully open, no candidate/human GO, overall NO-GO (E-I132).

253. V13 follow-up reproduces I133: three independently encoded/decoded five-byte single-compression
    fixtures falsely match minimum 100/maximum one in both initial/narrowed searches (twelve failures,
    six unfiltered controls). Baseline 1e5416e uses unchanged fdb17b4 production DLLs. Unknown-size members
    now exclude with a typed original-location reason; only Core.dll changes in the repeated probe.
    Twenty Core/four headless Find cases and all eighteen corrected observations pass. Full Core is
    the exact disjoint 819+1 case union: 774 pass/46 declared skips; full App 324 pass/21 skips.
    Source/binary/fixture pins and seven owned temp cleanups independently verify; proof SHA-256
    `402e810567a4ba7fded37041231354182b98173975e4065743e29bac1ea78620`. Failed GPG/space/Git-precondition/
    cleanup attempts remain failed and retained. Clean committed-source CI/Windows/Ubuntu checks are next.
    Mac is owner-deferred; one minimal Bonjour discovery found no address. Both VMs stay running, G:
    untouched/HOLD. Progress 111/133 remediated and 24/26 release steps partly/fully open; NO-GO (E-I133).

254. Clean 578a0ed CI 37244741557 passes all four required jobs. Four server artifact digests/six
    full TRX inventories independently verify: all twenty new Core cases pass in Windows and all
    four affected Find cases pass in each Windows/Ubuntu/macOS App inventory. ARM64 Core/App/package
    start/installer checks pass with log totals; no ARM64/Unix Core per-case inventory claimed.
    CI proof SHA-256 `85722d923dff8b8da830728c607b907fb240b2ba9c92ec83bcd0367e11b58451`.
    SDK-free Windows 26300 and Ubuntu 26.04.1 each pass all 24 affected cases without skips. All
    1,262/687 payloads, 1,263/688 ZIP members, sixteen canonical source exports per lane, exact case
    inventories and owned process/temp cleanup independently verify. First Windows cleanup observer
    counts itself; first Ubuntu observer cannot read one proc executable. Both remain failed/retained;
    independent PowerShell/read-only root censuses verify cleanup without altering tests or system
    settings. Guest proof SHA-256 `3b6f18c1047905dca7ea8ebe3291197d6e226626c6c67a5ce5b9eee6d499018f`.
    Mac remains owner-deferred, both VMs running, G: untouched/HOLD. Other formats/native/candidate
    remain; 111/133 remediated, 24/26 steps partly/fully open, no candidate/human GO, NO-GO (E-I133).

255. Wider V13/V10 corpus independently checks thirteen formats/fixtures using actual clean 578a0ed
    Core/Archives/Recovery DLLs. All seventy-eight file-member name/size initial/narrowed controls pass,
    including zstd unknown-size exclusion and frozen relative scopes; search creates no scratch or
    nested matches. Fifty-five content byte/hash controls pass. Two empty 7z members (solid/non-solid)
    are falsely Protected and cannot open, despite independent unencrypted listings/empty extraction:
    I134, must fix. Baseline proof SHA-256 `94c229eb4d0a0bc1987443e4ce67dba7e1f3eb16e4de15f856d5317d16ebb58f`.
    Failed private generators/aborted probes remain retained. Remediation/revalidation continues;
    source/fixture/DLL identity retained, native/candidate remain. 111/134 remediated; 24/26 steps open;
    both VMs running, Mac deferred, G: untouched/HOLD, NO-GO (E-I134).

256. I134 now identifies an empty 7z member only from explicit pinned HasStream metadata; missing
    metadata preserves protected refusal. Four independent empty/encrypted/header-encrypted fixture
    regressions pass. Same thirteen archives/private probe pass all 78 file-member search/57 byte/hash
    content controls, with only Archives.dll changed. I135 fixes fixture disk spelling after nine Core
    and one Find initial-search identity failures under default TEMP; no assertions/product behavior
    are weakened. Same-default-TEMP affected Core 98 pass/two declared benchmark skips and Find 4/4
    pass. Working source/fixture/DLL/full-case pins independently verify; proof SHA-256
    `f836cffe030e58578f12fe6888d67d0083ef35eec55a508b912c7fb03442172c`. Initial private proof's copied
    summary count remains retained and is superseded by derived counts. One intervening namespace
    build failure also stays retained. Opt-in search benchmark passes: 50,000-file name search 88 ms,
    content 3,694 ms/46 MiB allocated, 200 MiB content 83 ms; cancellation 9 ms after request/incomplete
    label. Exact TRX/DLL/owned C: cleanup retained; shared profile does not qualify physical V16.
    Clean CI/native next; 113/135 remediated, 24/26 steps open. Both VMs running, Mac deferred,
    G: untouched/HOLD, no candidate/human GO, NO-GO (E-I134/E-I135).

257. Clean 3caf480 CI 37247589861 passes all four required jobs. Four server artifact digests/six
    full TRX inventories independently verify: all 67 affected Core cases pass in Windows and all
    four Find cases pass per Windows/Ubuntu/macOS App inventory. Complete Windows Core 777/47 skips,
    App 330/15; each Unix App 302/43. ARM64 Core/App/package start/render/installer checks pass with
    log totals; no ARM64/Unix Core per-case TRX claimed. CI proof SHA-256
    `7f2fdb9d6aadc85df4bed233f7144cc03526460b7a61a6b15d522bbd57c0e4e4`.
    Both SDK-free Windows 26300 and Ubuntu 26.04.1 pass all 71 archive/Find cases without skips.
    All 1,268/693 payloads, 1,269/694 ZIP members, eighteen canonical source exports/four byte-exact
    fixture Git blobs per lane, exact native/host case inventories and owned process/temp cleanup
    independently verify. Corrected initial and post-bootstrap observers pass; native proof SHA-256
    `8f28aed0f02ec60bc234aefd40cbf2f53bfcf190c17ea670e7cd5c35bdd9145b`. Original failed tests/private
    attempts remain retained. Register status-prefix audit confirms 113/135 remediated and one Closed;
    I06/I16/I17 remain partial and are excluded. 24/26 steps partly/fully open, both VMs running,
    Mac deferred, G: untouched/HOLD, no candidate/human GO, NO-GO (E-I134/E-I135).

258. Independent xorriso ISO/Joliet and genisoimage UDF images plus all six complete RAR entry
    points pass 54 search/72 reverse-forward byte-hash controls. Missing middle part 03 exposes I136:
    one listing/six searches lack warnings, while affected content refuses. Baseline proof SHA-256
    `142e5194628ccafbb873e92717f06775daa68b30f3d262de92001c429e8317fc` verifies source/Git/input/DLL pins.
    Nine new baseline controls pass/one fails; working gap discovery reports warnings before entries.
    All ten new controls, affected Core 108/two explicit measurement skips and Find 4 pass. Identical
    corpus/negative content outcomes persist; all seven warnings pass with only Archives.dll changed.
    Working proof SHA-256 `595cd9a9c47a35536ad6df2526dd4649024ad9ccdbb5013844d9caf0ab5b6c18`.
    Failed private compiler/Counter/skip-category assumptions stay retained. Clean CI/native next;
    114/136 remediated, 24/26 steps open. VMs running, Mac deferred, G: untouched/HOLD, NO-GO (E-I136).

259. Clean a1c265f CI 37250443369 passes all four required jobs. Four server digests/six complete
    TRX inventories verify: all 77 affected Core cases pass in Windows and four Find cases pass per
    Windows/Ubuntu/macOS App inventory. Complete Windows Core 787/47 skips, App 330/15; each Unix
    App 302/43. ARM64 Core/App/package start/render/installer pass with log totals; no per-case ARM64/
    Unix Core TRX claimed. CI proof SHA-256 `803da2f98b5ee8993d1fb31708fc2962662c8c3d9756b19eb19a6d7c308f3e1b`.
    Both SDK-free Windows 26300 and Ubuntu 26.04.1 pass all 81 cases without skips. All 1,271/696
    payloads, 1,272/697 ZIP members, twenty-one canonical sources/ten raw fixture Git blobs per lane,
    native/host cases and initial/post-bootstrap process/temp cleanup independently verify. Native
    proof SHA-256 `d1f1eacdd726337aed4a7928976a55f4aa6bd0ff57da872338c99af7c92906e1`. Failed attempts remain retained. Register
    audit confirms 114/136 remediated and one Closed; 24/26 steps partly/fully open. Both VMs running,
    Mac deferred, G: untouched/HOLD, no candidate/human GO, NO-GO (E-I136).

260. Owner restores Mac availability. Trusted SSH at the known address verifies macOS 27.0.1/
    26A434 arm64, benny/UID 501; no network sweep/global installation/system change. Clean
    0bc1b66 CI job metadata shows all four required jobs successful; source/test/build/workflow
    paths are unchanged from a1c265f. An isolated verified full Git export produces self-contained
    osx-arm64 Core/App tests. All 77 archive and four Find controls pass without skips; five native
    device/descriptor/sector/topology controls pass. Full recovery session fails at the process
    admission guard, with zero device opens. Source image remains SHA-256
    `19f74a085272a8080035ffb5a649b8b53ae8fe1cdcd323555c89037293a10819`; device is detached.
    All 839 exports, 1,512 payloads/1,513 input members, exact cases/output pins and independent
    tracked/path process/temp cleanup verify. Proof `8db9e519fe0ab482210973c6f67eba0a8ccd3080c8d659e27ed187d3d98529d5`.
    First E: out-of-space build and private diagnostic reference failure remain retained; neither
    executed FileCat on the Mac. Original session failure remains failed (E-V09-M1).

261. Unchanged-production Mac admission diagnosis returns census true: one dotnet match and
    three unknown managed identities. Native ps identifies Visual Studio ServiceHub and confirms
    one unknown entry is Z/defunct; PID 0 has no managed executable identity. No Mac task stopped
    or interlock bypassed. I106 remains open. Noninteractive sudo requires the Mac password.
    A reviewable eight-second fs_usage preflight on one ordinary-user synthetic control PID is
    staged as `~/FileCatReleaseValidation/TracePreflight-20261005.command`, not executed. Owner
    must close Visual Studio and launch it with local administrator authentication; actual trace/
    authopen/native/candidate qualification remains. Progress unchanged: 114/136 remediated,
    one Closed, 24/26 steps partly/fully open. VMs running, G: untouched/HOLD, NO-GO (E-V09-M1).

262. Owner reports the trace preflight run under sudo and Visual Studio closed, then expressly
    authorizes needed Mac process termination. Native identity checks still find IDE PID 1743
    and ServiceHub PID 1912; bounded SIGTERM stops both. Unchanged production now returns null,
    no FileCat matches and three unknown kernel/defunct identities. Complete native BSD records
    and installed SDK headers establish the kernel/zombie distinction. I137 adds a narrow native
    exclusion with 17 adverse decoder controls; failures/short/mismatched records remain unknown.
    Host affected 40/11 declared skips and full App 341/23 declared skips pass, all case/source
    pins verified. Owner synthetic trace bytes/capture/cleanup verify, but encoded offsets/thread
    identity and whole-process/child/loss calibration remain. Proofs `b16d7d79ef4f0dda312e015a85625973ad7d82e8c620f24e7bed468f3b96d195`
    and `41c09385891b104968679f98fdffbd75f314f7fd452bcaa3470bd7b81d38cda1`.
    Clean committed Mac/CI revalidation pending; broader I106 remains open. Progress 115/137
    remediated, one Closed, 24/26 steps partly/fully open; VMs running, G: HOLD, NO-GO (E-I137).

263. Clean f623297 passes four required CI jobs. Four server digests/six full TRX inventories
    verify, including all 51 affected cases and all 19 census cases per available App inventory.
    Physical Mac passes 44/7 Windows-only skips and five native controls. All 843 Git exports,
    1,516 payloads/1,517 input members, output pins and process/temp/device cleanup verify.
    Recovery admission now opens the device once, then whole-file driver incorrectly chooses
    partial frag-a.bin. FileCat reports CompletedWithIssues/lost bytes correctly; independent
    16,384 generated bytes plus 24,576 zero bytes match. I138 restricts positive selection to
    Recoverable and uses shared finished-state wait, preserving Completed assertion. Host
    affected 40/12 skips and Core jobs 10/0 skips pass; native successor/CI pending.
    Proofs `eb62953c52c676aaa540f1e10d93d7a038cc4a6a97793bd8b011142c406da36d`,
    `55f9c2299cd136ad40d1661265f4e2c8a8a2b256d28fa0e5ca6cd14c364a8234` and
    `8ddaa276da1d304c1cd968bb2e4bd2558b954897485c7429b5cfbcbcd9b5e6a5`.
    I137 verified preliminarily; I138 remediated test-only. Progress 116/138 remediated, one
    Closed, 24/26 steps partly/fully open. VMs running, G: untouched/HOLD, NO-GO (E-I137/E-I138).

264. Clean 593583e preserves production/build/workflow source and corrects only I138's driver
    plus records. Physical Mac full recovery session passes 1/1 without skips: scan, viewer-reader
    content, completed copy of tiny.txt, 70-second save wait and close. Independent 60-byte
    generator/hash matches; writable source hash remains unchanged and device detached.
    All 844 Git exports/1,196 payloads/1,197 ZIP members, output/case pins and independent
    process/temp/device cleanup verify. Four required CI jobs pass; four server digests/six full
    TRX inventories verify. Proofs `ac8adc8f5ba7628b40efd696712683494e46470759e3fc5daa45ae4ff6b6070e`
    and `b83731a2763a6d41b4f257b61964223ffc63a21ed82a860beda6a7540b891fd8`.
    I138 verified preliminarily, not Closed; original failure retained. Owner-authenticated v2
    trace calibration staged, not executed: fourteen seconds, two ordinary-user PIDs, three
    native TIDs, eighteen size/offset controls including sparse offsets above 4 GiB. No devices
    opened. Source/launcher/native receipt and syntax pins verify; request
    `7f0dc6e2dd622b381836e129a4d0cb83e2a578980b7410bbd0cf47b284644aae`.
    SSH sudo still needs password; stop for owner to launch Mac calibration. Full tracing/
    authopen/native/candidate remain. Progress 116/138 remediated, one Closed, 24/26 steps
    partly/fully open. Both VMs running, G: untouched/HOLD, no candidate/GO, NO-GO (E-I138).

265. Owner executes v2 fourteen-second Mac calibration. All 18 controlled pread/pwrite events
    match operation/bytes/FD and three native TIDs across the selected parent/child; six known
    sparse ranges match independent generated bytes, each file allocating 131,072 bytes.
    All owned controller/parent/child/trace processes are absent. All 18 positional offset
    checks fail, including above 4 GiB; no ad-hoc decoder is accepted. No loss marker observed
    does not qualify zero loss, future-child capture or a full source trace. Exact source,
    transport/output/byte/operation/cleanup pins verify; proof
    `0afe6715a57a4b46775e362cbd89eb56e5cad70910c90793dd5977b41ba3b82c`.
    Pinned official source guides the next controls without claiming installed-binary build
    attribution. Docs-only 161c488 CI passes four jobs/three package skips; full case evidence
    carries its original 593583e provenance (E-V09-M2).

266. Native coverage-control staging v3 fails three signed comparisons; fresh v4 corrects
    these but installed clang 15 cannot link the default macOS 27 SDK. Both failures retained.
    Fresh v5 explicitly uses existing SDK 15.2, zero warnings/arm64, without system changes.
    Six whole-file dry-run byte controls, four native identities/shared and reused FDs, all
    26 source/binary/output/transport pins and owned cleanup independently verify; proof
    `0d31888973df68332281ce1918c2e1fadf72f9e629e63ac97b433b9d99f4c170`.
    Four six-second future-process/name/child/FD/burst/mapped-write captures staged as
    `~/FileCatReleaseValidation/TraceCoverage-20261005-v5.command`, not executed. No device or
    actual FileCat app opens. Stop for owner-local sudo password; full tracing/authopen/native/
    candidate remain. Progress 116/138 remediated, one Closed, 24/26 steps partly/fully open;
    both VMs running, G: untouched/HOLD, no candidate/GO, NO-GO (E-V09-M2).

267. Docs-only 63e7af8 CI fails native ARM64 App at the Markdown close-after-read entry
    checkpoint; 346 pass/one failure/17 skips, other three lanes pass. Failed log and three
    server digests/six complete inventories verify. I139's private four-worker gate reproduces
    the entry timeout; only a dedicated worker changes, and the same gate passes with the
    original deadlines/lifetime/bytes assertions. Eight affected host cases/full App
    341/23 declared skips pass with exit zero. One-worker setup hang/two owned process cleanup
    and the first full-suite supervisor timeout stay retained. Historical CI scheduling state
    unknown. Independent host proof SHA-256
    `c2c47000c4410e79b24db5af93ab9060a293fbfadba393f83eedcef798fb01ba`.
    I139 preliminarily remediated; successor CI pending. Progress 117/139 remediated, one Closed,
    24/26 steps partly/fully open. Both VMs running, G: untouched/HOLD, no candidate/GO (E-I139).

268. Clean 6509eff CI 37301404065 passes all four required jobs. Windows App 347/17 skips,
    Ubuntu 319/45, macOS 321/43, each 364 cases; all 24 affected available-inventory cases pass.
    Native ARM64 App 347/17 skips plus package startup/drawing/installer compilation pass;
    no ARM per-case TRX artifact exists. Four server digests/six complete inventories verify
    (private proof 505e367a31d2d3e786097885ca73bb4bc227ec3c009260413cabe2ea970a9831).
    Three tag-only package jobs skip. I139 verified preliminarily; physical/candidate remain.

269. Owner v5/v6 controls execute. V5 verifies 432 selected calls/24 files/72 pins/seventeen
    owned absences and shows parent-name/shortened-name omissions. V6 verifies 233 known calls,
    five processes/seven TIDs, all eighteen true 64-bit sparse offsets, six files/six ranges,
    46 pins/nine owned absences. Three raw mmap/msync pairs omit backing FD/offset; no packed-FD
    inference is made. Finite loss-marker absence does not qualify whole-source tracing.
    V7 misses a manifest member before execution; fresh v8 stages all 1,196 payload pins.
    Actual v8 refuses admission before opening any source; 68 pins/twelve owned absences,
    unchanged source/detachment and 318 flavor-2 plus 318 flavor-6 EPERM queries verify.
    Its root-origin cleared groups are observed, not proved as the sole cause (E-V09-M2/M3).

270. V9 stages all 1,196 payload pins/25 transport inputs and preserves ordinary SSH UID 501,
    GID 20/account groups. Actual session still refuses census admission, zero source opens;
    77 retained pins/fifteen owned absences/source/detachment verify. Four executable reads
    of /usr/bin/sudo fail EACCES; independent ordinary-user read control confirms mode 04511,
    errno 13. The same production census returns false after launchers exit. Cleared groups
    are not a sufficient explanation; sudo is a concrete conservative admission blocker.
    First detached-harness staging SSH timeout is retained. Owner restores connection; fresh
    v10 verifies 1,196 native payload pins/25 transport inputs/six whole files/four owned
    control absences. Detached sudo/nohup launcher checks sudo absence before recording,
    leaving production guard unchanged. Ordinary watcher armed; owner-local sudo is pending.
    No safeguard change/new issue/qualification claim. Progress 117/139 remediated,
    one Closed, 24/26 steps partly/fully open. VMs running, G: HOLD, no candidate/GO (E-V09-M3).

271. Owner v10 fails fifteen-second sudo-exit preflight before recorder/App/device starts:
    sudo -b retains its elevation monitor. Cleanup rejects framework Python's rewritten
    executable path; identity-checked manual watcher SIGTERM/owned-command absence and
    seven native records verify. V11 separate-session double fork lets the elevation parent
    exit. Harmless UID-501 control confirms parent 1/native session/group, zero exit and
    detached child absence, without App/device/trace. Fresh 1,196 native payload pins/26
    inputs/six complete files/four control absences verify. Cleanup records/matches actual
    process identity and launch time. Ordinary watcher armed, owner-local sudo pending;
    no guard change/new issue/qualification. Counts and NO-GO unchanged (E-V09-M3).

272. V11 sudo-exit preflight passes; root-only ready marker stops ordinary watcher before
    App/device starts. Recorder-only failure, 25 pins/three owned absences verify. V12
    atomically publishes complete UID-501/mode-0600 marker; staging 1,196 native/26 inputs/
    six files/four control absences and harmless detachment verify. Actual session passes
    1/1, exact generated 60 bytes, 70-second save wait, source unchanged/detached/temp clean.
    Supervisor identity refusal is timestamped before actual App startup, not proved as an
    exit race; worker/recorder continue. Scoped owner result-access seal enables retrieval.

273. V12 104 retained pins/18 tracked plus fifteen derived child absences verify. Raw
    2,669,238 events/115.488 seconds and 430 known before/after calls/twelve files match;
    loss markers absent, original recorder command exit uncaptured. Actual App 33 native
    thread births/fifteen diskutil syscall-observed children verify. Native source probe
    FD 164 reads nothing; recovery FD 140 reads eight times/264,192 bytes, two size/count
    ioctls, closes; no writes/truncations/aliases/forks while open observed. Service count
    one does not erase two native opens. All 79 shared nonanonymous mappings have initial
    PROT_READ but missing backing FD, so full source-write qualification remains pending.
    Privileged provider control staged, ordinary inventory needs local sudo; no SIP change.
    09e42e7 CI 37309749235 four jobs/digests/six inventories/24 affected cases verify after
    retained connection failures. Counts remain 117/139 remediated, one Closed, 24/26 steps
    partly/fully open. VMs running, G: HOLD, no candidate/GO (E-V09-M4).

274. Owner root syscall-provider inventory returns header-only exit zero with explicit SIP
   failure. Seven pins/controller absence/no capture verify. Process-level staging SDK and
   page-alignment failures stay retained. V4 finds probes but fails D numeric compilation
   before any mapping; 25 pins/three absences/entirely zero 32-KiB file verify. V5 passes
   ordinary/root controls: worker/recorder exit zero, three known FD/offset/address mappings,
   complete generated bytes, 32 pins/three absences, SIP enabled. Actual App mappings and
   broader source-write/helper qualification remain pending (E-V09-M4). G: untouched/HOLD.
275. Latest docs-only 34112ac CI 37315230588 has one Mac viewer Markdown/close-while-held
   entry timeout; other three required jobs pass. Four server digests/six full inventories
   retain the failure. Avalonia's callback timer starts in its constructor, before page
   detection. Four delayed-initialization page timeouts/four controls reproduce; explicit
   page availability passes all eight, including exact pre-arm placement. Public test-only
   remedy and eight actual host/full App 341/23 skips pass; 45 pins/full inventories verify.
   Original assertions/deadlines remain; clean successor CI pending, no candidate/GO (E-I139).

276. Clean e7a1e7e CI 37321377008 passes all four required jobs. Four server ZIP digests,
    six complete TRX inventories and all 24 affected viewer cases verify. App Windows
    347/17 skips, Ubuntu 319/45 and Mac 321/43 each total 364. ARM64 App 347/17 skips,
    startup/drawing and installer compilation pass by logs; no per-case ARM TRX/physical
    qualification is invented. Three tag-only package jobs skip. I139 remains remediated
    preliminarily, not Closed; original Mac/ARM failures and setup observers stay retained.

277. Owner v13 combined Mac capture passes actual recovery 1/1, exact generated 60 bytes,
    70-second wait, source unchanged/detached/temp clean. Byte-identical alias/1,197 native
    payload pins and 117 retained pins verify; all worker/recorder/decoder exits zero, no
    supervisor failure/forced cleanup. Raw 3,171,197 events/136.463 seconds, 430 I/O controls/
    twelve whole files/33 native App thread births and future-process mapping controls match.
    All 221 library mapping pairs uniquely match raw records; all 77 shared mappings have
    observed backing FDs/offsets, 75 filesystem paths/two shared-memory origins. Source FD
    166 probe reads nothing; FD 140 has eight reads/264,192 bytes/two size queries, no
    observed writes/aliases/forks/mappings while open. Forty-three unmatched private maps
    precede source opening. Twenty-one tracked/fifteen derived children/one attachment
    daemon absences verify. Independent verifier assumptions/failures remain pinned; native
    outputs unchanged. Broader helper/authopen/topology/native/candidate qualification open.
    Counts remain 117/139 remediated, one Closed, 24/26 steps open. VMs running, G: HOLD,
    no candidate/GO (E-V09-M5).

278. Prepare native Mac authopen refusal using byte-identical clean 593583e Recovery/Core
    DLLs and a private self-contained arm64 wrapper. All 197 staged pins verify. Two actual
    ordinary direct controls over regular file/raw owned image pass fourteen independent
    ranges including unaligned 5-MiB and EOF checks; returned FD read-only/closed, source
    unchanged/detached, seven owned processes absent. Installed authopen signature/hash
    and harmless detached launcher verify. Local CRLF/native LF verifier assumption remains
    retained; no native input/result changed. Concrete local refusal launcher is staged;
    owner sudo and native dialog cancellation remain required. No actual authopen or device
    permission change yet (E-V09-M6). Separate docs-only 302f63a CI 37328413504 passes all
    four jobs/digests/six complete inventories/24 affected cases; ARM64 log limits retained.
    Counts/VMs/G: HOLD/NO-GO unchanged.

279. Execute native Mac authorization twice against the pinned 593583e component. V1
    owner approves the separate dialog; helper opens read-only/returns a source, so the
    decline-mode wrapper fails its expectation (-6). Original failure/shortened raw trace
    remain retained; source bytes/detachment/51 pins/seven absences verify. Separate fresh
    v2 owner cancels: not-approved OperationCanceledException, no source/no timeout,
    clean worker/recorder/decoder/cleanup exits. Two attributed source opens fail EACCES;
    raw 1,502,267 events/125.956 seconds, 42 retained/197 input pins and seven absences
    independently verify. Finite loss-marker absence is not full-source/zero-loss proof.
    Corrected observer name/lifetime/attestation-label assumptions remain retained; native
    inputs/results unchanged. Fresh approval/returned-FD/seven-range case is staged and
    independently verified, awaiting local authentication (E-V09-M6). Docs-only 10d4e62
    CI 37331762053 passes all four required lanes; four server digests/six full inventories/
    24 affected cases verify, ARM64 log limits retained. Counts/VMs/G: HOLD/NO-GO unchanged.

280. Fresh native Mac approval case passes actual received-FD O_RDONLY and immediate
    EBADF after disposal. All seven returned ranges independently match the golden image;
    six bounded, aligned native preads total 5,250,560 bytes. Native direct/helper EACCES
    then helper read-only success verify. Clean worker/recorder/decoder/cleanup exits,
    42 retained/197 input pins, unchanged source/detachment and seven absences pass.
    Raw 1,976,923 events/125.978 seconds retain finite loss-marker/mapping limits; no
    source-FD writes/truncations observed. Original early-permission/syntax observer
    failures retained; native permissions/results unchanged. Native component approval
    and refusal now verify, while replacement/removal/full-helper/drawn workflow/candidate
    remain (E-V09-M6). Separate docs-only f35d4da CI 37342876513 passes all four required
    lanes/four server digests/six full inventories/24 affected cases; ARM64 log limits
    retained. Counts/VMs/G: HOLD/NO-GO unchanged.

281. Prepare the Mac pending-authorization device-binding case using byte-identical clean
    593583e Recovery/Core DLLs and a metadata-only wrapper. All 200 input/46 retained pins,
    native C/Python ABI and four ordinary metadata controls verify. Equal-size owned images
    reuse the same raw path with different inodes; golden source and one-byte replacement
    hashes independently verify. Both images remain unchanged/detached; five owned process
    absences and harmless detached launch pass. Preserve the pre-staging safety revision;
    partial attachment/permission cleanup now tracks exact owned identities. Pending native
    authorization has not run. Staged launcher needs local sudo, held dialog until the verified
    replacement marker, then owner approval (E-V09-M7). Docs-only ce28282 CI 37346405163
    passes all four required jobs, four server digests/six full inventories/24 affected cases;
    ARM64 log limits remain. Production source unchanged; no new defect or closure inferred.
    Counts/VMs/G: HOLD/NO-GO unchanged.

282. Owner runs and approves the held-authorization Mac binding case. Actual 593583e
    component returns the equally sized replacement inode 849 instead of selected 845;
    successful helper source-open occurs 19.208 seconds after verified replacement readiness.
    Probe has zero source-content reads; read-only/closure, source hashes/detachment, all
    88 retained/200 input pins and eight owned absences verify. Raw 2,142,873 events/125.982
    seconds retain finite coverage limits; all recorded commands exit zero. Stop the defective
    path and register I140. Guard received handle and current path with the native device/inode
    captured before opening; dispose mismatches before constructing a source. Working Mac
    six new real-file cases pass within affected 19/4 skips, 321 payload pins and owned test
    process absence verify. Windows affected 7/16 native skips compiles. Preserve verifier
    walltime/help-command failures and exact working producer. Commit correction; clean
    CI/native authorization revalidation next (E-I140). Progress 118/140 preliminarily remediated,
    one Closed; 24/26 steps partly/fully open. VMs running, G: HOLD, no candidate/GO, NO-GO.

283. Validate committed I140 source 8f75856 independently: 852 exported source pins,
    clean Mac 19/4 declared skips/all six new cases, 321 payload pins and process absence
    verify. Raw source mismatch is only CRLF/LF; original false receipt remains retained.
    Fresh byte-identical binding producer verifies 200 input/46 retained pins, four native
    metadata controls, equal-size same-path/different-inode rehearsal, golden hashes,
    detachment/five absences; v2 SSH setup fails unavailable interactive authorization; no source opens/replacement
    occurs, unchanged sources/detachment, 200/71 pins/four known absences verify. Fresh
    local-Terminal v3 controls/pins/cleanup verify; held approval remains pending. Separate root CLI
    account authenticates without saving the credential. Preceding 300c52a CI passes four
    jobs/server digests/six inventories. I140 CI has three passing lanes/one ARM64 App
    failure; original log/three server digests/six inventories verify. Register I141:
    three controlled original-fixture failures expose ordinal read targeting. Test-only
    feeder targeting passes all eight held cases/four new controls, affected 20 and full
    App 345/23 declared skips, exit zero. Clean successor CI/native ARM64 remains. Progress
    119/141 preliminarily remediated, one separately Closed, 21 remaining remediation;
    24/26 checklist steps partly/fully open, all 24 campaigns still need final qualification.
    Both VMs stay running, G: untouched/HOLD; no candidate/human GO, NO-GO.

284. Independently verify clean 6197592 CI 37359106547: all four required lanes pass,
    four server ZIP digests/six full TRX inventories/60 affected viewer cases verify.
    ARM64 App 351/17 declared skips, native startup/drawing and installer compilation
    pass by logs; no ARM64 per-case TRX/physical qualification is claimed. Corrected
    I140 local-Terminal v3 and owner-requested v4 each reject the approved equal-size
    replacement before source construction. Native inode changes, read-only helper
    opens after ready markers, inferred received FD fstat/close/no size ioctls, 200/88
    pins per capture, unchanged detached images/eight absences and clean command exits
    verify. Preserve inference/finite trace/mapping/native/candidate limits and prior
    failures. Prepare fresh unchanged-device approval on the same clean component:
    197 inputs/20 retained pins, two direct controls/seven independent golden ranges
    each, unchanged detached image/four absences/launcher verify. Local host extraction
    first fails on a missing stat import before writing any member; a separate host-only
    resume completes verification without rebuilding or rerunning native preparation.
    V2 times out despite owner-reported approval; raw two EACCES opens, receive EOF after
    89.974 seconds/runtime SIGTERM, 54 retained/197 input pins, source/cleanup/seven
    absences verify. Cause remains unknown. Fresh v3 prep verifies the same controls;
    owner reports approval and component rights/seven ranges/closure/source/cleanup
    pass; independent trace analysis remains pending.
    Restore malformed historical E-I19-V1 index row to its original f87ad32 provenance.
    Counts unchanged: 119/141 remediated, one Closed, 21 remain for remediation;
    24/26 steps partly/fully open. VMs running, G: HOLD, no candidate/GO, NO-GO.

285. Verify successful fresh I140 unchanged-source v3 approval independently: actual read-only
    F_GETFL/closed EBADF, seven golden ranges, six native aligned reads/5,250,560 bytes,
    55 retained and 197 input pins, source unchanged/detached/seven absences and all
    recorded worker/recorder/decoder/cleanup exits zero. Raw 3,820,309 events/125.999
    seconds retain six private mapping backing gaps/three nonreturning starts/finite
    loss limits; whole-source/native workflow/candidate remain. Preserve approved v2
    timeout/cause unknown. Owner authorizes autonomous Mac setup and temporary awake
    support: bounded owned caffeinate active, saved settings unchanged, restoration
    due when testing ends. Hidden benny sudo UID check succeeds, no credential saved.
    Native SSH versus desktop-agent SessionGetInfo control verifies graphical access
    only for desktop session; all 23 pins/two absences and agent removal verify. Local
    computer/browser runtime initialization fails before input; no remote-access service
    or security setting is changed. Fresh current-guard refusal prep controls/pins/source/
    cleanup verify; actual desktop-context consent driver remains to validate. Counts
    unchanged, VMs running, G: HOLD, no candidate/GO, NO-GO (E-V09-M8/E-ENV-MAC-1).

286. Construct fresh native GUI-session consent driver locally with pinned source and
    checked Python syntax. Staging SSH times out before reaching the Mac; one bounded
    read-only retry also times out. Preserve transport failure/local driver and do not
    claim staging, GUI handshake or actual refusal qualification. Ask owner to restore
    network/wake or provide current IP. Awake helper status is unknown while offline;
    prior settings were unchanged and owned-helper restoration remains due. Completed
    approval/session proofs remain valid at their recorded identities. Counts, VMs/G:
    HOLD and NO-GO unchanged; no candidate/GO (E-ENV-MAC-1).

287. Owner-restored Mac connectivity permits the fresh GUI-driver staging retry.
    Independently verify 201 native/host input pins, 21 retained pins, byte-identical
    production DLLs, ordinary gui/501 metadata/normal groups, seven golden ranges,
    read-only closure, unchanged detached source, agent removal and three absences.
    At the owner's request, arm a root restorer and temporarily set native SleepDisabled;
    preserve the absent original key and unrelated settings. Owner closes the AC-connected
    lid; four independent SSH/native-sensor checks over 62.868 seconds pass without boot/
    SleepWakeUUID change. Owner reopens it. Root restorer/caffeinate cleanup remains due
    at end; unplug/deadline restoration is armed but not yet executed or qualified.
    Actual fresh refusal is launched autonomously through gui/501; owner reports cancellation,
    ordinary component reports no source/no timeout, and independent capture analysis is
    pending. Counts unchanged, VMs running, G: HOLD, no candidate/GO, NO-GO (E-ENV-MAC-1).

288. Fresh committed-component refusal runs autonomously through an ordinary gui/501
    launch agent; owner attests cancellation. Actual UID/groups/native desktop context,
    no source/no timeout, two raw/formatted EACCES source opens, native helper cancellation
    exit 1, authorization channel close and helper wait independently verify. Component,
    recorder, two decoders and seven recorded cleanup commands exit zero. All 201 input/
    55 retained pins/source hashes, normal detachment, agent removal and nine owned
    absences verify. Raw capture has 3,064,665 events/128.003514583 measured seconds;
    four nonreturning starts/finite loss and mmap/full-workflow/candidate limits remain.
    Preserve rejected direct capture-flag edit; safer analyzer derives completion from
    raw span/recorder metadata. Preserve failed older-name close verifier and repair it
    using actual BSC_sys_close names plus explicit later pipe FD reuse. No native rerun
    or historical result rewrite. Counts unchanged, VMs running, G: HOLD, temporary Mac
    power restoration still due, no candidate/GO, NO-GO (E-V09-M9).

289. Prepare fresh committed-component held-device-removal case, without changing
    production DLLs. Recorded cross-publication exit zero, 201 host/native inputs and
    42 retained pins verify. Ordinary gui/501 regular and raw direct controls each pass
    seven golden ranges/read-only closure; desktop missing-source control refuses before
    access, no source/no timeout. Normal groups/graphic access, two agent removals, owned
    raw attachment/normal detachment, source bytes/eight tracked absences and eight command
    exits verify. Root controller retains restriction until owned nodes disappear and
    requires owner approval only after its removal cue; no content is read in removal mode.
    Actual authorization-held removal is unexecuted, awaiting owner readiness. Counts,
    VMs/G: HOLD, outstanding temporary Mac power restoration and NO-GO unchanged; no
    candidate/GO (E-V09-M10).

290. Execute the fresh owner-approved removal case on the byte-identical 8f75856
    component. Verify owned detachment/both missing paths/live ordinary helper before
    the approval cue; owner reports dialog and approval. Three uniquely linked source
    opens are read-only: two initial EACCES and helper ENOENT 75.100791 seconds after
    removal. No source/no timeout, channel FD 63 close/separate later pipe reuse,
    helper exit/reap, all recorded command exits, unchanged source/detachment/agent
    removal/nine absences and 201 input/57 retained pins independently verify.
    8,272,489 raw events span 128.007 seconds; measured duration/finite-loss limits
    retained. The pre-cue ps-format assertion failure remains explicit. Component
    reports not approved despite owner approval: real reporting defect I142.
291. Four new native I142 cases reproduce two reporting failures while unchanged
    refusal/explicit cancellation controls pass. Recheck native identity only on an
    uncancelled opener cancellation; changed/missing source reports IOException with
    the original inner failure. Corrected Mac 23/4 declared skips, host Core 7/20
    native skips and affected App 25/9 native skips pass. Failed v1 empty-container
    observer retained; fresh v2 verifies and removes only the empty exact owned
    nonsymlink container. 321 payload/six retained pins per native run, process/temp
    cleanup verify. Committed/native/CI validation pending; no additional human Mac
    dialog case queued. 120/142 preliminarily remediated, one Closed, 21 remaining;
    checklist 24/26 partly/fully open. No candidate/human GO; NO-GO.

292. Commit I142 at 348cbc7 and export all 858 exact raw Git blobs with object and
    SHA-256 checks. Preserve two pre-test Git archive line-ending failures. Clean
    Mac 23/4 declared skips and Ubuntu 26.04.1 VMware 25/2 declared skips pass all
    four new native cases, payload/result pins and process/temp cleanup. Prepare
    fresh byte-identical clean component removal controls; final owner-approved
    real case verifies detachment/exact paths/live ordinary helper before cue,
    helper ENOENT 54.651673 seconds later and post-helper missing-entry stat.
    No source/no timeout, correct IOException, channel close/separate pipe reuse,
    source/detachment/agent cleanup/nine absences and 201 input/57 retained pins
    independently pass. Raw span 127.965 seconds/6,413,688 events; finite/owner
    attestation limits and transcription amendment retained. Original baseline
    reporting failure remains. CI attempt one provider acquisition failure retained;
    exact-source attempt two pending. No further Mac interaction queued. Counts
    unchanged: 120/142 preliminarily remediated, one Closed, 21 remaining; 24/26
    checklist steps partly/fully open. No candidate/human GO; NO-GO.

293. Independently seal 348cbc7 CI 37373490704 attempt two: all four required lanes
    pass, three package publication jobs skipped; four server archive digests/six
    complete TRX inventories match all definitions, unique case executions and skip
    reasons. Complete App inventories 368 each: Windows 351/17, Ubuntu 323/45,
    macOS 325/43 declared skips. Affected recovery App 31/3 Windows and 26/8 per
    Unix lane; Windows Unix Core 7/20 native skips. Physical Mac/Ubuntu four new
    native cases pass separately. ARM64 App 351/17/368, package drawing/installer
    compilation pass by logs; per-case ARM64 and Unix Core CI/physical/candidate
    limits explicit. Preserve separate attempt-one metadata, primary acquisition
    annotations, both cancelled lanes' zero test steps and 38-member log archive;
    no test/workflow relaxation or merged attempt results. Counts unchanged; no
    additional Mac interaction queued. No candidate/human GO; NO-GO.

294. Continue V12 aggregate icon audit after sealing I142. Actual unchanged 348cbc7
    production getter on ordinary Ubuntu retains 50,000 cache entries with a 43,795
    sampled queued-work peak; 261 payload/four retained pins, transport, VMX/OS and
    process/temp cleanup verify. Correct Linux/Mac admission with 4,096-entry LRU,
    nonblocking 256 waiting requests, stale entry identity and safe unpublished-
    bitmap disposal. Six new held-worker/LRU/clear/late-result/queued/concurrency
    controls and full host App 351/23 declared skips/374 pass. Clean/native/CI pending.
    Verify separate Mac power restorer's AC-disconnect restoration at 21:30:26Z:
    original absent key removed, system/custom/native sleep baseline restored and
    exact root process absent; original timed ordinary caffeinate remains. Owner
    reconfirms G: disposable; no USB touched. Progress 121/143 preliminarily
    remediated, one Closed, 21 remaining; checklist 24/26 partly/fully open.
    No further Mac interaction queued; no candidate/human GO; NO-GO.

295. Commit/push I143 at 1559933; all 861 raw Git blobs/object IDs/source archive verify.
    Clean physical Mac arm64 UID 501 and Ubuntu 26.04.1 VMware UID 1000 each pass
    7/2 declared Windows-only skips, including all six new controls; 350 payload/six
    retained pins/native XML/source/process/temp cleanup verify. The identical
    50,000-request production Ubuntu getter now has 2,538 entries/queue peak 256/
    zero waiting; 261 input/four retained pins, native transport and byte-identical
    clean App/Core references verify. Actual finite heap 608,392 bytes versus
    baseline 7,411,592; native bitmap/frame/aggregate limits remain explicit.
    After AC returns, rearm the unchanged bounded Mac controller under existing
    authorization; after native tests verify exact root PID/command/hash, signal
    its restorer and ordinary owned caffeinate. Both original/fresh system/custom
    baselines, absent sleep-disable key, runtime sleep and three owned process
    absences independently verify. Current Mac testing complete; no interaction
    queued. Exact-source CI 37379380371 pending. Counts unchanged 121/143
    preliminarily remediated, one Closed, 21 remaining; no candidate/GO; NO-GO.

296. Seal exact 1559933 CI 37379380371 attempt one: all four required lanes pass,
    three package publication jobs skipped; four server archive digests/six complete
    TRX inventories and full 374-name App multiset verify. Affected icon cases
    Windows 8/1 declared skip and Unix 7/2 Windows-only skips each include all six
    passing new bounds/lifetime/stale/concurrency controls. Full App Windows 357/17,
    Ubuntu 329/45, macOS 331/43 declared skips; ARM64 App 357/17/374, package drawing
    and installer compilation pass by logs. Per-case ARM64/physical/frame/candidate
    limits explicit. Native original failure/getter/control/source/cleanup and final
    Mac power restoration remain independently pinned. Progress 121/143 preliminary
    remediation, one Closed, 21 remaining; checklist 24/26 partly/fully open. No
    further Mac interaction queued, VMs running/G: untouched; no candidate/GO; NO-GO.

297. Queue owner-requested unattended execution and the one-time 08:40 CEST check-in;
    defer human-interaction gates and continue unblocked work. Rechecked supported
    Windows Computer Use import still exits before input. V12's aggregate picture
    audit exposes I144: eight held owned PNG requests start eight Windows workers.
    Commit/push f017a94 shared four-worker/32-waiter admission and actual process-exit
    ownership. Four identical test-DLL cases fail with baseline App DLL and pass
    with corrected App DLL; native corrected worker peak four verifies. Two extra
    queue/cancellation-race controls, affected 25/full host App 357/23 declared skips
    pass. Original compile error, post-release fixture timing failure and finite
    collector assumption failure remain retained; fixture bytes/cleanup preserved.
    Clean native/CI collection pending. Mac UID 501 SSH works; owned pinned root
    controller freshly rearmed on AC with PID 19109/12-hour and disconnect restore.
    Restoration due when testing ends. Counts 121/144 preliminary, one Closed,
    22 under remediation/validation; 24/26 checklist partly/fully open; NO-GO.

298. Preserve f017a94 CI 37384217205: Mac/Ubuntu/ARM64 pass; Windows has one
    existing device-demand failure. All four server digests/six full inventories
    verify and all six new admission cases pass. Clean Windows guest 25/25 and
    native Mac/Ubuntu 24/one Windows-only skip pass; original temp observers fail
    on 62/64 orphan runtime debugger FIFOs. Independent strict owned FIFO cleanup
    verifies types/modes/UIDs/absent producers and unchanged original tests/payloads.
    I145 applies the existing Windows no-diagnostics policy to Unix workers.
    I146's forced nine-second delay reproduces one old fixture failure/one positive;
    fixture-only threshold correction yields two positives with only test DLL/PDB
    changed. Original CI timing remains inferred; every acceptance assertion kept.
    Working affected 25 pass; full host/successor native/CI pending. Raw Git 864
    blobs/source archive/350 Unix and 354 Windows payloads remain pinned. No owner
    interaction needed/requested; temporary awake support remains restore-due.
    Counts 121/146 preliminary, one Closed, 24 under remediation/validation; NO-GO.

299. Seal I144–I146 at pushed cb85f0a: 867 raw Git/source pins; clean self-contained
     Windows 25/0 and macOS/Ubuntu 24/1 picture inventories, all six new controls,
     354/350 unchanged payloads and automatic temp cleanup without FIFO exemptions.
     Original Mac receipt-variable shadowing failure is independently retained;
     fresh corrected observer passes, without changing production. Full host 357/23
     declared skips/380 cases and affected 25 pass. Exact CI 37387554116 attempt one
     passes all four required jobs; four server ZIP digests/six complete inventories,
     380 exact App names/25 affected cases and all six new admission controls verify.
     ARM64 App 363/17/380 and package start/installer compilation are log evidence.
     Original f017a94 cleanup/CI failures and historical timing inference remain.
     Counts 124/146 preliminary, one Closed, 21 remain; Mac awake v3 remains active
     for further testing with restoration due, VMs stay running, G: untouched.
     Continue Windows native icon demand/retention work; interaction gates queued
     until 08:40 CEST. No candidate/human GO; NO-GO (E-I144–E-I146).

300. Continue overnight V12/I06 Windows icon demand: baseline 50,000 shared entries,
     512 held per-item factory loads, 5,000 late republished plans and stale 16/32/16
     replacement reproduce four failures; helper-failure/retry positive passes.
     Identical final test DLL now passes all five with only App DLL/PDB different.
     Separate 4,096 retained/256 waiting caches, four asynchronous per-item consumers,
     stale identity/retry rejection and captured size are committed/pushed 6cf17e5.
     Three additional portable controls and final full host 365/23 declared skips/
     388 cases pass; affected 16/one capture skip. Clean native and exact-source CI
     in progress. Counts 124/147 preliminary, one Closed, 22 under remediation/
     validation; no desktop/whole memory/candidate claim. Mac v3 awake root restorer
     19109/SleepDisabled observed active, restoration due. VMs stay running, G:
     untouched/HOLD; owner gates remain queued until 08:40 CEST. NO-GO (E-I147).

301. Seal I147 at 6cf17e5: 870 raw Git/source pins; clean native Windows 16/1 capture
     skip, Mac/Ubuntu 10/7 explicit skips, all eight expected new outcomes, unchanged
     354/350 payloads and owned temp/test-process checkpoints. Exact CI 37389900000
     attempt one passes all four jobs; four server digests/six full inventories,
     388 exact App case names and affected 17-case inventories verify. ARM64 App
     371/17/388/startup/installer pass by logs. One metadata network timeout retained
     before bounded successful reads; no product tests rerun/overwritten. Counts
     125/147 preliminary, one Closed, 21 remain. Start V13 pure UDF/other revisions
     and legacy RAR naming using owned fixtures; first Mac formatter produces empty
     images, but generic attachment does not recognize them. No FileCat ran; that
     attempt and its logs/images are retained before explicit raw-image-class capture.
     Mac awake v3/restorer stays active, restoration due; VMs running, G: untouched/
     HOLD, owner gates queued until 08:40 CEST. No candidate/human GO; NO-GO.

302. Pure UDF six-revision native/independent/actual component corpus passes 36
     search/132 exact member reads with all inputs unchanged. Retain first generic
     attach and writable-census timing failures; fresh read-only final native
     inventory includes event files and all six images detach. Actual legacy RAR
     corpus reveals I148, six secondary entry-point failures. Commit/push 9da5738
     primary-once/ordered siblings/gap warning fix, unchanged upstream fixtures
     and six UDF/ten legacy regressions. Identical final DLL/only archive DLL changed
     gives six baseline failures/ten positives then 16 corrected passes; original
     secondary-tool and empty-container observer assumptions remain retained.
     Affected 63/0/full Core 804/56 declared skips/860 cases independently verify.
     Clean native/CI continues. Counts 126/148 preliminary, one Closed, 21 remain;
     no desktop/candidate qualification or human GO. Mac awake v3 remains active,
     restoration due; both VMs stay running, G: untouched/HOLD, owner gates queued
     until 08:40 CEST. NO-GO (E-I148/E-V13-UDF2).

303. Seal I148's 882 raw-source/three native 63/0 inventories and exact-source CI
     37393570643 attempt one: four required jobs/four server digests/six full TRX
     inventories, 63 affected Core/16 new exact names pass. Six native PE-path names
     differ explicitly; preserve strict and successor observer failures, no case
     normalization or product rerun. Follow-up format probe exposes I149: RAR
     signature comparison cannot match. Retain initial nullable test compile failure;
     fresh baseline 8 fail/6 pass. Commit/push 313d40b full RAR 4/5 markers and 14
     regressions. Identical final test DLL/only archive DLL different gives 8 failures/
     22 positives then 30 pass; affected 77/0/full Core 818/56 skips/874 verify.
     Clean native/CI continues. Counts 127/149 preliminary, one Closed, 21 remain;
     no native drawn UI/candidate/human GO. Mac v3 active/restoration due, VMs running,
     G: untouched/HOLD, owner gates queued until 08:40 CEST. NO-GO.

304. Seal I149 at pushed 313d40b: 885 raw source blobs; clean Windows/Mac/Ubuntu
     each 77/0 including all 30 new signature/legacy/UDF controls, unchanged
     331/330/331 payloads and exact case/process/temp inventories. Four required
     CI lanes at 37394739441 attempt one pass; four server digests/six complete TRX,
     all 77 affected Core and 388 App names verify, six native PE paths retained
     explicitly. Timed-out artifact download retained before successful read-only
     resume. Actual wider legacy probe verifies 42 search/42 complete byte reads
     plus safe partial reads/refusals and unchanged pins/cleanup. Mac owned root
     restorer 19109 is verified/signaled; restoration at 00:53:09Z matches original
     system/custom preferences and runtime sleep, seven native pins/four absences
     verify. No temporary power change remains. Windows guest browsing capture
     runs without user input; independent trace qualification continues. Counts
     127/149 preliminary, one Closed, 21 remain; 24/26 steps partly/fully open;
     no candidate/stable GO. Owner interaction needs remain queued until 08:40 CEST.

305. Run and independently qualify exact 313d40b native component browsing traces:
     885 source/354 payload pins verify; process 32 starts/32 stops, 26 exact
     sequence/lifetime descendants, four allowed browse starts and before/after
     exit-code controls. File capture v1 fails before FileCat on duplicate -p;
     retained v2 uses provider file and passes. Raw 5585/native 5584 rows retain
     header/partition decoder limits; all 5519 file/64 process events decode;
     594 phase opens/249 raw paths/zero network paths/eight positive opens verify.
     Share-icon/shortcut-target case passes actual local positives. Independent
     Ubuntu endpoint capture has 118 untruncated packets, 20 in exactly two known
     TCP controls and zero additional named-endpoint packets/connections. Other
     98 packets and clock skew remain explicit. Reported loss counters zero;
     owned receiver closes/recorder and controller absent, temp/process cleanup
     and all retained pins verify. No firewall/service changes or desktop/whole
     network/protected-write/candidate claims. I16/V23/V24 wider variants continue;
     all owner interactions stay queued until 08:40 CEST. Counts unchanged; NO-GO.

306. Broaden I16 to global/inherited Git settings: exact pinned 313d40b actual
     API executes both owned input-selected filter commands, with two direct
     positives/one negative and unchanged source/payload/environment/process
     observations. Original private assembly-name observer failure retained; fresh
     v2 independently verifies five retained pins. Register I150 High/must-fix,
     correct and revalidate autonomously. 127/150 preliminary, one Closed, 22
     remain; no candidate/human GO. Owner needs queued until 08:40 CEST.

307. Correct I150 at pushed 874b7ae/52df3d7. Global/system/inherited Git settings
     are isolated to the child; repository rules remain and capability scope is
     explicit. Six original host failures correct; affected 14/3 skips and full
     App 371/23/394 pass. Identical final native test DLL fails six/passes eight
     positives before and passes 14 after, with three opt-in skips each and only
     App DLL different. Unchanged actual probe likewise blocks both external
     filters while retaining known positives/ordinary badges. All 889 source/
     354/350/350 native pins and cleanup verify; Win 14/3, Mac/Ubuntu 13/4, all six
     new cases pass. Original Ubuntu lacks Git/seven required skips retained;
     authorized Git 2.53 installation enables fresh lane. Original CI retains
     seven ARM64 NUL failures/three passing lanes/three digests/six inventories.
     Documented /dev/null correction yields successor four passing CI lanes/four
     digests/six full inventories/394 exact App names and six controls each. ARM64
     377/17/394/start/draw/installer pass by logs, no per-case ARM64 inventory or
     physical claim. Mac bounded awake wrappers absent; prior power restoration
     stands. Observer failures preserved. 128/150 preliminary, one Closed, 21
     remain; 24/26 checklist steps partly/fully open, no candidate/GO. Continue
     indirect/reparse/protected-file/native I16; owner needs queued to 08:40 CEST.

308. Reproduce I151 on actual 52df3d7: the owned .git directory symlink causes
     14 additional SMB negotiation flows/126 packets between known controls.
     Independent pcap parses all 328 untruncated packets with zero reported drops;
     ordinary badges/546 exact payload pins/process/listener cleanup verify.
     Original manifest/Windows-path observer failures retained (E-I151).
     Correct local path checks before metadata probing; indirect/native/candidate
     I16 remains open. No owner interaction or physical-source write.

309. Push I151 correction 483032a. Six identical test-DLL baseline failures correct;
     affected host 20/1 capture skip, full App 378/23 declared skips/401 pass. Clean
     Windows 21/3 and Ubuntu 14/10 pass with 891 raw source blobs/354/350 payload
     pins/empty temp/owned processes verified. Unchanged original native probe,
     only App DLL changed, yields zero additional endpoint contacts versus 14 SMB
     flows/126 packets before. Two controls/zero reported drops/receiver and root
     recorder cleanup verify. Original observer failures retained; Mac SSH upload
     fails before launch and bounded retry times out, owner gate queued until
     08:40 CEST. Exact-source CI running. Wider I16/candidate open (E-I151).

310. Independently seal I151 CI 37407598742 attempt 1: all four required lanes,
     four server artifact digests and six full inventories/401 exact App names
     pass. All 24 selected Git/browse names match; Windows seven new controls pass,
     Unix one portable positive/six declared Windows-only skips. ARM64 full logs
     384/17/401 plus startup/drawing/installer success; per-case ARM64/physical
     qualification unavailable. Mac native wake/connectivity remains queued.
     Original private UTF-8 writer failure/partial files retained and corrected;
     no native evidence rerun or overwritten (E-I151). Continue compiler provenance.

311. Pin the official Inno 6.7.1 installer and all 119 frozen compiler inputs,
     record all 122 native installed files, ship its exact upstream license and
     correct redistributed/runtime notices. Final 0646053 raw-source VMware repeat
     passes nine acquisition/tamper/collision/x64/ARM64 recipe controls; all 19
     retained hashes/source/process/registry/tool cleanup verify. Real FileCat
     ARM64 CI 37410913442 is pending; inert recipe payloads do not qualify packages.
     Original native stderr, huge observer/partial transfers and two small helper
     failures remain retained. Wider I03/I18/candidate still open (E-I03-INNO).

312. Seal 0646053 CI 37410913442: four required lanes/five server digests/six
     inventories/401 exact App names pass. ARM64 compiler recipe verifies 119
     static hashes/122 tool files, upstream license and unchanged actual publish
     inputs/setup identity. Setup bytes are not uploaded in this main-push lane;
     no binary lifecycle qualification follows (E-I03-INNO).

313. Discover I152: current Git metadata-descendant case returns a snapshot while
     14 SMB flows/126 packets occur between exact controls. Independent 157-packet
     decoder, zero reported drops, payload/process/receiver cleanup verify. HEAD
     control has no extra contact. Original decoder/writer assumptions retained
     and corrected without replacing native results. Current 129/152 preliminary,
     one Closed, 22 remaining; correct/revalidate the defect (E-I152).

314. I152 e3c99d5 bounded metadata-tree admission passes six original failures/
     seven final controls, identical 721-input comparison/only App DLL changes,
     affected 27/1 and full host 385/23/408. All raw source/native Windows 28/3,
     Mac/Ubuntu 15/16 and payload/temp/process pins verify. Unchanged private probe/
     only clean App DLL now produces zero extra endpoint contact versus 14 SMB
     flows/126 packets, ordinary badge/two controls/loss/receiver cleanup preserved.
     Initial rebuilt test DLL identity differed; strict failed observation retained
     and fresh final controlled comparison completed. Mac connectivity resumes and
     queued I151 Mac repeat passes 14/10/350 pins/cleanup (E-I151/E-I152).

315. Preserve failed e3c99d5 CI 37412907260: three required lanes pass, Ubuntu Core
     cadence assertion fails I153. Four server digests/five complete inventories/
     408 exact Windows/Mac App names/31 selected names and ARM64 log/startup/drawing/
     installer verify. No Ubuntu App or per-case Core inventory exists; failure
     remains. Writer timestamp 6.10 s/callbacks 2.04/4.04/6.07 s need controlled
     event/content reproduction. Current 130/153 preliminary, one Closed, 22 remain;
     continue autonomously without an owner interaction (E-I153).

316. Seal test-only I153 fd1d780: actual complete-snapshot observation proves the
     timestamp oracle's false rejection; two deliberate unbounded-debounce cases
     fail, restore only Core DLL and both pass/all 861 payload pins verify.
     Host 3/full Core 819/56/875, committed native Windows 3/0, Mac/Ubuntu 2/1 and
     all four exact-source CI lanes/five digests/six inventories pass. All 408
     App names/31 Git controls/three Windows watcher cases and ARM64 startup/
     drawing/installer verify; historical scheduler cause/per-case portable Core
     and ARM64 inventories stay unavailable. Original failure retained. Current
     131/153 preliminary, one Closed, 21 remain; no candidate/human GO (E-I153).

317. Commit/push I03/I18/I108 toolchain slice 78a0716: exact 10.0.401 SDK, four
     official immutable action commits, explicit hosted OS labels, persisted
     checkout credentials disabled and clean compiler/runtime/image receipts.
     Two positive/three negative controls and host build pass; complete portable/
     ARM64 TRX is enabled. All four CI receipt steps pass; full run/artifact seal
     and broader provenance/release controls remain Open (E-I03-SDK-ACTIONS).

318. Seal 78a0716 CI 37415760813: four lanes/ten server digests/four clean SDK,
     compiler/runtime/image receipts/fourteen execution inventories pass. All
     408 App names/31 Git controls/two cadence cases/77 archive cases match or
     have exact declared platform outcomes. First observer wrongly expected
     unique display names; original preserved, corrected observer retains every
     execution ID/multiplicity and explicit long-argument truncation limits.
     Wider I03/I18/I108 and candidate stay Open (E-I03-SDK-ACTIONS).

319. Discover I154 in protected actual SC/FDD broker components: environment
     startup hooks execute before Main/plan/consent. Already-administrative
     caller, no UAC/limited-caller bypass inferred. Disable startup hooks in
     protected runtime configuration; identical configuration-only and actual
     working four-mode publish/native controls verify no markers/two positives/
     all pins/owned cleanup. Helpers are stopped after six seconds; UI/healthy
     plans and committed native/CI remain pending. Current 131/154 preliminary,
     one Closed, 22 remain (E-I154); continue wider loader audit autonomously.

## Evidence invalidated by the campaign's own changes

The compiler/license/notices/installer recipe changes at 0646053 change distributed setup bytes; earlier installer qualification is historical. A pinned tool receipt does not qualify candidate lifecycle or signing.

I151 changes Windows automatic Git path admission; earlier Git path execution evidence remains historical for affected cases. Current finite source is 483032a (E-I151); no installed candidate qualification is implied.

- I150 changes automatic Git child environment/configuration. Earlier E-V24-D2
  process/file traces are historical to 313d40b for affected Git paths; finite
  exact-source filter/regression controls pass at 52df3d7. Broader indirect/reparse,
  protected-file and installed-candidate observations still need validation.

- I140 adds Unix source-entry identity admission. Earlier Mac native approval/refusal and
  ordinary source passes at 593583e remain historical. Committed replacement v3/v4 pass;
  current unchanged approval passes; refusal/removal and broader qualification remain
  (E-I140/E-V09-M8).

- I138 changes only the native recovery driver. Original partial-file session stays failed;
  clean 593583e's complete successor, exact recovered bytes/source and cleanup verify. Its
  preliminary pass does not supply whole-process source-write tracing/native/candidate evidence (E-I138).

- I137 changes only the Mac process census. Prior kernel/zombie admission refusal remains failed;
  the host verification does not establish native Mac recovery success. Rebuild and revalidate
  the exact committed Mac producer before advancing this path (E-I137).

- I136: earlier archive passes do not prove missing interior RAR-volume reporting. Seven-Zip rejects
  the owned incomplete set; FileCat has seven missing-warning failures but refuses affected content.
  Correction/identical corpus, affected host checks, four clean CI jobs and 81 cases per VM pass; other
  variants/native desktop/AT/candidate remain (E-I136).

- I134/I135: prior archive passes do not prove empty 7z protection/content truth or I133 fixture identities
  under noncanonical Windows TEMP spelling. Working corrections/identical corpus/affected checks pass;
  clean committed-source CI/native and candidate remain. Encrypted content/header refusal is preserved.

- I133: earlier I115/I116 archive-search passes do not establish truthful size criteria for unknown-length
  members. The shared initial/narrowed search check is corrected and working host validation passes;
  clean 578a0ed CI/Windows/Ubuntu checks now pass. Other formats/native/candidate remain; unrelated count evidence is unchanged.

- I132: earlier quick-search passes do not prove cancellation-relative focus when a match completes
  during input delivery. The corrected observer/forced controls, full host suite and all four clean CI
  jobs now pass with complete affected inventories. Native/candidate obligations remain; production
  and E-V12-N1 data are unchanged.

- I129-I131: earlier passes did not cover already queued retired results, unchanged folder-caption
  demand or the partial-progress checkpoint after refresh. Controlled corrections, host suites, four
  clean CI jobs and all 74 combined SDK-free guest controls now pass at fdb17b4. Earlier failed runs
  remain retained; native/hardware/AT/candidate qualification remains.

- I128: prior viewer passes do not qualify direct line/page/Info reads during close. Working controlled
  correction/affected/full host suites, four clean CI jobs and 143 SDK-free guest controls pass. Native/
  hardware/aggregate/candidate remain.

- I127: earlier folder-count passes do not qualify inaccessible-subtree lower bounds or retries after restored
  access. Corrected probe/affected/full host suites, four clean CI lanes and 122 guest controls pass. Native/AT/
  candidate remain required.

- I126: previous comparison passes do not qualify revision/length lifetime during close or F5. Corrected owned
  real-file probes/affected Core/App/full App pass, as do four clean CI lanes and 91 guest controls. Wider content
  lifetime and native/candidate remain.

- I125: earlier scheduler passes do not cover waiting admission across disposal or new queues missed by its
  snapshot. Corrected owned probes, affected Core/App and full host suites pass; shared consumers require clean
  CI/guest and native/candidate revalidation. Clean 749f55f now passes four CI lanes and 87 guest controls;
  wider resource lifetimes and native/candidate remain.

- I124: previous scheduler passes do not qualify watchdog replacement while appending workers or enforcement
  of the cap when all workers are quarantined. Corrected owned probes, affected Core/App and full host suites
  pass, as do four clean CI lanes and 85 guest consumer controls. Actual hung hardware, wider shutdown/queue
  lifetimes, aggregate decoders and native/candidate validation remain.

- I123: prior passing synchronization runs do not establish use of the shipping Windows adapter or a held
  replacement target. Controlled baseline/final, seventeen affected/full App, four clean CI lanes and 17 guest
  cases pass; native/candidate remain.
  The original failed CI remains retained; its unavailable decision request prevents exact attribution.

- I122: earlier picture evidence does not qualify the new provider-keyed scheduler route. Working/full host,
  36 clean guest and four successor CI lanes/all affected controls pass; original Windows synchronization failure
  is retained. Native/candidate remain. Watchdog/hard-cap and aggregate decoder bounds have separate
  uncompleted scopes.

- I121: prior picture/quick-view evidence does not qualify actual feed ownership after cancellation or F3 bitmap
  retirement. Working/full host, four clean CI lanes and 34 guest cases pass; native/candidate checks remain required.
  Per-device picture-feed bounds and other direct Source consumers remain outside this remedy.

- I120: the original MFT integration pass did not distinguish live-history prerequisites/omitted assertions.
  Its successor separates MFT and live-log cases; skipped history is not qualified. Host platform/golden
  controls, four clean CI lanes and 20 elevated guest cases pass, including complete history with zero affected
  skips. Candidate/native scopes remain required.

- I119: prior page-reader tests do not qualify disposal against active provider calls or retired refresh demand.
  Working component/full host, clean guest and four successor CI lanes pass; original I120 CI failure retained.
  Native/candidate checks remain required. Direct
  calls through Source, including picture feeds, are outside this remedy's protection.

- I118: earlier metadata cache/verification evidence does not qualify the new publication and demand lifetimes.
  Working component/full host, clean CI and guest controls pass; native frame/AT/candidate checks remain required.

- I116: prior initial archive-search logs do not prove provider warnings were visible. Working/full host,
  clean CI and guest controls pass; other-format/native/candidate checks remain required.

- I115: old result-narrowing evidence does not qualify archive-member revalidation or the revised log/navigation.
  Working/full host, clean CI and guest controls pass; other-format/native and candidate checks remain required.

- I113: old quick-view evidence does not qualify the new request, scheduler and bitmap lifetimes. Controlled
  headless/full host suites, clean CI and guest controls pass; native presentation/AT and candidate reruns remain required.
- I114: old parallel GnuPG fixture evidence may use another fixture's selected executable. Failure is retained;
  controlled tool swap and isolated full Core pass. Production GnuPG policy is unchanged.
- I92/I111: prior quick-search/streaming-first-row execution does not qualify the revised listing/UI bytes.
  Exact working/full affected suites, clean successor CI and Windows guest controls pass (E-I92/E-I111);
  native/reference/candidate input/frame evidence remains required. The pending-search caption needs native AT revalidation.
- I112: the old concurrent cache plateau observer is insufficient for quiescent accounting. Its failed result
  stays failed; held-read/completion and full corrected host inventories pass (E-I112). Cache policy is unchanged.
- `f87ad32` (job engine, interrupted-copy review): E-A01 and E-L01 no longer describe current source for transfer
  paths; V03 interrupted-copy cases and small-file copy throughput must be re-run on the candidate.
- `5b061cc` (installer script): every earlier installer build; V19 lifecycle evidence must use the final setup.
- `45efc09` (D-56 `$LogFile` reader): V14 `$LogFile` evidence; the reader now waits up to about six seconds when the
  on-disk log lags, which affects the record window's time to open in that case.
- `33b7de2`, `5c54181` (administrator helper): any evidence of the consent window and V06-CONSENT; the helper binary
  changed, so broker evidence must be taken on the candidate.
- `47c27b9` (Registry provider): V13 Registry browsing evidence for explicit views.
- `63d5fc4` (Windows file operations, comparison window): V03 replace and move-over-existing evidence on Windows.
- `98fb594` (NTFS recovery decoder): V11 NTFS recovery evidence.
- `f93f919`, `2ba114e`, `1dce2c2`, `4c6b910`, `e527a86` (FTP and SFTP channels, remote jobs): every V08 result before
  them; the lab's runs must be repeated on the candidate.
- `8b0dafd` (state folders), `e399276` (journal recovery): V11/V23 state evidence and V03 interruption evidence.
- `2a6f882` (Shell helper): the helper binary changed; V16 Shell-preview evidence must use the candidate.
- `78a48ce` (FAT recovery): V09/V11 FAT recovery evidence before it.
- `efc128f` (comparison, Synchronize): V13 comparison and synchronization evidence before it; a one-sided folder is now
  read in full when compared.
- I99 (Unix instance sockets/recovery write folders): previous Unix instance and recovery write-location evidence;
  affected boundary/CI and dev.539 native packages revalidated; separate-session remedy tracked as I100. Candidate reruns required.
- I100 (Unix instance lock/endpoint and usual-instance probe): prior Unix process-election/probe and recovery guard
  evidence; native working inputs, CI and both rebuilt Linux package baselines pass. Candidate evidence still required.
- I101 (runtime temporary-folder guard and endpoint metadata): earlier Unix recovery/endpoint metadata evidence;
  affected native suites/harnesses, CI and rebuilt Linux packages revalidated. Candidate write tracing still required.
- I102 (independent instance lifetime/profiles and guarded instance directory): prior recovery probe, independent
  startup/forwarding and write-location evidence; affected native/CI and rebuilt Linux checks pass; wider discovery/candidate tracing pending.
- I103 (Windows instance names/state identity): prior Windows election/forwarding/probe and portable/installed
  state-isolation evidence; preliminary process/guard/CI checks pass. Windows release package/candidate evidence pending.
- I105 (portable/per-user candidate and profile discovery): earlier recovery lookup/profile inventory evidence;
  affected native working-overlay, Windows and CI checks pass. Rebuilt native checks and candidate tracing pending.
- I106 (device process census): prior device admission evidence; ordinary-name native after and CI pass at their
  recorded identities. Renamed-apphost audit invalidates broader absence conclusions; executable-identity
  correction under validation. Wider visibility/race/availability audit and candidate tracing pending.
- I106 confirmation recheck: previous device admission results do not cover changes while confirmation is open;
  targeted baseline/after and affected App/native Windows inventory retained. Native Ubuntu and all affected
  successor CI lanes pass at their recorded identities; wider physical/candidate work pending.
- I106 Windows limited image query: previous Windows executable-lookup evidence does not qualify the changed
  App/platform bytes. Working native controls/full affected host inventories and clean 36ee824 guest/CI pass
  (E-I106-P2); candidate tracing remains pending. Unavailable identities still refuse admission.
- Physical fixture checker 2e6dffe/85bb17d: earlier exact positive comparisons remain evidence, but the broader
  truthfulness gate can hide corruption beside missing ranges or truncated claims. Controlled baseline/correction
  and exact native/CI checks pass; the exclusive stronger physical rerun with observed hashes passes (E-V09-G3).

## Next actions (unblocked)

1. I107 is closed for preliminary remediation: failure path/baseline reproduced, affected App/CI pass and corrected
   host/clean guest success supported by retained native trace. Windows Computer Use is independently unavailable. I106 native process/GUI guard
   and affected CI pass at their identities. Dev.549 formats pass successor checks on 26.04 (E-V19-P3); wider
   audit reproduces a renamed-apphost gap. Identity correction passes native positive cases and affected App
   checks; refined root census now establishes absence while ordinary-account visibility remains unknown.
   D8c6f3b successor CI passes; confirmation gap is also corrected, with Windows/Ubuntu native and all cc1acf2 CI
   passing. Continue visibility/runtime-alias/lifetime and write-location audits. Physical source-device checks
   now have the identity-bound USB on host G:, with owner authorization for disposable use. Strengthened guards
   and physical preflight pass; Windows census remains unknown even elevated after owned app teardown. Continue
   that availability correction. VMware routing drops before native guest execution; owner elevated host run then
   passes all three filesystem component scenarios. Corrected byte checker passes affected/native checks and all four
   successor CI lanes. Stronger attempt overlaps an older USB campaign and is invalidated. Interprocess correction
   passes controls; clean exclusive 090a2b6 successor passes all three physical formats (E-V09-G3).
   Installed-helper raw-read/source-write tracing still needs separate evidence; tracer controls pass (E-V09-G4).
   Read-only component source-hash/trace run at f2f0141 has differing full hashes and failed capture controls (E-V09-G6).
   Hold further USB tests; off-source timing matrix has mixed outcomes (E-V09-G8). Built-in WPR file/disk marker
   controls pass with zero reported loss (E-V09-G9); owned virtual-device raw read/write controls now pass,
   with exact native attribution and full fixture-byte oracle (E-V09-G10). Resolve the unexplained physical source
   change before resuming FileCat USB validation. Read-only diagnostic images match G6's after hash, but its long
   trace loses 51,216 events and fails source-write qualification (E-V09-G11). Narrower kernel short controls pass
   with zero reported loss (E-V09-G12). Nine-minute duration control now passes after short trace-scratch correction
   (E-V09-G13). Proof-gated read-only diagnostic now passes complete images, all source reads, zero reported loss
   and no source writes (E-V09-G14); historical G6 attribution is still unavailable and FileCat USB validation
   stays held. Windows structural diagnostic accounting passes but does not establish absence (E-I106-P1). Prepared
   Windows guest menus pass preliminarily; exact-candidate checks remain. Exact dev.539 Linux packages pass on both fresh
   Ubuntu baselines (ENV-04/I04/I99–I103), with raw evidence retained and independently verified. ReFS/Dev Drive and same-server SMB copy cases are done preliminarily (E-V03-CLONE-1),
   including I97's corrected rerun. Fuzz campaigns are already collected (item 109).
1b. V12, what is left: further viewport/page-load and picture-feed demand, rapidly
   changing viewports, visible rows beside a copy or a search, many folders counted and partial sizes after Esc.
   Done this session: page and archive budgets (I06), the watcher (I87), counts and analyses ending with their folder
   (I88, I91), quick-view initial-load demand and stale-result lifetime (I113 host/clean CI/guest pass),
   views closed while busy, million-entry listings (I92 host/clean CI/guest pass; native frame pending), many tabs.
   In-flight invalidation/retry and controlled rapid viewport demand now pass working/full host suites and clean
   CI/guest controls (I118). Native request/queue traces and UI-thread timing remain required.
   Page-reader calls now retire new demand and release active sources safely on close (I119 working/full host
   suites, 45 clean guest cases and four successor CI lanes pass; original I120 failure retained). Direct
   Picture feeds now retain active sources after cancellation and release closed F3 bitmaps (I121 working/full
   host, clean CI and 34 guest controls pass). Controlled per-device feeds now pass working/full host cases
   and 36 clean guest cases/four successor CI lanes/all affected controls (I122; original synchronization failure retained);
   watchdog/hard-cap and other direct Source use/wider queues remain open. Aggregate
   decoder admission and the Unix cleanup/fixture follow-ups pass working/full host,
   clean Windows/macOS/Ubuntu and all four exact cb85f0a CI lanes (E-I144–E-I146);
   aggregate displayed bitmap memory remains.
   Unix native icon cache/queue growth is now corrected at 1559933: actual Ubuntu
   baseline 50,000 entries/43,795 queue peak, corrected 2,538 entries/256 peak/zero
   queued; clean Mac/Ubuntu nine-case inventories and four CI lanes/full inventories
   verify (E-I143). Windows icons pass controlled/full host, clean Windows/portable Unix and all
   four exact-source CI lanes at 6cf17e5 (I147); all-consumer memory and native frames remain.
   Real timer/list mutation and controlled worker-cap failures now pass working probes, 85 affected cases and
   full host suites, four clean CI lanes and 85 guest controls (I124); actual hung hardware and wider/native/candidate
   scopes remain. Shutdown admission races now pass corrected probes/two new controls/87 affected cases and full
   host suites, four clean CI lanes and 87 guest controls (I125); wider/native/candidate remain.
   Comparison revision/length lifetime now passes owned probes, four regressions/affected and full App,
   four clean CI lanes and 91 guest controls (I126). Wider content/native/candidate remain.
   Inaccessible subtree/zero-byte counts now retain lower-bound labels and permit restored-access retry (I127):
   owned probes/affected/full host suites, four clean CI lanes and 122 SDK-free guest controls pass. Native/AT/
   candidate remain, alongside many marked folders and the other partial-count scenarios.
   Direct viewer line/page/Info read lifetime now passes four baseline-failure corrections/eight controls,
   affected/full host suites, four clean CI jobs and 143 guest controls (I128). Native/hardware/aggregate/
   candidate remain.
   Controlled 32-folder counts and completed-but-queued callbacks now pass an App-only correction/
   sixteen identical probes/43 original affected and full host cases (I129). Combined clean fdb17b4
   four CI jobs/74 SDK-free guest cases now pass (I129-I131). Native sparse/hard-linked/mixed data,
   32 marked folders and refresh reconciliation now pass six cases per Windows/Ubuntu/macOS lane,
   with independently verified native snapshots/input pins/cleanup (E-V12-N1). Slow/cloud locations,
   other native environments, native Esc/frame/AT and candidate still need validation.
1c. V13: duplicates among a set and saved content criteria now pass working/full App controls (E-V13-F2);
   clean ab919ed/da3a3d6 CI passes all four lanes and affected Windows/Linux/macOS App cases; combined guest
   execution also passes (E-I117). Archive-result narrowing now
   passes working and clean CI/guest Core/headless Find flows (I115); TAR/gzip and initial warning propagation
   now pass working/full host and clean CI/guest controls (I116). The thirteen-fixture independent
   corpus now passes 78 file-member search/57 content controls after I133/I134 fixes; I135 repairs
   only fixture disk spelling. Four clean 3caf480 CI jobs and 71 SDK-free cases per Windows/Ubuntu
   guest pass, with all source/fixture/case/cleanup pins verified. Independent ISO 9660/Joliet/UDF 1.02 and six numbered-RAR entry points now pass another 54 search/
   72 content controls. I136 fixes a missing interior-volume warning; four clean a1c265f CI jobs
   and 81 cases per VM pass with all pins/cleanup verified. Pure UDF six-revision and legacy RAR component controls now pass host oracles; clean native/CI
   qualification passes, including I149's full version markers and 77 controls per
   native lane/four exact-source CI jobs (E-V13-UDF2/E-I148/E-I149). Other revision/topology variants,
   naming, native interaction and exact-candidate checks remain required (E-I136).
1d. V16: ready-for-input and input-to-frame latency need the window on a desktop and the reference machine.
   Current desktop state was not observed: updated Computer Use import and plain Node startup both fail before
   input. Restore that runtime for live interaction; I92's worker remedy passes host/clean CI/guest controls,
   with native frame/AT checks pending.
1a. Continue V24: the terminal and association routes as the user drives them from a window; the same cases on a
   candidate's installed files. (`.lnk` targets on a share held, E-V24-G1-I1.) Done so far: the Git, icon and gpg
   routes (E-V24-G1), the tool route with a recording program (E-V24-G1-T2), the discovery parsers, and the process
   and file traces of browsing (E-V24-D1, E-V24-D1-F1).
2. V09 on macOS: v13 actual combined capture/controls/recovery/bytes/source/cleanup and
   all recorded command exits verify (E-V09-M5). All 77 shared mapping FDs/offsets resolve;
   43 unmatched private maps remain explicit. No source-FD writes/aliases/forks/mappings
   observed. Historical v12 gaps/failures remain in E-V09-M4. Continue broader helper,
   native authopen approval/refusal/removal and adverse topology qualification with SIP
   enabled; do not infer those passes from ordinary-image recovery. Native consent needs
   owner-local interaction. Native component refusal and fresh approval/returned-FD/read
   controls verify historically in E-V09-M6; actual E-V09-M7 replacement exposes I140.
   Committed replacement v3/v4 pass; unchanged-source approval passes (E-V09-M8); continue
   broader native qualification before advancing that path; actual held removal safety
   and truthful reporting pass on committed 348cbc7 with a fresh native repeat and
   clean Mac/Ubuntu controls (E-V09-M10/E-I142); current
   desktop refusal now independently passes (E-V09-M9).
   The first approved/failed-expectation attempt remains
   retained. Continue intended-device replacement/removal and adverse topology controls.
   Broader I106,
   installed helper/Windows approval refusal remain.
3. Continue the V23 source review: B01–B03 (largely covered by the DPI rows, the fuzz campaigns and V07/V10).
   Fresh finite I16 process/file/share-contact controls pass with independently retained positive/loss/
   lifetime/payload/cleanup evidence (E-V24-D2). Broaden indirect paths, reparse/global-config/environment
   variants and protected-file effects; these observations do not close I16 or qualify installed candidate bytes.
4. I42's options for the owner (fewer requests per file; several files in flight), when the owner wants them.
5. Keep the records current after each change.

Waiting on people, hardware or a candidate: DPI P13's remaining case (locking the phone mid-transfer, with the owner; the disconnect cases are done, E-V21-U1); P07's loader audit (V06, installed
candidates); I09's device-level zero-write cases (USB connected to host; source-change/capture investigation pending); steps 2, 5, 7 and
11–26 of the plan. I04's Ubuntu 26.04 environment is now available and the package remedy is under validation.

320. I154 correction 99e54b3 seals preliminary finite startup-hook rejection: clean raw 909-file
    source producer/four actual Windows publishes, fresh native SC/FDD repeat/two positives/431
    payload pins/output/process/protected-fixture cleanup and all four exact-source CI lanes verify.
    Ten server digests/four toolchain receipts/fourteen full execution inventories and actual
    ARM64 runtime-config hash/startup/drawing/installer receipts verify. Corrected helpers are
    stopped after six seconds; healthy consent/limited caller/native profiler/candidate remain.

321. Actual protected committed 99e54b3 SC/FDD helpers load the owned native profiler
    DLL before Main despite startup-hook rejection. DLL attach markers match returned
    PIDs 12516/11108 and administrative identity. Two direct LoadLibrary controls, all
    input/output pins and protected/process cleanup verify. The unavailable factory
    supplies no profiler callbacks; both helpers are stopped after six seconds. Caller
    is already administrative, no UAC bypass claimed. New I155; pre-CLR remedy underway.

322. I155 remedy fc5e706 adds a native Windows entry point before CLR loading, retaining
    managed plan/consent logic and FDD servicing. Executable-only and actual working native
    controls block external profilers with positives; synthetic entry controls preserve
    original arguments/system environment/same-process identity, SC 10.0.12/FDD 10.0.9.
    Full host Platform 161/38 skips/199 passes. Deep raw export exposes native linker path
    limits; 7183268 stages native compilation at a short owned temporary path, restores
    logs/receipts and verifies cleanup. Failed compiler/publish/normalization observers remain.

323. Clean committed 7183268 I155 seal passes: all 917 raw source blobs/four native
    outputs/compiler receipts, fresh SC/FDD actual profiler rejection/two positives/431
    input pins/output/process/protected cleanup and separate synthetic CLR handoff verify.
    All four CI 37422904088 lanes/ten digests/four receipts/fourteen execution inventories,
    both native Windows executable bytes/source/receipt pins and ARM64 startup/drawing/
    installer pass. Caller already administrative; real helpers are stopped after six
    seconds, actual consent/limited caller/ACL/dependency/license/IPC/candidate remain open.

324. New I156: unchanged committed 7183268 native entry points accept a synthetic
    managed DLL under Program Files with an explicit ordinary-account Modify ACE.
    Both actual broker launches load it in the returned administrative PID and exit 41
    naturally. Before/after bytes/all inputs/two positives/owned cleanup verify;
    only the owned DLL ACL changed. No actual plan or unelevated/UAC bypass claimed.
    Native pre-load file/ancestor ACL correction continues autonomously (E-I156).

325. I156 correction ce8189e verifies ownership/ordinary write grants on the native
    helper, ancestors and bounded adjacent tree before CLR loading. Executable-only
    protected handoff succeeds; writable entry/folder/dependency and owner-with-inherited
    grant controls block managed startup with positives and owned cleanup. No dialog
    or clean refusal exit was observed; owner mutation is not an isolated owner proof.
    Four working native publish modes and 15 affected host tests pass. All 918 raw Git
    blobs/four clean committed native outputs/receipts verify; fresh VM repeats and
    exact CI 37426875741 continue. No user interaction is needed for this slice.

326. I156 ce8189e clean native/CI seal completes: all 918 raw sources/four native
    publishes/receipts, protected SC/FDD handoff and four adverse ACL cases/profiler
    regression/two positives/payload/output/process/protected cleanup verify. All four
    CI 37426875741 lanes/ten digests/four receipts/fourteen full execution inventories
    and both downloaded native Windows byte/source/receipt pins pass; ARM64 starts,
    draws and compiles installer. Dialog/limited caller/races/shared runtime/candidate
    remain. New I157 actual brokered-open counterfeit pipe baseline accepts exact
    owned regular-file bytes from a server PID other than the launched helper, without
    a consented report. Absent-device query/two positives/pins/cleanup pass; no physical
    source opens. Authenticate peer before protocol I/O; correction underway.

327. I157 b4b6e1b verifies kernel pipe server against the held live runas process
    before Info. Working four-mode publishes and 25/1 host affected tests pass. Clean
    923-source/four-mode publish receipts, actual SC/FDD counterfeit blocks with zero
    protocol request/reply bytes and separate synthetic managed servers in the actual
    helper PID preserve exact regular-file data and all pins/cleanup. No production
    consent or physical device is involved. Four CI 37429527482 lanes/ten digests/four
    receipts/fourteen full inventories/two native Windows executables/source receipts
    and both new native matching/mismatching identity tests pass. 135/157 preliminary,
    one Closed, 21 remaining issue remediations; all campaigns need final qualification.

328. New I158: exact b4b6e1b native FDD helper reaches a synthetic managed entry
    through installed .NET 10.0.9 CoreLib after only an explicit user SID Modify ACE
    is added to that file. Actual loaded path/PID/natural exit 41 verify; SC uses
    bundled protected .NET 10.0.12. No runtime bytes change. Two positives/all pins/
    owned cleanup and exact original VM runtime SDDL/content restoration pass. Caller
    already Admin; no real consent/device/UAC bypass. Pre-CLR shared runtime trust
    correction continues autonomously (E-I158); 135/158 preliminary, 22 remain.

329. I158 69603ec verifies the complete default shared framework tree before CLR
    loading. Working/clean four-mode native publishes/receipts and all 924 raw Git
    inputs verify. Protected SC/FDD synthetic handoff succeeds, unsafe-runtime SC
    stays compatible and FDD does not reach managed entry; original VM runtime ACL
    and bytes restore exactly, positives/pins/owned cleanup pass. Dialog unobserved
    and negative helper boundedly terminated. All four CI 37433725218 lanes/ten
    digests/fourteen full inventories/two downloaded native PE/source receipts pass.
    Two gh full-log reads timed out; official run-log archive/native curl succeeds, original failures/collector and unchanged event contents retained.

330. New I159: exact 69603ec caller/public runas launch accepts an owned harmless
    native replacement EXE with an explicit current-user Modify ACE. Actual held
    launch/native PID 10624/Admin/natural exit 73, protected positive/two DLL
    positives/pins/owned cleanup pass. No device/real plan/limited caller/UAC bypass
    claimed. Check executable and ancestors before Windows starts them; correction
    continues autonomously. 136/159 preliminary, one Closed, 22 remaining issue
    remediations; all campaigns still need final qualification. NO-GO.

331. I159 8b4be9d checks executable/ancestors before discovery and ShellExecute,
    refusing unsafe ownership/grants/reparse/resolved aliases. Host affected 12/0
    and four working native modes pass. Identical final observer with only caller
    DLL changed reproduces baseline writable-native startup and corrects it with
    no returned process, preserving protected natural exit 73. Clean 927 raw Git
    inputs/four native publish modes/receipts verify; fresh file/parent-grant
    refusals and healthy real-native SC/FDD synthetic managed handoff pass with
    positives/pins/owned cleanup. Four CI 37437361505 lanes/ten digests/fourteen
    complete inventories/two native PE/source receipts and both new Windows/ARM64
    API-test executions pass. No real plan/consent/device or limited-user bypass
    tested. 137/159 preliminary, one Closed, 21 remaining issue remediations.
    All campaigns still need final qualification; no candidate/human stable GO.

332. I03 native provenance improves at 573ed2c: original-version/revision diagnostic
    x64/ARM64 executables stay byte-identical; C++ header/library/tool pins,
    original reports/diagnostics and actual linked-symbol maps retained. Four
    working/four clean modes, 927 raw Git blobs and local input bytes verify.
    Four CI lanes/ten digests/four build receipts/fourteen full inventories/two
    native byte receipts/report-map sets pass. Hosted input bytes not retrieved;
    resource headers/full read tracing/licenses/system-library classification/
    broader provenance/candidate remain Open (E-I03-NATIVE). 137/159 preliminary,
    one Closed, 21 remaining issue remediations; no stable human GO.

333. New I160: exact 573ed2c production nonce guard accepts simultaneous claims
    in thirteen thread rounds and four separate eight-process rounds (8/5/8/7).
    Sequential replay refuses; all original child receipts/held PIDs/natural
    exits/payload/owned Registry restoration verify. Initial null-exit observer
    retained and corrected. No production plan/consent/device is exercised.
    Atomic correction continues autonomously (E-I160); 137/160 preliminary,
    one Closed, 22 remaining issue remediations; no candidate/human stable GO.

334. I160 committed atomic correction 37c88c8 passes the identical final
    cross-process observer, clean 931-source/four-native-mode receipts and fresh
    four eight-process rounds with one claim each, sequential refusals, all
    original child/PID/natural-exit/payload/Registry restoration checks. All
    fourteen new nonce cases actually pass in x64/ARM64 CI. Exact run
    37446927662 attempt 1 remains failed on one Mac picture demand case; ten
    server digests/four source receipts/fourteen complete inventories/original
    log/native input-output receipts verify. No four-green claim (E-I160).

335. I161: unchanged viewer independently admits three first reads on one device.
    Captured stacks show encoding/header reads; header-only correction still
    admits a third visible page read outside device admission. Both source paths
    now use the shared scheduler. Identical final observer/common Core/only App
    DLL changes: before fails with three, after passes with two; healthy device,
    zero queued reads, source lifetime/disposal/exact bytes verify. Host affected
    App 3/Core 17, full App 386/23/409 and Core 820/56/876 pass. Premature rebuild
    fails on the held test executable; after natural completion the rebuild and
    isolated comparison pass. Committed native repeats/successor CI continue.
    139/161 preliminary, one Closed, 21 remaining issue remediations; all final
    campaigns/candidate/human GO remain gated (E-I161). USB G: stays on HOLD.

336. I161 source 8975fdf seals preliminary remediation: 932 raw Git blobs/four
    native modes/compiler receipts and clean Windows 26/0, Ubuntu 25/1 and Mac
    25/1 native repeats verify every new header case, payload/result/transport
    hashes, source disposal/bytes and owned temp/runner cleanup. Exact CI
    37451501259 attempt 1 passes all four lanes; ten original server digests,
    four clean build receipts/fourteen complete inventories/two native Windows
    executables/report-map sets/eight new viewer/page cases and fourteen nonce
    cases verify. ARM64 startup/drawing/installer pass; package jobs skip. The
    failed original 37446927662 remains failed. No persistent guest/Mac setup
    changes, USB source or GUI qualification. 139/161 preliminary, one Closed,
    21 remain; all final campaigns/candidate/human stable GO remain gated.

337. I18: current 7b56b16 still omits ARM64 from all package prerequisites and
    uploads broad platform globs. All three jobs now require the four test lanes;
    shared exact versioned selection emits byte hashes and the same paths for
    artifact and draft uploads. Eight host synthetic controls/PowerShell parse/
    independent workflow graph checks pass. Controls are scheduled in every
    required hosted lane; tagged publisher and actual package path have not run.
    Installer suffix mismatch, signing/promotion/duplicates/protection/immutable
    retention/candidate gates stay Open (E-I18-A1). Counts unchanged; NO-GO.

338. I18 b9526b9 actual development validation seals 935 raw Git inputs, 652
    publish outputs/two final FDD native report-map sets, 510 SC guest payload
    pins and both real installer binaries/119 frozen compiler pins/cleanup.
    All eight Windows files/exact emitted paths/ZIP architecture/license/portable
    checks verify. Development dispatch 37455247699 passes all four test lanes
    and actual Linux/Mac packaging/install/version/signature/icon steps; eighteen
    digests/six clean build receipts/fourteen inventories/all package-manifest
    hashes verify. Original main 37454794034 remains failed on one Windows first-
    feed checkpoint, with fourteen digests/four receipts/fourteen inventories
    retained. Every hosted selector control passes in both runs. Separate cold-
    pool fixture investigation: 64 unchanged/64 capacity-reserved cases pass,
    all production bytes identical, median first case 6.974 -> 1.004 seconds;
    pool minima restore, existing limits/deadlines remain and full App 386/23/409
    passes. Original
    cause is not proven; committed fixture successor CI continues. No tag,
    publication, Windows package-install/GUI, physical source or candidate claim.
    Counts unchanged: 139/161 preliminary, one Closed, 21 remaining; NO-GO.

339. I146 separate cold-pool fixture source 4b2b9d7 seals 935 raw inputs/three
    SC payloads and fresh Windows 26/0, Ubuntu 25/1 and Mac 25/1 native repeats.
    All pool/checkpoint/restore outputs, exact XML/TRX identities and original
    payload/result/transport/temp/runner cleanup pins verify. Observer v1 wrongly
    expects native passed output in TRX; v2 uses original XML without rerunning
    tests, preserving that observer failure. Exact CI 37459327156 attempt 1
    passes four lanes/ARM64 startup/draw/installer; fourteen server digests,
    four clean build receipts/fourteen inventories/all 409 App names/sixteen
    fixture cases and hosted asset controls verify. Original 37454794034 stays
    failed; cause remains inferred. No persistent guest/Mac settings change,
    physical source, native GUI or candidate qualification. Counts unchanged.

340. I18 stable-producer gap: baseline 4b2b9d7 still accepts stable v* tags for
    producer rebuilds. A read-only reference-policy ancestor now rejects stable
    push/manual/build-metadata and invalid/mismatched refs before all test/package
    jobs. Sixteen synthetic host cases/five separate exit-code controls/PowerShell
    parse/independent job graph pass; no real tag or release API is invoked.
    Committed hosted successor continues. Approved-manifest promotion, human GO,
    signing/preview approval, duplicate asset refusal, publisher separation and
    repository/immutable controls stay Open (E-I18-P2). Counts unchanged; NO-GO.


341. I18 stable guard source 317a9a5 and original CI 37462073457 attempt 1
    seal the policy job/four green required test lanes, fifteen server digests,
    four clean exact-SDK build receipts/fourteen full TRX inventories/all 409 App
    identities/sixteen picture cases and ARM64 startup/draw/installer checks.
    Sixteen hosted reference controls match source/run pins; actual main/push
    guard permits development with stable promotion false. Three package jobs
    skip; no stable-negative tag, release or candidate is created. Original
    failed 37454794034 remains failed. Counts unchanged; NO-GO (E-I18-P2).

342. I18 draft action defaults to replacing assets; false alone skips a known
    duplicate. A read-only selected-manifest/package/ref/draft/paginated-asset
    preflight now precedes each of three replacement-disabled actions. Read-only
    postflight requires every newly uploaded name/ID/size/server digest and the
    independent remote identity, refusing skipped duplicates and changed bytes.
    Thirty-two host synthetic cases/eight selector output checks/four-script
    parse/independent workflow ordering pass. Two harness scalar-count failures
    remain in tool history; explicit fixture arrays correct them. Actual GitHub
    release inventory is empty; no live duplicate/tag/upload is claimed.
    Committed hosted successor continues. Full approval/signing/promotion,
    publisher/protection/immutable storage/final download/candidate gates stay
    Open. No physical source or guest/Mac setup change; counts unchanged; NO-GO.

343. Draft preservation source 6a6af3b is committed and pushed. The final document
    pin audit detects a mismatched v2 filename for the unchanged retained v1 proof;
    the initial audit failure remains in tool history. Correcting the document
    path preserves the original proof bytes/hash. Hosted source validation is
    running; no actual release/tag/upload, candidate or stable GO is claimed.


344. Draft preservation source 6a6af3b seals original CI 37464968767 attempt 1:
    policy/four required lanes/ARM startup/draw/installer pass; fifteen server
    digests/four clean exact-SDK receipts/fourteen complete inventories/all 409
    App identities/sixteen picture/sixteen reference/128 draft controls verify.
    Three package jobs skip. Twelve original b9526b9 Windows/Linux/Mac files
    additionally pass nine actual-byte phases with substituted remote metadata,
    same-file hard links and unchanged original/linked pins. Initial document
    proof-path audit failure is retained; ecac41c corrects the filename and all
    thirty then-current document pins/counts independently verify. No tag,
    publication, source mutation or candidate qualification (E-I18-P3).

345. The current three package builders still carry release-write authority.
    Builders now inherit global read-only permission and expose manifest digests.
    One dependent draft-only job receives write permission, downloads three exact
    current-run artifacts with pinned digest-enforcing action, checks all three
    manifests/fifteen paths and uploads once without replacement. Postflight
    checks combined IDs/names and refreshed per-platform bytes; no build/signing
    occurs in that job. Thirteen host synthetic cases/two-script parse and
    independent nine-job dependency/permission/transport audit pass. Hosted
    committed successor and actual read-only development packaging continue.
    Final approved promotion/approval/protection/immutable storage/candidate
    remain Open (E-I18-P4). Counts unchanged; no guest/Mac or physical-source use.


346. Read-only producer source 246ce18 seals push 37467658278 and independent
    development 37467776324 attempt 1: policy/four required lanes/ARM startup/
    draw/installer pass; fifteen/nineteen digests/four/six clean exact-SDK receipts/
    fourteen complete inventories per run/all 409 App identities/sixteen picture/
    sixteen policy/128 draft/52 set controls per run verify. Actual read-only
    Linux tar/deb/AppImage and Mac package/signature/start/icon checks pass;
    retrieved manifests match all actual file hashes. Draft job skips; no real
    tag/upload/stable publication/candidate claim. Original failures remain.
    Counts unchanged; no physical-source or guest/Mac setup use (E-I18-P4).

347. I03 prospective restore: isolated raw 246ce18 source/derived four-RID recipe
    restores all twenty-one projects and generates locks. Original git-archive
    byte failure and restore -r RID narrowing remain retained. Publish-equivalent
    property passes solution/four App RIDs/both broker SC/FDD, rejects changed
    request/hash and restores original inputs. SDK unexpectedly recreates missing
    lock with zero exit; prospective project-entry target refuses missing App and
    referenced Core without recreation, with healthy controls. Original observer
    logical/archive-hash and CRLF/raw-source assumptions fail; corrected seal
    uses unchanged logs/raw pins and 63 separate logical-cache/raw-archive pairs,
    with no control rerun for that correction. Repository policy is unchanged;
    accepted script RID/publishing/input/license/candidate checks continue.
    Counts unchanged, I03 Open and NO-GO (E-I03-RESTORE).

348. I03 locked-restore adoption: raw 246ce18 five-RID derivation passes default
    solution/all twenty-one projects/five App RIDs/four helper modes; changed
    request/hash/missing App/Core refuse and explicit maintenance restores the
    exact original lock. Actual file-based icon tool fails missing-lock guard;
    retained failure leads to explicit locked IconFrames project with seven exact
    ICO/PNG frames. Release solution build/five actual App ReadyToRun publishes/
    four native helper SC/FDD publishes pass; 52 commands and 1,645 output pins
    independently verify. Twenty-two locks and validated recipe are adopted on
    working 187fad1. New helper verifies/retains all twenty-two actual locked
    restore graphs; all six CI builder definitions run it before building and
    retain partial evidence. Staged source/graph/order/PowerShell/whitespace checks
    pass. Committed hosted and actual package validation continue; broader
    extracted/native/runtime/license/SBOM/candidate remain Open. Counts unchanged;
    no physical-source/guest/Mac setup or publication; NO-GO (E-I03-RESTORE).

349. Locked restore source a7a70ae is committed/pushed and seals original push
    37476206680 and development 37476271305 attempt 1: policy/four test lanes/
    ARM64 startup/draw/installer pass, nineteen/twenty-five server digests,
    four/six clean SDK receipts/fourteen complete inventories per run and all
    App/picture/policy/draft/set controls verify. Every retained source lock and
    actual assets graph independently matches: 88/132 graphs across four/six
    builders. Actual Linux/Mac package install/start/signature/icon checks and
    downloaded manifests pass. Bounded CLI transport timeouts are retained;
    original-attempt native API reads correct transport with no test rerun.
    No tag/upload/candidate or native desktop qualification (E-I03-RESTORE).

350. Owned NuGet archive/input comparison verifies 1,644 extracted files across
    63 previously pinned packages with metadata/case/OPC distinctions. Initial
    wrong archive-field observer fails before comparison; v2 preserves that
    failure and matches exact bytes without cache mutation. Nine immutable,
    package-declared source license/notice blobs verify Git length/SHA-1/SHA-256.
    Thirty-eight full texts/38 App graph components are privately staged with
    explicit build-only/RID scope and one LTRData.Extensions 1.0.23 full-text gap;
    its complete source tree has no license-named file. No binary/source license
    eligibility, all compiler reads or hosted/native/runtime/SBOM qualification
    is inferred. Notice packaging is next. Counts unchanged; NO-GO.

351. I03/I10 full-notice correction adopts 48 original texts/50 frozen files,
    maps 38 App graph packages/five actual .NET 10.0.12 runtime packs and retains
    one unresolved LTRData.Extensions full-text gap. New locked package-only tool
    refuses changed/missing/extra/path/version/hash/schema/runtime/existing-output
    inputs before copying. Seven actual SC/old-FDD metadata positives and twelve
    refusals verify exact outputs; original index-target controls are preserved
    and two fresh pinned-license cases close that observer coverage gap. Windows/
    Linux/Mac packagers check/copy before archiving or signing. Raw staged bytes,
    coherent mappings and script ordering/parse/build pass. Original whitespace
    diagnostic is retained; frozen-source-only attribute exception preserves
    all texts. V3 corrects v2's unchanged-path label. Summary eligibility/version/
    inventory claims correct. Actual committed packaging/hosted validation and
    full license/native/SBOM/candidate remain Open; counts unchanged, NO-GO.
