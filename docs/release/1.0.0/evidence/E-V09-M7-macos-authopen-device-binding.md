# E-V09-M7 — Mac native authorization device binding

Native baseline and two fresh committed-guard replacement runs independently verified, 2026-10-05. This follows the separate
successful approval/refusal cases in [E-V09-M6](E-V09-M6-macos-authopen-preparation.md).
**Actual held authorization returns the equally sized replacement device.** The defect and
working correction are recorded in [E-I140](E-I140-unix-device-authorization-identity.md).
The baseline preparation below remains historical; no candidate qualification or issue closure follows.

## Corrected v3 and v4 native results

Fresh corrected local-Terminal v3 and the owner-requested v4 repeat both reject the
approved equal-size replacement before constructing a source. The byte-identical clean
`8f75856802668f7d21c09f33aa876e4fdc4409d3` Recovery/Core components report the
changed/removed-device IOException with no returned source, timeout or probe content read.
V3 changes native inode 887 to 891; v4 changes 907 to 911 at the same `/dev/rdisk4` path,
with 25,165,824 bytes in each image. Successful helper read-only opens occur respectively
11.998218 and 14.402040 seconds after verified replacement readiness.

Each capture has successful post-recvmsg native fstat and close on FD 62. That received
FD is inferred from the unique matched native operations and pinned production control
flow; no source exists for wrapper descriptor reflection or direct F_GETFL measurement.
One FIOCLEX descriptor-control ioctl occurs; zero device-size ioctls or observed content
read/write/truncate calls occur on that inferred descriptor lifetime. Each case verifies
200 input/88 retained pins, both unchanged detached images, eight owned absences and all
recorded worker/recorder/decoder/cleanup exits zero. V3 retains 2,196,246 raw events over
125.979592250 seconds; v4 retains 2,359,505 over 125.966731708 seconds. Both requested
120-second recorders complete. Three unpaired nonreturning exit/thread-termination starts
and finite loss-marker inspection remain explicit; these are not whole-source, mapping,
drawn-workflow or candidate qualification. Historical failed v1 and unavailable-session
v2 are retained. Completed launchers are single-use.

Clean successor `61975926df8dd5ce32d741bc5eeddd9eec6f3704` CI 37359106547 passes
all four required lanes. Four server ZIP digests, six complete TRX inventories and all
60 affected viewer cases (20 per available Windows x64/Ubuntu/macOS App inventory) verify
independently. ARM64 App passes 351/368 with 17 declared skips; native startup/drawing
and installer compilation pass. ARM64 per-case TRX and physical qualification remain
unavailable; three tag/manual package jobs are skipped. Original failed CI is preserved.

Unchanged-source approval independently passes in [E-V09-M8](E-V09-M8-macos-guard-unchanged-approval.md).
Fresh ordinary desktop refusal on the same committed component also independently passes
in [E-V09-M9](E-V09-M9-macos-guard-desktop-refusal.md); native removal/broader qualification remain.

## Historical clean preparation and failed SSH session

Fresh committed I140 preparation uses `8f75856802668f7d21c09f33aa876e4fdc4409d3`
in native root `authbind-c6369740f9634543ad7e3839c5a4b4a1`. The Recovery/Core DLLs
match the clean Core test producer byte-for-byte. All 200 input/46 retained pins,
four native metadata controls, equal-size same-path/different-inode rehearsal,
golden source hashes, normal detachment and five process absences independently verify.
The unchanged wrapper reads no source content in binding mode. The single-use
`AuthopenBinding-20261005-v2.command` is staged for the separate root account and a
held ordinary-user authorization dialog; this binding case was pending at preparation and later fails SSH interaction as recorded below.
All exact source/payload/launcher/transport hashes and the clean Mac 19/4 declared-skip
test result are recorded in E-I140. Root CLI authentication is verified without retaining
the credential. It is not native consent. I141 retains the unrelated failed ARM64 CI gate and its passing successor.

The private self-contained arm64 wrapper calls the actual UnixDeviceSource.Open from
the byte-identical clean `593583e585d4a79cbb7ff961770a2d826858e14d` Recovery/Core DLLs.
The production source tree is unchanged through `ce282827380cc79e81bd775ead38688935533c02`.
The wrapper snapshots path metadata before opening, then inspects the returned descriptor's
native fstat identity, read-only flags, length and closure. It makes **no source-content read**.
It is a validation instrument, not a drawn FileCat workflow or release artifact.

The v2 SSH/root launch authenticates and starts the recorder, but native authopen says
authorization is denied because no user interaction is possible. The worker returns
not-approved/no source/no timeout; no replacement is installed. This is an unavailable
authorization-session setup, not a human refusal or a passing binding case. All 200 input/
71 retained pins, both unchanged detached images, five recorded command exits and four
known owned PID absences verify. The helper PID was not independently observed for an
absence claim. The 13,008,560-byte raw capture is shortened after setup failure; no completed
120-second or whole-source trace is claimed. Detached supervisor exit code is not recorded.

Fresh local-Terminal v3 setup uses the same committed Recovery/Core bytes and owned golden
images in `authbind-d8b09642eead468882d8dd2cf0204d97`. Four native controls, same-path/different-inode
rehearsal, 200 input/46 retained pins, both hashes/detachment and five absences verify again.
`AuthopenBinding-20261005-v3.command` uses the previously verified local sudo route.
Its held native approval was pending at preparation and now passes above; v2 is completed/single-use and must not be rerun.

## Verified controls and owned replacement rehearsal

All **200 input/payload pins** and **46 retained preparation pins** verify independently.
A native C metadata library, compiled with the installed macOS 15.2 SDK, supplies a checked
48-byte structure. Both its path snapshots and the managed wrapper's descriptor snapshots
match independent Python native stat fields for regular files and owned block/raw devices.

Four ordinary UID-501 controls pass: the same regular file, a different reference file of
the same size, the original raw image device and the replacement raw image device. The
different-reference control detects identity change while the existing length comparison
would pass. All controls return read-only descriptors and immediate EBADF after closure.
These are metadata controls; no source-content reads or native authorization occur.

Both owned images contain **25,165,824 bytes**. Source A matches the separately decompressed
golden FAT16 fixture. Source B changes exactly one owned byte at offset 16,777,216 from
zero to 90; the independent Windows golden reader verifies its resulting hash. The ordinary
native detach/attach rehearsal reuses the same block/raw path with a different raw-node
inode. Both image hashes remain unchanged, both images are detached, all commands exit zero,
and five owned process absences verify. This rehearsal does not hold an authorization request.

- Source A SHA-256: `19f74a085272a8080035ffb5a649b8b53ae8fe1cdcd323555c89037293a10819`.
- Source B SHA-256: `93e102afd953eff730a60e1cdf23d62677fedb7fdf46b0dc540714f9b8b51062`.

## Historical staged native case and human interaction gate

`~/FileCatReleaseValidation/AuthopenBinding-20261005-v1.command` is staged and verified.
It requests sudo locally, detaches the root controller, and requires sudo to exit before
starting the ordinary worker. The owner must **hold the separate native authorization
dialog until Terminal prints “Owned replacement verified,” then approve that dialog**.
On a setup failure or timeout, Terminal instructs the owner to cancel it.

The controller verifies exact owned image attachment, ownership, unmounted size and native
node identity before changing only the temporary block/raw nodes' permissions. It observes
the ordinary helper while authorization remains pending, restores and detaches the original
image, then installs the equally sized replacement. The replacement must reuse the recorded
path with a different inode before approval is requested. Early approval, an unexpected path
or identity, or a completed worker during replacement invalidates setup and stops the case.

The returned descriptor is inspected and closed before any source-content read. A returned
replacement would establish a binding defect for this component path; a refusal needs
independent interpretation of its cause and native events. The controller retains an
unfiltered C3/C4/C7 raw recording, owned process observations and command exits. Actual
capture duration, syscall coverage and losses remain unmeasured until execution and inspection.
No complete trace or whole-source qualification is claimed.

Attachment tracking starts before invoking hdiutil; partial node-permission mutations are
tracked individually. Cleanup rechecks the exact owned image/native node before restoring
attributes and normal detachment. No forced detach or guessed physical disk is used. The
original pre-staging controller and input ZIP remain separately preserved with the private
safety revision receipt. SIP remains enabled; physical USB stays untouched/HOLD and both
VMs remain running.

Plan V09 and §12.3 require local consent and human attestation. SSH cannot provide the
required local authentication. Device removal, drawn application admission/workflow,
broader helper/source-write/adverse-topology and exact-candidate qualification remain open.

## Actual v1 baseline result

The owner reports “ran and approved.” Actual component worker 11463 returns replacement
inode 849 instead of original 845 at the same `/dev/rdisk4` path; equal lengths would pass
the existing size gate. Independent source-open raw/formatted pairs place helper 11465's
successful read-only open 19.208 seconds after the verified replacement-ready marker.
The probe inspects metadata and closes FD 60 without calling a content read. Both image
hashes/detachment, clean recorded command exits, 88 retained/200 input pins and eight
owned absences verify. Raw 2,142,873 events/125.982 seconds retain finite trace/mapping limits.
This completed v1 launcher is not reused. Committed-guard revalidation needs a fresh run;
[E-I140](E-I140-unix-device-authorization-identity.md) records the correction and working cases.

## Private provenance

Private base: authorized second workspace's FileCatReleaseEvidence/mac-resume-20261005,
`authopen-binding-prepared-v1/`. Native root:
`/Users/benny/FileCatReleaseValidation/authbind-609da86fb9204571bc014047f3d00dea`.

- `independent-prepared-v1.json` SHA-256
  `f5a59ea0347c17b1e64e0254af912167a654982ccf2e8f28ed3676f86e35cc3f`.
- Native stage manifest SHA-256
  `5b5840888608becc910b54d1e0ef48fc1b19cf406e097518f7478f7dfe03467b`.
- Native LF launcher SHA-256
  `303aca89b36e164147bc0e15530121e70116e2c344e33aeea1961ab7b6e7d327`.
- Preparation collection SHA-256
  `14e446e15c59ec381fb7916aaa51c4e534ccd2927f927254823afcf60b534237`;
  transport ZIP `b5c2d088c58be444672f3367572dfda8e09e143796e09784d2f7319abb30bcc9`.
- Original pre-staging input ZIP SHA-256
  `b418c96e0acf2cee5418cf0450489e970b5ed1f8d710f44b2e38dec95b020689`;
  revised staging input ZIP `98b030f5074f5a91f12acf10299ffbff4d6d1ba00a6ee3bb5567d6bea2f2332a`.
- Installed authopen SHA-256
  `109a0ef0f2dfdc024b1210a5d7d57c3398e4c1793896c77bdf46a6f202688bdc`.
- Installed SDKSettings SHA-256
  `2fa5c0ce1bbcd261b132b572b1a9eece3b5905b04640a44deae1a6a8812928fb`.

- Corrected v3 native root `authbind-d8b09642eead468882d8dd2cf0204d97`;
  private `authopen-binding-executed-v3/independent-executed-v3.json` SHA-256
  `151fab10a6579f7d349a68a14302e37b4c01eb5a2d23c8fa4f44b5c7c8abb607`.
  Raw `958e6ef251d02b96924ee9e61b24d2208ad631df65b592611bb0a3cd6b66d35b`;
  collection `28c1d07cbac56b959abd81b26a535775ad37a64e52d4a33fa5ddbd95bb475fa6`;
  transport `7a5cb31dc05c99fe54c981a270b6c73efcd2a7e66bb1e8412f5be605780a8c8c`.
- Corrected v4 native root `authbind-0867145a0363424ab005733ac63b1552`;
  private `authopen-binding-executed-v4/independent-executed-v4.json` SHA-256
  `92c44eff14b99e6c71b7473772f1639c10d06236fc200a40378751ef8a46def3`.
  Raw `29b6ead350d9820866e8bbf62d6a0efbf711b73e1fc4362a2b91db229d44c8a1`;
  collection `ad2428268a0917bfc0ac8172b048a7e8625a335d8cba08b95a2b3ac909d27a42`;
  transport `1b1d5b80adae3bb5906571e411a66651ee65d82dddbf1dc267a4a65027eaebd4`.
- Clean successor CI 37359106547 at exact `61975926df8dd5ce32d741bc5eeddd9eec6f3704`;
  private FileCatReleaseEvidence/ci-37359106547/independent-ci.json SHA-256
  `c1a4af6495c4a447de0108bde6ed9c8f297755f79c3ad0d9b8da846aa699ebc3`.

Actual baseline failure is retained; corrected replacement revalidation passes. Broader qualification remains. No candidate
or human GO. **NO-GO** remains.
