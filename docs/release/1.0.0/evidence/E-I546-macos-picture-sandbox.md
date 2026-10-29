# E-I546 — macOS picture-worker process and network containment

**Preliminary I08/V10 containment child accepted at `5fe5e8a5c42bb6201164b1d15dd5ab9014ea63b7`; ordinary-user filesystem authority, whole-process memory, other Unix architectures, broader parser/Shell routes and installed-candidate qualification remain open.**

## Finding and correction

The macOS picture worker verified that it had ordinary user and group identity and had given up retained root authority before reading hostile image bytes. It still ran without an OS process policy: the actual worker could create processes and open inbound or outbound network connections under that account. A clean baseline observation at exact `9f3b4b579a5c5626be636addf57b810c6cbb8827` records `sandbox_check` results `process-fork=0`, `network-outbound=0`, and `network-inbound=0` for the real FileCat worker PID; zero means allowed.

The correction starts the real worker through macOS `sandbox-exec` with a minimal profile that keeps existing ordinary-user filesystem behavior but denies process creation and all network operations. It fails closed if the OS policy tool is unavailable or the worker executable is not an absolute path. Windows keeps its token/job route and Linux keeps its existing platform policy. Unix launches now use tokenized argument lists and full executable paths rather than interpolated command strings.

## Exact validation

Raw evidence is retained at `V:\FileCat\artifacts\release-evidence\i546-mac-picture-sandbox-20261028-v1`. Its `SHA256SUMS.csv` has SHA-256 `b609ef7c861cb4ca3c6971390a942098848e4e4e9b302dd061dcdba5533462fe`.

- The exact clean baseline actual-worker observation is `{"alive":true,"network_inbound":0,"network_outbound":0,"pid":35441,"process_fork":0,"source":"9f3b4b579a5c5626be636addf57b810c6cbb8827"}`. It establishes the missing policy on the prior product; it is not inferred from a helper process.
- At exact committed source `5fe5e8a5`, the complete Mac picture-related App slice passes **87 passed / 5 explicit skips / 0 failed**. The five skips are platform-explicit Windows/Linux cases. The actual process observation records `process-fork=1`, `network-outbound=1`, and `network-inbound=1` for PID 35508; one means denied. TRX SHA-256: `e59350a910fa60e2d72332aa1d42ffed58f50a218b69bac8fbe64ad27a64e845`.
- A final focused Mac run passes **15/15 with no skips**: the actual-policy case plus fourteen public decode/bounds cases. TRX SHA-256: `1f36606c3f7e212da71014b8e6e8fb6cef3910b4eff1088d803326e5f8500bda`.
- The same exact source on Windows passes the complete picture-related App slice **79 passed / 13 explicit Unix skips / 0 failed**. TRX SHA-256: `1306af98cde786f7d5d04d05d98cab238934f01ba2a8eb6296f2b494fcbb997a`.
- The exact Windows Release solution build exits zero with **0 warnings / 0 errors**. Log SHA-256: `4fcb7c2d92a5674612792bd81960491580993c734080921f23d6f5bbe2ee71ef`.
- The committed source files are pinned by SHA-256: `PictureDecoder.cs` `5d6fed0f90e4bf29c02c10d60d05bb9d35f2abca90fa5d4f270cffcbb78f55bd`, `MacPictureSandbox.cs` `6a9dbeb6fd2812b9092a5e1ce00a889240ef2921f6df8f65dd755d2915f097d8`, and `MacPictureSandboxTests.cs` `dac7b992279ea75c65be5c7ce2453f85704ad015fbc4bc933fda6b3e965fc2e0`.

## Retained setup failures and boundary

The Mac user's pre-existing `~/.dotnet/dotnet` host was rejected by macOS with `SIGKILL (Code Signature Invalid)`. Three raw crash reports and both setup logs remain in the evidence package. They are setup failures, not product failures. Validation temporarily used the valid Microsoft-signed `/usr/local/share/dotnet/dotnet`; the original user-local host was restored byte-for-byte afterward, and only validation-spawned build servers were stopped.

This accepts only denial of process creation and inbound/outbound network operations for the current macOS picture worker while preserving healthy decode and bounded refusal behavior. It does not remove the worker's ordinary-user filesystem authority, impose a whole-process memory limit, prove detached-child policy on Linux ARM64 or other Unix architectures, qualify other parsers or Shell helpers, freeze a candidate, or authorize publication. Those scopes remain in REL-I08-CONTAINMENT, REL-I06-AGGREGATE, REL-FQ-CANDIDATE and REL-GO-PUBLISH.

## Correction (2026-10-29, review finding R-H4)

The profile started from `(allow default)` and denied only reads outside its roots, writes, fork and network. A decoder exploit could still run another program, signal the user's processes, look up system services, send Apple events, open I/O Kit devices and read `/dev`, the user's terminals included. `16970200` denies all of that except exec of the worker itself, and `(target others)` turned out not to be enforced, so all signals are denied but the worker's own. `/dev` is no longer a read root; only null, zero, random and urandom are. Launchd job creation is denied explicitly, though launchd already created no job for a sandboxed `launchctl submit` on macOS 27.0.1. On the Mac, a sandboxed probe ran another program and signalled another process under the old profile, and the new one refuses both. The worker still decodes: 30 sandbox and picture tests pass.
