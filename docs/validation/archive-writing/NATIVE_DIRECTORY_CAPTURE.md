# Native directory observations for archive inputs

6 November 2026. Follow-up to [bounded regular-file capture](BOUNDED_CAPTURED_INPUTS.md), over `8696908f`.
This implements native directory capture infrastructure. Folder transforms and production write profiles remain disabled.

## Implemented contracts

The existing bounded capture collection now also holds typed directory observations. A directory record has its
native identity, fixed parent identity/name, metadata digest, opaque origin, exact UTC modification time and ordinary
mode. Its version has zero logical bytes and the canonical empty SHA-256; it has no payload descriptor or worker read
grant. Native adapters reobserve a fixed parent-relative leaf, and review requires every captured field to match.
Transient handles close before retaining the record. The caller must retain the native parent owner through review.

Files and directories share one positive-ID namespace, identity-alias set, 100,000-member ceiling, name/metadata/
retained-cost budgets and process-wide sixteen-lease admission limit. A failed/cancelled capture poisons the partial
selection. Admission occurs before native observation, including the 100,001st member and a folder observation while
sixteen file views are active. Sealed directory records cannot be silently submitted through the file-only planner:
that route specifically refuses until directory planning, provenance and publication integration are implemented.

On Mac and Linux, a directory-specific native open requires the actual directory type and refuses symlinks. The
outer-metadata reader has distinct regular-file/directory entry points; the original file entry point still rejects
directories. Directory capture includes the existing complete bounded stat/ACL/xattr/origin observation and native
change time. New-member mode keeps only ordinary permission bits, including executable bits; special source mode
bits remain in the review digest. Nanosecond timestamps are retained without tick rounding. Normal Unix directory
link counts are accepted; no regular-file single-link requirement is incorrectly imposed on folders.

On Windows, the adapter opens one directory-relative non-reparse disk folder and captures its identity, basic
fields, security descriptor and fixed `Zone.Identifier` stream. Native change time supplements the existing outer
metadata digest, so restoring last-write time does not erase evidence of a new child. The portable directory mode
is 0755 and native FILETIME precision is preserved. An unrepresentable native time returns a typed unsupported
reason. Origin reads require the same base identity before/after; locked, failed or oversized streams never become
an absent mark. The source parent's existing native handle denies namespace deletion/rename while retained. After
its owner is disposed, stored directory observations cannot acquire fresh authority.

These records are not recursive tree snapshots, source-deletion rights, archive member paths or publication
authority. Capturing a directory alone does not establish its complete child inventory. Reading opaque marked
origin does not complete aggregate mark representation or authorize publishing a declassified archive.

## Executed validation

Mac and Ubuntu each passed **207 cases / 7 platform skips / 214 total** covering capture, native storage and Unix
outer metadata/xattrs. The native directory cases capture and rereview 256 real folders, retain no per-folder file
handle, then still open sixteen native regular-file leases. They check ordinary versus special Unix mode bits,
exact subsecond time, raw origin bytes, regular-file/directory type separation and symlink refusal.

Directory mutations cover mode, modification time, replacement inode, replacement by a file/symlink, changed parent
namespace, new children with restored last-write time, and changed origin. The additional refusal run passed **12/12
on Mac** and **11 passed / 1 APFS-only skip on Ubuntu**, including unreadable folders and oversized Mac quarantine.
The production Core code is identical across these runs; the second source packet adds these refusal tests and the
Windows-specific timestamp check. The v3 packet changes only the Windows parent-pinning test.

The common capture suite includes a mixed file/folder selection of exactly 100,000 members, followed by refusal of
the next member before its observation callback. It also checks all directory fields, owner lifetime, shared global
lease cancellation and partial-selection poisoning. These are bounded-model tests, not measured whole-process
memory qualification or proof of 100,000 native roots.

Windows' final common/guard run passed **99 cases / 117 Unix-only skips / 216 total**. The complete actual standard-user
native run passed **214 cases / 1 platform guard skip / 215 total**, including thirteen directory-capture cases,
the existing real crash/content-transaction/storage tests and four fresh LPAC transform/verifier workflows. The test
account's SID ends `1003`, with no Administrators membership and a loaded profile. Medium-integrity common tests use
the existing lowering harness; the native run uses the actual standard account.

A final test-only addition also passed on both Mac and Ubuntu: a real directory timestamp of **-1 second plus
999,999,999 nanoseconds** is captured exactly, and a one-nanosecond change refuses review. Each one-case run used
production DLL/PDB files byte-identical to the already frozen source-audited assemblies. Windows' native test covers
its exact 100 ns FILETIME precision separately.

## Failed setup and test expectations retained

The first Windows start deferred on another tester's live process. The next broad standard-user run used a flat
copied test directory: 24 existing crash tests correctly failed to find their checkout marker and sibling harness
outputs. The complete source-shaped fixture restores `FileCat.slnx` plus all test, crash-harness and worker-probe
runtime trees. Its TxF opt-in is scoped to the actual standard process. No production guard or confinement policy
was changed to accommodate that fixture.

That corrected fixture then passed 213 native cases with one platform guard skip, and found a mistaken expectation
in the new Windows parent mutation test: pinned parent handles intentionally denied its attempted directory move.
The corrected regression requires that denial, successful unchanged review, successful move after parent-owner
disposal, and refusal of further reviews from that disposed owner. The complete native suite is replayed afterward.

An initial post-run compiler audit also refused xUnit's generated test-runner source outside the private checkout.
That refusal is preserved. The successful compiler audit covers the actual production assemblies loaded by the test
host; test DLL/PDB files, source packets and results remain independently inventoried. The checker still requires
source checksums to match and does not expand its read roots to accept unreviewed external paths.

## Provenance and remaining work

Mac and Ubuntu production PDB audits each matched **835 source documents / 7,895,577 bytes**, covering Core, Archives,
SharpCompress and the existing separate Rar15 reader assembly loaded by these development tests. This is not a
dedicated shipping worker bundle or new licence closure. Frozen payload inventories and copied archive SHA-256
digests were verified locally. Windows' five-assembly production audit additionally covers Platform.Windows and
matched **919 documents / 9,256,628 bytes**. Every file in its three transferred test/crash-harness/worker-probe
runtime trees was compared with the built source trees: **314 files**, with exact file counts, sizes and hashes.
The Windows frozen evidence also retains all earlier failed/deferred test runs.
The first local Windows archive inspection preceded completion of its still-running transfer and observed EOF;
after that same transfer exited successfully, the archive digest and every payload row were verified again.

| Artifact | Bytes | Payload rows | SHA-256 |
| --- | ---: | ---: | --- |
| `mac-captured-directories-evidence-v2.tar.gz` | 8948642 | 1300 | `B16EEE3DF30607CF25DE9D6419E3B2FA415A1CBE2BA05FDCF4764FE6808DA388` |
| `ubuntu-captured-directories-evidence-v2.tar.gz` | 8941887 | 1300 | `40EAE4D69C37013C78CB4E66C3D7ADDC1FAE8A5F43CA4E13179A4B4359515EF5` |
| `captured-directories-windows-evidence-v1.tar.gz` | 228217519 | 2134 | `6C3FF84671C17E942C7F47A3E850936963BBB7D7A3E4CB682779DE17D34FAC3C` |
| `mac-captured-directory-nanoseconds-evidence-v1.tar.gz` | 1889888 | 7 | `60262FF54AFDF78AB87E59EFFAD96AC29D5ECE0261FED47FE29A5C4F665CA80B` |
| `ubuntu-captured-directory-nanoseconds-evidence-v1.tar.gz` | 1889825 | 7 | `914BB9CC4EA8FD0AACEAFABFF669E348AB5338A0E2C2217D10782D0CA5FFC353` |

Source packet v1: `F90670A98B25917B3F5B1224ED17E8850F2A93B9220E934ADC0D9DA97E6A26B8`.
V2: `50D047EFE5A8952691E0D687E836C37DE5D0C26C52D74AA0B03DA5207F3CA0F4`.
Windows v3: `66D3FCC2ADE717A7E128C4B9F0DB4F390CD30484A0F008B58599E3C62B1AEC4A`.
Final nanosecond test packet: `7D98827CEB4E5036F01CB11A4A7C5A2CCA427589ACFB363E918DF51F0C8F759C`.

Recursive stable enumeration, exclusions/collisions, source filesystem/mount qualification, global directory-root
ownership, private snapshots, directory-aware planning/wire/sidecars, final publication review and production UI
integration remain required. Global preparation/transient-memory admission and the existing platform/resource/
package/fuzz/release gates also stay open. No host GUI or application was exercised. The private implementation
branch and shared `main` checkout remain separate. The full goal stays active, with RAR analysis and feasible
licence-compatible implementation preceding the final independent UX/UI/functional/security review.

All owned test/worker processes exited. Final collection censuses were empty and no unrelated process was stopped.
The Mac's temporary until-reboot wake helper/assertions remain active.
