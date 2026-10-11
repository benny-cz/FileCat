# I366 — Windows lease-fixture marker becomes visible before it is readable

2026-10-11 CEST. **High, required CI validation; remediated preliminarily.** The documentation-only main push `dd6c18a4f2d1e66d33feb09e1514d3a349690b8d` had a failed Windows x64 lane in original run `38174416461`, attempt 1. All other required lanes passed. Its digest-checked `test-results-windows` archive contains 415 Windows-platform TRX results, exactly one failure: `LiveUsbLeaseTests.A_second_process_is_refused_and_process_death_releases_the_lease`. The parent saw the child-created `.lock.ready` marker, but `File.ReadAllText` then threw a sharing violation at line 64. The raw job log, complete Windows TRX and first `gh api` escape-sequence collection failure are retained. The previous green I365 source `a21fd0a` has byte-identical test code; the changes through `dd6c18a` are documentation only. This identifies a fixture readiness race, not a demonstrated physical-source or product lease failure. The original CI run remains **failed**.

The fixture now waits up to five seconds for the already-visible marker to become readable, retrying only `IOException` while the child is alive. It still requires the exact `held` content, a live child, exclusion of the second process, and reacquisition only after child death. A persistent sharing failure or exited child still fails. No production source or lease implementation changed.

The corrected test project builds with zero warnings/errors. In the disposable Windows 11 VMware guest, 16 separate class runs produced **32 passes, zero failures/skips** in actual xUnit XML results. The independent reader rehashed all 55 staged payload members, each native output file, the original failed TRX and source/build inputs. The guest reported no residual owned test process or temp child; its owned payload/results and launch files were removed.

The exact committed `13b1bc5` source, without an overlay, was then built and its complete Windows Platform suite run in the VM. VMware `runProgramInGuest` used session 0 while Explorer ran in session 1; that run was **377 passed, four failed, 34 skipped**, with all four failures in WebView2 page tests (`Invalid window handle`). Both lease cases passed. A separate scheduled task ran the identical 55-file compiled payload in the logged-in session 1: the four page cases passed, as did both lease cases, but a broker requester fixture failed while deleting its exited copied `FileCat.exe`: **380 passed, one failed, 34 skipped**. All 415 names, raw XML, command results and owned-task/file cleanup are retained; neither full VM run is classified green. The broker cleanup finding and its separately qualified correction are [I367](E-I367-broker-requester-cleanup.md). Session 0's page failures are consistent with a missing interactive desktop; they do not establish a product regression. No host UI, physical USB source validation, release candidate or stable publication is claimed.

The **separate original-push CI run `38177420786`, attempt 1, at exact `13b1bc5` passed** its four required lanes. Its 27 digest-checked archives contain 14 actual TRX inventories with 32,891 passes, 1,085 explicit skips and zero failures; both lease cases pass on Windows x64 and ARM64. The independent reader rechecks every raw command output, archive digest/member and TRX count. The two VM full-suite failures above retain their own outcomes; a hosted green run does not erase them. This run predates I367's committed correction and is not a candidate.

## Selected immutable receipt

Paths are relative to the private `FileCatReleaseEvidence` root. The proof pins original failure, actual compiled payload, native results and cleanup.

| File | SHA-256 |
|---|---|
| `i366-lease-marker-fixture-20261011-v1/independent-native-v1.json` | `e3a9aa0fb957545b5d4cf8c11c5790b0f925ed13a1e80840ca1e60df161488e9` |
| `i366-exact-native-20261011-v1/independent-full-adverse-v1.json` | `b1ca5d3f25b6804e47b74efba49b065d8145a10ab7381bd0dcecb1c57888cc1f` |
| `i366-exact-native-20261011-v1/independent-interactive-adverse-v1.json` | `e14c0f7047c21bf1dd74feb22cbfabf278d166434ceb0e2369222dee040424ca` |
| `i366-ci-exact-20261012-v1/independent-ci-v1.json` | `95e6fb053296cc2ea03441b119fcf68d3f11b632f784ea10a83e686bfe769edb` |
