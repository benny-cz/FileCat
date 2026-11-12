# macOS archive workers: App Sandbox boundary and footprint monitor

12 November 2026, commits `a9ff8258` to `92a2a2c3`. This implements the macOS boundary of the
[route decision](RELEASE_ROUTE_DECISION.md), following the [spike](MACOS_SANDBOX_WORKER_SPIKE.md). It lists nothing:
`ArchiveWriteRoutes.Qualified` stays empty, so ordinary startup composes no macOS archive writer.

## Design

**Launcher.** `FileCatArchiveWorker` is a small C program (`src/FileCat.App/MacOS`) with an embedded Info.plist
(`io.github.benny-cz.filecat.archive-worker`). Its only entitlement is `com.apple.security.app-sandbox`. MSBuild compiles
it and signs it ad hoc on a Mac building for macOS. The output is deterministic: two publishes produced the same SHA-256.
`eng/package-macos.sh` signs it again after the bundle's deep signature and checks that it alone carries the entitlement.
Before it executes anything, the launcher:

- accepts only the fixed launch facts (mode, worker image, nonce, parent, CPU seconds, request, cancellation descriptor);
- refuses unless the system placed it in its container, so a build that lost the entitlement runs nothing;
- checks its parent, and that its standard streams and the cancellation descriptor are pipes, the latter read-only;
- closes every other inherited descriptor and checks that exactly four remain;
- sets `RLIMIT_NPROC` 0 (the sandbox itself allows new processes), `RLIMIT_CORE` 0 and the CPU allowance;
- checks that the worker image is a regular file beside it, in a folder no other user can change;
- gives up its bootstrap port, so the worker can look up no system service;
- executes the application in worker mode. The image keeps the sandbox, the limits and the missing port across exec.

**Worker.** `FileCat --archive-mac-worker` (`MacArchiveIsolation`) checks every one of those facts again before its role
reads a byte: the container, the loader environment, the three limits, the null bootstrap port, and the descriptors. It
also checks that the sandbox refuses its user's home folder and a loopback bind. Then it watches its parent with `kqueue`
(`NOTE_EXIT`) and exits as soon as the parent does. Any failed check ends it with exit 65 before input.

**Parent.** `MacArchiveProcess` refuses to start a worker from a root process. It starts the launcher with a fixed
environment: diagnostics off, invariant globalization, a 384 MiB managed heap limit, UTC, the C locale and no `PATH`. It
owns the cancellation pipe and caps standard error at 64 KiB. Every 5 ms it samples the worker's physical footprint
(`proc_pid_rusage`) and stops the worker above 768 MiB, the Linux worker's memory limit. The job then fails with the
memory-budget reason. This is the owner's re-scoped memory gate: a sampled bound, not a ceiling.

## Evidence

The Mac is a macOS 27.0.1 M1 machine, run as the ordinary user (UID 501). Run `mac-sbx-v5` at `92a2a2c3` passed 14
boundary tests and 10 real-dialog flows. The boundary tests use the published worker probe with the same launcher.

| Check | Result |
|---|---|
| Read a file in the user's home folder, list its folder, write there, change its time; write `/tmp` | All refused |
| Loopback TCP connect and bind, UDP send, resolve `example.com` | All refused; no connection reached the test's listener |
| `posix_spawn` and `Process.Start` | `EAGAIN` |
| Mach service lookups (22 probed) | None reachable |
| Container | `io.github.benny-cz.filecat.archive-worker` |
| An inheritable descriptor (200) left open by the parent | Closed before the worker ran |
| Footprint monitor, 256 MiB budget, a probe filling memory at full speed | Stopped at 292 MiB (5 ms sampling); 379 MiB in run v3 at 10 ms |
| CPU allowance of 2 s, a spinning probe | `SIGXCPU` at 2.0 s of CPU (exit 152) |
| Standard error flood | Stopped at the 64 KiB cap |
| Cancellation (assess, verify, transform, output flood) | Reaped in 6 to 9 ms. A worker that ignores cancellation is stopped at the 2 s grace (2,013.6 ms, exit 137) |
| `SIGKILL` of the parent, worker ignoring pipe EOF | Worker gone 11 ms later |
| Launcher re-signed without its entitlement | Refuses: not inside the App Sandbox (exit 65) |
| Launcher given another parent, or a malformed nonce | Refuses (exit 65) |
| Worker image started without the launcher, with or without a faked container variable | Refuses before input (exit 65) |
| Real TAR assessment in the sandboxed worker | Passes |
| Real Pack and Add dialogs, every native profile (10 rows), real FileCat workers | 10 of 10 |

These runs found and fixed four problems. codesign refuses XML comments and CRLF line ends in entitlements, so the
launcher files are pinned to LF. Disposing a worker that had already exited deadlocked against the monitor (`92a2a2c3`).
Two test defects were fixed: `fcntl` is variadic, so on Apple arm64 a fixed signature cannot pass `F_DUPFD`'s argument,
and one assertion message waited for a live process's error stream.

## Limits

- The memory bound is sampled. A worker can exceed its budget by what it allocates between two samples: 36 MiB at 5 ms
  for the full-speed probe above. Managed allocations reach the runtime's 384 MiB heap limit first.
- The worker can still read system libraries and the application bundle, as any sandboxed app can. It cannot read the
  user's data.
- The system creates the container `~/Library/Containers/io.github.benny-cz.filecat.archive-worker` on first use. The
  worker writes nothing there (it is stream-only), but the folder stays; the uninstall notes must mention it.
- These are ad hoc signed development builds. Developer ID signing, the hardened runtime and notarization are the
  owner's, and the denial runs must be repeated on the owner-signed release candidate.
- The packaged app's route test (the bundle `eng/package-macos.sh` builds) has not run yet.

Evidence: `mac-sbx-v3`, `-v4` and `-v5` evidence archives in the ignored
`.tmp/test-results/archive-writing/mac-sbx`. Published SHA-256: `FileCatArchiveWorker` `c34f4243…2317`, `FileCat`
`833b7cc6…0339`, `FileCat.Core.dll` `bd38b2d5…4abb`.
