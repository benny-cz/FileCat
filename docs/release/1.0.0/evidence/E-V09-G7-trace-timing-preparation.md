# E-V09-G7 — off-source capture timing preparation

Links: V09/REC-002/TV-09, I09, I106, ENV-07. **Preparation record; subsequent execution is [E-V09-G8](E-V09-G8-trace-timing-run.md).**
[G6](E-V09-G6-incomplete-usb-source-trace.md) retains the differing full physical hashes and incomplete trace.
Further USB tests remain held. This standalone diagnostic requests no USB I/O, fixture creation, format or FileCat
launch. It uses marked evidence/temporary directories on E: and the existing accepted host tracer.

The concrete private launcher is
`artifacts/release-evidence/v09-trace-timing-20261003/LaunchCaptureTiming.cmd`.
It requires an elevated host shell; the agent tool process is unelevated. The launcher pins its runner, which verifies
its child and installed Procmon 3.95 hashes, existing accepted license/flat configuration, absence of other tracer
processes and a non-waiting timing-campaign mutex. It clears physical-test opt-ins in its own child environment.
Original temporary directories must resolve to C: or E:. Every case requires at least 8 GiB free on E:.

Each case records early and late exact 4,096-byte read/write marker controls and a separate PowerShell child's
start/exit and marker reads/writes. The child reads again after two seconds; the controller reads its marker again
after the child exits and a further seven seconds. An untouched path must have no events. Exact expected event counts,
successful byte intervals and child lifetime must appear in the full CSV before TraceControlsQualified can be true.
Full native PML/CSV, expected/observed controls, process records, outcomes and hashes are retained, including failures.
Subsequent independent PML/CSV analysis is still mandatory.

| Case | Runtime bound | Working directory | Temporary directory | Additional readiness command |
|---|---|---|---|---|
| A, earlier-control style | 60 seconds | `E:\FileCat` | original C:/E: directories | none |
| B, failed USB capture settings | 180 seconds | owned case | owned case | none |
| C, runtime change from B | 60 seconds | owned case | owned case | none |
| D, working-directory change from B | 180 seconds | `E:\FileCat` | owned case | none |
| E, temporary-directory change from B | 180 seconds | owned case | original C:/E: directories | none |
| F, readiness change from B | 180 seconds | owned case | owned case | bounded /WaitForIdle |

These are instrumentation comparisons, not a reproduction of the complete USB hash/test sequence. The earlier
G4 capture did not record its inherited working directory; A uses an explicit `E:\FileCat` directory and is not
claimed to reproduce that unrecorded value. The bundled manual documents WaitForIdle as readiness, not proof of
sustained capture. Each case has bounded startup, child, stop/export waits, a 2 GiB PML stop check and its runtime
bound. Only the owned tracer can be stopped; another tracer causes refusal of global termination. Original thirty
configuration values/types/data are restored and checked between cases. Restoration failure stops the matrix.
Timed-control failures are retained separately so unaffected off-source comparisons can continue.

Preparation at documentation source `43e520de3773aabf27519dd45c861098bf314167` passes actual Windows PowerShell 5.1
parser and pinned-input checks. A marked child control produces the exact 4,096-byte SHA-256 and exits zero; an
unmarked directory is refused with exit one before marker creation. The exact cmd.exe invocation with
ValidatePreparationOnly exits zero using process-local Windows PowerShell module precedence. Independent verification
checks the five-line launcher/control equivalence, all **19** preparation/control files and both child outcomes.
No tracer or physical test is launched during these preparation controls. Timing, continued capture, live restoration
and source-write qualification are not inferred from preparation.

Independent scanning of **every** G6 PML event and CSV row also confirms its entire event range, rather than relying
only on first/last record order: UTC 15:13:28 has 1,004 events, 15:13:29 has 103,181 and 15:13:30 has 564. Minimum
and maximum UTC are 15:13:28.991290 and 15:13:30.004562; no later test/control events exist.

[CI 37135369558](https://github.com/benny-cz/FileCat/actions/runs/37135369558) at exact 43e520d passes all four lanes;
three package jobs skip. Complete metadata/logs are retained; no direct artifact inventory is inferred.

| Private preparation/diagnostic evidence | SHA-256 |
|---|---|
| Runner / child | `82ca5b73162f4d11df3ebc54b2fcfbe206975fb76d73ebba3a48028f50f6eea1` / `7200d1d6e4b07d64b903fd65191184d2faddd1f9c8f5fb58369c768d428435aa` |
| Launcher / exact preparation command | `e606b3d9abbd32e5faa2fcfc9f2ee4c222e5686afdb2168204da54056d259a15` / `7e59cf7987643cb05ce2de76082b98f464622f2d169a118ecfe7cee92255a261` |
| Windows PowerShell 5.1 preparation controls | `980516900683cc65dbbb56447455ec434d35e5d4ab4008b0c77a4b628573fc8e` |
| Independent nineteen-file preparation inventory | `5b1ae763c976385a581ea592cf9e8af0e51a984161c97c3be99e20701f54d795` |
| G6 all-event minimum/maximum and per-second counts | `95d6656e92c01c455b6e0eb6210f84602d99edbd756618d63ffb67bd792c0753` |
| Original USB launcher preservation / exit-one hold control | `a3efb67d77368754f2ee9b51df4503b03955123184672886da36d332d0007fb6` |
| 43e520d CI metadata / complete log | `55e442f09dcb98c16eedb256414794e5b23537155bcc9b532b5782ada2915a66` / `185fb91fed1310c01cf9333e2e3cda850a8b40bd80706f4e7c6b10c4c60c623f` |

The old LaunchReadOnlyUsbTrace.cmd is now an exit-one hold notice. Its exact executed bytes are preserved separately
as LaunchReadOnlyUsbTrace.cmd.executed-cd269aa1b4794ad2853adf00fe93db8d with the original G5 SHA-256; the recorded
runner is unchanged. An actual command control verifies the hold notice exits one without launching a capture or test process.

The owner subsequently authorizes agent launch; Windows RunAs succeeds and the immediate launch gate clears (G8).
The Windows VM remains running for later validation; its restored
snapshot currently lacks the tracer/license in known historical locations. I09/I106 and all installed-helper/candidate
cases remain open; no stable release GO is implied.
