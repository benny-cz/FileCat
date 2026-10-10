# E-CI-I320 — original hosted incomplete-Mac topology CI failure

2026-10-10 CEST. Original push **38069839231 attempt1** at **399a8290951680b38f67f69292e32f1fdf8dee58** fails on Mac; Windows x64/ARM64 and Ubuntu pass. No rerun or workflow mutation. Independent verification retains **24 available digest archives**, **12 TRX** and **27,430 results: 26,555 passes, 874 explicit skips and one failure**. All **72 added incomplete-query controls pass**. All **92 locked restore graphs**, four clean SDK10.0.401 receipts and **1473 canonical raw blobs** verify.

The existing MacTopologyRefreshTests mount-replacement case receives an unknown target classification and Assert.Contains throws ArgumentNullException at line74. Both owned images detach and the fixture is removed. The actual diskutil replies needed to identify the missing backing are absent from this failure's log; the historical cause is unproven. The three other required lanes pass. No downstream Mac App/Remote inventory exists in the available archives; their **4168 predecessor records are unavailable**, not passed or skipped. All available predecessor skip records remain. The separate [exact committed Mac run](E-I320-native-qualification.md) passes its 25 controls and does not turn this failed hosted run green.

The first strict reader expected all fourteen inventories and stopped with StopIteration when a downstream inventory was absent. Corrected adverse reader v2 records unavailable inventories explicitly and preserves the original failed test; it exits1 because qualification is rejected. No source assertion, outcome or skip is weakened. [I322's compiled diagnostics](E-I322-recovery-image-open-ownership.md) retain raw classification inputs on future unknown answers while preserving the original safety refusal and assertions. CI remediation remains open within I106/V09; no candidate acceptance is claimed.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact sources, artifacts, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i320-ci-20261010-v1/independent-adverse-ci-final-v2.json` | `5a6d6a014f72b82808a6adb538a2986f0476b27e8d39c4b458b4b536a12fe186` |
| `i320-ci-20261010-v1/actual-available-observations-v1.json` | `1179f5120aa471feb183546989c140e1b1a72e2055ff2a16d99ce3547cd88f6e` |
| `i320-ci-20261010-v1/watch-final-v1.json` | `63fc9fe461c0cb45d12b9538484b86deb058578c6a6f3de00c1bf231c6aa6b1a` |
