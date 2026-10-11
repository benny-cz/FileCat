# E-CI — original recovery destination publication admission

Original push **run 38099761118, attempt 1, source c3d0ee60d8167eff00e0464c5721c2a6b7b8f028** passes all four required lanes: Windows x64/ARM64, Ubuntu 24.04 and macOS 26. **32248 records: 31219 passes, 1029 explicit skips, zero failures.** All144 added publication-admission controls pass without new skips. [Original workflow](https://github.com/benny-cz/FileCat/actions/runs/38099761118).

Independent inspection verifies27 API digest-bound archives, fourteen TRX inventories, every 32104 predecessor outcome/message and all 1029 exact skips. All1538 canonical raw Git blobs, 92 locked dependency restore graphs and four clean SDK 10.0.401 build receipts verify. No rerun, historical failure reclassification or candidate qualification is claimed.

This qualifies [I335](E-I335-recovery-publication-admission.md), separately from the subsequent [I336](E-I336-all-selected-source-admission.md)/[I337](E-I337-recovery-folder-attempt-admission.md) corrections and the later [native SMB failure](E-I336-full-native-core.md). Physical-source HOLD, wider kernel/atomic/permission scopes and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records preserve exact source, commands, original failures/skips, whole bytes and independent restoration.

| File | SHA256 |
|---|---|
| `i335-ci-20261011-v1/independent-ci-final-v1.json` | `01ea3abb12c01b18101ea5136a449c552be94344bdd40486415fb164e54cd79b` |
