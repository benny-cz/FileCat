# E-V09-G6 — changed source hash and incomplete physical trace

Links: V09/REC-002/TV-09, I09, I106, ENV-07. **Source-write qualification failed.** The owner executed the
[G5 prepared launcher](E-V09-G5-read-only-usb-trace-preparation.md) elevated on the Windows Insider 26220 host.
The collection completed, but its positive trace controls fail and the complete physical source hashes differ.
No process attribution, source-zero-write pass, installed-helper qualification or candidate qualification is claimed.

The retained private run is
`artifacts/release-evidence/v09-usb-source-trace-20261003/read-only-cd269aa1b4794ad2853adf00fe93db8d`,
2026-10-03 15:09:33–15:17:38 UTC. Clean self-contained source remains exact
`f2f0141b34a2c693951019d724774ca0a0a6fc01`; all 300 inputs and the runner/hasher/tracer pins independently verify.
Serial `2F2000129618`, physical disk 5, 7,796,162,560 bytes, USB/nonboot/nonsystem, G: NTFS partition and volume
identity match the native preflight and read case. Each XML contains exactly one PASS and no skips, failures or errors.
The native read case compares direct and in-process raw-read-protocol paths. It does not use the installed broker,
GUI admission or production process census. Its assertions compare returned lengths and bytes between paths;
requested-length completion and nonempty deleted-item counts are not separately recorded/asserted.

Both independent hash helpers report native device length and hashed-byte count **7,796,162,560**. Before: PID 3144,
15:09:40–15:13:28 UTC, SHA-256 `8d6fade8d64d1cadba426e02b4b1689131c9cc081639c681970d9107a0bf5359`.
After: PID 24408, 15:13:57–15:17:36 UTC, SHA-256
`ad248796c88d82197e8fbe36701f6e72f21406056830484552ace428dbaa3e54`.
These are sequential full-disk observations, not atomic snapshots. No chunk hashes or disk images were retained,
so changed blocks cannot be localized retrospectively. The two hash phases lie outside the trace window.
No further USB tests are run while this difference remains unexplained.

Controller PID 60380 launched Procmon PID 57364 at 15:13:28.869 UTC with `/NoFilter /BackingFile /Runtime 180`.
Native test PID 60912 started at 15:13:32.032 and exited zero at 15:13:44.392 UTC. The owned stop command and native
CSV export exit zero. Nevertheless, **both PML and full CSV contain only 104,749 events**: first
15:13:28.991290 and last 15:13:30.004562 UTC. The last event precedes creation of the known control file at
15:13:31.020 and the native test. Neither the expected one 4,096-byte write/two reads nor any controller/native
or source-alias event appears. Native PML parsing with installed procmon_parser 0.4.0 independently confirms the
same count/time interval and missing events. This is not only a filtered CSV result. Capture-process lifetime and
backing-file existence did not establish continued capture activity; the cause remains undetermined.

The original qualification verifier stops at its missing-positive-controls assertion. Its inputs, code and failure
logs remain intact. A separate incomplete-run inventory reuses only its provenance checks, verifies all **43**
top-level run files (including result.json), and explicitly records TraceQualified=false and
SourceWriteQualificationPassed=false. The wrapper's Captured=true reports collection completion, not qualification.
Zero captured source events cannot establish zero source writes. The trace cannot identify what changed the disk.

The global campaign mutex covers the procedure. The fixed serial file lease is held separately for hashes and the
native case, with recorded handovers; no continuous file-lease claim spans those gaps. Independent post-run checks
confirm all thirty original tracer registry values, their types/data and flat structure restored, with no recorded
worker or tracer remaining. No fixture creation or format was requested in this run. Earlier exact recovery-byte
evidence in [G3](E-V09-G3-usb-campaign-serialization.md) and earlier instrumentation controls in
[G4](E-V09-G4-tracer-controls.md) retain their separate scopes.

Off-source investigation: the bundled tracer manual was extracted with 7-Zip after the prior hh.exe extraction
produced no files. Its WaitForIdle option documents readiness, but does not prove sustained event capture. Read-only
probes of known tracer locations in the running Windows guest find neither the tracer nor existing license/configuration
after snapshot restoration. No license is accepted by the agent. Prepare a bounded host timing control using the
already accepted host tracer before another physical run; qualify its beginning/end controls independently.

[CI 37124521907](https://github.com/benny-cz/FileCat/actions/runs/37124521907) at exact
`81b352e300435151d9044d1739ad6c9733d9865d` passes all four lanes; three package jobs skip.
Complete metadata/logs are retained. No direct artifact inventory is inferred from that metadata check.

| Retained private evidence | SHA-256 |
|---|---|
| Full native PML, 102,607,811 bytes | `52f291d745195b255a393dd44ccd74c879a9dbac344d01f1cb80291bd3c6e262` |
| Full native CSV, 19,777,176 bytes | `4e43a70789f0fa8f1b4b2c23d9ce1c8862d0d13c33e65504ab845a8fc04b562a` |
| Independent incomplete-run inventory: 300 inputs / 43 run files | `f2037760fa69d61e2384063b18e275bcba3d8a7b426121120767fda2d57dd772` |
| Independent native PML inspection | `af47e7377c33c0bfc3c0792a66378604f96f48ef973a724cb896e3c44791ce33` |
| Independent configuration restoration / worker census | `7d04dfe2ce71fa145f72ac498f7c5d26c9a6591b46b90ab16a970d5eaeb15b6f` |
| 81b352e CI metadata / complete log | `018e3cea38bf1b6462b19fb690b286ceb9a4a10ff41cb5ac096ac12cebeb2c4b` / `79982b1a809b49bc8b3cbe3edc7a4d519bbac01ba5a29e399cac7a3566cdbf91` |

I09/I106 remain open; no candidate exists and the recommendation remains **NO-GO**.
