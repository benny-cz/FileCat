# I224 — provider-folder upload scope and failure cleanup

Producer `0b0228dac408062cd4792aa84cebb9d6f3f12586` passes **944 additions / 3677 canonical expanded checks / 70 explicit skips**, plus full App **1191/25**. Qualifying CI **37800005259 attempt 2** passes all four required lanes with **3776 addition executions / no new platform skips**. Actual source archives, payloads, owned bytes and raw results are retained. This is preliminary component and hosted-platform qualification.

## Reproduction and resulting behavior

The final unchanged-runtime baseline fails 608 of 944 additions. Independent observations retain 48 silent folder-listing warnings, 60 controlled link-target opens, 192 invalid-name provider admissions, 192 staged temporary leftovers after controlled failures and 96 phantom-root progress totals. Counts overlap and describe distinct control observations, not a sum of lost user files. In-memory server path handling does not establish physical traversal or a real-server escape. The source files and prior destinations are owned; provider listings, link flags and exception injection are controlled.

Folder uploads now retain every nonfatal enumeration warning, copy readable members and leave an incompletely listed root uncompleted. A folder that cannot be navigated reports an error instead of silently failing its root. Each descendant name is checked against the existing remote-name rules before item-reference creation, navigation or content reads. Empty names, dot/parent, slash/absolute and NUL controls cover both files and folders. Valid dotfile, Unicode and literal POSIX backslash names remain accepted; this does not qualify a Windows server's namespace semantics.

Declared provider links are skipped before opening or traversing their targets, including selected roots and flags supplied by result-item references. The job tells the user to open the target explicitly when its contents are wanted. This follows the existing EntryFlags.Link contract and architecture §8.1; no native link is created by these controls. Copy jobs may complete a readable folder while reporting deliberately skipped links; a selected linked root remains uncompleted. Member admission checkpoints retain cancellation before the next read/navigation. Provider roots are counted through actual leaf members instead of adding a fictitious root file. Local upload descendants also receive the same remote-name check before source lookup.

Unexpected provider/read/verification/disposal exceptions now discard the owned temporary server copy, refund copied progress, record a failed journal step and propagate the original error to the job. Cancellation keeps its separate cancelled outcome. Existing IO/access/data failures retain their decision path. No exception is turned into a successful partial upload. These controls cover prepublication failures with a reachable controlled server; failed cleanup on an unavailable real server still reports a temporary leftover and requires later reconciliation.

## Matrix, adverse intermediates and preservation

The matrix comprises 480 root/nested tree cases, 16 selected provider-link roots and 448 controlled exception cases, over empty/65,537-byte sources, new/replacement destinations and Native/ReadBack. Tree controls include warnings before/after/both batches, three fatal listing classes, unavailable child navigation, invalid file/directory names, valid-name positives, file/folder link flags from listings and references, filtering, parent rows and cancellation at item-reference admission. Fault controls cover initial/final revision, read EOF, caveat/ranges, disposal and verification open/revision/read/final stages with NotSupported, ObjectDisposed, InvalidOperation, IO, access, InvalidData and cancellation failures. Independent readers regenerate every case signature and expected source/prior/destination SHA-256, file/directory namespace, opens/disposals, roots, warnings, decision count and progress.

The first fixture compile fails because two wrappers omit IContentSource.LocalPath; its command/source/payload/raw diagnostics remain with no invented TRX. The first complete baseline retains 672 failures. The first runtime rerun retains 64 access-message assertions: FileCat classifies access denial rather than exposing the injected raw message. The corrected fixture checks the access classification; runtime bytes remain identical across that correction. Fresh baseline/final reruns preserve 608 failures versus 944 passing additions and all 431 earlier targeted passes. An initial independent reader refuses the warning outcome because it expects enum 3; the immutable correction enforces declared PartiallyApplied 4. That refusal/script/input identity remains; no production or fixture change followed it.

Original CI 37800005259 attempt 1 failed because the Mac hosted job was never acquired. GitHub's failure and capacity notices, the cancelled empty job, all 20 available original artifacts, 11 raw inventories with no failed tests and complete original logs remain sealed. No Mac payload or result is invented for that attempt. The full push-event repeat uses the identical source; attempt 2 is the qualifying four-platform run. The original query's attempt-1 guard refusal after the rerun also remains with its raw response and immutable reader.

Canonical Core passes 1626/61, maintained Remote 1440/7, affected App 611/2 and full App 1191/25, all at this producer. Twenty qualifying-attempt artifact digests/every member, fourteen complete inventories, four toolchains, 92 locked graphs and prior mirror/launcher controls verify. I218–I224 repeat here: **1942 addition names / 7736 native passes / 32 explicit older platform skips**. All 16 current metadata preconditions and earlier byte/lifetime/progress/selection/copy/upload oracles remain strict. Historical I221 coverage/refusal, I222 failed admission, I223 timeouts and every earlier failure stay at their original identities.

## Invalidation and remaining scope

[I223](E-I223-upload-source-evidence.md), [I222](E-I222-transfer-copy-and-upload-bounds.md), [I221](E-I221-transfer-verification-bounds.md), [I220](E-I220-transfer-version-and-progress.md), [I218](E-I218-interrupted-copy-cleanup.md) and [I219](E-I219-interrupted-copy-selection.md) preserve all original qualifications and adverse evidence. Rebuilt binaries receive new identities and invalidate affected artifact qualification. Common local-transfer links/failure cleanup, actual servers and namespace/permission/alias/identity races, wider provider admission, same-size/reverted changes and check-to-delete intervals, blocking I/O/resource/reference, native desktop/human workflows and candidate remain. No full I06 or final campaign closure is claimed.

No guest/Mac setup, physical source, persistent Git/SSH configuration, freeze, candidate or publication changed. Physical-source HOLD, contract/custody prerequisites and explicit human stable GO remain.

## Selected provenance

Private `FileCatReleaseEvidence/upload-tree224-v1`:

| File | SHA-256 |
|---|---|
| baseline-v1/command.json | beb01cefe9816efd58179fc6beb2496d2332e0b4f01cb52377c7b1831c7410c9 |
| baseline-v1/source.zip | 66934d98205a92d967947077d53391088710bd51c104264cb69c80a3c4d078e1 |
| baseline-v1/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| baseline-v1/remote-stdout.txt | af775ebf0f7d485280dfe624448b8cbfe4869aad582a655e2a6ebd1c35a3f791 |
| baseline-v2/command.json | 5ba5757332d853973bd96a358ad1ad4c9f4e9803a2c50bd50f6383106ce32770 |
| baseline-v2/source.zip | 17ab21834c0f55873aee3d26782895a2fc43c3992a0e30574bc4c435025a6f26 |
| baseline-v2/remote-stderr.txt | c378dc29915af2a116992c833a0670f7e84ba60d8abe760988a040cac0fd8452 |
| baseline-v2/remote-stdout.txt | c4049bd8833a6a9f6b1599b10d2a7a5c6fdb431def9e966ddd7d7dd2d80b04a3 |
| baseline-v2/results/remote.trx | 9484307e179fe31cc335b03d859ce501dee4ab6848ab41c47a64e9d112f4e448 |
| working-v3/command.json | 0cba2213533f75fe4f1e27b20db355a04f1b08c233febff77382049d36ccd22f |
| working-v3/source.zip | 29bf437554deb5ee246e796fa14fcb240dc1295c656b02c220b1e638dc166d6f |
| working-v3/remote-stderr.txt | 7016898bf13a1fb73cb0ea39e3c2dc0431a4050854684b57a0e06dfbb05ccbfc |
| working-v3/remote-stdout.txt | 7d342cf9b7db47f4daf5c0facf523729f23a5a4c89939909f2ae071444938936 |
| working-v3/results/remote.trx | 54f0403e491e5efe2a1ec02343ea581d745ce24721c0d2bc2d808fe3154a2dbb |
| baseline-v4/command.json | 2f120ef13e46931f310feef65993ecd6ac6b4d20d8cf442aad81f9f093160956 |
| baseline-v4/source.zip | 7511ab8e0fd0da2abc65b63444ffdb62e042f842e170bdf91f9e56201bc3a8d1 |
| baseline-v4/remote-stderr.txt | 77f0f82706ca6c2c95401ceb8f0b9e85e805dd32f3b56981c4ef2848e4b30a6d |
| baseline-v4/remote-stdout.txt | 5e67138409c127b15ee959dbf88ce954211ed2a9c4b78d5d205cbfc6cddc034b |
| baseline-v4/results/remote.trx | 832d672838e3fd24b1356e9b3a31d7c2506d57d7aaa641e11baa69c1d98361e4 |
| working-v5/command.json | d9d01e5c06bf27d8a12b77848b17a05c3ea9c5fdc4e63631fb8fc3fed8f7094f |
| working-v5/source.zip | 077a1db9075ab999a3c2eb136ffd4e0ab44fc9690f9514972ff70b8cc0d049e3 |
| working-v5/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v5/remote-stdout.txt | 484fbe94aa9ccf63c8e315836197be6d9e2cc14c7c3df5ae05ad3ec37b0014f2 |
| working-v5/results/remote.trx | af9bb1ccbe7dde842483ae729306df01d36e6c3ad1441d67e65b6655ea61132c |
| prepare-runners-v1.py | 11a227d31edc0bd653e68e13b6dbe4ca797b8a813f134d4e67df3ecf63b3dcd3 |
| run-upload-tree-v1.py | f411c55b06e8196851ca86c75c88e6f4feef2bb8fe881017b2f0b681c37b75be |
| run-upload-tree-broad-v1.py | 9ad19e8d607d28a310b78a7940e84fb9be3f9237c7d44b2cf489dbd73fd7f80b |
| SftpJobs-before-v1.cs | 4c6e9c5adfeb4c4229eec5ba97e81f88da10661a2a36e5741eabebc393248f81 |
| UploadTreeEvidenceTests-v1.cs | 5f5a5d8f0611db7acd9986983e21d2033068ea3d9b10969c633add2b6e2272bf |
| preparation-v1.json | 174196901ea1999b201799f305150966168f83ce25367027b1e75a79aab5007f |
| seal-tree-working-v1.py | cf6fbb6c89846e8b629f5aa4833eb6d9bec0760c6070e02fd57673d8bd834399 |
| seal-tree-working-v2.py | 7b94d7cda5894b79209f9c16d08283a61c3e577dbf8e1f03db1df47e73134f07 |
| prepare-proof-correction-v1.py | 2e8cee7203cf2ab864ae9a0b66e3f14ecc9b084a3b829ed7c5d8d44a4a9641b7 |
| independent-reader-refusal-v1.json | 5a08c940cc79e1fac77d09894e6248195b3a904df26681753dd39de6a27cdb91 |
| clean-v6/command.json | c057eb93be95eb16899145f8fc951327ef03f9ab809ecbb52844c3c45014031e |
| clean-v6/source.zip | 80df0107b4c4a6ca55eb58a65e7bda7ca4e1483c4a18cf6ad23dab058cc760ed |
| clean-v6/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v6/app-full-stdout.txt | feb3b139b9988e271df7e423da5dda03190bc92324abc2e3215db5fa810eb103 |
| clean-v6/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v6/app-stdout.txt | edf75b0920eec3987da893dd5885a03e921467f51c552d9c823480c1dac75546 |
| clean-v6/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v6/core-stdout.txt | 397159810770de84422b765c24a0a4e7208d802f3a129b52a3403edc387b9089 |
| clean-v6/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v6/remote-stdout.txt | 0c30d34cfb6ed9119a1a60a9278bcd4cdd11412a4952a9cdd1aa410089e8e046 |
| clean-v6/results/app-full.trx | 5d5c97e245106b02f375ddbe79075119611050c6a06907ec6d4a5de6e5ba5588 |
| clean-v6/results/app.trx | 5fcb1570a57dcab6c680577f3fd62c112303998a73f95eb2497324119df00e80 |
| clean-v6/results/core.trx | c8a3f2548df109da90ce50123236e8cae8e2a212a8525f14efa609a7f5728e9d |
| clean-v6/results/remote.trx | 94a11a37b3406997d62b7a863d3fa500598604f12e40fb70dd096946e563fef9 |
| independent-tree-working-v2.json | 6ae50fd0fc47bdfb71a07c192181b0d11a3fd08480c5ae83d15eedce59b932ea |
| seal-tree-clean-v1.py | 8be16983d3d73d7ae166854ec999a1e719e859f668f4d60355f0295820290a6c |
| independent-tree-clean-v1.json | cd992dec8905c06fec9fbcf2798ebef574094300999ea4a3f95e016f2019b248 |
| native-parent-reader-v1.py | f10ec033ab85731ecf1b7bdf5c19dd91d08ccefdf12c01ec8ae08144ce0ba267 |
| retained-transfer-native-reader-v1.py | a54a1a413bf82d3aaba82cdb99897ef62ef5debfcf142375d9e076c3eca7aa38 |
| retained-copy-native-reader-v1.py | 6b39cf7f1ad284a98b0ca3d5658a82523bf39c8466be811fa8fa54189b64afff |
| retained-upload-native-reader-v1.py | 187c6bda259d095fa6649f9c399ad2873a51edaa49cecaedfdbb6371da45e2f2 |
| prepare-native-readers-v1.py | 2b0ff276b38445782f05de168fa21c71a7e120fcd08b9f0d474092834419a6e8 |
| seal-tree-ci-v1.py | b47e7497ad175385271e2785aabb1d77673306c485b197fb689254cb8afcfdc4 |
| final-native-source-v1.json | 7879565d612ec5f6c34e5e4de6ce41b00fdce7b5c7d020dffd201068000b0646 |
| document-baseline-v1.json | 37bebe96061b80a66fff8b2bb40f1f7aa18af1460a98b3d4c34cfd11830561f9 |
| document-transitions-v1.json | 477402e2af7a161a8f7d6cd471501bdb04a7fec7bc558b0ade149d7050af9fca |
| update-tree-documents-v1.py | a3b5f1174a1109498fa605c8d472886730a0474f44e232c8b62214532e6fbf65 |
| prepare-capacity-repeat-v1.py | fa3291ab2c7d36b86057e630dd67d72cad5462681547f4ec72eaf8a667d21a5c |
| collect-incomplete-tree-ci-v1.py | 97a828316980d3a3c609933500e766ca0bedfba1245ecd583528c5488074bffe |
| repeat-capacity-ci-v1.py | f0233e830dcca8dbbf616962789c2461394f52772d1079fd80810b10860de5bd |
| capacity-repeat-request-v1.json | ed9e36a749447912020fde2e0da8341912b361cdaff29ffc183e04e2c2ddec70 |
| capacity-repeat-stdout-v1.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| capacity-repeat-stderr-v1.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| prepare-attempt2-readers-v1.py | 909af17aac0b5e170f776b580c7492740653b91f79f2a8d00e9d0a2b622cc114 |
| collect-tree-attempt2-ci-v1.py | 2e5f0de6714b7c944d9305272f610d9bf0b0da0a903ab16c8b2b75555862f1e0 |
| native-parent-reader-v2.py | 82671409fa4ae4b517ef1e348854089736f67273a54ca349abce7ae188fe7968 |
| retained-transfer-native-reader-v2.py | 8a2d3b937ae405395d761ee9140b0fc1bc7d51d1819c0802e34587c9bc1d2fb0 |
| retained-copy-native-reader-v2.py | 44b5d34a055f13720c9b733c10c2740758f7dfaf2138a0a568ef701997ec1c17 |
| retained-upload-native-reader-v2.py | 3796a83dd8d566ae61bcae821c7a4961c0e6371547d2facdbaefc33c9bfcb939 |
| seal-tree-ci-v2.py | 8a33090b1bfab1129dba4809b3537c64fe913279504b467a771cae15f9f6376c |
| prepare-query-attempt2-v1.py | a048a30e374862ee3216397068b2019723250b5d60459c461534ae5e42286241 |
| query-tree-attempt2-v1.py | 062ef7e2b48ef4c2ad7af587cff80c1896445b92f7b1995ced43fb02a9ad7e2b |
| original-query-attempt-guard-refusal-v1.json | b857a1c8745135bac85054842c41ff32a1ae86fc144b90288487be58b9499bf6 |
| prepare-corrected-tracking-v1.py | 3510502d6d5866239d3110b0860add914665a9a933495a31442695b96c5593f0 |
| update-tree-documents-v2.py | 63fa640b71e2f962c820a1afad2b5e55f5f6c7e7e0f610d9d3d048f5abf44be0 |
| mac-cancel-query-v1/annotations-stderr | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| mac-cancel-query-v1/annotations-stdout | 47cf3412a8997a87809259c1986ed4f5b9734f45932481b7d0f19b70cfbca8b1 |
| mac-cancel-query-v1/check-stderr | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| mac-cancel-query-v1/check-stdout | a9f467f03190f8a82f5c3869d44b8306111c36ae5d05763bce94c48c3ff1029c |
| mac-cancel-query-v1/job-stderr | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| mac-cancel-query-v1/job-stdout | 2e22546c9a8ca4efe2b9bebd9d11e34212a06ec343bcc3190ed1fbbb86655e3f |
| correct-attempt2-collector-v1.py | a72900e34bc0cefebcac44d5c10766fba7af27ffd19680eba203fe42ed0cb603 |
| collector-predecessor-refusal-v1.json | 7b0b04b241d343726332fdadf8de2a1126c917be76ea99389307f2bd2386ba24 |
| collect-tree-attempt2-ci-v2.py | 1d465df0883a59861eec5e9613628d7467b9f013b2d57299f9a2cbf0454218ea |

Private `FileCatReleaseEvidence/ci-37800005259-assets-attempt2-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 48b76f8e10c349cae4f716ce93ead383b7cb666fa9731980ec60cc041a9fb4ec |
| independent-restore-ci-v1.json | 6ec908ec0112b9b60fe2bb22a7efb4c9dcd53375efdef1e89eac6c0dc2cbf966 |
| independent-tree-ci-v2.json | 217c7810ae29926c4116a5144ff2a758c6bd79481a79b2edb7a060a14929548b |
| run-native-stdout | e0c735b81e201cd841f8c6312cc4c2b2fa968d136d496dd19dcdef8adef75d5f |
| jobs-native-stdout | 005032e67608b5614ab9ce8fbc6a3f6a07f5d6d426952f502a3737459d4fdfc8 |
| artifacts-stdout | d1a686a5f5be801a97573f12a5d6bba4b244e64b0785ddde09a905b9cafcd3b2 |
| complete-run-log-archive-stdout | eab31b550d3871cb8753caa8c1766e23f56a217116b06507ac552b5447cb580d |

Private `FileCatReleaseEvidence/ci-37800005259-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-incomplete-tree-assets-ci-v1.json | 04fa5f0d766bf54aadd8a3e4c3027104b091da695ea6e6f29101686fdd0bfec3 |
| run-native-stdout | 0e87fe635357f9216968697dfff9fcdf9d133a7fbb6dd942085fa53c68ec5ce9 |
| jobs-native-stdout | c7662ea2419f0e4100c35fb1f5481e93e68e1843d156b1b73764865b59425bb2 |
| artifacts-stdout | 4c67fe33ac8424a0fcc14331195ee366212d4baee43280ace9ddce1299965758 |
| complete-run-log-archive-stdout | ab505af5d66e96d464eaeda482e171d5e998851324b9ebef19cd80c6e6338780 |
