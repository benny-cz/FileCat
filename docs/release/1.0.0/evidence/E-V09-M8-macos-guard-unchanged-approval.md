# E-V09-M8 — Committed Unix guard unchanged-device approval

Verified preparations and retained first-run failure, 2026-10-05. The owner reports
approving the fresh v3 dialog; its component checks pass read-only rights, seven byte
ranges, closure, unchanged source and detachment. Independent raw verification remains
pending. The completed v2 launcher is single-use and is not rerun.
This follows two corrected replacement passes in [E-V09-M7](E-V09-M7-macos-authopen-device-binding.md)
and [E-I140](E-I140-unix-device-authorization-identity.md). Earlier successful approval/refusal
at 593583e remain historical in [E-V09-M6](E-V09-M6-macos-authopen-preparation.md).

The private self-contained osx-arm64 wrapper uses Recovery/Core DLLs byte-identical to
the clean committed `8f75856802668f7d21c09f33aa876e4fdc4409d3` Core producer. It calls
the actual UnixDeviceSource.Open and checks returned F_GETFL read-only rights, seven
independent golden-image byte ranges, closure and EBADF. It is a component instrument,
not a drawn FileCat workflow, package or candidate.

## Independently verified setup

All **197 input/payload pins** and **20 retained preparation pins** verify. Two ordinary
UID-501 controls open the regular image and its owned raw image device. Each returns a
read-only descriptor, seven byte-exact ranges and verified closure; Windows independently
checks every range against the decompressed repository FAT16 fixture. The source contains
25,165,824 bytes, stays byte-identical and is normally detached. Four observed owned
processes (two direct controls, detached marker control and image daemon) are absent.

The controller tracks attachment attempts before hdiutil. If attachment partially succeeds,
cleanup finds only the exact owned canonical image, checks UID 501, Disk Image protocol,
25-MiB unmounted size and recorded node identity, then restores recorded attributes and
normally detaches. This safety revision is retained beside the original controller; the
partial-attach branch was not fault-injected. No forced detach or physical disk is used.
The failed local extraction (missing stat import before any member write) is retained;
a separate host-only resume verifies the existing transport and extracts into the empty
owned directory without rebuilding or rerunning native setup.

## Retained v2 authorization timeout and fresh v3 repeat

The owner reports that the v2 native dialog appeared and was approved. Its ordinary
worker nevertheless receives no descriptor: two paired source opens return EACCES,
recvmsg waits 89.974145708 seconds and returns zero bytes after the 90-second cancellation
callback successfully sends SIGTERM to the helper. The worker exits -6 with an unhandled
cancellation in the approval probe. This is a failed approval expectation, not evidence
of human refusal. The approval's event time and why authorization did not finish remain
unknown; no production defect is established by this run.

The shortened raw capture has 3,626,642 events over 90.842114541 seconds. Recorder exit
zero, five cleanup commands and both offline decoder exits zero, 54 retained/197 input
pins, the unchanged detached source and seven owned absences independently verify.
One unpaired nonreturning thread termination and finite inspected loss-marker absence
remain explicit. The requested 120-second capture did not complete.

The owner-requested fresh v3 repeat uses new native root
`authopen-d91b3c9e788d4d46ab07533edda7d696` and
`~/FileCatReleaseValidation/AuthopenApprove-20261005-v3.command`. The same clean component
bytes, 197 input/20 preparation pins, regular/raw seven-range controls, source/detachment,
four preparation absences and detached launcher verify independently. The owner reports
that the dialog appeared after a while and was approved. Actual component worker 13775
returns FD 64, read-only F_GETFL, all seven ranges and closed/EBADF. Native worker/recorder/
cleanup report zero exits and source/detachment; independent raw collection is in progress.
The test's original 90-second wait and assertions remain unchanged.

## Native interaction and remaining gate

`~/FileCatReleaseValidation/AuthopenApprove-20261005-v2.command` requests local sudo,
starts the detached controller and requires sudo to exit before the ordinary worker.
The owner must **approve the separate native macOS authorization dialog**. This case
keeps the owned device unchanged; no held replacement is performed. The requested raw
capture is 120 seconds, with offline decoding and independent native operation pairing
still to follow. SSH's retained AuthorizationCreate failure cannot supply this dialog.

Native component v3 results are reported above; independent raw/pin/source/owned-cleanup
verification and syscall coverage/loss interpretation remain pending until collection. Fresh refusal/removal, broader helper/mapping/
source-write/native workflow and exact-candidate qualification remain. SIP stays enabled,
VMs stay running and physical G: stays untouched/HOLD. No candidate or human GO; **NO-GO**.

## Private provenance

Private base: authorized second workspace's FileCatReleaseEvidence/mac-resume-20261005,
`i140-approval-prepared-v2/`. Native root:
`/Users/benny/FileCatReleaseValidation/authopen-1fb88445a9794fc4acff373b2ddc6c44`.

- Independent setup proof `08b77b6381f283cda8b31738e188d95c6b2e851bbec65a694789201ade67eba3`.
- Stage `6841a2eb95e15870b2c14d9c3d50a39a5c8fe1e1b0a6b249284ad9ea5a5413dc`.
- Input ZIP `e48bb6a8d08cac512c15010d2e0441db2f8acdaeb0c2a67cc5bab3520ffc8605`.
- Native LF launcher `cf1ffd783777bf88082fa30001843d5d5bb2e217ff03b6c269eebf310546baf3`.
- Collection `1c15309049b8c01a4dd4b4db4ef268a20492514f1da7dd85f230009173d70d9a`.
- Transport ZIP `e4558909c8f6ba3aa9996a08e3f48c619587adb19d5837f3c601ff9d9a22af07`.
- Golden source `19f74a085272a8080035ffb5a649b8b53ae8fe1cdcd323555c89037293a10819`.

- Failed v2 private `i140-approval-failed-v2/independent-failed-v2.json` SHA-256
  `ef85960d071f21c48b2cb256d3343c9b2a1b00309dc69ca60dcb9fb296bedc5e`.
  Raw `67c6ad492b7d0109fa4e635c56f8800e24d814e68d1279c5af51b3f36c247964`;
  collection `f32c46432a332391e3af3fe3807578b63bfa24e7bc624f9711aff59d98254474`;
  transport `0fd7dc419fc7734b83bc26ee65c5b2ced5b6cb13650de228072d6b0086e773a2`.
- Fresh v3 private `i140-approval-prepared-v3/independent-prepared-v3.json` SHA-256
  `f161b1f81fe670d2549e2ae11d0a3367e8db3f146a14441ab79179b8ac361166`.
  Stage `842218af2cc02a19b052164e4e082c5fcd2e63620eb81955587777a8ad76e3e5`;
  input ZIP `13291cc0f37b87d3a1d6d2cb4e1e36f8491a096bd94e8b54d7c62bd187a44dbd`;
  launcher `5752b485ea7b4123ad4ff565fc153e2102b8fa8352f05f0b3a150ad36a8d0d08`;
  collection `88153d449277d9fedc3e2658b217b16dac88350c8b394a5f9a87beade5cd7750`;
  transport `b9f6f1fe9b2900ea66d84fe559e9779c57e0d3e6dcafcbe19fde2939b15ccc76`.
