# Original I357 CI — one Linux queued-rename failure retained

Original push **run38157456129 /attempt1 /dfe32b33cbb6c53203fd15cb4636f8f0db2987be** is adverse. Windows x64, Windows ARM64 and macOS26 pass; Ubuntu24.04 fails one App assertion in InterruptedRenameTests.A_queued_rename_revalidation_cannot_enter_after_demand_ends, shutdown case. The source was moved after the observed stop boundary although the recorded call count remained17. Raw output reports ViaAtEnd=true/TargetAtEnd=false and then Via=false/Target=true, with the operation returning false. The original cause remains unproven pending controlled reproduction; this observation must not be called a harmless timing failure.

All **33712 observed records** remain: **32634 passes /1077 explicit skips /one failure**. All **336 archive/recovery/direct-reader/nested-reader controls pass** and their complete bytes/handles independently qualify, but the original overall CI stays failed. All other33647 predecessor outcomes/messages and1077 exact skips remain, with64 new nested-reader passes; the one formerly passing shutdown record is retained as failed.

Independent collection verifies **24 available digest archives /14 TRX inventories /1619 canonical raw blobs /92 locked dependency graphs /four clean SDK10.0.401 build receipts**. Missing successful Linux package artifacts are not fabricated. No workflow rerun or result reclassification occurred. [Exact selected dfe32b3 qualification](E-I357-native-qualification.md) passed separately and does not replace this full hosted result. [Prior04f6f18 CI](E-CI-recovery-reader-admission.md) remains the latest fully green run. I358 source-opening remediation is unrelated to the rename failure; follow-up must preserve this source/artifact identity and original output.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, payloads, commands, failures/skips and restoration.

| File | SHA256 |
|---|---|
| `i357-ci-20261011-v1/independent-adverse-ci-final-v1.json` | `7bd929664b3935439d99aca9d28d4ca032413fa21a97b7ab59462f829de5eef8` |
| `i357-ci-20261011-v1/retained-tool-sources-v1.json` | `c16331a7890cc5b2792ed5f7156f3a9bc2cd3b52325023e0dc48d8230f2c0c87` |
