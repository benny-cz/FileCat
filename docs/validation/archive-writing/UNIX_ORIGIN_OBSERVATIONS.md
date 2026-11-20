# Held-file Unix origin observations

2026-11-05, over `4972c985`. This adds trusted-parent raw origin-xattr observations from held Unix regular-file and
directory descriptors. It does not register a writer or qualify source preparation, strongest-input Unix policy,
metadata application, native publication/recovery or an installed package.

`ArchiveUnixOrigin.Read` accepts a `SafeFileHandle`, pins its lifetime across all native calls, and opens no paths.
It reads only `com.apple.quarantine` on macOS or `user.xdg.origin.url` on Linux. The native signature is platform-specific;
Windows refuses before handle inspection or libc loading. File/directory type and available identity/ctime/size/owner/
mode facts come from the held descriptor. Devices and uninspectable types are unavailable, never unmarked inputs.

Only the fixed attribute's native missing result becomes `Absent`; empty values are `Present` with zero raw bytes.
Denied, unsupported, oversized, changed and other failed reads remain typed `Unavailable` evidence. Size is queried
before allocation and bounded at 64 KiB; the second read checks exact returned size/status, including growth from an
empty value. Two complete raw observations and before/after held-file facts must agree. The API does not interpret,
trim, repair or normalize binary/text/URL bytes. Cancellation propagates; a closed handle cannot become absence.

These observations are not an atomic source or tree snapshot and do not exclude same-identity ABA changes. Source
preparation must acquire appropriate local no-link handles, bind the consumed-content version and reviewed plan,
account for shared budgets, and recheck actual source/retired/published objects. The Linux API can hide privileged
namespaces with a missing-attribute result; this API deliberately accepts no caller-selected name and never queries
`trusted.*` or `security.*`. Its ordinary-user local-filesystem denial evidence below does not qualify arbitrary
filesystem/LSM/server policies or translate a required zone-only mark into an invented Linux origin URL.

Primary API references checked on 5 November 2026: [Linux man-pages getxattr(2)](https://man7.org/linux/man-pages/man2/getxattr.2.html)
and [Apple fgetxattr(2)](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/fgetxattr.2.html).
The implementation is FileCat-owned code, using the existing libc/runtime boundary and existing native-stat helper;
no third-party source or package was added, and legacy path-based origin behavior was not changed.

## Native regression evidence

18 added cases cover empty/binary/unaltered UTF-8 bytes, absent-to-present transitions, frozen snapshots, source-name
replacement/symlink/unlink while retaining the descriptor, directory provenance, device refusal, cancellation/closed
handles, native Linux permission revocation, native Mac `readextattr` ACL denial, APFS 64-KiB/64-KiB-plus-one values,
raw evidence through the complete source-version sidecar, and Windows dispatch refusal. Both native denial cases
exercise present and absent attributes. The tests alter only their own temporary fixtures and restore their ACL/mode.

Affected filter:
`FullyQualifiedName~ArchiveUnixOrigin|FullyQualifiedName~UnixMark|FullyQualifiedName~ArchiveOrigin|FullyQualifiedName~ArchiveWindowsOrigin`.

| Environment | Affected suite | New native class | Evidence |
|---|---|---|---|
| Windows 11 x64 VM, runtime 10.0.14, medium integrity | 157 passed, 25 skipped, 0 failed (182 total) | 1 dispatch guard passed, 17 Unix-only skips | `archive-unix-origin-vm1.trx` |
| macOS ARM64 / APFS, source build, SDK 10.0.101 | 177 passed, 5 skipped, 0 failed (182 total) | 15 passed, 3 other-platform skips | `archive-unix-origin-mac-v2.trx` |
| Ubuntu x64 / ext4, ordinary UID 1000, source build, SDK 10.0.101 / runtime 10.0.14 | 174 passed, 8 skipped, 0 failed (182 total) | 13 passed, 5 other-platform skips | `archive-unix-origin-ubuntu.trx` |

APFS stores and returns the exact 65,536-byte value; the 65,537-byte value is refused as `TooLarge` with no exposed raw
bytes. Native Linux mode-000 and Mac ACL-denied reads return `Unavailable/AccessDenied`, including when no mark exists.
Path replacement/symlink/unlink does not redirect the descriptor's raw origin to the other fixture. These passes do
not test complete archive writing, confinement or quarantine classification/application.

Final builds report zero warnings/errors. Source two-file transfer archive:
`6C64099F49DC5032A019A4CDB9342B0C70AA81D40C6709042874C5B60B53B650`.
Windows binary/symbol transfer archive: `7CE5A9C56785306B4654578E54E7CBEF869782DA24CBF4BC3444139474662257`.
Windows `FileCat.Core.dll`: `EFD73BA18DBA29E8E04DAF1C1BAD38ADBBCEB5A73A3556F48ADDE66BEBF51000`;
`FileCat.Core.Tests.dll`: `F74756EBC2EC969BBC1FA7A114FF32018488AE2F4FEDA479D8AA555A66EAA861`.
Launcher parent/child 7044/2608, runtime 10.0.14, integrity 12288 before / 8192 after, exit 0. It retains administrator
memberships/privileges; this is not an ordinary filtered-user or installed-package qualification.

Raw TRX/facts remain in the owned guest directories and ignored worktree evidence. Final guest censuses have no
remaining FileCat/test/build processes from these runs; Mac caffeinate PIDs 1339/6428 remain intentionally active
until reboot. Shared `E:\FileCat` remains tracked-clean on `main`. The implementation loop, final independent review
and conditional RAR licensing analysis remain pending.
