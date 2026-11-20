# Mac VM native-host parent-death guard

## Result and scope

On the authorized macOS 27.0.1 M1 ARM64 machine, ordinary UID/EUID 501 runs the native development helper with an
installed parent-exit guard. **20 forced-parent-death cases and 8 invalid-parent cases pass.** The 16 separate healthy
TAR/gzip/BZip2/Zstd create/assess/replace/fresh-verify roles still pass, including independent output extraction.

This fixes an actual prototype lifetime gap: previously a killed FileCat parent could leave its native VM helper
running until the helper's wall deadline. The native helper now stops the guest explicitly when its actual parent
dies. It observes successful VM-stop completion and state zero before returning the parent-death exit status.

This is a private, unregistered development mechanism. **No production backend/profile is enabled and no full AW1
packet is qualified.** Dedicated cooperative cancellation, direct native-helper death, complete guest/host memory and
CPU/process restrictions, hostile setup/assets/IPC, exact release signing/packages, licensing/source closure and the
remaining implementation/validation work stay open. RAR analysis/feasible implementation precedes the final independent
UX/UI/functional/security review after the main loop.

## Implementation

The native host requires a canonical positive parent PID and checks the actual kernel parent relation and ordinary
UID/EUID before creating VM objects. It synchronously registers `EVFILT_PROC/NOTE_EXIT` through `kqueue` with an
`EV_RECEIPT` success check, then checks the parent again. The loop polls the registered event and actual parent relation;
registration/poll errors refuse or stop the experiment. The process-event contract is a public supported interface.
[Apple kevent manual](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/kevent.2.html).

On observed parent death, the helper uses the public Virtualization.framework stop API. An in-flight start must reach
a stoppable state within the bounded stop window; failure is not reported as success. The original natural guest-stop
delegate and explicit stop completion are separate facts. The 120-second run deadline and five-second stop window use
the monotonic clock, and the run-loop pump uses a relative interval. Exact public contracts were also inspected in the
installed MacOSX27.0 SDK's `sys/event.h` and `Virtualization.framework/Headers/VZVirtualMachine.h`.

The actual worker entry point adds only a controlled development spin role for the lifetime probe. It writes a ready
marker and then spins without protocol reads or EOF-based exit. The application's entry point/backend composition is
unchanged. All native code builds using explicit CLT clang 21.0.0/SDK MacOSX27.0 with warnings as errors and the same
public virtualization entitlement. No global developer setting, privileged helper or host-desktop input is used.

## Live adversarial tests

The observer creates an owned fixture parent, which launches the exact native helper with its own PID. The observer
retains both protocol pipe endpoints and a separate launcher identity channel. It independently installs a kernel
process-exit observer for the helper before terminating the fixture parent. SIGKILL and SIGTERM target only that owned
parent PID, not its process group. Thus neither inherited signal propagation nor input EOF can satisfy the stop test.

| Case | Count | Required result |
| --- | ---: | --- |
| Parent dies before native exec | 4 | The observer releases the exec gate only after the parent is reaped; native exit 65 before guard/VM setup |
| Parent dies after guard setup | 8 | Actual parent exit, VM state zero, native exit 69; all observed runs also completed an explicit stop after startup |
| Guest blocks waiting for an assessment request | 4 | Real .NET guest readiness and protocol hello; with both pipes held open, successful explicit VM stop/state zero and exit 69 |
| Guest spins without input reads | 4 | Controlled busy marker; successful explicit VM stop/state zero and exit 69 |
| Invalid/noncanonical/unrelated parent | 8 | Startup refuses with exit 64/65 and no guard, guest or VM output |

Every active stop completes in **under 118 ms**; the maximum measured parent-kill-to-helper-exit interval is
**0.11750375000000002 seconds**. All 16 post-registration death cases also return a real kernel exit event. All direct
fixture parents are waited/reaped. Orphan helpers are reparented to the system adopter, observed exiting with the
expected kernel status, then required to disappear from the process table; the observer does not claim to waitpid a
non-child. No guest completion/wait footer is accepted as an alternative for the blocked/spinning cases.

These cases cover the measured setup/active windows. They are not proof of every possible VZ starting/error state,
clock failure, release entitlement, package asset or process race. Those remain production qualification work.

## Healthy regression and machine cleanup

The same final runner rebuilds the Linux-musl-arm64 .NET 10.0.12 guest and Mac parent, executes all 16 archive roles,
then checks BSD tar/native libzstd extraction, exact Unicode name and replacement bytes. All healthy helpers show the
correct live parent, no parent-exit/monitor-failure event, no requested/forced stop, natural guest shutdown/state zero,
zero exit and actual reaping. Healthy cgroup peaks are 10,534,912–39,927,808 bytes; they still exclude some prepopulated
initramfs/runtime pages and do not qualify the full VM/helper memory envelope.

Healthy native PIDs: 60847, 60855, 60858, 60861, 60864, 60867, 60870, 60873, 60876, 60879, 60882, 60885, 60888, 60891,
60895, 60898. Forced-death helper PIDs: 60922, 60928, 60934, 60940, 60946, 60954, 60962, 60970, 60978, 60986, 60994, 61002,
61010, 61017, 61024, 61031, 61039, 61046, 61053, 61060. All helpers and fixture parents are absent after the run.

The idle census runs before building, between normal roles and between adversarial cases. It postpones work if another
FileCat/dotnet/test-host appears and never signals another tester. The Mac is idle after the run; the existing temporary
until-reboot keep-awake helper (PID 36765) remains active. No live test runs on the Windows host.

## Reproduction and evidence

Use the [owned Mac guest runner](../../../eng/validation/archive-writing/macos-vm-worker/README.md) against a private source
snapshot and fresh output directory. It now runs `verify-parent-death.py` after the healthy-role and independent archive
checks. `parent-death/facts.json` records signals, exact PID relationships, direct/adopter reaping distinctions, retained
pipe facts, kernel exit statuses, VM state/stop facts and timings. Partial results/logs remain if a case fails.

The final Mac output is `/Users/benny/FileCatValidation/archive-writing-42829297/macos-vm-parent-validation-v1`. The frozen
`macos-vm-parent-evidence-v1.tar.gz` is **104,500,096 bytes**, SHA-256
`57FEC37E7F11B67ACBE7520A3558BD901A66EE236F7FFE562F50E5F1954E5614`. Its **337 payload rows plus inventory** preserve the
18 startup source snapshots, full published host/guest/runtime assets, owned ELF/initramfs/native binary, exact build/
signing/tool logs, 16 healthy-role logs and outputs, all adversarial logs/partial/final facts, original fixture/source
bundles, oracle identities and cleanup proof for 57 known owned parent/helper/driver PIDs.

Every hash/length is verified after transfer into the private worktree's ignored
`.tmp/test-results/archive-writing/macos-vm-parent-v1`. All **16 code snapshots** match the final implementation. The two
captured READMEs remain as they were at startup; this report and the post-run README clarification distinguish the newly
tested parent guard from the still-unqualified direct-helper-death/cancellation path.

The subsequent [dedicated cancellation checkpoint](MACOS_VM_CANCELLATION.md) implements the separate channel and
tests cooperative and bounded forced stops plus direct helper-death/provider teardown. Startup/release and full
resource/confinement/package qualification remain open.
