# E-V09-M10 — Committed Mac recovery device removal

Preparation and actual held-authorization removal independently verified, 2026-10-05.
The owner held the dialog until the owned image was detached and both device paths
were absent, then reported that the dialog appeared and was approved. **No source is
constructed and cleanup passes; failure reporting is incorrect and is tracked as
I142.** This is preliminary component evidence, not full workflow qualification.

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
Before the approval cue, an independent SSH check also observes missing paths, no
owned attachment and `501 authopen`. That check exits one because its assertion
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
sleep-disable and restorer remain active with restoration due when validation ends
([E-ENV-MAC-1](E-ENV-MAC-1-temporary-native-session.md)). Broader topology/helper/drawn
workflow and final candidate qualification remain. I140 is not Closed. No candidate
or human GO. **NO-GO** remains.
