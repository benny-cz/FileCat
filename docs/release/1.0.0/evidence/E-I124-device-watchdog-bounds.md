# E-I124 — watchdog replacements preserve the worker list and hard cap

I124/V12/AI-03, high availability/resource-bound defect. Preliminary Windows host, SDK 10.0.401.
Working source `08acc2f327a99f63e904e9195085d180f55e4447` plus `DeviceIoScheduler.cs` and the new
`DeviceIoSchedulerTests.cs`. The baseline scheduler source also matches `073e84d`; its captured Core DLL is
the unchanged production DLL retained with the I123 baseline.

Two isolated owned-process probes reproduce real defects in that production DLL, using synthetic noncooperative
callbacks, one base worker, a two-worker cap and a 20 ms hang threshold:

- The actual 500 ms watchdog timer quarantines the first held call and starts a queued replacement. `Watch` appends
  to `_workers` while enumerating it with `foreach`; the next enumeration step throws an unhandled
  `InvalidOperationException`, "Collection was modified". The owned probe terminates with **-532462766**, with the
  timer/Watch stack recorded. This is a process failure observed in the probe, not a claimed physical-device run.
- A separate controlled run disables only its scheduler's timer, invokes the same private Watch checkpoints and
  retains/catches the enumeration exception. Once both slots hold quarantined calls, enqueueing another request
  starts a third worker: **three concurrent calls despite a cap of two**. Its healthy independent device returns 42.
  All callbacks return after explicit release, zero active calls; the probe exits 2 for the observed bound violation.

The enqueue condition's unrestricted `ActiveWorkers() < ThreadsPerDevice` branch made its second, capped branch
redundant. Enqueue now requires both available capacity below the hard cap and fewer active base workers. Watch
scans the initial worker count by index under its existing lock, so appending replacements cannot invalidate its
iteration. Existing priorities, configured defaults, thresholds, quarantine behavior and cancellation semantics
retain their values; canceled tasks still do not abort noncooperative callbacks.

Identical probe source with the corrected Core DLL passes both modes, exit 0, no stderr. The actual timer starts
its replacement without stopping the process; the cap control observes two active calls, zero third calls before
or after release, zero Watch exceptions and healthy result 42. All recorded probe processes and executable paths
below their owned root are absent after execution.

Two Core regression cases exercise the real timer/replacement/health recovery and controlled quarantine/cap
checkpoints. At the cap, live demand stays queued, canceled demand never executes, an independent device continues,
and returning calls permit the live request exactly once while peak concurrency stays two. All **48 affected Core
and 37 affected App cases pass**, zero skips. Full Core passes **747/793 with 46 declared skips**; full App passes
**271/292 with 21 declared skips**. The App controls include actual-file decoder feed/lifetime, quick view,
comparison/synchronization and operation routes.

Independent verification checks baseline failure/stack/cap observations, corrected process exits/traces/cleanup,
identical probe source, the actual Core DLL in each probe, **1,084 captured test inputs** and nineteen raw sources
per stage. Unchanged source matches canonical Git; active test assemblies and shared corrected Core DLL bytes,
exact direct XML inventories and every skip reason verify.

Private root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/i124-device-watchdog-bounds-20261004`.

| Evidence | SHA-256 |
|---|---|
| Original scheduler source / Core DLL | `2ca795df48bcb1e052c5c0c1640ff6b9d84f0beccd6b0a9f95f3dc5018bfd030` / `83538102380e4402a9063316850c93e89c4b4728e97926e2036107744340ad69` |
| Baseline / corrected process and input-pin records | `dd328d508e93e743ab027b2e1e2533329d7728a227f68312ae78ed6d8c4bee31` / `714ca5d5824c6bbc756a8957ac375284d69f0a08d6ee65cd30eae850640e744a` |
| Full Core / App XML | `19e38418ed5cc566fe1dd773ae89d78b965b3186a73d241fce5cde06d493e21b` / `c9db6eb0c70b03d289557fdb5d8a1b007fa4602b092f4ead7283e3d0fd2adadc` |
| Core / App input manifests | `3d12c7f971ae56d24cc943127e1fb510c1dbbad944af55f3b85795eeb6c39d88` / `90e619fa984f706593de6bbd5f8980d51395272dc8f97af3e541239959012669` |
| Independent baseline/probe/source/input/case inventory | `a0aaf5527e98502687619d1d0ef7cf792b851608105f7d9f186215e4a5e701a2` |

Clean-source CI and Windows guest checks are next. Synthetic callback controls qualify the observed scheduler
branches, not actual hung hardware, aggregate decoder memory/process limits, native queue/frame/AT performance or
an exact release candidate. The shared scheduler change requires fresh affected consumer validation. Physical USB
qualification remains held at its historical source-change gate; no USB action occurred. Overall **NO-GO** remains.
