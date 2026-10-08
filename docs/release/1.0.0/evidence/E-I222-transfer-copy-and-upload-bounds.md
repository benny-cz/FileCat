# I222 — complete copy evidence and finite upload verification

Runtime `49680a88d2f9004d3566f7ff92140ca82a24a4d6`, test-only correction `01161959f296c40144ba318fffd370346d9c51f2` and original final CI **37786200459 attempt 1** qualify **306 additions / 1224 native passing executions / no new platform skips**. Canonical local Core passes **1626/61 at 0116195**; Remote **112/7**, affected App **611/2** and full App **1191/25 at 49680a8**. The expanded total is **2349/70**. Source, exact payloads and every preceding case/outcome/skip are independently retained. This is preliminary owned-file/component and hosted-platform qualification.

## Reproduction and correction

The original unchanged-runtime baseline has 212 Core addition failures and 16 Remote addition failures. Core publishes 66 copies despite refusal evidence, changing 33 existing owned destinations; another four NativeId-resumed copies contain a stale middle, with two prior destinations replaced. Twelve initial revision-query failures leave the actual content object undisposed at method exit; twelve malformed overreported reads leave staged files. These effects have separate case identities and are not summed into a claim about unique lost user files. Positive-control failures caused by a missing final revision query are retained separately. Finite overrun controls make the old copy read two MiB beyond its declared length.

The copy now retains initial revision inside lifetime cleanup, checks declared length before/after each read, bounds known-length consumption to expected bytes plus one EOF probe, refuses invalid counts, short/overlong data and changing/lost revision, and checks final version before publication. Ordinary accepted copies make two revision queries. Resume compares the full ContentRevision including NativeId; the changed middle restarts even when the first/last comparison bytes remain unchanged. Unknown-length/revision streams retain read-to-end semantics. Actual ProgressiveContent keeps its deliberate one-MiB declaration slack and its own damage/ratio checks; read-back still requires its exact expected length. These are weak provider observations, not atomic snapshots or same-size/reverted-change proofs.

SFTP read-back now passes the copied length into both source and server hashing. In four original both-growth cases the old code publishes a larger wrong upload, replacing two owned prior destinations; twelve original cases exceed the verification progress bound. Changed local files are actual rename/replacement files. Server mutations are maintained in-memory fake-server controls after stat; no real server behavior is inferred. The copy/upload phase itself and partial-provider warning/identity policy remain wider work.

## Matrix and independent oracles

The 284 Core additions comprise 204 ordinary/partial-provider cases, 24 controlled resume cases and 56 actual ProgressiveContent cases. They cover empty/65,537/1,048,577-byte files, new/replacement destinations, Native/ReadBack, stable/unknown revisions, lost/changed revision, contradictory/changing length, short/overrun/negative/overreported reads, initial/final revision-query IO failure, clean/uncertain/missing-range content, NativeId/modified resume changes and container declaration slack boundaries. Actual owned FileContentSource reads, source/destination hashes, stage leftovers, disposal, bytes, query counts and explicit outcomes are checked. Partial evidence is synthetic and does not qualify a recovery parser/corpus.

The 22 Remote additions exercise stable/empty, source-growth, server-growth, both-growth and source-short uploads with new/replacement destinations through the actual SftpUploadExecutor and maintained fake server. Source/destination hashes, exact lengths, cleanup, progress refunds, source swaps and issue/decision counts are checked independently.

All four original final native lanes contain the 306 exact addition names and pass their raw byte/publication/cleanup/disposal/query oracles. Twenty selected artifact digests/every member, fourteen inventories, four toolchain receipts, 92 locked graphs and the earlier mirror/launcher controls verify. I218–I222 repeat here: **614 distinct additions / 2424 native passes / 32 explicit older platform skips**. All 16 current unchanged/restamped metadata preconditions pass with actual reviewed/current Length/Created/Modified fields. Every earlier I218 byte/deletion oracle and I219 selection, I220 transfer/progress and I221 verification oracle remains strict. The original I221 timestamp gap and refused reader remain unchanged at their own identities; this successful repeat does not retroactively qualify them.

## Original Linux failure and fixture correction

The first runtime commit 49680a8 has green Windows x64, ARM64 and macOS lanes, but Ubuntu fails 28 existing InterruptedCopyVersion controls at the initial recovery admission assertion. The attempted fixture stabilization set modified time to 2020, which put the fixture outside the job admission window on that platform. Test-only 0116195 instead sets it inside the job window and explicitly checks admission. Runtime, workflow and release code are unchanged. The Unix creation-time fallback reference is retained as context; it does not establish the cause of the historical I221 metadata gap.

All **21 available original artifacts / 12 inventories / 28 original failures**, official job/run records and complete logs are preserved and independently rehashed. Ubuntu's later dependency setup was skipped after Core failed, and its always-run retention step also failed because its control files did not exist. Missing controls/results are not fabricated. The original failed source/test snapshot is not replaced by the final passing test-only source. Working-stage overlays and canonical producers remain separately identified.

## Invalidation and remaining work

Rebuilt artifacts invalidate affected binary evidence and receive new identities. Local groups above and final native lanes qualify only their stated producers. Wider native upload copy/admission/partial evidence, actual servers/accounts/permissions, initial descendants/followed links, atomic handles/aliases, same-size/reverted changes, blocking I/O/resource/reference throughput, native desktop/human workflows and final candidate remain. [I221](E-I221-transfer-verification-bounds.md), [I220](E-I220-transfer-version-and-progress.md), [I218](E-I218-interrupted-copy-cleanup.md) and [I219](E-I219-interrupted-copy-selection.md) keep every original source and adverse result.

VMware inventory confirmed two running guests; no guest/Mac setup, device or physical source changed. Physical-source HOLD, contract/custody gates and explicit human stable GO remain. No freeze, candidate or publication is claimed.

## Selected provenance

Private `FileCatReleaseEvidence/transfer-copy222-v1`:

| File | SHA-256 |
|---|---|
| baseline-v1/command.json | 5fd00ef911823ca8c54a83f6ad6125404fea93d3f37b2a384d2c3f62d098c5d1 |
| baseline-v1/source.zip | 517684d18bb95fbf18aaf6004504817386b4ed8abb69682056afb5af30012a29 |
| baseline-v1/core-stderr.txt | 64dda4e2a809946d250398d8c3a341fa822738feb89bc36906cf559e3509c2a7 |
| baseline-v1/core-stdout.txt | 6aa05006f8949caa7bd8ff0cbd27b7bba49448a5c4e2196db18521937bfd6c54 |
| baseline-v1/results/core.trx | 249a072e5ab3cd560885c693b502e907746887351b348367afa4d8e2584e0941 |
| working-v2/command.json | 215a3ec2ee28f0b1b62857453bb0d6987f2754b8b95943ff1cde238a845dfb26 |
| working-v2/source.zip | 0d18c692278bebbcfe215501f3adc5b56c20eae203de807f3743ed4e3bf3aeb4 |
| working-v2/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v2/core-stdout.txt | 09bbd61fd1c5cf50870a79cbb8359646401defd4ffcf59bd6da2ede2f2ca5f55 |
| working-v2/results/core.trx | 375a1bdaea40fd9d3601f6edd31411f12a06a635adadf745067955346039b757 |
| run-copy-v1.py | e619a5ea1745590e03af7a48eb84c135d29dc240d48689ca877ff3ccd3d3ae1a |
| run-copy-broad-v1.py | 3ccfd6ea4a7948df9bdf670ab1077f89e77c24b5588ccb82afcc07b22d44fd78 |
| TransferCopyEvidenceTests-v1.cs | c87481096d3f636b50f672344ef74ab06e32f7d4c176d8e1c71b4f18bc10326b |
| StreamTransferExecutor-before-v1.cs | 861138f2f78ab58d04ebb1d9a76c6b3f103e179d98bb6f75e504b8dfea1d368e |
| InterruptedCopyVersionTests-before-v1.cs | cc2fdf14bf8e57bd12428b53b6197bd6e98d855cb19f472b662a2d30ffd3a0d1 |
| seal-copy-working-v1.py | cd6a1b86aaacca244b1a5cfb8d0720dd3f3611e024d08aa3795d02dc5b8bdc86 |
| working-v3/command.json | 2015d7d42acef1eb3b7a2a490dfa8813f6e12c72938402568acdb8f0fd20e258 |
| working-v3/source.zip | b30bce8ebe29ec0b81978d06f8b72444fcb6dd6c638b49e488ce5db673c675eb |
| working-v3/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v3/app-full-stdout.txt | de5ccf568687952bf97884aab34852dfc2424f7e4afc0f8713d2686f6b0b4143 |
| working-v3/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v3/app-stdout.txt | 0c47b138dbc38753bb12f0b6e428a249f6bbc6997bcdc9c72940c210a12e2b56 |
| working-v3/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v3/core-stdout.txt | 3d35f0f4d8b55ec077562238af790afab39ba0e5baa65a45862a29f1d20424e5 |
| working-v3/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v3/remote-stdout.txt | b7b2c8c30a233b7d0e07887d51fb63cf4cbaf4362a4b466487a2b0b973c8e079 |
| working-v3/results/app-full.trx | a26349e73ebe5fb740931e407679ee0865e31c7ba78a25d891d6711f2d2fee71 |
| working-v3/results/app.trx | 866d2f72deca570cf0c58707e9322843082446a56ea95d626500cfa280a5eae3 |
| working-v3/results/core.trx | 81eb40f579c9686d93bec324e53a1bc638e8da66582835e8797c1f5fb5ce9288 |
| working-v3/results/remote.trx | 656f343223b27267421ef0d0e72861ebe6b5fb4317a31c8e1ca2149dd51db82c |
| baseline-v4/command.json | 0c31464efa7772b8905c1a6c88a28d38eb03f8169b0427cb81edab1cab5e4f88 |
| baseline-v4/source.zip | 16feab9a395bf26043848472a4b08f6f3f10d2a0a43b26485dcf9f8b4bd1f0e9 |
| baseline-v4/remote-stderr.txt | 4ef39208815552997ffa0193bbdeb09f4bf6a4002bf1ceea1a7e7e81f4290f55 |
| baseline-v4/remote-stdout.txt | 5899a9c4f141361176ccaa95ddd64728bdc0c2a0dad44f6ac274007445f9f133 |
| baseline-v4/results/remote.trx | 08ea4d01ca1b707dd92a8464a2d2d096c78c15fb5547bd867110507cb4d00894 |
| working-v5/command.json | 83c1bd2d8d08272c9c6f34e731630b7371b474385fd065d79394d552f96360fc |
| working-v5/source.zip | b7861758b850434abae2a0065f241a5cada087ef47cb1fd9b1bbcd45be3ce3e1 |
| working-v5/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v5/remote-stdout.txt | 71266901e5cd3075f90444f16bc652030670f362a1f9f1b016d65f702175a245 |
| working-v5/results/remote.trx | 59b586ededc2af083e38836a0745af8d31176f051509ed1846ff5af0c336a574 |
| working-v6/command.json | 3347b7fc83b1de707f010136a74182a82f454ca57437983bb66700fbf3843440 |
| working-v6/source.zip | 73908829a28d985aa67c6b1236344858f5b17fce5d56d1e0f0f618d2f73b3016 |
| working-v6/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/remote-stdout.txt | b8d4caba92e75cc22249203ba170274099e8b270025c456a1a0ae315e7639923 |
| working-v6/results/remote.trx | 9c32bb33846be02958cba6770b2797dce12b754ac4aebaa3dcb50c074763903d |
| run-upload-v1.py | b82cbf1ed5b285647a680d2a1be34660c3330d0583d9417d0c73140137149972 |
| run-upload-maintained-v1.py | a0a1c6fa04f6c4257e73950dad7c6be29c31620e2ccd7bb8dcf2b9394ee38254 |
| run-copy-broad-v2.py | 49b1e27aee7bc1da77d860d48d64df3ada038862620f142e9f8016555ba45a62 |
| UploadVerificationBoundsTests-v1.cs | 9ca6f90f1b28c62c54f8782bcebb9c2548b5a7a4572498d42935a4d2d9654eb2 |
| SftpJobs-before-v1.cs | 28fc03fe739ae18f5e8689b02235c92dfaa6658097f97303c2213209e296e658 |
| seal-copy-working-v2.py | cbdac3ba3af8709de23070d0d7eba807c763ce9c638f77ae20449a97ac5fa355 |
| independent-copy-working-v1.json | 8d30a55dccd03e995dec05d4c966ea8e5b6b7fe2ef376911043d283493197929 |
| clean-v7/command.json | 7f0f852d40722f28d872000230fa0226156278f260eb1fd4d721895050b50773 |
| clean-v7/source.zip | 03143b17e1535555544eaacc66782a124b47333f4a784f25f0ad9f7e731964e5 |
| clean-v7/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v7/app-full-stdout.txt | 6aeaca48713b64d04b90972c85e8ca010cef29eb70382df51e2e0d5d3ab36294 |
| clean-v7/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v7/app-stdout.txt | 058f08a3105f76901e318eaee490673e34c3775e0e2c8eec458b1ea3178413b6 |
| clean-v7/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v7/core-stdout.txt | a0ee0abfcf6964db248e5476fb60b01e95a0f483ce3a8579b3695d0de65160fe |
| clean-v7/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v7/remote-stdout.txt | 03fa4b713ddb73a35e01c467b0de7f11ffd759cd508b5990ed7205fe3d360de5 |
| clean-v7/results/app-full.trx | 8d8460d747ff6615db31c32262f8f04ccb053bb317f71ea47c7155dcca4eca14 |
| clean-v7/results/app.trx | d6ed35f49364c27d4b1b823074d5850743eba85f589492ce1cdffb48db9435d2 |
| clean-v7/results/core.trx | cbc2d2f39c038f843e4339d6c579d45c305091d84c87d596fda6aef11aa216dd |
| clean-v7/results/remote.trx | 3f14b3aa3f53d58dbd39116f5b255c480c597cb2acdfc89c17fd9115ff169df5 |
| seal-copy-clean-v1.py | d387e9d9e23a4190449c9178c42a7d7b9618c8a01012479e982f292c6fa6fdc8 |
| working-v8/command.json | 93738738a81e3371b62d08a12de71ff144f879292f4c702a8b23d020a08506fd |
| working-v8/source.zip | 56efc85e71ca9ec62f0e725a3c5c9078eb6679a7f1f32c9e04d74eeb6020e9b6 |
| working-v8/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v8/core-stdout.txt | 3f4db672074f617d1f710d3b094e21e4692309267478e218e1ece7c4365f53c5 |
| working-v8/results/core.trx | 668d768cf1e70c4f5571a29e98bc3ee46475f69bf40b4d91c81eb6bba26a4657 |
| clean-v9/command.json | f8fda62ac036c280f29f4d58687cd10fa70f0047aceb4785dd1d17673cb5b6d3 |
| clean-v9/source.zip | 8372d7cf770134afb34d8556b1b82e63bbd3e36e2fcd3aac9cf78dd37af0052d |
| clean-v9/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v9/core-stdout.txt | 9cf186a4e97aa8a4b7fb802c146acf3eb0c8062b016c52bd5062ed4b1f59a5a9 |
| clean-v9/results/core.trx | b3fb216eb29a7cef353631be39e2d6884623ec75a019723fe8a27e07e7fbaffd |
| run-copy-fixture-correction-v1.py | e619a5ea1745590e03af7a48eb84c135d29dc240d48689ca877ff3ccd3d3ae1a |
| run-copy-canonical-core-v1.py | e1d6cc93b6ef33fbb67a3a689c2138d54be31c9863f835a98b0b273cb1d1be34 |
| InterruptedCopyVersionTests-failed-native-v1.cs | 1dc1105bafc6513a6d3431d7320a53eb8cb0c208b79ce91432dc0275bd8225db |
| unix-creation-time-reference-v1.json | f5065e5b8306763b351e7f832840643f6ee697ce86115c28e8b08969959ba81e |
| seal-copy-clean-v2.py | 626c993ec78455971fa3dc9449317d0cb1450d5bf02c6f318e61ee89e39f9773 |
| independent-copy-working-v2.json | f91db1d252695ea8381486157de927c6b11df6a1dfde289e5c0811bd122026a5 |
| independent-copy-clean-v1.json | 8ff7f0ddb7c150b6b0e1c81ee4b44d8e19af2d4377181d33fc90bbd6dd0104d4 |
| independent-copy-clean-v2.json | 5aa8635ade72fe75590cdfde6ef4171327e56536377c04c29441847357908e53 |
| native-parent-reader-v1.py | 2dca810d40818b2f6860e7eff6e79ac57bb066f6ef29c7d19c45f61bfade67e5 |
| retained-transfer-native-reader-v1.py | 16072e1068b10f871d34f7580e46fd6701328317cb43f62bc5394e224b47b4ae |
| seal-copy-ci-v1.py | d9551a564f575f12d3687b0ac5ab4ad321605300887aa6aa568d2b041618e21c |
| collect-failed-copy-ci-v1.py | 3c6faa2ae01309752e0a9bcd0e98e64f6f28d7e74bcd807c127d9f55114ad3fb |
| final-native-source-v1.json | a8c017d54337bc1a3e1eb275003db5a98525cf674a3703053f57e28c481fdf1b |
| prepare-document-baseline-v1.py | 2e80afb9fbed78d2bab3b085acbff75954c3653518f6488a6d07ef6cfccd2f8c |
| document-baseline-v1.json | 259b99ae23f1f77f35b974ef54a47d14a112b2fb589ac8f161111cedc7572b8b |
| update-copy-documents-v1.py | 05d008f435439b1726df4108673beef1d32721e57fd463dd1bd74d5462d18a3f |
| document-transitions-v1.json | d775aca70819a6a420efdf371ae3d6e5f52c2be40971efb4a4d55acdad6bb854 |

Private `FileCatReleaseEvidence/ci-37786200459-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 2437b010fd40c8a84b6d2c139e0ac16b38052c711a1f4db95192fb9a6396d295 |
| independent-restore-ci-v1.json | a10d4d3d4cbbe515daa1eb174cb52f0a4a52608d3cfeb791046034501654eb13 |
| independent-copy-ci-v1.json | a7a976063124f750346a3fcdbdc3bc1697e1377e69d97bd1b50f839db3b4c3a8 |
| run-native-stdout | f975b7d0c815a3286d55a18d71ea66cf9b74d4dc1594f4e74b672f31406439a9 |
| jobs-native-stdout | c5c904f4ebbd217e1c44e75c542ee456d5a8a11ce55132b20de70d2b1f1b2674 |
| artifacts-stdout | 492004842be33a06359db1cc2d8a2640f10a9e4b08fb93f5d22e350ea77ed4a1 |
| complete-run-log-archive-stdout | e960c9504417ee48bcebec91ca57443b47d20e5a68112fcf9a0ec141f94282e0 |

Private `FileCatReleaseEvidence/ci-37784587045-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-failed-copy-assets-ci-v1.json | 7e19010dceba1fc57b18b4ddc3584508ca5d11632439a86f5a379067bbc5e29e |
| run-native-stdout | f6595a8b4532878b76cbc828742584e1f8a6dc9fa9cf7f8a9acfd5396699a30e |
| jobs-native-stdout | 6cde4d6678a73e94b2592e55776cfd98269af5c1f4f3442e4ac8d5d835afb7d7 |
| artifacts-stdout | c2e85774a013f0bb2fc20a4193975d52aac8b50a0469138783e2b7a496863287 |
| complete-run-log-archive-stdout | 6a299f6a531f251421d7cf989e6799d7ea224341374bad708499af8d4b895f4f |
