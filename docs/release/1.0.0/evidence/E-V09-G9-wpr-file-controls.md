# E-V09-G9 — built-in WPR file/disk marker controls

Links: V09/REC-002/TV-09, I09, I106, ENV-07. **Preliminary ordinary-file instrumentation controls PASS.**
This is an alternate-recorder control after [G8's mixed Procmon outcomes](E-V09-G8-trace-timing-run.md), not a
raw-device or protected-source test. No USB I/O, FileCat launch, format, installed broker or source-zero-write
qualification is requested or inferred. G6's physical hash difference remains unresolved.

Private run:
`artifacts/release-evidence/v09-wpr-controls-20261003/control-489f7eee745d447f96fc041adc6d06b8`,
Windows Insider 26220, elevated controller PID 16988, child PID 57016. Standalone preparation source is exact
`892c65b87249072fca4dadb444f9adfbf95e31f0`; three Windows PowerShell 5.1 scripts parse and pinned preparation
validation passes. Windows RunAs launches the pinned runner without further owner interaction.

The built-in recorder/decoder hashes are pinned. DiskIO/FileIO profiles are exported and retained. Default and
unique named session both report no recording before start. The instance
`FileCatV09_076b7387a9b4486f84e93873c182374d` records in an owned E: trace directory, then stops/merges its ETL;
default recording remains inactive. A separate watchdog scopes cancellation to that exact name and enforces a
ninety-second/two-GiB recording bound; normal completion is recorded without watchdog cancellation. Stop/decode
helpers have separate bounded waits. All recorded native processes exit zero. Independent final checks find no
owned recording or recorded worker remaining. No global WPR cancellation is used.

The command design follows Microsoft's [WPR reference](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/wpr-command-line-options):
named-instance operations, multiple profiles, owned temporary location and collector statistics. Microsoft's
[tracerpt reference](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/tracerpt)
grounds the XML/summary export. Documentation supplies command semantics, not test results.

Each parent/child fixture is exactly 4,096 bytes of 0x5A and independently matches SHA-256
`f302957da5220938a7e3e51a8718c79b9e00dc13ab2119e8cfc978f041720382`. Each performs one WriteThrough stream write,
flush and two reads; child reads are two seconds apart and parent final read follows child exit plus seven seconds.
The independently decoded native events contain two FileIO Write records and two Read records per marker,
all offset zero/size 4,096, with correct PID/path. **Two FileIO records are not relabelled as two application writes.**
There is one corresponding DiskIO Write per marker, 4,096 bytes, correct PID and physical disk 2 (E:'s backing disk).
The untouched path has no selected filename/create/read/write/disk event and does not exist. Child start/stop map to
the controller parent. Native parent-marker span is 9.494542 seconds; final read follows child stop by 7.010348 seconds.

Both live collector reports show dropped events zero and both collectors' Events Lost zero. Native ETL processing
and tracerpt summary independently agree on **2,561,544 events and zero reported loss**, UTC range
17:02:27.5646075–17:03:14.6594396 on 2026-10-03, including stop/rundown. Zero reported loss does not establish
visibility of every possible raw-device write; that needs a separate controlled raw-device positive test.

Full ETL, native XML, summary/report, resolved profile, expected markers, process records and logs are preserved.
The tracerpt XML contains schema/processing errors and renders a malformed timezone offset (`+01:59`), so it is not
used for UTC/path/process qualification. A separate native ETL reader uses the already cached, pinned Microsoft
TraceEvent 3.2.6 package and .NET SDK 10.0.401. Its API design follows Microsoft's
[TraceEvent guide](https://github.com/microsoft/perfview/blob/main/documentation/TraceEvent/TraceEventProgrammersGuide.md).
The diagnostic project is private evidence only; no FileCat dependency is added. It reads all native events,
matches the tracerpt total and retains selected controls/lifetime/disk events. An initial display-only attempt to
print a summary with Windows' default Python encoding fails; collection/decoding and native data are unchanged.

Independent verification checks **63 run files and 23 reader/source/build files** by size/hash, all native phase
identities/exits, both fixture bytes, event ranges, loss reports and named-session cleanup. No temporary ETL remains
after the recorder's merge. Collection status is kept separate from control qualification.

| Private evidence | SHA-256 |
|---|---|
| Full ETL, 598,736,896 bytes | `5f2157107fc855e8f734517ec6784f1b76253acbb62b6eeffc8ff40fde69e056` |
| Full native XML, 3,428,411,290 bytes | `308e77479428a11c1d15e4b3fa314641814193056fc6556c56059cebef6cc61b` |
| Resolved combined profile | `7fa094f0b86acd5144f4624b8eac85b5e95e9c99829d365a36727cabe90d01c3` |
| Native decoder summary | `6b63d25129756ded6ef496421a1c079ae915d57b8c476ab774849352ce41e6a5` |
| Independent 63-run/23-analyzer-file inventory and controls | `b214c14f3c433a9470f983da426ea752b11002ae251c083c18a4a6cb44937851` |
| Selected native ETL inspection | `e9b9a5afcf118584e15856e26788da01352c3cb81401035fe5055919511a4fc7` |
| Independent named-session/worker cleanup | `3dd0eb69141e4b820fad1da91071e99262e8dbe86570c538fd29ffe12dfcc419` |
| Cached TraceEvent 3.2.6 package | `e4dd62e649642130145fa6e07f75a73ceb9c617b739b96455fb60b7cac89ed20` |

Next: establish raw virtual-device read/write visibility using owned off-source media before returning to the
unexplained physical source difference. I09/I106 remain open, no candidate exists and the recommendation is NO-GO.
