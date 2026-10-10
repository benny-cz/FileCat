# E-CI-I319 — original committed Unix recovery source-case CI

2026-10-10 CEST. Original push **38068462762 attempt1** at exact **7b73e2b970a2e8bc8b36cdcec0b6d31cdd56e3fb** succeeds on all four required Windows x64/Windows ARM64/Ubuntu/macOS lanes. No rerun or workflow change. Independently verified **27 digest archives**, extracted members, **14 TRX** and **31,526 results** contain **30,519 passes, 1007 explicit skips and zero failures**.

All **16 applicable new controls pass**: same-source controls on every lane and four case-distinct Unix controls on each Unix lane. Windows x64/ARM64 explicitly skip the eight Unix-only records. Every **31,502 predecessor outcome/message** and all **999 predecessor skips** remain. All **1469 canonical raw blobs**, **92 locked restore graphs** and four clean **SDK10.0.401** receipts verify. [Exact local/Ubuntu evidence](E-I319-native-qualification.md) remains separately scoped. Earlier adverse CI keeps its original outcomes; broader identity/races/physical/candidate gates remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact sources, artifacts, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i319-ci-20261010-v1/independent-ci-final-v1.json` | `43f7b56b0b20e030e8fd6aecc4a0f95e5a4bf93a3a5cee012d2f458885d4293a` |
| `i319-ci-20261010-v1/actual-available-observations-v1.json` | `9036130672627447d6dd65c16909556a8de8b2abbb56f6748ecab1467bfec65d` |
| `i319-ci-20261010-v1/watch-final-v1.json` | `24d7c19965a2e5b0f17d79fefdd35a44bace962043338c6ee5002cf1e2c40dff` |
| `i319-public-20261010-v1/i319-main-push-v1.json` | `16edc779d6f7c53cec74ec0506bcb5c7f10b167485498792014c9fd837f2e226` |
