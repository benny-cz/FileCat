# I646 — The Linux root process census opens an executable path that another account can replace

**Found:** 2026-11-24 by the B12 review of the modes added since B12 (I641, [PR #28](https://github.com/benny-cz/FileCat/pull/28), finding F2), in the source of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md). `src/` at `main` `f4e42468b` is identical to the tag. **Status:** open. A fix is proposed for the next candidate, and the owner decides.

## Observation

`--filecat-recovery-process-census` runs as root by design during deleted-file recovery. It goes through `RecoveryProcessCensus.cs:43` and then `SingleInstance.cs:110`, `:136-141`, `:145` and `:157-180`. For every process it:
- opens `Process.MainModule.FileName` with a plain `FileStream`, which follows links and blocks on a FIFO;
- reads up to 512 MiB from it.

The review checked dotnet/runtime v10.0.12: for a deleted executable, that name keeps the ` (deleted)` suffix that `/proc/<pid>/exe` shows.

## Why it matters

Any local account can:
1. run a program;
2. delete it;
3. create `<path> (deleted)` as a FIFO, or as a link to a device.

The census, running as root, then either hangs on the FIFO, so FileCat cannot finish and the scan is refused, or opens that device and reads from it as root.

**Not run.** This rests on the source and the runtime's source; nothing was tried on Linux.

## Remedy (proposed) and its cost

Read the executable through `/proc/<pid>/exe` itself, which reaches the running file even after deletion. Otherwise open the path without following links and without blocking, and accept only a regular file. Keep the size cap. This changes product source, so it reaches users only in a candidate after rc.2.

## Fix

Proposed in [PR #32](https://github.com/benny-cz/FileCat/pull/32), branch `fix/i646-census-reads-proc-exe` (`6178a544b`, on `main` `1dfc0c12d`), for the candidate after rc.2. The status above stays until the owner decides.

**Linux.** The census no longer opens the name that `Process.MainModule` gives (`SingleInstance.ProcessImages.cs`, and the census loop in `SingleInstance.cs`):
- It opens each process's `/proc/<pid>` directory once, and reads both its `stat` line and its image through that descriptor.
- The image is opened through the `exe` entry itself: `openat(directory, "exe", O_RDONLY | O_NONBLOCK | O_NOCTTY | O_CLOEXEC)`. That entry is the kernel's own link to the running image, and reaches it after deletion. A file planted at `<path> (deleted)` is never opened.
- Only a regular file is read, checked with `statx` on the opened descriptor, within the unchanged 512 MiB limit.
- **Races.** A process that exits after its directory is opened makes both reads fail (ESRCH or ENOENT), so it is unknown. Nothing is read from a process that has taken over its ID. Unknown still refuses: `RepeatUncertain` retries the whole census, and the root census still exits with 3 on a persistent unknown. A process already gone before its turn is skipped, as before.

**macOS.**
- **No root census there.** Only Linux asks an administrator (`RecoveryProcessCensus.OtherFileCatRunning`), and `RunHelper` refuses elsewhere. The census runs inside FileCat, as root only when FileCat itself was started as root, with `sudo` for example.
- **The name** comes from `proc_pidpath` (dotnet/runtime `ProcessManager.BSD.cs`, `GetModules`). XNU builds it from the image's vnode, then checks only that something exists under it (`proc_pidpathinfo_internal`), not that it is the image. So, by the source, the old open could also meet a FIFO, a link or another file put in place of a deleted or renamed image. This was not tried on a Mac.
- **Change.** Darwin gives no descriptor for another process's image, and its file identity only to the same user or root. So the census now opens the name with `O_NONBLOCK | O_NOFOLLOW | O_NOFOLLOW_ANY | O_NOCTTY | O_CLOEXEC`: no link anywhere on the name is followed, and no FIFO is waited on. Only a regular file is read, checked with `fstat` on the descriptor, within the same limit.
- **What remains.** An account that can write the image's folder can still put another regular file there between the lookup and the open. That changes only that process's answer. The same account can already make the answer "running" by starting any program named FileCat or dotnet.

**Windows.** Unchanged. Its branches query the image path and read it as before; `IsFileCatAppHost(string)` now serves only them.

**Test seam.** `OtherFileCatRunning(…, inspectInventory: true)` inspects a supplied inventory as production inspects every process, instead of counting all of it as FileCat.

**Tests.** `RecoveryProcessImageTests`, 18 cases. The Linux cases run in hosted CI's `ubuntu-24.04 (App tests)` job; the macOS cases in its macOS App jobs.
- **A deleted image whose name is replaced (8, Linux).** The replacement is a FIFO, a link to a file that says the opposite, a link to `/dev/zero`, or a regular file that says the opposite. Each comes with an image that is an apphost and one that is not: a copy of `sleep`, with or without the apphost's `FileCat.dll` binding appended. Each case first checks that `Process.MainModule.FileName` is the planted name. The census must answer from the image within 10 s.
- **A running image (2, Linux):** with and without the binding.
- **An exited process (1, Linux).** With `/proc/<pid>` held, the process is killed and reaped; its image is still on disk. Reading the image through the descriptor is unknown (errno 3), and its `stat` cannot be read. A census whose inventory still lists the process skips it.
- **Opening a name (5, Linux and macOS).** A FIFO and a link are unknown, without waiting. A regular file and an apphost are read. A link on a folder of the name is refused, on macOS only.
- **macOS (2).** `/bin/sleep`, and a copy started through the temporary folder's `/var` link, are read as not FileCat. A deleted image with a FIFO at its name is answered without waiting.

**Runs on the Ubuntu 24.04.5 VM** (kernel 7.0.0-43, .NET 10.0.14), as the ordinary user, with the committed source and tests:
- **After:** 15 passed, 3 skipped (macOS only); the build has no warnings. `RecoverySafetyTests`, `SingleInstanceMetadataTests` and `MacProcessCensusTests`: 59 passed, 14 skipped (Windows or macOS only), none failed.
- **Before**, as mutations of the fixed source:
  - **The old line,** `IsFileCatAppHost(process.MainModule?.FileName)`: 8 failed. Both FIFO cases hit the 10 s limit, waiting on the FIFO. The linked and planted files gave their own answer, in both directions. The `/dev/zero` link was read up to the 512 MiB limit, and gave unknown.
  - **The safe open of that name instead of `exe`:** 8 failed. Nothing waited, but the FIFO and the links gave unknown, and the planted regular files were read.
  - **Opening a name** without `O_NONBLOCK`: the FIFO case hit the 10 s limit. Without `O_NOFOLLOW`, the link was followed. Without the regular-file check, the FIFO was read as empty, "not FileCat". Each failed only its own case.
- **Ad hoc, not committed:** the census over the VM's whole process list, without the 2 processes named FileCat or dotnet. It went through 460 processes, 303 of them kernel threads, in 469 ms. The answer was unknown: 59 processes that the user may not inspect refused `exe` (errno 13), and 1 had no image (errno 2). As before the fix, an ordinary census is unknown there and asks for the root census.

**Not verified:**
- **A root census.** The VM grants no `sudo`, so `--filecat-recovery-process-census` did not run as root, through pkexec or otherwise. Root differs in which processes it may open, not in how; the tests read the user's own processes.
- **macOS** runs only in hosted CI, on this change's pull request; no Mac was used.
- **Windows** tests were not rerun here; hosted CI runs them.
- **Linux on ARM64.** Its `O_DIRECTORY` differs from x64's; the census takes it from `LinuxOpenFlags`, as other FileCat code does. Neither the VM nor hosted CI runs Linux on ARM64.
- **ID reuse itself.** The tests show that reads through a held `/proc/<pid>` fail once the process is gone. Making the kernel hand that ID to another process needs privileges that the VM does not grant.
- **Slow file systems.** An image on a stalled FUSE file system mounted with `allow_other`, or on a hard NFS mount, can still hold the census in a read, as before. That is outside this issue.
