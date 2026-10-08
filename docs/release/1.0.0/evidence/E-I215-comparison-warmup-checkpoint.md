# I215 — comparison polling fixture arms during an earlier check

Test-only correction `cfbc40e097e9717edabc0120b96494248622c60e`: one new forced control, six retained polling controls and full canonical App **1107 passes/23 existing skips**. The later test-only lifetime correction `2dc1417804d8aa3936089bdd10a90e67565d401b` qualifies all eleven comparison controls (seven polling, four lifetime), keeps the five-second held-call deadline, and seals full canonical App **1150/23**. Original CI 37731958789 passes all four lanes, including four forced-control executions; twenty server digests/every ZIP member, fourteen full inventories, four build receipts and 92 locked graphs independently verify. Production and workflow files are unchanged on that producer.

The retained original I214 Windows comparison timeout waited for the armed source's Entered checkpoint and emitted no main observation. A bounded owned-file control proves the old ready predicate can hold while an earlier unarmed revision check remains active. Arming and requesting another check correctly coalesces; the expected armed checkpoint stays absent. After that earlier check returns, an explicit request enters, 25 repeated requests coalesce, exact bytes remain unchanged and both sources close after the calls end. The fixture now waits for the earlier check to drain before arming. This proves a fixture precondition gap; it does not prove the historical CI timeout's exact cause or call it a flake. The original failure remains in I214.

The first independent native reader accidentally compared its inventory to its own run. Its digest/member/graph/control checks remain valid, and the collector had used the correct ce5b119 predecessor. The limited original reader/proof are retained. Reader v2 independently checks that actual predecessor and preserves every prior name, outcome and skip. The explicit guard records the mistake; no evidence is overwritten. These are actual comparison-window/owned-source headless controls on native CI platforms, with no desktop, physical/network source, performance or candidate qualification.

## Original lifetime follow-up and retained audit guards

The original icon producer a1e6879 passes both Windows icon lanes' six cases and all 260 source-content/four warmup additions, but its Windows x64 lane fails the existing comparison lifetime case at the Entered wait (pageRefresh false/reopen true). The raw failure and no-main-observation limitation remain. That fixture's earlier readiness used IsComparing/runs without draining loading and the pending activation check. The already forced I215 control proves the missing readiness state can prevent an armed CheckInputs checkpoint; the historical failing run's precise interleaving remains unproved. The later correction waits for loading, runs and that check to drain before arming; it changes no production code and does not lengthen the five-second Entered deadline. All four lifetime cases retain exact owned bytes, one active call/zero early disposals while held, one disposal after completion and zero final active calls. Corrected original CI 37738707924 passes all four lanes, all sixteen lifetime executions, four warmup executions, 260 source additions and two Windows-only icon additions/two explicit other-platform skips. It records the single prior Failed-to-Passed transition and preserves every other prior name/outcome/skip.

The original failed producer collector initially used the wrong TRX inventory path and refused its predecessor audit after retrieving the twenty archives. A preparer then expected 25 raw captures where there are 24; its supplemental guard was not written. The independent reader still succeeded, but its wrapper's later guard check failed. Those recipes, original archives, proof and the supplemental guard remain. Read-only finalization uses the exact original inventory path, performs no network/refetch/raw-capture writes, and independently seals the one failure and both Windows eviction observations. A document preparer also refused compilation before any repository document changed; its bytes and guard remain. These are evidence-tool failures, not product test results, and no outcome is rewritten.

The first lifetime local reader also refused on an incorrectly unpacked helper result before writing qualification; reader v2 checks the actual canonical source, payload and full inventory. Two final CI capture preflights retained the earlier in-progress metadata: one was started by an incorrect driver success check, and another still selected that cached stage. Both refused before qualification. The final collector uses fresh stage v2 of the same original attempt after all lanes complete; the earlier metadata, failed recipes and explicit guards remain unchanged. No test rerun replaces the original failure or skips.

## Provenance

Private `FileCatReleaseEvidence/cpoll215-v1`:

| File | SHA-256 |
|---|---|
| diagnostic-v1/command.json | 111ae9ce7d96bd675cff8730e1a43370be847809751e56f760cd5ed7686b7036 |
| diagnostic-v1/source.zip | f5ddb8ba93128fc16c5a6a0adfbbc46c23301f02e7939c41a19b4b2e0a119030 |
| diagnostic-v1/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| diagnostic-v1/app-stdout.txt | b9708d3f64d6808e1407b26b8f649393cfd9ab7dfc5064c20ad5e8b7e04fcd0d |
| diagnostic-v1/results/app.trx | 8641356793c2085fccbb75187a9c4083285f95003766da67583e8cacde640acb |
| working-v2/command.json | c89f7449e19f0eea49e5213e4307a73c73786a9e0589d69d9e1443bf9d7c87a7 |
| working-v2/source.zip | 63316bfde549fa29cddda92bfbcbef3b7443a49b204456a9e2eaef6061bb3f15 |
| working-v2/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v2/app-stdout.txt | 1dd1c7cf8f8bdff4004746942192d1e93fa34fe031908a3af056d47fad70d063 |
| working-v2/results/app.trx | 7ef5a3332d1e2bec2dbf4b20cc7b1b3f34aeae46fb2cf79ccec95f28d1384652 |
| clean-v3/command.json | e7c51f11d71385fb8a9d9a7f12931a7ae86942c1a22d4a650c166c89cdc48c2b |
| clean-v3/source.zip | 3a6ac6c44a38ae3b0b6672437b958ce1d0e82579560d2c75491683d0efccebcf |
| clean-v3/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v3/app-full-stdout.txt | 2ed9cde881ff16a6ba7e4515088d8809b902abef1693b09d6e18374855004de9 |
| clean-v3/results/app-full.trx | 4d71a55449d93b15f8f7075907db361e21419a0c254937f180bf0eac0133bed1 |
| prepare-diagnostic-v1.py | 32daf69a1aaf1a6523260d711e04a6516631568027f362f11cdf2d58effccb35 |
| CompareRevisionPollingTests-diagnostic-v1.cs | a6401fa7ddcba42a3d23b285ca1506f7cfa51c0e36ef7e60f997ae4fd72aa60a |
| prepare-runner-v1.py | dd07160b708d1b070a22e751a217d3721c95c31651b62c8c46ef64ba319213f1 |
| run-comparison-v1.py | fb7c0d184c9dbe20e2ef8254abd6d6274b3a0a2e8d18e02dd0c86d4ea67d1365 |
| install-fixture-v1.py | bd859b4c19ac191f9f2f61d3fee75d630f743d0793c65fe3692e71c39a819e92 |
| fixture-transition-v1.json | d8e5b527d32496dbe207edfedf001fbad61ffc97321e760516252f067d103fd0 |
| query-ci-v1.py | 0a6195dadc4a9344b29760eb4f626f8dacb711d8c7da8f3bcb950ea982d139a3 |
| ci-query-v1/observation.json | 8002d7f9aa6ad4890927aee8ba2abf1acb94f7241833ea36459996fa51eac413 |
| seal-comparison-v1.py | 9ad9f1e6f00a5edd0e8691d32276d1313641e0d175fd04ae40eb4efdb736e10f |
| independent-comparison-clean-v1.json | c031c6f352d4075e52954fa088c451ebdf3a99e2c56b9a8a7429c8a5561c2705 |
| prepare-ci-query-v2.py | a43519a5944fc25bb76cc39910a2d649141d65f383730d5dd2093df722d4ce5a |
| query-ci-v2.py | bc183ff6314f301f6a73ed5c1b562c3c6bd3e64373505799392e5697c7844645 |
| ci-query-v2/observation.json | ec7cc0239bb31192e091579cd4a9468ac8b8f9464a692bae9dd3dc71f5b16999 |
| prepare-ci-query-v3.py | a3d1f6b44e409240fa9a97da7a10bf56c8e0815437b6b84572fe801698563d62 |
| query-ci-v3.py | 07518b754b9ee7313f781790f4f419b1c66859ee59f37992656186fe627362bf |
| ci-query-v3/observation.json | 514bbd23419e87692de1a6562b9b14b60b07d55458fb4d9ef769b5e1a998cc8d |
| prepare-native-v1.py | f0ad7f3809a9a0807ebe4f9f757621be70104c230d17d2da7ee09371d8d9f1e7 |
| collect-comparison-ci-v1.py | 57a0c76d875b94c542406f78143b5ec78044e937e64c0523d0aa4cc08a57b190 |
| seal-comparison-ci-v1.py | 32f3e5ce6cf5bc4853e83eb641d4aba7520407934594df897cbbf9bcff46970c |
| prepare-native-reader-v2.py | b1079972638159a524574d69fe66523a3699465866620f1af4c6820b694945aa |
| seal-comparison-ci-v2.py | 907983bb6897f47df0a49d9fcb12ecacfd4bae23f6ff71eb92349ae7571c2ac5 |
| predecessor-reader-guard-v1.json | 45982a2afc251f7c0b1aaffd428bfcb8728feee12eb0611871e56c5078c490e8 |
| lifetime-working-v1/command.json | 6cbd673fcbfd5b0da4d4f53474ba4b3b2fabdda05fa9112e19dfc1ab40f234bb |
| lifetime-working-v1/source.zip | c13ca4d95c2d95b74fe638656be4fe733ff78ff022fc776f8cd42f1cdb285ce3 |
| lifetime-working-v1/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| lifetime-working-v1/app-stdout.txt | b11f16df72c2fba062eb00a7c814dbbf061d8f6fc76c623d28733c59338ac649 |
| lifetime-working-v1/results/app.trx | 0ea99b681b905170af973e52b27700bd259da01e1c9fa8266cd93ed1c3005621 |
| lifetime-clean-v2/command.json | 668e57708a444cb3090a1aeedc8e2467f59a8b47519f7f3ffb45eabf3e9da582 |
| lifetime-clean-v2/source.zip | f5089dd9b07e9a2d44077ad299ca4515f8d9e84891abfa29282275fd7f5ecd04 |
| lifetime-clean-v2/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| lifetime-clean-v2/app-full-stdout.txt | 0ccb7ba9225a6213cebf0d399dfc8143889466118b27a5857dbb818fcb8b7f7e |
| lifetime-clean-v2/results/app-full.trx | b159130799939adc8fb755e9f7cd39d03cabda04d76644bb294b9d8f36245d07 |
| prepare-lifetime-runner-v1.py | 795a2afa1530d1469ee7e4e4118bcec733130032070d3f0a28db13722379dab1 |
| run-lifetime-v1.py | 4fbe325e39a0dbf97159bfa8559ffaa673a8ec608553d57135783e844fb4abb2 |
| seal-lifetime-v1.py | 3bef4932444dec85e875bbac063cc716027e1a0695788416ae21c487975a0fc6 |
| prepare-lifetime-reader-v2.py | 8eb1b7ab39dec29c18c650b595183960ef629b199863b02dbf0c7deeacb01fde |
| seal-lifetime-v2.py | c9885cb4e3b9ade0f2b4924b7ac71592cc61bbb5cbf189de1f5c6f0aedd44702 |
| lifetime-reader-guard-v1.json | c55ffee21c2b98f8e69a72d525f95bc929a3aabcc842a69618f636c87c48e6f7 |
| independent-lifetime-clean-v2.json | 4aafd44b4678faeab5eb66b124d20bdd8bb9a5c0bea6783ad7baa253af701c79 |
| query-lifetime-ci-v1.py | 9d2ac036aa69e23942104eee40dd26b294d39eebf090f4db369fe2c1a3d6bff1 |
| lifetime-ci-query-v1/observation.json | b4ab6346b86352d71ce94b0acf80c8f81a10227291fc96d02854720f3452cf3c |
| prepare-lifetime-native-v1.py | b685274e336aadb568fe6a9399c61502069c46e02d54f848864bfc91338d6dea |
| collect-lifetime-ci-v1.py | 9d0bfaebd77277a4c3b0f4ff39d80510600ff6900c9136d89f6ba939008200e9 |
| seal-lifetime-ci-v1.py | 37d5091f88dc61a3ce193afea6661f4fa09d1334ae83b9fa984ad4820f200e95 |
| seal-lifetime-native-results-v1.py | 07e7c841fb4baf61d22eb95ba3c3731670f9bb714c1092ff151fa08260454976 |
| prepare-lifetime-native-v2.py | 856defb7913d2689806dbe2f52cc1df879a1f8f909390f2b1daee34495247b16 |
| collect-lifetime-ci-v2.py | 9d0bfaebd77277a4c3b0f4ff39d80510600ff6900c9136d89f6ba939008200e9 |
| seal-lifetime-ci-v2.py | e4175dda330b0eba7f35561ae528e54028dff4dd0d73e0449744d87df9136ec3 |
| seal-lifetime-native-results-v2.py | 0cd9748665e7087a47a306dad69a700f605494b32a8f4a52b1d1b3cd9c36c023 |
| lifetime-native-seal-command-v2.json | 808a5ccf2cf6a673c688fab681ecbd52170f86d88ee7b94476172e2c794d1bd5 |
| lifetime-native-preflight-guard-v1.json | 133cada16f4c45dd361c63ece03d4453b91e03915784653581eebf523c3bbd76 |
| prepare-lifetime-native-v3.py | 51b49fa9392f70dd5e091179935c22d75f9610f166e47a7ceb5f9e9536ae1b05 |
| collect-lifetime-ci-v3.py | bdf19ce427284ffd490980a37ab9bad5abf6ab97aec7cab2af16cd88609627a1 |
| lifetime-native-finalizer-path-guard-v2.json | af157ff19778b1aad795102f5393dac8d888cebf4fa55e174e6169a2e15d555c |
| prepare-lifetime-query-v2.py | 561b45a75175d4dcfd02f306f9277a7251f86a07e06642c4a98f30d1565c52d4 |
| query-lifetime-ci-v2.py | 1c9bfa6f06d1bb4cb02f6aacb9647abcba08a7040dec2342b7f51bcd13d6b517 |
| lifetime-ci-query-v2/observation.json | c73065cd24535c1fdebeaba06bae5f505cd1976956873ef974ecbf0595a93d63 |
| prepare-lifetime-query-v3.py | 3ccd2f057c23ba82cd44deee7edbf720b78078afc75612809c9429291e72f4d7 |
| query-lifetime-ci-v3.py | f44259dcf36571c7e9ac72c56918a08851333c9b93feb4a9ffb3fac7995d9371 |
| lifetime-ci-query-v3/observation.json | c73065cd24535c1fdebeaba06bae5f505cd1976956873ef974ecbf0595a93d63 |
| prepare-lifetime-query-v4.py | 57d16fa664e1ee52271b8dc7bdcefa8daff3bc6b634d5a3e3b34aae347610b0c |
| query-lifetime-ci-v4.py | 3ca760ac5ed03f5654eec9913d2497d9cfbfa04914193e7185ebf40c1a48f4f9 |
| lifetime-ci-query-v4/observation.json | 307b19188b3e5f633e7320ed5f11f769d93f7f627c88de3a834e0f0147cefa1e |
| prepare-lifetime-query-v5.py | 6826c23447577dadd3aea677893d059016f67e1ef0bed25a522321797467b30b |
| query-lifetime-ci-v5.py | 31c616b0cb9fc0e187ed7166347da0afefddb9c585cdc1a2a5247faa2ea01303 |
| lifetime-ci-query-v5/observation.json | 78c2da1e41b98736a580abe9f329ea06d1769feeb9b9b64d2e67239e50bc72dd |

Private `FileCatReleaseEvidence/ci-37731958789-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 12f7e3c767430365909c609a05aabf6f6b5eeb7d307335cb4741e36e416ea94d |
| independent-restore-ci-v1.json | a7f97894f60f06c6a0c76f94dc55cd4c189e6c78b63b357fe8ff7743e0f2018d |
| run-native-stdout | c78ec2c457854c56a2c46cccd183c884d12e3ebb37e66f04b64ffd81453143e8 |
| jobs-native-stdout | dbbe98c5fe268906210385a916b94a42787c9a384396a713a9e3e2e82c2dee06 |
| artifacts-stdout | 37e834c6c52b2d8cfa5b4b256fa103a075ff341979af757ec9126f3c3886c7b7 |
| independent-comparison-ci-audit-v1.json | 788dfc5831795e9949cdf40bedd8259446152c8ff08ecfc2e9a6796e7b3a5a86 |
| independent-comparison-ci-audit-v2.json | 792aa6e4903c8eb9a86abcf92c5d8586f4fbebb2000cdf1d5c8cb9615cc1ae4e |

Private `FileCatReleaseEvidence/ci-37738707924-assets-attempt1-v2`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 5f84158b822d812eb7e7d0204270a2762ba22398dcacf160ad09652689ab05f4 |
| independent-restore-ci-v1.json | 64addea0de07def3dce4bfcddefea974b54bcf5919759de1b809df1214ac4efe |
| run-native-stdout | e6321312df1780da0e091bf7a8601a38d08b9a59ea3b0f0efc6b23798a73eb00 |
| jobs-native-stdout | cc4f14edb69f4a34d66419e8c2d269a4f4b855e0b84392da16a1e7c62ce347cd |
| artifacts-stdout | 08b67d008e4e5d52390bb4e70c941db3b5063e24b567f096af80e440eb84f2cc |
| independent-lifetime-ci-audit-v2.json | ad5e4a965860bf990d4e539dc6d868dc0758f8d9b35ca8209293aab3250b2b5c |
