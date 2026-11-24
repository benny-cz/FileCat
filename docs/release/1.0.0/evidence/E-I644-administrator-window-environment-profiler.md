# I644 — An administrator window loads a .NET profiler that the ordinary account names in its environment

**Found:** 2026-11-24, during final qualification of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md), on the installed candidate in the Windows 11 25H2 qualification VM ([FQ progress](E-FQ-RC2-20261124.md)). It answers a question that the [I638 to I640 amendments](E-DEC-2026-11-24-contract-amendments.md#i640) left to V06's loader audit: can a user's environment reach an elevated window? **Status:** open. A fix is proposed for the next candidate, and the owner decides.

## What was run

- **Machine.** The qualification VM, Windows 11 build 26200 (25H2), with rc.2 installed: `C:\Program Files\FileCat\FileCat.exe`, 1.0.0+7269ec61, SHA-256 `f2d55a62990118c62475efffdbb81f16116896c26e3b356ba5d0abdf0ddf808a`. The account `fcqa` is an administrator with UAC's split token.
- **Probe.** `FqProfilerProbe.dll`, SHA-256 `8ff13ddab270223a4123b04c0d6cdda8fb6ff4ccf7100f690f766a9d4c89fd62`, built from `probe.c` (`4bb9c890929087706e8a55c80374751a515cfe30c3d6c182c0cfef1f3dafe592`) and `probe.def` (`62dbfd785bc1bfd7f83bcd7cdd3f6d5dfff3fdc271053d9c077c14df61b15a35`). The release session keeps all three locally in `.tmp\fq-rc2\hyperv\hookprobe`.
  - The DLL's `DllMain` writes one marker file: the process ID, whether the token is elevated, the program and its command line.
  - Its `DllGetClassObject` refuses, so the runtime goes on without a profiler. It does nothing else.
  - It lay in `C:\FQ\work\hookprobe-bin`, a folder the ordinary account can write.
- **UAC.** For this run only, `ConsentPromptBehaviorAdmin` went from 5 to 0, so Windows elevated without asking and no person was needed. It was set back to 5 afterwards. With the default setting a person approves the same elevation in Windows' prompt; this run did not observe that case.
- **Scenario.** `batch28.ps1` ran unelevated, at medium integrity, in the test account's session.

## Results

1. **Control.** An ordinary FileCat started with the three variables in its own environment loaded the probe (marker: pid 12208, not elevated). So did its `--storage-cloud-query` worker.
2. **The account's environment only.** The variables were then set only in `HKCU\Environment`, which the ordinary account writes without elevation:
   - `CORECLR_ENABLE_PROFILING=1`
   - `CORECLR_PROFILER={8A3E1F52-6C1B-4E07-9D35-0F2B7A3C9E41}`
   - `CORECLR_PROFILER_PATH=C:\FQ\work\hookprobe-bin\FqProfilerProbe.dll`

   An ordinary FileCat started without them loaded nothing.
3. **The administrator window.** In that FileCat: Tools → Open FileCat as administrator, then its confirmation's **Open as administrator**. The administrator window, pid 12512, titled "FileCat — fcqa — fcqa (elevated)", loaded the probe. Its marker reads: `pid 12512; elevated 1; exe C:\Program Files\FileCat\FileCat.exe; command line "C:\Program Files\FileCat\FileCat.exe" --new-instance`.
4. **Cleanup.** The account's environment was cleared, the administrator window was stopped and UAC was restored.

A first run of the same scenario stopped at FileCat's own confirmation, because the scenario did not yet click it. Its control and negative control matched the second run.

## Why it matters

Any program running as the ordinary account, with no elevation, can write `HKCU\Environment`. It can therefore choose a native DLL that runs inside the next FileCat administrator window, with full administrator rights, before any of FileCat's code starts. This holds for the installed build in Program Files, which the I640 guidance recommends to users.

I155 found the same thing in the administrator helper (High) and fixed it there. The helper's native bootstrap removes the runtime's environment variables before the runtime starts (`ClearRuntimeEnvironment` in `src/FileCat.PrivilegedHost/NativeBootstrap.cpp`). The administrator window is the ordinary `FileCat.exe`, started by the stock .NET launcher, and has no such step.

Microsoft's [runtime configuration documentation](https://learn.microsoft.com/dotnet/core/runtime-config/debugging-profiling) lists the profiler settings and `DOTNET_EnableDiagnostics` as environment variables only, with no runtimeconfig.json equivalent. FileCat's runtime configuration therefore cannot turn them off.

## Related routes, not run

- **Startup hooks.** The self-contained packages disable them: `eng/publish.ps1` publishes with `StartupHookSupport=false` and checks the result. The framework-dependent publish does not set it, so that package's administrator window would also run a hook named by `DOTNET_STARTUP_HOOKS`.
- **The runtime itself.** In the framework-dependent package, variables such as `DOTNET_ROOT` choose the runtime.
- **Not audited.** Other variables the runtime reads before `Main`.

## Remedy (proposed) and its cost

Remove the runtime's environment variables before the runtime starts, in every administrator window FileCat opens, as the helper does.
- **Installed build.** This can reuse the helper's native bootstrap, which already checks its protected folder: a mode that clears the variables, then starts the protected `FileCat.exe` with the same arguments, elevated.
- **Copies outside a protected folder.** These stay in I640's accepted residual.
- **Framework-dependent publish.** Publish it with startup hooks disabled as well.

This changes product source, so it reaches users only in a candidate after rc.2. If the owner accepts the risk for 1.0.0 instead, the user documents' "use the installer" advice needs a sentence about this route.
