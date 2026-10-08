# I225 — common-transfer link scope and failed-copy cleanup

Producer `186d599a28da15668cb6f6d1689ec9aa9b949bd6` passes **352 additions / 4029 canonical expanded checks / 70 explicit skips**, plus full App **1191/25**. Qualifying CI **37804147852 attempt 2** passes all four required lanes with **1408 addition executions / no new platform skips**. The tests read owned source files and write actual owned local destinations. Provider listing/link flags and exceptions are controlled. This is preliminary component and hosted-platform evidence.

## Defect and correction

The corrected unchanged-runtime baseline fails 232 of 352 additions. Independent byte/lifetime/progress observations retain 60 controlled link-target opens, 156 owned staged leftovers, 86 unrefunded-progress cases and 16 repeated disposals. Counts overlap; they describe controls rather than a sum of lost user files. Every original source and prior destination remains byte-for-byte checked.

Common provider transfers now skip selected, listed and resolved-reference links before reading or navigating their targets. A selected linked root remains uncompleted. Tree copies can complete readable members while reporting deliberately skipped links; moves retain an incomplete source root. The archive diagnostic says that links inside archives are not followed or extracted, preserving its existing policy and regression test. Other provider links tell users to open a target explicitly when its contents are wanted. Synthetic EntryFlags.Link observations do not establish native link traversal or physical alias safety.

The copy owns a source until disposal has completed. It clears its ownership reference before calling Dispose, so a throwing provider is not disposed twice. Disposal happens inside the staged-copy cleanup scope. Unexpected read/revision/partial-evidence/disposal and read-back failures remove the owned prepublication temporary file, refund copied/verified progress and propagate failure; cancellation keeps its cancelled outcome. Known IO/access/data errors keep their existing handling, and a skipped known verification error discards the copy. A prior destination remains unchanged. Successful source/destination byte hashes and completed-root claims are independently regenerated.

## Controls and retained adverse results

The matrix contains 80 root/nested tree controls, 16 selected linked roots and 256 failure controls. It covers empty/65,537-byte sources, new/replacement destinations and Native/ReadBack. Tree cases include unchanged positives and file/folder links reported by listings or resolved item references. Failure cases cover initial/final revision, read EOF, partial caveat/ranges, disposal and verification open/revision/read/final stages, with NotSupported, ObjectDisposed, InvalidOperation and cancellation exceptions. Open/disposal counts, original source/prior/destination SHA-256, temporary namespace, root completeness, job state, issues, decisions and byte/verification progress are retained in each raw case.

The first baseline has 248 failures. The first candidate has 16 decision-message assertions: known NotSupported verification failures expose the original cause in the decision request and a discarded-copy summary in the final issue. A fresh fixture captures both; runtime bytes remain unchanged across this fixture correction. The fresh baseline retains 232 failures, while all 352 additions and 408 earlier targeted cases pass. A private preparation script initially refuses an exact-source transformation; its immutable script and refusal are retained.

The broad first candidate fails the existing compressed-TAR link diagnostic test while its destination bytes are correct. The original failed TRX/source/payload remain. A fresh runtime correction retains the archive-specific explanation; the existing test is unchanged and passes. No old result or adverse intermediate is overwritten.

Original CI 37804147852 attempt 1 failed when the Mac hosted runner was not acquired. The cancelled empty job, official capacity annotations, all 20 available artifacts, 11 raw inventories with no failed tests and complete original logs remain sealed. No missing Mac result or payload is invented. The identical-source full push-event repeat is the qualifying attempt 2.

Canonical Core passes 1978/61, maintained Remote 1440/7, affected App 611/2 and full App 1191/25, all at `186d599a28da15668cb6f6d1689ec9aa9b949bd6` with no overlays. Twenty qualifying-attempt artifact digests/every member, fourteen complete inventories, four toolchains and 92 locked graphs verify. I218–I225 repeat here with **2294 addition names / 9144 native passes / 32 explicit older platform skips**. Earlier copy/verification/upload byte oracles and all 16 current metadata preconditions remain strict.

The [I224 record](E-I224-upload-tree-evidence.md) retains its original Mac hosted-capacity failure separately from its same-source qualifying attempt 2. Earlier historical coverage gaps, fixture failures, timeouts and unavailable outputs keep their original source/artifact identities. A later green result does not qualify a missing earlier result.

## Remaining scope and invalidation

This does not close all of I06 or V02/V07/V08/V12/V23. Resumed-source lifetime, cleanup failures/permissions, wider provider/open/admission/account/actual-server semantics, native link/alias/atomic identity, same-size/reverted changes, check-to-delete intervals, blocking I/O/resources/reference, native desktop/human workflows and exact candidate remain. Owned file byte evidence cannot substitute for the physical-source safety hold or final artifact qualification.

Affected rebuilt artifacts receive new identities; original qualifications remain at their source commits. No VM/Mac setup, physical source, persistent Git/SSH configuration, freeze, candidate or stable publication changed. Human contract/custody decisions and explicit stable GO remain.

## Selected provenance

Private `FileCatReleaseEvidence/transfer-failure225-v1`:

| File | SHA-256 |
|---|---|
| baseline-v1/command.json | fabbbfbae2d81f1f0e35c897770a381f38b67520047f687f8e71d90f2d4b4322 |
| baseline-v1/source.zip | fce69f305ab8e7ee4e26d4a679c32d0e2807e2305c27d4cb62ebdff778a230ff |
| baseline-v1/core-stderr.txt | ef8864d1e6d384d92751cc099df2a41430804aa24ed2d275acd43b2cfc6776cb |
| baseline-v1/core-stdout.txt | 14885a91bbe2462be1dd5b68bfc27aec213ae37796432a96d7790d0193732139 |
| baseline-v1/results/core.trx | 27b0bf0afbd662e23ece76a51086d01e2f929b19683c6b74e44c80fe3efa7e3a |
| working-v2/command.json | 621335a11d2d00651023b7894dea29772422b5efd93cb48dad4bc073bf1c272d |
| working-v2/source.zip | d07bb476fc883a577748b8fff7405132d3efc6818a73b05ab72b85d83605f86a |
| working-v2/core-stderr.txt | b35eb1b04e5fce2d90dcbf513a6d3ae30152f6a39c3fe098e3b92b4a673cefbe |
| working-v2/core-stdout.txt | 7d057502af8b60318809a2670e1e9fddf45bbea0881b06606daf473bd158784b |
| working-v2/results/core.trx | 7d8d270150a2ae0415d81ddea78f1bacd4adfabc3fe87432908f143bbc892faf |
| baseline-v3/command.json | 989259452bec6be549fdc35129e2a4f3865b67d1d1073620ad4d4461fd91d67f |
| baseline-v3/source.zip | af23d3d1d41f40f3d7f8aac56e8ae04d196e76acfb8d488d3d0cb2c070f52d45 |
| baseline-v3/core-stderr.txt | 815f6bf5b27ddfd7fcac910b21b255be5d38069c9cd53787762b75a6d308721d |
| baseline-v3/core-stdout.txt | ae45440fdeb675f5223a5f094880c6f7773b666d53d263e209138c10739c8968 |
| baseline-v3/results/core.trx | 6387d4c94192486ef7b412bb345161126f9e31c3267e13af5d6ac8a56d1e0f98 |
| working-v4/command.json | b0fb796b4ad9d9a915c0d67aa8a1d73b29f4656738bf48b176b31962d263f0eb |
| working-v4/source.zip | d4f11f5b9f2236ff0b1b9e5bc64ac860531397ab1b1fe0abaa87394ca598de87 |
| working-v4/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/core-stdout.txt | 332c63622f7287ea25c8cb5a568ce53c79f31004fd2377333318b3e9fd501725 |
| working-v4/results/core.trx | 763308f84b99e3594a428efce27e97f80b66b7a8e8ea688183a5fa4008baf77c |
| working-v5/command.json | 6c1ab3809fe88a4fa6c847d2a190dd7127ac700d9204e0314ea7907a68164c46 |
| working-v5/source.zip | 300477ce595d0f71dc4eb82f382bfa488f143b1fd8b74ff4e782c28e96ba5c01 |
| working-v5/core-stderr.txt | 1190179420d87a08a6cc8e4907f1e630036db6ee224889463777f58dd4790f83 |
| working-v5/core-stdout.txt | 8ad2054829239c78c39c39f24aba0a0e31f1a00febaee81b877c5a15a6aea98a |
| working-v5/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v5/remote-stdout.txt | 5002f04268d5fe040077c638cc7ddce9423e3b102a49e93f3b1945f532b7f517 |
| working-v5/results/core.trx | d784740f9b71c0055cd3a93b223188df48ef38fc89af71ad564c3aae27305be9 |
| working-v5/results/remote.trx | b5b4f5f2dcf333ef045765a70b73459603f3aca8b7d9775160058cefbed6da99 |
| working-v6/command.json | 9ff555efc5b056aa0da48f8c2ac20fb7ed846754a3849bd799f8d6bc8f4f3794 |
| working-v6/source.zip | 32f21682a6803abf6d186eb760a6a1d74fc84fdca4a924298475d9cac5f46aae |
| working-v6/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/core-stdout.txt | 81cc5bd93f74c8e53c01ea34b31879820667b84517f7653b124ba1f212c15597 |
| working-v6/results/core.trx | 9893542300eb121b3e9f1e98a3bb1b7ca425d95237ffea37c273d9505477b284 |
| working-v7/command.json | 7e25b0249c613b82bf8c01ddd7166f0f37ab057f80d40f308887fa69f86da25d |
| working-v7/source.zip | 05a82ab504736cf760191e302c302f81d60f54aefa9c838d2fea786e01e9bfae |
| working-v7/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v7/core-stdout.txt | 089cb21fe23c3e316d83496da9f3ef1a4ece8415e39b1a996867876e24ed2d04 |
| working-v7/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v7/remote-stdout.txt | 85a27f9ced6ce43f4dc4db84a73cb420664cd858133eedfbf7e504ac9631bca4 |
| working-v7/results/core.trx | 13edb2c4ba7d23fb344ddb156c1837e8760caff22590681dfa076eba364a021c |
| working-v7/results/remote.trx | b891cec19856fab536ac5988745a38c83172aea6ce16480a8c867328eacf042c |
| prepare-failure-controls-v1.py | e595d63f651033b0fdea003b81e3961f19dbf895a188126adaee98ffef98ab40 |
| prepare-failure-candidate-v1.py | 5d54eab5f4b658ba2917ac095eb9fe91e7ebab91ffce3b8d667917fe15b5a2c4 |
| prepare-failure-candidate-v2.py | 02f40de2b982e2ed1c923f30eb112ddc12f23046019b2385a716a0c78d203272 |
| candidate-preparation-refusal-v1.json | e303f44aa526c1d2d774753b1997535e0b69332f2fb91262c53679213e56e067 |
| candidate-preparation-v1.json | b9252ea06886e1e8fee5663c3d6cf41bc0bb2a0d6ac1b28381097734eff18477 |
| fixture-correction-v1.json | 3b131d1e25dae1805dfc07e7a627324c635357694d152b3ce67e874005ff8bc2 |
| run-transfer-failure-v1.py | 4c58ce8807100f5d76258acdbe114d44aaacf20d02c16dbc08604dd73a857c1d |
| run-transfer-failure-v2.py | 9a8e726b2693d0298fd11254b8a7f5807d259c488fee8a396678eb77e94baf83 |
| run-transfer-failure-v3.py | 46941f6b146beb6353cef30adda8d67138703f32db5ea962cdab58c2b9288a36 |
| run-failure-broad-v1.py | 00c8bacfe7aa069c9c1c58ca77896b83cf10c8584b48ec197afa70c811ed957f |
| StreamTransferExecutor-before-v1.cs | 16be8f383f18b7be5384e2eaf1947f893efcb4a03e5c613da14875862b35077c |
| StreamTransferExecutor-candidate-v1.cs | 7e347b77b6cc52ddbb310ca8551dc21bd992bc48ec9bd298ff9c66571d10db5c |
| TransferFailureEvidenceTests-v1.cs | 13f7daa7d84d532f04b15ce2b09c53979488d29461d053e53719f2e9f6284dfa |
| TransferFailureEvidenceTests-v2.cs | 33c10d11a07b54e452a4af9c1e78f8950544e6455bd1b03093004617b3af766c |
| preparation-v1.json | 5e8b0bfc111ac56115c6b268c66d81544d30ecc651f16598120465a52d999d3a |
| prepare-working-reader-v1.py | 5fbdbde246f65ee340d7c1670415d7a6decdd09d111590f2ad1213208850cb5f |
| seal-failure-working-v1.py | de6c5452fdcc055490e8646bc00604b22132770d87476ef3b3b5bad9abaa3fcc |
| prepare-archive-diagnostic-v1.py | 22f2eacada4a163273fe66ea914fd613c8e541372d51aef53459d9f41ca9efdb |
| archive-diagnostic-correction-v1.json | b2f6e81c8674c76f71273abe4b2477f55a2d081e2394b1a10d401ffee3d811e1 |
| StreamTransferExecutor-candidate-v2.cs | 964a50fd44996dd308d896b1c1853a6139f8d0a07a46f2aef8b764f00eeb1e65 |
| run-transfer-failure-v4.py | 8f842d852ac4fa67327b614a9c84f6f8c99aee19ae8e74bd632d7d2276ada87b |
| run-failure-broad-v2.py | 1471727a72a0b159a5dd51192f6c1610f340d7b5d6183391d3b1acc1c844e0a7 |
| prepare-corrected-working-reader-v1.py | ea2a9c94ba1429acf049ee12457e534fce899a56b0a0ed0ed9780149b87f0062 |
| seal-failure-working-v2.py | d47484981b8d0d1cf8829d471d1d0a424d3e1c9d388e63733b17fb4df8c63aed |
| clean-v8/command.json | 41c4fafba9900b9c7226bc496ea5a1feb2fd7a86e07969b7b8ef9f358b42efb8 |
| clean-v8/source.zip | 165eed341c298f583fa6c3de3a03e32dafbf0ff3e65697f184ce389f5e476d7f |
| clean-v8/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/app-full-stdout.txt | 0994ec02cd1dbea92d1dcffe6e9602829a96a93be5643d55896ba2484d235495 |
| clean-v8/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/app-stdout.txt | 386d6b57f276293e6e05b7fd5f02e43dac4e2b756e3becaa51193066baf3b345 |
| clean-v8/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/core-stdout.txt | cb6fba0adbf5eb120ef34d5ec223be777b8766f8cd95c51e85aeef1cfd168002 |
| clean-v8/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/remote-stdout.txt | 3200aed2082fea9c8c50cf6807404ae5e62dfd5169861d207b08f8a8eacc3e9d |
| clean-v8/results/app-full.trx | 3d5ea5ae88d97641758aa597e722a7d1187cd9f79241f18d7fb59e98435c10b0 |
| clean-v8/results/app.trx | a30c07f4f112dde0e213621ab90862cad0204070c819bd708fc9e7f0167a6a09 |
| clean-v8/results/core.trx | 569d0f3215389ae4c02d26821cebf3bfc525a774c476b85f7956889d673b30c9 |
| clean-v8/results/remote.trx | 423b33473357da6211da1f247b9844090242cb290d4d5f752e011b1caab9faed |
| prepare-canonical-validation-v1.py | 6a63f39a34d7b431f4cff8360f01f23f945e76cff91570f9ba9efadfb4d91a11 |
| run-failure-canonical-v1.py | d533c79e79e1b0ba62b7667745306cc78f09f120e172ceac679b3c1d997b0a71 |
| independent-failure-working-v2.json | 5994350178ecd936f550320ec8b00d714a7d498f68c2545861fa49b7c41ea162 |
| seal-failure-clean-v1.py | fe6e38b745c93e3c6eb37051a4fc011a90e33366ed4b6f48b85724c4dd844140 |
| independent-failure-clean-v1.json | f9ee59dbb71be9a6c41b087d7760e4fd260cfe48cff29847162a10da51f5de6c |
| native-parent-reader-v1.py | a3d5fed3067a48a5bbdc55b3aca57b4728a999e4b8276654ea7012eba1637f79 |
| retained-transfer-native-reader-v1.py | f9e24db7c187c6579cbe6f4d10fab679281de6934c837072b07f4cc5fff13a58 |
| retained-copy-native-reader-v1.py | 503d40958181dad1f4eea21b7157f12feff95fa457976bb1e990a903a33b04d4 |
| retained-upload-native-reader-v1.py | d805dcea8d399c1645bbb4910994f9ebdea8e070509900a894401b8dc440e6ce |
| retained-tree-native-reader-v1.py | b8a34b68404edadededf85a857acf2ddc579218f29cd6f5a62bbeb31baaef7db |
| prepare-native-readers-v1.py | 4f681f6e3c7c1b63ef4fc68c7033b322446ae62b7ef0a10a1e1b68f11cb573ca |
| seal-failure-ci-v1.py | 9bf729983dc1159baac5bfd939f638486e08d5c7a1fe3edac2fa1fca9d3053cf |
| final-native-source-v1.json | 18b59e0deb9b38f611fdcb6ff7df8afa9c9c61b17c51c5a4dc821cbceade91a5 |
| document-baseline-v1.json | fcd70ece57b63f2bbe710e787279ecedd4c6970f419247b36736bc88fd4f394b |
| document-transitions-v1.json | 96ab4f73774cfee1ec35a1d6322a9a61e13247d6a5dd300d2ebe5772e6d768f2 |
| update-failure-documents-v1.py | b3f6338fda9db238c304a3f3d306c64d2243b3da90c5a772f01d4e71d26544c6 |
| prepare-capacity-repeat-v1.py | c825b5c628a1c58b77a85a9ff80841aceefbdebbedc555814ac0e4c6f468c0b1 |
| collect-incomplete-failure-ci-v1.py | 704a3b8eaf199b8a89b0f085c297379004f69cd07f2fce873c3bb0548778c9ce |
| repeat-capacity-ci-v1.py | bb2ca0cf51025911551a0e67d6d00020238b1c9c981c8cefb8dd016cdbf36276 |
| capacity-repeat-request-v1.json | 812a368fb9d1b3119991836192794e90208f667b45e362114de7c0586b2d9e3d |
| capacity-repeat-stdout-v1.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| capacity-repeat-stderr-v1.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| prepare-attempt2-readers-v1.py | 61a2050102ac321ab0663c1ccb7ab971381db2d9b3cfd1b1f04cb951d4222437 |
| query-failure-attempt2-v1.py | f7456fa43e14e57ba3bca182e2eff8d9be9a31dc700ed43be17e5d71f69dd966 |
| native-parent-reader-v2.py | 21271120f006bddf4ff33e6983dbc5d7ea32ed905f2e51b6408c04a586f64ea4 |
| retained-transfer-native-reader-v2.py | 8dd399ab9916fddfdf6d8a8c4e4d6288332bdd3664cdfbba2606e71aeb697281 |
| retained-copy-native-reader-v2.py | cb4bcd967b146aecafb5b0bfaeaf95aa5b295115ee210ba4a8c1a3b0289534b7 |
| retained-upload-native-reader-v2.py | 9bd0b7bee0d1122401bb5fbace3e84a864882488aa2764d3ca78b719cb7bd4bd |
| retained-tree-native-reader-v2.py | 33d2c7f45374fe72d4639866ef69382ad583130f81341ffdfe1d1ad4b30c884a |
| seal-failure-ci-v2.py | 98ab180ba72acf6f2e96ef3908df41f9ad6892793f02a68378bd2687b19ec397 |
| update-failure-documents-v2.py | 59c275285f90cbea03ea57a914481ddbd2635b0294046a7970a2c674b2b1d4a4 |
| mac-cancel-query-v1/annotations-exit.json | a33cc05ee27e7be2ceb03c90bb4e04631652090712787c23062faed7a1bba952 |
| mac-cancel-query-v1/annotations-stderr | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| mac-cancel-query-v1/annotations-stdout | c1e13b976a0a2cf47b4b18a3d2870d54939ba29e6b219898c33cc45842a97649 |
| mac-cancel-query-v1/check-exit.json | 22c2d4d873b20b301e9e837d90d510cc368b99d7e1d9b57192ca2d18e79092b7 |
| mac-cancel-query-v1/check-stderr | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| mac-cancel-query-v1/check-stdout | 8fed3036b3213351d8900ff1c75e6a925deef5e0644c9cbc7e98076700ee3e9e |
| mac-cancel-query-v1/job-exit.json | d22ad68ea24d5000c45591cc33cff30f95ced06e2503e0d4250d0916e149ebb2 |
| mac-cancel-query-v1/job-stderr | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| mac-cancel-query-v1/job-stdout | e01fe7ee906786b0fa0be7d0f6e303ae17293a4d55e36ec90e27cbb79b7e7ba1 |

Private `FileCatReleaseEvidence/ci-37804147852-assets-attempt2-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 8e427c9bb654cc88f89a3b94d066d9714872b13564da11f9998b6f2808b66a6d |
| independent-restore-ci-v1.json | 6b6ad05bc0801ddb10de8dbfb601965118f85bdb404df1e96869baecd8ec0ffd |
| independent-failure-ci-v2.json | 9b7565b813deb978f1e69f89dc425b552826de048d596595b5a489982a355a46 |
| run-native-stdout | b90639322e79cb8ec35091efa9e7301f9fa758611af576397c9399f6758cd2b4 |
| jobs-native-stdout | 19cfd651f068f92a549a83958c08c574912500b4c3ae408c8bf7bb2060f8b68d |
| artifacts-stdout | 6c3d4d758ad96fd5344666a7cc61f102f8b9303e9379100d383fa5baa01df534 |
| complete-run-log-archive-stdout | 0ca314ed49bbbb3b84acfa9b0b53076f29295ecf7453f12590e9c032af396e13 |

Private `FileCatReleaseEvidence/ci-37804147852-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-incomplete-failure-assets-ci-v1.json | 784915da2bcfc4c92e112802ad53f7b965a278028f1c06ddffd9117802edef84 |
| run-native-stdout | 97138096be79286462dfce46a2f14711119327c5442581ff78b251ec7be7c116 |
| jobs-native-stdout | 05a56525f99aa4645947e0424f387c3891dba64414587f00605d6d0b6792042c |
| artifacts-stdout | 9bb339734c12b8a0a618dd4f87ec53bfb898bd2c43c3400f83be8cb954eda2fe |
| complete-run-log-archive-stdout | b2ede8c9ee11da99e4b4ecf9ffe17cea5fabb588f2cc0e57238b92c75ec2e60a |
