# E-I100 — Unix launches in separate sessions run independently

Discovered 2026-10-02 during E-I99's actual GUI check, Ubuntu 26.04.1. Same account, profile, data root, TMPDIR and
hash-bound I99 working FileCat.dll. No candidate.

Expected A-07/V23 B12 behavior: a second launch opens its folder in the existing window and exits. Launching from a
new SSH session instead returns timeout exit 124 at ten seconds, with the original window still showing source.
strace proves the child creates /tmp/.dotnet/shm/session17460/FileCat-4E0D79942132A7ED while the original server holds
session16723's mutex. The child proceeds through GUI startup, unlinks the first instance's live socket and binds the
same endpoint. The bounded child is terminated and the original GUI is gracefully closed; all private state retained.
The same-session control forwards successfully (E-I99).

This matches Microsoft's documented distinction: Unix Local synchronization objects are limited to a shell session.
See [NamedWaitHandleOptions, .NET 10](https://learn.microsoft.com/en-us/dotnet/api/system.threading.namedwaithandleoptions?view=net-10.0).
The observed .NET 10.0.12 mutex storage under /tmp also warrants checking V09's write-location accounting separately
from the configured TMPDIR. Neither cross-TMPDIR behavior nor a recovery scan is claimed tested by this trace.

Severity Medium (one profile's state/instance contract), with V09 safety impact when another instance is missed;
must fix. Linux reproduced; macOS has the same source path but native reproduction pending. Windows uses a
login-session namespace and is not established affected. Initial status was Open; remedy and native verification
below supersede that checkpoint. Prior Unix single-instance evidence does not cover distinct-session launches.

Raw host records in artifacts/release-evidence/linux-os-matrix-20261002/26.04/:

| Record | SHA-256 |
|---|---|
| i99-gui-forward-native.txt | `d2666354dc032e26a8f2ab22d935fc5268b8d76cf60b561c9a3b9d92b2855a95` |
| i99-gui-client-strace-r2.txt | `0d82e25ce1b2d92c5ed64ea328dfb6e45315706af4bbea34c829a025b8a934e2` |

## Remediation and native regression

Unix election now holds a native exclusive flock on a persistent instance.lock in the actual profile's LocalDirectory,
already covered by AppPaths.WriteFolders. No Unix named Mutex is constructed. Native lock success is required rather
than trusting FileShare's best-effort Unix behavior; a read-only existing-file probe detects a busy usual instance
without creating folders/files. Windows keeps its existing mutex namespace.

The owner atomically publishes its socket's absolute path in owner-only instance.pipe. Clients discover that path
from their shared state folder and retry across startup/stale metadata, regardless of TMPDIR or Unix session. Socket
identity uses the actual state directory; distinct case-sensitive data folders do not share an endpoint. The usual
instance's actual socket directory participates in the recovery guard; missing/invalid location refuses a scan.
Shutdown finishes/disposes the listener before releasing its lock, preventing an old listener from unlinking a new
owner's socket. The lock file is not deleted/replaced on release. Pipe current-user enforcement and payload limit remain.

The authoritative .NET 10.0.12 [PAL initialization source](https://github.com/dotnet/runtime/blob/v10.0.12/src/coreclr/pal/src/init/pal.cpp#L1074)
uses its compiled temporary directory for mutex shared files; TMPDIR alone cannot relocate that storage. The observed
trace, rather than an assumption about managed temporary paths, motivates the profile-local lock.

Native source starts from cc97a8d's archive plus I99, then these LF-normalized working inputs:

| Input | SHA-256 |
|---|---|
| SingleInstance.cs, R2 | `b5f651236b14e925d7d95e0cc412e30269e1e0bb5e0547e29b497a0337c139ca` |
| MainViewModel.Recovery.cs | `fec0fee4800e0f060bebd2bbc4787cb7fcb2d0b96878cae210664713967c8ca2` |
| RecoverySafetyTests.cs, R2 | `bf7dffb080741195fff591c3eca435b69ce7bff42d01b169b51a653796bd20d7` |
| InstanceSmoke project / Program.cs | `24dd78527339dad1ae2aeba69c5971a5ed548a119f3a7895e7af385ff1d134e8` / `965c418a389c55b4102c65a956488b8474d64c57ba9ee4f947310c08d2dfb737` |
| Updated boundary harness | `b4d195cdb4b5e96bdbefd20aef6465a93a1cc74fb4fd0d789ca665730ccece5f` |
| Cross-process harness R3 | `fb019b0cea574acb21b060f6dedb02a907ec4e2bd97ba4edca76a5cdd5961893` |

SDK/runtime remain 10.0.401/10.0.12. Native FileCat.dll `ffc87a829cca7875701374793e7453dff180e551198f6f651d21217e4b4129b6`,
test DLL `4c105f37f1855095230246e90e3dc288888a99e53f5221b1dd2dc9628f6309eb`, smoke DLL
`9aeee70b7e0ce79aaf6b3e07679eb74a2f58cd08e641ce9b3c51587e229f3d7f`. The committed source additionally clarifies
one PipeName comment; native working inputs/binaries above and subsequent CI/package identities remain distinct.

| Validation | Result | Evidence SHA-256 |
|---|---|---|
| Production API process harness against unchanged I99 build | 1/5 pass; distinct-session election and cross-TMPDIR usual probe fail | `0995ef9181fc1baf72c0a03b6ddfa7bda438f88b5dd77cf96e1fcfa79aa7df97` |
| Corrected cross-process harness R3 | 6/6 pass, no skips; includes two live case-sensitive data folders on one profile | `72bba0ecaa238e1edc1efa27774bd22f2600a28bcb3ef401926b15a154fa62a0` |
| Boundary/long ASCII/Unicode fresh xUnit processes | 14 passes, one expected boundary skip; all three scenarios pass | `b57acbd071116d9501acabfb9b22befcd5c56c3dfb737a11acda8136e060e4cd` |
| Full affected native App suite | 232 total / 205 passed / 0 failed / 27 skipped; required WebKit branches pass | `5337aeec2ee61d41d355e49c5d8c17cf7a274302caa3f551a5bb44ed4ecd7b45` |
| Real GUI, distinct session and TMPDIR | Forwarder exits zero; original PID/socket survive; target tab opens | `9ae369741cc515ce0ffc4700fabc8399c21e69fbbbfbf034ced4180ec4713f0e` |
| GUI saved state after graceful close | Source/target tabs retained in parsed workspace | `9b2add73398ccb1063dd6fe0e194429e1db27bebedc2b110a39981c19d96877a` |
| Native filesystem trace | Profile-local lock/metadata and native flock observed; no /tmp/.dotnet mutex mutations | `f1f4eb9404f2501a51355a84afe0259aca80701bd63edfa8dcdaac416804c8e5` |
| State folders on owned native FAT32/exFAT mounts | Both election/forwarding cases pass, no skips | `bae2b28f4e83571bf4c2f11f8033e5ebdad73b8c9eb1f6516a0f4e7e9abdee59` |

Framebuffer `3ae26c363377926c20487f1d635e74d4e850c4450e5e611e981028ee0dc80046`; strace
`efe9af89e1bd6a1e89af38a31962651b510c795938290891036af6416b333e70`. Ext4 lock/metadata modes are 0600.
FAT/exFAT expose 0700 from their UID1000/umask0077 mount controls; do not claim POSIX ACLs on those filesystems.
The first remount attempt did not apply FAT UID options and exFAT rejected remount options; no product case ran then.
Exact owned loops were unmounted/remounted with verified options before the successful cases; no physical disk formatted.

Windows targeted recovery tests: five total, three passed, two platform/prerequisite skips. Initial R1 guard regression
failed because the assertion expected "cannot tell" while the correct refusal says "may be on that disk"; corrected
assertion/R2 pass. R1 inputs, binaries, fixtures and failed results retained. This is not a product failure hidden as a pass.

Status: **Remediated and natively verified preliminarily**; CI/macOS, rebuilt Linux package checks, re-audit and
final-candidate closure pending. Full runtime/recovery write-location audit remains part of I09; this trace establishes
the named-lock correction only. Earlier Unix instance/recovery-probe evidence and rebuilt artifact qualification are
invalidated. Raw private archives must be retained before restoring the VM.
