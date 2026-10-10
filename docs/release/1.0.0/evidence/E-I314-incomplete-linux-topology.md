# E-I314 — incomplete Linux block dependencies remain unknown

2026-10-10 CEST. **Potential High/Critical deleted-data safety; remediated preliminarily.** V09/I106 requires incomplete source/write topology to remain unknown. `LinuxTopology.BlockDisks` instead treated a missing `slaves` dependency directory as an empty, known leaf. An owned synthetic mapped device that previously resolved to `sda` became the apparently independent `dm-0` when that directory disappeared. This could incorrectly distinguish source and write disks. No actual device mutation or physical-source loss is demonstrated.

An ordinary Ubuntu user runs the unchanged **42e5185 product** against owned synthetic sysfs directories. **Six incomplete cases fail and four complete controls pass**, with raw known expected/actual values and fixture removal. No device node is opened. The correction returns unknown when the dependency directory is unavailable; a known empty directory still identifies a leaf. Eight permanent read/write/folder/disappearance/invalid-directory/healthy regressions retain **seven failures and one positive before the product fix**, then all **eight pass**. The existing synthetic fixture now includes normal leaf dependency directories.

Validation uses explicitly declared **42e5185 product/test overlays**, not a committed-artifact claim. **74 affected tests pass and all 29 exact predecessor skips remain** across the Core and App recovery selections. In Ubuntu, all **ten topology controls and eight permanent regressions pass**. The native sysfs snapshot independently checks 29 entries: two partitions and 27 entries with dependency directories; it is a finite observation, not a universal topology/race guarantee.

Independent postchecks rehash **573 actually available staged files** and all **193 private runtime files**, check no owned payload process, and verify all ten owned fixture/temp paths absent. One nested fixture omitted from the first refused package is explicitly unavailable, never counted as verified. Earlier package/dependency/observer-root and postcheck reader refusals, the first native attempt's eight permanent passes, and the original product failures remain. Only verified empty owned temporary trees are removed. No system setting, runtime installation, host UI or physical source changes occur.

I106 remains open for broader process visibility, aliases and topology/identity/lifetime races. I106/I110 physical-source HOLD remains. Exact committed/original hosted and installed-candidate qualification must use their own identities. No risk acceptance, contract freeze, candidate or human GO is supplied.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested records preserve source, commands, raw failures/skips, native evidence and owned restoration.

| File | SHA256 |
|---|---|
| `i314-linux-topology-20261010-v1/independent-remediation-v1.json` | `7afb0c37caf8653d92df6a6168c094ab72e4cb9371af2a85fc9ad9c236e18e04` |
| `i106-linux-topology-20261010-v3/i314-discovery-v1.json` | `9d2a5878e021626ee8894741e43df2f5fec3ca1d5a86f270a429e49b3d2bd197` |
| `i106-linux-topology-20261010-v3/linux/transport-final-v1.json` | `69123de2dcd7a77b7ed0f5723885f4467cdc30b5a6c7cc5e84b2bb2f897af818` |
| `i106-linux-topology-20261010-v1/packaging-refusal-v1.json` | `2f30f49964c613367d56034f51e7fc0bb61e350adf9d4bf235bc3d8982719a28` |
| `i106-linux-topology-20261010-v2/linux/transport-final-v1.json` | `e54fca88269abf7424284b829d29c2bb4b36c2107dfe3d7fe48d7039e41bc8e3` |
| `i314-linux-topology-native-20261010-v1/native-refusal-v1.json` | `0147eea39fc0a564c6ffe2eaac7c7de89a3cba676dbaccbef37e06eaf005cffd` |
| `i314-linux-topology-native-20261010-v2/linux/transport-final-v1.json` | `0b940a5ed8e5165d9442944eff6502c71291c2d22339504ed9ac00d9cae31052` |
| `i314-linux-topology-postchecks-20261010-v3/linux/transport-final-v1.json` | `4c19dbf9a6ee85167986dbb8db44a300bc8a5b47fa839e495e97919901f68479` |
| `i314-linux-topology-20261010-v1/baseline-build-tests-v1.json` | `0fa1127e67d0c929be6e276c4c892e06f6effc179fc53127609abcf3a196f14c` |
| `i314-linux-topology-20261010-v1/fixed-build-tests-v1.json` | `1a1348ef9785684024c0e6c38af8568da9350dcced2a93f720142b00e3e50194` |
| `V:/FileCat/artifacts/release-evidence/i314-linux-topology-20261010-v1/fixed-v1/inputs.json` | `138c0b79e21ce84e4282c5d36d48f041d1bf099a0bc6f0f84cb16c33dab04c0c` |
