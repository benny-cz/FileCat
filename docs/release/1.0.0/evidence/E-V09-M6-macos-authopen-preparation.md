# E-V09-M6 — Mac native authorization control, prepared

Preparation and direct-read controls, 2026-10-05; follows
[E-V09-M5](E-V09-M5-macos-combined-mapping-session.md). **Native authorization refusal
has not run.** Owner-local authentication and interaction are the next gate.

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

## Concrete owner activity, not yet executed

On the Mac, run `~/FileCatReleaseValidation/AuthopenDecline-20261005-v1.command`.
Enter sudo locally to prepare the owned image. After the detached launcher returns,
**cancel the separate native macOS authorization dialog**. This case expects refusal;
do not approve that second dialog. Record whether it appeared and was canceled.

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

The required human action is native authentication/refusal, which SSH cannot perform.
Plan V09 and §12.3 require native consent and human attestation. Direct controls do not
supply that missing result. Full source-write/helper, application desktop, approval/removal,
adverse topology and candidate qualification remain open.

## Private provenance

Private base: authorized second workspace's FileCatReleaseEvidence/mac-resume-20261005.
Native root: `/Users/benny/FileCatReleaseValidation/authopen-98779a0ee8c944ffbbde42c9ab9b0688`.

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

No issue closure, candidate or human GO. **NO-GO** remains.
