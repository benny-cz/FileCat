# E-I155 — administrator broker accepts a native profiler before Main

2026-10-06. Confirmed High pre-consent native loader defect under I17/V06/B04. Open; correction/revalidation in progress. No candidate/stable GO.

Actual clean committed **99e54b3f61cbc7ecc67a5caf620c992e3f9c0a19** production win-x64 self-contained and framework-dependent/ReadyToRun helpers run from separate fresh protected Program Files fixtures in Windows Insider 26300. The private launcher calls the actual `ElevationBroker.Launch` runas route with process-only `CORECLR_ENABLE_PROFILING=1`, an owned synthetic profiler CLSID and an absolute ordinary-writable owned DLL path. Both actual helper PIDs (12516 SC, 11108 FDD) match DLL attach markers reporting administrative token membership. The control factory deliberately returns class-unavailable and supplies no profiler callbacks. Its attach writes only the owned marker; no plan, registry operation or device opens. Both helpers are stopped after six seconds; native dialog/healthy plan behavior is unobserved.

Two direct LoadLibrary/FreeLibrary positives bracket the helper launches and exit 73. The full pinned actual payload, retained outputs, owned worker absence and protected fixture removal verify. No machine/user environment, UAC policy or security setting changes. Caller already administrative: this is actual pre-entry-point native code acceptance, not a demonstrated unelevated escalation or UAC bypass. The managed startup-hook switch from I154 does not guard native profiler loading. [The runtime profiling design](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/profiling.md) documents profiler initialization and environment selection. [The environment documentation](https://github.com/dotnet/docs/blob/main/docs/core/tools/dotnet-environment-variables.md) documents process diagnostic/profiler controls; clearing them inside managed Main would occur after this load.

Private root `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\broker-loader-20261006`. `profiler-probe-v1` failed native control compilation on an SDK declaration/linkage collision before any guest execution. Its sources/diagnostics remain. V2 uses a uniquely named C export aliased to the expected factory name; both harmless controls pass. Those fixture failures are not broker results.

| Retained path | SHA-256 |
|---|---|
| profiler-probe-v2/native-baseline-v1/independent-native-baseline-v1.json | 81ed1fcef128355facf3ab6f1e9818837e3a8666be8a9d929861f8e24bacc1e2 |

Pre-CLR environment and runtime/dependency trust must be established in a native entry point. Correction and identical/native/committed/CI revalidation continue; limited-caller, consent, full runtime/search/dependency/pipe and installed candidate scopes remain open in I17.
