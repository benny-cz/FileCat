# E-V09-M9 — Committed Mac recovery component refuses desktop authorization

Verified preliminary native component result, 2026-10-05. Production commit
`8f75856802668f7d21c09f33aa876e4fdc4409d3`; Recovery/Core DLLs are byte-identical to
the clean producer previously verified in [E-I140](E-I140-unix-device-authorization-identity.md).
This fresh case follows the unchanged-device approval in
[E-V09-M8](E-V09-M8-macos-guard-unchanged-approval.md). No production change or
candidate qualification follows from this private instrument.

## Actual desktop-context refusal

Native environment is the physical macOS 27.0.1/26A434 arm64 Mac, ordinary benny/UID
501. Owned root is `/Users/benny/FileCatReleaseValidation/authopen-f9deb49d2a9c4839bef1a16c7ff027f1`.
The agent starts the detached root recorder over credential-safe SSH; sudo exits
before the component starts. A temporary interactive launch agent in `gui/501`
starts the ordinary wrapper. Native SessionGetInfo verifies UID 501, graphical
access and session 100014/attributes 24624 before the recovery component runs.
Normal account groups remain unchanged. The owner attests cancellation in response
to the specific dialog question; the exact event timestamp is not independently known.

The only source is an owned 25 MiB FAT16 image, attached without mounting as UID 501.
Its raw node `/dev/rdisk4` has inode 957; native disk metadata and inventory establish
the owned Disk Image/size/attachment. Root temporarily restricts its block/raw nodes
to mode 0600. The ordinary-user permission check confirms it cannot read the raw node.
No physical source device participates.

Actual component PID 14930 and native authopen PID 14933 both run as UID 501.
The component returns `System.OperationCanceledException`, reports no constructed
source and no test timeout, and exits zero. Native stderr reports user cancellation.
The helper exits 1 on this expected refusal; it is reaped. No artificial kill syscall
is observed in the two owned processes. This is actual human cancellation through the
desktop security session, rather than the earlier unavailable SSH interaction context.

## Independent byte, trace and cleanup checks

All 201 input pins verify before/after on the Mac and against host input bytes; the
197 original inputs are preserved. All 55 retained output pins and the transport
archive verify independently. The attached source and final detached image retain
SHA-256 `19f74a085272a8080035ffb5a649b8b53ae8fe1cdcd323555c89037293a10819`, matching
independent decompression of the repository fixture.

Both formatted source opens link uniquely to raw syscall pairs: the component and
helper each receive EACCES/13 with read-only access flags. No successful source open
is observed. Helper sendmsg and the first component recvmsg transfer two bytes; a
second recvmsg returns EOF. The component then successfully closes authorization
channel FD 63 and waits for the helper. Subsequent pipe creation explicitly reuses
FDs 63/64; its later close is tracked as a separate resource lifetime.

Recorder configuration is 120 seconds, classes C3/C4/C7 and a 256 MiB buffer, with no
process filter. Its exit is zero. The decoded raw timestamps independently span
128.003514583 seconds across 3,064,665 events; capture-duration completion is derived
from these measurements and the recorder metadata. Both decoders and all seven
recorded attachment/info/permission/agent/detachment commands exit zero. The temporary
agent is removed, the image is detached, and nine tracked processes are independently
absent: controller, recorder, wrapper, metadata control, component, helper, attachment
daemon and two decoders.

Four syscall starts are unpaired: two thread terminations and the two process exits.
The three previously calibrated finite loss-marker IDs are absent. These observations
do not establish zero loss, mmap backing coverage, complete helper behavior, a drawn
FileCat workflow, broader V09, or final candidate qualification.

## Instrument repairs and retained failures

The first local verifier expects older `BSC_close` names and fails. The actual decoder
reports `BSC_sys_close`/`BSC_sys_close_nocancel`. Its corrected check uses those measured
names and explicitly separates channel closure from later pipe FD reuse. The original
verifier and failure receipt remain retained; source, capture and native case are unchanged.

Automatic approval review rejects an edit that directly changes a capture-complete
flag. That action does not execute. A separate safer analyzer instead computes the
flag from the recorded duration, recorder exit and actual first/last raw timestamps.
The independent verifier recomputes the span from those timestamps. No historical
failed capture or false receipt is rewritten.

## Exact private provenance

Private base is the authorized second workspace's
`FileCatReleaseEvidence/mac-resume-20261005/mac-gui-refusal-executed-v4`.

| Item | SHA-256 |
|---|---|
| Independent result `independent-executed-v4.json` | `cec62fcc7e528b7cc69e980ef6f74fd959d3f5bdc5f4438349dc40b516193800` |
| Raw capture `retrieved/results-decline/capture.ktrace` | `33e4ca6c3078599cb1bb5bbe0f07227cf0498154bbc0c816b2e728bb6d70a110` |
| Collection `retrieved/executed-collection-v1.json` | `43505d09b523803559da306d4b903d22bb922673fd6db16183f89a7777d4ef8c` |
| Transport `outputs.zip` | `384111ad794133c21081c91e770d5b5148c5660ee810e6b01b73ff7e481a7ab2` |
| Raw diagnostic `raw-diagnostic-v1.json` | `04178b55e1c0d7d2e1410c0c914f159d9d744e7df78cbe64512e5ca96e6fa716` |
| Owner attestation `owner-attestation-v1.json` | `a0ea867045c02ddf05f919985c9d5de0fc3d4b3f2b3f23f65d449f63ae748515` |

The preparation's 201 input/21 retained pins, seven golden ranges, read-only closure,
normal desktop session and three absences are independently sealed in
[E-ENV-MAC-1](E-ENV-MAC-1-temporary-native-session.md), which also records the temporary
closed-lid sleep setting and outstanding restoration obligations.

Current native device removal, broader topology/helper/drawn-workflow and candidate
qualification remain required. I140 remains preliminarily Remediated, not Closed.
No candidate or human GO. **NO-GO** remains.
