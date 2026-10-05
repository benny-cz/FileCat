# E-V09-M1 — Mac resumption, current-source native checks and recovery admission

Preliminary runtime evidence, 2026-10-05. The owner reports the Mac online and authorizes its use
for release validation. Trusted SSH succeeds at the previously known address; no further discovery
or network sweep occurs. The previous availability deferral is superseded. No release candidate exists.

## Identity and provenance

- Physical Apple Silicon Mac: macOS **27.0.1**, build **26A434**, arm64, account benny/UID 501.
  This is the owner's existing machine, not a clean installation.
- Exact clean source **`0bc1b6624033c93933ac8bba08473a7f1756f588`**. Production/test/build/workflow
  paths are unchanged from I136's `a1c265f`. The current baseline's four required CI jobs pass in
  [run 37251513820](https://github.com/benny-cz/FileCat/actions/runs/37251513820); three package jobs
  skip. This slice retains job metadata, not another full downloaded artifact inventory. I136
  retains its independently verified a1c265f artifact/TRX inventory.
- Release/self-contained **osx-arm64 Core and App test producers**, built on Windows from a full
  verified source export in the private C: evidence workspace. All **839 Git source exports**,
  **1,512 payload files** and **1,513 input ZIP members** independently verify. Source text is
  compared canonically; binary fixtures are byte-exact. SourceRevisionId is supplied explicitly.
- Isolated owned Mac root:
  `/Users/benny/FileCatReleaseValidation/macresume-1257f02ed7924ecfa482f75bd667dd0c`.
  The installed Mac SDK/runtime, user applications and system settings are unchanged.

## Observed cases

| Scope | Result |
|---|---|
| Empty 7z, supported archive formats, discovery warnings, archive-result/unknown-size criteria, nested archives and multipart RAR | **77/77 pass**, zero skips; exact case inventory matches sealed host evidence |
| Headless Find archive-result flow | **4/4 pass**, zero skips; exact case inventory matches sealed host evidence |
| Native FAT16 raw device | **1/1 pass**: actual attached fixture scanned, 10,000 report bytes match generated fixture content |
| Native off-source topology | **1/1 pass**: virtual source and the owned state/output folder are reported on distinct source disks |
| Descriptor handoff, whole-sector reads and native device enumeration | **3/3 pass**, zero skips |
| Full recovery session through MainViewModel | **Failed**: admission refuses before scanning; **zero device opens**. Autosave/recovery/output phase is not reached |

The source is the repository's FAT16 fixture, decompressed to an owned **25,165,824-byte** image
and attached unmounted as `/dev/disk4`, raw `/dev/rdisk4`. hdiutil's image-to-device mapping,
diskutil size/type/mount state and native character-device ownership (UID 501) verify before use.
The attachment is writable, so this preliminary unchanged-byte observation is not masked by a
write blocker. Before/after SHA-256 is identical:
`19f74a085272a8080035ffb5a649b8b53ae8fe1cdcd323555c89037293a10819`.
The compressed Git fixture SHA-256 is
`079f5e36673b12b3c7e9408447325008ed22ccece9156bbf4726485b3f7f11d6`.

The failed session's output says other FileCat processes may still write to the source and asks
for accessible process information. It closes without opening a device. No recovered-file or
whole-session zero-write claim is made. This preserves the I106 safety interlock.

## Read-only admission diagnosis (I106 remains open)

A private diagnostic calls the unchanged public production census and records its per-process
decisions via the unchanged apphost inspection method. All eight FileCat DLLs remain byte-identical
to the Mac App producer. The production census returns **true**, with **one matching process and
three unknown identities**:

- PID 1912 is named `dotnet`; production conservatively treats that name as a possible FileCat.
  An independent native ps observation identifies **Visual Studio's ServiceHub controller** under
  `/Applications/Visual Studio.app/Contents/dotnet/dotnet`.
- PID 0 has an empty managed name/path. Two other entries also have empty managed identities.
  The later native observation confirms PID 4157 is **Z/defunct**; the other observed entry has
  disappeared. Their identities remain unknown to the production census. No name-based exemption
  or unavailable-identity bypass is introduced.

Visual Studio is not stopped by the agent. Native read-only ps independently confirms the diagnostic
probe is gone. The production test's bootstrap, controllers, workers and device utility PIDs are
absent, as are processes under the exact owned root. All payload hashes still match; both owned
temporary directories contain no fixture files. hdiutil confirms the owned source is detached.
Historical Windows census-availability evidence remains in I106; this Mac observation extends it.

## Administrator interaction gate

`sudo -n /usr/bin/id -u` returns **password required**. fs_usage therefore cannot run unattended
through this SSH session. A bounded, reviewable preflight is staged for the owner:

- Mac launcher: **`~/FileCatReleaseValidation/TracePreflight-20261005.command`**.
- It asks sudo for the Mac password locally, then runs an **eight-second fs_usage capture filtered
  to one owned ordinary-user control PID**. That process reads and writes a synthetic private file
  with pinned offsets/bytes and verifies its readback. It opens no disk device, changes no system
  settings and never runs FileCat as root. All workers have bounded termination/cleanup.
- Native root:
  `/Users/benny/FileCatReleaseValidation/trace-preflight-d23bc6c41ca440f08d3f4d0c299a6744`.
- Script SHA-256 `09be552ca440127963f65983d7481c2d844382dad2c3719d8c900f60d88eaa8d`;
  launcher SHA-256 `24437293765e9cf0bfad279bba441420a6a2f83a88624b143936284e27a0e5a9`.

The owner must close Visual Studio for the next admission diagnosis and launch the preflight to
provide local administrator authentication. **It has only been staged, not executed**. Even a
successful control will require independent trace interpretation and wider capture/child-process
controls before actual FileCat source-write qualification. Real authopen approval/refusal, removal,
mounted/adverse backing topology, native desktop and final-package qualification remain pending.

## Retained evidence and limits

Private root: the authorized second workspace's **`FileCatReleaseEvidence/mac-resume-20261005`**.
It contains source/publish logs, manifests, safe ZIP transport receipts and outputs, all case XML,
source identity/image, unchanged production diagnostic source/results, independent native process
observations, cleanup census and staged administrator preflight source/pins.

- `independent-v1.json` SHA-256
  **`8db9e519fe0ab482210973c6f67eba0a8ccd3080c8d659e27ed187d3d98529d5`**.
- `native-process-identity.json` SHA-256
  `80080f8afed9b2abfeade44c7a53f7d3130dc562a27cd4fe5a14861d60cf4119`.
- `ci-37251513820-metadata.json` SHA-256
  `c05afa5d686e2e88c31009adb2a654bdf2f4f29ae19e4c7cd49b779d615f14a6`.
- `trace-preflight-request.json` SHA-256
  `884963c941e4ba1a7b845d3d700307af95f2e636398461531a64e7dc4a6fe458`.

The first producer attempt exhausted E: space before Mac execution. Its failed log and partial
ignored build output are retained; the successful producer builds entirely in C:. The first private
census probe referenced FileCat.App.dll instead of the actual FileCat.dll and failed before execution;
that attempt is retained, and the corrected probe is separate. Neither is a FileCat runtime defect.
The original recovery-session failure is retained as failed and is not relabeled as a pass.

This slice adds **86 passing executions and one failed session**, with zero skips. It closes no
whole release campaign: **114/136 issue rows remediated, one separately Closed; 24/26 checklist
steps partly or fully open**. Both VMs remain running; G: remains untouched/HOLD. Native Computer
Use remains unavailable. No candidate or human GO exists; overall **NO-GO**.
