# macOS App Sandbox worker spike

12 November 2026, for the [route decision](RELEASE_ROUTE_DECISION.md): can FileCat's .NET archive worker run inside the
App Sandbox, and what does that boundary deny? This is a spike with a probe program, not the production worker; it
qualifies nothing. Tooling: [probe and driver](../../../eng/validation/archive-writing/macos-sandbox-worker/).

Machine: macOS 27.0.1 (26A434), M1, ordinary user (UID 501). Runtime .NET 10.0.1, self-contained osx-arm64. Launchers
built with the Command Line Tools' Apple clang 21.0.0 and the MacOSX27.0 SDK, `-std=c11 -O2 -Wall -Wextra -Werror`. All
signatures ad hoc; the only entitlement is `com.apple.security.app-sandbox`.

## What was tried

1. **A copy of the .NET apphost signed with the entitlement.** It dies with `SIGTRAP` before `main`:
   `libsystem_secinit` reports that the signature's Info.plist has no bundle identifier, so the sandbox has no container
   to name. The apphost cannot carry an embedded Info.plist.
2. **A small C launcher** with an embedded Info.plist (`-sectcreate __TEXT __info_plist`) and the entitlement. The system
   applies the sandbox before the launcher's `main`. The launcher then `execve`s the bundle's own .NET apphost, and the
   process stays sandboxed across the exec. Variant: the launcher sets its bootstrap port to `MACH_PORT_NULL` before
   the exec.

## Results

| Check | Unsandboxed control | Sandboxed (launcher) | Sandboxed, no bootstrap port |
|---|---|---|---|
| .NET runtime, own assets, SHA-256, Deflate, local time zone | Work | Work | Work |
| `HOME`, `TMPDIR`, `APP_SANDBOX_CONTAINER_ID` | User's | The container's | The container's |
| Read a file beside the bundle; list home and Documents | Allowed | Refused (`EPERM`) | Refused |
| Write beside the bundle, write `/tmp` | Allowed | Refused | Refused |
| Write in the container's `tmp` | — | Allowed | Allowed |
| Loopback TCP connect and listen, UDP send | Allowed | Refused ("Permission denied") | Refused |
| Resolve `example.com` / `localhost` | Both resolve | Refused / resolves (hosts file) | Refused / resolves |
| Start `/usr/bin/true` (posix_spawn, Process.Start) | Allowed | **Allowed** | Not run unlimited |
| The same with `RLIMIT_NPROC` 0 | — | Refused (`EAGAIN`) | Refused (`EAGAIN`) |
| Inherited pipe descriptor (1 MiB); stdin to stdout (8 MiB, SHA-256 compared) | — | Works | Works (1 MiB) |
| Mach service lookups (26 probed) | 24 reachable | **21 reachable**, including the pasteboard, Apple Events, WindowServer, Dock, TCC, notification centers, preferences, SecurityServer, Spotlight metadata, file coordination; refused: LaunchServices daemon, DNS-SD, mDNSResponder, nsurlsessiond, networkd | **None** (`MACH_SEND_INVALID_DEST`) |
| Pasteboard (item count) | Readable | **Readable** | Unavailable |

Parent exit: inside the sandbox, `kqueue` `EVFILT_PROC`/`NOTE_EXIT` on the parent registers. The worker stays alive while
its parent lives and exits **13 ms** after the parent is killed with `SIGKILL`.

Footprint monitor: the parent samples `proc_pid_rusage` (`ri_phys_footprint`) of the sandboxed worker, which touches
native memory at about 800 MiB/s, and kills it above a 256 MiB budget.

| Sampling | Killed at footprint | Over budget by |
|---|---|---|
| 10 ms | 267.8 MiB | 11.8 MiB |
| 50 ms | 270.6 MiB | 14.6 MiB |
| 10 ms, 128 MiB workload | Not killed (peak 155.8 MiB) | — |

Start-up: a sandboxed run that exits at once takes about 42 ms against 38 ms unsandboxed (74 ms on the first run, which
creates the container). A launcher with different bytes under the same identifier reused the existing container without
refusal or prompt. The launcher works from a flat folder as well as from an app bundle. Its own folder (or bundle) is
readable, and a sibling folder is not. Removing the containers afterwards needed no permission.

## What follows for the production worker

- The worker roles run in the app's own executable, started through a C launcher that carries the App Sandbox
  entitlement and an embedded Info.plist. No second copy of the runtime is shipped.
- The App Sandbox alone does not stop process creation, the pasteboard or about twenty system services. The launcher
  therefore sets `RLIMIT_NPROC` to 0, `RLIMIT_CORE` to 0 and the CPU limit. It closes every inherited descriptor except
  the protocol pipes and the cancellation pipe, and gives up its bootstrap port. Only then does it execute the worker.
- Before it reads input, the worker checks these facts itself: container, limits, null bootstrap port and descriptors.
  It also checks that its home folder is refused and that a loopback connection is refused. It refuses to run if any
  check fails, so a build that lost the entitlement fails closed.
- The worker watches its parent with `kqueue` and exits when the parent does. The cancellation pipe stays as on other
  platforms.
- The parent samples the footprint and kills the worker above its budget, as the owner's re-scoped gate allows. The
  overshoot measured here is about 12 MiB at 10 ms sampling.

## Not established here

The production launcher, worker and monitor that followed are recorded in [MACOS_SANDBOX_WORKER.md](MACOS_SANDBOX_WORKER.md).

The production launcher and worker, archive roles under this boundary, the CPU limit's effect, packaging inside
`FileCat.app` (whose deep signing must not strip the launcher's entitlement), and the owner-signed release candidate
(Developer ID and hardened runtime). Evidence: `results.json` and the driver log in the ignored
`.tmp/test-results/archive-writing/macos-sandbox-spike-v5`; launcher SHA-256 `39d951a0…283a7`, bootstrap-free launcher
`5097699b…32c0`.
