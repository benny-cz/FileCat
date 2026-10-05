# E-I137 — macOS kernel and zombie entries block recovery admission

Preliminary remediation, 2026-10-05. This extends E-V09-M1 and I106; no candidate exists.

## Retained baseline

E-V09-M1's clean `0bc1b6624033c93933ac8bba08473a7f1756f588` production remains byte-identical
in the private diagnostic. The owner reports Visual Studio closed and the staged trace preflight
run under sudo. Native ps still sees Visual Studio PID 1743 and its dotnet ServiceHub PID 1912.
The owner then explicitly authorizes needed Mac process termination. After checking their exact
executable identities, the agent sends SIGTERM to those two processes; both exit. No other task
is terminated.

The unchanged production census now returns **null**, with **zero matching processes and three
unknown managed identities**. The snapshot includes PID 0, root-owned defunct sshd PID 4157 and
another defunct/transient SSH entry. Native `proc_pidinfo` independently returns complete 64-byte
records for the kernel (PID/parent/UID 0, SRUN, system flag) and PID 4157 (SZOMB). The prior full
recovery session remains failed at admission, with zero device opens; it is not relabeled a pass.

The installed MacOSX27 SDK's `libproc.h`, `sys/proc_info.h` and `sys/proc.h` are retained with
hashes. Their ABI defines the short BSD record, process states and system flag. Apple XNU's
[process query implementation](https://raw.githubusercontent.com/apple-oss-distributions/xnu/main/bsd/kern/proc_info.c)
also documents including zombies when the query argument is nonzero. The
[record definition](https://raw.githubusercontent.com/apple-oss-distributions/xnu/main/bsd/sys/proc_info.h)
and [state definitions](https://raw.githubusercontent.com/apple-oss-distributions/xnu/main/bsd/sys/proc.h)
support the decoder. GitHub main is supplementary source documentation, not an assertion of the
exact running kernel version; the installed headers and native records pin this machine's ABI.

## Correction and current verification

The Mac census excludes only a complete native record identifying the kernel task or a zombie.
Creating, runnable, sleeping and stopped user processes still undergo executable identification.
Failed, short, mismatched and otherwise invalid queries remain unknown; ESRCH is not treated as
proof that no writer exists. Other system processes are not exempted. Windows and Linux paths
are unchanged. The existing unknown-identity refusal and known-FileCat precedence remain.

Seventeen decoder controls cover positive identities and adverse records. Two Mac-only native
checks cover the kernel/current process and an unavailable process query. Affected working host
checks pass **40/51**, with **11 declared platform skips**. Full working host App passes
**341/364**, with **23 declared skips**, zero failures. Complete TRX inventories, execution IDs,
skip explanations and source pins independently verify. Committed-source Mac/runtime/CI
revalidation is pending at this checkpoint; I137 is remediated, not Closed. Broader I106 remains open.

## Clean committed revalidation

Clean **`f62329788630514251a87d2524f256f978bef031`** is pushed to main. A full verified source
export produces the Release/self-contained osx-arm64 payload. All **843 exports**, **1,516 payload
files/1,517 ZIP members**, output pins and owned process/temp/device cleanup independently verify.
Native Mac admission/census tests pass **44/51**, with seven Windows-only skips; all **19 new
census controls pass**. Five native device/topology/descriptor controls pass without skips.
The full recovery session now confirms and opens the device once: the kernel/zombie admission
block is removed. It subsequently fails because the whole-file driver selects an intentionally
partial fixture; that failure and its independently correct partial bytes are retained in E-I138.
The source image is unchanged and detached. No whole-session success is claimed here.

All four required jobs pass in [CI run 37285720557](https://github.com/benny-cz/FileCat/actions/runs/37285720557);
three tag/manual package jobs skip. Four server artifact digests and six complete TRX inventories
independently verify. Full App totals: Windows **347/17 skips**, Ubuntu **319/45 skips**, Mac
**321/43 skips**, 364 cases each. Exact affected inventory: Windows **46/5 skips**, Ubuntu
**41/10 skips**, Mac **43/8 skips**. All 19 census cases are accounted for: native Mac cases pass
on Mac and declare their platform skips elsewhere; seventeen decoder controls pass everywhere.
ARM64 App totals are 347/17 skips and package-start/installer checks pass; no per-case ARM64 TRX
is available. The extra Unix affected skip in CI is the short-TMPDIR fallback precondition.
I137 is verified preliminarily, not Closed; broader I106 and candidate qualification remain.

## Owner-run trace pilot

The owner executes `~/FileCatReleaseValidation/TracePreflight-20261005.command`. The administrator
controller launches the synthetic control as ordinary benny/UID 501; both the control PID 4935
and eight-second fs_usage PID 4939 exit successfully. No source device is opened.

The retained trace shows the control's open, two 4,096-byte preads, one 4,096-byte pwrite and
fsync/close. Independent control-file bytes match before/after and the generated write. This
proves capture availability and this file's byte control, **not full source-device tracing**.
Reported offsets include high bits (`0x100000002000` and `0x100000001000`) absent from the
known 8,192/4,096 offsets; the process suffix is `Python.236040`, not PID 4935. Their encoding
and thread identity require calibration, as do whole-process/child coverage and loss controls.
No zero-source-write qualification is claimed. The administrator credential from the owner's
Terminal does not enable SSH `sudo -n`; it still requires a password. Actual fs_usage/authopen
qualification will require another bounded owner-authenticated launch.

## Evidence and limits

Private root: the authorized second workspace's `FileCatReleaseEvidence/mac-resume-20261005`.

- `owner-preflight-v1/independent-v1.json` SHA-256
  **`b16d7d79ef4f0dda312e015a85625973ad7d82e8c620f24e7bed468f3b96d195`**:
  transport/output pins, synthetic bytes/raw trace, original census, installed headers, native
  kernel/zombie records, identity-checked IDE termination and independent owned-process cleanup.
- `i137-host-independent-v1.json` SHA-256
  **`41c09385891b104968679f98fdffbd75f314f7fd452bcaa3470bd7b81d38cda1`**:
  both complete host TRX inventories and working source pins.
- `i137-native-baseline-independent-v1.json` SHA-256
  **`eb62953c52c676aaa540f1e10d93d7a038cc4a6a97793bd8b011142c406da36d`**:
  committed producer, 51 native cases/five controls, actual admission, retained I138 failure,
  independent partial bytes and source/process/temp/device cleanup.
- `../ci-37285720557/independent-ci.json` SHA-256
  **`55f9c2299cd136ad40d1661265f4e2c8a8a2b256d28fa0e5ca6cd14c364a8234`**:
  four required jobs, four server digests, six full TRX inventories and exact affected cases/skips.

The original refusals and trace interpretation limits remain retained. The Mac is a personal
installation (macOS 27.0.1/26A434 arm64), not a clean qualification environment. Native desktop
automation remains unavailable. Both VMs remain running, G: remains untouched/HOLD. Progress:
**115/137 issue rows remediated**, one separately Closed; **24/26 checklist steps partly or fully
open**. No candidate or human GO exists; overall **NO-GO**.
