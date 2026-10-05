# E-V09-M10 — Committed Mac recovery device removal

Preparation and actual held-authorization removal independently verified, 2026-10-05.
The owner held the dialog until the owned image was detached and both device paths
were absent, then reported that the dialog appeared and was approved. **The original source-safety capture exposes reporting defect I142; a fresh
committed 348cbc7 repeat below passes safety, reporting and cleanup.** This is preliminary component evidence, not full workflow qualification.

Production commit is `8f75856802668f7d21c09f33aa876e4fdc4409d3`. The private
self-contained arm64 component wrapper references byte-identical clean Recovery/Core
DLLs. This follows the verified unchanged approval and current desktop refusal in
[E-V09-M8](E-V09-M8-macos-guard-unchanged-approval.md) and
[E-V09-M9](E-V09-M9-macos-guard-desktop-refusal.md).

## Prepared executable case and oracle

Fresh owned native root is
`/Users/benny/FileCatReleaseValidation/authopen-64f7d42a97274c18a25fe96ccac0ba1f`.
The controller attaches only its owned 25 MiB image without mounting. It verifies
native Disk Image/size/ownership/node identity and restricts the nodes to root 0600.
It starts the root recorder, then an ordinary-user gui/501 component. After observing
the live authopen child, it detaches only that owned image, verifies both original
device paths are absent and the source bytes unchanged, and publishes the ready cue.
The access restriction remains until the nodes disappear; no permission window lets
the held request bypass consent. The owner must approve only after that cue.

The component's removal mode reads no source content. It reports a bounded failure
with no constructed source and no test timeout; an unexpectedly returned source is
disposed and fails the case before content reads. Actual exception/helper-open/timing
and descriptor behavior are independently checked below. A native
failure after removal is distinct from a human refusal; no exact exception is forced.
The controller retains source identity, removal timestamps, helper identity, all
results/errors, trace and cleanup. Its temporary agent and owned image are cleaned
up through the existing guarded route. The private wait fallback is bounded explicitly.

## Verified preparation controls

Cross-publication exits zero by the recorded command receipt. All 201 native/host
input pins and 42 retained pins verify independently. Production DLLs match the clean
references; new wrapper/controller/GUI launcher bytes are separately pinned.

An ordinary gui/501 regular-image control and a direct raw-image control each pass
seven independently computed ranges, read-only descriptor rights and closed/EBADF
checks. An ordinary desktop missing-source control returns IOException, no source
and no timeout before requesting access. Both desktop controls verify UID 501,
normal groups and actual native SessionGetInfo graphical access before component
launch. Their temporary agents are removed.

The ordinary raw-image rehearsal verifies owned attachment/native metadata and normal
detachment. All eight recorded commands exit zero; eight tracked process absences
verify. Source image SHA-256 remains
`19f74a085272a8080035ffb5a649b8b53ae8fe1cdcd323555c89037293a10819`, matching independent
decompression of the repository fixture. No physical device participates. No native
authorization dialog or root removal capture runs during these controls.

## Exact private provenance

Private base is the authorized second workspace's
`FileCatReleaseEvidence/mac-resume-20261005/mac-removal-prepared-v1`.

| Item | SHA-256 |
|---|---|
| Independent preparation `independent-prepared-v1.json` | `be189a600f50d769add693b5f5aa7f7cb25374d36bd3298fc40fdf00c6fcc862` |
| Input archive `inputs.zip` | `b23aae7f4ad7019883ac27fd8d55236a0b823174b107ef725c9ffb1d61780db1` |
| Input manifest `stage.json` | `5adfd8b514341378df8de381d536475d6bf2a5dc0c34fc9269e429553a0cb023` |
| Native collection `collected/prepared-collection-v1.json` | `977097d907c95a1225083ab5512302692f3f7b99adb3877f959d2563bd9ec709` |
| Transport `prepared-outputs.zip` | `23fb84d6c0ad7869437e15378870086f7b12474774595d5a8c115a875457dbac` |

## Actual held approval after removal

Actual component PID 15805 and authopen PID 15808 run as ordinary UID 501 with normal
groups. Native SessionGetInfo verifies graphical access before the component starts.
Selected raw inode 969 belongs to the owned 25 MiB Disk Image. Initial component and
helper opens fail with EACCES (13), using read-only flags. Nodes remain root 0600 until
they disappear; there is no temporary permission bypass.

Owned detachment completes at `2026-10-05T20:42:42.146367Z`, with both `/dev/disk4`
and `/dev/rdisk4` absent, the image unchanged and the ordinary helper still waiting.
Before the approval cue, an independent SSH check also observes missing paths and `501 authopen`. Its attachment query uses `source.img`
instead of the actual `source-fat16.img`, so that independent attachment assertion
cannot qualify detachment. The native controller and collection separately verify
the exact owned image; the fresh corrected probe below uses the exact path. That check exits one because its assertion
expected a full executable path from ps instead of the returned basename. The exact
observation and failed assertion are retained; no successful probe exit is invented.
The owner then reports: "Dialog appeared; I approved it."

At `22:43:57.247158 CEST`, 75.100791 seconds after verified removal, the helper opens
the missing raw path and receives ENOENT (2). Each of the three source opens has a
unique raw/formatted PID/TID/syscall/time link and read-only access flags. No source
descriptor or object is returned, and the test does not time out. The helper exits
one; the ordinary test exits zero for this expected unavailable-source safety case.
The component nevertheless reports OperationCanceledException and "not approved."
That misclassification fails truthful reporting and is the actual baseline for
[I142](E-I142-unix-device-removal-reporting.md).

Two-byte send/receive followed by EOF, authorization channel FD 63 closure and helper
wait/reaping verify. The later pipe reuses FD 63 only after that close; its separate
closure is independently tracked. No artificial kill is observed. All seven recorded
controller commands, the recorder and both offline decoders exit zero. Source bytes
are unchanged and detached, the temporary GUI agent is removed, and nine tracked
process absences verify. All 201 input and 57 retained output pins verify on native
and host sides.

Raw capture has 8,272,489 events over 128.007111750 seconds; completion is derived from
the measured span and the configured 120-second recorder's zero exit. Four unpaired
starts are nonreturning thread termination/process exit. Three calibrated finite loss
marker IDs are absent; zero-loss, mapping, whole-source/helper coverage and drawn
workflow qualification are not inferred. Human approval timing is an attestation,
not an independently measured click timestamp.

Private executed root is `mac-removal-executed-v1`, beside the preparation root above.

| Item | SHA-256 |
|---|---|
| Independent actual `independent-executed-v1.json` | `1f39017853f7b38500555ac42199c7c8394cc5241f1bfba1e300faee465a96f6` |
| Collection `retrieved/executed-collection-v1.json` | `f96bbfc6f4d200c2bcdbff7a255388d199496e6760d09cc946efb2363f4e7ac2` |
| Transport `outputs.zip` | `7e10af381ef7928e4b71f6382c88b56b7bc796cadd24933d931e58fb8adf9259` |
| Raw `retrieved/results-remove/capture.ktrace` | `0f2758ec5060dd3369353d98e6e1fcff0a0640b5756c99431b8e2d012e38afb9` |
| Diagnostic `raw-diagnostic-v1.json` | `9e27a79702c80e62922aa36e4097e8a3ac43719a98f8c71597fdb542df9e6dd0` |
| Owner attestation `owner-attestation-v1.json` | `54ef5c1a76481331d7d500413e43306bf1225699193b2d5f7a931caa42b18b04` |
| Removal marker `retrieved/results-remove/source-removed.json` | `6165e0e088e0818faac723ef7c0ba87adbb1505c5f0538d9ae965437d1e1fc08` |

Temporary Mac
sleep-disable and restorer were active at capture time, with restoration due when validation ends
([E-ENV-MAC-1](E-ENV-MAC-1-temporary-native-session.md)). Broader topology/helper/drawn
workflow and final candidate qualification remain. I140 is not Closed. No candidate
or human GO. **NO-GO** remains.

## Fresh committed removal repeat with truthful reporting

Production `348cbc703140b1b4d84a05b75185d268ae5fc8f6` is published from the raw Git blob
export described in [E-I142](E-I142-unix-device-removal-reporting.md). Preparation v2
uses Recovery/Core DLLs byte-identical to that clean Mac regression payload. The new
201 input/42 retained pins, seven-range regular/raw controls, read-only/closed descriptor
checks, missing-source desktop refusal, two agent removals/eight absences, unchanged
source/detachment and all eight commands independently verify before the real case.

Fresh native root is
`/Users/benny/FileCatReleaseValidation/authopen-e2bf150341e74660a88c055d5b68bfbb`.
Actual component PID 17636 and authopen PID 17639 are ordinary UID 501 with normal
groups; actual graphical SessionGetInfo is verified before launch. Owned image
detachment completes at `2026-10-05T21:18:34.045473Z`. The native marker and successful
independent pre-cue probe verify both device paths absent, the exact owned image no
longer attached, unchanged source bytes and the live ordinary helper. The owner then
reports: "Dialog appeared; I approved it."

Helper read-only open at `23:19:28.697146 CEST` receives ENOENT, 54.651673 seconds
after removal. All three source opens uniquely match raw/formatted PID, TID, time
and syscall; initial app/helper opens receive EACCES. The initial selected-entry
stat succeeds, and the post-helper EOF/reap path stat uniquely links to ENOENT.
The actual component now reports IOException: "The selected device changed or was
removed while opening. Select it again." No source is constructed and no test
timeout occurs. This passes truthful unavailable-source reporting separately from
the unchanged-refusal and explicit-cancellation regression controls.

Two-byte authorization send/receive then EOF, channel FD 63 closure, helper exit one
and reaping verify. Later pipe reuse/closure of FD 63 is tracked as a distinct
lifetime. No artificial kill is observed. Worker, recorder, two decoders and all
seven controller commands exit zero. Source bytes remain unchanged and detached;
the temporary GUI agent is removed, nine owned process absences and all 201 input/57
retained pins verify independently.

The raw trace contains 6,413,688 events over 127.965095166 seconds, exceeding the
configured 120 seconds with recorder exit zero. Four unmatched starts are native
nonreturning exits/thread termination. The three finite loss IDs are absent; this
does not prove zero loss, whole-source/helper/mapping or drawn-workflow qualification.
The actual approval is owner-attested; no measured click timestamp is invented.

The generated owner v1 file retained the older readiness-question wording. Its bytes
remain unchanged; owner v2 corrects only that transcription, pins the original file
and records the exact final readiness/approval questions and answers. Test outcomes
and the final approval answer are unchanged.

Private preparation root is `mac-removal-prepared-v2`; execution root is
`mac-removal-executed-v2`, under the private base above.

| Item | SHA-256 |
|---|---|
| Preparation `independent-prepared-v1.json` | `218d6869eab8a879fb543212efcc4a19a7ef8bcfe98d6ee4fb6226ff38b4190d` |
| Input manifest `stage.json` | `950bbd87eb37325bcc887ae1c2cd67ead84499e68fa0c3d61fd32aef618a8824` |
| Actual `independent-executed-v1.json` | `b990dff20015d290b00c945851b4c810e9d466e65f9f8db4f6cf081786e38692` |
| Path recheck `independent-path-recheck-v1.json` | `9f3449ca282a8f71b0d134b59658cc69cb013dabcb68baa30e1f4249d90b8425` |
| Collection `retrieved/executed-collection-v1.json` | `2bb16e18bb8681cb8f6070d93a6818fc9a9a8a6e92d9e2ab767872bf4f8e3a98` |
| Transport `outputs.zip` | `c9fc83fef21b2de3112b3e00984a6a975a656d741edbec182e4a4a81ce0a05fa` |
| Raw `retrieved/results-remove/capture.ktrace` | `f4a833c713505bb518b8e811676518359d47baf1c0f73a4b7554bb523fb8e54d` |
| Diagnostic `raw-diagnostic-v1.json` | `009eb9d734f2e911755e9fd5e05d841498c119a70f6c6741a1df3347d9a28c7d` |
| Owner `owner-attestation-v2.json` | `20816e07fdec3092e0b9b1e04c968f4b00ceb02867d785ae2184765d5d8556b8` |
| Removal marker | `bcf2aa85aad8792431870a25c536c4f4f0dbd4a869cb65dd2b33c83a70e7048f` |

The original 8f75856 failure remains the baseline. No further Mac interaction is
queued; exact-source CI retry passes all four required lanes, server digests and
complete inventories (E-I142). Broader/native workflow/candidate qualification remain. Temporary
power support is now independently restored after the subsequent clean icon tests,
with both owned root restorers and caffeinate absent (E-ENV-MAC-1). **NO-GO** remains.
