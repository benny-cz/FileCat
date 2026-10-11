# I366 — Windows lease-fixture marker becomes visible before it is readable

2026-10-11 CEST. **High, required CI validation; remediated preliminarily.** The documentation-only main push `dd6c18a4f2d1e66d33feb09e1514d3a349690b8d` had a failed Windows x64 lane in original run `38174416461`, attempt 1. All other required lanes passed. Its digest-checked `test-results-windows` archive contains 415 Windows-platform TRX results, exactly one failure: `LiveUsbLeaseTests.A_second_process_is_refused_and_process_death_releases_the_lease`. The parent saw the child-created `.lock.ready` marker, but `File.ReadAllText` then threw a sharing violation at line 64. The raw job log, complete Windows TRX and first `gh api` escape-sequence collection failure are retained. The previous green I365 source `a21fd0a` has byte-identical test code; the changes through `dd6c18a` are documentation only. This identifies a fixture readiness race, not a demonstrated physical-source or product lease failure. The original CI run remains **failed**.

The fixture now waits up to five seconds for the already-visible marker to become readable, retrying only `IOException` while the child is alive. It still requires the exact `held` content, a live child, exclusion of the second process, and reacquisition only after child death. A persistent sharing failure or exited child still fails. No production source or lease implementation changed.

The corrected test project builds with zero warnings/errors. In the disposable Windows 11 VMware guest, 16 separate class runs produced **32 passes, zero failures/skips** in actual xUnit XML results. The independent reader rehashed all 55 staged payload members, each native output file, the original failed TRX and source/build inputs. The guest reported no residual owned test process or temp child; its owned payload/results and launch files were removed. These are targeted native results; the full suite and original-push CI for the corrected commit remain separate and pending. No host UI, physical USB source validation, release candidate or stable publication is claimed.

## Selected immutable receipt

Paths are relative to the private `FileCatReleaseEvidence` root. The proof pins original failure, actual compiled payload, native results and cleanup.

| File | SHA-256 |
|---|---|
| `i366-lease-marker-fixture-20261011-v1/independent-native-v1.json` | `e3a9aa0fb957545b5d4cf8c11c5790b0f925ed13a1e80840ca1e60df161488e9` |
