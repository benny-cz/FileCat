# E-CI — original Unix held-source CI

2026-10-11 CEST. Original [run 38087925138 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/38087925138), push at exact **c14fb214294e6acee2d91ab5aab012a624ddf7f3**. All four required lanes succeed. **27 digest-verified archives/fourteen TRX/31,920 records: 30,903 passes/1017 explicit skips/zero failures.**

All twenty new control records are accounted for: **ten native Unix passes/ten expected Unix-only Windows skips**, with each exact skip reason verified. Every **31,900 predecessor outcome/message and 1007 exact predecessor skips**, all **1510 canonical source blobs**, **92 locked restore graphs** and **four clean SDK10.0.401 build receipts** verify independently.

[Exact native I329](E-I329-native-qualification.md) remains separately qualified at the same commit with ten native passes. Original failures in other runs remain failed; no rerun, workflow mutation, physical-source resumption or candidate acceptance is claimed. This run precedes I330/I331. Their exact committed/hosted qualification remains separate, as do owner/freeze/publication gates and explicit human GO.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, raw failures/skips, observed bytes and independent cleanup.

| File | SHA256 |
|---|---|
| `i329-ci-20261010-v1/independent-ci-final-v1.json` | `4f25261056dbbe8f691f26d73ea9e67f6de3db2f3bf427e3a2067f56b94d6018` |
| `i329-ci-20261010-v1/actual-available-observations-v1.json` | `9e1ecdffb99bdd0c0c084fa0ef996b489e37ae940f06ddaf3fb3ffcc0c7f767b` |
