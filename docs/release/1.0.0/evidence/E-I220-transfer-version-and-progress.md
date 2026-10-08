# I220 — reviewed transfer versions, copied-move deletion and progress totals

Final test-only producer `ae19f8600e2e24b169c6352a093348ca3dc61e83`, runtime producer `3e6cfc506f0ad39b422cce4bb486601a02b2e838`: **119 additions (116 Core, three App)**. Runtime 3e6cfc5 passes **1946 canonical expanded tests/70 explicit skips**; test-only ae19f86 passes **1191 full App tests/25 skips** and **102 targeted checks**. Original CI **37770708565 attempt 1** passes all four required lanes with **468 addition passes/eight explicit Windows-sharing skips on Unix**. This is preliminary owned-file/component qualification; contract/candidate/physical-source and stable GO gates remain.

## Data protection and correction

The unchanged 1f3f992 baseline passes 40 of 88 controls and fails 48. Owned real files prove twelve unique moved-source versions and six unique replacement-destination versions lost; 26 unapproved copies/renames and six ignored missing reviewed children are also observed. Some behavior counts overlap within the 48 failed controls. Four actual Windows sharing controls distinguish attempted source deletion from actual native refusal: two source-lock cases retain bytes despite four delete attempts; two destination-lock cases remove a source with its original copy still present. No physical-user loss or atomic native race is inferred.

At `873c1660edc930fd1b64cdda8e1d664dd94b0535`, each pending reviewed file is rechecked for complete approved bytes/metadata, directory child membership is rechecked, and pending native-rename subtrees/conflict answers are revalidated. Written-copy bytes must match the approved snapshot before publication/replacement. A copied move reads complete current source and published-copy hashes again before delete intent, even if earlier read-back passed before publication. Missing, changed, linked/reparse or unreadable files keep the source. Replacement controls retain the existing destination. Native checks remain path based; the interval after a check is not atomic.

The 88 controls cover two sizes, copy/move, content restamping/addition/removal, native/read-back verification before/after publication, direct/staged/replacement writes, actual same-volume merges/conflicts and actual Windows sharing. Copied-move fixtures explicitly synthesize destination-volume classification while performing actual owned-file copies, hashes, publication and deletion. Same-volume controls perform real native renames. Wider providers, link contents and permission/topology races remain outside this finite matrix.

## Progress corrections and retained failures

The first producer's local full matrix is 1918 expanded passes/70 skips, full App 1188/25; its original CI 37760480222 has green assertions. Independent raw-observation checks nevertheless refuse two portable cases: verification reads 16 bytes with a reported total of eight. Their exact TRX observations remain retained; those counters are not qualified merely because CI is green, and their historical scheduling is not proved.

Four controls then fail the per-file work plan on 873c166: copied native moves report one file-size unit rather than three, read-back moves three rather than five. `119572ac0db505790df9233f37e99bb59dcd3404` adds the final source/copy check to the active file plan and reconciles unused verification work after refused reads. Its full local matrix passes 1922/70 and App 1188/25. Original CI 37762904568 fails one Windows pending-folder-caption timing assertion; all other lanes pass. Full artifacts/logs are retained. The key never changed; the caption arrived once and remained correct. A fixed 250 ms initial-delivery assumption is not evidence of repeated caption clearing.

Twenty-four controlled discovery cases reproduce sixteen accounting failures on 119572a. They cover 4-byte/1 MiB actual copies and copied moves, native/read-back verification, and early/late/unavailable background metadata. Actual file admission now independently supplies copying/item/read-back lower bounds; aggregate maxima preserve estimates without counting late discovery twice or retaining another per-file map. Unavailable/incomplete discovery cannot claim a complete estimate while active. These controls deliberately synthesize metadata timing/omission and do not claim the exact historical native interleaving. One intermediate run passes all byte counters but fails eight test assertions that incorrectly expect an unfinished-discovery flag after JobManager completes the job; the corrected oracle observes the active-copy boundary, retaining that raw run and unchanged production bytes.

The folder test now observes bounded first delivery and at least ten unchanged post-delivery samples, retaining every initial blank sample. Two added controls intentionally hold initial timer delivery beyond twelve row-update samples, then verify stable captions under continued real row events. Positive focus/size/lower-bound changes, exact owned-file hashes and all twelve preceding case names remain. No QuickView product change, native frame guarantee or new initial-latency acceptance is claimed.

Runtime producer 3e6cfc5 passes canonical Core 1246/61, maintained Remote 90/7, affected App 610/2 and full App 1190/25. Test-only ae19f86 changes only two App fixtures and passes 102 targeted checks plus full App 1191/25; runtime/eng/Core/Remote source bytes are identical. Every preceding name/outcome/skip remains at its actual source. The 1946/70 expanded matrix belongs to 3e6cfc5, and the latest full App qualification to ae19f86. Hosted Windows x64/ARM64 each pass all 119 additions; Ubuntu/macOS each pass 115 with four explicit Windows-sharing skips. Fourteen full inventories, twenty server digests/every archive member, four toolchain receipts, 92 locked graphs, nine mirror/five launcher controls and all earlier qualification subsets independently verify. Data-protection, file-plan, discovery and caption observations are checked separately from test outcome labels.

Original 3e6cfc5 CI 37767676636 passes three lanes and fails two Windows comparison observations: the final revision query entered before its banner was applied, and the directory window was absent after five seconds. Full artifacts/logs remain unchanged. The test-only follow-up waits for the actual revision result and the directory command task while asserting complete, non-refreshing error-free listings. A new owned-source control holds the final revision call: the old query-count predicate is satisfied while the banner is absent; releasing it and awaiting completion produces the expected unknown-state banner. The directory test still fails if the actual completed command provides no window. Historical native scheduling and the directory-window absence cause remain unproved; no comparison product change or native latency qualification is claimed.

The first final native reader refused the Unix sharing skips because it looked for their reason in StdOut; the original XML places that explicit reason in ErrorInfo/Message. The preserved refused reader/receipt and corrected reader keep all eight skips, verify their exact four-case matrix per Unix lane and leave every raw result unchanged.

## Limits and next work

Native copied moves add two complete file reads, with cancellation/pause checkpoints; reference throughput and wider changing/growing-file resource bounds remain unqualified. Native atomic handles/aliases, changes during content reads or between the final check and deletion, initial descendant snapshots, followed link contents, wider providers/permissions/admission, Shell/helper/DPI/formats, human/native interaction and exact-candidate qualification remain in I06/V01–V24. The [I218](E-I218-interrupted-copy-cleanup.md)/[I219](E-I219-interrupted-copy-selection.md) original cleanup qualifications remain at their own identities. No guest/Mac setup or physical source changed; physical HOLD and explicit human stable GO remain.

## Provenance

Private `FileCatReleaseEvidence/transfer-late220-v1`:

| File | SHA-256 |
|---|---|
| clean-v16/command.json | 65995a82c99628becff18feaa226cc6860631a93f34e7cbfa1e55ffaddcc5097 |
| clean-v16/source.zip | c92c661be2f56f9a7586b7fd8e3be85c1a41d7e89a1fbe779b9205a138a30b20 |
| clean-v16/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v16/app-full-stdout.txt | a8b1fcba520603e361b90aac7a3f9d50a1e6f9fa684671bb5219c43a875c7a52 |
| clean-v16/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v16/app-stdout.txt | 902ece53604895c5274fb61d5cbf58fb1b40204174bc04357de1eb4be6def1ed |
| clean-v16/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v16/core-stdout.txt | f89019d87af4dd0417f1857ece9f0a84644a9f66078bd406953cbf0d9080a71d |
| clean-v16/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v16/remote-stdout.txt | 7e293b2be3e424a107408b86cbb83e61b64abcab070e47d90502543f75f7d49f |
| clean-v16/results/app-full.trx | 2788f062168359426e971cb89d9e69512f69f1c73d1461138be46e5d2c564078 |
| clean-v16/results/app.trx | 8702d8a01316bc1a025c8d24e7610b388c239211c9a3dc6ecadf3c03dadca83b |
| clean-v16/results/core.trx | 30ef8c24984daa8a7bdeb8c60ad4da07ec59a8f23b82a6242d7f54afbe67e214 |
| clean-v16/results/remote.trx | ecb6f1969bc948b5d6fd471723846829c080c0eacaaafe3b85531096668ba7cb |
| clean-v11/command.json | 3f13910013d7147564b824356ad77eb59f1a2e51c1d238695553def02eaeab7d |
| clean-v11/source.zip | 42809fae308afe3dd1ac266308b78f2bec24a01814b992a866bb22bfbe467166 |
| clean-v11/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v11/app-full-stdout.txt | 56df9df389150a6c17d8ad27f1cf34ee51a361402f74c4636b28c732306dc70e |
| clean-v11/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v11/app-stdout.txt | c7e5717b1362c1246cba227f6e0394fe9c39fac499885dd684fb47c0c1a5049b |
| clean-v11/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v11/core-stdout.txt | 3504ffe1ddc1483a2eb5b62e005020a80d5cb44c5dfe928b850c0b0f6fda29f7 |
| clean-v11/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v11/remote-stdout.txt | 5c1dc6fb3b4f31a48395c110807f48c650652241b57034435b32c38785ba336d |
| clean-v11/results/app-full.trx | 7a14a4103e0de217b9f141a75d5dd1fe2528d4c59eea60b467105370b8f27dec |
| clean-v11/results/app.trx | 84cc1dff5f7bf788fb110340016ef9ac6ed96d54918a17810865de1087a44840 |
| clean-v11/results/core.trx | a8372111ca12dafe2b60ced12d392a163094ee0df5599e314acdea75257db1dd |
| clean-v11/results/remote.trx | acf81d610c0238572a28ab9e0e42ca862dfcef8620df7213f7dd2e761028852a |
| clean-v8/command.json | 2f4918476d52bc8a9ad8dd5a252f2fe0931ae8ce1cee2a3481a43561bfeb1d3f |
| clean-v8/source.zip | 6773b7f74dc71688061d44abc981094ade6ffc9ab504ed7561791d651e8830b2 |
| clean-v8/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/app-full-stdout.txt | 3fe1c28903f02d3ad618d3c9440cfb2f6174f454fd9765bc27ba8babc53d954c |
| clean-v8/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/app-stdout.txt | 0dcd577d093738bf4238df1009b9371b888ac0bd661f3419b8ffc0ab5cf88a58 |
| clean-v8/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/core-stdout.txt | c14e300dc9f78712a08854c5a48654a2dc925337a9a93d991f9d5bd3c66362e2 |
| clean-v8/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/remote-stdout.txt | d0d21565d8e7f04bcdf4e51cf523ac830646e29432ded22c2c495ed3e02d8559 |
| clean-v8/results/app-full.trx | 208a36610100261547635d862c935ab66047139d23ee6c977c94e57c4883dbef |
| clean-v8/results/app.trx | 48d60b42be315f18fa8f74bf10dd03ecd72d1af144d1494e904fecc9bf37f7fd |
| clean-v8/results/core.trx | 01f09189ad8e6a1fe14dd4f261940669fcea7f5bf8939a8366dccd4544173d50 |
| clean-v8/results/remote.trx | 3f243a9eb8d4639e2432a2e7c20168585a52b37f1b80ff2b12576047282a05d9 |
| baseline-v1/command.json | d36fac505ea05b0196a7a4e91fc8e245ea071bbd9eac01cf3169a44c57dae25c |
| baseline-v1/source.zip | c6c94562e37d86a7d41d0d1f7e5bf26d22adea6dd6059c2ef9ef4f947204ece2 |
| baseline-v1/core-stderr.txt | ee82dd81b32b9e51ae8bf40b0c155a0698a31dbf613e6bc825fbb87292f0af37 |
| baseline-v1/core-stdout.txt | 88fe337eccb9e9e65c5008b94a1ef6f67e789ce5f03c566ed737c31f5368ce77 |
| baseline-v1/results/core.trx | 1dfa397ccc82248c5b4c0b899ec156b27416c8b283f97ecb5f684dc1bfc41306 |
| baseline-v2/command.json | 6c0780314b2751375b627bba1f1ff0488b2e6f4c3f9015a38d1bfa70c00852fc |
| baseline-v2/source.zip | ab6d9a287786eedda00ffa34251e69981f22862cc015d6bc4de0f3e53b6b5b5e |
| baseline-v2/core-stderr.txt | 631511ee174506242f019576ebbe7cb734cebf4ccad67dee162771a44e2dcab3 |
| baseline-v2/core-stdout.txt | dfd8c4b2c8bd837a4babb08cd8bb9450539eb266c590245351518bbd6fdc8bbd |
| baseline-v2/results/core.trx | 12ecb390168c4bb2ec4df8700d352d96cbdc6e49b7d4ea27e86a76aacf09fb3f |
| working-v3/command.json | c95e71d63a9b5ecd56c172bbd6914267684435a7cbf96be2059ab745f77d36aa |
| working-v3/source.zip | 624d2d24f59e972c643b362a936f81da1aac2a7eac5ff25aa6c85e9d43343ff6 |
| working-v3/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v3/core-stdout.txt | da47303a3224965ffc62b11cffc9ffbae67e83e37c84ac773643c9c065e7ce7b |
| working-v3/results/core.trx | 2f31b9c7e1f48fc369a88a74e5c5e0f5c417eefdbb6c6450a925559a715ba495 |
| baseline-v4/command.json | 87b5cba6b86f7a8978bbd4a350995fb7e5849c93274a8ce7ad43c2bdf7aeae5c |
| baseline-v4/source.zip | 990a855eedbb3e4e83ccb63c71ae35d55c010844311aa11c4170d394a2623787 |
| baseline-v4/core-stderr.txt | f3be2bb383129e23536bf104d74df4f3f671c54da268b493f61d8def7ab1376e |
| baseline-v4/core-stdout.txt | 6afbe66cbfc14dc497088302b7a6eb0be3a2faa318e37b473caa6100245fdd89 |
| baseline-v4/results/core.trx | 753ba15d713e1eb6c5d5a28f0cb78e3a426cfef0789b7e919d9720b59d06a5f8 |
| working-v5/command.json | 3f19c6585c5be518b94561e7f9da4eaad757f1a78fbe6ab5ea9be1278d543044 |
| working-v5/source.zip | 15278dac79d80c3a0f97a6542c89cf4e0b2fe0bee3318ad20f1f18f8967e8e19 |
| working-v5/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v5/core-stdout.txt | 9e95bbec5b4111155bbafb6dd10ec999e52edd6212b4c90e0d8dee09821efce7 |
| working-v5/results/core.trx | be84f3355066a3eae391505171169726490c45a4550fbcd11053f168c993131b |
| working-v6/command.json | 2f4fa95fc367f50a76b20ceba7e1bc68251bcf421bb31236dc7f32370d226c45 |
| working-v6/source.zip | 6dfa6e4dac81b9965966de26f8b79446fcf61514e72fb2cdd97a821d3eb8ee17 |
| working-v6/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/app-full-stdout.txt | bc9a93ce1ce21f4b9f29c98c1c76c1c2631690ca9962b6f6f70b63e35e56e17b |
| working-v6/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/app-stdout.txt | e36bf7ec4fcdb888b41b9c422703ec0fd84ecca10c25f19d9d6b2dd06dd6b420 |
| working-v6/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/core-stdout.txt | a9a90c708525068dde58402876efbd9ef85f25ffcc4dc4cac360958f74b7df5f |
| working-v6/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/remote-stdout.txt | 4b84b71b9d9bb1c176728f9c45f3ff8d29cce20535ce344cda5626c1fa827494 |
| working-v6/results/app-full.trx | 365f9e89f43471825148513dce1a85548bb6e6a7fece8a45ffcf1e6301734eee |
| working-v6/results/app.trx | 183e2bda8711844317e927e1955273db49a3dbedd9a30812e53b09443cb0ada5 |
| working-v6/results/core.trx | 6c881e6234b541267463d09f7578a20797f10f5a07a3dec2da9b91edfedb6694 |
| working-v6/results/remote.trx | 2f1a4835bb80e607cf92fa1ff5374313665ab36db9b7ed850cd332ffb9d3dc89 |
| working-v7/command.json | cd36ef21a2928fee5df42a065c54dd65fd6c5bcb6cc68a0d19f63eee74be5c26 |
| working-v7/source.zip | 6e4a08e4f25251faac5983a869cdd9b885d155ee121b0ee7ce21d8ed33b1393e |
| working-v7/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v7/core-stdout.txt | 34bd76ba5ed1bb01e2da00fbebe3968a43436d0071f63f0903cacc08c26f5d0d |
| working-v7/results/core.trx | e55e620dec0f58e40a703ed57a96c70755097eb5987db960f1f22796d13616cf |
| prepare-run-v1.py | 79410ad5ca881d35be8fb448a5c0bc86d136d6aabf441a8a3c4a72e5112c11da |
| run-transfer-v1.py | 50e11e4180d15bcdb7b17622a13d2b0da36e1d6d9fce39f90044ef1649d3cb78 |
| prepare-broad-v2.py | e019c202380f04cb3c78dc90b82d59c8101150e99f51599f4c9c277d7f58dcd1 |
| run-transfer-broad-v2.py | 3c2a6eda3a779bd89e898e6854b6c0884513e3d8d4bd9f0014c30856c247709c |
| discovery-v1.json | 01a3f9ff1e878068d636e094012204c6d764a6279aa47cd0c1e74f3441ab57d2 |
| seal-transfer-working-v1.py | a571c42e3a850f93d9286a392bfe50beef54c855d3e2cd867f3eada7566c315c |
| independent-transfer-working-v1.json | 24e785754e7f9c8ec4f4a4d440cf0ad8a385e182aa62449148851b1ca75f9617 |
| prepare-native-tools-v1.py | 6efa54dfee9956316d7299d214f8c2fb1e89b68341f84c1d9248a61175f8f8ec |
| query-transfer-ci-v1.py | 11577390856058e40f6f0182623b967a4d0ea8a442f0b38950bc123c58faa727 |
| collect-transfer-ci-v1.py | 4ca683e9cb75dffe0cb39819c1f03661dcc642e3e6ed82d55bbd355e8894bc54 |
| seal-transfer-clean-v1.py | 6f0bb47329db8730af9dcba8236dda929cd8165f83cf150c6a9790ff9232a4d3 |
| baseline-v9/command.json | ee7de07ac3a5baaae5e50280577899a3976c3bbd2a9fedd353d8e9d3a31ffdfb |
| baseline-v9/source.zip | 2a7fe49163ec84aa14782864732fdf5fdf46dae09116fc6a090883f3edd628a6 |
| baseline-v9/core-stderr.txt | 43b42fbf0f6f3800d266c3b4740d1be3361cbfba5f9c76e6dcc176471e6e05ee |
| baseline-v9/core-stdout.txt | 4f20bd60df67e4a4f30baa2ab493c851dbbc42b5ffca8a60bbdd60dd99efdbc2 |
| baseline-v9/results/core.trx | 3de285813ff44017f98b5c60efce9b6b94a1af8ddae87b5278e8b9a2e41767cb |
| working-v10/command.json | 154172963642d3fe7eb643c4dd93a9dc43d83fb94199b56c196cadca73b0f40a |
| working-v10/source.zip | fdd7319b01cb6f18825e9cbdca38d8b3623240b83eea478747c25c49d53d6308 |
| working-v10/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v10/core-stdout.txt | e5134342e2b7ae82cfd37cba261b35d6cbd14128ad65b3831113d4487bd6d0f7 |
| working-v10/results/core.trx | 6d792d114c008a46ecc2448ee517fde802371dfc72172d4619761571ddb3b763 |
| prepare-progress-v1.py | 359cf948d988c69ddc5d2e1db6483beea8250119c216080a065f2b011960e537 |
| run-progress-v1.py | aba11ca907a33def634d2049916caa957a3719cc0e193526650f3a17d031e2a1 |
| reread-transfer-working-v2.py | b093e9b46a0bb5c4fd0b44be7f7c239e019789b0e10620e3543add91c49f4c12 |
| reread-transfer-clean-v2.py | e86887056f265aa88a78460b15b7dac8fac3926bc1dca082124117e7dd27308e |
| seal-first-clean-v2.py | 496d36ed106b0a2576a76dc6d8b89d0df3574963a1384cd158c5acd4fe725a3d |
| seal-progress-working-v1.py | b6d90a4f388fad9e12b51ce4add6f496a92ce58752c7f7eaa560d60a2e11a72c |
| independent-transfer-clean-v1.json | cf288c2c9ed4b37abefe4ccefb779f2e7bddc434f9fb873385ad3f7c041cee68 |
| independent-progress-working-v1.json | 924fc73ffa0b9f2534a575154bb1af7d595dc323f31cba221f0488869c8a83eb |
| prepare-final-v1.py | 4810602535664bb53a97299e9b0f613deca30ee9cd1c234d0b2d6049beb7cc57 |
| run-final-broad-v3.py | 5502db064a069cfb9945f9e9713fa8691fa0975745d9974455a6c469e6fbab70 |
| document-baseline-v1.json | a16fde0616b8a55c9c8f98f7cf8715ea3c806ea6ee9db69d898e23032d1868fd |
| document-baseline-v2.json | ae9b158ce59c489f0cb3a6ac24c73dbfdf695abc1901d74883a03e22a4a3f1ae |
| source-review-v1.json | d5b45b7267fc16c9bfcad97827b69f78b64f9138819e7ea239f1ed8f5be75464 |
| prepare-documents-v1.py | 64103078784dac6f602470633671b03fbc8509f75d279dda594d4c9313973be8 |
| seal-final-transfer-v2.py | 8459fdbe579f66052cef2f27ee510c91a524903de8d790b0efb549bc39a1eedd |
| baseline-v12/command.json | d59fdc7a1e53e3d7de9cb94f83df0c197d6e1d7c24b1fbd3062f28f2de72ccb8 |
| baseline-v12/source.zip | 8a9e1d04d7c58d933a1c0cfcbb553f75ca4ac3d08b9436c50092df7ebd467715 |
| baseline-v12/core-stderr.txt | cece0c8af8b15224d138b7e531c5e34edf7b3110573a2bcae34783e4c4ebfd1f |
| baseline-v12/core-stdout.txt | 4dd856da5317a74f6de532581657ed1464dbdd5e62475dda9e952db47560bfbb |
| baseline-v12/results/core.trx | 0a8b763870b69138935883cdff6d4e61a600382188a1cfe618fe1649e81d1608 |
| working-v13/command.json | 5b6d46bf6036d3e30329b42cd2e9dbe758db6bc8150f1b90adcdadf0db7f86de |
| working-v13/source.zip | 27030fbc9c8542b8a90595ca9985ac4c5f057d2a24fe00e9d30080b683b50bfa |
| working-v13/core-stderr.txt | f30ae111bad8229528b5c953bca0b4dc124108d70ba56b05fb103e76284436da |
| working-v13/core-stdout.txt | 31d96c69a61d0ef972683842e2a658dbf67d28893703050602ad6bfeafbae7f6 |
| working-v13/results/core.trx | 1db38ab18886453ea75987b26457d9ebb9f4f00843f6755c3ea99ac7bc6892f9 |
| working-v14/command.json | eb1ce17dad9a8c22ce65b7444bec284b4125dbb4dc21aae328bc9196165afa9d |
| working-v14/source.zip | 4b4e4cff7ab59c879133419b129b8a62e7865432f63034f9a04438fb88a6c9bb |
| working-v14/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v14/core-stdout.txt | e16f70a6981b8ca2622c41ae24c0b2037fa2cdb2e0dcbdb924239151f9749c48 |
| working-v14/results/core.trx | c1e3fe79ef807449c4784694f4ab5741240790def3c9954f3489e2aadec4e67c |
| working-v15/command.json | 221df16b873ce13af441ac49accd0ec1d336d617bb2af65117456d70a0109da6 |
| working-v15/source.zip | edb99a3f033edc4e345b3f8e41862826904495a4fc05ae5c449e7f1fc6f5f94d |
| working-v15/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v15/app-full-stdout.txt | 55c4662fdf9ca32fb7f221e88e7f0a385b7c9ef0e541b8327b652300d7bb9a45 |
| working-v15/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v15/app-stdout.txt | f9ed5bb54bdc924fb94816038d149d4b5805d450be127fe09e3e6465f6b11b62 |
| working-v15/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v15/core-stdout.txt | fc55000e6802af13b89b6279be9784a9b003f07e8ca07661e478eea50d7aa41f |
| working-v15/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v15/remote-stdout.txt | bc6118620ac2652c38fd4777ac77abd37e47c711cbfd531d4e80ff4adce384d8 |
| working-v15/results/app-full.trx | 247ece1456944deada99d83d074f20e65819c672e5612fbca10132061d6561ad |
| working-v15/results/app.trx | 1da636951c331b76cf4df5844c04c5b7db174cd535ae491d29826f52495e83c1 |
| working-v15/results/core.trx | b43daa23e528322a368696d04146e29b043124859da5dc479ad95a880995081f |
| working-v15/results/remote.trx | fa7b9b09445298f0feb44f519ed75d670a53633010ec363e63a0fd98230e9a93 |
| run-discovery-v1.py | ccac3df9646ae8178071e4db7196b49621fbbd03bc71c9337cf8594913e7f8e2 |
| run-final-broad-v4.py | f99f22f8d038b445b92aa1906f8f0a1b856044e2910be729518f7ea2675140ca |
| seal-discovery-working-v1.py | bd28dcb9c7ce8f6c2858bff514f7c1e84dcf3bcbc24cc1b4f251fd89604f464f |
| reread-progress-working-v2.py | 7ef1fe4103f8d91b6c5dee9dbd18726d09483bf23d1588db337545380f6a1528 |
| native-first-producer-progress-refusal-v1.json | ffe72747329e368d9179e5d0d4dbc06a682d528722f799ceb9f67df573ac5ac6 |
| collect-failed-transfer-ci-v2.py | 298fabb5413496b68f22a4b98595d1394eff10690b58eb73300d389b13050c03 |
| seal-first-progress-clean-v3.py | 174404728c9935c2706e0bfa4a92c5e3c9ba7545604333f44de35fc5001a9a43 |
| independent-final-transfer-clean-v3.json | 6d79ede6800fd242c951060163bb14e6ad0e0b183c2854b6111f903f96153f44 |
| independent-discovery-working-v1.json | 27fc33a7d46b431277d03d38be38aea05d38c065d1c226326a798363971bb3ab |
| prepare-final-source-v5.py | 1a3b5cff938de6584aa2831bd5b1870f39afdf8ebcd4ec7d82638d2c5066be12 |
| reread-discovery-working-v2.py | ae7605e784573899b48a8e46a28cf69b1f6491af05c0aa3236a578984cd6fa03 |
| document-baseline-v3.json | 8d5fe96cca2cf18243a2752b8d797ed7fa816d05fbac9bc4a427fbd963ec81a9 |
| final-source-v1.json | f56dbf3ee5cd36c48ec4544277b2ede6a0e0c4245e4981990adbd061da516430 |
| seal-final-transfer-clean-v4.py | 6149076a5ed87d8b9e2d0c53e9eb3f67e057787f5251b4b8a8afedcd57b807e4 |
| working-v17/command.json | ba7484b7ab743f710a3c865251a2a58fddf199df85bd19a6d724b479f7969860 |
| working-v17/source.zip | 3f07957ea78aef119d1dd53163869fa56a8a09cb2b0cbdc0e10f5ca04dae78b6 |
| working-v17/app-checkpoints-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v17/app-checkpoints-stdout.txt | 05ef056ba3bb9e810fba96d835db06192add0e128717627a91019c283953bda3 |
| working-v17/results/app-checkpoints.trx | d4c88467a83fe2472e5e7868de9eb0c6eb3e16823a8d373f1d5b9b06af4fd2e1 |
| clean-v18/command.json | 6bb63e619086ccc188a4a564abe99c32c6305e260e5be7b5cb854e8896895107 |
| clean-v18/source.zip | ba480e40dbda8a8ed7f70256eff586cb8b9a708428c22a7310a13f0cc5af507f |
| clean-v18/app-checkpoints-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v18/app-checkpoints-stdout.txt | ecdde35d0a17bc46970bd0d1c74f11aab0c700d30cd2e32ce07d7e2b767ae62b |
| clean-v18/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v18/app-full-stdout.txt | f537bae1ac49bd3a29f34507c88941293e2f3cd1bf0ce24f1d7a08808acf0deb |
| clean-v18/results/app-checkpoints.trx | 435dcad7298dd9b86c086be4b5393d5b6d9d75d591a942ddfd8daa43dd87f4aa |
| clean-v18/results/app-full.trx | 906cebafa10fb0d5c9d92ba4fb724e129992cd2eee2cfbe734ace949f8f6147c |
| run-checkpoints-v1.py | 71820ec7af6b8789e5e99fe6b2c7f1c7c737a075abed839233cb70b76933ff07 |
| run-checkpoints-full-v2.py | c03e488dadbab270f15565574cf762684e48e3462dd53e4a9393c790a27a6e78 |
| independent-completion-working-v1.json | 2038fcbc88f3772d51e7e758242582a55548809f3709060487a78007e2aeb854 |
| seal-checkpoints-clean-v5.py | 3d158346395abd61b6c3e4d773ee93ff67a9c3cd606b9d21cb664c28814a0a83 |
| independent-final-transfer-clean-v4.json | e6dc2b935b47d8b369bc1971ca3eff57008f461ce4fc8fd22bc0f6eacf66e3f9 |
| independent-old-native-diagnostics-v1.json | 8098813acbafcafea3637148934c9e9d89afe9ea18798989c9b774870328a501 |
| independent-checkpoints-clean-v5.json | 5f8f8863f5282416daafd2f6173fb4a868b37b89ae78a2524766ad2ce637b906 |
| native-parent-reader-v3.py | 546439344c12dcd9c67e9355a5f204c565ef9925ac788c37f582390ef26dc91f |
| seal-final-transfer-ci-v3.py | a87c3cc62144cb86bb89ca4e5558f7fef036da10a902024f770898adaf1ee762 |
| native-skip-reader-refusal-v1.json | 302c0c625de9072ec2765977dc7064cb3aee28b113cb8579596428beea663211 |
| seal-final-transfer-ci-v4.py | d0757d97b085214b8d7af507a5ffbfa10baf6f980a12e7a411db90cd55807a04 |
| final-native-source-v2.json | 945aadcecfecd9ca05ac6d65f336f186251e2d13d24f3cc5ddb82eb5d6d96a4c |
| update-documents-v3.py | 03d56fabee826e600748e705ff6db8691722b79be969bf2ae5d88def4526bfdc |
| document-transitions-v3.json | 9dbb8b11db5c459ec60a86e261c983a93602d11e1035cc2d515468c9948f9c39 |

Private `FileCatReleaseEvidence/ci-37760480222-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 79d494f1424b6f08bfe6b1bc63a1860192b54a00db8dddc80318963cc72d1617 |
| independent-restore-ci-v1.json | 68d2f335265e7a54ca390190de5c313b561514ea7ddd93a0487aac1fee58318e |
| run-native-stdout | f3c312432b36c9569fe4715f7b57aa840f240c6782b1ba4dd478677c709dd082 |
| jobs-native-stdout | 3a712d705f86e4169c1cbff6e6be9945c3f12cd3693b0eed1b8d6b30475a230b |
| artifacts-stdout | 99ed3148cc09d681a71abcb7f5ae41e54821658406c23c94c120a5654fd3f348 |
| complete-run-log-archive-stdout | 7f8551fea17f3446da1a0e08f8845c611e7bd186f991ddc194a826b389b174f9 |

Private `FileCatReleaseEvidence/ci-37762904568-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-failed-assets-ci-v1.json | 1db5d72e6e4af1327e9063aa99e09997b12f7b8eb47bdff8a186f15516403789 |
| run-native-stdout | 7109f03e64605b60dc3a9e8a7870c04d7212df46a660d064833eddd8d672265a |
| jobs-native-stdout | d05f2120d3bacb1008cce5cda4b98f9d8898cfd1ee726ea4c873192ec60ee64f |
| artifacts-stdout | bb6264d7ae03c948272bcf41b90bcea74fd062038c8361956b5628f7cfec1d1a |
| complete-run-log-archive-stdout | d303601bdcbc2e2b55ef443278c769bb6fdc5f86266b646da40c5b3a537cdc52 |

Private `FileCatReleaseEvidence/ci-37767676636-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-failed-assets-ci-v1.json | bd4defb01a9a3e4083381cf6b1ee8cdf98a73c3ed4934c2663c7de0131cb9cf6 |
| run-native-stdout | 350880c32dd26e2bb2dd9feb4d0337bf94c8c4fbd9a3081298fd3becc4181154 |
| jobs-native-stdout | 2d6f6dc90ec0c199bcec37a65a89c92e573d0eafce81bf9b4275378eaa3807ac |
| artifacts-stdout | f8209a188121bd9adb928ea898a61e0b49e2821357b4a6372c1799531b8f1aaf |
| complete-run-log-archive-stdout | 73b200f1d88562ff55197b8f4a1172a3fadb506898f9d59a94a6041d363963a2 |

Private `FileCatReleaseEvidence/ci-37770708565-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | add2a3f771487f309f9131414dc6a1f63f79adecc43c053f017bb7053c18eb0c |
| independent-restore-ci-v1.json | 4fb876b4833fe5a60f35c3a5845b3ca3ef8296d36ca0c6e70647a6aac0068bf3 |
| independent-final-transfer-ci-audit-v4.json | fd4b9b6494b638e0eab20a8bed5d9c5315733fde0c3fcba1b9cb344b63631a60 |
| run-native-stdout | c8f69bc0862ebadba536cc7637bd51cab4fd776b03a5d68352c98fab6eebc9c5 |
| jobs-native-stdout | ee3351ffe3613c34764e97a8e42174f7a8a1c450930919871a80abc879801643 |
| artifacts-stdout | 577810a8a3c85d498299bd4ccc9a9176184a986cecbd1abd74ac23544cf5edaf |
| complete-run-log-archive-stdout | d9fff8ac1299e3530cb9ea01ee209c1f820bea7d2eb8f955b7aa451a2dca86ad |
