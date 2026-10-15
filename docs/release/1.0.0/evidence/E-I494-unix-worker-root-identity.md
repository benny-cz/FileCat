# E-I494 — the Unix picture worker decoded hostile input as root

I08/TV-16, 2026-10-14. **A demonstrated containment gap, corrected and verified on Ubuntu.** The Mac root case was unavailable in this original run and is now separately measured in the [2026-10-15 Mac follow-up](E-I494-mac-root-worker-identity.md). No candidate, publication or human GO follows.

## The gap

FileCat may be started with root rights: a recovery session reads devices directly, and the product's own privileged paths are gated on `Environment.IsPrivilegedProcess`. Its picture worker (`PictureWorker.Run`, started per picture) applied `LinuxPictureProcessPolicy`, whose seccomp filter denies `fork`, `vfork`, `execve`, `execveat` and `clone3` and sets `PR_SET_NO_NEW_PRIVS`. That bounds what the worker may **start**, and nothing about what it may **read or rewrite**: a root-started FileCat handed every picture to a decoder running as uid 0, with SkiaSharp's native codecs as the parsing surface. The same applied on macOS, which has no process-creation filter at all.

## The correction

`UnixWorkerIdentity.Drop` runs before the worker reads a byte of the picture: supplementary groups are dropped first (while still root), then the group, then the user, to the account that asked for FileCat as sudo records it (`SUDO_UID`/`SUDO_GID`). The result is verified — real and effective user, group, and that `setuid(0)` no longer succeeds — and a worker that is root with no requesting user **decodes nothing** and returns the reason, rather than decoding with root's rights. The process-creation filter is applied after the drop and is unchanged.

## Measured on the owned Ubuntu 26.04 VM

`FileCat.WorkerProbe`, which calls the product's own `UnixWorkerIdentity.Drop` from `FileCat.Core.dll` (`90175b0c…`), against `/tmp/fc-root-only.txt` — mode `600`, owner `root:root`, holding a sentinel:

| Started as | Identity before → after | Root regain refused | The root-only file |
|---|---|---|---|
| Ordinary user `benny` | `1000:1000` → `1000:1000` | n/a | refused (`UnauthorizedAccessException`) |
| Root through sudo, `SUDO_UID` set | `0:0` → `1000:1000` | yes | refused (`UnauthorizedAccessException`) |
| Root with `SUDO_UID`/`SUDO_GID` cleared | `0:0` → `0:0`, drop refused with its message | no | **read: the sentinel came back** |

The third row is the authority the product now declines to use: the probe keeps reading to show what a decoder in that position reaches, while FileCat itself returns the refusal and decodes nothing. Two host controls fix the other paths: an ordinary Unix user keeps the identity it has with no refusal, and on Windows the call is a no-op because the worker's token is restricted there instead ([I493](E-I493-worker-administrator-authority.md)).

## Retained private evidence

`E:\FileCat\artifacts\release-evidence\i494-unix-worker-root-20261014-v1`, five files, self-excluding manifest `sha256.json` SHA-256 `0456a64f8e9df997d1eb0c087a181748af2bdd424fd9adb8a8d22512004a7b1d`: the raw Ubuntu probe output, the probe script, the host control run, and binary identities at commit `953c3e11` (probe `bdc10380…`). The owned root-only fixture and the probe directory were removed in the same run.

## What this does not establish

At this original source identity the macOS root case was **not verified**: no root run was made there. The later [Mac follow-up](E-I494-mac-root-worker-identity.md) closes that identity-probe gap at its own source commit. Neither record proves a verified sandbox — the dropped worker still has the requesting user's own filesystem and network reach — nor the behaviour of `pkexec`, `doas`, a root login shell with no sudo record, setuid installations, containers with a single uid, or an installed candidate. The pushed-source CI lanes cover only the non-root controls, because the hosted runners do not run tests as root. I08 remains open.
