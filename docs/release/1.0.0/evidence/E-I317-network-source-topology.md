# E-I317 — unresolved network backing appears to be an independent recovery disk

2026-10-10 CEST. **Potential High/Critical deleted-data safety; remediated preliminarily.** V09/I106 requires unresolved source/write topology to remain unknown. Linux `BlockDisks` previously returned an NBD/RBD name for a source read while correctly returning unknown for a write. An unresolved userspace/network export can have local backing; the apparent source name and a known local write disk are not evidence of independence. The production comparison could therefore return false from incomplete backing information.

Unchanged **17035d5 product plus only the new test overlay** reproduces **eight unsafe read classifications and eight healthy unknown-write controls** across NBD/RBD names and direct/mapped routes. Owned synthetic sysfs/mount fixtures yield `[nbd0]` or `[rbd0]` against local `[sda]`, with a false overlap comparison. These fixtures establish the classification defect; they do not demonstrate an actual exported backing device, source mutation or lost physical data. The [Linux NBD documentation](https://docs.kernel.org/admin-guide/blockdev/nbd.html), [QEMU NBD tool](https://www.qemu.org/docs/master/tools/qemu-nbd.html) and [block-driver reference](https://www.qemu.org/docs/master/system/qemu-block-drivers.html) describe the relevant export and block-device mechanisms.

The correction returns unknown for both source reads and writes with unresolved NBD/RBD backing, including mapped dependencies. The old existing `nbd0` read expectation is explicitly changed; it is no longer treated as an independent known disk. All **16 targeted controls and 82 affected Core/App recovery tests pass**, preserving the same **29 exact skips** and every **111 predecessor outcome/message/skip**. Existing complete physical/partition/mapped/loop/network-folder controls remain usable at their declared scope.

Ubuntu UID1000 repeats the original **eight failures/eight positives** and then passes all **16 fixed controls**. All **86 staged pins and 386 reused private runtime pins** verify; both owned fixture/temp roots and payload processes are independently absent. The controls use actual production methods over owned synthetic topology; no source device or network server is opened, no package installed and no persistent setting changed. All owned source/controller files at archival time are additionally retained on V:.

Qualification uses declared product/test overlays at 17035d5. [Exact committed/native qualification](E-I317-native-qualification.md) now passes at 59d37cd. Original hosted, actual export/backing relationships, broader visibility/alias/topology races and installed-candidate scope remain separate. I106 remains open. The I106/I110 physical-source HOLD, owner decisions and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact sources, artifacts, commands, original failures/skips and owned restoration.

| File | SHA256 |
|---|---|
| `i317-network-source-topology-20261010-v1/independent-remediation-v1.json` | `55ae4b36479cccd1b020562e0652b4532ff2375e203ffad8c9e697159dedc9b7` |
| `i317-network-source-topology-20261010-v1/i317-discovery-v1.json` | `a92890daf96742d8e01f7e920c201ee0ed1fcc128bca161d5d0dec5d4312af86` |
| `V:/FileCat/artifacts/release-evidence/i317-network-source-topology-20261010-v1/baseline-v1/inputs.json` | `4e4637554dd0a226ed33ce17f3ec9e28fe79690b25d3dda676df359fd1bc1996` |
| `V:/FileCat/artifacts/release-evidence/i317-network-source-topology-20261010-v1/fixed-v1/inputs.json` | `04adc9c312b8701f50886f3f7f9be28e87b14f37a3d10251288d3d12a0961d7c` |
| `i317-network-topology-native-baseline-20261010-v1/linux/transport-final-v1.json` | `e0ccccd206bf8768847926b181657c06bc68bf4c35d82073e61aec6abc0c0eca` |
| `i317-network-topology-native-fixed-20261010-v1/linux/transport-final-v1.json` | `c93d29cab0759de305096e61518ffe2325ab6b0ceb7472c97c5fce0ac906df03` |
| `i317-network-topology-native-baseline-20261010-v1/postcheck-linux-v2/independent-final-v1.json` | `a750480d1b84f1a9ff1197035670db9630af31abd239145b13cb15359fa88c82` |
| `i317-network-topology-native-fixed-20261010-v1/postcheck-linux-v2/independent-final-v1.json` | `d684936f47e47922cd1cbae11c63548e8f47029a67bfcc8b25d04818dcc112f4` |
| `i317-public-20261010-v1/retained-tool-sources-v1.json` | `7a038fffbcba16d37449a4325b4bd9c143bfeae05ccd30b7232e79d270eed5ab` |
