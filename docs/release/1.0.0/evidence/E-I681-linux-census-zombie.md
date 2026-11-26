# E-I681 — On Linux, one unreaped zombie process made recovery refuse to scan a mounted disk

**Found:** 2026-11-26, by the owner's consent walkthrough on `v1.0.0-rc.6`, Ubuntu step U3 ([rc.6](E-RC6-TAG-20261126.md)). **Severity:** Medium. Tools → Recover deleted files… refused a mounted disk for as long as any zombie process existed on the system. Nothing was read or written; the user could not proceed, and the message pointed elsewhere. **Disposition:** Must fix for 1.0.0, under the owner's rule of 2026-11-24 that every known issue is fixed. The owner chose rc.7 ("Yes, fix and rc.7").

## What happens

Before it scans a disk, FileCat's recovery census makes sure no other FileCat process could write to it. On Linux an ordinary account cannot read root's processes, so after the ordinary census FileCat asks polkit (`pkexec`) to run the census as root (I658).

In the walkthrough the owner approved polkit's dialog (U3). The system journal shows:
- 10:26:39: "successfully authenticated as unix-user:benny";
- 10:26:44: `pkexec` "Executing command [USER=root] … /opt/filecat/FileCat --filecat-recovery-process-census 567781 …".

FileCat then refused all the same: "FileCat does not scan disk sdb … yet: other FileCat processes may still write to this disk. Close other FileCat windows, wait for their helpers to finish, then ask for the scan again. If none are open, check that process information is accessible." Only one FileCat process was running: the owner's.

## Why

- **The root census answered "unknown".** Run by hand as root with `pkexec`'s clean environment, rc.6's census exits 3 and prints nothing.
- **A zombie caused it.** As root, every other process's image could be read except one: pid 10210, `sd_espeak-ng-mb`.
  - It was in state `Z` from 2026-11-22 21:11: a speech module that exited, but whose parent, `speech-dispatcher`, never reaped it.
  - A zombie keeps only its exit status for its parent. Its `/proc/<pid>/exe` is gone, so `LinuxImageIsFileCat` cannot open an image and returns unknown. One unknown makes the whole census unknown, and the census repeats that answer on every try.
- **The other platforms:**
  - macOS skips zombies explicitly (`MacProcessCanExecute`, `SZOMB`): "no task remains that can run managed code". The Mac had a zombie at the time, without effect.
  - Linux skipped kernel threads, but not zombies.
  - Windows has no zombies of this kind: an exited process reports `HasExited`.
- **Not just a VM quirk.** Zombies are an ordinary sight on Linux desktops: one buggy parent is enough. A user who has one cannot recover from a mounted disk until its parent exits or the session ends.

## A subtlety the fix must respect

A process whose main thread called `pthread_exit` while its other threads run also shows `State: Z` in `/proc/<pid>/stat` and `status`, and its `exe` link is just as unreadable. That process still runs and can write.

On the Ubuntu VM:

| Process | State | Threads | Tasks |
|---|---|---|---|
| The zombie | `Z (zombie)` | 1 | the leader only |
| A `python3` process whose main thread exited, one thread sleeping | `Z (zombie)` | 2 | `Z` and `S` |

## Fix

PR #70 (`src/FileCat.App/SingleInstance.cs`, `SingleInstance.ProcessImages.cs`). The Linux census skips a process whose stat state is `Z` or `X`, and whose `status` shows it as its process's only thread. Both are read through the process's own `/proc` descriptor, so a process that has taken over its ID cannot be read in its place. A leader whose other threads run stays unknown.

**Tests** (`RecoverySafetyTests`):
- The parsers.
- On Linux:
  - A census over a real zombie must answer none. The zombie is made by `sh -c 'sleep 0 & echo $!; exec sleep 30'`.
  - A census over a `python3` process whose main thread ended with `pthread_exit` while another thread sleeps must not. That case skips if `python3` is missing.

**Checked on the Ubuntu VM** (self-contained linux-x64 builds):
- The new tests pass.
- With the skip line removed, the zombie test fails: "Assert.False() Failure, Expected: False, Actual: null".
- Beside the VM's real zombie, rc.6's root census exits 3. The fixed build replies `FileCatRecoveryProcessCensusV1:<nonce>:clear` with exit 0.

PR #70 is merged into main as `0c1049b7` after its full-matrix run 45794347786 passed all 19 jobs; in its Ubuntu App tests the zombie test ran and passed. The fix reaches users in rc.7. On rc.7 the owner repeats U2 to U4 of the walkthrough, which run exactly this path.
