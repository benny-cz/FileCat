# I219 — interrupted cleanup includes files outside the recorded selection

Production correction `1f3f9923d438f769d3157c9b9a54485d51f30467`: **30 additions (22 Core, eight App)**, **1830 canonical expanded passes/70 explicit skips**, full App **1188/25**. Original CI 37748570675 passes every required lane and all **120 selection executions**. The combined I218/I219 scope retains **93 additions/348 native passes/24 explicit platform skips**. No freeze, candidate or stable publication is authorized.

## Defect and resulting behavior

The preceding 8fb5512 producer uses journaled folder pairs to inspect every destination namesake, including an unrelated file never selected by the job. Eight owned-file Core controls prove deletion outside a single-file, two-folder or 65-source manifest selection, including a prefix sibling of a selected directory. Four additional actual-writer missing/torn-manifest controls still delete a partial without a known complete selection. Four real App cleanup/run-again flows delete the unselected partial and close the journal; run-again only copies the genuine selected source afterward, so the unrelated destination stays missing. Two separately labelled synthetic count mutations also authorize deletion. Thus the unchanged final baseline is 12 passes/18 failures: sixteen actual owned-file deletions and two synthetic-record controls. Sources retain those bytes; unique-data loss is not claimed.

Cleanup now loads a complete known selection within the caller's root-count review budget, verifies its count and admits an exact selected file or a descendant of a currently ordinary selected directory. The path comparison respects separator boundaries, excluding prefix siblings. Unknown, missing/torn, inconsistent or over-budget selections yield a limited review with no deletion offers; unselected candidates are excluded before their existence/content probes. Known selected partials and ordinary selected-directory children still clean up. Root-path eligibility uses current directory metadata; initial descendant snapshots, followed link contents and atomic native path/handle identity remain unqualified.

Working groups pass 281 checks/three existing platform skips. Full canonical Core passes 1130/61, maintained Remote 90/7, affected App 610/2 and full App 1188/25; every preceding name/outcome/skip and all 63 I218 controls remain. All thirty additions pass on Windows x64/ARM64, Ubuntu and macOS. Both original producers' native artifacts and all prior qualifications stay at their own exact identities. Final CI independently verifies twenty server digests/every member, fourteen full inventories, four build receipts, 92 locked graphs, native access/sharing observations, nine mirror/five launcher controls and every predecessor outcome.

The first twelve-case diagnostic retains four real unselected-file failures and four manifest fixture errors: completed jobs remove the sidecar, so removing only the end record did not model a known manifest. The corrected fixture captures the actual writer's exact sidecar bytes before copying and restores them with the removed end record; it does not invent a durable selection. The corrected twelve cases are six passes/six real failures, followed by the full thirty-case baseline. Two earlier fixtures are corrected to use known actual selected paths: the synthetic Core partial-copy record and App kept-file-alert case. Their original behavior checks, names and outcomes remain. No native crash or human pointer interaction is inferred from headless confirmation flows.

Wider provider/permission/admission workloads, changes during an admitted root transfer, native atomic aliases/handles, larger selections, Shell/helper/DPI/formats, desktop/people and exact-candidate qualification remain in I06 and V01–V24. [I218](E-I218-interrupted-copy-cleanup.md) retains partial-byte approval and its original native producer. Machine setup and physical sources were untouched; physical HOLD and explicit human stable GO remain.

## Provenance

Private `FileCatReleaseEvidence/copy-selection219-v1`:

| File | SHA-256 |
|---|---|
| clean-v5/command.json | 02ad861a51730279b237e40c8da2937558876b03efa1a0616192b1181e73510e |
| clean-v5/source.zip | be5815ea829e0b1eb5f1eef2d114bf226c3c349f868340af4f1d9b9fc85f0531 |
| clean-v5/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v5/app-full-stdout.txt | 5a08b0190ba51be887fa7124ef05ac8d94023ff0bafe359efbb13c0cbb5f5715 |
| clean-v5/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v5/app-stdout.txt | aeaf6ebaae470bb0dffe7efbbc1539349826d780e83cd724a1134dfb6ddce553 |
| clean-v5/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v5/core-stdout.txt | 3a184129326f45e9f29d10d476ffbcdb830495bef9ef74452c78baf25a1dac29 |
| clean-v5/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v5/remote-stdout.txt | 835d07015e4267a27d43d22fb0a5259e275c0b66769976be871b093fd19f5670 |
| clean-v5/results/app-full.trx | b349640cf9a38d0672d2930f5ed8810c323a997b7b1b51a2ae24e74ba3e48827 |
| clean-v5/results/app.trx | ae9a8352c1a831f24d360c9222bc75c45e060f107bf58f5dc3db110afb4d27ea |
| clean-v5/results/core.trx | 22b3fbfb5847c1765e10b0a387148c03597add3deb560887f946a3e0a39fedaf |
| clean-v5/results/remote.trx | fee242c683eb674b75dd95848e2dd9e05d4c25486da3f375c4a4c5f2692a2483 |
| baseline-v1/command.json | 7eceb202c5ccfbd64791016792174740034170558ae35f3a74a796f9817a06a8 |
| baseline-v1/source.zip | 9cdc368eed8ab60188b5606b6ae8850991d8fd1dc09095cf74bda0fc6e2fa076 |
| baseline-v1/core-stderr.txt | 09bded9700afeb31032e8e313d86b04aaf755a8c84102da6ec48cab80ab36f77 |
| baseline-v1/core-stdout.txt | c044608a0b5e7d54cf8ed96cd0b5d0379c9626d7819914e92d78a3286124e0b5 |
| baseline-v1/results/core.trx | de2fefd365519bcb7bedc3ec207f98f40e4396d91c57c725ac55b781bacaba9f |
| baseline-v2/command.json | c91248a1e9c52329a66ac894f695bed480c378ed9945ca3e77bcef6cf88fa306 |
| baseline-v2/source.zip | 2e078c6011604aba24669455681c7a442e3b58b2cd766bd1bb826e52fbb9387e |
| baseline-v2/core-stderr.txt | 630d58944a696d2a5d83e1297465dfec1b29bb890d310c9eb79ad78ce373e295 |
| baseline-v2/core-stdout.txt | d6d06857406e182b3f91a8ff26c963ce9e534251f0c8ee88e89061c3ba1e19ec |
| baseline-v2/results/core.trx | 63383168f7d7a90e8e1bf66d20082e5cfe7221a5fdd71a59ec9a463901a6191d |
| baseline-v3/command.json | 362e4052b8891c285a8342857d21836d7388c2b803b12628802c85b1637c333c |
| baseline-v3/source.zip | d1aa811142e3fb7d388772ca64f6e3ac8e5d80eb73176a515a226e4f71e28ac5 |
| baseline-v3/app-stderr.txt | 66854913b72318974c5ba989aef31c484f339a54ea6deac7c3bdf5cbcfa13038 |
| baseline-v3/app-stdout.txt | 7d4ddd6761596ab7137c5b93883d3e37b06e26c79b95f56b38a47f37766eb570 |
| baseline-v3/core-stderr.txt | 118dd23047c46216e932e590f51c159ba43c347eb8f7bcaec7d33017a16ed54c |
| baseline-v3/core-stdout.txt | 349ab3acf8393ea077c9e415e61f023703fe82d1cdf590c190df4d4c2c673d33 |
| baseline-v3/results/app.trx | 6744e32a9ffae504a7bd62ee9c149f720c50fad69be862b52dbb80d29d35a525 |
| baseline-v3/results/core.trx | 6c5957c4fb60b10979b079c2843a3b3dfd660c32bd04db3ed6745c26a8046aad |
| working-v4/command.json | 638a1b172cc3be189ae70ee6c4efd73fe719d6c5b7f79ac31b8019aacfb05b6f |
| working-v4/source.zip | fcc5c37373e128cf236cf46832529879fde7401c3b79807a1005e0cdc6f071d4 |
| working-v4/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/app-stdout.txt | b7841d1466f4135a0711b8842c425f25982bb31175fbcb36c06bc6b5951b982e |
| working-v4/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/core-stdout.txt | 5f89e9c8cc1306df764898d7b1a15dc5e10a29bfa405d332fec1f234e3cf15b5 |
| working-v4/results/app.trx | 10215c2bfb18c5116ecc230089f85da4e2194abe63f757823e99c7c219eaa8ec |
| working-v4/results/core.trx | 51f0b19c66c06d9b9e7739a9db6ad8cc0dbbae82b82bca0d59487c235e127797 |
| InterruptedCopySelectionTests-diagnostic-v1.cs | feb23bc29c1239e58a0ffdec01ae9d7ac028cc667824326156ab4c094aec186e |
| InterruptedCopySelectionTests-diagnostic-v2.cs | 42f77491c78e36f723b4d9a832f63c8d4abc4a329edaff384038371c9d5b4920 |
| InterruptedCopySelectionTests-diagnostic-v3.cs | 56675a2cdb3f7c0a88ec114765335f604b7a3e86d9a2eedf811a4c60eb734abe |
| InterruptedCopySelectionUiTests-diagnostic-v1.cs | 2dcde7371a99ae8d5a0382bc7b2f26a0fbae4019401e3e9488efe3954246dbd9 |
| prepare-baseline-v1.py | 26476b5c34bdc6ce9b82604fbd53724dfdfe2e39cbf30580ebb1ea9683a24ca1 |
| prepare-baseline-v2.py | a51f578c4c4d6a00cc1e3e6e88de0fcc1d2b619ecd2cb5f7e90f91f334fbed78 |
| prepare-expanded-v3.py | 34d8860c70406c1f48f7a049a0beacf3985170d946bbba5276e77a3472dd4133 |
| run-selection-baseline-v1.py | 07bc1ae8e5f44fef287f68627f64b2220911b1ff958b3482f4b82cf4a71f1c1c |
| run-selection-baseline-v2.py | ee09c33eb16fe2649ac0e83d06c52426403aab16e1fdbe26a04f9608f6e75d7b |
| run-selection-baseline-v3.py | dd5ee74116cef3d48445e2773675543b5fba6f83c28bcbfef3d4eea87a90542b |
| run-selection-working-v3.py | 498d554b966c4741be20d9598478f07d3d841ffa914f2ad078365fef82a02168 |
| manifest-fixture-refusal-v1.json | 02f49d25a7a4dcd68380a1561e84d2adbe12234ffb8936a899fe5cbefa553e3d |
| seal-selection-working-v1.py | 51b836d7491265adb6f53885b3f5efee9a455a50069b50f769ec8137d18d85d1 |
| independent-selection-working-v1.json | b528bca5808a13a49bb03050f2d585c66139ff33ec219dc8735a1dd4f1d0431b |
| prepare-clean-native-v4.py | 5dd7c9cd26e8f14e2ffc7c0263d7392adf33e34d3448049ec867d26381087e52 |
| run-selection-broad-v4.py | 1774c2c087c164cadf6593e925d5afa7480a0b1ab072db897148f1597a65a6d4 |
| query-selection-ci-v1.py | 396a6f4ac285d8c9bea7b4decb9c2d3ab62f4c18c02d69bb6efcd4796086d373 |
| collect-selection-ci-v1.py | 78fa8018204b9f543c6b215615bf2c2cb7d0151a35e951f2754ec5ef1b3ccb27 |
| seal-selection-clean-v1.py | 33d72f337d489802723041a86b1e2fe2f9bec6dcb5d3692a3cab9a4018d6fa8e |
| independent-selection-clean-v1.json | 65741f91358b81e2659471107f27efa1d9c048bfcaa4ea075afd9832e5a3def5 |
| prepare-native-reader-v5.py | 2757b36bd70c4bf36a3e152d37d158b60dee6313b575e1d14fe5364ffed631b6 |
| seal-selection-ci-v1.py | 4453ac3a95d513334259a01f9ee025e8122a385fd32472b3ef2d039a516ab3fc |
| prepare-document-source-v1.py | 33d1ccb38df1c17b4d1e6087fdae6453552a73f5004ad0b30e1ed1719aa6540e |
| document-baseline-v1.json | 64b49b1c9541e6ecedfb7fa75d0133163d9542e9e7c7ab509dc519b9242b6c6d |
| source-review-v1.json | 3fc2db646faf332ad09e53b20795dfd96619d213ae9e59f96fc7dc4cda1eca99 |
| update-documents-v1.py | 9b5b94bdf55f390c71a66f595904ee354e11d5eec6dfe4ea03ea52002a926c9e |
| document-transitions-v1.json | e66ca4f2adbf11758fdd2042d620c1666ea9af40f2bf24dbd83e76589e326ff6 |

Private `FileCatReleaseEvidence/ci-37748570675-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 2e215f8e5d7a9edaf7b21474128b5946b6955d79dfdf0b7830a47f3023afe757 |
| independent-restore-ci-v1.json | e89882444991bcca7594900bb31733fbf411cd22c787c6d35edca0a449692ba0 |
| run-native-stdout | 87cbc9520d4a6829a43752d7f7f6bd4170c0f79af9e582b4b840f34c4856d01d |
| jobs-native-stdout | ffb71a06382a43941eba8e622bdeaa865164d212e3f59c8ede7977c8ec49ed22 |
| artifacts-stdout | f35887372ea1de984b650aac175d5a27bcd6515ab10d14154ecc959a060220bf |
| complete-run-log-archive-stdout | f5400ca31aa21e2a296d5c7ed3707c0a58980965d7322a44f2ef1ed48e087a40 |
| independent-selection-ci-audit-v1.json | 13d56f1d5c9371c85618b99534282dfeede28c002381642314ec36f58e2f5ded |
