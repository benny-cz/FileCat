# E-I173 — completed flat-view cancellation-source lifetime

2026-10-07. Discovered against canonical product source 8a57103154a75174e21e83371e0eca41c57d282a, unchanged at discovery documentation HEAD 02adcc23a6c43d4947e160ffde3d793fe9425177. Medium retained-resource defect; must fix under I06/V12/V13. Remediated preliminarily by c6e13f5; cd6ba2e567807a527626d268bc2d746bc0eab80e trims only the new test's trailing whitespace and is the clean build/CI producer. Wider lifetime and candidate qualification remain open.

## Failure and correction

Each actual Flat View command registers its producer's cancellation source in MainViewModel's `_flatViews` dictionary. Completed producers never remove or dispose it, so the live workspace retains every completed source, including when its result tab closes.

The correction registers ownership before starting the worker, then posts removal and disposal to the UI thread when the producer exits, through `finally`. Retained result sets and existing consumer completion behavior remain available. Active, faulted and cancellation/provider variants are broader I06 work; these controls qualify successful completion only.

## Controlled validation and provenance

Two durable headless controls invoke the actual public Flat View command over two owned files, with the result consumer kept open or closed. Both searches complete with two results and unchanged file bytes before the reachability assertion. With the workspace and result provider retained, both original-code cases fail because their actual cancellation sources remain reachable. The corrected working build and fresh committed build each pass all 24 affected App cases without skips: these two controls plus the previous 22 consumer, comparison, Find/archive and working-set cases.

The initial baseline preflight uses nonexistent ResultSet.Items and fails compilation; no test DLL or TRX is produced. Its exact source, logs, receipt and single source-mapping output remain. The first seal incorrectly expects no preflight payload; corrected independent verification checks that mapping and preserves the failed seal source. Neither preflight is counted as a product test failure.

The independent seal verifies 30 retained files, 423 actual test payload files, all 1,068 original raw source blobs except the documented working module/test overlays, and all 1,070 committed Git blobs/modes with their source archive. Working and committed product module bytes are identical. The baseline/working test differs from committed source only by CRLF/trailing-whitespace normalization. Clean actual FileCat.dll SHA-256 647d17c06ec3d971ac1ec5794d822f368a0a9f0812ce283aa5ea4e9763f8c8a5.

Owned fixture/state folders are cleaned by the tests. No persistent machine setting, Mac/VM setup, physical source, native desktop input, contract, candidate or publication changes occur. This is headless component ownership evidence; it does not establish native UX, exclusive-machine performance or complete worker/provider lifetimes.

Original push CI [37557951023](https://github.com/benny-cz/FileCat/actions/runs/37557951023), attempt 1 at cd6ba2e, is sealed green on policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26. All nineteen server artifact digests/all members, fourteen complete TRX inventories, four compiler/tool receipts and 92 locked graphs verify. Each full 482-case App inventory equals the previous 480 names plus exactly these two additions. All eight new executions pass with distinct IDs and no skips. Core inventories retain 884 Windows/879 Unix cases, and I163–I172 subsets reconcile.

Full App outcomes: app-test-results-macos-26: 403 Passed/79 NotExecuted; app-test-results-ubuntu-24.04: 401 Passed/81 NotExecuted; test-results-windows: 465 Passed/17 NotExecuted; test-results-windows-arm64: 465 Passed/17 NotExecuted. Explicit overall skips remain unqualified. ARM64 package version startup, headless drawing and installer compilation pass. Tagged/manual package and draft jobs skip; no selected shipping artifact, installed native desktop, physical ARM64, participant or candidate qualification is implied.

Private `FileCatReleaseEvidence/flat-view-source-lifetime-20261007-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v2/command.json | 8d2d4da554c81550e788736d212e04634825777414101caecd13b8341d824d1a |
| baseline-v2/results/baseline.trx | 7c246d1cb75c3306e8af1331868338f4a21b3349b69d37c3b2407fdea81a7c9d |
| working-fixed-v3/command.json | 3f25dd8a61de26709c7f2bcb3d03ed4ff54f90ff7296add6979cf107952e83a4 |
| working-fixed-v3/results/fixed.trx | 404c7ecb54ec5d789ee0417f5ff635136483cef3a42a2f21f1f03a671b004090 |
| independent-working-v4.json | cc7f4749448fb2fc6afff72eb75a4c3f4488e4235e08bb4bd4bae9da2bd80ac2 |
| clean-committed-v5/command.json | 28c0d04e778fb12ea60de214f40be924022b71a41f968d5fea3afcc93d8d528e |
| clean-committed-v5/results/clean.trx | e21387af71928f200640c31ccfe36472a4642105663a370a0d7f161274f9c110 |
| independent-flat-lifetime-v7.json | 7ad91da43c5006cce96b2caa0035efea54a13f6e8abe529505b22eabf8f8043c |


Private `FileCatReleaseEvidence/ci-37557951023-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 8ea5274aa3f60178d4994d0461068acfb9eb58bd8070aa0735245c93e32d31de |
| independent-fixture-ci-v1.json | f2221c4924c8113e7d9b2e9f93fdb3a5fcaa88a2a81208a41db0d7cbbd08097a |
| independent-producer-policy-ci-v1.json | 9b3b68412353cdd62a8eff7d61361b872aefc87922c02497818ab359363193bf |
| independent-draft-guard-ci-v1.json | 41e6b2d38dcae7122c7acc81cf97af406934dd59c0f4db00d282a1c9e94c6377 |
| independent-separation-ci-v1.json | cd46d00c32202da8ecde07d2f68ccbf1677d58c93332973021fb1f1716f9d600 |
| independent-restore-ci-v1.json | 5646dbcd1af28e9ebdf34387c9c7be9a2d33edd4442c9fa5d870b2de73b3d923 |
| independent-i163-ci-cases-v1.json | 22d2d7e0c520de5d7188c8dccbdd3161d5af1461a95974987663b1d01ccc14c7 |
| independent-i164-ci-cases-v1.json | 7c2fdd8d30df68f340993f65b24a51941a7212d8fe9ce67003da5a39a30a2261 |
| independent-i165-ci-cases-v1.json | cccdb189f5c0a3bfc9677f7f7c5aee2eaea31f5a2e6539c736d815496eecdbf5 |
| independent-i166-ci-cases-v1.json | cdc0a40b0bed297dc6eb0b4aeb03396a6150f602729e0dc86768215ca0ad38c5 |
| independent-i167-ci-cases-v1.json | 3575481183082a936d4f7fe53b5a2e5f27ef36bd596266a798e16ba379c1ec9d |
| independent-i168-ci-cases-v1.json | 616943fa87a51a950c1867f33767ee96bdb13e1b0344175fda1059befd07aa54 |
| independent-i169-ci-cases-v1.json | 5c22715f4ce8b12bd5a85d089e437f0023f791fc189d493469cd3e79c005c125 |
| independent-i170-ci-cases-v1.json | 46e8aa75cbf27c817afc938b6c05cb3f4810e72a3823090aaf7413012e4450e9 |
| independent-i171-ci-cases-v1.json | a604dfe1dada42e42f57674aec49c05e0212752eb2cbd7a06b215a534efdad37 |
| independent-i172-ci-cases-v1.json | 7bf0cf321210348534eeed2af42a863de1790c0e0d7c23fbfdbc6ab4cffaf96e |
| independent-i173-ci-cases-v1.json | b915e3589da0b4d68a7f49dc9bd280d3281094af0966d35e0d8328a3cc20c240 |
