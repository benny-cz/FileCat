# Bounded native regular-file capture

6 November 2026. Implementation over `a89abddf`, on the private `codex/archive-writing` branch.
This adds preparation infrastructure and tests; no production write profile is enabled.

## Implemented behavior

`ArchiveCapturedInputs` captures fixed directory-relative regular files through the native Unix/Windows adapters.
It retains reviewed identity, length, SHA-256, metadata and origin observations, then closes each native file.
A sealed collection grants lazy read views; each view owns its native file and process-wide admission permit.
The directory owner must outlive preparation. This collection does not enumerate folders or grant publication.

All collections share sixteen active source leases. Waiting is cancellable and stops after five seconds with a
typed resource refusal, before another file is opened. Disposing the owner cancels pending admission and prevents
further reads. Nested disposal releases the native file and permit even if a borrowed stream throws on disposal.

Capture checks the 8 GiB per-file and 64 GiB cumulative logical bounds before hashing. It charges member count,
encoded metadata, names, conservative retained record/container overhead and variable string/provenance costs.
Any capture failure poisons the partial collection. The sealed cost is imported into plan admission before its
maps/manifests allocate, rather than granting capture and planning separate full retained-memory allowances.
Preparation requires the same owner, exact bindings and exact cost; equal-content retargeting and discarded
capture costs refuse before journal intent, artifact creation or worker launch.

Worker reads reopen the reviewed native identity/metadata without rehashing the whole file on every source switch.
Full content review still occurs during capture and the existing before/after preparation reviews. Zero-byte reads
open no source. Positive source switching is separately limited to four opens per fixed grant plus four per charged
full read frame of source IO. Tiny alternating reads exhaust their credits rather than amplifying native opens or hashing.
Credits depend on charged IO, not a large maximum allowance. The exact IO unit is 65,524 bytes (a 64 KiB frame
less its twelve-byte response header).

## Executed checks

| Actual machine/lane | Passed | Skipped | Total | Scope |
| --- | ---: | ---: | ---: | --- |
| Windows 11 x64 VM, final v4 source, medium-integrity parent | 115 | 90 | 205 | Capture, broker, plan, transforms; Unix-only native cases skip |
| Windows VM, actual standard account with loaded profile | 5 | 0 | 5 | Native 256-source reacquisition plus four fresh LPAC transform/verifier workflows |
| macOS 27.0.1 ARM64, v3 broad regression | 202 | 2 | 204 | Capture, broker, plan, transforms, actual Unix storage |
| Ubuntu x64 VM, v3 broad regression | 202 | 2 | 204 | Same common/native scope |
| Mac and Ubuntu, final v4 source, each | 95 | 0 | 95 | Final five-second admission deadline plus affected capture/broker/plan/transform contracts |

V4 adds the finite admission wait and its regression to v3. Seventeen final capture cases include 1,000 synthetic
inputs without retained files, sixteen global views across owners, cancelled/disposed/saturated admission, poisoned
partial sets, identity/metadata/provenance replacement, equal-content retargeting, combined plan costs, throwing
disposal, zero reads, tiny-read switching and bulk-IO credit replenishment.

The native Unix workflows use 256 sources for TAR and 64 each for gzip/bzip2/Zstandard. Mutation cases exercise
content, mode, inode replacement, symlinks and parent namespace changes. Windows' actual standard account has SID
ending `1003`, no Administrators membership and a loaded registry hive; its four LPAC workflows use the same counts.
These prepare and freshly verify unpublished native stages, rather than publishing a user archive.

The Mac's strict Linux ARM64 VM run additionally captured 128 native APFS files per format and completed eight
distinct, successfully exited and reaped VM helpers: separate transform and verification for each format. The final
v4 guest's PDB audit matched **812 compiler source documents / 7,604,370 bytes**. The actual Windows confined probe's
PDB audit matched **839 documents / 7,931,540 bytes**. These audits read the binaries' compiler checksums against
their private source checkout and retain the checked source bytes.

Native `/usr/bin/tar` and pinned native Zstandard independently checked exact member names/order and every four-byte
payload on Mac and Ubuntu. The Windows outputs were separately transferred and checked with the Mac readers.
The Zstandard oracle remains test-only. On Mac, the oracle reports its CPU/output/wall controls and explicitly does
not claim an address-space or whole-host memory limit.

The v3 strict VM workflow also passed the original sixteen healthy archive roles, 29 named kernel checks,
parent-death, cancellation, helper-death, extra-process startup refusals and memory-pressure controls. The provider's
measured peak RSS **1,242,415,104 B** and footprint **1,096,598,968 B** still exceed 1 GiB. The whole trusted-host
resource envelope remains unqualified; fixed guest RAM and the guest cgroup do not complete that gate.

## Retained setup failures

The first Windows source fixture omitted the application icon and privileged-bootstrap build scripts; the fixture
was completed and the real build rerun. No source/build guard was bypassed. Core builds have zero warnings/errors;
the Windows test project reports four existing WebView2/WPF assembly warnings and zero errors.

Earlier standard-user launches failed CLR initialization before testing, despite a loaded profile. A diagnostic
using the previously qualified launcher settings succeeded with both the global and private runtime. The final
runner mirrors that route: hidden profile-aware PowerShell, System32 working directory, redirected launcher output,
and actual standard-user execution. The exact cause of the earlier startup failures is not established; they are
retained as failed setup evidence, never counted as native passes. A scheduled task that never ran was removed after
its owned action was verified.

The first independent-oracle attempts lacked its pinned inventory, then exposed unsupported Mac `RLIMIT_AS` and
GNU tar's requirement for explicit compression flags on piped input. The corrected oracle was rerun successfully;
the failed attempts remain in evidence. No production archive code was changed to accommodate these tool failures.

## Evidence identities

Ignored raw artifacts are retained under the private checkout's `.tmp`. Each frozen archive contains a file
inventory. The copied archive SHA-256 and every payload size/hash were independently verified locally.

| Frozen artifact | Bytes | Payload rows | SHA-256 |
| --- | ---: | ---: | --- |
| `macos-vm-captured-inputs-evidence-v1.tar.gz` — full v3 lifecycle run | 261313933 | 817 | `267DE68CC780F8BA76D6F7C318C78981D3CD7465E0488850B07FD7F46538A5F9` |
| `macos-vm-captured-inputs-evidence-v2.tar.gz` — final v4 native capture and PDB audit | 96754274 | 1212 | `00C7FB61D557F92FC9CFB84842D18B9E678B413AC769BAE65CB3A660227E72D9` |
| `captured-inputs-windows-evidence-v1.tar.gz` | 54561205 | 1221 | `1769961BD815FD7B910E8DCA281FA95F5E8E10DD4FDE34122952C7FB78563C69` |
| `windows-captured-inputs-oracle-evidence-v1.tar.gz` | 26046 | 15 | `3D0BD105252EA80A0F9456B5DAC470FD3AA737C30D088702A2793A5BC679D76F` |
| `mac-captured-inputs-evidence-v1.tar.gz` | 172612 | 28 | `C7417B8EFC05C36F1EDC3210BC7C512292DBEBDDDA3A0EB946DEF6CF273E34E5` |
| `ubuntu-captured-inputs-evidence-v1.tar.gz` | 173390 | 28 | `2EF4D4B733B9CB0D8E1E6817D44B2C7464F3F5105DB5B6E2101A4B45BF1DD577` |

Source baseline bundle: `8B3D6958B434EF681DD646865D2DBDAF6D71180A22DA3B807ABBCE75DE608F03`.
V3 overlay: `7FA1CA21F712E817AB2A30F06D4D9DB77F4C362898339936003B37FED5B8DEAB`.
Final v4 overlay: `614A4B420F12BD0A3453B1372FDD370FF79EE6D842627F0E1AC681B22BA090EF`.

## Remaining work

Recursive frozen selection capture, private source snapshots, directory/root ownership and global preparation/
transient-memory admission remain incomplete. This conservative cost model is not measured process-memory
qualification. Production composition, publication/recovery, remaining format/UI journeys, installed signing and
packages, exact candidate licence closure and the required fuzz campaigns also remain open.

This task's test/worker processes exited. Other testers' instances were preserved and access deferred while busy.
No host application or desktop input was used. Mac's until-reboot wake helper remains. The shared `E:\FileCat`
checkout remains on `main`; work is isolated in its private worktree. The goal stays active. RAR analysis and any
viable licence-compatible implementation precede the final independent UX/UI/functional/security review.
