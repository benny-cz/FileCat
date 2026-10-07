# E-I185 — initial checksum input errors reach the dialog

2026-10-07. Original canonical product 25432ce5ac8c21cd069ad2e8a518bb2755e2280d. Medium unusable-error-path defect; must fix under I06/V10/V12/V15. Remediated preliminarily at 7fef87811ac33e6c43a94ebe39680bc2960b49a7; broader/native/candidate scope remains.

When an owned selected file is missing before hashing starts, FileInfo.Length throws before the computation's try/catch/finally. The discarded initial task faults while the actual dialog stays open with blank output and visible progress. Two original adverse controls reproduce this for a single missing input and a missing second input; ordinary one/two-file SHA-256 controls pass. The correction moves the initial size lookup inside the existing error/completion handling. Both failures now show the existing item-no-longer-exists error, hide progress and leave manifest saving disabled; ordinary hashes and manifest-style output stay exact.

Working and fresh locked committed builds pass eighteen affected cases without skips: fourteen prior outcomes plus four additions. The runner initially expected nineteen using display-name matching; one CommandSearch case has file.checksum only in an argument, which VSTest FullyQualifiedName excludes. The controller count failure remains alongside the actual eighteen passes/zero child exit; no product test rerun is used to erase it. Independent Python verifies the owned 65,536-byte content hash 19b5fa6ac4854462f2ab19bb281f22589e88aa559c6efe7cf74e6b7d674ef07e and full one/two-file output. Owned remaining files stay unchanged and fixtures clean up.

Independent seal SHA-256 7126f7a613789b6d10864113a0a7be4d943ee49096794f67b6424a4c357f7655 checks 21 retained files, 423 actual payload references, 1,091 original canonical blobs/modes and 1,095 clean committed blobs/modes plus every archive member. Clean FileCat.dll SHA-256 a67b5beddbb969e0f912553b006f79f7bcf0ea1414de07d3ab0bcb8604e82363. The controller's count guard is not a product failure.

Original push CI [37581221791](https://github.com/benny-cz/FileCat/actions/runs/37581221791), attempt 1 at 7fef878 is sealed green on policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26. Nineteen selected server digests/all members, fourteen full raw inventories, four compiler/tool receipts and 92 locked graphs verify. Each full 552-case App inventory equals the preceding 548 names plus exactly four additions; all 16 new executions pass without skips. Core retains 890 Windows/885 Unix names; previous finite subsets retain their names/outcomes, including all 24 I184 copies. The preceding original macOS checksum-fixture failure is retained separately; a later passing producer does not erase it. ARM64 package version startup/headless drawing/installer compilation pass; tagged/manual packages and draft jobs skip. No shipping artifact or candidate is selected.

This selected error handoff does not qualify algorithm-change/closure cancellation races, later provider failures, native clipboard/dialog workflows, source revision races, reference performance, physical sources, human UX or installed candidate behavior. No persistent machine, physical-source, contract, candidate or publication changes.

Private `FileCatReleaseEvidence/cd185-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | 66ebd00bec86f234ef70de96c9ea3d7717df909612eb34a8ce528d0f0a8b084d |
| baseline-v1/results/baseline.trx | e91f1a4671d29e53d2614b34209004b71d24917458c1b0b1ca7afb9bd1e044a3 |
| working-v2/command.json | 6fed5d0d2f799496355c4a87df25c36b970a4465f7ad363b7f4a7be4d40fcb60 |
| working-v2/results/baseline.trx | 07c1712312f7ceaf8ce89813ea04ed91fd82043318c1d48289a19319c4853f1d |
| independent-working-v3.json | 0d62c7cc75c6087fca4f98a88f7aa5fda7841d19860495d1250d827052d1e78b |
| clean-v4/command.json | 781d92e33ca9e34bda01d39456404924d734cf429abba5f6dfa14f968a3d2a0c |
| clean-v4/results/clean.trx | db83c971d85031d53657e8750e96ace465915cde1f50fc86baa12b31963a1df3 |
| independent-checksum-dialog-v5.json | 7126f7a613789b6d10864113a0a7be4d943ee49096794f67b6424a4c357f7655 |

Private `FileCatReleaseEvidence/ci-37581221791-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 58740c49303cbe4805cb3072d4574b828536a5a163b08bdb8865138beedb8678 |
| independent-draft-guard-ci-v1.json | 8a6b6671e752e39df136a94ef97a9ea6e69cf65f5efe2735907a10dad8e4be1d |
| independent-fixture-ci-v1.json | 43d9b9173cc8ebe27425e4a770814e6f5a9e165d8d2456160d6ab84a36e2f367 |
| independent-i163-ci-cases-v1.json | e24050c32c0c837ea9d0f4f952c4f3893700953b1b0f5d4d21256d15e81e93ac |
| independent-i164-ci-cases-v1.json | d7530a7aaacb67104bd753e4638fb1ba1b5ecd8b7c7fcfa2c9423d6de134d6a8 |
| independent-i165-ci-cases-v1.json | 4fd81ed3a3c4718403d1871c381bae7192b185d2700634dbdd9ee79cbec1afa5 |
| independent-i166-ci-cases-v1.json | ef80217ba06d3409104b1cd7a919a01c89ba818ccb6fd54d70d2a9c447d981e5 |
| independent-i167-ci-cases-v1.json | 7f0ef793def247d093e99e64d941ae60074b60aa011094dddc0e80b02207c85d |
| independent-i168-ci-cases-v1.json | 11911b5aed09e50547b189fd49c2849fb48a69414412c224bfbdb4203d4f4456 |
| independent-i169-ci-cases-v1.json | 13f0f9fc46c4a9f5b68ac4a4738ab82cb7e00c7f2df5bed889993589a2290718 |
| independent-i170-ci-cases-v1.json | 44c3380d70757177cb9b8e6e5ff036889fa0ebe22f8153525d83fa338dac98e2 |
| independent-i171-ci-cases-v1.json | 0b317e3c7af8a3113424d6f80deb7dc126584b58da76dde88b7bc2ef8b06f5aa |
| independent-i172-ci-cases-v1.json | 2ecb524fa96f99e51c58d94771d6cc9cac1e959a1320e82281910e7c03e71553 |
| independent-i173-ci-cases-v1.json | de6be4d5e044d060b6a13647d26fde9afc8d997337832139e6733715c1461675 |
| independent-i174-ci-cases-v1.json | 9da35cba6e0d574e27c4dd59bcd694d6e3c95ee454eb86766b27d190461e0ba1 |
| independent-i175-ci-cases-v1.json | 5324c8134d1eaa077fe6e4aca126525b88e4c0e9ea23a944fb73c5544855c24a |
| independent-i176-ci-cases-v1.json | 709cb38fc52d38f9bb7fa785e7506860f10aa6f046daef02c6f9a1b703055a78 |
| independent-i177-ci-cases-v1.json | 72127fc73916625dca0dc33ff9fe5a65d41198840c05d683f1a56dd2f6c61c6f |
| independent-i178-ci-cases-v1.json | 07e8d14306050af3d698468bac495826e60ba7626ae38b8deb533150b5dc4825 |
| independent-i179-ci-cases-v1.json | c8bc0faadd489d4a40405c5dece9a37281a1ae0cfa2970e0c5c41305a23e95ef |
| independent-i180-ci-cases-v1.json | f3488ae42a157c954f93a94011d84046a65a03303092fd58e3d9eb07033cfe10 |
| independent-i181-ci-cases-v1.json | 5e84e415749992b2af93e7b62844254663e72bf55252165eeb0a8b74ca057ae2 |
| independent-i182-ci-cases-v1.json | 0759ffcc6658a82b1b321cb1dc57a6ec3f389ddde4916d064060a72cd48972e4 |
| independent-i183-ci-cases-v1.json | cbd1fdaf68fb054310e7d4d1d8eeeaa55b463a31f28a9c4b889acc84fb02b248 |
| independent-i184-ci-cases-v1.json | 47847e9fc4ed190c365ae4171752e87f82a13a52e2c244e6a992a44a7fdbd31b |
| independent-i185-ci-cases-v1.json | edf2eb6823f000724c8353e44b83dcc76e3a7e6884240c89216da741d4a26194 |
| independent-producer-policy-ci-v1.json | bbfdd52ba93c83ad5ad350ede8442c54cbdefae52fa23f53be1d7cfe66df3078 |
| independent-restore-ci-v1.json | 5182c7835bfe05713d8cef16b4253e07e90d1787c50250482d01012b4593ed88 |
| independent-separation-ci-v1.json | 1ec9528ef26fe485f06f49831f3f6f8b334048cdba80a5e926784e1380c766f0 |
