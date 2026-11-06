# Fixed Mac guest RAM and whole-provider measurements

## Result and scope

The managed Mac experiment now accepts only **1024 MiB**, configures exactly **1 GiB guest RAM** and has no memory
balloon. Guest startup also checks kernel-reported physical memory before reading input. A separate unchanged
allocator-only experiment retains its earlier 512/1536 MiB settings and evidence.

On the authorized macOS 27.0.1 M1 machine, ordinary UID/EUID 501, the new setting passes all sixteen healthy archive
roles with independent output checks, twenty parent-death/eight invalid-parent cases, four cancellation/ten invalid
descriptor cases and twelve direct helper-death/provider-exit cases. Four new memory/output pressure cases pass and
eight other RAM arguments refuse before VM setup.

**The whole host-side memory boundary is still unqualified.** Provider RSS and physical footprint exceed 1 GiB under
allocation pressure. No worker limit is waived, sampled accounting is not treated as an OS hard control, and no
production backend/profile is registered. Complete host/helper/provider accounting and enforcement, CPU/process
denial, startup/release/package states, hostile assets/IPC, scale/fuzz, guest-system licensing/source closure and other
implementation work remain. RAR analysis/feasible implementation precedes the final independent review after the loop.

## Boundary and measurement method

The VM setting describes guest-recognized physical memory. Host backing and host-process accounting are different
quantities. The public property does not constitute a host RSS cap; these experiments measure that distinction rather
than infer containment from a configuration value. [Apple memorySize contract](https://developer.apple.com/documentation/virtualization/vzvirtualmachineconfiguration/memorysize).

The child still has the guest cgroup's 1 GiB ceiling, swap disabled, UID/GID 1000 with empty groups/no-new-privileges,
128-task cap and fixed CPU limit. Limiting the entire guest to 1 GiB also includes the prepopulated initramfs/runtime
pages omitted by the child's small cgroup peaks. That strengthens the guest boundary without closing the separate
trusted host/provider overhead question. No disk/network/share/balloon is passed to the guest.

The observer reads helper/provider/observer `proc_pid_rusage(RUSAGE_INFO_V4)` values through the installed public SDK.
A separately compiled C probe checks structure size **296** and offsets: resident **64**, footprint **72**, process
start **80**, lifetime-max footprint **240**. Python's ctypes layout must match. Each sample retains the process-birth
absolute-time identity; PID replacement is refused. Raw CPU counters are retained without claiming CPU qualification.

The observer checks the uniquely created same-UID system virtualization provider after guest readiness, then samples
at approximately 10 ms intervals while processing bounded progress or output data. Each process's instantaneous and
lifetime-max values remain separate; the per-process maxima below are not a simultaneous whole-host total. Diagnostic
abort watermarks (128 MiB helper / 2 GiB provider) protect this private run and are **not production limits**.

## Live pressure results

| Case | Workload/outcome | Provider peak RSS | Provider peak footprint |
| --- | --- | ---: | ---: |
| Native allocation control | Dirty every word of 256 MiB with changing entropy; complete and free | 686,112,768 B | 539,231,240 B |
| Native allocation overrun | Attempt 1280 MiB; kernel kills the child after 800 MiB touched; VM stops | 1,233,207,296 B | 1,086,506,424 B |
| Entropy output, actively drained | Ten seconds, 52,494,964 bytes received; cancellation/cleanup | 497,713,152 B | 349,537,216 B |
| Entropy output, unread | Ten seconds without draining output; cancellation/cleanup | 493,256,704 B | 346,211,240 B |

The provider footprint column uses the observed lifetime maximum. The native helper's maximum resident value is
**10,993,664 B**, maximum lifetime footprint **3,211,888 B**. The provider exceeds 1 GiB in both RSS and footprint despite
the exact guest-RAM setting. The tests therefore leave `whole_host_memory_boundary_qualified: false` explicitly.

The allocation probe uses bounded 8 MiB native chunks, touches every 64-bit word with changing nonzero entropy and
reports progress only after touching a chunk. It cannot pass by reserving untouched virtual address space. The control
must emit completion and a zero child wait status. The overrun must not emit completion and must report child SIGKILL
with a touched total below 1 GiB. This is an intentional allocation diagnostic; a resource kill during archive fuzzing
would still fail that fuzz case.

The output probe now changes the entire payload buffer on every write. Draining/unread cases each run at least ten
seconds before closing the dedicated cancellation writer. Both complete the previously tested native/guest stop path,
with helper reaping and provider disappearance. Finite observed plateaus are evidence for these exact workloads, not
a proof that every framework allocation or malicious device/IPC sequence is bounded.

An independent verifier rechecks sample maxima, stable birth identities, exact helper/provider disappearance, allocation
and output outcomes, then tests RAM arguments `512`, `1536`, `0`, `-1`, `+1024`, `01024`, `1024.0`, `2147483647`; each
refuses with exit 64 and no VM output. The managed helper's single fixed profile is distinct from the old allocation
experiment's deliberate larger-VM cgroup probe.

## Cleanup, reproduction and evidence

[The owned runner](../../../eng/validation/archive-writing/macos-vm-worker/README.md) builds the ABI check and actual
Linux/.NET 10.0.12 guest with CLT clang 21.0.0 / SDK MacOSX27.0, then runs healthy/lifetime and pressure checks. It changes
no global developer setting, installs no privileged helper and does not use the Windows host desktop. Other testers
are respected by the idle census. Every new pressure helper/provider exits; the Mac has no task-owned FileCat/test or
virtualization instance left, and the temporary until-reboot keep-awake helper (PID 36765) remains active.

The Mac output is `/Users/benny/FileCatValidation/archive-writing-42829297/macos-vm-memory-validation-v1`. The frozen
`macos-vm-memory-evidence-v1.tar.gz` is **104,696,144 bytes**, SHA-256
`AD8862A176455A818EDB42A78F19BA80C0A32A05091146F731A596415FD9E674`. Its **389 payload rows plus inventory** preserve 27
startup snapshots, full published/native/guest assets, ABI binary/results, tool/build/signing logs, all healthy/lifetime/
pressure outputs and accounting samples, original fixture/source bundles, final tooling and cleanup proof for **94
known owned PIDs**.

Every hash/length is checked after transfer into the private worktree's ignored
`.tmp/test-results/archive-writing/macos-vm-memory-v1`. Twenty-three captured owned code sources match byte for byte;
the copied Core transport matches canonical source after CRLF/LF normalization. The exact independent after-run
verifier is preserved separately. The final reusable runner appends that successfully executed verifier, and README
clarifications are post-run documentation; startup snapshots remain unchanged.
