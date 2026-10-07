# I205 — signature, overlap and attributes preparation

**Preliminary remediation qualified at d6062cc24d835b9155f84774671b883f3d299a97.** This related batch passes 153 working and canonical clean checks (115 App/38 Core), including 47 additions, with zero skips. The unchanged c131ba5 baseline has 40 failing controls and seven positives. Original four-platform CI is sealed green below; wider provider/path races and exact-candidate qualification remain.

| Confirmed gap | Resulting behavior |
|---|---|
| Unknown-extension archive signature probing opens/reads files directly on the UI and ignores registered content admission. | The actual Enter command opens and reads the registered content on a shared device worker. Genuine ZIP/TAR signatures still navigate; short reads accumulate, invalid byte counts/error/unsupported content fail safely. Content is disposed exactly once after an active read returns. |
| Recursive comparison resolves final paths on the UI, propagates lookup errors and offers Synchronize when paths are unknown. | Final-path lookups run on shared workers. Known overlap and unavailable paths withhold Synchronize while allowing comparison. Obvious lexical overlap needs no lookup. Existing core lexical-policy semantics stay unchanged. |
| Attributes/Unix permissions collect metadata on the UI and silently omit unreadable selected items. Windows can show every checkbox checked for an empty metadata list. | Every selected item must have readable information, and Unix permissions must also be readable, before the dialog opens. A constant-size intersection/union summary preserves uniform/mixed states and detects changed permissions. Failed preparation creates no job; cancelled preparation releases its captured selection. |
| Preparation can publish after its originating tab changes or closes. | Refresh/navigation/close/shutdown stop further work and suppress late dialogs/previews/navigation/notifications. Active synchronous calls retain their owner until return; eight concurrent requests share finite device workers while another device progresses. An attributes dialog already shown keeps its captured selection for an explicitly approved ordinary job. |

The tests invoke the actual Enter, Attributes and CompareDirectories commands in headless Avalonia. Fixtures create genuine owned ZIP/TAR files and wrap real FileContentSource reads; held/error/short-read controls explicitly substitute the registered filesystem provider. Metadata controls explicitly replace only AppServices.Platform's file-operation dependency after creating the normal app, recording actual callback threads and delegating ordinary item information to the real owned files. Final-path alias/unknown outcomes are disclosed synthetic controls. No physical device, installed native UI or remote-server claim is made. The two state controls and three approved timestamp jobs use the OS's actual supported attributes/permissions branch; The original native Ubuntu/macOS CI now verifies the Unix branch as well.

Forty original failing controls verify absent provider admission, UI metadata/final-path callbacks, propagated IO/access errors and incomplete metadata/unknown overlap outcomes. Seven positives preserve uniform/mixed states, approved captured jobs and lexical overlap. Held baseline callbacks have a bounded owned rescue timer so the old synchronous UI route cannot deadlock the fixture; final controls check callback admission before queueing additional requests. Assertions, worker limits and deadlines remain intact. Initial ambiguous ResourceProvider, nullable notification, Button symbol and missing extension import compilation errors are retained; they are not product baseline evidence. Earlier 40/45-case and intermediate corrected passing inventories remain immutable.

A canonical clean export checks every Git blob/mode/source ZIP, actual payload hash, raw TRX definition/name/outcome and all 47 JSON observations. Independent checks verify unchanged source bytes, no active disposal, no late publication, bounded shared workers, other-device progress, complete metadata, genuine signature outcomes and explicitly approved frozen jobs. All 1484 retained actual payload references, invalid/intermediate runs and line-ending normalization records verify. This closes only this preliminary subset of I06/V07/V08/V13/V17.

The preceding [I204 original CI](E-I204-archive-transfer-warnings.md#original-four-platform-follow-up--c131ba5) is separately sealed: four lanes pass, 232 archive additions/468 content additions/128 directory additions, 20 server digests/every selected member, 14 full TRX inventories, four builder receipts and 92 locked graphs. Native Ubuntu setup succeeds with one actual mirror-list replacement. No original failure is overwritten and no timeout/test scope is reduced.

The physical-source hold, unresolved owner/environment gates and explicit human stable GO remain unchanged. No candidate, tag, stable publication or borrowed-machine setting changes.

Private `FileCatReleaseEvidence/mp205-v1`:

| Selected receipt or reader | SHA-256 |
|---|---|
| seal-preparation-batch-v1.py | a9b0e46557cf76f2d0b53ecc06e05892c2c1e957f2ee7d942f8f99eb2095feac |
| independent-preparation-clean-v1.json | cc06b50724be30af082ba515ba41446ed9808838d9ddb5767609070cf5570430 |
| run-preparation-v1.py | f552a8603b389326f11c0dafbf0f7224ce2cd48361aa7d81e04f0285cd876b54 |
| run-preparation-v10.py | 25e5e88bdbcd7a8787fed533fcd073743a143695666ed26ecbbaf9d073eb0ba3 |
| run-preparation-v11.py | fcd1ead203fecf7aec4de04e50887ec92b5bb2759e41a341931378104c4df6a5 |
| run-preparation-v12.py | 8e5e813bcfaccdd354f8b28f9c791701e13b5d37682eb45d83d7516fc686f546 |
| run-preparation-v2.py | c06642b8281500a00ee0a31633d49a463a735a2d62078245a3aa047023355a18 |
| run-preparation-v3.py | ea864474880f575a7dbf7524180c3c7dc39500e074bb7bd8d5bfc492a6d75736 |
| run-preparation-v4.py | 2bd38946aedd34cdd349ca29982b37b08786d665663b2b5092b010476aeea42b |
| run-preparation-v5.py | 8fb29f36c4f2b704712f3c753d8486d1de52f99ce72a862bd5ff5a384a277f4b |
| run-preparation-v6.py | bd151d3245c7af6573cb551bab96d974b6019b9f720dc0141d66b4ca04d8278e |
| run-preparation-v7.py | 5cb3e0650311b3157b203ae7cd45c29e9a76bc47e27e666e370eb4971fed8281 |
| run-preparation-v8.py | 9d5b248d33f7cea422895cab60c8d8a9e1bd507c8cc9dc61c27384d873059bee |
| run-preparation-v9.py | 948e59450284e50c1acd2acbb26a4dc5650ee1587a2a8c33c6fd2ac318c9611c |
| baseline-v1/command.json | 54dc28f5993294f039cb2bc27e95532af042de5c22d451c83310ba434982c4cb |
| baseline-v2/command.json | 31a765fbfe234e013637cb79554d97d2ff41d20f01df03ef6557861dc673ed0f |
| working-v3/command.json | 11db491035faf83cd205bfff4d517839017d5584d574933385348aa936cc17ad |
| working-v3/results/core.trx | fa6bae74b704040f54332f29608cac8070bf99761e93505bb92d3fc437c25260 |
| baseline-v4/command.json | 6bed9da9a2e2f82b1b721527a82880bf3c4081c650abc7c9f56d06f2e917cf13 |
| baseline-v4/results/app.trx | 46e2cdbfa8b7d9c51c3a7951c29ed55a27c82a0c744a68acfef4010763040136 |
| working-v5/command.json | 8b3c8b534964a359d8ecb8a87cf8edb6da44a8554fa9240d533068511b38795d |
| working-v5/results/app.trx | 511a142674335daf5c6f3ea07f156f13c96c4ee2c64d78d3bae61ace59bd7a5b |
| working-v5/results/core.trx | 2c98d762698157f7e76c63fc6e3dafd5157c1ef6a0c884d32f263247c75aa969 |
| baseline-v6/command.json | e3ee237190c1434d09553433d2e048f619b3b987c4af1850b6dfe1e3945c6f7f |
| working-v7/command.json | edc3fcdd109f65d60b244aeedbd725cac6df5d94da58ad2c3e297775fa985a53 |
| working-v7/results/core.trx | a1fca59c4619ebf51dab6d628fce59500d3465d2794c628eaf7800bd8b977da3 |
| baseline-v8/command.json | 0ecba61deaa26146caafc59616b4c5fdcc7bd86baf44a7fcb8d738c1f5580276 |
| baseline-v8/results/app.trx | d3fbab1ef0a441177d00bd9f1505e918faf5a966de972a68ad381d84ea403934 |
| working-v9/command.json | b18fcb228df7fd498de0e56d888e326a3dcde67e16801fa8be811c105f96c40a |
| working-v9/results/app.trx | 775f371a27461aae85b260083ea1f379f3c3a15c9d0313a70b9d84f80f8a5f58 |
| working-v9/results/core.trx | 9af70a4049b7b6b0fcebc2e68c04afb272ea9c99fb4af10b88a3bd2e9eb10587 |
| baseline-v10/command.json | 9e9345332c26f68f9449e436d6b4d7eefb0e18bd3bfa346d40e43dcc5f9c0373 |
| baseline-v10/results/app.trx | d89a024bea81ebe523a6a41de624491bd46e34cf305d7309c72102f668641694 |
| working-v11/command.json | be2e33894e74f36de89d0790527fe18f571fb16f1d1aef7d3a3d773aa8203ede |
| working-v11/results/app.trx | f54bc107c6eab4563dda47e32f5ea5832d49c7624b8e36fd17ea507be9cb3f8b |
| working-v11/results/core.trx | 224d76d82030ec76a7fd59b19a2409fb11592a5829a93a608e44c58716f68ecc |
| clean-v12/command.json | 1c999af3ee401a7cde575a5624d9cf800b9cc5f3a61f7b1019d40328a2c08e0e |
| clean-v12/results/app.trx | 885b1fe582c9a17968562ed925dd744f5bafcbd36003e67c6cde3328865f3b07 |
| clean-v12/results/core.trx | e4b1f2b3e0f65aa0bd715021c14abd0d4c29c30f936d025ffb83a670f42c0840 |
| baseline-v10/source.zip | 103aac46e854c9973829b7df415ed249ef5bad75981d19fe9d32659170b9e31d |
| clean-v12/source.zip | 97569c6f6246987f7a3179669d901011abafa9b2e238da6ad6cc39ade85148a4 |

## Original four-platform follow-up — 4a4f4ef

Original CI 37697121508 attempt 1 passes Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26. All 188 executions of the 47 additions pass, including the real OS attribute/permission branch; every available predecessor name/outcome remains. Independent readers recheck 20 server artifact digests/every selected member, 14 full raw TRX inventories, four exact builder receipts and 92 actual locked restore graphs. The earlier 232 archive, 468 content and 128 directory additions pass again. Native Ubuntu setup and its nine mirror/five launcher controls succeed. Packaging/draft jobs are skipped on this main push; no package, installed GUI, hardware or release candidate is qualified.

The CI source is 4a4f4ef0c1a17170c41a4cff8f90eae71916fde3; its 794 runtime/test/eng/workflow Git identities match the locally qualified d6062cc product. Local and native evidence retain separate exact producer identities. Original earlier failures and preliminary closure limits remain.

Private `FileCatReleaseEvidence/ci-37697121508-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 7a2c1fdd3260f5725652e81dc321c0ef9844c491f442dd870184e650ebf79d9e |
| independent-restore-ci-v1.json | 9c0e5c5e82199ff01423a9133185d13518e3c378b909898f2ca17959fb0e1d2e |
| independent-metadata-ci-audit-v1.json | 4bd0bfb66cff93c5982f047c4fb2c6e617d618e16a4d040834c75c9adb8949cf |
| run-native-stdout | 969de5c3735848e04797d280da5b9ce8327c1344e53e248897e786c8a9223e9e |
| jobs-native-stdout | 4361af8a3bdaad49e6aa6161be00d84ea592167824d1a230393409e8725898c6 |
| artifacts-stdout | d89c87afe2d844d6cc27ed2128ab12d24fde61bc95b1b4f444793f70fb36b8af |

Private `FileCatReleaseEvidence/release-assets-20261006`:

| File | SHA-256 |
|---|---|
| collect-i205-green-ci-v1.py | 6097c64e03d05a47d28c162e462e6a74fbe5fe8234d2763e4eebd3bb15e88d85 |

Private `FileCatReleaseEvidence/mp205-v1`:

| File | SHA-256 |
|---|---|
| seal-metadata-ci-v1.py | ee68c7dc154ca44e36b5e7512029fcea17820ce9695d5ba6a97f88eee341d37b |
