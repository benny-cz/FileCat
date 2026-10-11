# E-I347 — stop writes after losing an observed source-container location

2026-10-11 CEST. Final baseline **ec5868e41e36f3ef3545b52e659f429a1b7511f4 /1577 canonical raw Git blobs**, declared product/test overlays. Source-container identity and final-path queries can return unavailable answers. After an owned source is renamed into the copy's staging path, those answers previously admitted cleanup or publication despite the missing original location. Each baseline retains **six failures/six healthy cases: four complete source-container deletions and two publications of source bytes as extracted output**.

The correction refuses when a previously observed backing container now has neither a current final path nor an identity. Failure cleanup uses the same check without requiring a successful cancellation checkpoint. It preserves complete source bytes at the externally renamed staging path, refuses publication and rolls failed-copy progress back. Healthy extraction remains usable. This addresses lost observations; containers with no initial observation, replacement at a still-observable source path, other unavailable identities, atomic mutation races and physical/candidate qualification remain separate.

All **24 local/48 native final controls pass** through six source-close/verification/publication failure/retry routes plus healthy extraction. Actual Unix device/inode and native Windows volume/file-ID answers are retained behind explicitly controlled unavailable staging-query answers. Windows Core's portable route declares its identity control. Every changed case proves a real owned source rename and the original identity hidden by that control; whole source/target/staging bytes, closure, publication count, job state and byte progress independently verify. No kernel failure or parser exploit is alleged.

Every **6194 affected predecessor outcome/message and 131 exact skips** remains. The full default headless App suite retains every **1652 predecessor result: 1621 passes/31 explicit skips**. Independent postchecks verify **1279 native files**, absent owned processes/temporary roots and stopped Windows listeners. Windows uses measured high RID 12288/session 0; Ubuntu UID 1000 and Mac UID 501 use pinned .NET 10.0.12. Complete logs and owned synthetic files are retained/hash/exclusive-open checked before host restoration.

Earlier d909333 /1572-blob v1/v2 investigations retain twelve Core cases and 1342 affected records/seventeen skips each. Both v2 Windows fixtures fail compilation because their private TempDir helper is missing; no Windows test inventory is invented. Fresh v3 adds that helper and composes with committed I346. One Unix postcheck preparation selects an absent v2 manifest and stops before guest commands; fresh v4 independently checks the actual v3 roots without rerunning product tests. All original adverse records remain.

No workstation UI, physical source, package or persistent setting changes. [I106 current progress](E-I106-current-progress.md) records the wider remaining source-safety work. [Exact committed/native qualification](E-I347-native-qualification.md) and [original four-lane CI](E-CI-backing-visibility-loss.md) now pass at0beaa10. [I348](E-I348-backing-identity-replacement.md) separately addresses a changed known identity at a still-observable path. I106/I110 physical-source HOLD, owner decisions, candidate follow-up and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, raw failures/skips, complete bytes and restoration.

| File | SHA256 |
|---|---|
| `backing-visibility-safety-20261011-v1/independent-final-v1.json` | `1fd13a9c21b6c83a9e293a524194d50f00f4fdf6c9dda9d41a4206c0364fa305` |
| `backing-visibility-safety-20261011-v1/retained-tool-sources-v1.json` | `66488905bec6a2ff7e594add4d22e9df83bfd275a860b2f7804b082189e9d39c` |
