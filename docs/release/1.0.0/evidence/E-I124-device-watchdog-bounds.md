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

Clean source `18006cc9746a80e3c1b88ec40b89fb5f38dbe8cc` passes all four required lanes in
[CI 37195976222](https://github.com/benny-cz/FileCat/actions/runs/37195976222); three package jobs skip.
Direct Windows XML records Core **746 pass/47 skips**, App **277/15**, Windows platform **166/33** and Remote
**88/28**. The live NTFS history case passes. Linux and macOS App each pass **256/36**. All **85 affected Windows
Core/App cases** and **37 affected App cases per Unix lane** pass without affected skips. The ARM64 lane succeeds,
but has no retained direct XML artifact. Independent verification checks all three server artifact digests and
ZIP sizes, extracted XML bytes, six complete case/skip inventories and selected affected names.

The same clean source is published self-contained for the SDK-free Windows Insider 26300 guest, VM UUID
`9D224D56-1161-A849-ABA7-2581A980895C`. Its owned root is
`C:/Users/Public/FileCat-workerbound-validation-b0f47da9a7384ab5a90ea6006ea28115`.
All **48 Core and 37 headless App controls pass**, zero skips, ending at **10:48:15.6149910 UTC** on 2026-10-04.
Independent verification checks **691 payload files, 692 ZIP members and nineteen canonical sources** against the
pre-launch pins and exact direct XML names. At **10:50:02.4059026 UTC**, controller PID 9220 and worker PIDs
12296/13848 are absent, no executable children remain below the owned root, and its temporary folder is empty.
The App consumer checks include actual-file picture feeds/lifetimes, quick view, comparison/synchronization and
operation routes. These are headless consumer controls, without native desktop input or physical source-device access.

Private clean payload/guest root: `clean-18006cc` below the working evidence root above. Private CI root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/ci-37195976222`.

| Clean evidence | SHA-256 |
|---|---|
| CI metadata / complete log | `d878b2995528253bb39ec967a94c83e2a6895125ade1bfea37c7e1fed6460d39` / `a52db2b28586349e97c12ac18b09750522a93591ff75f8a93aa3140f86d22860` |
| Windows artifact 11301097460, 378181 bytes | `cc8a30d100352b930b5c2c0d5bf127c063365ff8650fc2024c6b6bdf4e02caea` |
| Linux App artifact 11301441418, 81333 bytes | `5547e33de4de3badb46ec207196b5a66ce3dc0b11f913f20666a9d7a1134c775` |
| macOS App artifact 11301301945, 82098 bytes | `c2f9aee8436c6fc94232b28431a51624d40fe490c00f0c858f30ed5ee232c43d` |
| Independent CI inventory | `332130db2973055f47eceb324960baa71e0498a79bcca17dc173fa21d0e1b935` |
| Guest ZIP / payload manifest | `55410b5b6977bc4896be0dfdaea18b5897fa42b84df989f8cbd52dddd715cbaa` / `e6ff0a40dc702bf2807b5f2c2e502baa02d2f46972cb14ad6fbc46b0fa8bfe70` |
| Guest runner / cleanup script | `09f76295b6da7e55d6e26f8b7fe8c067675493f7cb6456c8ad5b3c42b5bf5248` / `4a940e18884b10479332ed129665f3fd0d1d8c040f1bab39a3454e4e25251365` |
| Guest Core / App XML | `63ec8f621b052a51c336845f267c2203dad7eee75b0fa36f6c12005df3fc9030` / `5a6e709c8108579012d5c645ab10ba9e1ef5e217232bb7801ad922eb2fea596c` |
| Guest cleanup / independent inventory | `160b07a47c3fe826a66d850c1752746c88fc467cd41607c862b80ea0ba025c99` / `945a792bc215f00bb50d4395f577408d904c7dad0cea4c9ac028ff414ee45f2c` |

Synthetic callback controls qualify the observed scheduler branches. Actual hung hardware, aggregate decoder
memory/process limits, wider shutdown/queue lifetimes, native queue/frame/AT performance and an exact release
candidate remain unqualified. Physical USB qualification remains held at its historical source-change gate;
no USB action occurred. Overall **NO-GO** remains.
