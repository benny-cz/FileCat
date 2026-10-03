# E-V09-G8 — native capture timing execution

Links: V09/REC-002/TV-09, I09, I106, ENV-07. **Completed preliminary instrumentation; physical source qualification remains failed.**
The owner authorizes the agent to launch the [prepared G7 diagnostic](E-V09-G7-trace-timing-preparation.md) itself.
The tool subprocess still reports a standard token, but a Windows RunAs request succeeds and the runner independently
checks an administrator token. This clears the immediate owner-launch gate; no manual repeat is requested.

Private campaign:
`artifacts/release-evidence/v09-trace-timing-20261003/timing-d6804bf8dd3647ff81b9d68ff2e7daa6`,
controller PID 51016, started 2026-10-03 16:23:50 UTC on Windows Insider 26220. Runner/child/tracer hashes match G7;
its standalone preparation source remains exact `43e520de3773aabf27519dd45c861098bf314167`.
Original TEMP/TMP are C:\\WINDOWS\\Temp. The diagnostic requests no USB I/O or FileCat launch.
The initial three-case snapshot independently inventories **25 files each**, native PML and complete CSV
streamed independently, matching event counts and exact successful 4,096-byte marker intervals. D/E/F were still
running when that snapshot was emitted. Their subsequent final results are added below.

| Case | Independent outcome | PML/CSV event count | Native UTC range |
|---|---|---|---|
| A, 60 seconds, original temp, E:\\FileCat working directory | FAIL: timed marker/child controls absent | 108,525 | 16:23:51.095763–16:23:52.083275 |
| B, 180 seconds, owned temp/working directory | PASS: each parent/child writes once and reads twice, child start/exit present, untouched path absent | 1,012,856 | 16:24:34.184800–16:24:48.931112 |
| C, 60 seconds, owned temp/working directory | PASS: same exact controls/lifetime | 996,149 | 16:29:56.529670–16:30:11.283424 |

A reproduces an incomplete native trace without any physical-test access. B shows the G6 capture settings can
also sustain recording, and C passes with the 60-second runtime. Therefore runtime length alone is not an adequate
explanation. The capture failure's cause remains undetermined; further comparisons are needed before attributing it
to temporary directory, working directory, readiness or another condition. Capture-process lifetime/backing-file
existence remain insufficient proof. The original runner reports configuration restored after each of these cases;
independent final registry/worker checks await campaign completion.

The initial independent verifier assumes a native event timestamp must be greater than or equal to the runner's
wall-clock checkpoint. B's final read is 91 microseconds earlier on those two clocks, although both native and CSV
exact marker counts/lifetime already agree. That check stops before emitting an inventory; original verifier bytes
and failure are retained. The revised verifier proves ordering and continued recording within the native event
clock and reports cross-clock offsets explicitly. B/C native marker spans are 10.868251/10.699591 seconds; final
parent reads follow child exit by 7.074042/7.053374 seconds. This changes the comparison method, not the observed
read/write counts, byte intervals, process identity or coverage requirements.

The original PowerShell runner imports the full CSV before filtering, so analysing million-event captures takes
several minutes per case. The live pinned runner remains unchanged. Independent verification streams the native
PML/CSV; no timing benchmark for FileCat is inferred from instrumentation analysis time.

[CI 37136382740](https://github.com/benny-cz/FileCat/actions/runs/37136382740) at exact
`84615d9130e85bf197b96e3c8bbf1fe182bf94ee` passes all four lanes; three package jobs skip. Full run/job metadata
and complete logs are retained. No direct artifact inventory is inferred.

| Private evidence | SHA-256 |
|---|---|
| Elevation request record | `2a6798b5f97b7954067523533afbb9dea6770a97dbdc2391fcaaefa342a0bff5` |
| Independent partial inventory, A/B/C, 75 files | `29609a8351b74776949386b4dfeb724db4cb8a0644f8cc5cda3ca9b6d04fa93f` |
| A PML / full CSV | `b9fa7ce57e8126367bf037ebfca891856fb8c65a1fe891b28722e693745f271f` / `736da86dcdbe9a2b90750a256fbd445fbb3f7b77e41a926904c206a819d1922b` |
| B PML / full CSV | `e05435948fe7fe166f7c963cd3ee96da33bbe39a629a32f5def566ced45e1d9c` / `167235b32a1dee93532f46837dce6c75aa388f83ba244518cfa109ca730af473` |
| C PML / full CSV | `248cb105acfdad7a1eb3fef5b32ba051094afeeaec4d52a1a3e68292fde1632f` / `6727d5dddc37d13672c682b613889ea1cc82efcd98b815621d7512f3f6c92506` |
| Initial verifier failure record | `82f73ecd9895a93c158bafcddff32093d8debc630a75a8675e7bac8c1165d1a0` |
| 84615d9 CI run / jobs / complete log | `19724a1f7574a15e26c59921adaa82c1e2770794cbf9cd887bcbd6ccb502d813` / `91ea51225a8bb8fc66f0204d4575c1f109f0cb81d054e27e24b234ec4915d974` / `f4277ac563e178bd8ced2e10b013d7440f369373befabac19aa7e72b8cb034bb` |

## Completed matrix and independent restoration

All six cases subsequently finish. The final independent inventory checks **154 case files plus four campaign
files**, including every complete PML/CSV and recorded output hash. B/C/D pass exact parent/child byte controls,
lifetime, sustained native-clock ordering and untouched-path control; A/E/F fail those positive controls.
The failed cases retain only about one second, ending before their markers/child. No zero-event pass is credited.

| Additional completed case | Independent outcome | PML/CSV events | Native UTC range |
|---|---|---|---|
| D, original working directory, owned temp | PASS | 979,383 | 16:37:14.774746–16:37:29.298885 |
| E, owned working directory, original temp | FAIL | 100,680 | 16:44:49.714750–16:44:50.690966 |
| F, owned working/temp directories, separate WaitForIdle command | FAIL | 121,105 | 16:45:51.538419–16:45:52.545873 |

These controlled results associate the original system-temp setting with failure in A/E, while owned-temp B/C/D
pass. F also fails with owned temp and a separate readiness call. They do not explain every failure: G6 used owned
temp and still failed, and none establishes that a particular option is a durable remedy. Do not infer a driver,
other application or user action as the cause. The separate WaitForIdle call is not a qualified correction.

Final independent checks confirm all thirty original registry values/types/data and flat structure restored, with
no recorded worker/controller or tracer remaining. Local built-in WPR profiles enumerate DiskIO/FileIO and its
status reports no active WPR recording. Investigate this alternate recorder using off-source positive/negative
controls and loss statistics before any raw-device/source-write conclusion. No WPR recording is claimed here.

| Final private evidence | SHA-256 |
|---|---|
| Complete independent six-case/campaign inventory | `792ec29b8de1d5cb018aece87dd5231f598fe3e39f3d28d04be72d6601176d85` |
| Independent final registry/worker restoration | `ba344a83d937f9e82358a738b6c869b709a4f9b56c0fd81f3b6edc37d0d4b755` |

The [G6 full source-hash difference](E-V09-G6-incomplete-usb-source-trace.md) remains unexplained. No raw-device
visibility control, event-loss proof, installed helper, GUI admission, zero-source-write or candidate pass is supplied
by these off-source controls. Further USB tests remain held; I09/I106 remain open and the recommendation is NO-GO.
