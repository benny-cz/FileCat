# E-I318-EXACT — committed Mac recovery topology refresh

2026-10-10 CEST. Exact **55737cb2306d713c02e36979471be4a6f9bddfd2**, **1465 canonical raw Git blobs**, no product/test overlays. All **98 affected recovery tests pass**, with every **127 predecessor outcome/message** and the same **29 exact affected skips** retained. The native-only regression is explicitly skipped on the Windows build host; this extra skip is recorded separately and supplies no Mac acceptance.

The committed Core artifact passes the permanent mount-replacement regression on macOS27.0.1/M1/UID501. Both 32 MiB images are owned fixtures. Source A stays attached while folder M changes from B to A; the current classification includes A, excludes B and reports overlap. All **64 KiB known file bytes** compare exactly. Backing-image hashes are taken while detached and are not attributed only to the marker. No FileCat device reader opens a source.

All **43 staged and 267 reused private runtime files** verify before/after. A separate later postcheck rehashes them and independently finds no owned image attached, payload process, fixture or temporary root. No sudo, UI, account, power or persistent system changes. Owned retained test payload/results remain available for the campaign's final restoration.

Original push **38066949468 attempt1** at 55737cb is being collected in the background; complete hosted qualification is not claimed here. Wider concurrent topology races, physical-source attribution and installed-candidate scope remain. Twenty broader unresolved issues, I106/I110 physical-source HOLD and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact sources, artifacts, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i318-qualification-20261010-v1/independent-exact-final-v1.json` | `e90c7826ed5aa4d76f9364e6c80464d0e100f7d94170f00fd66f6040d603e50e` |
| `V:/FileCat/artifacts/release-evidence/i318-qualification-20261010-v1/exact-v1/inputs.json` | `516baa753865c5270247c85b87579535b24c9b16721f766814f5c69ba881faac` |
| `i318-qualification-20261010-v1/exact-build-tests-v1.json` | `885b71190a54d661c5ad410ab6c7ddb303cb7ade44194fb49fcd159502fb0b79` |
| `i318-qualification-20261010-v1/exact-macos-v1/transport-final-v1.json` | `eb4aaf77661ebc734ad74ab89e7e20a663aa31dce8db6899c114007816e3995d` |
| `i318-qualification-20261010-v1/postcheck-v1/independent-final-v1.json` | `560a2d52eb53909b24b2b9cd6b5da5f20bbb4f77294f7c3c41d86b709f63b330` |
| `i318-public-20261010-v1/i318-main-push-v1.json` | `d1202ab4a0452e04fa15ac9ee2aaf038081f38f3f50448f2dde6859ef66aa6b7` |
| `i317-i318-followup-public-20261010-v1/retained-tool-sources-v1.json` | `a7dd9b0d5fdbeffee36eaf19665827e5e8420d2eca0cb23a1a2325ca6646f049` |
