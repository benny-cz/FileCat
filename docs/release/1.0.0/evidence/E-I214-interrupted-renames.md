# I214 — interrupted temporary items change during rename approval

Exact product `ce5b119004e6adbb22bdcf9375cea4f570ee6d7f`. Preliminary owned-file/Core and actual headless cleanup-dialog scope: **53 additions**, **1658 canonical expanded passes/67 existing explicit skips**, full App **1106/23 explicit skips**. Corrected original CI 37729215497 passes all four lanes and **212 addition executions**; no final candidate exists.

## Proved defect and resulting behavior

Recovery previously collected temporary names using existence checks, then moved whatever occupied each name after confirmation. On unchanged 4993607, 18 of the first 21 controls fail: 15 move changed/replacement files, changed descendants, changed kinds or injected unavailable identity/path/link versions; all 18 close the journal, including missing temporary items and both occupied destination names. Three ordinary/approval-cancel controls pass. Synthetic identity/path/error replies are distinguished from actual owned bytes, metadata, kind and descendant mutations.

The intermediate 30-case App baseline retains 29 failures and one queued-close positive. Nineteen failures observe changed/unresolved/mixed-item journal outcomes; one observes the temporary item moved during scheduler shutdown without a precise before/after end-boundary attribution. Nine encounter absent active-review or worker checkpoints: the six held metadata/identity/path callbacks never enter, and three otherwise-correct approval/cancel controls lack the new review-worker checkpoint. Those nine are not invented mutation failures. The final corrected-fixture baseline retains 28 failures and 2 positives with the explicit classification in the independent proof. All original raw observations, assertion errors and payloads remain.

Recovery now captures each ordinary item's metadata, available identity/resolved path and complete content hash before approval. Directory reviews include descendants and aggregate bytes with a finite 1,000-item/64 MiB budget and depth bound; linked, incomplete, unavailable or over-budget items remain unreviewed. Parent identity/path/link state is compared, without treating unrelated parent modification times as approval invalidation. Every destination attempt rechecks the approved item/tree and parent version, so a failed first move cannot silently restore changed bytes to the original name. Moves never replace occupants. Successful original-name fallback remains reported.

Cleanup retains the old journal while any rename is unreviewed, changed, missing, over-limit or otherwise unresolved, including mixed successful/unreviewed batches. Active calls stay owned until they return; accepted close and scheduler stop prevent subsequent mutation. The existing immediate Core helper also performs bounded review; UI callers retain the pre-dialog review explicitly.

## Validation and limits

Working-v2 passes 28 Core checks and 30 App checks. The final batch adds a failed-first-move content-change control and validates the legacy bulk-rename helper. Working-v4 passes the expanded Core/Remote/App selections but retains one full-App close-oracle failure; clean-v5 retains the same wrong oracle in its affected selection. Those records show the approved move already recorded at accepted close, with no calls afterward. Placement saving precedes OnClosed and can admit a watchdog worker during the save; the corrected fixture checks exact state and call counts at accepted close/stop without requiring rollback of an earlier reviewed move. Product source is unchanged between 7cd5ab0 and the ce5b119 fixture correction. Working-v6 passes 59 focused checks; canonical clean-v8 independently passes the complete Core suite, maintained Remote selection, expanded affected App selection and full App suite. A rejected HEAD-alias preflight executed no tests and remains retained. All previous 2516b14 test names, outcomes and skips are preserved. Twenty-three new Core controls cover exact files/trees, fallback, byte/item limits, cancellation, identity/path/link/access refusal, parent aliases, occupied names and revalidation between attempts; thirty App controls exercise the actual cleanup confirmation, changed bytes/kinds/descendants, missing items, mixed batches and active/queued demand end.

Original CI 37728821237 at 7cd5ab0 fails all four lanes on the same close oracle: 17 calls at accepted close and 17 afterward, with the reviewed item already at its target. It retains 208 passing addition executions and four failing ones, plus one existing Windows x64 comparison-revision timeout at its armed Entered checkpoint. That timeout has no main observation; its cause is not inferred or claimed fixed. Independent readers verify all 18 available server digests/every member, 14 full raw inventories, four build receipts and 92 locked graphs. ARM64 screenshot/Inno artifacts are absent because their producer steps were skipped. The missing-artifact collector guard, guessed-step preflight failure and additional-outcome guard remain; no unavailable evidence is invented.

Corrected original CI 37729215497 at ce5b119 passes every lane and all 212 additions. Independent readers verify 20 server digests/every member, 14 full inventories, four build receipts, 92 locked graphs, nine owned mirror and five launcher controls. All previous names/outcomes/skips are retained except five explicit Failed-to-Passed transitions: four corrected close oracles and the existing comparison case. The latter passes on this producer without a comparison-source change, so its cause still needs a forced diagnostic repeat. Static inspection identifies a possible pending unarmed activation check across the fixture's arm boundary; this remains a hypothesis and is queued next. Packages/draft publication are skipped as normal on main. No original failure is replaced with a rerun.

App fixtures use owned journals derived from completed copies with their end records removed and explicit rename intents. They exercise the real cleanup method and confirmation controls; they do not claim a native process crash or desktop interaction. Identity/path/unavailable controls are explicitly synthetic; byte/tree mutations use actual owned files. This is a conservative path/content review, not an atomic native handle-based rename or filesystem snapshot. There remains a check-to-move interval and separate cross-device/remote, link, copy/move root-content, native alias, sharing/access, wider admission and candidate qualification work. No physical-source testing or VM/Mac setup changed; the physical HOLD and explicit human GO requirement remain.

## Provenance

Private `FileCatReleaseEvidence/rn214-v1`:

| File | SHA-256 |
|---|---|
| baseline-v1/command.json | d16ad84adca5b0ba537d07aa432edfc485b8a8767d92338dae8ddbb8ccd07cb4 |
| baseline-v1/source.zip | fde526ee25bfd696c509ff8c9e45ec22f47bdc60ac1975338acdb106a9b13148 |
| baseline-v1/app-stderr.txt | 57ffc25b5805187f0cd1cd1ad47484696b7d97ec28406f3234573e748563600b |
| baseline-v1/app-stdout.txt | 7d091272c97d7b2c4cf9d7b0b09c42ed24dbfdd2317c47f552e8fcb8bcda3a43 |
| baseline-v1/results/app.trx | 4912026ea4e4b27becdeb7ffe0219721c3cd119d17a6eeecf2e7e1ea1c715385 |
| working-v2/command.json | 515d72c711f2e9125b642a086158c1db97fb35dd071a53e0415f2fe538a111a9 |
| working-v2/source.zip | c0252809ddde0c945f464e0605d41a8cc2593fc8256ec7bde70a9fd62b237067 |
| working-v2/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v2/app-stdout.txt | 795872ce9e40d64c5386059e378aafb734115b78999b6d9105cc6ba7e893af79 |
| working-v2/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v2/core-stdout.txt | cfe5ac42b9d193d9aec6f51c293289f32a1811342e35285f71f752d88d92249e |
| working-v2/results/app.trx | 981eb2b1823e58dce215c92e7a3e20eb3257a39e88e2149aae9f6c85eeb1561d |
| working-v2/results/core.trx | cfc2dc1d5b6779002efaee9b59370f22320fe7c4e2e927d08289253ba9507758 |
| baseline-v3/command.json | 30288222998556dbd16ca26126ad625c0ecda3cd2b7d00a507119d7895a1abcd |
| baseline-v3/source.zip | 10752e1e3653c129dfe84d57a19f1d52986ffd8feb1118891cc8dbda679cb1f5 |
| baseline-v3/app-stderr.txt | a3dc887a13f4bca7d610e12373ddea5a74851326cee4e946f716757322262efe |
| baseline-v3/app-stdout.txt | 8eb4148a1739623454294cd7d5fb310641f9f09c2d537e192682881318797b5f |
| baseline-v3/results/app.trx | 2fdcda7181a033bfc71f04e8164b28f68ecb8c4261a8e1d1ede34feb5c019175 |
| working-v4/command.json | 5de9ec967dcc27da1f0569126a0f89cf3b8151604e44c96f6f72feef83ede2e9 |
| working-v4/source.zip | 1a84004fec164eee91929bdcf4a0fd2328d273e58c5a3133260b412fc7eb7037 |
| working-v4/app-full-stderr.txt | 44bab557f138ee94ecc730676e58a5c439899ebc9bcd8b738eedb42f7fab93a5 |
| working-v4/app-full-stdout.txt | 6742cf1e9e424aa6f080be62298de01f706b722be47b607c66ffaf79543dd32d |
| working-v4/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/app-stdout.txt | 67ddec3cba3bfa24c31ff6a72985d5e514a70ff3ec52e7e5c2dcc36f0939a4ba |
| working-v4/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/core-stdout.txt | 25c84b9b74eac70aba554ff6c2ab0110ba899f6ed19777a4b11dbb85995e6cb7 |
| working-v4/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/remote-stdout.txt | 24eda597a2048be437977c9be94df31b53a2f84cb39c382f22e654e500bf56f1 |
| working-v4/results/app-full.trx | 091b1c9f88c2a160f268c45cd511ac3acc60c253d993012fa14d80e833a2f4b2 |
| working-v4/results/app.trx | 6a4629ffe2863a035ff288d87a5b48a7f5c23d3339df6678587e8ab159c9016a |
| working-v4/results/core.trx | f6528849e8e52fe21a92a537beaa28fc21d79babf660fc5a372df0be90700cfc |
| working-v4/results/remote.trx | 339ac12f318009ed0a31a33d7347777c4049504cd24e04ce921914e89d6042e0 |
| clean-v5/command.json | b1b274028e45a3316ff7bf203a4e43c8638955b9ec9fabd96e596444148f1578 |
| clean-v5/source.zip | 999daf4b453ff61d104c5438231d780ee297cc5cc402c71d9793697406df851f |
| clean-v5/app-full-stderr.txt | 7e40f7cc052b5f7f8aed0e1b5bb1cc76759d3786ee01d290e722051cf68efa7f |
| clean-v5/app-full-stdout.txt | 07419884c35bf6349bda9b4081bb9ce5e88dc9708aa1d3c4163b185f8adcb556 |
| clean-v5/app-stderr.txt | c372b1803220b8895c20741f762842da166d6d20c2dfeca841979d41f7a8cc59 |
| clean-v5/app-stdout.txt | ce882653ebdb4f56c911bde2835771b0f1a81978eadafceb8b87c79f8a6cf439 |
| clean-v5/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v5/core-stdout.txt | 2337473404fb863aa57a871d8264dfc880c182c056e7e96f10cb8826ae6c4e6b |
| clean-v5/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v5/remote-stdout.txt | 1cb6620f4821741ff3ee4cbb581a8a6e1bb9623348746adb2a1d78600a73d5f1 |
| clean-v5/results/app-full.trx | 0ec1c6a2b6f81d8aec77f5ffaea65e5cad377c01c8c33cb33f07b7a837a95d5a |
| clean-v5/results/app.trx | d2a22b9f2f9761b823c396b4f92129b2acb2ecdaccca511dceac42c3fcea87f5 |
| clean-v5/results/core.trx | 9ddc1ad9e8f5d7323c44d744e17ee467806947f3dbddb2ab0b975cb38e42209a |
| clean-v5/results/remote.trx | 91c97a0392e65fe57e3beb01147e37b4886fb479a40526ae5be831e05d41a5de |
| working-v6/command.json | e299d1dbb97360d6d8bc771096f44b792f2a50b82ef3764c3af2d73bf8b7acb0 |
| working-v6/source.zip | eb98478736e7315a25ec44d5644086f656a2248531b5bf07e11e5d9770ad920b |
| working-v6/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/app-stdout.txt | 5d83e6a8513945bd94eebb68fa3ec8e46dbebc40833eea07f32c20d325ded037 |
| working-v6/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/core-stdout.txt | 33e69353a2e5b741503b9eed1d7825ad9fde3afbcc56c2e7edb541ff891b0981 |
| working-v6/results/app.trx | b43b61bbb36d20c46433ccd54e9495b16dc04abba027f99374e92eaac643c966 |
| working-v6/results/core.trx | 00411a1471246ed7c20a9b924bd6409c2728ad8b79f5048e81ac9da0573c81a2 |
| clean-v8/command.json | ece4bcca8aec2983f723273109d37102355abc7f300ac31779b60a0736ff2782 |
| clean-v8/source.zip | cc36059338592dbdc34af22c8ca5da460b848138abd15f20b8afcc91bc243b26 |
| clean-v8/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/app-full-stdout.txt | f8146801a575335bb9c6da6ee7dfd9b4d3526628259643e5dad1236000d0624d |
| clean-v8/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/app-stdout.txt | 37b4630a07b647f0add7ce0dcd81a50f8caa2dfa5882487b9f2af8e8c5d96696 |
| clean-v8/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/core-stdout.txt | 4286a3b451fc09721af53489e4d75c74d3dceb1daee9576877175ae37bb8729b |
| clean-v8/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/remote-stdout.txt | 97d4410b226465a36b7e14b83bf9510231baef75494c2966a9c5b6239089f0c0 |
| clean-v8/results/app-full.trx | e8c630179d55ef187a71013ca0162d38a1166e18d6108614bc2489ec40c63e25 |
| clean-v8/results/app.trx | 25e6d3a3182ea728e0429ad275d58cc47d56f54736dfd6f185b89248ff3f4592 |
| clean-v8/results/core.trx | 665aa57cfdd262fd600ab627d5c2bc0ce0fbf91f7b6812fad2543aade0d9f59f |
| clean-v8/results/remote.trx | 429c90bb5826a9c5f92d489fd31a8f6e682ede8558f508469793bbacd75667cd |
| baseline-v9/command.json | 1408ae830cc1e1267b32db0327b4b8dba1a88d93a9ac0b427122aa22e5ca8982 |
| baseline-v9/source.zip | 06efee226b0ed17d19db150d9f9ff528c6e4ce3b2b6c1d7e84627031335f9a41 |
| baseline-v9/app-stderr.txt | 1740a4239eb7e9cd11ad3002534b33440f57730339bab6d6be12e585baddd7a7 |
| baseline-v9/app-stdout.txt | f34dd7b69af2f5fb8ceda894faefd1131d4a9052d37cdefc7d5f13057a5bbbcb |
| baseline-v9/results/app.trx | 4257fa698bdbdc57efb73d6efb58ee81591b1c8a54b5f9bc1c9eaa4a8208e406 |
| run-rename-v1.py | 57885d8c60bc7da77e33638d4a92b9877d4938c15d07091f8adea4c5a700bcd0 |
| run-rename-v2.py | 12124d70bda0348aaeba95927705dd23ef12701234f9139c0149fe745f0733e9 |
| run-rename-v3.py | 3da75c3227c87f2e59f4256593314fd262168e36b1ae1d0ed8c8caedda75cc75 |
| run-rename-v4.py | 4f586fbfbb3204dc893b08f5aa056c53a9d3a668c46ad4f9b6c902e2162b2430 |
| prepare-recipe-v1.py | a1483f84b3f963f28253eebb8fa083ec60d7bae62c7dd5d7789df3e4dc08efb7 |
| prepare-recipe-v2.py | 7833620a7717f0eeedf2d6cdb7264b6753b541a6eb3e9978f6b787314740be96 |
| prepare-broad-v1.py | 5baa41c28f2c29a877fca1eeec067fe71c969fc2f450fe24407f7dd01a873061 |
| prepare-final-recipes-v1.py | 067893e08648a8221128621a3fc33157a58dbf97eb26a75a1bbe296cb51afc90 |
| preflight-clean-v7-command.json | a00f86bc4fbacc29f4f6f52c6071dd8e3cfa4a90ccf9af5f6f21bf7df2b471ee |
| seal-rename-v1.py | 482e516f1e28d630464f044da00f54b88ed10a8eb4b4e7b100be94541b7262da |
| independent-rename-clean-v1.json | 3a5dfd70420c3ada8bff75750c1b14c7e1dac0c5bf9a78094a28a9f6a10a3794 |
| update-documents-v1.py | 078558f249690d4d0a73c8f23457517d0149e3ab00041fa079ff530b0a2bd3f8 |
| document-transitions-v1.json | 707732e48307b06d26d75d6ebdd5a1c3bfabfef1595e9e10b3af8270c5d7e0fe |
| query-product-ci-v1.py | 6315b7149997763e426bde4a7fe84a00987e3a647aa78330b3199a6bad8e8a81 |
| product-ci-query-v1/observation.json | a5203638686262c73198b7c94ac17076b149b4df5f93d4763ea058dbe1b117ba |
| query-product-ci-v2.py | a9c41037c6a3e7382e5c99504043403773f1255fad3e1210747b294896dea284 |
| product-ci-query-v2/observation.json | cf41a4e145d1ca2da90f22ba8ffe5f76ca5f98396df0b4e9c0bc28048d8f6df2 |
| query-both-ci-v1.py | 08d73737a2d8224cb4907e1c5b863c3ade972955c2b50031aa38c39d938eeea9 |
| both-ci-query-v1/observation.json | 9117b8118f0a49ceba7aa6a8913acd365b794432d2c83379ba4ced8eaa39bb07 |
| collect-original-failure-v1.py | d20e4fc088c2bdf2d6b170dd09a46017a2fae82e0a1ccd1998b379781c85f7aa |
| original-ubuntu-failure-v1/original-ubuntu-job-log-stdout | 612993506950ad3f61fa97cee96e28026d62efafab104692ad078d7735c93175 |
| prepare-native-readers-v1.py | c28f2fce2c69de8e33ccdb0567d9be193b41e8c5278da43bf85085ec830b0c7e |
| collect-original-rename-ci-v2.py | cf6e389ec6508f9201dc0cb22efdd2f248fbd5c5c9175421a806070e78e2c190 |
| prepare-original-native-v2.py | e02139790d240e0748c10604534f7aa1599c089a02b7841ee74589736aa3381d |
| prepare-original-native-v4.py | 05380b6e2f8854e96e2265517109c1b75807400a7fe531c8b2e7a2b9d1ae01ae |
| prepare-original-native-v5.py | 5df26dcb56ae52294f40ece4c43ae3e9c1d42cd3d248bf6501032cda33e9d976 |
| prepare-original-native-v6.py | 283fb5d317d355ab8c9ba58e4dd32d0172828d68dce527227aabf07dd2d567f7 |
| collect-original-rename-ci-v4.py | e4b8e7782ee00ad5cfa480af06407bb38065c473e878933890850ab6a67945d5 |
| collect-original-rename-ci-v6.py | b84d225182d8465b06988989ed94b66431a9d9d0037075db8d41d415fe52c6a9 |
| seal-original-rename-ci-v6.py | b1a13597d218ca53d55b2fea38742a8e46e0bc1d4d72a684a1571332745d5e65 |
| original-preparer-guard-v1.json | 3aa50d45735bd58f510b09681c62d17fca23c074f6be8b80589a7626bc3166fd |
| original-collector-guard-v1.json | 3d05a3a5b64d390f49c69138e95f5fdc6d03bbed37aa6b21ca4520d8c551b282 |
| original-outcome-guard-v2.json | 342828b6851f4f70a49cf048880e84b8e5a534a5d6807c154318055f8cfb4703 |
| prepare-corrected-native-v3.py | cd2d3c821c5388b9a1f11ba6e3c91da8de50b1b05f3e975b4e1783176c9c702e |
| prepare-corrected-native-v7.py | 4e9482dac4a767bfca18292478f595441e42c5f8e0725ea787d415bf796a781e |
| collect-corrected-rename-ci-v7.py | b0457a0301622dbd3e5631899c8506044dbf4e0138289ea034bab3ca6bfd6bba |
| seal-corrected-rename-ci-v7.py | cca98582581963ab176285a43d39a177587cf42b8593fde5ced6ce2e48e4ef4d |
| query-both-ci-v4.py | f239df22320bcf5b29afa1a398825e9bc0e4d9f1b644befad3667c6603be1070 |
| both-ci-query-v4/observation.json | d36e1f9cd334a150888eb623238a7a0b704edc45b92a17f42bcb15664615494d |
| next-source-inspection-v1.json | 81068451ef2365a4cc50a14a9da5833ad1293986bcd1c2d732f2edd0eca83448 |

Private `FileCatReleaseEvidence/ci-37728821237-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 9c4524799afcd7d2aee2c980d05c089834bfb0dfee75d4d871cbcd2caab13f5b |
| independent-restore-ci-v1.json | e48525ca75effb60441fe304016151b56838dc7e54f09a95e5293a6bb23e4d78 |
| run-native-stdout | 3a2ca0d8f81c46011c4b6818eca27fabf70c6faa32858347404d70cc274e9e40 |
| jobs-native-stdout | d62587a89de02eee7d24f33a96397a2901a7c4c9f3b25c7451e9b41420231980 |
| artifacts-stdout | 80384956dacf9aea20ac032685476503dff5d8af3aa9746ed348529881bd5f9f |
| independent-original-rename-ci-audit-v6.json | 45b688e0d640060d400ce9560a7eb78844931ceaac92ac3509dc2dc8ae089cca |

Private `FileCatReleaseEvidence/ci-37729215497-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 4f26ae8ab926bab35c82d6553e111c8ebfcb4f79d9ecc5ec0a166713dfc55c21 |
| independent-restore-ci-v1.json | 411cdd8d097bc19defe346cf817c8eeacb9fa79a58e44f7674ed94cb1c215049 |
| run-native-stdout | b284c52d522686c935c07d68494963041ac300ca43efc79f817b10beab7fd54f |
| jobs-native-stdout | d8d2d9d0cb071fb8953d665e15106b17691ae4a32d80033bf3f840f72dc92b5d |
| artifacts-stdout | dee15e31d371346b4c3923b67f469150c24d8a0af4bc86923894b691fb5107b0 |
| independent-corrected-rename-ci-audit-v7.json | 1be74e4ed1b2b16a882205c8561244379a5ae351dc60d94ab44bd5eb6acc3104 |
