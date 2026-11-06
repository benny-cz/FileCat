# Mac guest cancellation and direct helper death

## Result and remaining scope

The authorized macOS 27.0.1 M1 machine, ordinary UID/EUID 501, passes four live cancellation cases, ten invalid
cancellation-descriptor refusals and twelve direct helper-death cases. The final run also passes all sixteen healthy
TAR/gzip/BZip2/Zstd roles with independent extraction and the twenty parent-death/eight invalid-parent regression cases.
All task-owned FileCat/test instances and observed dedicated virtualization providers are closed after testing. The
existing until-reboot keep-awake helper (PID 36765) remains active; the Windows host desktop is never used for live tests.

This implements and verifies a private VM lifetime mechanism. **No production profile/backend is registered.** Startup
cancellation and exact release/signing/package states, whole guest/helper/provider memory and CPU accounting, process
denial, hostile assets/IPC, large-scale/fuzz/performance and guest-system source/license closure remain open. RAR
analysis/feasible implementation and the subsequent independent UX/UI/functional/security review remain later goal work.

## Dedicated channel and actual session integration

The managed parent creates an inheritable anonymous-pipe read end and retains the sole write end. Its
`IArchiveWorkerProcess.RequestCancellation` atomically closes that writer; it sends no archive protocol bytes and
does not wait for protocol capacity. The native host requires a canonical inherited descriptor, a read-only FIFO and
successful nonblocking/CLOEXEC setup before VM objects are created. EOF is the only valid parent signal; bytes are a
malformed channel and trigger refusal/stop.

The native loop polls this descriptor independently from the archive stream. It relays one fixed byte through a third
raw serial port; guest init exposes only read-only fd 3. The guest supplies the resulting token to the real archive
roles and reuses the exact Core `ArchiveWorkerTransport.cs` as a linked validation source, without broadening Core's
production API/friend surface. Both transport directions retain their existing bounded queues and cancellable waits.

If the guest does not stop cooperatively, the native host calls the public VM stop API after 1.5 seconds, with margin
before Core's two-second hard-kill boundary. It requires actual stop completion/state zero. Direct helper termination
remains the final hard fallback. Reports distinguish cancellation sent, guest cancellation markers, natural guest exit,
explicit forced stop, native exit/reaping and the virtualization provider's exit.

## Cancellation cases

| Case | Measured result |
| --- | --- |
| Actual `ArchiveAssessmentClient` / `ArchiveWorkerSession`, source request outstanding | Caller gets cancellation; cooperative guest exit zero and actual helper reaping in 0.0288601 s |
| Guest blocked awaiting an assessment request | Guest token interrupts the bounded transport; cooperative exit/reaping in 0.0585292 s |
| Full unread protocol output | Cancellation observed and relayed; explicit successful VM stop/state zero in 1.5183655 s |
| Guest spinning without protocol reads/token checks | Explicit successful VM stop/state zero in 1.5245506 s |

The actual client/session test uses its real cancellation callback, source authority and disposal/queue path. Its owned
source stream signals when a real proxy read arrives and waits for the token; no source bytes are returned after
cancellation. The caller must receive `OperationCanceledException`, not a successful receipt. All four helpers exit zero
and are reaped, while cancellation remains authoritative in the parent.

The blocked-output probe initially failed a test assertion that demanded cooperative exit for every IO state. Its
retained native facts instead show the permitted bounded hard-stop path. The corrected test requires a complete
permitted outcome: natural zero guest exit with cancellation marker, or explicit successful VM stop/state zero. The
actual assessment and blocked-input cases still require cooperation; the spin case requires forced stop. The final
full-output case has no guest cancellation marker before its forced stop. That observation does not establish whether
the signal, diagnostic output or subsequent guest shutdown was delayed; the underlying cause is not claimed proven.

An independent verifier checks all four native/guest logs, exact PID/exit/reaping/time facts and state zero. Ten bad
descriptors (noncanonical, standard IO, out of range, closed, write-only pipe, character device and socket) refuse with
exit 64/65 and no guard/VM output. No production cancellation gate is inferred for untested startup/release states.

## Direct helper-death proof

For blocked input, a spinning guest and full unread output, the observer performs two SIGTERM and two SIGKILL cases
each: **twelve total**. It keeps both protocol endpoints and the dedicated control writer open after terminating only
its own native helper. Thus neither EOF nor a cooperative guest read can hide a broken hard-stop path.

Before each VM it inventories existing virtualization providers. After guest readiness it requires exactly one newly
created provider, same UID, parent 1 and this system image:

`/System/Library/Frameworks/Virtualization.framework/Versions/A/XPCServices/com.apple.Virtualization.VirtualMachine.xpc/Contents/MacOS/com.apple.Virtualization.VirtualMachine`

It installs a kernel process-exit watch on that specific PID, kills/waits its own helper, then requires the provider's
exit event and absence after adopter reaping. Existing baseline providers are preserved; the observer never signals
the provider. Full-output cases also require at least 8,192 bytes pending in the unread stdout pipe before the kill.
All twelve providers disappear; the slowest measured kill-to-exit/absence interval is **0.04031879199999988 seconds**.
These are observations of this exact package/OS experiment, not a general guarantee about every VZ release/state.

The early two-case investigation with the prior helper also observed dedicated provider exits after SIGTERM/SIGKILL,
with pipes retained, in about 31 ms. It is separate provenance, not added to the twelve final cases.

## Evidence and reproduction

[The owned runner and instructions](../../../eng/validation/archive-writing/macos-vm-worker/README.md) use the pinned
kernel/module/library fixtures, CLT clang 21.0.0, SDK MacOSX27.0 and stable .NET 10.0.12 guest. No privileged helper or
global developer setting is installed. Captured startup sources remain unchanged; the final independent descriptor/
cancellation verifier was executed after the full run and its command is also included in the reusable runner.

The final Mac output is `/Users/benny/FileCatValidation/archive-writing-42829297/macos-vm-cancel-validation-v2`. The frozen
`macos-vm-cancel-evidence-v1.tar.gz` is **104,636,537 bytes**, SHA-256
`6E32A44D35166E526DF1B7CE2EC5939D8C0F8E3ACB3B01F24A6367D10FF3B919`. Its **382 payload rows plus inventory** preserve the
22 startup snapshots, published/runtime assets, native helper and guest ELF/initramfs, build/signing/tool logs, all
healthy/parent/cancellation/helper-death outputs and facts, fixture/source bundles, initial failed strict assertion,
early direct-death investigation, final reusable tooling and cleanup proof for **86 known owned PIDs**.

All artifact hashes/lengths are verified after transfer into the private worktree's ignored
`.tmp/test-results/archive-writing/macos-vm-cancel-v1`. Eighteen captured owned code files match byte for byte; the copied
Core transport differs only by CRLF/LF and exactly matches the canonical Git source after line-ending normalization.
Its captured SHA-256 is `D23B34D23FD2FCD666559D4852092F07025B384650849C9B9F9F6645E0E1DF54`, canonical LF SHA-256
`1CE45DA3B19C02DDEEB5E4969AD7D4D0097652251149694B9938397C94AFC039`. The exact after-run verifier is separate provenance.
The final runner adds that successfully executed verification command, and README clarifications are post-run docs;
captured startup snapshots remain intact.
