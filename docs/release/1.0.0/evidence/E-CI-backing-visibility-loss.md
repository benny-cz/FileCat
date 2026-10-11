# E-CI — original source-location refusal run passes all four lanes

Original **run 38136477388 attempt 1 at 0beaa10c8a7bc11c307c34a0506d3a77669e98a4** passes Windows x64, Windows ARM64, Ubuntu24.04 and macOS26. **32936 records: 31907 passes/1029 explicit skips/zero failures.** All **72 visibility additions**, plus **56 memory additions since d909333**, pass without new skips. [Original workflow](https://github.com/benny-cz/FileCat/actions/runs/38136477388).

Independent inspection verifies **27 API digest-bound archives, fourteen TRX inventories, 1583 canonical raw blobs, 92 locked restore graphs and four clean SDK 10.0.401 build receipts**. Every **32808 last-green predecessor outcome/message and 1029 exact skips** remains. The intervening [ec5868e Mac hex failure](E-CI-memory-content-retirement.md) stays adverse; its cause remains unproven. [Unchanged Mac diagnostics](E-I06-hex-retirement-investigation.md) retain a separate incomplete full-App result.

[Exact I347](E-I347-native-qualification.md) is sealed separately. This run precedes [I348](E-I348-backing-identity-replacement.md), which needs its own exact committed/hosted follow-up. No rerun, candidate, physical-source resumption, contract freeze or publication is claimed; human GO remains.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence; nested records retain exact source, commands, raw failures/skips, complete bytes and restoration.

| File | SHA256 |
|---|---|
| `i347-ci-20261011-v1/independent-ci-final-v1.json` | `2e3d35da54dbd99ab532b98caac5223e8c70d8e99a359a623db28166e74eb3f5` |
