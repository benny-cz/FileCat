# E-I145 — Unix picture-worker runtime debugger artifacts

Classification: preliminary committed remediation; successor clean native automatic
cleanup and four-lane CI verify at cb85f0a. Original failures retained; no candidate or human GO.

The clean f017a9494758afdf4ab9e160391f4425aa27283c 25-case picture inventory passes
on native Mac/Ubuntu (24 pass/one explicit Windows-only Shell thumbnail skip), while
the required temp observer fails. Native readback finds 62 Mac and 64 Ubuntu orphan
runtime debugger FIFOs inside the owned worker TMPDIR. Native type/mode/owner and
every parsed FIFO producer's current process absence verify. Owned payload processes
are absent; exact FIFO paths are unlinked and only empty owned containers removed.
Original XML/TRX/stdout/stderr hashes remain unchanged. Automatic production cleanup
is explicitly false; later strict observer cleanup does not turn it into a pass.

Private root is the authorized FileCatReleaseEvidence/picture-admission-20261006
directory. Original Mac baseline-header metadata carried an older 348cbc7 value;
the retained annotation identifies the actual Windows baseline as c022f79 and says
no native baseline test was executed. Recovery's simple UID placeholder also named
the JSON owner fields 501/1000; their numeric values and ordinary groups remain
native observations, and the host annotation supplies NativeUID without rewriting
the original source records.

Committed correction sets DOTNET_EnableDiagnostics=0 on Unix picture-child startup,
matching RestrictedProcess's existing Windows environment policy. Microsoft's
[runtime setting documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#dotnet_enablediagnostics)
defines this flag as disabling debugger/profiler/diagnostic-port facilities before
startup. This bounded supplied-content child requires none of them. The parent
application's diagnostics and OS/kernel tracing remain available. No Unix sandbox
or ambient filesystem/network isolation is inferred.

| Retained item under private root | SHA-256 |
|---|---|
| `clean-v1/mac-executed/independent-recovered-v1.json` | `5513fc112b02a64284cf7695767cb726fbf980c50471a34fd8414679606675ee` |
| `clean-v1/linux-executed/independent-recovered-v1.json` | `b39b5a8eac31ca676722184e2feaaa330cd09d0b07d252835a8b952e572741c8` |
| `clean-v1/mac-executed/recovered/cleanup-observer-v1.json` | `442f356f5110ae5d97d7b05034250e5007a8ae2d29db56f348791c031016dce6` |
| `clean-v1/linux-executed/recovered/cleanup-observer-v1.json` | `3d7a94fb66a067f02445b6f7762e14df7039307b703e4a723861c5a6aaa1efc8` |
| `clean-v1/windows-executed/independent-guest-v1.json` | `0d1f982d210716b3d726c8aecb32b31626b93cf3387dfc567ade4ca632150272` |
| `corrected-v3/results.trx` | `b714424ea4271fe847c12bf889a04c8af91cfe1894900118495e887aac4c128c` |

No observer control was rerun to erase the original failed cleanup. Successor clean
payloads must pass both the 25 actual cases and automatic owned-temp/process cleanup.
Finite process observations do not qualify a whole native drawn workflow or candidate.

## Committed successor qualification (2026-10-06)

The combined I145/I146 correction is committed and pushed at
`cb85f0a43566874c44fc0ec29f5e2d24b0ff8504`. The 867 raw Git blobs, modes and
SHA-256 source manifest verify before clean self-contained publication; all five
affected source files are included. Native Windows guest: 25 pass/zero skips, 354
payload files unchanged before/after. Ubuntu 26.04.1 and macOS 27.0.1 each: 24 pass,
one explicit Windows-only Shell thumbnail skip, 350 payload files unchanged. All
six decoder-admission controls pass on each target. The owned temporary directories
are empty automatically except the expected empty test containers, which the
observer removes. No debugger FIFO is exempted or manually removed in these passes.
The test process is absent at the native checkpoint; Windows also checks all owned
payload processes. These are finite component/headless observations.

The original corrected Mac v2 capture passes its tests and temp checks, then its
collector shadows the process variable with an empty-container path while creating
the command receipt. That failed driver and all original results are retained;
its test PID was not retained. Independent readback verifies unchanged payloads,
results and an empty temp folder. A fresh v3 capture fixes only that variable name
and records the original command/PID with a complete independently verified receipt.
Neither failed capture nor original f017a94 cleanup/CI results are overwritten.
The new Mac baseline metadata identifies c022f79 as the Windows baseline and does
not assert that a Mac baseline ran; UID keys are preserved correctly.

Affected working host: 25 pass. Full host App: 357 pass/23 declared skips/380 cases,
with exactly the same tested DLL/PDB hashes as the affected run. Exact-source CI
[37387554116 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37387554116)
passes all four required lanes. Four server ZIP digests and six full per-case TRX
inventories independently verify: Windows App 363/17, macOS App 337/43, Ubuntu App
335/45 (pass/declared skip, 380 exact case names); Windows Core 787/57, Platform
166/33 and Remote 88/28. Affected picture cases are 25/0 on Windows and 24/1 on each
Unix CI lane; every new admission case passes. ARM64 App logs report 363/17/380,
and package-start/draw and installer compilation pass; no ARM64 per-case TRX or
physical-device qualification is inferred.

I144, I145 and I146 are preliminarily remediated at this exact source identity.
All-consumer/displayed bitmap memory, Unix containment, wider native UI/frame/AT,
reference hardware and exact-candidate qualification remain. No candidate or human
GO exists; release recommendation remains NO-GO.

| Successor retained item under the same private root | SHA-256 |
|---|---|
| `full-v2/receipt.json` | `a7079499665549a629bff9b772f9ecbf860cda0d5f7e82faf600c848af8966ed` |
| `full-v2/results.trx` | `591c98ff47e3bac0fbe675eda5f3dcc2fbd01d2bd62edb233e4eb2289a485b86` |
| `clean-v2/source.zip` | `2d7684984ae32550b65cba289c5bf7e81dca2240ab80220d0fc5effb4cd55d84` |
| `clean-v2/producer.json` | `fedc9dc5cdf0fd02193edda29c4ccc00a57d6e66dce919a5f44271537b09bc05` |
| `clean-v2/windows-executed/independent-guest-v1.json` | `3835393c62fb9c79fa49c55278ec27d39a45876abbc553dd6e6b99b74ade3c8e` |
| `clean-v2/linux-executed/independent-guest-v1.json` | `ce30fdc806fc01cf3403ada21a6dea3f09493e4a641e34ce6d9756d052c5483e` |
| `clean-v2/mac-executed-v3/independent-native-v1.json` | `ae58ff2a87760f900bdf096189dc44cb2e2a5057018f96507705f4928e563737` |
| `clean-v2/mac-executed/independent-observer-failure-v2.json` | `6e6bf952333b01824eb4d1e08c43c63111b86f27d9f713bc8c5c068df7906099` |
| `../ci-37387554116-attempt1/independent-ci.json` | `9fe191738c2f3fbea6c178a4059993f68f255615d25b77339bec5561a9624607` |
