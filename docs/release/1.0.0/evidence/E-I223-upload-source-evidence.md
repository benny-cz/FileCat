# I223 — complete source evidence for remote uploads

Producer `e87773f6fcf3b77b564577c3d355562d6ddfb0de` passes **384 additions / 2733 canonical expanded checks / 70 explicit skips** and full App **1191/25**. Original CI **37793311586 attempt 1** passes all four required lanes, including **1536 addition executions / no new platform skips**. All local groups use this exact source; actual payloads, source archives and all preceding names/outcomes/skips are retained. This is preliminary owned-file/component and hosted-platform evidence.

## Reproduction and resulting behavior

The unchanged-runtime final baseline fails 324 of 384 additions. It publishes 112 controlled copies despite refusal evidence, including 56 changes to existing owned destinations; 48 accepted partial copies omit their required warning, 44 cases leave temporary server names, four use stale listing timestamps and 32 retain a stale retry progress total. Case identities and actual bytes are separate; these counts are not added into a count of unique lost user files. Positive controls that fail only because the old adapter never queries revision remain separately identifiable. Providers and server responses are synthetic over actual owned files, the real upload executor and real local-file mutation; this does not establish physical-user loss or real-server behavior.

Provider streams now retain opening revision and known length, bound reads to expected bytes plus one EOF probe, reject invalid counts/short/overlong data, check length around each read and query revision at final EOF. A failed initial query occurs inside lifetime cleanup. Accepted reads make two revision queries, preserving server-query cost. Read-back requires the copied revision where known and exact copied length. Actually progressive archive members retain their deliberate one-MiB declaration slack, own damage/ratio checks and ordinary disposal/spool cleanup.

Partial-capable sources preserve their caveat/missing ranges and report a warning when appropriate. As in the common local transfer, read-back is skipped for partial-capable content because repeating guesses cannot authenticate the original bytes. This is explicitly exercised with VerifyBytesDone/Total remaining zero. Actual progressive damage enters the upload error handler, which discards its temporary server copy and refunds copied progress. Paused/cancelled/empty reads pass through the job checkpoint; a server must consume complete source content before publication.

Local uploads check the current source version before and after verification, preserving the source and prior destination when it changes. An explicit Retry first checks resume against the prior version, then retains the new attempt's local metadata for bounds, timestamps, verification, source deletion and byte totals. Thirty-two grow/shrink retry cases verify new bytes, exact progress, a second connection and a full restart; copy retains the new source and move deletes only that successfully copied new version. Provider timestamps use the opened revision when usable, falling back to listing time when unavailable. These remain weak metadata observations rather than atomic handle identity or same-size/restored-version proofs.

## Matrix and original intermediate failures

The additions comprise 216 provider controls, 56 actual ProgressiveContent/member-limit controls, 72 local copy/move controls, 32 explicit grow/shrink retries and eight opened-revision/listing-time controls. They cover empty/65,537/1,048,577-byte files, new/replacement destinations, Native/ReadBack, stable/unknown/lost/changed revisions, contradictory/changing length, short/finite-overlong/negative/overreported reads, query IO failure, partial evidence before/during reads, archive slack/damage, actual local replacement and retry mutation. Independent checks reconstruct expected hashes and enforce disposal, names/cleanup, exact lengths, warnings, bounded query/read/progress and source-retention/deletion decisions. Archive metadata, provider evidence, disconnection and the in-memory server are controlled; no real-server/native-desktop/corpus/physical/atomic/reference/candidate claim is made.

Every original run remains: the first 344-control baseline fails 284; the 376-control baseline fails 316; the final 384-control baseline fails 324. An initial source argument fails the exact-HEAD guard before export or tests. Two intermediate implementations hit the owned 300-second process limit; their command receipts, payloads, sources and raw stdout/stderr remain, and complete TRX is unavailable. No completed inventories are invented. Source inspection exposed a stale-version Retry loop; corrected bounded controls prove the resulting behavior without attributing unrelated historical CI scheduling. The first intermediate stdout also retains progressive-damage temporary names. The existing changed-source move test keeps its name and now requires no publication, exact retained local bytes and no temporary names; its original assertion body remains in the original source.

Canonical Core passes 1626/61, maintained Remote 496/7, affected App 611/2 and full App 1191/25. A working broad run before the final timestamp addition is retained separately; its 376 additions and all earlier outcomes pass, while the final 384 additions and broader canonical groups are requalified here. Twenty selected artifact digests/every member, fourteen inventories, four toolchains, 92 locked graphs and earlier mirror/launcher controls verify. I218–I223 repeat at this producer: **998 distinct additions / 3960 native passes / 32 explicit older platform skips**. All 16 current metadata preconditions and every earlier cleanup/selection/transfer/copy/verification byte oracle remain strict. Original I221 metadata refusal and I222 failed Ubuntu admission attempt retain their exact identities and are not retroactively qualified.

## Invalidation and remaining scope

Rebuilt artifacts invalidate affected binary evidence and receive new identities. [I222](E-I222-transfer-copy-and-upload-bounds.md), [I221](E-I221-transfer-verification-bounds.md), [I220](E-I220-transfer-version-and-progress.md), [I218](E-I218-interrupted-copy-cleanup.md) and [I219](E-I219-interrupted-copy-selection.md) retain their sources and adverse observations. Initial descendants/followed links, folder enumeration warnings, actual servers/accounts/permissions, wider admission, atomic handles/aliases, same-size/reverted changes and check-to-delete intervals, blocking I/O/resource/reference throughput, native desktop/human workflows and candidate remain.

No guest/Mac setup, physical source, persistent Git/SSH configuration, contract freeze, candidate or publication changed. Physical-source HOLD, contract/custody gates and explicit human stable GO remain.

## Selected provenance

Private `FileCatReleaseEvidence/upload-source223-v1`:

| File | SHA-256 |
|---|---|
| baseline-v2/command.json | 25223540e894ec8d0a679382db98e4ad9f165f5bee609f3b9d83b86632f0823c |
| baseline-v2/source.zip | ffbe1f0ee05535f768a50e461400d63e343d13650eb86e30b087e301c4165225 |
| baseline-v2/remote-stderr.txt | 62935cb240efef01ac7ed461c117c6139633d57b429512ad28b4c49669ff621f |
| baseline-v2/remote-stdout.txt | 9337e7bf7d1b0c50b1141e24067d6aaf05b21e20ec1a8f0e18c0357c71430670 |
| baseline-v2/results/remote.trx | 3b597e2e75cd7391b7264f70d1b44a7154d62e2d0e7cb2e87c1067433379ba4b |
| baseline-v5/command.json | dbccdeb6d8cb7c433c8c987655ddcab73fd0b21f8b647902b16a21e5af9c0c82 |
| baseline-v5/source.zip | 5f3f3e5ca7614643b73fccf771d98cd5e038327010ed55ce1b9008f52be9ff26 |
| baseline-v5/remote-stderr.txt | 014581a59c1edf8cf4995b09c847e0e7aa5ab9b30899d96c5db18e80b308046d |
| baseline-v5/remote-stdout.txt | 3e17815135db31491645f5b63f3a5d65358d7fa050d59f8a2b994c41b18845c2 |
| baseline-v5/results/remote.trx | c64a09557a1efee1e3b2bd586be46d5f1b14ede96a374f294a6a81da9f46045a |
| working-v6/command.json | a814dc42e76dfde4c552953639e7f908913e43d464b88f4c5fdf340f36f415c7 |
| working-v6/source.zip | a21d3f4058b0ca5c6748acaf24f683b624aa67e612f6cb1b315c43047930b295 |
| working-v6/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/remote-stdout.txt | 68fdd30ffcc6dafa61ae124e2ff2d107e20e8a8bfabccc6cc8b799d5f17e3e65 |
| working-v6/results/remote.trx | c9c970abc8d061ce9b531baae4b2b682d4cac825d20fff81f4536bee2ac1a257 |
| baseline-v8/command.json | fdc5c17e77e036d55d4ffaeb162790f2b9aa281396caaf8e27c97ad52ea2580c |
| baseline-v8/source.zip | d0a5035b74eb4b0d85ead8c31563748a3b0a5becb2963560e202a37c0fcdd1cf |
| baseline-v8/remote-stderr.txt | efdb16a1828d90714dc2f314bb85bea6daab92ff4dbea7ebb50ec1b76ec250f0 |
| baseline-v8/remote-stdout.txt | 0f185f22558bbaab76ec4406ecc27a34940e26627e4d560e0e76670a9d2d542f |
| baseline-v8/results/remote.trx | de4163e06875d2ecc360e3b6e0a27272d4ad9a51984e40d990247dd7cfb47594 |
| working-v9/command.json | 9268aea4c30b07a7325c4db6253bf7b65ecbad7fdc411091407a44910805b17d |
| working-v9/source.zip | 0964b24660ec150d5634a32c6af0d09033c32744469613e1aaba6182b5be2eda |
| working-v9/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v9/remote-stdout.txt | 36fc2721cd7db710192d04c52ae14e7ddbb74b1231560de57ff34ca81bf1e95c |
| working-v9/results/remote.trx | 750ecb29676eebc410ed127c30e552adb7afa8d275425d331df3a3d7a5c60c2f |
| working-v7/command.json | f12ed51528f087f3e75aa995f5ad43a2dd32f9ac8f9feea789e5dde35392e660 |
| working-v7/source.zip | 39aae23e059add1586566cb30eebacab1dd6e67416a1f94592df9862a7621766 |
| working-v7/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v7/app-stdout.txt | 91134e2506b9f92e2191dba959e9094c29f10eaeafb47a08b306cde37878a415 |
| working-v7/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v7/core-stdout.txt | 2b1825f0876c298d8616f3ad934614fce1b3d888e4715d68633a565812a180cf |
| working-v7/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v7/remote-stdout.txt | 66abfdc7986447d758ddab757ddfbedcf0ddcf5223ec2b801377e5c9644d2ea3 |
| working-v7/results/app.trx | 70a0dc38d3437f25c3ec0b3ae94cdf0ee6123f0149dc9ceb9aecb8afb2981725 |
| working-v7/results/core.trx | 95db457b93ee69d8a485755626662456c7a0b47e53d9ba1955ef85a1a101d119 |
| working-v7/results/remote.trx | 8b0ee7c747d9c2420be92066db581b4c698f58992fa46c52b9bcdb360b73c71c |
| working-v3/command.json | d5873ef3dcedb7e5eb8625a2505486f44d429de88db3cfb2ae725ae07c9a840a |
| working-v3/source.zip | 61440c0aaf01e3a1cbe62b710add8742976f877649e5aea81a13d7c64cc1bbd8 |
| working-v3/remote-stderr.txt | b8c1babb1e90726b285ee2fa98e40391613e397157d1967acc9c55ed6e0dfd4d |
| working-v3/remote-stdout.txt | 5c323c7255e921228116b4ebc892fb6bb7b8fd86b5636814039878c4e1e66f42 |
| working-v4/command.json | 2eb6909fdffe6499e546d1156d6fdabe8cb8d313f6b19116e196a2ec2ad50236 |
| working-v4/source.zip | af3f24aa14c769ca47e9991e5b49032b588ed0405ee13e494b2e69d3b0aacd97 |
| working-v4/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/remote-stdout.txt | 4e4e1cd3627c204b1e89a39f37a6cc3f13c94221e4994245d27d9010cce32232 |
| run-upload-source-v1.py | f4b5d8c5ed6820422734b650c9282771dfba02f9c0193fcf5dee10aa12b77c66 |
| run-upload-source-v2.py | 3f7dc8bf3b60594522651ddab3d28da3befcee5459f3b0b45d14e39302fe3e2a |
| run-upload-source-v3.py | ff021c630987e6c3d134672f7530e786b19f9778cc9501fff30588d9ac8a92fb |
| run-upload-broad-v1.py | e93383c3cc9dc46fbb3ad7a9ec214c3ff77973baa93665ec0b4e59c78c44054e |
| run-upload-broad-v2.py | 4ad0b480fc3874b341cf1ed3b8f83055e4dacda9c42d8d7a2230ec9a86a58732 |
| run-upload-broad-v3.py | 7c3e238e65093d987348617447f848b6fa44705f5f5c9a0a7f73c8cc0acbc870 |
| run-upload-broad-v4.py | 5e0719a83a923ab39589ad2860ef08b7a330c9169816b50e9793a3ffc500313d |
| UploadSourceEvidenceTests-v1.cs | f24ed00c51afbee68157e32db6d4449d3a5d70e60d8577aa0bd8ce4a0e555d4e |
| UploadSourceEvidenceTests-v2.cs | 879d9383985e3b9606c4b5c5a2cacec6b47f7a767d82ffb526f8c1241d1b38db |
| UploadSourceEvidenceTests-v3.cs | 83aef9f9e783e9607f650c9a8738c9e43abcce72e44b1267f2f46542da800d4c |
| SftpJobs-before-v1.cs | 82e98b97d45113d783882977782692bd38cfdf08d79fb82d56c0fa72af31aa59 |
| SftpJobTests-before-v1.cs | 67be47e2158980d60b517b9d2fb1c524235ab9ed0d021706b1763063ed6cd322 |
| baseline-launch-refusal-v1.json | d4dbf663200c1f926449192e2524f71410f0a28b3607a47c9367de2860ab6462 |
| seal-upload-working-v1.py | 40a24cd1667188dfeb5e343863cf1225113ca9eb03bef33b49887c82ce1c0fb5 |
| clean-v10/command.json | 6c802c215557eb080e4d6d12bec6208bdfcd70d023b840ad61b95c6fb1fccbfa |
| clean-v10/source.zip | 112a3d9cb21cdbed4ea1b5a898174b605ce7dd8fce707d596f7eede7b7d69871 |
| clean-v10/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v10/app-full-stdout.txt | 4ce16ffc005bebfdc462ed615fc1a4a6d7eb7989751ea5f792097bfdce7c02b9 |
| clean-v10/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v10/app-stdout.txt | 4a16374a1da37b5d14fd76961bb0204f3c042181a95d65766cb19c8cfe1a72f6 |
| clean-v10/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v10/core-stdout.txt | 06e97e4d6623027116f36c1bfd44c74d9c4c0471b338829438808e9f03de3566 |
| clean-v10/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v10/remote-stdout.txt | 9095e88b47d0e4cbed81f9a484aa50c254ad657c33736737ccd28c1fa110a4cc |
| clean-v10/results/app-full.trx | d090753787cba51f4f43d62bbc0381b5f3f1b83475087cd3d98545c8cc294e8f |
| clean-v10/results/app.trx | f281d5aab0714a105a0323417efa1dde45977cb035eacbeea64fb732da64c238 |
| clean-v10/results/core.trx | 7a0c8e6f0f598f5ea7108a559c5d275c5f37d82087b77ff846b0403e4f3c9052 |
| clean-v10/results/remote.trx | 48b30a669797871109fd407273efcecb2094e35e09a03b2eddd4bf7d7acbd9ab |
| independent-upload-working-v1.json | 0805b3a424142f997e8521578441604f8a659d8d2b9831ed0177633d1a7d285e |
| seal-upload-clean-v1.py | b1c436ae18775720abf0512afa04b1284e0195857f9fbcb71f4b9c1781ba3d5b |
| ../transfer-copy222-v1/prepare-next-upload-controls-v1.py | 0f6cfe03a0b11ccef0383afef7cba46c15ab6d2440e45f87ebebc97061cdb2aa |
| independent-upload-clean-v1.json | f51cd35fe0943600748f100109530a4e3e13440ec11d92464b00f55cceacbe58 |
| native-parent-reader-v1.py | 247c7ed60fe7ec2caa96428dbf1275fb1f64d96cd0faca63f7a45fcbf0220617 |
| retained-transfer-native-reader-v1.py | 94845804ce55d305511cc8bfe9fb96d1e8004d515721b8a0c782b5cd59b39645 |
| retained-copy-native-reader-v1.py | 21e85f78c9bbfcb6a0c88cf95a9f692f1a34c991e76054224115a816ba7bc69d |
| prepare-native-readers-v1.py | 70e00ad30fa060c240784fd58365e469635863d0c6ea042b54b2ff89f69b37bf |
| seal-upload-ci-v1.py | fd56f20e91c749bf0140730abcf26224244f91a7db230a96ca94cd373f47d602 |
| final-native-source-v1.json | 59ae6b2638abf6856610cddacdd75cfd4197e057b73b285636de80790fa51ea0 |
| document-baseline-v1.json | 51a9008e19116a935677bc29ddd63a8e093fd14c5dc4ef56a4a8a5e02d592112 |
| document-transitions-v1.json | 6a6e05524132127bc9326a4c56c7fec551bbdd487ce9cee99db7d09b314b2172 |
| update-upload-documents-v1.py | 1822d4109a40b78a97a0308a6a7abf1a6d8ae6061307c6da4413e28d0a9b214d |

Private `FileCatReleaseEvidence/ci-37793311586-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 748bf9b61987610b74066f3418117be4c1af1a297a296098699fa6eafa9d97b2 |
| independent-restore-ci-v1.json | 6a68b7528515ae21b2300d9205549f98a08e99f1c59dcc200cebea57f7fd91c3 |
| independent-upload-ci-v1.json | 043b4e281784fd02e817e3e1650b0e58dce1e9c504bcd3903590d65216353e6f |
| run-native-stdout | 9c094addb46d5c35a5b8e108d8a4cf6c921b497117680887b7961852404d5813 |
| jobs-native-stdout | 93e04aabac60fcbc9fc010834de6ddc4b3103f1e071722ab34077de72b667755 |
| artifacts-stdout | 24b162de3460f34b5078955829958d0635e7cd5808345c38a2c7f354eb5dc0a5 |
| complete-run-log-archive-stdout | 45c099e2df6ea8753b4882eac8fe8ea9f85bca7decdbba994cedf48d6d913eb4 |
