# E-I178 — cancel content search between actual page calls

2026-10-07. Discovered against complete canonical product 9f5f135c511b6a2bf5c70a24f11ae1fd2446047b; intervening documentation commits leave this implementation unchanged. Medium provider-demand and cancellation-truth defect; must fix under I06/V12/V13. Remediated preliminarily at 4f25c5802a70f9f705c417d7e58878e9381e725d. Broader consumer/worker/frame lifetimes, native interaction and candidate qualification remain open.

## Failure and correction

Forward byte, backward byte and text searches check cancellation before a one-megabyte blocking reader call. That call can load multiple 64 KiB pages without observing the search token. In each owned 256 KiB control, cancellation while the first actual source read is held still allows three more source reads and returns a match at byte 64.

A cancellation-aware PagedReader overload now checks the token before each page call and immediately after it returns; all three search modes pass their token. The existing two-argument public reader API remains available. An active synchronous source read returns normally before cancellation is reported; this does not claim interrupting a source mid-call or change the search algorithm.

## Controlled validation and provenance

Six durable Core controls use an owned real file: canceled and ordinary positive searches in each of the three modes. Original source records three adverse failures/three positive passes. Working and fresh locked committed runs each pass all 46 affected content/search/page-reader controls without skips. Both working and fresh App runs pass the same 54 affected viewer/picture/admission/lifetime cases without skips. Corrected cancellation makes exactly one actual source read, reports OperationCanceledException and returns no match; ordinary searches retain four reads and the exact match at byte 64. Structured observations retain unchanged file hashes, no active reads at completion and no source disposal during a read; owned temporary fixtures are removed.

The independent seal verifies 36 retained files, 528 actual test payload files, all 1,078 original raw-source overlays and all 1,080 fresh committed Git blobs/modes plus their archive. Working production and test overlays differ from canonical bytes only by Git CRLF normalization. Clean FileCat.dll SHA-256 722a5a9a6c52ab8b38af2e987b364e0ec5b6c85f43c79c0a8dcd62fc960dc196; clean FileCat.Core.dll SHA-256 c3b1d3778ee5128c5e3fe3afe92ed941171c3190e5cc0e3aa496aa1b545bb2fc.

Original push CI [37568308393](https://github.com/benny-cz/FileCat/actions/runs/37568308393), attempt 1 at 4f25c58, is sealed green on policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26. All nineteen server digests/all members, fourteen complete TRX inventories, four compiler/tool receipts and 92 locked graphs verify. Full Core inventories contain 890 Windows/885 Unix names, equal to the prior 884/879 plus exactly six additions. All 24 distinct new executions pass without skips, with the required one-read cancellation/four-read positive observations. Each full 509-case App inventory retains its prior names and outcomes; I163–I177 subsets retain exact outcomes and explicit skips. ARM64 package version startup, headless drawing and installer compilation pass; tagged/manual package and draft jobs skip. No selected shipping artifact or candidate is produced. Component controls do not establish native desktop, human UX, physical-source, installed-candidate or reference-performance qualification. Prior rebuilt-artifact evidence keeps its own exact producer. No Mac/VM settings, physical-source, contract, candidate or publication changes occur.

Private `FileCatReleaseEvidence/content-search-page-stop-20261007-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | 842c8f0be34890b293c09a53f4f52efed54de93908f196478702ee84f848bdba |
| baseline-v1/results/baseline.trx | 1eb471fc2da03dd953c8da8f695b446369bc3d0b155d5b82b20b5fb20a3d9da3 |
| working-v2/command.json | 507053295387d86aafd6af0b6c3b35365f93751e1d99b42eaa8f5de42b38d454 |
| working-v2/results/baseline.trx | 56c24df317a594217d2726faf7545655fb6cfec2bcb7a9247a9ffd04946255e0 |
| independent-working-v3.json | ffc5d3f186cf7f9900cf232676060422e8a3e8f8a78d1618a21e222a4161c901 |
| independent-app-working-v5.json | 5b8096f8235a38678434cf794d24b1f5bac83bb7d79910d275a63e5ebb3f92d0 |
| independent-content-search-v7.json | 0d0524288d07afdb74ece5ca5fef61531add3eabb2284507a9b9d50c8d213cfa |

Private `FileCatReleaseEvidence/cs178-app-v4`:

| Retained path | SHA-256 |
|---|---|
| working-v4/command.json | e7a29d56e96daeff1f696136bc0a88277e19714fe6f69378107054c9425f3d66 |
| working-v4/results/baseline.trx | 821c9d04b053dfcb08ba268d4b8b8c0a9163bf3b51506bc645a526ebe7696130 |

Private `FileCatReleaseEvidence/cs178-clean-v6`:

| Retained path | SHA-256 |
|---|---|
| clean-v6/core/command.json | 42df5f8f14bae558bc7360c9a164ea08d41be609470dd685c69d9aaf16c68dc9 |
| clean-v6/core/results/clean.trx | 0458cdeee7010cd0cbc51791ae3530a1e54d1f163fc1c09642aa7f1ade4a2d52 |
| clean-v6/app/command.json | 2ff1b3b754255ce6e7900dac02a3c424c1baeb07891f516bf626f7e2f6fffe18 |
| clean-v6/app/results/clean.trx | 518432e9284c72523a84c3ed6bd8ad8bd4dfa2d551924bf3a0c88a6e5ebbefde |


Private `FileCatReleaseEvidence/ci-37568308393-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | a5edc0f0533c5976a02cd0142f71f824e7622c565895a8c2137e6ac4519fbe28 |
| independent-draft-guard-ci-v1.json | 50725272e4e8fb7b27b333af68b8c67fa71253d59dac05e8bea83814d22e450d |
| independent-fixture-ci-v1.json | 86ece9d2e0c659c877b666f1499a0f2a7de0d7a0fba65e7214b21249f71e77a6 |
| independent-i163-ci-cases-v1.json | aa8661ac4cebb7f4e8553cddc2e5a89335de1ccaaafb0a2d7a41718e0658403d |
| independent-i164-ci-cases-v1.json | 4797cd7622d9746a174ccbe38eff17ab0adea6bc111b0703053069c6c5c462eb |
| independent-i165-ci-cases-v1.json | 4bcfa70457c7c488a754cc3e959d48a779ecf8bd0b3d4d21525ebd7d8bdd1813 |
| independent-i166-ci-cases-v1.json | f4098cbbdbf0ab4a90213c2265f7a72a5dd2bf7ac94a9344f055fe2482b2eabb |
| independent-i167-ci-cases-v1.json | 0d1d41f5d56fe750089d4ac403053c09f2a36a3d44deb47b052a44e62e0c88f4 |
| independent-i168-ci-cases-v1.json | a5c95de9b132e60d9c02967e23a0c7fcd30152c523bd9a3d723b21a383ffb54f |
| independent-i169-ci-cases-v1.json | aa09858641275d34f16706f3469893a42835eafcd08623583657b76f70047164 |
| independent-i170-ci-cases-v1.json | 6cd7708cf3e33dcd3f0a746594af1dbdb49a03564009c68da8118de798ba2367 |
| independent-i171-ci-cases-v1.json | 3e11d57c909df4a6a509018653d35817d966e4ed960aa32ce42a1fc4b96858d1 |
| independent-i172-ci-cases-v1.json | e58903af7931f1ca107fdcf015c594229749df59e0fed025609aa5f4fe072b12 |
| independent-i173-ci-cases-v1.json | f31b85b00599700a0f45feab55843ad7af52d89a663fd8ff09c5514a46fd2674 |
| independent-i174-ci-cases-v1.json | 965e82e172357c93634f39d2f9d29ad39ff637972e66812504430477a47f5d81 |
| independent-i175-ci-cases-v1.json | 839a9fcb52d2968f56423e00a6a3456ba43b83cf69d24441318e612ec63275ce |
| independent-i176-ci-cases-v1.json | f903f1ca09564186c84ee9c3acdc088cb6b4c380e305f60746aed16981fc3bed |
| independent-i177-ci-cases-v1.json | 81273f1eaf503588d104301a9d17a9044d0d45a2d3abebd04105fe10014cfdc0 |
| independent-i178-ci-cases-v1.json | c67d6afca156349c54a8752b5af15b5733a8642b184fb113f5e8784d96bdc50e |
| independent-producer-policy-ci-v1.json | 808589bedbef772583a4a4cf9b8e32e2a7b6bc08eb42aa96c2071f24b63075be |
| independent-restore-ci-v1.json | 5fc6724c5c3578591f5371bb178276604e5526757df234d9076982847a816ee3 |
| independent-separation-ci-v1.json | 29025b09b036e1506e91a30a2c1fcf61ce69278504d0711a4dab1c10e8fcfe47 |
