# E-V09-M7 — Mac native authorization device binding

Native baseline executed and independently verified, 2026-10-05. This follows the separate
successful approval/refusal cases in [E-V09-M6](E-V09-M6-macos-authopen-preparation.md).
**Actual held authorization returns the equally sized replacement device.** The defect and
working correction are recorded in [E-I140](E-I140-unix-device-authorization-identity.md).
The preparation below remains historical; no candidate qualification or issue closure follows.

The private self-contained arm64 wrapper calls the actual UnixDeviceSource.Open from
the byte-identical clean `593583e585d4a79cbb7ff961770a2d826858e14d` Recovery/Core DLLs.
The production source tree is unchanged through `ce282827380cc79e81bd775ead38688935533c02`.
The wrapper snapshots path metadata before opening, then inspects the returned descriptor's
native fstat identity, read-only flags, length and closure. It makes **no source-content read**.
It is a validation instrument, not a drawn FileCat workflow or release artifact.

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

Actual binding failure is retained; I140 correction/native revalidation remains. No candidate
or human GO. **NO-GO** remains.
