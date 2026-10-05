# E-V09-M4 — Mac complete recovery session and retained raw trace

Preliminary execution, 2026-10-05. Follows [E-V09-M3](E-V09-M3-macos-full-session-context.md)
and [E-V09-M2](E-V09-M2-macos-trace-calibration.md). No production source, permission policy,
dependency or recovery oracle change. Actual SDK-free App remains clean
`593583e585d4a79cbb7ff961770a2d826858e14d`, payload manifest SHA-256
`f3b8e93a97f0d4a9383548f5d1d7fcc98ad32d0084d2cb8558d9f235bafe1288`.
This separately pinned payload is not a release candidate.

## Retained v11 recorder-only failure and v12 correction

V11's separate-session elevation lets sudo exit; its preflight verifies no live sudo process.
The detached root launch uses umask 077. Its ready marker is created root-only, so the ordinary
watcher gets EACCES and exits before starting the actual worker. No FileCat/device runs.
The recorder reaches its bounded interval; the controller fails waiting for worker completion.
All 25 retained pins, 68,794,800 raw bytes and three tracked owned absences verify. The failure
is preserved, not counted as a source-write test.

Fresh v12 atomically publishes a complete mode-0600 marker owned by benny/UID 501. All 1,196
native payload pins, 26 input/transport pins, six complete dry-run files/four control absences
and the separate-session harmless detachment control verify at staging. No production
admission exemption is added. The owner runs the actual launcher.

## Actual full session — passed with retained supervisor failure

The ordinary worker retains UID 501/GID 20 and its normal supplementary groups. The actual
case passes **1/1**, zero skips/errors, command exit zero. It scans the owned, unmounted,
writable 25-MiB FAT16 image, reads as the viewer would, copies tiny.txt with Completed,
waits **70 seconds** for timed saving and closes. Native drawn F3 is not exercised.
The complete recovered **60 bytes** match an independent literal filename/numbered-line
generator; SHA-256 `2c9aed8755e52ee19e225e615a4cb8a11fbc509343a3e0e93618d2cfe089fda3`.

Source image before/after SHA-256 is independently equal:
`19f74a085272a8080035ffb5a649b8b53ae8fe1cdcd323555c89037293a10819`.
Independent native inventory verifies detachment, and the owned temporary folder is clean.
All **104 input/output pins**, **18 tracked owned absences**, and **15 additional derived
diskutil child absences** verify. The full logical source image is hashed on the Mac;
it is not downloaded. Source hashes are byte/cleanup evidence, not a write-trace substitute.

The root supervisor's strict string identity check fails at 12:32:42 UTC, before the actual
App starts. Its cleanup repeats the conservative refusal and leaves root-owned outputs.
The independent worker continues successfully; the bounded recorder also continues.
The mismatch's exact cause is not proved. Earlier speculation about a watcher-exit race
is superseded by the actual timestamps. Both original errors remain retained.

The owner runs a scoped access seal after it verifies the recorder/controller have exited
and the raw file is stable. It changes only owned result access for ordinary retrieval;
it neither reruns FileCat nor opens a device. Offline raw and formatted decoders then both
exit zero as UID 501. The original recorder's command exit was **not captured**; its own
Trace completed message and complete decoded output are retained without inventing that exit.

## Independent native observations and qualification limit

Raw capture decodes **2,669,238 events over 115.488 seconds**, matching recorder metadata,
from a C3/C4/C7 capture with a 256-MiB buffer and no PID/name filter. Raw size is
**176,638,936 bytes**. TRACE_LOST_EVENTS, TRACE_RETROGRADE_EVENTS and TRACE_PAST_EVENTS are
absent; no loss/overrun report appears. This finite observation is not universal zero-loss
qualification. All **430 known before/after read/write controls**, six processes/eight
native TIDs, twelve whole control files and observed native thread births independently match.

Actual App PID 8701 has **33 native TIDs** with matching birth metadata and fifteen direct
children that exec diskutil. Each child has native syscall observations; no further child
fork is observed, and all fifteen derived identities are absent afterward. Fifteen unmatched
thread-terminate starts are nonreturning lifecycle observations, not missing ordinary I/O
pairs. Broader helper/future-process qualification remains open.

There are **two native read-only source FD lifetimes**: the availability probe FD 164 opens
and closes without reading; recovery FD 140 opens, issues two size/count query ioctls,
performs **eight successful positional reads totaling 264,192 bytes**, and closes after the
70-second wait. No source-FD writes/truncations, dup/fcntl aliases or forks while those FDs
are open are observed. The service callback's one device-open count refers to the recovery
open; it does not erase the native availability probe. Formatted paths/FDs and raw entry/end
records agree; incorrect formatted positional offsets are never used.

Actual App also has **270 mmap calls**, including **79 nonanonymous shared mappings** with
initial PROT_READ. The raw format still omits backing FD/offset and has no extended FD events.
Read-only initial protection does not resolve source attribution. Under the previously
stated conservative rule, **full source-write qualification remains pending**. No universal
zero-write, authopen, native desktop or candidate pass is claimed.

## Next concrete instrument gate

Ordinary provider inventory on this exact Mac reports additional privileges required and
warns that SIP limits features. Installed dtrace SHA-256 is
`b510c2d13b953b98d37f397a21669e1390d835ff248784db23e33f0b70950381`.
This does not establish whether the needed kernel provider works under root.

A bounded, pinned provider/known-mapping control is staged as
`~/FileCatReleaseValidation/ProbeMmapProvider-20261005-v1.command`, native root
`/Users/benny/FileCatReleaseValidation/mmap-fd-431448f4cf2e44f5a6171925d3e1cdb1`,
stage manifest SHA-256 `afd7de07aeafbd664adcdf6101d6e339e6f145379fb92d0adfacc061bfaf9bda`.
It inventories syscall::mmap:entry and, only if available, runs renamed byte-identical
owned C controls as UID 501 in small owned files. It never runs FileCat or opens a device,
and makes no SIP/system-setting change. Four input pins and native transport hashes verify;
actual privileged execution/calibration remain pending owner-local sudo. No FD argument
interpretation is accepted before positive calibration.

## Retained private provenance

Private base: authorized second workspace's FileCatReleaseEvidence/mac-resume-20261005.

- session-v11-marker-failed/independent-marker-v11.json SHA-256
  `3c1dc2e21e90a2c0fbbf812e85e9d1fe5e9c88394de5ec3dda43325b00ecf12a`:
  recorder-only failure, 25 pins/three owned absences/no actual App/device.
- session-stage-verified-v12/independent-stage-v12.json SHA-256
  `e4ef2443ebdf04f11c73cf60b5f4e1fe452b3211901177f52b20b04f2c58404f`:
  historical not-executed staging checkpoint, 1,196 native/26 input pins/six control files.
- session-executed-v12/independent-executed-v12.json SHA-256
  `26324be589d270072fb79db23e46c5d0b7b30d5f859fb5d5bdb3177162bc22b6`:
  104 pins, actual case/generated bytes/source/cleanup, native controls/FD lifetimes,
  shared mapping attribution gap and original supervisor failure. A local verifier's
  wrong decoder filename is retained; correction uses the actual pinned filename and
  does not alter native outputs.
- session-executed-v12/derived-child-syscall-check-v1.json SHA-256
  `abe3d42a0e9ef74473cb9d4e00b8a4ae1c247a0730b7b159813b3f8354a65349`:
  all fifteen derived children have native syscall events, no further fork observed.
- mmap-provider-preflight-v1/native-preflight-stdout.json SHA-256
  `c83f42229d947c58b21974b09c8a22bf46a2de1ae845a102fa3c7f7b896d7046`:
  ordinary provider inventory/error, not a capture or qualified FD decoder.
- mmap-provider-staged-v1/native-stage-stdout.json SHA-256
  `dfeb34c4c94a9456f953a83fdf13399bb3822c05935a648b29ed4c22a019f517`:
  concrete root launcher/four inputs/stage pins, explicit not-executed checkpoint.

Separate docs-only 09e42e7 CI 37309749235 passes all four required jobs. Four server digests,
six complete inventories and 24 affected viewer cases verify; ARM64 uses log totals, no
per-case TRX or physical qualification. Private ../ci-37309749235/independent-ci.json SHA-256
`241ece99816fb59d3ef07ff3ee271deca0f7d2e491b0994f78e7854a59357385`.
Initial GitHub connection/log-read failures are retained; successful reads preserve their
own provenance and do not imply those failed transports passed.

V09 full source-write/authopen/native/candidate qualification remains open. Progress:
**117/139 issue rows remediated**, one separately Closed; **24/26 checklist steps partly
or fully open**. Both VMs stay running; G: untouched/HOLD. No candidate or human GO; **NO-GO**.
