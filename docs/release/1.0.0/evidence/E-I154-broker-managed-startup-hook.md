# E-I154 — administrator helper accepts managed startup hooks before its entry point

Date 2026-10-06. High pre-consent loader defect under I17/V06/B04. Native baseline verified; correction and qualification in progress. No candidate/stable GO.

Exact raw source `fd1d780ad3732bf0940f1d5bd896831d7881f6ab`, actual helper/Platform/Core code byte-identical at 78a0716. Publish uses production win-x64 self-contained and framework-dependent/ReadyToRun properties. Both helpers are copied into a fresh protected Program Files fixture in the disposable Windows Insider 26300 guest, with every input size/hash checked. A separate owned startup-hook assembly is explicitly writable by the ordinary account. The private launcher calls the actual `ElevationBroker.Launch`/ShellExecuteEx runas route; only its process environment carries the hook path.

Both real helper processes load the owned hook from outside the protected install folder, write the observed PID/image/Admin identity/working directory and exit 73 before the actual helper entry point. The self-contained returned PID is 4736, framework-dependent 11844; each matches its hook observation. Thus Main's DLL-directory, location, plan/requester/nonce and displayed-consent checks cannot guard this earlier managed code. Two direct hook positives bracket the actual launches. No operation plan, registry action or source-device open occurs in these hook cases; no authentication policy or persistent machine/user environment is changed. Owned workers are absent and the protected fixture is removed; retained output hashes independently verify.

The caller already has an administrative token. This demonstrates pre-entry-point code acceptance on the actual elevated helper launch route, **not** escalation from an unelevated caller or bypass of Windows UAC. Native GUI/UAC/valid-plan consent are unobserved. The controls are protected component copies, not an installed signed candidate.

The proposed narrow correction sets `StartupHookSupport=false` in the helper project so its protected runtime configuration disables startup hooks before Main. Exact SDK targets map that property to `System.StartupHookProvider.IsSupported`; the [runtime implementation](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Private.CoreLib/src/System/StartupHookProvider.cs) checks it before resolving or calling hooks. The [runtime startup-hook design](https://github.com/dotnet/runtime/blob/main/docs/design/features/host-startup-hook.md) describes the pre-entry-point environment route. Changing a managed environment variable inside Main would be too late. Native profiler/runtime/search/dependency/pipe/protected-file and candidate scopes remain in I17.

Private root `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\broker-loader-20261006`.

| Retained path | SHA-256 |
|---|---|
| native-baseline-v1/independent-native-baseline-v1.json | 233cf28d621f72cfc9646edab3c939e1a8e05e6c864d5728245ab6f62cd3cbfd |

Repeatable bounded sources are retained at `eng/validation/broker-startup`; no UI automation is used. Identical runtime-config-only comparisons, actual project publish checks, committed native repeat and required CI remain pending. I154 Open.

## Controlled and working-source correction

An identical comparison preserves all 205 self-contained/18 framework-dependent helper files except `FileCat.PrivilegedHost.runtimeconfig.json`; every launcher/hook binary is unchanged. Both fresh native launches now have no hook marker, while the two direct hook positives still run and exit 73. Each helper remains running for the six-second window and is terminated only after its returned PID/image are verified. Their reported exit code is -1; no dialog, successful plan refusal or healthy consent behavior is inferred. All payload/output/process/protected-fixture cleanup verifies. This is a finite bootstrap observation, not a complete native workflow pass.

Actual working project publishes produce the disabled runtime property in all four win-x64/win-arm64 × self-contained/framework-dependent configurations. Seven source pins verify before/after. A separate fresh Windows run of the actual working win-x64 outputs repeats no hook markers/two positives with all 431 payload pins and owned cleanup verified. Both helper processes are again explicitly terminated after six seconds; UI/plan behavior remains unobserved. The source is a recorded overlay on 78a0716, rather than a clean committed producer.

| Retained path under broker-loader-20261006 | SHA-256 |
|---|---|
| corrected-control-v1/native-baseline-v1/independent-native-baseline-v1.json | cdaaed58e36ef983480934f8322dac3fa7c28f87a3061ff399e48854dc956442 |
| working-correction-v1/independent-working-publish-v1.json | ab05f9596a50b1a74d23c5da02fc2c3e12c6c3cb240f5cc42e6aaf44639defe6 |
| working-correction-v1/native-input/native-baseline-v1/independent-native-baseline-v1.json | c2c42d8dfa42a96c363a38ee87deccc757b32aabfa3b8f473711b2aabbfa60ac |

I154 remains Open pending the committed-source/native/CI seal. Wider I17—including native profiler and runtime selection—remains Open regardless of this narrow correction.
