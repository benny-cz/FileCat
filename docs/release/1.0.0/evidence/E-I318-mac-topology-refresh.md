# E-I318 — a replaced Mac mount reuses the previous recovery disk answer

2026-10-10 CEST. **Potential Critical deleted-data safety; remediated preliminarily.** Unchanged **59d37cd product plus the new regression test** proves the twenty-second Mac topology cache can report a write folder as independent after its mount is replaced by the selected source disk. This is an actual native owned-image reproduction, not a fabricated topology response.

The ordinary-user Mac test creates two 32 MiB HFS+ images. Source A stays attached; folder M initially belongs to B and primes its write classification. B is detached and A mounted at M in **379 ms**. Independent `diskutil info` reports M on A, but FileCat still returns B plus disk0 and `SharesDisk` is false. A complete **64 KiB known file** is written/read through ordinary filesystem APIs. Full backing-image hashes, taken while detached, change; mount/metadata writes also occur, so that hash change is not attributed only to the marker. FileCat opens no device and no lost physical data is claimed.

The correction removes the stale device/mount-point answer cache and queries the current backing disks whenever admission is checked. The **same permanent native regression passes**: source A is included, B excluded, overlap true, all known bytes exact. This adds bounded diskutil queries to admission; it is not reference-machine performance acceptance or proof against every concurrent topology race.

All **98 affected Core/App recovery tests pass**, preserving every **127 predecessor outcome/message** and the same **29 exact skips**. Both native artifacts verify all **86 staged and 534 reused runtime pins** before/after; an independent later postcheck rehashes both payloads/runtime sets and finds no owned image attached, fixture/temp root or payload process. No sudo, account, power, system-setting or UI changes are used.

Original fixture attempts remain failed: v1 uses an invalid blank-image type; v2 uses a source-only format option; v3/v4 try to hash an image while macOS holds it exclusively. All leave no owned image or fixture. V5 is the first valid product discovery; v6 uses the final permanent test without an unrelated mount-speed assertion. Its actual 379 ms switch establishes the original cache reproduction without imposing a release timing requirement. A separate independent postcheck first exceeds the Windows command-line limit locally, before SSH; v2 supplies the script through stdin and verifies restoration. No adverse record is relabelled.

Qualification uses declared product/test overlays at 59d37cd. Exact committed/original hosted follow-up, wider topology/physical-source attribution and candidate scope remain. I106 is open and the physical-source HOLD and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact sources, artifacts, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i318-mac-topology-cache-20261010-v1/independent-remediation-v1.json` | `50182b18cd5d8e23992a94afe461dcbdf4751a188500a9895ba5b7dd299eb3f6` |
| `V:/FileCat/artifacts/release-evidence/i318-mac-topology-cache-20261010-v1/baseline-v6/inputs.json` | `8c5ed8792d525054fbf7bbede1e60cd44cf3a81b204679a67608ef2efe6fc98a` |
| `V:/FileCat/artifacts/release-evidence/i318-mac-topology-cache-20261010-v1/fixed-v6/inputs.json` | `2aacdd30839e629612dd47722801b473270742bfc3a90cdebee69350d4965df6` |
| `i318-mac-topology-cache-20261010-v1/baseline-macos-v6/transport-final-v1.json` | `150e07312ec65357b3e0f108b9cc7d52b36ddfc473bc9f75014317ec4f27b32e` |
| `i318-mac-topology-cache-20261010-v1/fixed-macos-v6/transport-final-v1.json` | `2f7b6263c4b5ad8304c90c779e99f3fb7dafbce5d687e79f77091978114747f8` |
| `i318-mac-topology-cache-20261010-v1/postcheck-v2/independent-final-v1.json` | `b9aa90b16d6900709f229d69335835126723a90cc3dce8de79f3334390ed9d85` |
| `V:/FileCat/artifacts/release-evidence/i318-mac-topology-cache-20261010-v1/baseline-v5/macos-v1/retrieved/topology.xml` | `10d517dd242ce6316c90eecb5147b83bdb673fd5c214702200fd4b2cf4f1fa15` |
| `i318-public-20261010-v1/retained-tool-sources-v1.json` | `2eee4ab9b14108df5caa80cacd68f059f4d67558b998fd20f9821547ab788f2f` |
