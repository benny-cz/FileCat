# I216 — interrupted transfers use source content changed after approval

Final production source `3f121edbf14b3f7abadfb9c4e5103a0bebf586f6`; current test-only icon producer `2dc1417804d8aa3936089bdd10a90e67565d401b`: **65 additions (23 Core, 42 App)**, **1730 canonical expanded passes/67 existing explicit skips**, the source producer's full App **1148 passes/one existing icon failure/23 skips**, followed by the test-only I217 producer's full App **1150/23 existing skips**. Final original CI 37734151010 passes every required lane and **260 addition executions**, plus four I215 controls and all earlier additions. No final candidate exists.

## Proved defects and behavior

The unchanged cfbc40e producer passes twelve unchanged controls and fails twenty-four real mutation controls: eight at the actual confirmation, eight during the kept-partial-file alert and eight while the submitted copy/move waits to run. Same-size/time byte changes and changed/added/removed descendants still continue, close the old journal and create the changed destination. Copy and move are both covered; actual owned bytes and tree mutations are distinguished from synthetic root identity replies.

Recovery now captures complete ordinary-file hashes and every descendant's available metadata, identity and resolved path on the source worker. Review is bounded across roots by 64 MiB, 1,000 entries and finite depth; unknown, incomplete, unavailable or over-limit trees refuse continuation and retain the old partial file/journal. Initially missing roots retain their established omission behavior. Links are reviewed only as literal item/link metadata, without opening followed target content. After confirmation, before partial cleanup, after any kept-file alert, before queued-job destination creation and before processing each root, the retained review must still match. Changes before cleanup keep the partial file and journal; a later queued-job refusal preserves the current source but cannot undo the already-approved cleanup/submission or reopen an old journal. A later root changed during an earlier transfer is kept while the first destination remains exact. This is a conservative path/content guard, not a transaction or native handle snapshot.

The first 7ee2c3c correction passes its complete sixty-control scope and original four-platform CI/240 executions. Five additional owned controls then prove its tree recheck opens a changed metadata/identity version or new descendant before rejecting it. They are retained at that exact first producer. The final correction compares the known item version and membership before each content open; same-version complete hashes are still compared afterward. A failed root-path guard also short-circuits the tree read. All five ordering controls pass in the final local/native scope. The separate check-to-open/mutation intervals and unavailable native identities remain unqualified.

## Qualification and retained adverse records

Working-v2 passes 45 existing Core controls and 132 App controls. Working-v3 passes 63 Core and 136 App but retains two active-read fixture serialization failures: anonymous fields differed only in case and could not bind. No successful observation is invented for those failed cases. Working-v4 corrects the field name and passes 201 checks. First canonical clean-v5 passes 1,073 Core/60 skips, 90 maintained Remote/7 skips, 562 affected App and full App 1,149/23 skips. Baseline-v6 records all five ordering failures; working-v7 passes 206 checks. Final canonical clean-v8 independently verifies every source blob/mode/archive, actual payload, raw output and full inventory; all previous names and skips remain, and every outcome except the explicitly retained existing icon pass-to-fail remains. The source proof does not claim a passing full App run. [I217](E-I217-icon-checkpoint-eviction.md) retains that raw failure, forces the double-sample fixture mechanism, reaches actual cache eviction, and seals the current test-only producer's full 1150 passes/23 skips with production/workflow trees unchanged. The shared bounded tree helper retains all I214 rename controls and their refusal semantics.

Both source producers' original CI artifacts are retained at their exact hashes. Each reader verifies twenty server digests/every ZIP member, fourteen full inventories, four build receipts, 92 locked graphs, nine owned mirror controls and five launcher controls. The first producer's sixty additions pass; its pre-read guard is explicitly unqualified there. The final producer passes all sixty-five. A native-reader preparer failed on an absent predecessor literal before collection/qualification; its script, correctly created collector and guard remain, and the corrected readers use the actual earlier producer. No original outcome is replaced by a rerun.

App journals come from owned completed copies with end records removed and explicit pending intents. Real cleanup/confirmation/alert/job flows execute headlessly; identity and literal-link controls explicitly identify synthetic replies. Ordinary file/tree mutations and unavailable/partial/held source controls remain owned and bounded. Native desktop/process-crash, atomic alias handles, followed link targets, larger/mounted/remote/device admission matrices, source mutation during an admitted root transfer, cleanup access/sharing, physical-source and candidate qualification remain in I06 and the campaigns. No VM/Mac setup or physical source changed. The physical HOLD and explicit human stable GO remain.

## Provenance

Private `FileCatReleaseEvidence/source-content-v1`:

| File | SHA-256 |
|---|---|
| baseline-v1/command.json | f943e1aceed897cbe2fd43c47e2a829345439537ec6907f4d9a4a5550096a01e |
| baseline-v1/source.zip | 797dd5408c84a060526bba6ef75342bb0681330c118c25254f04b2e2c8bb0ab1 |
| baseline-v1/app-stderr.txt | db4fc389ec03a115ae59dde1aa8858e8d54b029b5ca511561b4e4f6758b92764 |
| baseline-v1/app-stdout.txt | b5a4403d18e6d3dc272580cb56d370e12182eddf5996accc4e8e1c322c15006b |
| baseline-v1/results/app.trx | b5988c622a0a1f1159acb55abe6c1df83a235552a209614059fdaaba3b07644d |
| working-v2/command.json | 5fc9a500ef5583650d3554b55d21a97e2994938e3bc0959eefae6e8a50640c0c |
| working-v2/source.zip | b7fa495d786aedda6c94686ef083e05f5abc15346244626163c333347847ce7b |
| working-v2/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v2/app-stdout.txt | d3ab407e22bbc25632b7fc2a4061f2dcd35f72b3c34c33e466648c6faa58b702 |
| working-v2/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v2/core-stdout.txt | 6536e661e422fa392a64ea439f2238cfd71ce116750af6b96c7471d836e41d9f |
| working-v2/results/app.trx | 96f781b734f6ef101de198e92e32a2bfe805397f04106f20379e3cfae325c09a |
| working-v2/results/core.trx | 05a5666b35f91b0730418591670d34b5f57002f979eca90293413b49a2886f3c |
| working-v3/command.json | 324db04376c53e07aa8cba8179066982b32b0d2f2f909899aad383255a78aa6e |
| working-v3/source.zip | 9cb0dc04ec344d3b9502bfc308eb3c67ce887fa0afc903ebcc2395a9d38059db |
| working-v3/app-stderr.txt | c023436d6a2bd09b00da642168faea3ada0956e19dcea3289c536d227f541a42 |
| working-v3/app-stdout.txt | fae619d6c08b424e19de01e449cf856c0d16b320dede69c615f1ed58df9219ed |
| working-v3/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v3/core-stdout.txt | 742e8ecae3dd4d8fe32916036a13e2d6db62484f49c637cf12a3c5aca8708259 |
| working-v3/results/app.trx | 3fd65d46cc82b711972b6af5ea104f3bc5dc2831280efa16f6ff97141696f076 |
| working-v3/results/core.trx | 9131f915f9f10798f0d6390e7f9d46de286654bae9eabc4dfaa0f697f909cf37 |
| working-v4/command.json | e8304467a1755a304f9a789727eb6663380cc087c8a5ff8c1444f4b296b3e6d5 |
| working-v4/source.zip | d048d4f1cd35c15a135ad17e79f3e47a1f5d62031885de534a504bcf442902e8 |
| working-v4/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/app-stdout.txt | 5ad4ac1c39485e63ccd0c9a1d3dd4285f7f19ae110545997c833738ec2a8c66e |
| working-v4/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/core-stdout.txt | 000375dd2ca8b9ffbdcbc15bef6962487452a5bc64e7bc5af09c58f5ba6cf181 |
| working-v4/results/app.trx | 4a16299e77066f89c87722181d9171128634e4d88287d0cb0f0f395fdb103a1c |
| working-v4/results/core.trx | 00388b198d9f96a3e60a27c93ff248e92b103c57050bf14f05aa89204de1b447 |
| clean-v5/command.json | a9fcc1f06817f067599985cad0acd6db445570ddf151d0b9b496cc0676db3155 |
| clean-v5/source.zip | d10891bba14f9f19ca01bfae7aa09bce8077c2c72ca65d10eeca5e97494e967e |
| clean-v5/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v5/app-full-stdout.txt | c332245f04cd6b1e163cab14e130806b3fb792ac6f5fb9bc77bec26f054deb5c |
| clean-v5/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v5/app-stdout.txt | 3118b0b29ca3b5b6ed684e2e25faf68cb925d316a99e945ebe144f9d22385f84 |
| clean-v5/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v5/core-stdout.txt | 3ab27216afa7784cf8cce1fa3e522a1d6f8b90ad1565441d8f786a5420463a20 |
| clean-v5/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v5/remote-stdout.txt | a5c293e3f18d8ff43a620a6d3eaabb002da2332686ca28cfa01d1756ce336116 |
| clean-v5/results/app-full.trx | 02ca2622f542e65e587b9ffdb818764259e0186ea1995a7aea26ed977988986a |
| clean-v5/results/app.trx | 6d097901de9efa6f056ec1d2825666eb8234b9dc34296bedd2129737b14d0e24 |
| clean-v5/results/core.trx | d4ab20b130036a6195945b8434932f610a3c04afbe6534dd017eb03cbbec25d2 |
| clean-v5/results/remote.trx | ee4dd1b8a80ccce62c29a3f8533b40294ec5f7baaeb0027b9063e9aad2ddd04b |
| baseline-v6/command.json | c02c5bd26905bc334de2d24327fd562f09181159b4b4541c1cf849dadfc18492 |
| baseline-v6/source.zip | 0e471df5b8b193f85c67791abd5adb13c6e206c59d5877b9e40de0e5d2f69d2c |
| baseline-v6/core-stderr.txt | a8c1068d42d1fd483d1d488cb54d246d658d3ec0f2a39a99a5bb666ed9e3781b |
| baseline-v6/core-stdout.txt | ba4e7619f54e59b969fb703ee2566e10c3e851dd7cd731601c7b37d5369ed6b6 |
| baseline-v6/results/core.trx | 11a72bbf3b3bd402e786996602a173e80f21ffee320d9f8ec0bae759aa56675a |
| working-v7/command.json | 2f97a04269b0ebf32b8b9d097872c737a0d48166a23115647ac1995853ba6aae |
| working-v7/source.zip | e4539354af0ab74dff711f9c4d6bf69d00cb4283e9d19ac676788fbfd520c218 |
| working-v7/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v7/app-stdout.txt | bcc21075340ed63912629107a2e04cf058b8269650bdcf76b64a896406d65a23 |
| working-v7/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v7/core-stdout.txt | f2d05109eb41162f80c503c6c987e686a601e7db7510f838bd15997ba163c368 |
| working-v7/results/app.trx | 5585b4184b6d0bf8560b0004a09312a21af14e3c83f29a9f835cf79a86d1926a |
| working-v7/results/core.trx | 066a7cec999278531ff482735c0693d9f78bf9ddc88e6041940c7c5db6d6ff4a |
| clean-v8/command.json | c3511d0ea85abf3503b266338fec0e0d61dee5b2c67daeb5d2262fba07037e78 |
| clean-v8/source.zip | a720e61872219ad52246ee83f211f8b90c2e703426c123ec2c1ba9461142839b |
| clean-v8/app-full-stderr.txt | 747551f455d1e0173f6e00084f8d48d26c9d433671c093647812c51741f3be18 |
| clean-v8/app-full-stdout.txt | b3a31ca40871ea3e2f311e8ce31c0075f45e5c8cd2b8b00590613dff854a97e5 |
| clean-v8/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/app-stdout.txt | 16f1d681f5a7b8a3fd392a414db9e6d4df58d97f5e45208107bb6bf9b4ee8068 |
| clean-v8/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/core-stdout.txt | 4ba8c2fdd1d9f04936eda743576fe521a1cb51f5ae42767b634f0cf42caae04e |
| clean-v8/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/remote-stdout.txt | 00ce8c434ff34f5d6fe30fc1303cf083b994a075ada3f81fa9bf26972c832bfc |
| clean-v8/results/app-full.trx | 140d566cffb97b19cfaac1f4db8c8b0e99f49033717503057b0c3b8b68b3aa57 |
| clean-v8/results/app.trx | d932f27f98663f7d8969850c18c33b98e05b8ddd7c245de154bdca9843a3d44b |
| clean-v8/results/core.trx | fe868ebb0b6e0f0e411368550bd45bd73ab14ae061af9e5c5a30ac378131cecc |
| clean-v8/results/remote.trx | 530dfed9ede7de1863dac54743b583e2a9fcacb26b810ce9fb510e9cca795c27 |
| prepare-baseline-v1.py | 5fb05c68430890bea00fa0c8c943b94c314d3afa3e2a04ff5a69d07738525e2e |
| InterruptedSourceContentTests-baseline-v1.cs | 4ce5c6b75eb4509cdd1f9ef5ed6a8bfa9fc2ad90287f104b94291e27bf8ea30b |
| prepare-runner-v1.py | 99a0ff054e167f6da8fef64e5798dfc982f1647f0fd8542806bd7f6dbb020354 |
| run-source-content-v1.py | adb5f535798f77ecdfcada8cdb9dd7cfa387dd1649531790f71b4ace7c5027d3 |
| install-tests-v1.py | 5d6e1c7f6fdb7f9b4e7c35eac00197fcb48f489b924a6566a7e3d1db2a35a3e0 |
| test-transition-v1.json | d01ebe528d1648a98fd7fa96190d6bf1aeb40e513e3aaf0c1b4ee94dbee258c2 |
| prepare-working-v2.py | 51f8972de5e7577cf75ff4d7ee2e4bfd7a46e1c2ec92913da1a9b562164a7da5 |
| run-source-content-v2.py | 2459bea496ee2f5d913fa29b34d3c732236474045f446618f6c5d16bb2fff868 |
| prepare-working-v3.py | 3f05e57715b37d6181712d8b10e3d35cf1ebf0a77d06e82835413a7399ebdb86 |
| run-source-content-v3.py | d34f84e6243a266aa9f97b54ba57e2650a379f64cf453b71a2f315e8fff06ea1 |
| prepare-broad-v4.py | 7fe74f25ca13ea0373c13b709c42de2aba9a1a8c2ae3cd2b1cce0623adc2b487 |
| run-source-content-v4.py | d59c8627385aba627ee0da061b50ee5493868fe36d630d244c17570120d9d942 |
| seal-source-content-v1.py | 40e0c85214bc12db5aac51c06cc16362f165cbe3b093bf2d8faeedd42d861e21 |
| prepare-ordering-baseline-v1.py | ef312c10e17c34d09c328283afc450d669b7ce673b16b9e0b4273109576b88b8 |
| ReviewedSourceContentTests-ordering-baseline-v1.cs | 9c713945402f67f3e9987173be548e6a7dcdf0e92efb6b40aa6e71e6fd8828f6 |
| run-ordering-baseline-v1.py | 3c23935fe49f48e801294c4a927af07b30beaecaee0ad49f285f42d94c133f68 |
| independent-source-content-clean-v1.json | 82a7b8b0973661188605199067448f52ce8685cf424352cff5af8b0d0a4717c9 |
| update-documents-v1.py | f769566e9be0d1f8f58507f75a83874b7852bbfc1ca7fafcb62ccbdce586fd14 |
| prepare-documents-v2.py | 9a999650cb9e3a97b20329211b61f00cdb8ada5df5e0fb828aced06688da9ff5 |
| update-documents-v2.py | 184ce5ae440020194e900ec96281bbb959b4f9ce675e24fe28be7c04ff81cdd4 |
| prepare-documents-v3.py | ae125383e12586e3b73ddd9aa061c0a79363bd6697713fd19af7cf801ec205e0 |
| update-documents-v3.py | 465283551428626f4f3f1050b3ed8feb7ec5567f97107e19c6669cb3d1329206 |
| document-preparer-guard-v1.json | e77276b8858a6d5300a2f56992b992b61e781dbda26d29cb33fe375085738afa |
| prepare-documents-v4.py | ed0e21887b22f298b919036b66570e9be88d59f7e84087fc21c3cc5b53a7331d |
| update-documents-v4.py | 57640f78784824129ea4900d0cc0b0cd41543981d99fab791e1fd599b2550d2d |
| document-transitions-v1.json | 59deca7b0cdf168d08a0fe334ce52e9dad203a17f729805d7da800ccc2ba9d87 |
| prepare-ci-query-v1.py | 2e1db731dbd4f6cc159ac88a7a2226741386ef2122b48255cd02854ae189f24b |
| query-source-ci-v1.py | 19b4d1326519a9cf8822e20266ef2deebfd590291788932ab4f5093626cd57d5 |
| ci-query-v1/observation.json | 9027e75d506d7a3b0dc941eba1faf81dce767c651b30ea1b3263bfa946f8309b |
| prepare-ci-query-v2.py | 6c9cd86334e25c46632310fb875839484775a10086d2a769edde32ac145c2e6e |
| query-source-ci-v2.py | 97caa3393a4a2278fbc8631552a2df23a1dc365ba07166ccdf4053de61e9deca |
| ci-query-v2/observation.json | 41bec8f9118a9fbc53e2e7b974eb710d24bc2f8d183320e6b8df1157db1c8459 |
| prepare-ci-query-v3.py | 209eb6b9dffc381daf13a83b254c184ddba75156e13754c19d7a33b46a358a2b |
| query-source-ci-v3.py | 20bae227a75e06fe8f8645fbae49465c3b88e077bd1109978fc9ae92e727ee01 |
| ci-query-v3/observation.json | 40201f01f6c442c4259eca1ead91021be7c1c50a19b8bc778cf81aa390a2d5be |
| prepare-first-native-v1.py | 6115230235778fa4d5053eb6a737d4da89d86b793aef240c331e7a0cbd47b140 |
| collect-first-source-ci-v1.py | ab6dfecaac8182c055de51e109e9f363bf0b1f340a20253d982e6a9efcbe772b |
| prepare-first-native-v2.py | d8341b03b47be1ce66b8ee9228724630358cec2ae1863a1a6107fe9238a8f9b4 |
| prepare-first-native-reader-v2.py | 806274ddace9084e00ca010270a6571a8db18306403a782edebfa010edcfd50e |
| first-native-preflight-guard-v1.json | 7f587b7de2ae499d9441aea33301ed7a51ff916cc5adb054539f199ecb1e6530 |
| collect-first-source-ci-v2.py | ab6dfecaac8182c055de51e109e9f363bf0b1f340a20253d982e6a9efcbe772b |
| seal-first-source-ci-v2.py | 1aa5e22ff9d071adfa3059fff84eef643c709a5ec2cc690d34405909df891d01 |
| prepare-final-ci-query-v1.py | 7855c8f421e86f0801d877077519aae6b369c6ad4202ae1afef7170814962d68 |
| query-final-source-ci-v1.py | 36bd012da95e3c3f6b9b0a97a176a1590b6d73168a6e1551b476550851538212 |
| final-ci-query-v1/observation.json | 89542d084fc53e5b6e26bba14ea499ee3593cbef4dcd31fbb0a642dc2aad6dcc |
| prepare-final-native-v1.py | 8b11c23dc35704dd2a4d54b9e7dc6464e30f77bbe8d565e5c8133a7a5207ad4e |
| collect-final-source-ci-v1.py | 41de9100079752beba19e2310bb431c6bb20d90f5586a3e2e3d8fd0a96f7e08e |
| seal-final-source-ci-v1.py | ab6ba0b9bca975aa8694c536aa3c11eb50932e38f1756351286db2cdfc001916 |
| seal-final-native-results-v1.py | 83bd9fc4a2d870b1345b6d3389a771617f166c8949c7b0a58c599ab5907de09e |
| final-native-seal-command-v1.json | 6d55eeb09e9501d1df4cc56ac9991bb5311977328dec0970170add52f3e62ced |
| prepare-final-ci-query-v2.py | 66b3ae826f9a887083fa7e50a80e533f5e1e1f08db6b0b48a06bc9f10bae9541 |
| query-final-source-ci-v2.py | d2c56c6031ad16466f61b6abe70af8a184ac770a768336bb4ace61313d418ecd |
| final-ci-query-v2/observation.json | ef3be408587ca51ccd75a56b812406828cbf601d27508d4829e3b3a69a220351 |

Private `FileCatReleaseEvidence/ci-37733244206-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 607ee25a7dbcf2076207366fa00ce35efe821cc513a1d791cd37f0b42c6eede8 |
| independent-restore-ci-v1.json | 8c444450d5dda996f7e53a608a2ef52586aea25f4e084a7787a3acacd0fa2f83 |
| run-native-stdout | 4ddd08ee42759dc940bbeedb4f101d6c0cc7a2cbd710c0d94f8163a2ef1155b8 |
| jobs-native-stdout | 5ca9a36fd81d9df7efbdd940d30f0a9fb1980687477ad77ad60cd5d967115a9e |
| artifacts-stdout | ccc0aef1fe6913df614301444e182c9222f3bd8eb38b26240b1bb70443d3502e |
| independent-first-source-content-ci-audit-v2.json | 6e5a0679da7ef768c32cda154b82574cfe0a1392f198aa4067bcea8cd1594679 |

Private `FileCatReleaseEvidence/ci-37734151010-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 8f3f737cd9e5891e6bf8a46488352465d38fcc47f8a84da3f61e556069bab05a |
| independent-restore-ci-v1.json | d6bb2c497db99015f2cca8db82d2da935c3ea7aba31acb08bf040516bbd9d677 |
| run-native-stdout | 89075feb4ba9df2df263866d1c9b59002aa341b0685292ff844e58d817dd8bad |
| jobs-native-stdout | 7fcce0dff72e76b47f254cb4c1adb04589aace2de205dbb60e0e58d67aac0034 |
| artifacts-stdout | 9b11946f1973631d07ce51caba70d70c3c84c1474aaa90fd386bfcbdaf718c25 |
| independent-final-source-content-ci-audit-v1.json | 44599ecca1df6d531ceee057759890564d58301b56f00e6f2452e3eaec92bb13 |
