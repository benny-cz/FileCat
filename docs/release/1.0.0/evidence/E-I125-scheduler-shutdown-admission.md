# E-I125 — shutdown cancels waiting admission to existing and new device queues

I125/V12/AI-03, medium resource-lifetime/availability defect. Preliminary Windows host, SDK 10.0.401.
Original source/DLL: clean `18006cc9746a80e3c1b88ec40b89fb5f38dbe8cc`. Working source:
`924d6d8093351de7844a8b923a47f7cce11dbce0` plus `DeviceIoScheduler.cs` and `DeviceIoSchedulerTests.cs`.

Two owned-process probes use the actual original production Core DLL and controlled synthetic callbacks.
Both warm a scheduler, hold its admission lock on the disposing thread, let a public `Run` caller pass the initial
disposed check and block, dispose the scheduler reentrantly, then release admission:

- With an existing device queue, the returned task stays **WaitingForActivation**, incomplete after the observation
  interval; the queue has shut down and no worker can take the late item. No callback runs. The caller thread exits,
  and a separate request started after disposal cancels correctly. The probe exits 2.
- With a new device key, only that owned dictionary's insertion locks are held. Disposal snapshots the existing
  queues before insertion resumes. The new queue then starts its callback **after disposal**: one callback, a
  successfully completed task rather than cancellation. The separate after-disposal request cancels; the probe exits 2.

The initial owner check did not protect either admission interval. Enqueue now checks both the queue's shutdown
flag and the owner's disposed flag under its existing queue lock, canceling the item before queueing or starting
a worker. The owner check also covers a newly inserted queue missed by disposal's snapshot. Running callbacks retain
their existing safe-boundary behavior; this remedy concerns waiting admission.

Identical probe sources with the corrected Core DLL each exit 0, no stderr: the late and after-disposal tasks are
canceled, zero callbacks run, and both caller threads exit. Two regression cases reproduce the same existing/new
queue interleavings through public `Run`, with bounded waits and owned-thread cleanup. Their private reflection
controls only the owned scheduler's timer/locks and the owned .NET dictionary's insertion locks.

All **50 affected Core and 37 headless App cases pass**, zero skips. Full Core passes **749/795 with 46 declared
skips**; full App passes **271/292 with 21 declared skips**. Consumer controls cover metadata, page/cache lifetimes,
content/hex operations, actual-file decoder feeds, quick view, comparison/synchronization and operation routes.
Independent verification checks the baseline pins/canonical source, exact failure and correction observations,
identical probe sources, actual loaded-copy Core DLL bytes, **1,084 captured test inputs and nineteen sources per
stage**, active test assemblies, shared corrected Core DLL, complete direct XML inventories and every skip reason.
Recorded process IDs and executable paths below the owned root are absent after execution.

Private root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/i125-scheduler-shutdown-race-20261004`.

| Evidence | SHA-256 |
|---|---|
| Baseline scheduler source / Core DLL | `ee54e0379d4f67e084a4b3c0c93b90f3c3477a3807b852bfa63f115ece7aa356` / `240fd30aa277000f888784fe91a22be9aaf3b4b5ac374749589216ddd4b6b43a` |
| Existing / new queue baseline exit records | `8f906852839f7b11d70a392bc1a66efd4d6883f8e86e436980d63e1745f56ca8` / `16747e6092f65b3037c762e448bacb8f031d9a453e59efc11115be761b4b59e8` |
| Existing / new queue corrected exit records | `c61476e772890829bd359030e4329461ee7ad750fd76f23ac7ac97e350e4c24a` / `6d99cb2ae9024254b2b8da63e00e2efe483b23a8afd7c8e1529082a0bf0a8add` |
| Full Core / App XML | `9a11c70493e3b87ec9e3ce295466d27d995e570875601379a3e1e70801d37c81` / `2b29958f9394622e9b474d6c15debdb9a8c7d5aad7126af946526c675a1146e6` |
| Core / App input manifests | `c97a7c27f240b0daba956ddacafad51f8c90c2cd7adb71a6211b14705f479860` / `5e6c2393a338d8d6be47f319bb57cc663cef8f0db6f66000340f8b30b0b4177f` |
| Owned process cleanup / independent inventory | `095c88b7f5d0901c9d82e2d2eb004bb39326ac4089e69afbbdb4b4275c48740b` / `c6400f1ded3ffe55e3837a9ee35b227648e7f414a47443e3cdc1168abf8be5e9` |

Clean-source CI and SDK-free Windows guest validation are next. These controlled races do not qualify physical
hung devices, wider cancellation/resource lifetimes, aggregate decoder limits, native queue/frame/AT performance
or an exact release candidate. No physical USB action occurred; its historical source-change gate remains held.
Overall **NO-GO** remains.
