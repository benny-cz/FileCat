# Linux archive worker development checkpoint, 5 November 2026

Status: **unregistered development boundary; no new write profile enabled**. This checkpoint implements and exercises
Linux native containment for a stream-only assessment/probe worker. It does not complete AW1, qualify a release package,
or complete the [implementation plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md).

## Implemented boundary

`FileCat.Core/Archives/Linux` contains FileCat-owned native wrappers and an internal process implementation. The current
application entry point and production service registry do not dispatch this lane. The only executable entry in this
checkpoint is the self-contained `FileCat.WorkerProbe` qualification bundle.

Admission requires Linux x64/ARM64, a non-root unchanged UID/GID with no root supplementary group or retained effective,
permitted, inheritable or ambient capability, an actual cgroup-v2 filesystem, existing user-manager delegation of
memory/CPU/pids, an ext4 self-contained bundle, and Landlock ABI 8 or later with successful all-thread restriction.
ARM64 has predicate coverage only; it has not been run natively. Unsupported controls refuse; no helper is installed
and no global/user-manager controller setting is changed.

Each launch exclusively creates a nonce-named controller beneath the existing `user@UID.service` delegation. Limits are
read back: `memory.max=805306368` (768 MiB), `memory.swap.max=0`, `memory.oom.group=1`, `pids.max=128`, and no descendant
groups. A fixed trusted launcher establishes parent-death SIGKILL, waits for 32 bytes of nonce-bound parent authorization,
attaches to that controller, sets a finite exact soft/hard `RLIMIT_CPU`, sets no-new-privileges, marks every descriptor
above stderr close-on-exec, and execs a fresh CLR inside the controller. No archive input is consumed by that launcher.

The fresh worker verifies its actual parent signal, privilege state, limits, membership and descriptor inventory. All
three standard descriptors must be pipes. It warms crypto/deflate runtime assets before restriction, then installs a
default-deny architecture-specific seccomp policy with TSYNC and a Landlock policy with TSYNC. Only measured package and
exact system-library file reads, own process/controller observations, CPU facts and required read-only devices are
granted. The user's home, archive input paths and stage paths are not granted. Inputs/stage IO stay in the parent proxy.

Seccomp independently denies sockets (TCP/UDP/Unix), subprocess creation/exec, namespaces/mounts, remote-process access,
credential changes, metadata mutations, old/new xattr reads, io_uring, unqualified syscalls, unsafe ioctl/fcntl operations,
and explicit hugetlb/secret-memory routes. Legacy clone requires an ordinary thread-only flag subset; clone3 returns
ENOSYS for libc's register-based fallback. Runtime thread affinity is allowed only for the calling thread (`pid=0`).
Parent-death/CPU controls cannot be weakened. Signals can address only the worker's own positive TGID.

The parser runs on a separate thread after both global policies. A second main-thread-only seccomp filter denies
thread-only exit while preserving whole-process exit. This keeps the main task carrying parent-death state alive.
The parent process wrapper separately retains the dedicated creating thread until actual child reaping, because Linux
parent-death signals follow that creating thread. Normal stopping uses cgroup.kill and pidfds, with no PID-kill fallback.
Stderr retention is capped at 16 KiB, traffic above 64 KiB stops the worker, and repeated disposal shares one reap/drain.
Cleanup removes only the exact held, empty native controller; unresolved cleanup retains its path for review.

The native charged-memory boundary is 768 MiB, leaving a separately bounded 256 MiB budget for read-only runtime assets
under the plan's one-GiB ceiling. Bundle/system-file inventory accounts file bytes, not virtual address reservation or
arbitrary summed process VmRSS. This is not a claim that cgroup accounting equals every RSS measurement. Package update,
mapping/accounting races and complete resource qualification remain gates below. The managed heap hint is additional
only; the memory adversary uses real unmanaged touched pages.

## Native evidence

Owned Ubuntu source: `/home/benny/FileCatValidation/archive-writing-42829297/src`; dedicated self-contained bundle:
`/home/benny/FileCatValidation/archive-writing-42829297/archive-linux-probe`. Ubuntu 24.04 x64, kernel
`7.0.0-38-generic`, ext4, UID/GID 1000, existing delegated systemd user manager, SDK 10.0.101 and serviced runtime 10.0.14.
SSH sessions are outside the delegated user-manager subtree on this machine. The tracked runner uses a collected,
transient `systemd-run --user` service for the qualification driver; each worker has its own separate controller.
The service's summary memory statistic is not the worker memory measurement.

Final source overlay over checkpoint `05e3e61b`: 13 production/probe/test/project/runner files,
`linux-archive-native-source-v8.tar.gz`, SHA-256
`51C743E0AC457566CBA2743FCCFF59EDA7E2455A51CB970B62458CC296BCA4B5`.
Source builds on the Windows host and Ubuntu completed with zero warnings/errors. This is an overlay over the already
recorded source checkpoints, not an independent clean release build or package SBOM.

| Final lane | Passed | Skipped | Failed | Scope |
|---|---:|---:|---:|---|
| Ubuntu native Linux controller/policy/process | 40 | 1 | 0 | 41 total; only unsupported-platform guard skipped |
| Windows VM medium-integrity compatibility | 26 | 15 | 0 | Linux native cases explicitly skipped |
| Mac ARM64 compatibility | 26 | 15 | 0 | Linux native cases explicitly skipped; no Mac containment claim |
| Windows VM native worker regression | 25 | 0 | 0 | Existing LPAC/low-token/environment suite with updated shared Core/probe |

The ten native process cases exercise: positive approved-asset access plus unrelated content/write/mtime denial;
TCP/UDP/Unix socket and process/signal/control-change denials; kernel xattr syscall denial; a positive parser-thread
identity; inability to open the own memory controller for writing; a touched 64 MiB allocation followed by a touched
1088 MiB unmanaged allocation stopped by the kernel; finite CPU; bounded stderr flooding; repeated concurrent stop;
healthy 64 MiB framed proxy copy/full SHA-256 readback; actual strict TAR assessment after isolation; an explicitly
inheritable unrelated descriptor with a parent-side read control; main-thread-only exit denial; and abrupt real parent
exit with a positively started worker which ignores pipe EOF. Some assertions share a process case.

Final Linux TRX: `linux-containment-20261105T071152Z-b613aa5c-f26f-4ef2-8dc3-be6f39db89a5.trx`; raw log:
`linux-archive-native-ubuntu-v5.log`. The tracked `eng/validation/archive-writing/run-linux-containment.sh` produced this
run. The native memory test records `memory.events`/`memory.peak`; the CPU test records cgroup CPU accounting. Limit kills
are deliberate controlled resource probes, not accepted archive fuzz crashes.
The final memory peak was 805306368 bytes, with `oom=1` and `oom_group_kill=1`; final charged CPU for the one-second
probe was 1.011755 seconds. Final Linux TRX SHA-256:
`2D0C238E37227E20A3D98A933D108202E9C2D9BCAD470EB42392857C4B046D56`.

| Final Ubuntu bundle file | SHA-256 |
|---|---|
| FileCat.WorkerProbe apphost | `69E0308BAB17EBC10507E849116006939A58C92279B5DEEFBC1CCEB74B693143` |
| FileCat.Core.dll | `77C06D0B18053FA1F34932557434E4AA08BF48042790B96DD963DDEC10097BF9` |
| FileCat.WorkerProbe.dll | `FDBE511F2DB15B7DED54D0A44D058AFDC48153BD61FDEA58FB510BC76302472F` |

Windows compatibility TRX is `linux-archive-portable-vm3.trx`; its 28-file managed-binary overlay SHA-256 is
`81131175DAC4FCFCE1F52CF4C7EC34E143CBBC4F7A1D73AEE95AFD50023D4A6E`.
Mac TRX is `linux-archive-portable-mac-v3.trx`, built from the same final source overlay.
Windows native TRX is `containment-20261105T071223Z-164eca64644944ed9b1b48fe8ede2316.trx`; its 40-file updated test/owned
bundle overlay SHA-256 is `EC85EA249521F71516081FBA7ED5CBB5BC3F405F371EC9A6A0EB79F298384847`.
Windows runners separately record actual medium integrity (8192); this does not establish an ordinary UAC-filtered
interactive login or installed-package qualification. Existing self-contained runtime/application assets were retained,
with the updated shared Core/probe assemblies overlaid; this is not a complete newly published Windows package.
Raw logs, TRX, launch facts and artifact hashes are retained in the worktree's ignored `.tmp/test-results/archive-writing`
and the task-owned guest validation directories.

## Failures and corrections

- Initially published fixture modes were group-writable; the worker correctly refused. Qualification provisioning now
  inspects and removes group/other write permission from only the 207 task-owned bundle entries, preserving execute
  bits. Runtime admission does not silently repair an arbitrary package.
- Blanket affinity denial aborted CoreCLR thread startup with “Out of memory.” An existing strace identified the
  denied `sched_setaffinity(0, ...)`; the policy now admits only the calling-thread form. Explicit target/high-word
  aliases remain denied, with both ABI predicates covered. The healthy workloads then passed.
- An early qualification-test assertion before disposal left an empty owned controller. The test now owns disposal
  before asserting. The exact inspected empty controller was removed without recursion; later native censuses found
  no remaining task controllers.
- Review identified new `getxattrat`/`listxattrat` read routes which Landlock did not cover. They now have explicit
  seccomp denial alongside the older xattr operations, with native syscall and both-ABI predicate regressions.

Tests were started only after idle checks. Final Ubuntu census found no FileCat/test process or nonce controller;
Mac test processes exited and its temporary until-reboot caffeinate assertions were retained. Windows test workers
are reaped by the native lane; final guest census is recorded with the checkpoint. No other tester's instance was killed.

## Remaining qualification and implementation

Production early entry/registration, full transformation and fresh verification roles, cooperative cancellation,
cross-parent scheduling, parent publication/recovery, and full archive/UI workflows remain unfinished. Package admission
needs complete identity/update/alias races, descriptor/environment/IPC and all-thread adversaries, ordinary desktop
sessions, installed/AppImage/framework-dependent routes, and native ARM64 qualification. Other filesystems and older
kernels have not qualified; unsupported controls must keep the feature unavailable. The Mac hard-memory/signing gate
remains unresolved. Neither compatible compilation nor the Windows regression qualifies Linux on those platforms.
The required whole-goal independent UX/UI/functional/security review is still pending after the implementation loop.

## Primary interface references

The wrappers use public native interfaces; no third-party implementation or header was vendored and no dependency
was added. Interface review used the versioned Linux 7.0
[Landlock UAPI](https://raw.githubusercontent.com/torvalds/linux/v7.0/include/uapi/linux/landlock.h),
[x64 syscall table](https://raw.githubusercontent.com/torvalds/linux/v7.0/arch/x86/entry/syscalls/syscall_64.tbl),
[generic syscall definitions](https://raw.githubusercontent.com/torvalds/linux/v7.0/include/uapi/asm-generic/unistd.h),
[cgroup-v2 documentation](https://docs.kernel.org/admin-guide/cgroup-v2.html), and the
[parent-death](https://man7.org/linux/man-pages/man2/PR_SET_PDEATHSIG.2const.html) and
[seccomp](https://man7.org/linux/man-pages/man2/seccomp.2.html) manuals. The versioned UAPI defines the all-thread flag
used here; newer evolving Landlock documentation does not expand this checkpoint's qualified rights or kernel matrix.
