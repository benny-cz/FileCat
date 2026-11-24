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

## Fix

Proposed for the next candidate in [PR #34](https://github.com/benny-cz/FileCat/pull/34) (branch `fix/i644-admin-window-launcher`), not merged. It changes product source, so it reaches users only in a candidate after rc.2.

### Design

Every administrator window now starts through `FileCat.AdminLauncher.exe`, a small native program beside `FileCat.exe` in every Windows build and package. `AdministratorWindow` runs the launcher with `runas` instead of `FileCat.exe`, for every route: Tools → Open FileCat as administrator, the change journal, denied folders, link reviews, administrator reviews and Operations retries. The launcher:
1. removes every variable whose name starts with `DOTNET_`, `COMPlus_`, `CORECLR_`, `COR_`, `COREHOST_` or `MONO_`, in any letter case as Windows compares names, and sets `DOTNET_EnableDiagnostics=0`. These are the helper's `ClearRuntimeEnvironment` and its prefixes;
2. starts the `FileCat.exe` in its own folder with exactly the text of its own command line after its own name, the system folder as working folder, the cleaned environment and no inherited handles. It takes no program from its arguments;
3. turns idle only after that FileCat does, then stays until FileCat exits and exits with its exit code.

Step 3 keeps the ordinary window's waits as they were. A link or administrator review keeps its staged request until the new window is idle, and a file retry follows the window until it closes. Both now wait on the launcher, which follows FileCat.

**Why not a mode of the helper's bootstrap.**
- The helper ships only in the installed build (DEC-04). A helper mode would leave the portable and framework-dependent ZIPs exposed, or end administrator windows there, which is I640's open owner decision.
- Windows' prompt names a program by its version resource. The helper's says "FileCat administrator helper: runs one approved plan, then exits", which is wrong for a full administrator window, and changing it changes the plan route's prompt.
- The helper is the per-plan broker (ADR-14): it requires elevation and binds consent to one plan. A mode that elevates FileCat with any arguments would widen it and reopen its I155 and I17 qualification.

The launcher reuses what fits: the same clearing rules, the same build script, toolchain, compiler flags and build receipt.

**What it does not do.**
- It does not check that its folder is administrator-protected, as the helper's bootstrap does, so the portable ZIP can use it. Copies in folders the ordinary account can write stay in I640's accepted residual.
- It passes `PATH` and every other variable on unchanged, because FileCat finds tools on `PATH` ([E-V24-G1](E-V24-G1-git-config-contact.md)).

**No launcher, no administrator window.** Before Windows asks for approval, FileCat checks that the launcher and `FileCat.exe` are in its own folder. If either is missing, it says so and stops. It never falls back to starting `FileCat.exe` directly, which would reopen I644 for a package that lost its launcher. The launcher starts only a program named `FileCat.exe`, so a copy whose executable was renamed, which rc.2 relaunched under its new name, now refuses as well. Windows development builds build the launcher too, so a development run through `dotnet` finds it in the build folder. A development build can skip it with `-p:FileCatBuildAdministratorLauncher=false`; its administrator window then refuses to open, and a Windows publish refuses the switch.

**Windows' prompt.** The launcher's description, which UAC shows as the program's name, is "FileCat administrator window". rc.2's prompt showed "FileCat", the description of `FileCat.exe`. **Owner decision:** keep this name, or use "FileCat" (one line in `eng/Build-PrivilegedBootstrap.ps1`). The prompt's details show the launcher's path and the arguments.

**Startup hooks.** The framework-dependent publish now sets `StartupHookSupport=false`, as the self-contained one does, and `eng/publish.ps1` checks both payloads' runtime configuration.

### Files

- `src/FileCat.App/Windows/AdministratorLauncher.cpp`: the launcher. Its `AdministratorLauncher.manifest` asks for no elevation (`runas` elevates it), so tests start it without UAC. `AdministratorLauncher.targets`, imported by `FileCat.App.csproj`, builds it after the compiler on Windows, copies it beside `FileCat.exe` in build and publish folders and refuses a Windows publish without it. It rebuilds when its sources, the version or the source revision change.
- `src/FileCat.App/Services/AdministratorWindow.cs`: the start info names the launcher; `Start` and `StartFileRetry` refuse without it.
- `eng/Build-PrivilegedBootstrap.ps1`: a `-Component` parameter (`PrivilegedHost`, the default; `AdministratorLauncher`; `AdministratorLauncherStandIn` for the tests). Built by the changed script, the helper's win-x64 bootstrap at `cafc6f65e` (version 1.0.0) is byte-identical to the old script's (`f9997d7f9a26b3992c5812f6bf7e76d3abc61582491dc899cd5c2a5f8c676d14`), and its receipt keeps its fields.
- `eng/publish.ps1`: `StartupHookSupport=false` for the framework-dependent publish; both payloads' startup-hook setting is checked, and each must hold `FileCat.AdminLauncher.exe` built for the package's architecture.
- `eng/installer/FileCat.iss`: a comment only. The recipe already installs the whole checked payload.
- `.github/workflows/ci.yml`: the Windows x64 and ARM64 lanes keep the launcher's build receipt and output with the helper's.
- Tests: `tests/FileCat.App.Tests/AdministratorLauncherTests.cs`, the stand-in in `tests/FileCat.App.Tests/AdministratorLauncher/`, built by `FileCat.App.Tests.csproj`.
- `docs/adr/ADR-14-privileged-broker.md` and `docs/BUILDING.md`: the launcher, and the C++ build tools that Windows builds now need.

### Tests

`AdministratorLauncherTests` has six tests. The native ones start this build's launcher without UAC, beside a windowless stand-in named `FileCat.exe`. The stand-in records its command line, the arguments the C runtime parses from it (as `FileCat.exe`'s .NET host parses them), its environment and its working folder. The fixture folder's name has spaces and text outside ASCII, as `C:\Program Files\FileCat` has spaces.
1. **Every route's start info** names the launcher in FileCat's folder, with `runas`, the system folder and `--new-instance` first, and its arguments parse back to the route (all systems).
2. **A missing launcher or `FileCat.exe`** stops `Start` and `StartFileRetry` before Windows is asked, and a start info for `FileCat.exe` itself is refused (all systems).
3. **The build** puts the launcher beside `FileCat.exe`, for the process's architecture, with its version resource and neither half of the .NET apphost's placeholder.
4. **Environment and arguments.** I644's three probe variables and thirteen more runtime, host and diagnostics settings, several in other letter cases, with near-miss names such as `XDOTNET_KEEP` and `CORECLR`. The arguments have escaped quotes, a backslash before a closing quote, an empty argument, a tab, repeated spaces and text outside ASCII. The stand-in's command line is exactly its quoted path plus the launcher's arguments, and it parses them as expected. Of the runtime names, only `DOTNET_EnableDiagnostics=0` reaches it; other variables and `PATH` arrive unchanged; its working folder is the system folder; its exit code 37 comes back from the launcher.
5. **Every route through the product's own start routines.** All five routes through `AdministratorWindow.Start` or `StartFileRetry` and the shell, without `runas`. The stand-in gets exactly the start info's arguments and a clean environment, and was idle when `Start` returned, so a staged request outlives the new window's start.
6. **Startup and lifetime.** With a stand-in that turns idle after 1.5 s and stays 5 s, the launcher turns idle between the two and exits after it, with its exit code 23.

**Results on this host** (Windows 11 build 26320 x64, Debug build, App tests run at medium integrity through `explorer.exe`, at the fix's final source):
- `AdministratorLauncherTests`: 6 of 6 passed.
- `AdministratorActionsTests`: 3 passed and 6 skipped. At this session's high integrity, the 3 tests among them that need an administrator token passed. The other 3 need the VM and skipped in both.
- `PermissionInspectorLayoutTests`: 7 passed; 1 skipped, which needs an administrator process and passed at high integrity.
- `JournalPlaceTests`' two administrator-window tests passed.

**Mutation check.** Each mutant was built by the same script, put in place of the launcher in the test output, and the class was run again at medium integrity.

| Launcher | Failed | What reached `FileCat.exe` |
|---|---|---|
| Skips the clearing | 2 of 6 | The injected `complus_ZapDisable=1`; through the shell, the test host's own `DOTNET_ROOT` |
| Removes the variables, but sets no `DOTNET_EnableDiagnostics=0` | 2 of 6 | No `DOTNET_EnableDiagnostics=0` |
| Compares names with case | 1 of 6 | `complus_ZapDisable=1` |
| Ends its own name at the first space | 2 of 6 | `admin launcher ü …`, the rest of the fixture's folder name, as arguments |
| This launcher, put back | 0 of 6 | |

**Packages.** `eng/publish.ps1 -Version 1.0.0-local.i644` ran here for win-x64 (260 s) and, cross-compiled, for win-arm64 (298 s). It ran at `aa779e0d5`, the fix's commit before it was rebased onto `ec126ea94` as `9b40f0df5`; the rebase changed only release records.
- Both payloads of each runtime carry the launcher, built for its architecture (`0x8664`, `0xAA64`), with the same bytes in the self-contained and the framework-dependent payload.
- The framework-dependent runtime configuration disables startup hooks.
- Each runtime's three SBOMs list the launcher as one of FileCat's own files. The portable and framework-dependent ZIPs carry it, still without the helper.
- The installer's payload matches its setup SBOM (`Assert-SetupPayloadMatchesSbom.ps1`, 401 files for each runtime). The installer itself was not compiled here, since that needs the pinned Inno Setup download; CI's ARM64 lane compiles it.
- PR #30's stricter SBOM tool, at `58153494`, accepts five of these payloads (x64 setup, framework-dependent and portable; ARM64 setup and framework-dependent). It binds only `FileCat.exe` and `FileCat.ShellHost.exe` to the reviewed apphost, and no reviewed archive holds the launcher.
- Given payload copies that lack the launcher, hold the other architecture's launcher or enable startup hooks, `publish.ps1 -FromPayload` refuses each, naming the problem.

### What the next installed candidate must check

- **The qualification VM's profiler probe** (this record's scenario) on the installed candidate: with the variables only in `HKCU\Environment`, Tools → Open FileCat as administrator must leave no marker. The ordinary control, with the variables in the process's own environment, must still load the probe, which shows the probe works.
- **The prompt** with the default UAC setting: its program name ("FileCat administrator window", or the owner's choice), the verified publisher, and the launcher's path in its details.
- **Signing.** `Sign-WindowsPayload.ps1` signs every unsigned PE file in both payloads, so it should sign the launcher with no change, once, since both payloads hold the same bytes. The candidate's signed-output manifests and `Test-WindowsSignatures.ps1` must show it signed by FileCat's certificate for x64 and ARM64. Not run here.
- **A review through UAC:** a link review or an administrator review still opens with its request. The launcher's idle and exit behavior was tested here only without UAC.
- **The window comes to the front** after approval: the launcher passes its right to take the foreground to FileCat. Not observed.
- **An upgrade or uninstall while an administrator window is open.** Restart Manager now also finds the windowless launcher, which exits when the window closes.
- **The framework-dependent package** opens an administrator window with a .NET runtime that its host finds without `DOTNET_ROOT`: the registered or default installation. A runtime reachable only through `DOTNET_ROOT` no longer serves an administrator window.

### Not changed

- Copies in folders the ordinary account can write (I640).
- FileCat started elevated in other ways, for example Explorer's Run as administrator or an elevated shell, does not pass through the launcher and gets the account's environment, as any elevated program does.
- Variables outside the runtime's prefixes are not audited (see above).
- The launcher links the Microsoft C++ runtime statically, as the helper's bootstrap does. Both build receipts leave the license and system-library classification open (`LicenseAndSystemLibraryClassificationComplete: false`, I03).
- Copied beside another program named `FileCat.exe`, the signed launcher would start that program after a prompt naming FileCat. The signed `FileCat.exe` apphost already starts any `FileCat.dll` beside it, so this adds no new kind of exposure.
