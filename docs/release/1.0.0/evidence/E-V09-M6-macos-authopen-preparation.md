# E-V09-M6 — Mac native authorization refusal and retained approval attempt

Native component execution and direct-read controls, 2026-10-05; follows
[E-V09-M5](E-V09-M5-macos-combined-mapping-session.md). **The separate v2 native
authorization refusal passes.** The first attempt was approved by the owner and remains
a failed refusal test. A fresh approval/read-range case is prepared, awaiting local authentication.

The private, self-contained arm64 validation wrapper calls the actual UnixDeviceSource.Open
implementation. Its FileCat.Recovery.dll and FileCat.Core.dll are byte-identical to the
separately pinned clean `593583e585d4a79cbb7ff961770a2d826858e14d` Mac payload. Production,
dependencies and permission policy are unchanged. The wrapper is a validation instrument,
not a release artifact or a drawn FileCat workflow.

All **197 staged input/payload pins** verify. Native ordinary UID-501 controls pass over
both a regular fixture file and its owned, unmounted raw image device. Across the two modes,
**fourteen independent read-range checks** match the separate golden-image reader: prefix,
unaligned offset, 5-MiB bounded read, short EOF, EOF and negative offset. Returned FD access
mode is O_RDONLY and closure returns EBADF. Command exits are zero. The 25-MiB FAT16 source
remains byte-exact, SHA-256 `19f74a085272a8080035ffb5a649b8b53ae8fe1cdcd323555c89037293a10819`.
Detachment, seven owned process absences and the harmless separate-session launcher verify.
No authopen call or permission change occurs during these controls.

Installed /usr/libexec/authopen SHA-256 is
`109a0ef0f2dfdc024b1210a5d7d57c3398e4c1793896c77bdf46a6f202688bdc`;
native codesign verification exits zero. This verifies the installed helper, not a FileCat
distribution signing or Gatekeeper policy.

## Historical preparation

The original `AuthopenDecline-20261005-v1.command` requested sudo in Terminal, followed
by cancellation of the separate native authorization dialog. Its preparation proof below
records the then-pending owner activity; it is not rewritten as an execution result.

The pinned root controller verifies the owned image's hash, native Disk Image identity,
unmounted size and attachment ownership before restricting only its temporary block/raw
device nodes. It drops the worker to benny/UID 501 with normal groups. The direct-read
preflight must fail, making FileCat's actual authopen route necessary. The wrapper requires
OperationCanceledException with the not-approved result, without a test timeout or source
returned. A 120-second unfiltered raw capture and bounded worker/process observations are
retained for independent interpretation afterward.

The controller verifies device identity before restoring original node permissions and
detaching the owned image. Source-byte mismatch stops this path. It keeps SIP enabled;
the physical USB remains untouched/HOLD. There is no source-device selection by guessed
disk number: the selected kernel node must belong to this exact attached owned image.

## First actual attempt: approved, refusal expectation fails

Native root: `authopen-98779a0ee8c944ffbbde42c9ab9b0688` under the private native base
below. Root controller 10460 starts the ordinary UID-501 wrapper 10477 after sudo exits.
Only the verified owned image's temporary device nodes are restricted. The ordinary
permission preflight fails as intended. Raw/formatted event pairs independently identify
the wrapper's direct source-open EACCES, helper 10488's initial EACCES and its later successful
O_RDONLY source open. The owner reports: **“Dialog appeared; I approved it.”**

UnixDeviceSource.Open returns a source, so the decline-mode wrapper deliberately throws
`Declined request returned a source` and exits -6. The controller assertion and original
stderr remain retained. This is **not a refusal pass or an established production defect**.
The wrapper does not execute its returned-FD F_GETFL or seven read-range checks in this
mode. Raw helper send/worker receive events, source-size ioctls on received FD 62 and its
successful close are observed. No approved read-range pass is inferred from them.

The recorder stops during failure cleanup: **1,078,760 raw events, 20.394 seconds** of
observed event span, 74,451,096 raw bytes. Its exit zero is not a completed 120-second run.
Both ordinary offline decoders exit zero. All 51 retained pins, 197 input/payload pins and
seven owned absences verify. Source bytes are unchanged and the image is detached.

## Fresh v2 attempt: native cancellation verified

Native root: `authopen-0b3e4f52057d4356ab4a893d95e95d05`. This fresh run preserves v1
inputs/results. The owner runs `AuthopenDecline-20261005-v2.command` and reports
**“ran and cancelled”** in response to the explicit request to cancel the separate dialog.
The ordinary UID-501 wrapper 10755 invokes the actual pinned FileCat implementation;
root controller 10737, authopen 10764 and recorder 10754 are separately identified.

The component returns **OperationCanceledException**, `Reading /dev/rdisk4 was not approved.`,
**SourceOpened=false** and **TestTimeout=false**. Native helper stderr says authorization
was canceled by the user. Worker, recorder, attachment/info/permission-check/detachment
commands and both ordinary offline decoders exit zero. The controller has no failure or
cleanup errors. Both initial source-open attempts match raw/formatted events and fail
EACCES; no successful source open appears in those attributed events. The worker receives
the helper's two-byte status and then EOF; no device-size ioctls are observed.

The unfiltered C3/C4/C7 recorder completes its requested 120-second command. The retained
raw file has **102,017,296 bytes**, **1,502,267 events** and **125.956 seconds** of observed
event span. Wrapper/helper have twelve/three observed native TIDs. The three unmatched
syscall starts are exit/bsdthread_terminate; all ordinary paired calls remain retained.
The three inspected loss-marker IDs do not occur. This is finite observation, **not a
whole-source or zero-loss qualification**; mapping backing coverage is not established here.

All **42 retained pins**, **197 input/payload pins** and **seven owned absences** verify.
The 25-MiB source remains SHA-256 `19f74a085272a8080035ffb5a649b8b53ae8fe1cdcd323555c89037293a10819`.
Independent native inventory confirms detachment; physical USB is untouched/HOLD, SIP
remains enabled. This verifies native component refusal with human attestation, not a
drawn FileCat recovery workflow, application admission or exact-candidate qualification.

Independent observer corrections remain retained: formatted `open` normalizes the raw
open_nocancel name; raw close uses sys_close names and FD 62 has distinct socket/source
lifetimes. The corrected verifier pairs native PID/TID/end time and limits closure to the
source lifetime. A copied v1 question label in v2's first local attestation record is retained;
the corrected record names the actual v2 request. Native inputs/results are unchanged.

## Next owner-local case: approval and descriptor reads

Run `~/FileCatReleaseValidation/AuthopenApprove-20261005-v1.command` on the Mac.
Enter sudo in Terminal, then **approve the separate native macOS authorization dialog**.
The fresh root is `authopen-dc067d3f608e464b9cef0f6545dc0e0f`. All 197 copied pins, installed
helper identity and launcher readback verify. A separate ordinary direct-file control passes
seven independently checked golden-image ranges, O_RDONLY/closure and two process absences.
**This fresh authorization case has not run.**

After native approval, this case requires the actual returned FD's F_GETFL access mode to
be O_RDONLY, seven byte-exact reads including unaligned/5-MiB/EOF bounds, and immediate
EBADF after disposal. It records the raw session, verifies unchanged source bytes, restores
only identity-checked owned nodes and detaches the image. It does not exercise a physical disk.
Local authentication is still necessary because SSH cannot provide it. Plan V09 and §12.3
require native consent/human attestation. Full source-write/helper, drawn application desktop,
removal/adverse topology and candidate qualification remain open.

## Private provenance

Private base: authorized second workspace's FileCatReleaseEvidence/mac-resume-20261005.
Native base: `/Users/benny/FileCatReleaseValidation/`; roots above are separately preserved.

- authopen-prepared-v1/independent-prepared-v1.json SHA-256
  `fbc9403ebd8b4f24cffe692d6c151dd2036928bd8f23c791eca66202e5ac3c60`:
  all inputs, unchanged production DLLs, both native direct controls/independent bytes,
  source/cleanup, installed helper and owner-execution-pending state.
- Native stage manifest SHA-256
  `7f71681461fbff82f0e6c7de24fddbbaf97306e8e3518901e30c2bdf13577da5`.
- Native LF command script SHA-256
  `7ea39e3cde1c81e3b41c20d9e8d46a3a858bd42a88934169dc90fa8ec9abb574`.
  Local CRLF copy is separately pinned. An independent observer's mistaken byte-equality
  assumption is retained; verification compares normalized source and the native file hash
  without altering native inputs/results.
- authopen-executed-v1/independent-executed-v3.json SHA-256
  `0432cad8cd6e8b56d20ac95bee6148c240a016940b46bb3509d869b4e4ba133e`:
  actual approval, failed refusal expectation, raw/helper/source observations and cleanup.
  Raw SHA-256 `2f5c5eb4a8653332bc6470551b58316ddbe3d0cc500228755951245d611e44bb`;
  transport ZIP `422845450ffba6f02da2869574986dc69a78a2d052b37870fc746d4c7eee5198`.
- authopen-prepared-v2/independent-prepared-v2.json SHA-256
  `1486d7e393f3c517ec829dc88c72cd7c88d291447dbb574086efced246f9a379`:
  separately verified preparation of the successful refusal run.
- authopen-executed-v2/independent-executed-v1.json SHA-256
  `86470bff671acd6f0c7910136a444fed59a15ed1cd5c85aedaa1b5053695dfae`:
  actual cancellation, component/native observations, all pins/exits/source and absences.
  Raw SHA-256 `4510f0ab4bb185f37452118be4d6ffc043a6cec837910ba5b71cf88fab7ad421`;
  collection `1d1259c51b807ff097b70b2f9cec031ff0e1b07993f5219462e4ddfc7211a1c7`;
  transport ZIP `537c5bf57a21c61681fd2f60d43f32d575ce63df9cc16ef6385c539b61b509cd`.
- authopen-approval-prepared-v1/independent-prepared-v1.json SHA-256
  `53a4d1319f50750dfb53f6396fc5286d59ad925f4557933bfbe72122b59689b0`:
  fresh approval case's verified preparation; owner execution still pending.
  Native LF approval launcher SHA-256
  `43ce7899113198f6b56997532e012e48a5a4f84307b5c88372225bec9d35059e`.

No issue closure, candidate or human GO. **NO-GO** remains.
