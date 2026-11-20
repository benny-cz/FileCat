# Windows standard-user qualification

6 November 2026. Follow-up to `d07df6b1` and [actual recovery-process crash qualification](NTFS_RECOVERY_PROCESS_CRASHES.md).
Writer profiles and production recovery actions remain disabled; this closes a specific validation gap.

## Actual identity and qualification controls

The disposable Windows guest has a dedicated local Users account, `fcarchive42829297`, outside Administrators. Only
this task's binaries/SDK were made readable to that SID; it writes its own result tree and byte-checked application/
probe copies. No other tester's account or workspace was changed. Production source is unchanged since `d07df6b1`;
this follow-up changes only the test harness and explicit qualification runner. The final complete runtime copies
match the host-published trees and SPDX reported at that checkpoint, as verified below.

The actual launcher now records the user SID and native token-group membership before/after its integrity check.
`-RequireStandardUser` requires medium integrity and absence of the Administrators SID. It refuses before creating
the test child when the actual token contains that SID, including disabled or deny-only membership.

The raw `TokenGroups` reader bounds the native buffer, count, layout and SID ranges and checks every group without
filtering its attributes. The managed enabled-group view would be insufficient because it excludes deny-only groups;
see the [.NET 10 implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Security.Principal.Windows/src/System/Security/Principal/WindowsIdentity.cs#L574).
A separate actual native control creates an Administrators-deny-only restricted token: raw membership is true while
the enabled-group view and administrator role are false. The standard-user gate also rejects an actual administrator
after lowering integrity, with exit 71 and no child PID recorded.

## Windows profile precondition

The initial VMware batch launch did not load the new user's registry hive. Its native run passed 88 cases, failed
82 at `CreateAppContainerProfile` with FileNotFound and correctly skipped five administrator-only controls. This
failed run is retained as diagnostic evidence, not accepted qualification. A separate probe recorded the missing hive.

The profile-aware route uses Windows `Start-Process -Credential ... -LoadUserProfile -WindowStyle Hidden` and waits
for that actual standard process. A probe within the resulting process recorded the correct user's hive loaded;
both real confined application/probe assessment smoke cases then passed. No sandbox fallback or archive privilege was
added to compensate for the unloaded batch profile. The final full suite requires the profile to be loaded explicitly.

## Validation

| Actual lane/control | Result |
| --- | --- |
| Windows 11 / NTFS / .NET 10.0.14, actual standard account with profile loaded | 170 passed, 5 administrator-only skips |
| Actual recovery-process crash cases within that suite | 31 passed |
| Real confined app/probe smoke cases with profile loaded | 2 passed |
| Actual Administrators-deny-only token control | Raw membership true; enabled-group/role views false |
| Administrator requesting strict standard-user qualification | Refused before child creation, exit 71 |
| Mac arm64, final harness build and Windows-only guards | Build clean; 107 expected skips |
| Ubuntu x64, final harness build and Windows-only guards | Build clean; 107 expected skips |

The final actual test parent recorded user SID ending `1003`, integrity 8192 both before/after, Administrators membership
false both before/after, and exit 0. The profile-aware standard PowerShell process also exited 0. The five elevated/
administrator controls are covered by the preceding 175-pass administrator lane; their standard-account skips are
intentional. All builds completed without warnings/errors. This test-only follow-up leaves the production code and
the preceding three-platform common/UI regression results unchanged.

### Complete runtime transfer correction

An intermediate 170-pass standard run used the previous incremental runtime overlay. Comparing the actual copied
package against the host SPDX found ten app files not refreshed by that overlay: the Rar15, Recovery and Remote
DLL/PDB pairs, ShellHost DLL/EXE/PDB and FileCat EXE. That run remains diagnostic evidence and is not accepted as proof
that the complete host inventory was exercised. Earlier component reports' host SPDX inventories remain host inventories;
incremental runtime overlays must not be interpreted as exact transfers of every published file.

The final rerun used complete app/probe trees extracted into a fresh VM directory. The transfer packet and manifest
were checked before extraction; every source file and count was verified there. The actual standard account copied
the packages into its own new directory and checked each source/destination hash against the host manifest before
launch. Afterward, local collection rechecked all 469 copied files against that manifest and all 261 actual app files
against the preceding SPDX inventory. All matched. Future validation must transfer complete published runtime trees
and verify the actual copies rather than reuse an incomplete DLL overlay for runtime identity claims.

All own FileCat/dotnet/testhost processes exited. The Unix guard runners completed their idle checks, and the Mac's
temporary until-reboot keep-awake assertion/helper was retained. No account was made an administrator, and no other
tester's running process was killed.

## Evidence identities

Ignored raw evidence remains under `.tmp/test-results/archive-writing/standard-user-v2` (controls) and
`standard-user-v3` (final Windows run), with corresponding guest archives. Final Unix guard archives are retained in
`.tmp`; the unchanged development app SPDX is in `recovery-crash-artifacts-v1`.

| Artifact | SHA-256 |
| --- | --- |
| Three-file test-harness/runner source delta over `d07df6b1` | `071A2061A2A972DAF2133E92D0546EDCCD97C27C18C5B391BA3461D126791262` |
| Windows harness/runner overlay | `51045AE073220ADBFB3AACD951979F3B14BE03181B605B8FC6175A9BE11117D0` |
| Initial unloaded-profile batch failure | `803E541D4FA2B2E2E1BCEDE19D4E81D56ACF26C1DAA5C894FB3C722FC0E3AA90` |
| Actual-token/refusal/profile-aware smoke controls | `6FAA80CFCD4D825A3EE26D1D11B66991094C2C76EBD35F5D27D58365E01F9DD4` |
| Intermediate standard run with incomplete runtime overlay | `D4C650A6CA3BB96128118E63F6E0B767DE96C99DE9893CA05FDF0BB92507A6D0` |
| Complete app/probe runtime transfer | `FDAB454E2F8FD1FBE72604E672F986502ED1869F48DE0E091697D7963D1FCF45` |
| Host manifest of all 469 runtime files | `62A45743564E0F4B073D767363DFC32C23142438CA06BDEB7D31F8A164B9BAD2` |
| Final standard-user run with complete matching bundles | `D46AAF429E54733EA30528EBD676EE8A77481D10AD965110CC0CEACCBE961D4F` |
| Mac final harness build/guard evidence | `59B858F870D720B69B230A7F7351465423965B63C5A9B129FCE35726B51A16F3` |
| Ubuntu final harness build/guard evidence | `B9C893B8BF8B197C69363812D0E77B8A91B05D72E0C9B1A8573BF60A85D8469E` |
| App SPDX, all 261 actual standard-user app copies matched | `70BD747A07283AC99751298D793E38F2245210FC64A03C6623A0FB7BA704241A` |

## Remaining work

This is a validation of the explicit backend on this Windows/NTFS/runtime/user configuration. It does not qualify
power-loss durability, every filesystem/security policy, production recovery UI or a new writer profile. Restoration,
production actions and the remaining implementation/qualification gates remain open. RAR analysis and feasible
implementation still precede the final independent review after the implementation loop.
