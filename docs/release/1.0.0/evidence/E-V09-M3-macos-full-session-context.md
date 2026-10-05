# E-V09-M3 — Mac full-session trace refusal and launch context

Preliminary execution, 2026-10-05, following [E-V09-M2](E-V09-M2-macos-trace-calibration.md).
The v11 staging/request checkpoint below is historical; actual v11/v12 execution and the
current provider gate are in [E-V09-M4](E-V09-M4-macos-recorded-session.md).
No production/source-write oracle change. Actual SDK-free payload retains clean
`593583e585d4a79cbb7ff961770a2d826858e14d` and manifest SHA-256
`f3b8e93a97f0d4a9383548f5d1d7fcc98ad32d0084d2cb8558d9f235bafe1288`.
Repository at staging is `6509eff74583db81355bcee5e5c96446e9b3b8d3`; I139's test worker
does not change this separately pinned payload or recovery production dependencies.
This is not a new candidate build.

## Failed v8 complete-session attempt

Fresh v8 copies all **1,196 payload pins** byte-exact. Initial v7 staging misses a root-level
manifest member outside source/app and fails before App/device/capture execution. Its partial
owned copy/error remains; v8 corrects the copy. Ordinary dry-run controls verify all six
complete files, 24 input/transport pins and four owned process absences. Privileged staging
does not imply capture success.

The owner executes v8. Root records C3/C4/C7 without a PID/name filter, with a 256-MiB buffer
and a 110-second cap. The synthetic controls complete, but the actual ordinary-UID-501 App
case **fails admission**: process information cannot establish an absent writer. It reports
**zero device opens** and closes. Root stops the failed capture and verifies cleanup. The
one case remains failed with no skips; recovery/timed save are not claimed to run.

All **68 retained pins** verify. The raw trace has **1,045,988 events** over 6.95488 seconds,
matching the recorder count; its offline decoder succeeds as ordinary UID 501. Actual App
PID 6965 receives **318 denied flavor-2 and 318 denied flavor-6 proc_info calls**, errno 1,
including the recorder/controller and many other processes. The root-origin worker's raw
setgroups entry/exit confirms its supplementary groups were cleared before dropping UID.

The owned 25-MiB source image stays byte-exact:
`19f74a085272a8080035ffb5a649b8b53ae8fe1cdcd323555c89037293a10819`,
and its device is independently verified detached. All **12 tracked owned process
candidates** are absent. Unchanged hashes are cleanup/byte evidence, not full write tracing.
The failure is preserved; no process-census refusal is bypassed.

## Ordinary-login diagnosis and actual v9 failure

A private diagnostic invokes the same current FileCat.dll from the verified 593583e payload
through the existing benny SSH login. Production census is **false** (no other FileCat),
UID 501/GID 20 with the sixteen usual supplementary groups, including admin group 80.
Production DLL bytes independently match. Its older per-process diagnostic loop still
reports kernel/zombie rows separately; that is not the current production result.
This diagnostic establishes the ordinary-login result, not the sole historical cause.

V9 keeps the actual worker in this existing SSH context and uses root only for recording.
All 1,196 payload pins, 25 input/transport pins, six complete dry-run files, four native
identities and four owned control-process absences verify at its staging checkpoint.
The owner then executes the concrete launcher. **The actual case still fails census
admission with zero source opens**, UID 501/GID 20 and unchanged normal groups.

The retained raw trace has **1,053,707 events**. Actual App PID 7517 receives 318 flavor-2
and 318 flavor-6 proc_info EPERM results. More decisively, four executable reads of
**/usr/bin/sudo fail EACCES** on native thread 322098; formatted path/errno observations
and four raw open errno-13 exits agree. An independent ordinary SSH read control verifies
mode **04511**, root ownership, and read errno 13. Production must retain unknown status
when a live process executable cannot be inspected. After the elevation launchers exit,
the same current production census returns false again.

This proves an unreadable sudo executable is a concrete admission blocker. Cleared
supplementary groups are **not a sufficient explanation**. No sole-cause claim is made
for every historical refusal, and no production exemption or census weakening is added.
All **77 retained v9 pins**, **15 tracked owned absences**, unchanged source SHA-256 and
independent detachment verify. The failed case stays failed; recovery/timed-save/full-source
tracing are not claimed. Offline decoding succeeds as ordinary UID 501.

## V10 staged checkpoint and failed elevation preflight

The prepared v10 launcher uses sudo background execution plus nohup, allowing the unreadable
elevation launcher to exit. Before starting the recorder, the root controller checks the
live executable inventory for /usr/bin/sudo for at most fifteen seconds. Any remaining
sudo process stops the harness before the actual App/source flow. No other process is killed
by that preflight, no permissions are changed, and production admission remains intact.
The ordinary SSH watcher and actual 593583e App/control/driver inputs remain unchanged.

The first v10 staging attempt has an SSH connection timeout before the remote copy command;
its local launcher/error are retained separately. After the owner restores Mac availability,
fresh v10 verifies all **1,196 native payload pins**, **25 input/transport pins**, six complete
independent dry-run files, four native identities and four owned control-process absences.
Actual privileged capture/full recovery **has not run at this staging checkpoint**.
The ordinary watcher is armed, UID 501/GID 20 with the normal account groups, for a bounded
fifteen-minute ready-marker wait. An armed watcher is not a completed capture or cleanup pass.

- Launcher: `~/FileCatReleaseValidation/TraceSession-20261005-v10.command`.
- Native root: `/Users/benny/FileCatReleaseValidation/macresume-8e5a532f45904083be7169bfa3d904a9`.
- Stage manifest SHA-256:
  `3260eedf0bd64431033c1589dee865d1fff83a58d4e086eddf7922d1018f1c90`.

The owner executes v10. Its fifteen-second preflight finds sudo still live: sudo -b retains
an elevation monitor for its running command. **No recorder, actual App or device starts.**
Cleanup's predicted Python argv also fails to match the framework's real executable path;
it conservatively refuses to terminate the watcher. Manual cleanup verifies UID, group,
launch time, exact owned script and framework executable before SIGTERM, then verifies
absence. Seven native records/pins and owned-command absence independently verify.

## V11 detached launch and harmless control — staging verified

V11 uses a bounded, owned double fork into a separate session. The elevation command
parent exits; only the detached root recorder remains. An ordinary-UID-501 harmless control
executes the same detachment mechanism: its grandchild has parent PID 1, a separate native
session/group, exits zero and is absent. It does not start FileCat, a device or a recorder.
Cleanup now compares the complete observed process identity, including launch time and
actual Python executable path, against the identity recorded while the parent owns it.
It does not accept a mismatched/reused PID.

Fresh v11 verifies all **1,196 native payload pins**, **26 input/transport pins**, six
independent whole-file controls, four native identities and four owned control-process
absences. Ordinary watcher is armed with the normal UID/GID/groups and a fifteen-minute
ready-marker wait. Actual capture **has not run at this staging checkpoint**.

- Current launcher: `~/FileCatReleaseValidation/TraceSession-20261005-v11.command`.
- Current native root: `/Users/benny/FileCatReleaseValidation/macresume-afb63f27d53644fa8e4559926805e1c8`.
- Stage manifest SHA-256:
  `489228c8ce94bdbe0bd718c2b67404594eb7d415e9b0438349f0e9034efad1a8`.

SSH sudo still requires the owner's local password. The concrete v11 launcher is requested
in Mac Terminal; actual execution/interpretation remains pending at that credential gate.
It returns after detached startup; background outputs are retained and retrieved over SSH.
The existing test still requires exact recovered bytes, timed save, unchanged owned source
and detachment. Headless windows do not supply drawn native UI/authopen qualification.

Source-write interpretation stays conservative: source writes/attempts, missing
process/descriptor coverage, loss markers, or actual FileCat nonanonymous shared mapping
with unresolved source FD prevent qualification. No packed-FD guess is used. Calibration,
unchanged source hashes or a successful case cannot substitute for whole-session tracing.

## Retained private provenance and release limits

Private base: authorized second workspace's `FileCatReleaseEvidence/mac-resume-20261005`.

- `session-stage-verified-v8/independent-stage-v8.json` SHA-256
  `b38c80b1f9421e99c5545f331120260a1c126db79c593cc1807b56c501b7de91`:
  historical v8 staging, six complete dry-run files/24 pins/four owned absences.
- `session-failed-v8/independent-failed-v8.json` SHA-256
  `bcf60343c6f7c3aa064da2f576612196e59cad69818aa89c5cd12b75f3b314b6`:
  68 pins, actual failed case/zero opens, native denials, cleared groups, ordinary-login
  diagnostic/current DLL identity, unchanged source/detachment and twelve owned absences.
- `session-stage-verified-v9/independent-stage-v9.json` SHA-256
  `6f106cd317f8ae61d2a8d411bb0a32e901086d963f8de2359e56a00a18cf2a14`:
  1,196 native payload pins/manifest, 25 input/transport pins, six complete dry-run files,
  four native identities/owned absences and explicit not-executed staging state.
- `session-failed-v9/independent-failed-v9.json` SHA-256
  `5ec68eaaacb5c7e3f5a04ab5ccc7b9933b398f900a95587064d43349c969a5df`:
  77 retained pins/fifteen owned absences, actual failed case/zero opens, normal groups,
  raw query/open observations, independent sudo read control and later false census,
  unchanged source/detachment. Additional diagnostic pins are included in this proof.
- `session-stage-verified-v10/independent-stage-v10.json` SHA-256
  `ff99c8af4623f557712f94e114a63b68167209e6cd3aecd6b5540bd9b959235f`:
  1,196 native payload pins/25 input pins/six files/four identities/owned absences,
  detached launcher and explicit not-executed staging snapshot.
- `session-stage-connect-failed-v10`: first staging SSH timeout before remote execution,
  exact local launcher/error and failure note; not a product run.
- `session-staged-v10/watcher-launch-stdout.json`: bounded ordinary SSH launch identity,
  UID 501/GID 20/account groups, ready token hash; armed watcher is not qualification.
- `session-v10-preflight-failed/independent-preflight-v10.json` SHA-256
  `63e8b35b558c450689043b13ee66416381d1da8dfa447140e41b03a7e2a4397d`:
  seven native records, actual preflight failure/no App/device/capture, original cleanup
  refusal, identity-checked watcher termination and owned-command absence.
- `session-stage-verified-v11/independent-stage-v11.json` SHA-256
  `3c4f0271600086c642c5f2a567081585e090807cfe87fcd514beb5dcab00aba8`:
  1,196 native payload pins/26 input pins/six files/four control absences and explicit
  not-executed staging snapshot; no production changes.
- `session-staged-v11/detachment-control-stdout.json` SHA-256
  `765cf9c540c97790eb8f2c2ec188b42606bcb35698befbbc9cb6d7d8c60ce680`:
  harmless ordinary-user detached grandchild/native session/group/parent/absence.
- `session-staged-v11/watcher-launch-stdout.json`: observed complete process identity,
  launch time, native framework executable, normal account groups and bounded wait.
- `census-v8-context-diagnostic-v1`: exact probe/source references, native account/output
  and current production DLL SHA-256; no device or native desktop opens.
- `session-staged-v9/watcher-launch-stdout.json`: bounded ordinary SSH launch identity,
  UID 501/GID 20/account groups and ready-marker token hash. An armed watcher is not a pass.

V09/full source-write, authopen approval/refusal, native desktop and candidate qualification
remain pending. Progress: **117/139 issue rows remediated**, one separately Closed;
**24/26 release checklist steps partly or fully open**. Both VMs remain running; G: is
untouched/HOLD. No candidate or human GO; overall **NO-GO**.
