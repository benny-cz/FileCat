# I658 — The recovery census calls other accounts' FileCat absent when /proc hides their processes

**Found:** 2026-11-24 by I646's fix ([PR #32](https://github.com/benny-cz/FileCat/pull/32), noted by its author), confirmed in the source of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (`7269ec61`). **Status:** open; to be fixed for rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate).

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
