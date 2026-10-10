# E-I329 — stale Unix recovery source paths

2026-10-10 CEST. **Potential Critical, deleted-data safety (I106/V09), remediated preliminarily.** Source **5f9128f9f8c1e3a13c48767fb5ca9e37720d5590 /1507 canonical raw blobs**, with declared test/product overlays.

The real UnixDeviceSource holds one descriptor while destination admission classifies its current path. On both Ubuntu and Mac, an owned regular-file image is scanned, then its selected path is replaced, removed or its symlink retargeted. Native stat identities show a different entry or absence, but the old descriptor still reads the original complete bytes. All three cases incorrectly pass admission and copy those bytes. Two positive controls keep the selected entry unchanged or recreate a symlink to that same entry. These controls use recording destination topology and an owned 32 KiB FAT12 image; they do not open a physical device or establish an exploit.

The correction gives native readers an optional device-path guard. UnixDeviceSource captures the held descriptor's device/inode entry and rechecks both that descriptor and the current followed path before the provider consults destination topology. Changed, missing, unverifiable or disposed source entries refuse recovery with a request to select the device again. Readers without this Unix guard keep their existing route; no Windows/helper identity claim is made.

Each platform changes **three original failures/two positives into five passes**, **ten fixed native passes**. Refusals perform zero destination topology calls, write zero bytes and preserve the existing marker; both positive routes deliver all 32 expected bytes. Original held-image hashes remain unchanged. Host Windows explicitly skips the five Unix-only cases. All **928 affected passes/19 exact skips**, every **947 predecessor outcome/message**, including all 21 I328 controls, remain. Independent native restoration rechecks **172 staged/920 runtime files**, UID1000/501, no owned payload processes and no owned temporary trees.

The original fixture treats the normal drive-in-use warning as an assertion error; all five cases on each platform stop before admission. Those four original native inventories stay failed. The corrected fixture records that actual warning. Earlier pre-test builder variable/missing-export failures and their source/empty phases remain; no command receipts are invented for tool-visible exceptions. No native failure is reclassified.

**I106 remains open.** This verifies Unix filesystem entry continuity at admission. It does not prove physical medium identity when device numbers/entries are reused, bind Windows/helper readers, make source/destination checks atomic with writes, or accept physical/installed-candidate behavior. I106/I110 physical-source HOLD and required owner decisions/explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve source, commands, raw failures/skips, complete observed bytes and independent restoration.

| File | SHA256 |
|---|---|
| `i329-held-unix-source-path-20261010-v1/independent-remediation-v1.json` | `fcb7f31cd03797a9a25b973c36461cf9809715a4ced37904291bfe179a4c58b0` |
| `V:/FileCat/artifacts/release-evidence/i329-held-unix-source-path-20261010-v1/fixed-v4/inputs.json` | `c53fa1b3ddf932cacbae1ee5592225dfd04f998dcb5089c650ac8277aaf1de3b` |
| `source-identity-batch-public-20261010-v1/retained-tool-sources-v1.json` | `4cee629168a28af5d41e1e6dad45f763e795140275052937f7c7bf402eb860c5` |
