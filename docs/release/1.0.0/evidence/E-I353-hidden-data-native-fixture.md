# E-I353 — provide native backing identities to the Windows hidden-data fixture

Original CI at **69cd563** retains a failed hidden-data copy: the headless services use PortableFileOperations, which intentionally supplies no Windows file identities, while this test installs the native Windows stream provider. I350 correctly refuses the resulting unavailable backing identity. An exact committed local repeat preserves the same failure.

The fixture now installs production WindowsFileOperations before submitting any job, after checking that the manager is idle. Its existing stream listing, copy result, exact payload, deletion, mark retention, unchanged main-file bytes and navigation assertions are preserved. The corrected targeted case passes. Global App composition and the product safety check are unchanged; no new skip or host UI.

The combined source export independently verifies **1600 canonical blobs /five declared overlays**, including I351, I352 and this fixture. Its full headless App attempt hit the **600-second limit** without a complete XML/final command receipt. Raw output remains; no exit code, full-suite pass or cause is invented. The separate I351 347-case App selection remains qualified at its own declared source.

Automatic approval review initially refused scratch cleanup pending process-exit verification. An independent process census confirmed the timed-out PID and all owned producers absent before cleanup was retried. Four bounded host scratch roots/**1324 files** are archived, hash/exclusive-open checked and restored. Original source/commands, timeout, reader import failure and approval refusal remain recorded. [The original CI](E-CI-initial-identity-adverse.md) stays adverse; committed follow-up remains required.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, payloads, commands, original failures/skips and restoration.

| File | SHA256 |
|---|---|
| `hidden-native-fixture-safety-20261011-v1/independent-final-v1.json` | `4c171b7f116c9fc65561610574268c8c9c5975f2829795421c48635696ff69c3` |
| `hidden-native-fixture-safety-20261011-v1/retained-tool-sources-v1.json` | `22c7984cac43343631bee890c666f2c60ef171ff2817b7056282164748ec3ee7` |
