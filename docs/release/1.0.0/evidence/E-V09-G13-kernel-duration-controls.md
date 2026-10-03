# E-V09-G13 — kernel recorder duration and trace-location controls

V09/I09/ENV-07. **Preliminary nine-minute raw virtual-device instrumentation controls PASS after trace-location
correction.** No USB access or FileCat launch. G6's historical source difference remains unresolved; no product or
physical source-write qualification is inferred.

Standalone preparation source: `6267331ee1e00027986822baa0e0b5bd5ce6c826`, elevated Windows Insider 26220 host.
Private diagnostic root: `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\v09-kernel-duration-20261003`.
This is an authorized second workspace root; evidence/VHD files reside on C: backing disk 3, distinct from USB 5.
Trace scratch uses a new marked short `artifacts/release-evidence/wpr-short-<guid>` on E: backing disk 2.
All earlier inputs, failed runs and scratch markers remain retained; no unrelated files are deleted.

The custom profile has one system collector, 512 buffers of 256 KiB, the same seven G12 kernel keywords and no
stacks/user-mode collector. It explicitly sets SuppressHighVolume false. Microsoft's
[SystemProvider documentation](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/systemprovider) describes
that setting's save-time behavior; local WPR accepts the actual profile. Documentation supplies no test outcome.
Private helper/reader builds have zero warnings/errors, with no production dependency or code change.

The initial nine-minute run `34d6f7b446d6499c84027edf4f2528bb` reports successful collection and zero loss, but its
393,216-byte ETL has **only 32 metadata events**, no positive operations or lifetimes. Qualification fails. A pilot
launcher next fails before capture because its evidence function is called before definition; no raw fixture or
recording begins. Original scripts/pins and failure logs are retained. A successor fixes that order and normalizes
the scratch ownership check before execution.

Matched three-second pilot helper/profile controls then differ in trace temporary location: short E: run
`1cb2940dda214b69be2ba125cd155672` captures 507,138 events/zero loss and passes all exact raw/parent/lifetime/byte
checks; long C: run `142bc93c99d94778a8631d436fa69f4e` again captures only 32 metadata events and fails.
Both use the same helper and profile, with C: fixture storage. The location association is established; exact
Windows failure mechanism, path-length attribution and storage-volume causality are **not established**.
Zero reported loss without positive visibility is never accepted.

The corrected nine-minute run `337aaa074c6c43a1a362c6e35cb53db8`, controller PID 52612, uses the original long-delay
helper and short scratch. Its new entirely zero 64-MiB VHD has no source/parent and is independently checked for
exact virtual mapping, bus 15, RAW/zero partitions, capacity and non-boot/non-system identity. Native alias is
`\Device\Harddisk6\DR14`, derived from this run. At 1 MiB, the aligned helper reads zeros, writes 4 KiB of 0xA5,
reads it, waits **540.020002 seconds**, writes the same 4 KiB and reads again. Native FileIO/DiskIO contain exact
ordered **Read/Write/Read/Write/Read**, process/thread/offset/count/alias and corresponding call-boundary timestamps.
Both writes are intentional fixture controls. Two System attachment reads of 512 bytes at zero are separately
retained. Full offline checking finds the sole 0xA5 block and zeros everywhere else, including the negative block.

Parent file bytes, typed writes/reads and corresponding disk write pass; the late read follows helper exit by at
least seven seconds. The untouched filename is absent. The trace spans 19:31:32.9016942–19:40:45.4064003 UTC on
2026-10-03: **1,190,789 native events, zero reported loss**, with both live counters zero at start/end. All native
phases exit zero. Independent cleanup confirms detached owned media, stopped named recording and workers absent.
The bounded named watchdog does not interfere with another recording; its six-GiB size check is nominal metadata
accounting, with a separate twelve-minute deadline.

Independent successful verification checks 64 run files and 98 private diagnostic source/build files, pins,
whole fixture data, all positive/negative events, duration, lifetime, loss and cleanup. Separate incomplete
verification checks both metadata-only captures and the pre-capture launcher failure, explicitly refusing a pass.
An initial preparation source-ID transcription error is preserved and corrected against git before execution.

| Private evidence | SHA-256 |
|---|---|
| Exact custom profile | `32db96404ccd1b36ba98e5d070471782120b7c7244592878781ee79822255e90` |
| Corrected duration preparation pins | `766d1e490f3658c7f887019972ab14958e327b7400b0c8fa143f9db111af42c8` |
| Successful full ETL, 153,616,384 bytes | `097373f8bf37ba2904e5d9a615dbb1c78a9c92ee8655d41c3120a891ce7df38f` |
| Successful full VHD, 67,109,376 bytes | `73dd48878932781839b64f56f7af6e835b73b413dadbb0c849ca6aa51eb4dafb` |
| Native selected-event inspection | `b6799d0f36235ffb7659a17626c829b8444c9d0e7a03b492eda92546f3b8e8cd` |
| Independent duration qualification | `3f4630ec454251b4d5da17d792e68b2d62a55e8cf8f36b726469085105957de0` |
| Independent short-pilot qualification | `43c629be6bd7e811590be4b043795c1bb776abab55a086c910b0fa8488dd32fe` |
| Independent incomplete-capture inventory | `c4b860a37c21b9430499be72c5a40c4386e8778e51cd6e150c94c60be377a717` |
| Independent successful cleanup | `fcd6657d9f9418302ec05967edc88333e95151cb488fe79807d1aef46941f8ac` |

Exact 6267331 CI [37146418825](https://github.com/benny-cz/FileCat/actions/runs/37146418825) passes four test lanes;
three package jobs skip. Retained run JSON SHA-256 `efd4d9f514aedda8e43f70c4ec33f349b582da5d7e574c44e8c222be463bfe64`,
full-log SHA-256 `54bfe76e55044c6e39e2a6ee818c3282b42223faf84499570463445688d4cf70`.
CI does not execute these standalone diagnostics or qualify a release artifact.

Proceed with a read-only source-change observation using this exact profile and short scratch. Its runner requires
the independent duration proof before source access. G6/physical product tracing and I09/I106 remain open; no candidate,
recommendation NO-GO.
