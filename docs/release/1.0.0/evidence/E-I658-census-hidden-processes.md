# I658 — The recovery census calls other accounts' FileCat absent when /proc hides their processes

**Found:** 2026-11-24 by I646's fix ([PR #32](https://github.com/benny-cz/FileCat/pull/32), noted by its author), confirmed in the source of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (`7269ec61`). **Status:** remediated preliminarily for rc.3 on 2026-11-25 ([I658](../FILECAT_1_0_RELEASE_ISSUES.md#i658), merged into main as `a2f983e9a`); first recorded as open; to be fixed for rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate).

## Observation

Before scanning a disk for deleted files, FileCat refuses while another FileCat may write to it (`MainViewModel.Recovery.cs:518`). On Linux, `RecoveryProcessCensus.OtherFileCatRunning` (`RecoveryProcessCensus.cs:21-27`):
1. first runs the census as the user (`SingleInstance.OtherFileCatRunning`, which lists processes through `Process.GetProcesses`, that is `/proc`);
2. asks an administrator for the root census (`pkexec`) only when that answer is unknown.

An ordinary user cannot read other accounts' executable images, so with the default `/proc` their processes are visible, their identity is unknown, and the root census decides.

When `/proc` is mounted with `hidepid=invisible` (`hidepid=2`), a hardening option, other accounts' processes are not listed at all. The user's census then sees only the user's own processes and answers "no other FileCat", so the root census is never asked. With `hidepid=noaccess` (`hidepid=1`) the processes stay listed but unreadable, which already reads as unknown.

## Why it matters

On a computer with several accounts and that mount option, a scan for deleted files goes ahead while another account's FileCat may be writing to the same disk, which the census exists to prevent (V09). It needs that option and a second account running FileCat with files on the disk.

**Not run.** Mounting `/proc` with `hidepid` needs root, and neither test machine offers sudo without a password. This rests on the source and the documented behaviour of `hidepid`.

## Remedy (proposed) and its cost

When this process's `/proc` hides other accounts' processes, treat a "none" from the user's census as unknown, so that the root census decides. The `/proc` entry in `/proc/self/mountinfo` says whether it hides them: `hidepid=2` or `hidepid=invisible`, unless its `gid=` group exempts this process. A census already running as root is unaffected. This changes product source, so it reaches users in rc.3.

## Finite check

On Linux with `/proc` remounted `hidepid=invisible` and another account running FileCat, a scan request asks for the administrator census, and refuses while that FileCat runs. Without `hidepid`, the behaviour is unchanged.

## Fix

On branch `fix/i656-i658-linux-flags-census`, after I656's commit, on `rc3/integration-3` at `928ba1c52`, for rc.3. The status above stays until the owner decides.

**A census that cannot see every process does not answer "none".** On Linux, `SingleInstance.OtherFileCatRunning` now asks one more question when it has found no FileCat and nothing unknown: may `/proc` have left processes out (`LinuxProcessListingLimit`, in the new `SingleInstance.ProcessListing.cs`)? If so, the answer is unknown, and the reason goes to the census's diagnostic. The ordinary census then asks for the root census, as it does for an unreadable image. A FileCat it can see still answers "running". Tests that pass their own inventory are unaffected unless they pass a limit.

**What the check reads:** `/proc/self/mountinfo`, `/proc/self/status` and the `/proc/self/ns/pid` and `/proc/self/ns/user` links. Anything it cannot read or recognize counts as a limit.
- **The `/proc` mount.** Every mount on `/proc` must be the proc file system. `hidepid=off` (or `0`) lists every process.
- **`hidepid` limits** in each of its three modes, written as names since Linux 5.8 and as numbers before: `invisible` (`2`) and `ptraceable` (`4`) hide other accounts' processes, and `noaccess` (`1`) withholds them. An unknown value is a limit too.
- **Exemptions.** The `gid=` group exempts its members (file-system group or supplementary groups, as the kernel's `in_group_p`) from `noaccess` and `invisible`, but not from `ptraceable` (`has_pid_permissions`). A process with `CAP_SYS_PTRACE` in effect sees every process: root's census is unaffected. Both count only in the system's user namespace (`user:[4026531837]`). Outside it, `gid=` is printed in another numbering, and a capability does not reach processes outside it.
- **The PID namespace.** One other than the system's (`pid:[4026531836]`, the kernel's fixed `PROC_PID_INIT_INO`) is a limit for every account, root included (below).

**`noaccess` did not read as unknown.** The record expected that. But the census lists processes with .NET's `Process.GetProcesses`, which reads each process's `/proc/<pid>/stat`, and leaves out one it cannot read. dotnet/runtime 10.0 shows this: `ProcessManager.Linux.cs`, `CreateProcessInfo` returns null, and `Interop.ProcFsStat` `TryReadFile` catches the `UnauthorizedAccessException`, whose inner exception is an `IOException`. Under `noaccess` the base census listed only its own process and answered "none", as under `invisible` (below). The fix treats `noaccess` like `invisible`.

**A PID namespace of its own (containers, sandboxes).**
- **What FileCat cannot know there.** A process in a PID namespace other than the system's sees only that namespace's processes, whatever its account. A FileCat on the host, or in another container, that writes to the same disk is invisible to it, and to a root census started inside it.
- **What it can know, cheaply:** whether its own namespace is the system's, by the link's fixed inode number. Where it is not, the ordinary and the root census are unknown, and a deleted-file scan is refused there. This is a change: before, a census in such a namespace that found nothing answered "none".
- **Containers that share the host's PID namespace** (`--pid=host`) list the host's processes, and FileCat decides as on the host. Its `/proc` mount is the container's own, and its options are read like the host's.
- **Not detected:** a security module (AppArmor, SELinux) that keeps FileCat from reading other processes' `/proc` entries would also make .NET leave them out. Nothing cheap shows that.

**Tests.**
- `RecoveryProcessListingTests` (App, on every host), 24 cases:
  - the check: `/proc` without `hidepid`, with `off`, `0` and `subset=pid`;
  - each mode by name and number, and a `gid=` group the user is not in;
  - the `gid=` group in a supplementary group and as the file-system group, and that it does not exempt from `ptraceable` or in another user namespace;
  - root and `CAP_SYS_PTRACE` alone exempt, but not in another user namespace;
  - a PID namespace of its own, for the user and for root;
  - unreadable or unknown input: no mountinfo, no `/proc` line, a `tmpfs` on `/proc`, `hidepid=8`, truncated lines, no status;
  - optional mountinfo fields, and a second proc mounted over the first;
  - the census itself: unknown with a limit and nothing found, "running" when it sees a FileCat, "none" without a limit;
  - on Linux, the check against this machine: if this process cannot read process 1's `stat` or .NET does not list process 1, the check must report a limit; if it can, in the system's PID namespace, it must not.
- `RecoverySafetyTests` (App, Linux), 2 cases that also build against the base without the fix:
  - `A_census_beside_another_accounts_process_does_not_answer_none`, with `FILECAT_TEST_OTHER_ACCOUNT_PROCESS` set to a process of another account;
  - `A_census_in_a_PID_namespace_of_its_own_does_not_answer_none`, with `FILECAT_TEST_OWN_PID_NAMESPACE=1`.
  Without them they skip.

**Hosted runs.** These were on the throwaway branch `qual/i656-i658`, since deleted. Final run [45463438305](https://github.com/benny-cz/FileCat/actions/runs/45463438305), on `ubuntu-24.04-arm` and `ubuntu-24.04` (kernel 7.0.0-1017-azure), with the same results on both. The earlier run [45461002016](https://github.com/benny-cz/FileCat/actions/runs/45461002016) gave the same answers, but its harness started the census in a folder the census account could not enter, which turned passing runs into exit 4.
- **The setup.** One account, `fcother`, ran `sleep`. Another, `fccensus`, ran the census, from the base (`928ba1c52`, with the 2 cases above) and from the fix. `/proc` was remounted for each mode with `sudo mount -o remount,hidepid=…,gid=… /proc`, and without `hidepid` at the end. The output below is the census's own line.

| `/proc` | `fcother`'s process listed | Base census | Fix census |
|---|---|---|---|
| default | yes; 157 processes listed (x64), 162 (ARM64) | unknown: images unreadable (errno 13) | unknown, the same |
| `hidepid=invisible` | no; 1 process listed (its own) | **none** (the case fails) | unknown: "/proc is mounted with hidepid=invisible: other accounts' processes are hidden from this one" |
| `hidepid=noaccess` | no; 1 listed | **none** (fails) | unknown: "hidepid=noaccess: … cannot be read there, so they are not listed" |
| `hidepid=ptraceable` | no; 1 listed | **none** (fails) | unknown: "hidepid=ptraceable" |
| `hidepid=invisible,gid=` a group `fccensus` is in | yes | unknown: images unreadable | unknown, the same; the check reports no limit |

- **Root under `hidepid=invisible`:** the check reports no limit. The root census itself (`--filecat-recovery-process-census`, started with sudo, as pkexec would start it) replied `clear`, from the base and from the fix.
- **A Docker container** (`mcr.microsoft.com/dotnet/runtime:10.0`, its own PID namespace), as root and as uid 1002: the base census answered **none**, and the fix answered unknown, "this process runs in a PID namespace of its own (pid:[4026532317])".
- **The check against the machine** agreed in every mode: process 1 readable and no limit, by default, with the `gid=` exemption and as root; unreadable and a limit under each `hidepid` mode.
- **The App census classes** as the runner account, default `/proc` (`RecoveryProcessListingTests`, `RecoverySafetyTests`, `RecoveryProcessImageTests`, `MacProcessCensusTests`): 105 cases, 86 pass, 19 skip (Windows or macOS only, and the 2 opt-in cases), on both.

**Other runs.**
- **Mutations on this workstation**, each caught by `RecoveryProcessListingTests`:
  - `invisible` read as hiding nothing, with the census ignoring a limit: 9 of 24 cases fail;
  - `noaccess` read as hiding nothing (the record's expectation), with the `gid=` exemption granted in any user namespace: 4 fail.
  The hosted base runs above are the fix's absence on real kernels.
- **Ubuntu 24.04.5 VM** (x64, kernel 7.0.0-43, default `/proc`, no sudo), the committed source, Release: `SingleInstanceMetadataTests` and the App census classes, 122 cases, 103 pass, 19 skip. The check: "Process 1 read: True; listed: True; limit: none". Beside root's process 1, the census answered running: another tester's FileCat ran there under the same account.
- **Windows 11 workstation**, at medium integrity: the App single-instance, census and recovery classes, 130 cases, 0 fail, 44 skip. The check is Linux only.

**Not verified.**
- **pkexec.** The root census was started with sudo, with the arguments FileCat gives pkexec. FileCat's own request through pkexec, with a PolicyKit agent and an administrator's password, was not tried.
- **The scan request in the app**, the record's finite check, was not made in a running FileCat. Its census and refusal message are unchanged; the census's answer is what changed.
- **Older kernels.** Before Linux 5.8, mountinfo gives `hidepid` as a number; only the unit cases read that form. The hosted kernel is 7.0.
- **A user namespace** (rootless containers) and WSL2 were not tried. If WSL2 distributions do not run in the system's PID namespace, deleted-file scans there are refused.
- **macOS and Windows** are unchanged: the check runs on Linux only. macOS was not run here; hosted CI runs its classes.
