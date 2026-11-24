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

Proposed in [PR #32](https://github.com/benny-cz/FileCat/pull/32), branch `fix/i646-census-reads-proc-exe` (`6178a544b`, on `main` `1dfc0c12d`), for the candidate after rc.2. Its macOS part failed on macOS and was corrected in `bada5da1d` (below). The status above stays until the owner decides.

**Linux.** The census no longer opens the name that `Process.MainModule` gives (`SingleInstance.ProcessImages.cs`, and the census loop in `SingleInstance.cs`):
- It opens each process's `/proc/<pid>` directory once, and reads both its `stat` line and its image through that descriptor.
- The image is opened through the `exe` entry itself: `openat(directory, "exe", O_RDONLY | O_NONBLOCK | O_NOCTTY | O_CLOEXEC)`. That entry is the kernel's own link to the running image, and reaches it after deletion. A file planted at `<path> (deleted)` is never opened.
- Only a regular file is read, checked with `statx` on the opened descriptor, within the unchanged 512 MiB limit.
- **Races.** A process that exits after its directory is opened makes both reads fail (ESRCH or ENOENT), so it is unknown. Nothing is read from a process that has taken over its ID. Unknown still refuses: `RepeatUncertain` retries the whole census, and the root census still exits with 3 on a persistent unknown. A process already gone before its turn is skipped, as before.

**macOS.**
- **No root census there.** Only Linux asks an administrator (`RecoveryProcessCensus.OtherFileCatRunning`), and `RunHelper` refuses elsewhere. The census runs inside FileCat, as root only when FileCat itself was started as root, with `sudo` for example.
- **The name** comes from `proc_pidpath` (dotnet/runtime `ProcessManager.BSD.cs`, `GetModules`). XNU builds it from the image's vnode, then checks only that something exists under it (`proc_pidpathinfo_internal`), not that it is the image. So the old open could also meet a FIFO, a link or another file put in place of a deleted or renamed image. On the Mac (below), a deleted image had no name (ENOENT) until a FIFO was made at its old name. Darwin then gave that name, now the FIFO's, and later the same name for another regular file linked there.
- **Change** (`bada5da1d`). Darwin gives no descriptor for another process's image, and its file identity only to the same user or root. So the census first looks at the name with `lstat`, which opens nothing: a FIFO, a device or a link there is refused without being opened. Only then is the name opened, with `O_NONBLOCK | O_NOFOLLOW_ANY | O_NOCTTY | O_CLOEXEC`. No link anywhere on the name is followed, its last part included, and a FIFO put there after the look is not waited on. Only a regular file is read, checked with `fstat` on the descriptor, within the same limit.
- **A link on one of the name's folders is refused, not resolved.** Darwin builds the name from the image's own folders, so there is no link on it. On the Mac:
  - The names of all 781 of its 783 running processes that had one, root's and other accounts' included, opened this way as regular files. Images in cryptexes are named under `/System/Volumes/Preboot/Cryptexes`, not through the `/System/Cryptexes` links.
  - Copies of `sleep` started through `/var/folders/…`, through `/tmp/…` and through a link to their own folder were named `/private/var/folders/…`, `/private/tmp/…` and by their real folder.
  - When an image's folder was moved and a link put at its old name, Darwin gave the moved name.

  So a link on a name Darwin gives can only have been put in place of a folder after Darwin built the name. Two ways of accepting such links were weighed. Resolving them, with `realpath` and then an `lstat` outside `/dev`, would let the account that put the link there choose which regular file is read, and would gain nothing for a real process. Allowing only Darwin's own links at the root, `/etc`, `/tmp` and `/var` into `/private`, would change nothing, as Darwin names no image through them. An ordinary account cannot make device nodes, but it can make FIFOs and links; the look and the open refuse both. The `FolderLink` case therefore keeps its expectation, unknown, now checked as ELOOP (errno 62).
- **What remains.** An account that can write the image's folder can still put another regular file there between the look and the open. That changes only that process's answer. The same account can already make the answer "running" by starting any program named FileCat or dotnet. A process whose image was deleted has no name, so it is unknown while it runs, as before.

**What failed on macOS.** Hosted CI's `macos-26 (App tests, remainder)` job, in run 45445858172 on `rc3/integration-2`, failed 3 of these cases. The census of `/bin/sleep`, and the reads of a regular file and of an apphost by their names, were all unknown. The first version opened names with `O_NOFOLLOW` beside `O_NOFOLLOW_ANY`, and XNU refuses that pair (`vn_open_auth`, `bsd/vfs/vfs_vnops.c`); either flag alone is accepted. On the Mac, every open of a name failed with EINVAL (errno 22), for `/bin/sleep` as for every other name. So the census was unknown for every process on macOS, and would have refused every deleted-file scan there. The whole-process-list case saw it, 787 × `image error 22`, but passed, as it looked only for ELOOP. The temporary folder's `/var` link was not the cause: no name Darwin gives has a link on it (above).

**Windows.** Unchanged. Its branches query the image path and read it as before; `IsFileCatAppHost(string)` now serves only them.

**Test seam.** `OtherFileCatRunning(…, inspectInventory: true)` inspects a supplied inventory as production inspects every process, instead of counting all of it as FileCat.

**Tests.** `RecoveryProcessImageTests`, 21 cases since the macOS correction (19 before). The Linux cases run in hosted CI's `ubuntu-24.04 (App tests)` job; the macOS cases in its macOS App jobs.
- **A deleted image whose name is replaced (8, Linux).** The replacement is a FIFO, a link to a file that says the opposite, a link to `/dev/zero`, or a regular file that says the opposite. Each comes with an image that is an apphost and one that is not: a copy of `sleep`, with or without the apphost's `FileCat.dll` binding appended. Each case first checks that `Process.MainModule.FileName` is the planted name. The census must answer from the image within 10 s.
- **A running image (2, Linux):** with and without the binding.
- **An exited process (1, Linux).** With `/proc/<pid>` held, the process is killed and reaped; its image is still on disk. Reading the image through the descriptor is unknown (errno 3), and its `stat` cannot be read. A census whose inventory still lists the process skips it.
- **Opening a name (6, Linux and macOS).** A FIFO, a device (`/dev/zero`) and a link at the name are unknown, refused before anything is opened, and nothing waits. A regular file and an apphost are read. A link on a folder of the name is refused by the open (ELOOP), on macOS only. Each case checks the reason it is given.
- **A FIFO at the name is never opened (1, Linux and macOS).** A writer waits in its own open of the FIFO, and is still waiting after the census has looked at the name. Any open to read, even one that does not wait, would end that wait.
- **macOS (2).** `/bin/sleep`, and a copy of `sleep` started through the temporary folder's `/var` link and through a link to its own folder, are read as not FileCat. The copy must be named by its real folders. A deleted image with a FIFO at its name is answered without waiting.
  - On macOS the copy of `sleep` is signed ad hoc. With System Integrity Protection on, macOS kills an unsigned copy of its own `sleep` outside the system volume ("AMFI: Launch Constraint Violation").
  - The deleted-image case waits 2 s before deleting. macOS assesses new ad hoc code from its file after it starts, and killed a copy deleted 0.1 s after it started ("ASP: Security policy would not allow process"); deleted after 0.25 s, the copy lived.
- **The machine's whole process list (1, Linux and macOS).** No running process is refused because its name has a link, because its image is not a regular file, or because the kernel refused the open's flags (EINVAL). On macOS this checks that the names Darwin gives real processes open; otherwise every census there would be unknown.

**Runs on the Ubuntu 24.04.5 VM** (kernel 7.0.0-43, .NET 10.0.14), as the ordinary user, with the committed source and tests:
- **After the macOS correction** (`bada5da1d`): 21 cases, 18 passed, 3 skipped (macOS only), none failed; the build has no warnings. The whole process list was unknown as before: 48 processes that the user may not inspect refused `exe` (errno 13), and 1 had no image (errno 2). None was refused for a link, a non-regular image or EINVAL. `RecoverySafetyTests`, `SingleInstanceMetadataTests` and `MacProcessCensusTests`: 59 passed, 14 skipped (Windows or macOS only), none failed.
- **After**, with the first version (source `6178a544b`, tests `fff712647`): 16 passed, 3 skipped (macOS only); the build has no warnings. On the whole process list, the census was unknown: 56 processes that the user may not inspect refused `exe` (errno 13), and 1 had no image (errno 2); none was refused for a link or a non-regular image. `RecoverySafetyTests`, `SingleInstanceMetadataTests` and `MacProcessCensusTests`: 59 passed, 14 skipped (Windows or macOS only), none failed.
- **Before**, as mutations of the fixed source, run with the first 18 cases:
  - **The old line,** `IsFileCatAppHost(process.MainModule?.FileName)`: 8 failed. Both FIFO cases hit the 10 s limit, waiting on the FIFO. The linked and planted files gave their own answer, in both directions. The `/dev/zero` link was read up to the 512 MiB limit, and gave unknown.
  - **The safe open of that name instead of `exe`:** 8 failed. Nothing waited, but the FIFO and the links gave unknown, and the planted regular files were read.
  - **Opening a name** without `O_NONBLOCK`: the FIFO case hit the 10 s limit. Without `O_NOFOLLOW`, the link was followed. Without the regular-file check, the FIFO was read as empty, "not FileCat". Each failed only its own case.
- **Ad hoc, not committed:** the census over the VM's whole process list, without the 2 processes named FileCat or dotnet. It went through 460 processes, 303 of them kernel threads, in 469 ms. The answer was unknown: 59 processes that the user may not inspect refused `exe` (errno 13), and 1 had no image (errno 2). As before the fix, an ordinary census is unknown there and asks for the root census.

**Runs on the Mac** (Apple M1, macOS 27.2.1, System Integrity Protection on, .NET SDK 10.0.101), as the ordinary user over ssh, from a fresh clone of the branch:
- **Before** (`fff712647`): 4 of the 19 cases failed. 3 were those that failed in CI. The fourth was the deleted-image case, whose unsigned copy of `sleep` macOS had killed. The whole-list case passed with 787 × `image error 22`.
- **After** (`bada5da1d`): 21 cases, 10 passed, 11 skipped (Linux only), none failed; the build has no warnings. The whole process list gave a definite census, not running, with no process unknown. In the deleted-image case Darwin named the FIFO, which the census refused without opening it: unknown, without waiting.
- **Mutations**, one line each:
  - `O_NOFOLLOW` back beside `O_NOFOLLOW_ANY`: 5 cases failed, the whole list among them (786 × `image error 22`).
  - Without `O_NOFOLLOW_ANY`: the folder link was followed, and its case failed.
  - Without the `lstat` type check: the FIFO, device and link cases failed on their reasons, and the FIFO-at-the-name case reported that the census had opened the FIFO.
- `RecoverySafetyTests`, `SingleInstanceMetadataTests` and `MacProcessCensusTests`: 73 cases, 61 passed, 12 skipped (Windows only), none failed.
- **Ad hoc, not committed:** the names Darwin gave every running process, the copies started through links, and the moved and deleted images (above). The open with `O_NOFOLLOW | O_NOFOLLOW_ANY` was refused (errno 22) for all 781 names, and also for `/bin`, `/private/tmp` and `/System/Library/CoreServices/SystemVersion.plist`.

**Not verified:**
- **A root census.** The VM grants no `sudo`, so `--filecat-recovery-process-census` did not run as root, through pkexec or otherwise. Root differs in which processes it may open, not in how; the tests read the user's own processes.
- **macOS after the correction in hosted CI,** and on any macOS but 27.2.1: not run. The CI's macos-26 ran the first version, with unsigned copies of `sleep`; the ad hoc signed copies ran only on the Mac.
- **macOS as root,** FileCat started with `sudo`: not run. Root opens the names the same way.
- **Images in folders that macOS privacy protection guards,** such as Downloads or a removable volume: FileCat can read them only with the user's consent, which macOS may ask for. Without it the open fails and that process is unknown, as before. Not tried; over ssh every image on the Mac could be read.
- **Windows** tests were not rerun here; hosted CI runs them.
- **Linux on ARM64.** Its `O_DIRECTORY` differs from x64's; the census takes it from `LinuxOpenFlags`, as other FileCat code does. Neither the VM nor hosted CI runs Linux on ARM64.
- **ID reuse itself.** The tests show that reads through a held `/proc/<pid>` fail once the process is gone. Making the kernel hand that ID to another process needs privileges that the VM does not grant.
- **Slow file systems.** An image on a stalled FUSE file system mounted with `allow_other`, or on a hard NFS mount, can still hold the census in a read, as before. That is outside this issue.
