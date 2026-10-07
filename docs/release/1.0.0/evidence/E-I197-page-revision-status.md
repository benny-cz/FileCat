# E-I197 — cached HTML/Markdown page revision status

Recorded 2026-10-07. **Preliminary remediation; I06 and native desktop/human/reference/candidate qualification remain unresolved.**
Original product 7a86b4026dd5abfe83ab8ff43431326edff93cf0; discovery 64ad01aaae9019f38f81b01edaa189776adc2992; corrected producer 94d5af7530a0c871b0fc37b69c887adfdc4d92d3.

Refreshing the file reader leaves an already loaded HTML/Markdown browser document unchanged. Viewer status nevertheless claimed current content or omitted the change warning after page/status notifications. Returning to a cached hidden page also omitted it. Four original changed visible/hidden cases fail; eight unchanged or first-opened-after-change controls pass. Discovery and all twelve raw observations were recorded before the product change.

The viewer now records the source generation when first feeding the page. A cached document from an earlier generation keeps “The file changed; reopen the viewer to refresh this page” through status notifications and hide/show. First opening the page after a refresh captures the current generation. This finite correction does not add automatic browser reload, qualify changes in adjacent assets or prove every concurrent loading boundary.

Twelve App controls use actual owned FileContentSource/PagedReader/HtmlPage/Markdown/ViewerWindow/PageView with an explicitly injected snapshot consumer. HTML and Markdown each cover visible, hidden and late-first-open cases with changed/unchanged inputs; source bytes, revisions, rendered-document hashes and all three later statuses are independently checked. This consumer is headless component evidence, not a native browser or desktop-input test. Working and exact clean producer pass 137 affected App cases without skips and preserve all 125 preceding names/outcomes.

Separate actual Windows WebView2 controls use the existing owned hidden Win32 STA harness. Runtime 154.0.4258.62 renders each original HTML/Markdown document, then retains exactly the same 640×480 pixels and title after reader refresh: zero changed pixels and one successful root request. Explicit navigation reads the replacement: two root requests, changed HTML title, and 6353 HTML /6910 Markdown changed pixels. The unchanged original six raw PNGs, native TRX/base64 frames and request/revision/input observations remain. Baseline two new native controls pass; working and clean pass all four PageView controls without skips. Native component evidence does not qualify full desktop/input/human/reference behavior.

Independent v7 verifies original 1119 and clean 1121 canonical Git blobs/modes/archive members, exactly three changed paths, all baseline/working overlays, 573 actual executable payload references, 48 retained private files, 36 App observations and six native observations. Its process query finds no owned stage executable. The generated clean-v5 runner failed with a line-23 syntax error before creating any stage/build/test; its source and tool-reported metadata are retained. Fresh v6 runs the clean producer successfully, without replacing any original result.

Original [producer CI 37643607270](https://github.com/benny-cz/FileCat/actions/runs/37643607270), attempt 1 at 94d5af7, is sealed green on all four required lanes. Each complete 632-case App inventory preserves all preceding 620 names/outcomes/skips plus twelve; all 48 new executions pass without skips. Each Windows Platform inventory preserves all preceding 208 names/outcomes/skips plus two: all four new native controls pass without skips. Their pixels, exact inputs, titles, requests and revisions are independently checked. Core/Remote inventories remain unchanged. Nineteen official artifact digests/every member, fourteen raw inventories, four compiler receipts, 92 actual locked graphs, seven CI seals and all 48 page-status observations are independently verified. Producer reference policy passes; package/draft jobs are skipped. No original CI run was rerun or substituted.

| Original CI lane | App Passed / NotExecuted | Windows Platform Passed / NotExecuted |
|---|---|---|
| Windows x64 | 615 /17 | 177 /33 |
| Windows ARM64 | 615 /17 | 176 /34 |
| Ubuntu 24.04 | 546 /86 | Not applicable |
| macOS 26 | 548 /84 | Not applicable |

No physical source was used and no persistent machine setup changed. Native component/hosted evidence leaves full desktop/input/human/reference and exact-candidate qualification unresolved. The physical-source/USB hold, contract/freeze and explicit human GO/publication gates remain.

Two documentation-checker failures are retained: the first compared the CRLF status row to canonical Git LF bytes; a dependent tracker started before that audit proof existed. Fresh readers normalize only that Git-row comparison and wait for the audit. Neither failure changed or reran original source/build/test/CI evidence.

The initial normalization-reader preparation also failed before writing an executable source, due to an escaped line-ending literal. The generated resolver was not executed; its hash and tool-reported preparation/missing-source errors are retained. Fresh v102 compares complete row text without a line-ending literal.

Private `FileCatReleaseEvidence/pg197-v1`:

| Retained path | SHA-256 |
|---|---|
| I197-discovery-v3.json | 6dc2c0845f144ac10d97ada53ec2e402dbb6a4d2e849e83cf6c2883f0190c493 |
| baseline-v2/command.json | eb026d97063a7c8fba6646d02656fd56b6363512d471d2ef47558c31b670067f |
| baseline-v2/results/app.trx | 52c7e0ec5212f5445fbc51cf244ebb3178aeef9dcacd19ecbcf53ea8cc5cfc0c |
| baseline-v2/results/native.trx | 4863cf2805fa349483c9930e13eba15de46f7b3feda9174e7ff409ffeef4b2d9 |
| working-v4/command.json | e1f96fa77dd7c064ad7310687e1ad0574d6f2162630cf55d2f699066bf9f4274 |
| working-v4/results/app.trx | d4504997f14ff64f2f746fb50779a9c43517303c2d057de31aac9fb965ff60ef |
| working-v4/results/native.trx | b8f9f7334ee108a1ed193d36101389663b5e3df57de7de8f0d122caa0a609d90 |
| clean-v6/command.json | 412ee5b4f39f7c1db4520fde077e23e745415da1ef00ae642fe232b3109a6b3f |
| clean-v6/results/app.trx | 4527c527b3a87006bf5579041e14a8c3e43ec2848c8728b3caa0f7cb360e1a8c |
| clean-v6/results/native.trx | 783d216a455523da359252b8fb029d958ea9a5437f320c616c4506b6b8764fd3 |
| independent-page-observation-reader-v7.py | 676108a5db76d090c0819ffef9b1426023452d1cbb9f897d9cc1c16af1766dec |
| seal-page-revision-v7.py | 23d6e770eb6975339aad2ae17e6c59d4df027e5a3b92f10dbe9257f91089cc44 |
| independent-page-revision-v7.json | 555298c192631486307f974053156af18045630b074f3679435d29a972a3c578 |
| owned-process-absence-v7.json | ce91418be60a75ef316787c3d5495a166d9e8d93a7aafb89ccb735b9341b52e0 |
| clean-runner-generation-guard-v6.json | 6c21235e5755d839d3bb585d75ff3f786e82474903e920aa78b68758e6afab2d |
| run-clean-v5.py | ddeaf756cfe6d1689732414127df6130e3c5288bebb9ef0dd9b5f6aea94f1711 |
| run-working-v4.py | 5b03fbcac46c2cdf264e7b442cc89f0f23bd7e96273345fd9c0e7d361adf3f61 |
| run-clean-v6.py | dcde9313f370fff9fc30f6b494c9626c4994d8f898e1b2d7176f267321f84f0b |
| record-discovery-v3.py | ddb8776a22d06bd229f1ba4efe1b3146f9ae34f5c9eab560398be673b5129203 |
| run-baseline-v2.py | 2328f276fc80e19d40198e49f7ee0c7520b2c71d6e23574cc8b456a29fcf2738 |
| original-native-frames-v3/html-before.png | 91a3c83d6f65e413431a9f538523e70e2a39f8e86ce7b39f02d7755c07940d76 |
| original-native-frames-v3/html-cached.png | 91a3c83d6f65e413431a9f538523e70e2a39f8e86ce7b39f02d7755c07940d76 |
| original-native-frames-v3/html-refreshed.png | 04f68c0e1959d871cfc5af32ad005e1cc927f9490e5c70532c7010ab0c131465 |
| original-native-frames-v3/markdown-before.png | 34bf47fe85d05831b2ef1b29641bf7b9b90c890f5a747acc4065e177c50c6bc1 |
| original-native-frames-v3/markdown-cached.png | 34bf47fe85d05831b2ef1b29641bf7b9b90c890f5a747acc4065e177c50c6bc1 |
| original-native-frames-v3/markdown-refreshed.png | c5bc1ed44d39636dcb8f8948da3c0478e35e8e4cdbcb017306c2752b3709adab |
| tracking-audit-order-guard-v10.json | 64de4aa85871bb0060a2e15ef33d6c3ccf56ff093573a62c4c39af5fdd4b384a |
| global-row-line-ending-guard-v11.json | 1683d0811a735d050835e566b120341426bebbe566a8a269c4df1d8df719d6d8 |
| global-reader-preparation-guard-v12.json | 28035821724f022485247ca5e3fdf3a397ce7d49530c9f421f5ed3fa3352fde4 |

Private `FileCatReleaseEvidence/ci-37643607270-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | a5e2f42b716fad263ee79336b26fcb8d774506e7374b4c925835b0903962baeb |
| independent-draft-guard-ci-v1.json | feaa5a31fbf2b1d3b8846db06b0fd19d253c0424e7696c2f3505646627dd2592 |
| independent-fixture-ci-v1.json | a623b37acbffdc1b6cb07f5208e7a485b799f57908f037609b5458a20e663481 |
| independent-i197-ci-audit-v1.json | 27d72faaa0a41fbd276e6398348b8fe293a87e921e185c2a5c006ee7e2bb530d |
| independent-i197-ci-cases-v1.json | 9f1f1f0c70f103c7b317d1270b31f163595c30d4015df6e47dc10b3992b50e9a |
| independent-producer-policy-ci-v1.json | 18add3dd9854e3182e4b499c6434d21b012041bab47325ae0197eef2a7e362d8 |
| independent-restore-ci-v1.json | 4506773d02769f56dfc96d5da634da58418b9c300f6f766b46fbd541ebd9823b |
| independent-separation-ci-v1.json | b374ff3291e090d0c76b35df95eacfc842874a4c1b2969a2b8a0a3246f10f437 |

Private `FileCatReleaseEvidence/release-assets-20261006`:

| Retained path | SHA-256 |
|---|---|
| collect-i197-ci-v1.py | 5f234f3bfaa849d5f7419ab99943eaac8daad5d8faec15405c1d4e3366091034 |
| verify-i197-ci-v1.py | 8dca059e1e1172c674fe502f51b39c10135db6e33bd24e46bc9ba4d253f9d9fe |
