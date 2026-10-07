# E-I172 — tilde-prefixed Git worktree admission

2026-10-07. Discovered with the actual f502fd169478d0631ea4280adbb5b3f6457cda68 App component, unchanged at discovery HEAD 16dcf19d394bd85dfc15d807185da7226ab3e3ec. High path-admission risk; must fix under I16/V23 B10/V24. Remediated preliminarily at 8a57103154a75174e21e83371e0eca41c57d282a. Broader boundaries and candidate qualification remain open.

## Actual failure and narrow correction

The complete native Windows reader admits `core.worktree=~anchor/../../target-link` and returns Untracked for `hidden.txt`, which exists only in the owned junction target. An ordinary directory named `~anchor` lives under the gitdir; the relative traversal reaches a junction outside that metadata tree. The equivalent absolute/ordinary relative junction spellings are refused. Three ordinary/restored positive cases establish that the reader and installed Git work.

Expected: resolve configured worktree paths against the actual gitdir, then apply the existing ancestor-junction policy. Actual: an early tilde check assumes home expansion and bypasses that policy. Git's documented worktree base is the gitdir; the observed installed Git treats this worktree tilde literally. [Primary Git configuration documentation](https://git-scm.com/docs/git-config#Documentation/git-config.txt-coreworktree).

The correction limits the early home-relative allowance to settings without a worktree base. Worktree spellings, including literal tilde directories, receive the existing resolved-path check. Ignore/attribute overrides verified in [E-I16-HOME](E-I16-HOME-opened-rule-overrides.md) remain intact. This is a distinct complete-reader failure, unlike that rejected isolated-admission hypothesis.

## Controlled validation

Seven durable controls are added before remediation: three tilde/quoted junction spellings, one linked-worktree context, and three ordinary-path positives. Original production records four expected failures and three passes. Working and fresh clean committed builds each retain the same 89 affected Git cases: 88 Passed, one NotExecuted, zero failures. All seven new cases pass. The skipped share-capture case explicitly requires FILECAT_V24_SHARE and is not substituted with a fabricated network observation.

The working controller incorrectly asserted a total of 88 after the actual test process exited zero with 89 cases. Its assertion/error and exact receipts remain. An independent reader derives the correct inventory from the original 473-case CI inventory's 82 Git cases plus seven new cases; no test is rerun or result replaced to qualify the existing pass.

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
