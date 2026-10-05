# E-V09-M10 — Committed Mac recovery device-removal preparation

Preparation independently verified, 2026-10-05. **Actual authorization held across
device removal is not yet executed.** The next case requires owner-local dialog
interaction: hold the dialog until the agent verifies owned-device detachment, then
approve. No passing removal result is inferred from the preparation.

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
and descriptor behavior will be independently checked in the raw trace. A native
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

Owner readiness/approval timing is still required for the actual case. Temporary Mac
sleep-disable and restorer remain active with restoration due when validation ends
([E-ENV-MAC-1](E-ENV-MAC-1-temporary-native-session.md)). Broader topology/helper/drawn
workflow and final candidate qualification remain. I140 is not Closed. No candidate
or human GO. **NO-GO** remains.
