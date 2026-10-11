# E-I338 — owned pipe fixture scheduling and server lifetime

2026-10-11 CEST. **Medium validation reliability; preliminary correction.** Baseline **85ba1eb452a493effc3f8dd9536aa36a90773dda / 1546 canonical raw Git blobs**, with declared test-only observations/correction and a private diagnostic process. Production, runtime, dependency locks and the performance threshold remain unchanged. No device or workstation UI is used.

The raw-read test fixture serves blocking pipe requests with `Task.Run`. A separate owned diagnostic process caps its worker pool and occupies the sole worker, then starts an ordinary caller thread. After the blocker releases, the baseline helper still cannot finish until the private pool's original capacity is restored. Serving the same session on a dedicated long-running task lets it finish before capacity restoration. Host and Windows guest controls reproduce this distinction with identical 4096 known source bytes, exact read/close messages and complete owned restoration. This establishes a finite fixture dependency; it does **not** establish the cause of the original hosted 3.12-second benchmark failure.

The fixture now uses a dedicated helper thread. All three file-backed session methods also await the server's exact closure with a bounded wait. The benchmark requires the complete 70,000 known photo bytes on every read, retains warmup/all three timing samples and worker availability, and keeps the original **pipe < direct × 20 + 2 seconds** threshold. These are test changes; actual privileged-helper scheduling is unchanged.

All **twelve healthy method runs** pass across paired host/Windows-guest producers: unaligned/whole/EOF bytes, image scan and the unchanged timing requirement. All four private scheduling probes have full known bytes, closed servers, exited callers/blockers and restored process-local pool settings. Guest parents/children measure high RID 12288/session 0; the pinned .NET 10.0.12 runtime and **535 independent native file checks** verify. Both transport listeners stop. Two owned SDK logs are retained/verified before removal; owned host/guest temporary roots are absent.

The original private-project restore refusal is preserved; the fresh private project is outside the canonical tree and retains normal FileCat lock enforcement. An earlier diagnostic joined its blocked caller before restoring pool capacity and aborted; its unchanged 4096-byte leftover is retained, independently verified and removed after the producer exits. Fresh paired producers restore the private pool before joining; they pass without discarding those earlier outcomes.

The [original I336 CI failure](E-CI-all-selected-source-admission.md) stays failed. This finite fixture correction does not qualify a complete hosted CI result, reference-hardware performance, native devices, physical-source safety or an installed candidate. Exact committed/hosted follow-up remains; owner gates, physical-source HOLD and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records preserve exact source, commands, original failures/skips, whole bytes and independent restoration.

| File | SHA256 |
|---|---|
| `i338-device-pipe-fixture-20261011-v1/independent-remediation-v1.json` | `c07eb047210ce299697e8fab83e51711c54daf9934172c1d29204c292da2601b` |
| `i338-device-pipe-fixture-20261011-v1/retained-tool-sources-v1.json` | `f156ff664af634d707d0e8b8e690362ae562044aacd43b8334c237ea6a1c2bdf` |
