# I218 — interrupted cleanup loses the approved partial-file version

First production correction `8fb55123b93f7432679b63f230a0d0d9cd4213f5`: **63 additions (31 Core, 32 App)**, **1800 canonical expanded passes/70 explicit skips**, full App **1180/25**. Original CI 37745540158 passes all four lanes with **228 addition passes/24 platform skips**. Final selection producer `1f3f9923d438f769d3157c9b9a54485d51f30467` retains every I218 case and native refusal observation. These are preliminary owned-file/component qualifications; no candidate exists.

## Defect, correction and qualification

On unchanged f038591, four Core controls change both a reviewed partial and its current source prefix while replaying the partial's length/timestamps. Cleanup deletes the unapproved replacement. Four real App cleanup confirmations reproduce the deletion; one legacy metadata-only model also authorizes deletion. The source holds the deleted bytes in these controls: loss of unique source content is not proved.

Review now retains SHA-256 of every byte of the partial while comparing its prefix to the source. Deletion requires that retained hash, the same size/times, an ordinary non-link target and a still-matching source prefix. Changing both files can no longer replace the approved version. Legacy five-parameter named construction and deconstruction remain available; metadata-only models conservatively keep the file. Empty and near-one-megabyte prefixes retain exact hashes without another content pass. Existing cancellation checkpoints, direct-copy bounds and journal acknowledgement/closure policy remain.

The final unchanged-producer diagnostic records 51 passes/nine failures/three platform skips across all 63 additions. Working groups pass 230 checks/three skips. Canonical full Core passes 1108/61, maintained Remote 90/7, affected App 602/2 and full App 1180/25. Every earlier local name/outcome/skip remains. Tests cover destination/source/both byte and timestamp changes, length/removal/link changes, unchanged/source-tail controls, zero/near-limit prefixes and legacy API use. Real Windows sharing/read-only preflight refusals and ordinary-user POSIX parent-unlink refusals preserve exact owned bytes and restore fixture handles/attributes/modes. App cleanup and run-again use actual confirmation and kept-file alerts. Changed-source run-again retains its old journal; cleanup's established closure after kept-file acknowledgement is deliberate and unchanged.

Original artifacts retain twenty verified server digests/every member, fourteen full inventories, four build receipts, 92 locked graphs, nine mirror and five launcher controls. Windows x64/ARM64 each pass 60 additions/three POSIX skips; Ubuntu/macOS each pass 54/nine Windows skips. All earlier native outcomes remain. Journals model interruption by removing completed-copy end records; actual process-crash, native desktop/helper, atomic aliases/handles/check-to-delete intervals, wider account/provider permissions, physical-source and candidate scopes remain unqualified. No VM/Mac setup or physical source changed; physical HOLD and human stable GO remain.

## Retained diagnostic and evidence-tool refusals

The first hash edit landed in TryClose and failed compilation before tests; corrected before commit. Two App builds then refused an ambiguous Location type, retaining their passing Core inventories and no App passing claim. Both failed stages and the exact alias correction remain. The first canonical reader required byte-identical working/canonical source and refused Git's CRLF-only normalization; exact hashes and the checked line-ending transition remain. The first native reader incorrectly required Core and App in one inventory; correction aggregates their actual separate inventories by lane. A read-only re-reader then refused tuple versus stored JSON-list shape; JSON-normalized comparison verifies the same immutable proof. These are retained fixture/evidence-tool errors, without raw artifact replacement, test reruns to hide failures or fabricated inventories. The initial source-inspection hypothesis is retained and superseded by the proved baseline and final-source review.

## Provenance

Private `FileCatReleaseEvidence/copy-cleanup218-v1`:

| File | SHA-256 |
|---|---|
| clean-v9/command.json | e2a8bcbeca54a237480a2afcd6cceeda4cd4effcd0dc16952b9d9fac60f7c285 |
| clean-v9/source.zip | 05a571c584159049f0c4cca1baf42a9947d80f79120b7d0ac28466a184e70ddc |
| clean-v9/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v9/app-full-stdout.txt | e00c86a6ede679173154f98aaef53b768a06e990cc787b19c0b7eca0571dc2dc |
| clean-v9/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v9/app-stdout.txt | 668fc6f32211b1fd58032087ef6d721527d9f2b7fb980388b412d30b3dfa01ea |
| clean-v9/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v9/core-stdout.txt | 3e02d76844a6e263ab36bbbbe039a03c93cf1f0cd67e6b134a22c3cf8a8abce3 |
| clean-v9/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v9/remote-stdout.txt | 2b3bb01eded113377946db06381b11f6f54c1075654e957b48507d9b2f1b1742 |
| clean-v9/results/app-full.trx | 9a1d82f10bf47748d5d032ca4b918eadaa9ce6710f4993d58563ead1497783d8 |
| clean-v9/results/app.trx | 57be4b752eda0decf0f68bb3f6507a68a824a7a62798e9c91e2986198d055c28 |
| clean-v9/results/core.trx | f7624f795dc4b191b906781298a726be01ff6da3d851d21a0c2e90d2a897a9b1 |
| clean-v9/results/remote.trx | 9ec32e5ce20609be028d79cba5ab41af5acb23792b75905934746083fca284e3 |
| baseline-v1/command.json | 46f09904d4e0d49cac3849609ec4b32df456b52678e046b997d9c1f235c20611 |
| baseline-v1/source.zip | 90a3671d046bbd23a8e95f1aaa5be63567d575773b4f946056b4cfd097414e37 |
| baseline-v1/core-stderr.txt | dfd5c457233609aa8e114e5ce3f02d8c8f3f314ae61fa543079cbe6a081376b6 |
| baseline-v1/core-stdout.txt | 869e352926c79c2bcb485412b02553d8885526c84fe4a90c1b34b057e61b0d05 |
| baseline-v1/results/core.trx | 5c11c327d3a7c1c7354f76d91f04de0a67880a7b5f0319343af50e3819f433de |
| working-v3/command.json | 33c68a75f6dab78cab51a4cb2d63e994ab99895caf226a3fbb7d004d3b3c28e6 |
| working-v3/source.zip | dd66771dc121123d40b20b95a07193565a1f90e863b4aeac710ab7d12818bc37 |
| working-v3/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v3/core-stdout.txt | a543c6965c6cf550732197b995e5b6630dfb7516ec2caa5c72315787549b0978 |
| working-v3/results/core.trx | c251a25195e6caceefb78f288a9d5ef5651aa6b55db02a31bee48bf2a4135fcf |
| working-v4/command.json | 3d421e0ea7b4d7f4d8aaeaff585dee2d687d77fa222a3054e7d0d733e702861e |
| working-v4/source.zip | 7181bc6d4575e7d16173daa325829df850374027f2d433de9fec5ac087c352c0 |
| working-v4/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/core-stdout.txt | ebba72d21be3e9343e940ef2b4a9b5cc6c7a1324a71447136a99d4cb572c5fdb |
| working-v4/results/core.trx | 5d191019febf06268f82f2ca139820557927674f57a3c182c18e5409d5d77484 |
| baseline-v7/command.json | 17e3eb98e2f3059cb824378211437c81679e5b056393a9702fc084c047d0752f |
| baseline-v7/source.zip | 0a9dedc5dd9b2ec197fc1c628eac20df004dc4dec989ba0e82833a1f92dcd7cd |
| baseline-v7/app-stderr.txt | f3a5df1d1f84166923ace7c057cde2ca99dedde064ef2719c3181d963803b303 |
| baseline-v7/app-stdout.txt | d51a3e300da89c0f4c54dea8ecc46a610a47f883d2e27f132e89d389213074dc |
| baseline-v7/core-stderr.txt | a632fe779a3b93bacbf8e57c962c01bf46883b2681059b3521e54e143da5c56f |
| baseline-v7/core-stdout.txt | 20d50b5cb54c9e12001e162876faf269d3223f3b481c6ac16173aa746b534f2f |
| baseline-v7/results/app.trx | 4b15a1761bab29ba93256d745012aa0ebae7f394d4cff87d5858da3f82285ea2 |
| baseline-v7/results/core.trx | 715533490937c0dc0bb80992ed4e25dd454d549c0a5fbe9035a950a116935b41 |
| working-v8/command.json | a59a21f75f79efdaad8aff8fbaac2abab308fada748c5df2ab14693d4d370c39 |
| working-v8/source.zip | 0a9dedc5dd9b2ec197fc1c628eac20df004dc4dec989ba0e82833a1f92dcd7cd |
| working-v8/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v8/app-stdout.txt | 39e4683235911fc141d7be0045fe3e9efcafec7d080c1dcf9fc8763f846a5431 |
| working-v8/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v8/core-stdout.txt | 9336cb3691ebcc9fa255526d99c5b70d10aa5cfcf2a1239d0b5f0ee341049817 |
| working-v8/results/app.trx | cc5de0040e222b912ef155f54f8f9ddee799c744436bd82077f00f2c57edd475 |
| working-v8/results/core.trx | 86cd750a643982f0d83873d65079d7aec6af3a6014c16706e6f4bc3c1f209671 |
| working-build-error-v1.json | 75453b305d7586ac10275b09ada8923fd0b5510a3c1914770308cd1ab066cf40 |
| working-v2/command.json | 1eed3ce5b1057ebc11e000e57bab40b49362228a1dc1980ffd09d8721a6b56c0 |
| working-v2/source.zip | 744cafbec6c922841cfd857a7401dcc6e32505cf72c0c2f3cc4efae186595181 |
| working-v2/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v2/core-stdout.txt | 6a04339e431361ad1b59e25a250310788212f91d5a30658af25fe70aef601692 |
| app-fixture-build-error-v1.json | 09c9d8a095f72b79b43dbe4d22082d40b4008129579eec3921dd6575150853a9 |
| baseline-v5/command.json | 08d7c5f01d1698abd8d6cba9b2c1a4472bd52204bb8c2c470f615cad2175217b |
| baseline-v5/source.zip | 67d56caa5f91ba8925942ef5f5ab66c5a37d418dba127229b88e1cb5def38d42 |
| baseline-v5/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| baseline-v5/app-stdout.txt | 9a04987d312d5bd3f7fe110e422f97bdff10eb79866386e66eda8bd893b5d798 |
| baseline-v5/core-stderr.txt | b353370a20031689cf6c969cfac195cac49b407fd996cbe56943a86044751d7f |
| baseline-v5/core-stdout.txt | 647f483870bf7927acf628c6f29ef2601fc9cccf78c811815ae978281e6f929c |
| baseline-v5/results/core.trx | 3141fe6f7680c5d05855363a94dd1f2612a7a103f59b9c05aaa086a81f64a24e |
| working-v6/command.json | c8881c2fc1a12e4c2736961bd5b07f397a02116495119dbdf2520790118665f8 |
| working-v6/source.zip | 11ef4d7e6f794b5ee4254ccbb2ea131f16642c19198a19e507d9ecd973b77756 |
| working-v6/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/app-stdout.txt | db7523d70ceae1a4670c389b63c8ce1cb69a72554673594554f7d9458ad6c821 |
| working-v6/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/core-stdout.txt | e173d6a1d75e44b0f326e8a631ebecb6d067596b504edebc41a257a51f287aec |
| working-v6/results/core.trx | ffe734afaba5be7a52475fbdc5b6cc663d9cdeec68625d9124e4cc3ab60264d8 |
| prepare-baseline-runner-v1.py | 71160d13e50a2a93311bd6d3d9ae586ab552066ab000cb9a655082d90c6fa41b |
| prepare-broad-runner-v4.py | d3085fbe8d767a77078ba4465728e0302848b51f74bf29932b55fec3d8d1a54c |
| prepare-clean-reader-v2.py | d4afc1c0a49e53af5711654dea4be2fa05ed69dc3bdf8436f6f214bb8b93cc4f |
| prepare-expanded-runners-v2.py | f3681c4f43df516c832c68d6d9b1d0ea423c029460947a32ce4096303de15947 |
| prepare-expanded-runners-v3.py | f31c506f4ba9fdff2cd9a411dc85b711b32283451f54c99a877f2bfe5abc969d |
| prepare-native-reader-v2.py | 5de6e271e34e065950ead3f592d4f5a0d5ea041b0272eaefb13febd6ddbd671b |
| prepare-native-v1.py | d5891434e3a3bb38bf00ad9444fe928ab318ab9dc0bbd8b36557c225fce7e299 |
| prepare-working-runner-v1.py | 5ec52d28486a0a8a774ac6c0ad296270f26f96d28dd5ea82936dcad91db27a6a |
| run-cleanup-baseline-v1.py | a0c24efcc0a3f056dbb16f2b7762890d006c7dfbd9ca5519fe7c5131c3a8983c |
| run-cleanup-baseline-v2.py | e29c0ea735d86a8c28f554dd8c7ecf0b4c41e5b81565efa23741106192d52ac6 |
| run-cleanup-baseline-v3.py | 0c735511d0f5bf745077c697817a39cc5f38a0ed93e3e2aaa52fd61efda181fd |
| run-cleanup-broad-v4.py | 3044e5b10415f121d1dad118c2e65f775a40df6f30f29af244a0413ec0445239 |
| run-cleanup-working-v1.py | c1c98c06dbb2e47af2770ed5f2fca907a5b162401f606928e84603cb2b3aaee1 |
| run-cleanup-working-v2.py | 8444f704c0ef31f178f41f57992af229b1c73494a81f725eb2b36e0f0d71690e |
| run-cleanup-working-v3.py | 8444f704c0ef31f178f41f57992af229b1c73494a81f725eb2b36e0f0d71690e |
| InterruptedCopyCleanupTests-diagnostic-v1.cs | 952e42d47d32cddc478e0d540b06ee91a27004d2243e31a57aac6151e0b9b78a |
| InterruptedCopyCleanupTests-diagnostic-v2.cs | 3db088616c43ddfbb889b4e2020b0b30d321b749d91df256d8ffa8ed57c21bc7 |
| InterruptedCopyVersionTests-diagnostic-v1.cs | da579806edacd59e0b5606aa2b1eadc088c236595cc8f22b7c425499763332f3 |
| InterruptedCopyVersionTests-diagnostic-v2.cs | 5896433de6a2f2395c445fb9ccb84db45cff79b53d6ada5826c9f0d35b02c58c |
| InterruptedCopyVersionTests-diagnostic-v3.cs | cc2fdf14bf8e57bd12428b53b6197bd6e98d855cb19f472b662a2d30ffd3a0d1 |
| source-inspection-v1.json | c42de46d18e823dde7f144b15541940190d158bf2bd08a3376ac728d278a632b |
| seal-cleanup-working-v1.py | c32f61a3bd30b75cd4eb7e7ab493fbc7fe5025a0eeb7abd79a7ee466642f8b94 |
| seal-cleanup-clean-v1.py | 3e27cb3c27382d1e033f131711b5064b310fc11a609680ebc77dd3826414ca42 |
| canonical-line-ending-guard-v1.json | 2a1934b8ab445667b040dfece00f3bd6c0077e67255b06267d20e48294e41ffe |
| independent-cleanup-working-v1.json | 76f2a7a5044279f4447489e7ac73f7fee056a7c985a4d364d8b46d900e084be8 |
| seal-cleanup-clean-v2.py | 7f55943bc90f7e284b9b6ad358f5d9f5947a716fc2cff425964b7443c0f2fe11 |
| query-cleanup-ci-v1.py | 6c9d7fdda6c55391686d9a443b451e3feff32e14858a048d9cc0021f6a675e9a |
| collect-cleanup-ci-v1.py | a407bd2f86d4ecfac8e3199782d24fe7bbe10a8558f724ab2b7f92383d24b62a |
| independent-cleanup-clean-v2.json | c0e8f2ad8047e5dc42e57eff8de02678a67271f073a23b470d41184cd5ae3d30 |
| seal-cleanup-ci-v1.py | 1173035b132185d1c1f8885d3b9ef4730dca881b4bedb397ecf029f88792e9eb |
| prepare-native-reader-v3.py | a53ddebaa84a2b652bd287a585af9ece105331104ae314fde77d5453073880bb |
| seal-cleanup-ci-v2.py | e179479ce0abe8d8f7a19bf39abd6ec89366795f87335a412af5d7c9dfb2548f |
| native-inventory-guard-v1.json | 32c8eac5da733ceed3d78c8522e1f0a6caa75d628f03d617cfeb310a999d1324 |
| reread-cleanup-clean-v3.py | a575e2652e8d28cfc9090edca06dd80192e373d769afa5ef9ad2f51aa8fd03e4 |
| prepare-rereader-v4.py | 5391c9fa7b4df2240d19f0bb1db03f8c32ab9f32a9bffa3996a73c71d8e37496 |
| reread-cleanup-clean-v4.py | 4928f9c4334a425f732158d2e14e33522806d371b24dfef015f74f66d6e32319 |
| rereader-shape-guard-v1.json | b681f938a47a3546bb252c5c9e28f02d1410acdb35d55c9fe330e27e90516017 |
| cleanup-ci-query-v1/observation.json | cabcadf7d6cb3d0ad9380096b71315cfdafefe5848614c77a5475ba670777f51 |
| cleanup-ci-query-v2/observation.json | cabcadf7d6cb3d0ad9380096b71315cfdafefe5848614c77a5475ba670777f51 |
| cleanup-ci-query-v3/observation.json | cabcadf7d6cb3d0ad9380096b71315cfdafefe5848614c77a5475ba670777f51 |
| cleanup-ci-query-v4/observation.json | bf880b8e2c06b59aea4bf57c5f6d2392f9f18045e8a34ea4713e684531c6d554 |
| cleanup-ci-query-v5/observation.json | 6c92994ff1c9ea01c3a1e9e0dd3bb747e39d78b78cf4cb97013299a2297c95b9 |
| cleanup-ci-query-v6/observation.json | fb9d6dceb0a4d6bbd79ee25591f18ce883483887eaad5bcdc508fff1f8cddb92 |

Private `FileCatReleaseEvidence/ci-37745540158-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | c4cf30d68ea82b797b6ac6b5e812f4145d7326bfccd450b44b2d4f402b5ba8fc |
| independent-restore-ci-v1.json | eb6495ab32d5f161386c243cda7493b89456b700489c994b58ec78f46678dbf1 |
| run-native-stdout | 7c565c490c9f9e8648d06929b135a8b917cad1fd061761187a2b7892cc334acb |
| jobs-native-stdout | 6cd2e2afa170cd8b3280a1ce07767e2ea4a7d98ae31d79c8ec1544a5581f4c9a |
| artifacts-stdout | 67e2cd1c1729e3a0471f515564894bbf3c3ed5cf57bc71d2cce9b4fcf2304643 |
| complete-run-log-archive-stdout | d6b90d17c524138080748a0e44dea833e348350bf1eaee99d8dce31290d8935f |
| independent-cleanup-ci-audit-v2.json | 6378d21b2f37bcaeccef6ababbe7ff7fcb7a5e036fe55d47f0a5101a26094bdc |
