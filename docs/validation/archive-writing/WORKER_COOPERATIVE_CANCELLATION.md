# Archive worker cooperative cancellation checkpoint

This checkpoint adds a dedicated cancellation lane to the disposable assessment, transform and verification workers.
It does not enable new production profiles or complete the common transaction, platform, UI or release gates.
Local move-into and feasible licence-compatible RAR writing remain separately required additions to the amended plan.

## Boundary and lifecycle

The parent owns the sole write end of a fresh anonymous pipe and never writes to it. Closing that end requests
cancellation. The worker receives only the read end through fixed canonical startup arguments. Windows inherits it
through the explicit AppContainer handle list alongside stdin/stdout/stderr. Linux preserves just that additional
descriptor across the trusted launcher exec, verifies a read-only FIFO, and continues to reject unrelated inherited
descriptors. No archive data chooses a handle or receives new filesystem authority.

A worker background observer converts EOF into its cancellation token. Any byte, or an unexpected channel failure,
is malformed and prevents a successful worker exit. All three endpoints use bounded cancellable protocol streams:
at most two queued input chunks and one queued output request, each chunk at most 65,524 bytes. Output completion
waits for the actual OS write/flush. Parser waits can cancel while an OS input read or output write remains blocked;
background IO threads do not keep the disposable worker alive.

Unix worker protocol IO uses the already verified standard descriptors directly. It avoids the global console-output
lock around Unix console writes, which would otherwise interfere with exit while an output pipe is saturated. This
choice is supported by the [.NET console implementation](https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/libraries/System.Console/src/System/ConsolePal.Unix.cs).
The cancellation observer closes its own read end after its read returns. Normal parser completion never disposes
that pipe concurrently with its pending read: an earlier Ubuntu attempt reproduced a completion/parent-exit wait
cycle with that approach. Still-blocked process-lifetime handles are released by actual worker exit.

The parent signals once and keeps its monitor running. A worker still alive two seconds after the signal is killed
through its native job/cgroup/process-descriptor controls. Reaping and final diagnostics/accounting happen afterward;
the queue reservation lasts through all of them. Two seconds is the cooperative grace, not a claim that every OS reap
finishes within exactly two seconds. Existing CPU, idle, wall and diagnostics failures can still stop a worker earlier.
Cancelled conversations cannot accept a late success receipt. Parent authority replies check cancellation again after
their bounded IO completes, before sending another reply.

Ordinary scripted process implementations retain a stop fallback for interface compatibility. The actual Windows,
Linux and controlled-child factories implement the separate signal. This does not qualify an ordinary process launch
as a production boundary. An already executing parent OS read may still be uninterruptible; network filesystems and
all currently unqualified profiles remain disabled.

## Acceptance and corrections

Dedicated tests cover canonical handles, malformed bytes, blocked input/output waits, bounded IO and actual flush,
all three roles waiting for a request, an uncooperative real process, and cancellation with saturated output. The
saturated-output assertion requires actual exit before the parent drains the pipe. Native tests repeat the three
roles and both adversarial processes under Linux containment; Windows additionally repeats all three roles through
the actual application's LPAC entry. Existing parent-death, unrelated-handle/descriptor and native resource tests remain.

The first source attempt exposed the Unix completion deadlock above. A test-script relabel also incorrectly changed
the independent C observer filenames; those remain the existing pinned `tgz-*-observer-v1` executables. The interrupted
failed attempt is retained, not counted as acceptance. The older two Ubuntu monitor refusals without precise causes
remain a separate unresolved historical investigation; this correction does not establish their cause.

| Final guest run | Core archive/session checks | UI checks | Native worker lane |
|---|---|---|---|
| Windows 11 x64 | 1,303 passed, 186 skipped | 222 passed | 60 passed under actual LPAC; storage suite separately 103 passed, 48 skipped |
| macOS arm64 | 1,425 passed, 64 skipped | 222 passed | Controlled children only; production hard-memory boundary still unqualified |
| Ubuntu x64/ext4 | 1,438 passed, 51 skipped | 222 passed | 61 passed, 1 skipped under actual cgroup/Landlock/seccomp controls |

The post-correction broad runs above cover 1,489 core cases per platform. A final two-file lifecycle follow-up adds
two failure-injection tests: an exit-observation failure during the grace must still dispose/reap while holding the
queue, and native signal/stop errors must not escape from the UI cancellation callback. Disposal is now in a finally
covering the grace itself; secondary stop failures remain diagnostic evidence. Its focused run passes all 61 checks
on the host and each of the three guests. The final actual Windows LPAC replay passes 60/60; final native Linux replay
passes 61/62 with the same one explicit skip. Both new failure-injection cases pass on each platform. The format and
UI paths are unchanged by this final parent-lifetime cleanup; their broad/oracle results above are retained.
Fresh gzip, BZip2, Zstandard and exact-time TAR output oracles
are part of the guest runs; all three formats still use separate transform and verification processes. Each guest's
outputs pass 12 Zstandard, 11 BZip2, 11 gzip and 8 exact-time TAR independent checks. Windows guest outputs are
read independently on the Windows host using the pinned native C library. Native cooperative exits are 16.7–28.0 ms
on Windows and 29.2–36.5 ms on Ubuntu, including saturated output without parent draining. Ignoring the signal requires
2,014.0 ms and 2,010.6 ms respectively through reap. Mac controlled cooperative exits are 8.6–13.8 ms; the ignored-signal
case is 2,063.5 ms through reap. Controlled runtimes are Windows/Ubuntu 10.0.14 and Mac 10.0.12; the Windows
self-contained bundle is 10.0.14. These observations do not establish universal scheduling/IO latency bounds.

## Evidence

Ignored evidence is retained under `.tmp/test-results/archive-writing/cancellation-v1`. All tested runtime code is
captured in immutable source/binary overlays; documentation added after the overlay is not runtime evidence.

- Corrected Unix source overlay, 56 files over the existing `3f67ec0b` guest baseline plus the prior Zstandard overlay:
  `389D92E49A3204A10D39D7264AAF1BDD6A8ED481365FC4A2DDC5F8906890DF53`.
- Corrected Windows managed overlay, 88 files: `23E0A76FC9DD0449923F14547F8486E6FD63A2CA5DE5DFC6D8A69949E6448E59`.
- Final two-file lifecycle source follow-up: `73AE91769FD88B6B6ED9F382894B664065D216423407105FBEBEB7177CBF35EB`.
- Final Windows managed follow-up, 88 files: `94147BC30608C55D9112D15A8C7CCB829CFF8AF75C16937B1B3091F008FCC41F`.
- Complete final 26-file delta from parent `9ec7ab36`, captured before final report edits:
  `A405E593A3E4A9CDC26A09A08FE404B74BF19FF81EE6442051EA4AB8A339364A`.
- Mac final evidence: `9550C84997A8E1487AB187BA188BAFD095DCFD6E06A806F74FFE6FCB8E31D13F`.
- Ubuntu final evidence, including interrupted failed-attempt logs: `C766D68912273B540735DF72D586F4DEFFB938FEB9E8804FE52E2088A1E8786E`.
- Windows final evidence: `2973085D908F4A8B18760652A1E280E2A870CEC7FAF8602133541985C5E4993F`.
- Mac final lifecycle follow-up: `1E8A7476521823AD2DD9050FB0071B3DBC0B46E70FA2E3240CE40C7E6FC87164`.
- Ubuntu final lifecycle/native follow-up: `DA074222605F67B31A5615804CA20D18F56A8387F3B776630CB2B6D1D159ED2D`.
- Windows final lifecycle/native follow-up: `119775EB8700CCA353F91A80B39A83DA97520D213758AE1DE4CDF0E8DCB26610`.
- Final development application SPDX SBOM: `811AC81A9BA19CE062E74EB1C4CD903C73F03D0DE7D29DD50454BE9054775C25`.

The rebuilt Windows application inventory contains 261 exact files, 34 reviewed NuGet packages and one runtime pack;
all 235 restored runtime files match their package archives. Separate payload-to-SBOM readback checks all 261 SHA-256
values and the exact file count. The dependency inventory is unchanged; no codec or package was restored or upgraded
for cancellation. The development SBOM records the dirty overlay identity and is not release/reproducible-build proof.

The shared `E:\FileCat` checkout remains on `main`; implementation stays in its isolated `codex/archive-writing`
worktree. Tests are postponed when another tester is present. Owned workers are reaped; the Mac's temporary
until-reboot keep-awake assertions are retained. No release qualification or absence-of-all-bugs claim follows from
this checkpoint.
