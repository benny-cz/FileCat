# E-I145 — Unix picture-worker runtime debugger artifacts

Classification: original committed native defect and working correction; successor
clean native/CI qualification pending. No candidate or human GO.

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

Working correction sets DOTNET_EnableDiagnostics=0 on Unix picture-child startup,
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
