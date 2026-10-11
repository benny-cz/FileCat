# E-CI — original initial-identity run retains two failures

Original **run 38146279696 attempt 1 at 69cd5639d598cf3c5fb60d4414665f2fa1050a0b** is adverse. Ubuntu/macOS pass; Windows x64 fails the hidden-data copy fixture and Windows ARM64 fails the regular-image pipe closure observation. [Original workflow](https://github.com/benny-cz/FileCat/actions/runs/38146279696).

Actual available evidence: **29066 records /28216 passes /848 explicit skips /two failures**, **23 digest-bound archives /twelve TRX inventories /1600 canonical blobs /92 locked graphs /four clean SDK 10.0.401 build receipts**. All **200 new initial-identity/fixture controls pass**. Every observed predecessor skip remains exact. ARM App and Remote inventories are unavailable after the earlier lane failure; their outcomes are not fabricated or reclassified.

[I352](E-I352-pipe-fixture-observation.md) reproduces and removes a fixture scheduler dependency without changing deadlines; the original ARM timing cause remains unproven. [I353](E-I353-hidden-data-native-fixture.md) corrects the native-stream fixture composition and passes its targeted test; its separate full App timeout remains adverse. [Exact/native I350](E-I350-native-qualification.md) is qualified separately. Reader v1 wrongly required absent ARM inventories; v2 preserves missing observations and raw failures without rerunning CI.

Last fully green CI remains e0bbc53/run 38143197589. The new remediation batch needs its own original committed CI qualification. No contract freeze, candidate, physical-source resumption or publication is authorized.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, payloads, commands, original failures/skips and restoration.

| File | SHA256 |
|---|---|
| `i350-ci-20261011-v1/independent-adverse-ci-final-v1.json` | `3fdfe393862224c7ef3481bd03d6fd439b89793331fb36431ab3d9fdad962b48` |
