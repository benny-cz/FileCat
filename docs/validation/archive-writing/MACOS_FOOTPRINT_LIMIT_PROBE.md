# Ordinary-user Darwin footprint-limit probe

This follows `a39ad6d2` and the ineffective address-space limit in [the earlier native spike](NATIVE_WORKER_SPIKES.md).
It investigates another Mac worker-memory route. No production application code, package, entitlement, system setting
or dependency changes, and no worker/profile is qualified.

The installed MacOSX27.0 SDK declares `task_set_phys_footprint_limit(task_t, int, int*)` in `mach/task.h`.
Apple's published kernel source checks caller privileges before forwarding this request and retains the existing
active/fatal policy. A successful setter alone would require further enforcement and escape tests. That source is
context, not proof of the installed kernel's implementation. No Apple implementation code is incorporated into
FileCat. [Apple XNU task implementation](https://github.com/apple-oss-distributions/xnu/blob/main/osfmk/kern/task.c).

The FileCat-owned [C probe](../../../eng/validation/archive-writing/macos-footprint-probe.c) and
[supervisor](../../../eng/validation/archive-writing/run-macos-footprint-probe.py) run as UID 501 on the actual Mac.
The binary is ad-hoc/linker signed, has no TeamIdentifier and produces no entitlement output. A limited case requests
64 MiB before allocation and stops immediately if refused. The control requests no limit, then touches twelve 8 MiB
blocks with changing data. Maximum touched allocation is 96 MiB. Each child has a 20-second supervisor deadline and is
reaped. This diagnostic supervisor is not the production worker boundary; its timeout/kill branch was not exercised.

| Observation | Final actual result |
| --- | --- |
| Machine | macOS 27.0.1, Darwin 27.0.0 `xnu-13432.1.9~1`, arm64, 16 GiB RAM |
| Toolchain | CLT clang 21.0.0, explicit MacOSX27.0 SDK, `-std=c11 -O2 -Wall -Wextra -Werror` |
| Control PID 56140 | 100,663,296 bytes touched; 102,105,568-byte physical footprint; exit 0 |
| Limited PID 56141 | 64 MiB request returns 8 (`KERN_NO_ACCESS` in the installed SDK); no allocation; exit 0 |
| Lifetime | Both exact child PIDs reaped; neither needed a supervisor kill |

The direct setter is unavailable to this ordinary caller. This does not establish an enforced limit, a privileged/
entitled route, other Mac builds' behavior, or general impossibility of bounded Mac workers. Together with the prior
`RLIMIT_AS` result, it identifies two routes that do not establish the required boundary. Another enforceable route
must be implemented and qualified before enabling the Mac writer.

## Reproduction

Use fresh owned output/evidence paths and the matching installed compiler/SDK. On the observed Mac:

```sh
/Library/Developer/CommandLineTools/usr/bin/clang \
  -isysroot /Library/Developer/CommandLineTools/SDKs/MacOSX27.0.sdk \
  -std=c11 -O2 -Wall -Wextra -Werror \
  eng/validation/archive-writing/macos-footprint-probe.c -o "$probe"
python3 eng/validation/archive-writing/run-macos-footprint-probe.py \
  --probe "$probe" --evidence "$new_evidence_directory"
```

Check for other testers before running. The supervisor accepts a small regular executable owned by the actual user,
rejects linked/group/world-writable paths and checks native PID/UID observations. Read the facts: `PROBE_COMPLETED`
means the experiment ended, not that its resource gate passed.

The first compile used globally selected Xcode clang/linker 15 with the newer SDK and failed on an unknown SDK
architecture. Explicit CLT paths resolved the mismatch without changing global `xcode-select`; the older compiler
observation is retained. The first successful iteration has the same control success/setter denial. The final source
clarifies the control's zero requested limit, guards Darwin-only includes and handles exit between timeout and kill.

Another Mac tester (`dotnet` PID 55957) started after the first children exited. Further testing was deferred while
completed evidence was copied read-only. That exact PID was later absent and the census clear before the final run.
No other tester was killed, no host desktop input was used, and the temporary keep-awake configuration was retained.

## Evidence and limits

Raw evidence is retained under the private worktree's ignored `.tmp` directory. Native source/binary/result hash rows
are verified after SSH transfer, and complete inventories cover the packets. Final source hashes match tested copies.

| Artifact | SHA-256 |
| --- | --- |
| `macos-footprint-probe.c` | `30B73D1F30919F5A77F60623FB8448A3801A4D86097746B1CA04C4712FF1E0DA` |
| `run-macos-footprint-probe.py` | `7CF6D0239181E62FA4BA0FA9DD067FECCFDE21A511CBFDE666E5F6F8EDEBB75A` |
| Final Mach-O | `B91FCA9FF35EF04498EBBED2E1DDC29046A02D4252DB01B298A02C9E87E09E2C` |
| `macos-memory-evidence-v1.tar.gz` (20 payload files plus inventory) | `EEFD1E4F27D87AF64F57275AF532E7B8A8B444326C95787204CE440D70BF0B8E` |
| `macos-memory-evidence-v2.tar.gz` (21 payload files plus inventory) | `B1D358216F9B6EF06218ABC2A1D88DD6D19913B842FAB3896564078755F7D1D2` |

Complete Mac confinement, application integration, signing/package/resource qualification, remaining formats and
workflows, RAR analysis/feasible implementation and final independent review remain unfinished.
