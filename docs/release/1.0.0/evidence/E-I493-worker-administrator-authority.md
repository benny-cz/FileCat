# E-I493 — an elevated FileCat's hostile-input worker held administrator authority

I08/TV-16, 2026-10-14. **A demonstrated containment defect, corrected and verified in three sessions.** No candidate, publication or human GO follows; the I106/I110 physical-source hold is untouched.

## The defect at exact parent `3d338fc9`

`RestrictedProcess.LowIntegrityToken` duplicated FileCat's own token and set only its integrity label to `S-1-16-4096`. Low integrity restricts writes, not reads, so when FileCat itself ran elevated the worker that parses hostile input (Windows thumbnail/icon handlers, pictures) received a token that still carried an **enabled `BUILTIN\Administrators` SID** and **all 24 administrator privileges**, among them `SeDebugPrivilege`, `SeBackupPrivilege`, `SeRestorePrivilege`, `SeTakeOwnershipPrivilege` and `SeLoadDriverPrivilege`. The class comment nevertheless described the helper as running "at low integrity where Windows allows it", which overstated the boundary.

Measured on the elevated Insider 26220 host at that parent, with four controls in `RestrictedProcessLowTokenTests`:

| Control | Parent | Fixed |
|---|---|---|
| Worker token carries no enabled administrative group | FAIL — enabled `S-1-5-32-544` | pass |
| Worker token holds no privilege beyond `SeChangeNotifyPrivilege` | FAIL — 24 privileges listed | pass |
| Windows' own `AccessCheck` denies the worker token `FILE_GENERIC_READ` on an administrators-only file | FAIL — access granted | pass |
| A started worker reads the user's own file but is refused the administrators-only one | FAIL — read the sentinel | pass |

The third control is Windows' own answer for that exact token and that file's security descriptor, not an inference. The fourth starts a real worker and asserts both the positive control (the user's own sentinel is returned) and the refusal (`refused admin-only.txt`), so an absent sentinel cannot pass as containment.

## The correction

The worker token is now built from the session's **UAC-filtered token** where `TokenElevationType` reports one, then passed through `CreateRestrictedToken` with `DISABLE_MAX_PRIVILEGE` and with every privileged group the token carries listed as deny-only (`S-1-5-32-544`, `547`–`551`, `556`, `557`, `559`, `578`, matched only on an exact two-sub-authority NT Authority SID), and only then lowered to low integrity. The result is **verified before use**: integrity at most `4096`, no enabled privileged group, and no privilege whose LUID differs from `SeChangeNotifyPrivilege`. A token failing that check is refused, and because `CanUseFallbackToken` already refuses an elevated token ([I492](E-I492-windows-elevated-worker-fallback.md)), an elevated parent never starts an unrestricted worker instead — it reports the failure and the helper stays off.

The job object additionally sets `JOB_OBJECT_UILIMIT_HANDLES`, denying the worker every USER handle owned outside its job, so it cannot post messages to or read the windows of the program that started it.

## Verification in three sessions

| Session | Token | Result |
|---|---|---|
| Elevated Insider 26220 host | High, Administrators enabled, no logon SID | 14P/0F/0S across the four controls, the picture-worker environment case and nine `ShellHostTests` |
| Disposable Windows 11 VM 26300, signed-in Admin console session | Medium, Administrators deny-only (UAC-filtered) | 14P/0F/4 explicit elevated-only skips |
| Same VM, elevated in that signed-in session (scheduled task, interactive principal, highest run level) | High, Administrators enabled | 14P/0F/0S |

The full host Platform suite on the fixed product is **439 total, 5 failed, 35 explicit skips**. All five failures (`DeviceReadTests.An_elevated_FileCat_reads_a_volume_itself` and four `WindowsFileRecordsTests` administrator cases) reproduce at the exact parent with the same compiled test assembly, so they predate this slice and are retained for separate triage. `FileCat.Core.Tests` passes **4,256 total, 0 failed, 141 skips**.

## A worker must be windowless

The tests previously used `cmd.exe` as a stand-in worker. A console program needs a console, and that needs access to the window station; the measured WinSta0 DACL on the host grants its full rights to the console **logon SID** and to `BUILTIN\Administrators`, and only `WINSTA_ACCESSCLIPBOARD|WINSTA_ACCESSGLOBALATOMS` to the plain user SID, while the `Default` desktop DACL carries no ACE for the user SID at all. In a session whose own token carries no logon SID (a service, scheduled or `vmrun` guest parent), a worker token without administrative authority therefore cannot allocate a console, and `cmd.exe` exits `0xC0000142` (`STATUS_DLL_INIT_FAILED`) before running.

FileCat's real helpers are unaffected because they are `WinExe`: the same restricted token started `FileCat.ShellHost.exe` successfully in every session above. The tests now start `FileCat.WorkerProbe`, a windowless .NET program added for this purpose, so they exercise what the product actually starts. This is a property of console allocation, not of the restriction: no FileCat helper is a console program, and `TryCreate` now documents the requirement.

## Retained private evidence

`E:\FileCat\artifacts\release-evidence\i493-worker-authority-20261014-v1`, nine files, self-excluding manifest `sha256.json` SHA-256 `6d62e6f76e986ae64ac0dbb29d7d40f408694de99f52e82d358e6c2b3fa8140c`. It holds the parent and fixed control runs, the full host Platform suite, the Core and App suite outputs, both VM session outputs, and binary identities: test assembly `440233447e298d4c62aaa2a6fd123b3e6769a4c51ba6b39cd0127eba5c2a38cf`, parent `FileCat.Platform.Windows.dll` `bf9ac3df0adc236db1d0e975d7236d318f5129579ec52da21b918c385c1ba089`, fixed `c34e1f83f5f6d05a6a63edf4f6b042bfd2c78350cf8704ea0fa336ac3127e476`. The parent DLL was built from a separate detached worktree at `3d338fc9`, so the shared checkout was not disturbed; both control runs used the same compiled test assembly with only that DLL replaced.

## Later hosted-source qualification

At exact later source `8fe93efe`, the [five-job green hosted run](E-CI-I493-I494-worker-identity.md) retains the four elevated-token controls, eight fallback-token controls and one worker-environment control as **13P/0S/0F on each of Windows x64 and ARM64**. The elevated-token tests would skip if their process were not elevated. This later CI supplements the three native sessions above; its TRX/member hashes and full-run archive are separately recorded.

## What this does not establish

It does not claim a verified sandbox: the worker keeps the filesystem and network reach of an ordinary user. It does not prove the behaviour of every unusual token construction (no split token, domain-joined sessions, Administrator Protection), an installed candidate, default-UAC consent paths, or the five retained pre-existing Platform failures in the original host suite. The Windows VM was reverted to its `updated #38` snapshot at the owner's instruction before the VM runs above, and both owned VM test roots remain for further slices. I08 remains open.
