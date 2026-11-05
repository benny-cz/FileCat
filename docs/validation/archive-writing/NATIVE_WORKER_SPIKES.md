# Native archive-worker spikes, 4 November 2026

Status: **partial development evidence; no new production writer enabled**. This report belongs to the native-worker
checkpoint on `codex/archive-writing`, following the TAR/gzip foundation. The
[implementation ledger](IMPLEMENTATION_STATUS.md) and [controlling plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md)
retain the full outstanding scope.

## Windows x64, Windows 11 build 26340

Owned guest root: `C:\Users\admin\FileCatValidation\ArchiveWriting-42829297`.
SDK 10.0.101, VSTest 18.0.1, actual process runtime 10.0.14. Host-built binaries were copied to the owned checkout;
the source baseline in that guest is not presented as a new full-source build. Earlier successful native run:
`results/archive-containment-win-vm-medium13.trx`, **21 passed, 0 failed, 0 skipped**, about six seconds. Its launcher facts:

```json
{"parentPid":3360,"runtime":"10.0.14","before":12288,"after":8192,"childPid":5856,"exitCode":0}
```

The test parent inherits a lowered-integrity VMware login token; this is not proof of a UAC-filtered ordinary-user
interactive session. The suspended worker separately verifies removal of administrative authority and its actual low
AppContainer token. Normal-user/installed-session qualification remains required.

The final nine new tests cover:

- Exact package identity, zero capabilities, a readable approved asset, private file read/write denial, package root plus
  LocalState/TempState/LocalCache/AC-Temp write denial, registry creation refusal, a parent-proven live loopback listener,
  and child-process policy denial.
- Successful 64 MiB native allocation plus refusal of a 1 GiB allocation under the one-GiB process commit job limit.
- A positively started busy worker terminated by its one-second CPU allowance.
- A positively started stderr flood drained with finite storage/traffic and terminated.
- An idle worker that ignores pipe EOF, killed by job lifetime after its actual parent is killed.
- An explicitly inheritable unrelated file object excluded from the inherited handle list, with a valid native
  file-query control inside the worker before the negative query. Strict invalid-handle termination is an expected
  result of this deliberate OS-denial probe, not an accepted archive parser crash.
- Concurrent/repeated stop/dispose calls ending and reaping one healthy worker without touching closed handles.
- A healthy 64 MiB parent-proxied copy and complete staged readback with matching SHA-256, without filesystem input or
  stage grants to the child.
- Refusal of a shared-runtime-shaped or linked asset directory before changing its ACL. Provisioning requires the
  FileCat-owned assembly beside the self-contained CLR and pre-inspects a bounded tree without reparse points.

The other thirteen passing tests retain existing low-token, ordinary-fallback and clean-environment assertions. The
tracked `eng/validation/archive-writing/run-windows-containment.ps1` was live-tested afterwards with another **21/21**
pass: `tracked-evidence/containment-20261104T220403Z-6c1596aa0fed45ba950c6373d7da27a3.trx`. This rerun validates the runner;
it is not additional format coverage. After the asset guard was added, the final checkpoint run passed **22/22** in
about ten seconds: `tracked-evidence/containment-20261104T220848Z-1bc3dbfc9e4949faa1d10bb1abe83eb4.trx`. Host copies of
both the original and final TRX/facts and runner logs are under the ignored
`.tmp/test-results/archive-writing` directory.

Modified binary SHA-256 values used in the final 22-test run (not a complete package SBOM):

| Binary | SHA-256 |
|---|---|
| FileCat.Platform.Windows.dll | `5D1EFF0A622C1448BE41C5AAB970C3B5184559479C083D36A82ADBCA96D25B82` |
| FileCat.Platform.Windows.Tests.dll | `5013E744AA7BF657DD5B116FB8EEC6D1E435C28409CA4752B4EE7B699E8AF3C2` |
| FileCat.WorkerProbe.dll | `C1C9145709509F967C7FA065867D97E73CBA8FBF0E0E5AD422216D1EFC9E6C20` |
| FileCat.CoreCrashHarness.dll | `50421EA1CE7ED49684814A2C9D41CB37334E5BB464EE4D1EEB35390AFFB750F6` |

Failures that changed the implementation or probes:

- Win32 token query class 46 returned error 87. It did not establish LPAC. The launcher now performs documented
  impersonation-token access checks with exact-package positive and user-only/ALL_APPLICATION_PACKAGES negative controls.
  Startup remains suspended and refuses if any check is inconclusive.
- The neutral process environment initially lacked OS-known AppContainer profile fields (creation error 203). Only
  OS-derived profile/temp locations were added; parent loader variables and diagnostics remain excluded.
- Native Winsock startup is refused with 10107 on this LPAC. The CLR wrapped this in a type-initializer exception; the
  probe now calls Winsock directly and records startup refusal separately from socket/connect access denial (10013).
- Child creation correctly returned Windows error 367. The probe originally omitted that policy-specific denial code.
- A package-SID DACL deny alone still allowed profile file creation. Mandatory medium no-write-up labels now protect the
  whole package directory, not just the API-returned AC directory, including pre-existing child labels. Actual write
  attempts verify the result. Profile tree size/depth/reparse anomalies refuse setup.
- Shutdown originally was not idempotent. Shared disposal now awaits one kill/reap/drain operation and caches exit facts.

Still unqualified: production protocol/phase/lifecycle integration, real archive transforms under this boundary,
installation/portable/framework-dependent assets, setup/ACL races, private registry/IPC and alternative network routes,
Windows ARM64, release-candidate packages and the full fault/scale/fuzz matrix. No profile registration uses this spike.
The final guest process census found no FileCat, worker, testhost or dotnet test instance owned by this task running.

## macOS ARM64, macOS 27.0.1 / APFS

Owned fixture: `/Users/benny/FileCatValidation/archive-writing-sandbox-spike`.
Apple Command Line Tools clang 21.0.0 and MacOSX27.0 SDK were used explicitly; global Xcode selection was retained.
The `probe.app` helper was ad-hoc signed with the checked-in App Sandbox entitlement. No valid release-signing identity
was present. The executable was invoked by absolute path because sandbox startup changes the working directory.
`result-correct-path.json` records:

```json
{"uid":501,"ownRead":1,"readErrno":1,"writeErrno":1,"networkErrno":1,"nprocResult":0,"nprocErrno":0,"forkErrno":35,"asResult":-1,"asErrno":22,"smallMapSucceeded":1,"largeMapErrno":0}
```

Its own read works; unrelated read/write/network return EPERM; fork after RLIMIT_NPROC=0 returns EAGAIN. The attempted
absolute 1 GiB RLIMIT_AS returns EINVAL, and both 64 MiB and 2 GiB mappings succeed. The large mapping is never touched
and is immediately unmapped. **Whole-process memory containment fails this probe and remains unqualified.** These
results neither qualify an archive helper nor prove that every supported design is impossible. Resource accounting,
supported lifetime controls, actual engine work and a signed release candidate remain outstanding. No deprecated
sandbox API or private memory-control SPI was adopted.

The probe exits immediately and no FileCat/test instance remains. At the user's request, a temporary
`caffeinate -diu -t 2147483647` assertion (PID 6428) remains active until reboot, with display/system idle sleep prevention
and UserIsActive assertions observed. Another pre-existing caffeinate process was left untouched.

## Ubuntu

The initial spike was postponed for another tester's picture-validation instance (PID 65891) and image viewers.
The user subsequently authorized stopping those exact old instances, and later idle windows permitted native testing.
The [5 November Linux checkpoint](LINUX_ARCHIVE_WORKER_CHECKPOINT.md) now records the unregistered cgroup/seccomp/
Landlock worker, healthy assessment/proxy workloads, native denied-access/resource/parent-death tests, final source and
artifact hashes, and remaining gates. The final native lane passes 40 with one platform-guard skip. This supersedes the
old postponed status; it does not infer Linux qualification from Windows or Mac or enable production writing.
