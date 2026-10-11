# E-CI — original known-identity replacement run passes all four lanes

Original **run 38139134555 attempt 1 at 97109700ef7345c0ff2493633eae229bc7b6b0b2** passes Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26. **33008 records: 31979 passes/1029 explicit skips/zero failures.** All **72 new known-identity replacement controls** pass. [Original workflow](https://github.com/benny-cz/FileCat/actions/runs/38139134555).

Independent inspection verifies **27 API digest-bound archives, fourteen TRX inventories, 1589 canonical raw blobs, 92 locked restore graphs and four clean SDK 10.0.401 build receipts**. Every **32936 last-green predecessor outcome/message and 1029 exact skips** remains. Historical failures, including the ec5868e Mac hex failure with unproven cause, remain separately adverse.

[Exact native I348](E-I348-native-qualification.md) is sealed separately. This run precedes [I349](E-I349-unix-picture-parent-lifetime.md), whose overlay requires its own exact committed/original CI follow-up. No CI rerun, candidate, physical-source resumption, contract freeze or publication is claimed; human GO remains.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence; nested records retain exact sources, commands, raw failures/skips, byte checks and restoration.

| File | SHA256 |
|---|---|
| `i348-ci-20261011-v1/independent-ci-final-v1.json` | `1c920d8e1b15c7c089b3b34eb4d521ef8c0ea6e85e04936ab7f9d06fc3087542` |
