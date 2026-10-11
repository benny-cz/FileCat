# E-CI — original staging ownership and fixture qualification

Original push **run 38129169252, attempt 1, source 1b1369702c37e5837cc6a37aca97acc21f6c4e30** passes Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26. **32,736 records: 31,707 passes, 1029 explicit skips, zero failures.** All **128 additions** pass: 26 Core controls in each of four lanes and twelve Windows controls in each of two lanes. [Original workflow](https://github.com/benny-cz/FileCat/actions/runs/38129169252).

Independent inspection verifies **27 API digest-bound archives, fourteen TRX inventories, 1564 canonical raw Git blobs, 92 locked restore graphs and four clean SDK 10.0.401 build receipts**. Every **32,608 predecessor outcome/message and 1029 exact skips** from [I339](E-CI-recovery-metadata-admission.md) remains, with no unexpected additions/removals.

[Exact native I341/I342 qualification](E-I341-I342-native-qualification.md) and the original [local staging ownership](E-I341-local-staging-ownership.md)/[fixture ordering](E-I342-staging-fixture-ordering.md) retain separate provenance. The original I340 CI failure stays adverse. No rerun, freeze, installed candidate, physical-source resumption or publication is claimed. Explicit human GO remains.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, original failures/skips, complete bytes and restoration.

| File | SHA256 |
|---|---|
| `i341-i342-ci-20261011-v1/independent-ci-final-v1.json` | `ceaf383e0aa42c513931328aa7145e3090c0e5ece3412b35aa0c1a4bada2b29b` |
