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

Clean source `749f55f52154d1f76449658a83861e1ec159041d` passes all four required lanes in
[CI 37198032750](https://github.com/benny-cz/FileCat/actions/runs/37198032750); three package jobs skip.
Direct Windows XML records Core **748 pass/47 skips**, App **277/15**, Windows platform **166/33** and Remote
**88/28**. The live NTFS history case passes. Linux and macOS App each pass **256/36**. All **87 affected Windows
Core/App cases** and **37 affected App cases per Unix lane** pass without affected skips. The ARM64 lane succeeds
without a retained direct XML artifact. Independent verification checks three server artifact digests/ZIP sizes,
extracted XML bytes, six complete case/skip inventories and selected affected names.

The same clean source is published self-contained for the SDK-free Windows Insider 26300 guest, VM UUID
`9D224D56-1161-A849-ABA7-2581A980895C`. Its owned root is
`C:/Users/Public/FileCat-shutdownadmission-validation-97aa0599da4f4338b14bfe8060295fd3`.
All **50 Core and 37 headless App controls pass**, zero skips, ending at **11:18:46.0461221 UTC** on 2026-10-04.
Independent verification checks **691 payload files, 692 ZIP members and nineteen canonical sources** against
pre-launch pins and exact direct XML names. At **11:20:25.8315581 UTC**, controller PID 3472 and worker PIDs
13388/12780 are absent, no executable children remain below the owned root, and its temporary folder is empty.
The two admission races, preceding actual watchdog/cap controls and all selected consumers pass in the guest.
These are owned synthetic/headless controls, without native desktop input or physical source-device access.

Private clean payload/guest root: `clean-749f55f` below the working evidence root above. Private CI root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/ci-37198032750`.

| Clean evidence | SHA-256 |
|---|---|
| CI metadata / complete log | `c57de52092abed937579ac5e1b1f8e76202b094190773b4cbfcbe035e6aabee0` / `5d155a21b9117b59402a4b4a278b3fa167082819221d64ce7a322a6b9506ba9c` |
| Windows artifact 11301795847, 378331 bytes | `736749867d5d53ff97e30508a903c9f75052272a0a1691bfdd3e3c1981862674` |
| Linux App artifact 11301539543, 81553 bytes | `07c7b5cf79a08c19d01b497106cf578251f0e7993108984e6ef4a92c97b76639` |
| macOS App artifact 11302120591, 82179 bytes | `dda48be388bd2f4d4240834eb668085305aadf4d9d22d0f4728d405b612b2ff0` |
| Independent CI inventory | `f44a534c4644192c19caadfbd35bae4398b648158c78615a24b481d1b3c2f0ca` |
| Guest ZIP / payload manifest | `5bb4b0e37815b73c1e9844311012505784672cb3e864ed0c4df7c25822e139ce` / `e04d54fcf21320931d0754ca8217dfd9e1abd03bc150df95c287f06344b14338` |
| Guest runner / cleanup script | `a1cb8155a8628419dee39dfa5a4345531792fbb47c8e246a16ffc4763ee4b988` / `6adc4a119f19ae6b5a24f7752cb03f2764d8ae4bf73102a19da09fa606e86836` |
| Guest Core / App XML | `86fb38ef465f70e8eb63c9d6c116955b3ad3c570b8bde3797962eac4a79033a9` / `56c8e2028cc5c5d51c139920fd7ac2d2af0a3858a43faa00d9f9c0cd6185f3bc` |
| Guest cleanup / independent inventory | `85bbd16ed130a4d0ca19cf1ae6ed4e44e93f31bb787d9d8923ee28ab6ef19d89` / `2fee8dd10b8355016182b16326653da27eb4d7043069285058c73d6e499a13b1` |

Physical hung devices, wider cancellation/resource lifetimes, aggregate decoder limits, native queue/frame/AT
performance and an exact release candidate remain unqualified. No physical USB action occurred; its historical
source-change gate remains held. Overall **NO-GO** remains.
