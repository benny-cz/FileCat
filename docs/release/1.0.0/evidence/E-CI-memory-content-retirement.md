# E-CI — original memory-source run with Mac hex-editor retirement failure

Original **run 38134467259 attempt 1 at ec5868e41e36f3ef3545b52e659f429a1b7511f4** is adverse. Windows x64, Windows ARM64 and Ubuntu 24.04 pass; macOS 26 fails one existing App hex-editor retirement case. **32,864 records: 31,834 passes/1029 explicit skips/one failure.** All **56 added memory-content controls** pass without new skips. [Original workflow](https://github.com/benny-cz/FileCat/actions/runs/38134467259).

The failed case is `HexOverlayRetirementTests.Closed_or_replaced_hex_editors_retire_old_edits_while_live_editors_keep_undo(size: 65536, history: "redo", route: "close")`: all eight observed edit-storage weak references remain alive. Its complete original output, stack and failing outcome remain; the cause is unproven. Native memory controls and a passing Windows headless App suite do not reclassify that failure. Investigate actual close/retirement and fixture ownership before changing an assertion.

Independent inspection verifies **27 API digest-bound archives, fourteen TRX inventories, 1577 canonical raw Git blobs, 92 locked restore graphs and four clean SDK 10.0.401 build receipts**. All 32,808 predecessor identities remain: **32,807 outcomes/messages unchanged, one former pass now failed**, with all **1029 exact skips** retained. No inventory is unavailable. [Earlier corrected-fixture CI](E-CI-cleanup-identity-observation.md) stays the last fully green source. Historical failures keep their own outcomes.

[Exact native memory qualification](E-I346-native-qualification.md) is separate. This run precedes the [I347 product correction](E-I347-backing-visibility-loss.md), whose own original hosted follow-up remains. No rerun, candidate, physical-source resumption, freeze or publication is claimed. Explicit human GO remains.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, raw failures/skips, complete bytes and restoration.

| File | SHA256 |
|---|---|
| `i346-ci-20261011-v1/independent-adverse-ci-final-v1.json` | `6b39651c1e8f267fa8eab8ec83248ef4e3864159d273653aa0ca80b68739f89d` |
