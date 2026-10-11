# E-CI — original recovery metadata admission qualification

Original push **run 38104302687, attempt 1, source c7d17dab4efe8fe848e733efd671a7510c85069a** succeeds on Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26. **32,608 records: 31,579 passes, 1029 explicit skips, zero failures.** All **72 added metadata-admission controls** pass without new skips. [Original workflow](https://github.com/benny-cz/FileCat/actions/runs/38104302687).

Independent inspection verifies **27 API digest-bound archives, fourteen TRX inventories, 1553 canonical raw Git blobs, 92 locked restore graphs and four clean SDK 10.0.401 build receipts**. Every **32,536 predecessor outcome/message and 1029 exact skips** from the original I338 run remains. The exact eighteen-case local inventory occurs four times in the hosted lanes, with no unexpected additions/removals.

[Original controlled remediation](E-I339-recovery-metadata-admission.md) and [exact native follow-up](E-I339-native-qualification.md) retain their own scopes. Historical failures stay failed; this is preliminary evidence. No workflow rerun, contract freeze, installed candidate, physical-source resumption or stable publication is claimed. Explicit human GO remains.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, raw failures/skips, whole bytes and restoration.

| File | SHA256 |
|---|---|
| `i339-ci-20261011-v1/independent-ci-final-v1.json` | `5de51addac173a124aaf00ccb7af38de562599f2e785ad223a14e7d741c9bf31` |
