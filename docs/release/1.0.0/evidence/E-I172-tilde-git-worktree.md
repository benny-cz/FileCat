# E-I172 — tilde-prefixed Git worktree admission

2026-10-07. Discovered with the actual f502fd169478d0631ea4280adbb5b3f6457cda68 App component, unchanged at discovery HEAD 16dcf19d394bd85dfc15d807185da7226ab3e3ec. High path-admission risk; must fix under I16/V23 B10/V24. Remediated preliminarily at 8a57103154a75174e21e83371e0eca41c57d282a. Broader boundaries and candidate qualification remain open.

## Actual failure and narrow correction

The complete native Windows reader admits `core.worktree=~anchor/../../target-link` and returns Untracked for `hidden.txt`, which exists only in the owned junction target. An ordinary directory named `~anchor` lives under the gitdir; the relative traversal reaches a junction outside that metadata tree. The equivalent absolute/ordinary relative junction spellings are refused. Three ordinary/restored positive cases establish that the reader and installed Git work.

Expected: resolve configured worktree paths against the actual gitdir, then apply the existing ancestor-junction policy. Actual: an early tilde check assumes home expansion and bypasses that policy. Git's documented worktree base is the gitdir; the observed installed Git treats this worktree tilde literally. [Primary Git configuration documentation](https://git-scm.com/docs/git-config#Documentation/git-config.txt-coreworktree).

The correction limits the early home-relative allowance to settings without a worktree base. Worktree spellings, including literal tilde directories, receive the existing resolved-path check. Ignore/attribute overrides verified in [E-I16-HOME](E-I16-HOME-opened-rule-overrides.md) remain intact. This is a distinct complete-reader failure, unlike that rejected isolated-admission hypothesis.

## Controlled validation

Seven durable controls are added before remediation: three tilde/quoted junction spellings, one linked-worktree context, and three ordinary-path positives. Original production records four expected failures and three passes. Working and fresh clean committed builds each retain the same 89 affected Git cases: 88 Passed, one NotExecuted, zero failures. All seven new cases pass. The skipped share-capture case explicitly requires FILECAT_V24_SHARE and is not substituted with a fabricated network observation.

The working controller incorrectly asserted a total of 88 after the actual test process exited zero with 89 cases. Its wrong assertion remains in the preserved controller source, alongside the exact test receipts and raw inventories. An independent reader derives the correct inventory from the original 473-case CI inventory's 82 Git cases plus seven new cases; no test is rerun or result replaced to qualify the existing pass.

A fresh native observer compiled against the actual clean committed App DLL repeats the original six owned cases. The tilde junction now returns no admitted configuration or badge snapshot; absolute/ordinary relative junction refusals and all three ordinary/restored positives remain. Direct native Git still shows the controlled target member, so the refusal is FileCat's policy, not an absent fixture. The observer's explicit private transport query is instrumentation that bypasses admission and is not presented as the automatic reader.

The independent seal verifies all 1,068 canonical Git blobs/modes and the retained source archive, original 1,065-file baseline source except the two documented overlays, 423 actual test payload files, 55 actual corrected observer files, original unchanged component/observer inputs, raw command/outcome inventories and 176 retained files. Working correction bytes equal canonical committed bytes. Clean actual FileCat.dll SHA-256 cf4a63b48a64e0083f276ce3bcc9920e73d9388e5e988e515ff43111a82203c4.

Both configurations restore byte for byte. After independent inspection, only the two exact owned junction nodes are removed without recursion; target member bytes remain unchanged. No physical source, remote server, native desktop input, broker, user/system setting or Mac/VM setup changes occur. The observed local redirect does not establish network contact, credential transmission, arbitrary execution, races, Unix mount behavior or candidate qualification. Other worktree traversal remains wider I16 scope.

Original push CI [37555498358](https://github.com/benny-cz/FileCat/actions/runs/37555498358), attempt 1, is pending at this local seal.

Private `FileCatReleaseEvidence/git-tilde-worktree-20261007-v1`:

| Retained path | SHA-256 |
|---|---|
| native-original-v1/actual-tilde-worktree-v1.json | f59b95669f0c5708b81f53db749f4dafa625b1a5758e39af38a4ea63bc821f47 |
| durable-baseline-v2/command.json | dffcdd607c18895be4826e1befd6ca0d0ddfaafad7685ac16f77689e6fe55393 |
| durable-baseline-v2/results/baseline.trx | 691ec23352dc5e138fa0e9ee656765682b097dfa9b715c4f3167c18519df71b7 |
| working-fixed-v3/command.json | 7f495345450a3cddf30be8eeff1f569f4d55fbc8d8653977f6eeefe6618ac50e |
| working-fixed-v3/results/fixed.trx | 77fe285f1fc80eed644fd63eb54b198cf26c8d1572b38eaf2361c70e776d6bf0 |
| independent-working-v4.json | 2a12d2f2f646f72f193adf8598a6ee4afef2a965b7edf8ef1ce4384b7e8249a0 |
| clean-committed-v5/command.json | dd93ca235d0bc48f6f1065ee57ade318a26ac6c1bd21fd665db5ce6749b821e3 |
| clean-committed-v5/results/clean.trx | a0b8e1e9e68fde7da2f9572221622c9539f19d1a9d9c123412e0ef14109f40ff |
| native-corrected-v6/actual-tilde-worktree-v6.json | d928552949c7a880e973b7c6019f838917ec8f3518027bc9a88ff34a17e4aeef |
| independent-tilde-worktree-v7.json | 36ce402007da95982c24d48c640db625fca8f078a40b235ee5bb50b387dd9a6e |

## Original four-platform CI sealed

Original run 37555498358 attempt 1 at 8a57103 completes green on policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26. All nineteen server artifact digests/all members, fourteen complete TRX inventories, four compiler/tool receipts and 92 locked graphs verify. Each full 480-case App inventory equals the previous 473 names plus exactly seven additions. Core inventories remain 884 Windows/879 Unix cases.

All seven new cases pass on both Windows architectures. Each Unix lane passes three ordinary-path positives and explicitly skips four Windows junction cases. Twenty passes/eight declared skips have 28 distinct execution IDs; no Unix junction qualification is implied. Full App outcomes are 463 Passed/17 NotExecuted on each Windows lane, 399/81 Ubuntu and 401/79 macOS. All I163–I171 regression subsets also reconcile.

The ARM64 package's version-start check, headless rendering check and installer compilation pass. Tagged/manual package and draft-publication jobs skip; no final selected artifact, installed/native desktop interaction, physical ARM64, consent, network capture or release candidate is qualified.

Private `FileCatReleaseEvidence/ci-37555498358-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | f8ac901df548f0acf5f84663e173ae75bff15257dec7b2a00f69a7564b47135d |
| independent-fixture-ci-v1.json | 8c71f464b8aa22f0645d80baf1fb7be47af025fd8f1403061371f42f423b51b3 |
| independent-producer-policy-ci-v1.json | fe575352c60b5daec68178fe8dec6c24e691944df6f98bd8f83eee2429ce8efd |
| independent-draft-guard-ci-v1.json | 8ea47a26699e3bdaefbe29ee34c8586bf60859a43ee581681ef8629e9cefaf53 |
| independent-separation-ci-v1.json | 95e447482d8865b69e3d4d6476023492fd37e0b6493c54d48276203f4d9a8cae |
| independent-restore-ci-v1.json | 6d617dd4b59d3344e8c886a68cb2de9ea0e4cd7adbca9e9ffb2bc831ffa770be |
| independent-i163-ci-cases-v1.json | 34f29b4423b5dcabe2e75d97bcc389ca4cab6aadc24d66ec1d5c11286a774c77 |
| independent-i164-ci-cases-v1.json | 0c0d9864d02742a79ca11ea5df7853fa3f2ee0fa281559476280658636d7e5cd |
| independent-i165-ci-cases-v1.json | 895e06fd3b665c2ddeb4cd9f99a9637ff093a1cb0c50a5d01f110bf82721679c |
| independent-i166-ci-cases-v1.json | 158c0ce470b8561aa11c34722013a5df43e696b09f27260768025c318304c2a6 |
| independent-i167-ci-cases-v1.json | 8c1c03915bc89d58bb90db8194541c2ccfcc5568e1a93ee98a6aef1ff13d1aeb |
| independent-i168-ci-cases-v1.json | 0edda0f42ecf23ec38b7ab6f7b5ca1b4d3ceb82224e5acbcef436bce686bace1 |
| independent-i169-ci-cases-v1.json | f8e0aedd046b2e03e2c64ee65e51221ecfa28a7f052f3b0199b81320dfb30bc6 |
| independent-i170-ci-cases-v1.json | 454c3a6995729c5a1d4e97764535ae3dfe4b8c77e02066c15be0bb6ecdc1dc5a |
| independent-i171-ci-cases-v1.json | 7e30fa8bacfb0842aa9929d339accb62daae6a9b4f8c3acaa44952fbae546a91 |
| independent-i172-ci-cases-v1.json | 51b1ae56c98c78b51cb080288f476d94ae118ae612d23b3c5f0ad7f303ea61c9 |
