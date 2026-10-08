# I228 — admit notices only for consistent published package metadata

Notice producer `859a2c2d809962cd47a140f1afa2bb191fc2ad2b` passes **63 committed-source manifest controls: seven original manifests accepted and 56 altered manifests refused**. Original CI **37824396918 attempt 1** passes all four required lanes and repeats **252 controls: 28 accepted and 224 refused**. All 12 earlier notice/license/output refusals also pass locally. This is preliminary metadata consistency and actual notice-tool/output evidence.

## Defect and correction

The notice tool reconciled its snapshot to the App lock and checked runtime-pack identities, but did not reconcile ordinary packages in the published `FileCat.deps.json`. Every one of 56 altered metadata cases was accepted by the unchanged tool and copied 50 notice files. Unknown packages, changed versions/hashes/kinds, duplicate identities, missing library inventory entries and an absent selected runtime target could therefore receive the existing notice bundle.

The tool now rejects duplicate case-insensitive published identities, requires ordinary package ID/version/`sha512`/kind to agree with the pinned notice snapshot and dependency lock, and requires each library in the selected runtime target to appear in the library inventory. Known package IDs cannot bypass the check by becoming reference entries. Existing runtime-pack, snapshot, path, exact license-byte, source-link and new-output checks remain.

## Exact controls and provenance

Seven original manifests cover five self-contained publish RIDs and two retained Windows framework-dependent outputs from the earlier [notice record](E-I03-NOTICES-pinned-full-texts.md). Their exact raw bytes are pinned in the fixture index and preserved in Git; they are historical metadata fixtures, not newly qualified payloads. Each crosses unchanged, unknown package, wrong package version, wrong package hash, missing package hash, case-duplicate package, missing package library, unknown runtime target and changed package kind.

The actual built C# tool executes every case. Independent readers regenerate each mutation and verify original/source/build/payload pins, exit/stdout/stderr, destination absence on refusal and all 50 exact copied file hashes for each accepted case. All four native artifacts retain the tool payload, build logs, altered manifests, case logs and accepted notice trees. Twenty original artifact digests and every member, fourteen complete test inventories, four toolchains and 92 actual locked restore graphs verify.

Only notice tooling, fixtures, CI integration and exact-byte fixture attributes changed. App/test sources are identical to `9f643d9414641304ec7effd940a1827ffc1f0e6c`. Its canonical Core 2218/61, Remote 1568/135, affected App 611/2 and full App 1191/25 retain that producer. Native CI at `859a2c2d809962cd47a140f1afa2bb191fc2ad2b` independently repeats I218–I227's 2790 addition names / 10872 passes / 288 explicit platform skips, including 256 actual Linux/Mac owned OpenSSH passes and the 16 strict current metadata preconditions. Historical gaps, missing outputs, capacity failures and timeouts retain their original identities.

## Retained execution corrections

The first preparation refused an already-created private parent before any fixture receipt; a fresh preparation retains the original refusal. An earlier regression observer incorrectly expected LF after Windows text writing; the partial run and CRLF marker remain unqualified, and a fresh exact-byte marker control verifies all 12 refusals. The first adoption backup refused an existing parent before main mutation; a fresh backup/install preserves it. Original Git whitespace diagnostics concern only the seven byte-pinned generated CRLF manifests; narrow fixture attributes preserve those bytes while source/document whitespace checks remain active. The staged-source reader initially omitted cached paths; its original refusal remains and a fresh reader verifies the actual staged set. The artifact collector first expected only the three legacy control members; its fresh successor omitted the retained compiler-object subtree. All twenty original archives and both refused expectations remain. The final collector verifies the exact case/input/output/payload members and permits only the notice tool's build-object subtree, checking its package-free graph and matching compiled DLL. No executed script or raw result was overwritten or native run replaced.

The first current-tracking reader also refused a missing legacy nullable transition field after the document/evidence checks passed; no final tracker receipt was written. Its executed reader and transition remain unchanged. A fresh schema adapter records that no runtime producer is awaiting qualification and pins the original refusal in the final tracking receipt. This bookkeeping correction changes no product, artifact, scenario or selected evidence hash.

## Remaining scope

I03 and V20 remain Open. Consistent declared metadata does not establish actual binary composition, embedded/static/native source provenance, legal eligibility, managed transformations, every runtime-loaded image, signature authenticity or a full per-artifact SBOM. Coherently changed manifests/payloads and other metadata edges are outside these finite controls. Candidate/freeze/signing/notarization and the explicit human GO remain outstanding. The physical-source HOLD remains in force. This notice qualification uses owned files and hosted CI, without a physical source or native desktop interaction.

## Selected evidence

Private `FileCatReleaseEvidence/notice-manifest228-v1`:

| File | SHA-256 |
|---|---|
| ../prepare-notice-manifest228-v1.py | c0372dd750215f69192ff8f09c2bfaaf2733804932d393fb3b1d3a37899f60af |
| ../prepare-notice-manifest228-v2.py | 979f4b5499d4557ea4204e311701d7c9b5a3e1bbbe029ee2ab47fd700c44c9c9 |
| ../correct-notice-manifest-preparation-v1.py | bb7d403b41b4aece803ef9e3697e0936c499653903f9331b60e34800d7699bb8 |
| preparation-refusal-v1.json | 0c450c41cbd0a6e2183c1dadef5373a606586a163f16d3c8a4dbf967ba06c4d4 |
| preparation-v1.json | 711330e6609c1024d892d6724ae98b463da88c549d9b0514a2dfd39150c351d6 |
| Program-before-v1.cs | fa3f3d44b687b2be99c18be025ad1eb257651cd7b213e20f8d4f20f436f61c1a |
| prepare-manifest-candidate-v1.py | 52bca24c307dfbabcf9e9eafcf3aedbf91262cbda8e2f8dd70127ac9ef780299 |
| Program-candidate-v1.cs | 5283d91c1cd15ba64b77fc49d3994b589cae51e3f18cce155cdca44859160412 |
| candidate-preparation-v1.json | 3f9d20684cf218dab9cfdac8c32815a86a221b836f245cb53d734431bb57c6f3 |
| run-notice-manifest-v1.py | 46cdc2da15bca487d315b2cede399c9e272a83368dae240fb49aa21ee168ff4e |
| expand-manifest-kind-controls-v1.py | de5bcac2f7c8d35cbdae76c8fb1f78a66369120a87cbac021057b88828e7a387 |
| Program-candidate-v2.cs | cb631725adb7e3a229d8033715ca30125ca6d368547964adcb69d7774e4e9bb2 |
| run-notice-manifest-v2.py | 3b8875d2b1b2db0fe54fdc2b49ab09709a27f3c71902e2c20e17956eaa72edf5 |
| expanded-kind-controls-v1.json | 9e3e5270f6d42d6e9845344d25f5f141435d6e240f8edb3fcf27469eb5d48f2c |
| run-notice-regressions-v1.py | 779ca0d2d5941a758b3cf7542da201901770a1321302121be5afb108427dad61 |
| correct-regression-marker-v1.py | 9d0bfcf9e7ce0b98621dfe793dd56413311232f7482f9a76427e0d2f7f649f5c |
| regression-marker-refusal-v1.json | adfd5099799553653f8ecb36778029c6c9d87c18bdcca20f683a7faee8a2acd8 |
| run-notice-regressions-v2.py | 45b9f21a2d36781a43bdd2f0f6c5a63d0576248a56c54a0aa306f6638ef90418 |
| regressions-v6/command.json | 22be2f4f3e7757d45071014c5b2cd5e9b2c23b925343b4a583503f0c1cfc5e61 |
| prepare-public-controls-v1.py | 2e4fae42c6ae341b6525e0a6274fc1901f63b6e21baf2355f79c59a3c50d4c20 |
| validate-dependency-notice-manifest-prepared-v1.py | 6bf4964277def9e8be31b4a9f6131ba394169cd39b1ae9d5c6c8f1b27d0c08ea |
| public-controls-preparation-v1.json | 3a85681db9a42814fbf39e66dd8309d341bf83aa198cc917785b50608e050174 |
| seal-notice-working-v1.py | 5de9d8c0fa08ccb7fdf5ffe1b6aaad46b6313b0b6f5f1c6f49854fa685cb3c48 |
| install-notice-adoption-v1.py | d683acc59c842b4e1c6868ca9e8267076794d1069dbd0364cf10b88a1f465e39 |
| correct-adoption-backup-v1.py | 697ca4635d8b92ec678a383615982464f9aa600bdcd3c805c180b2840168a861 |
| adoption-backup-refusal-v1.json | c66485f5819249c3607eaac6d38a53f72fa799b72e0c56e898bd72774e4be6ba |
| install-notice-adoption-v2.py | 61918262e78b13f87f632ea3688a8d199f994d162a4dd506b260fd090a53e47f |
| main-install-v2.json | 92024af58aa71a6e51dbec9ed59fb3c258a4be6df09ee353f2b43306e9dacc2b |
| main-install-v3.json | 8f3e0149efced0b7220c84af457771df8727ba5fd0f6bffc862c4ae05f2c09fe |
| preserve-manifest-whitespace-v1.py | 8291e99065d1ab5c8668c2f6adbb80f9d7002157ce5b92df72bd7eaac5af609a |
| manifest-whitespace-refusal-v1.json | fcd45582bed78766656a58616d69b74f40c4d3d6170d2b92d539ba6682c8cd34 |
| staged-original-whitespace-check-v1.txt | 832fa32cc166d410cc838963df5e92a9fed36caef72e37ebd3903cd9d0ad2d72 |
| gitattributes-candidate-v2 | 0bd296ea5f6fc5ebd7ee4667db801058b03c450bb07328416e6aa1af822952de |
| prepare-ci-adoption-v1.py | 2001778ab98114caccb18c62728c9f060683b83d1a7337fe3bf2a2cb5e469fd3 |
| ci-candidate-v1.yml | 82b504ad318851a5f59ba8d78e3f7426185df0a751355aea5be38aa233b8e1d4 |
| gitattributes-candidate-v1 | a9435754c3f652bf205238b0996389c0c9e465e42c909c055e39ffd09ca8c6e1 |
| ci-adoption-preparation-v1.json | 7584824ab68acff8297a8b839f3d7504566c44bce209bf3930db9b8fdda6e42d |
| public-working-v7/dependency-notice-manifest-controls.json | 58c3b4f3ab9caeda1bed973886b3cc6b40dcb3a7a5cadf7e30a160e068f24cde |
| seal-notice-public-v1.py | 28d5a1393773b7eeb54eed01388703e3316ae13b06e0f3f24a21cca105fe605a |
| independent-notice-public-v1.json | 763c4d136ca0a3120eb8eb7e499e3f34eba84efc813dc8354886f5f820360e47 |
| seal-notice-public-v2.py | 7ccf647f0c3e8b37dcabac7fd0e4f63cea0549684c4486b9a9adcfdaaf7c65ea |
| correct-staged-reader-v1.py | 89cfa61eaec93cefbabd5d2908c8c2fceb07b5bd89d033d0dde3e63db50f0e4e |
| staged-reader-refusal-v1.json | 4284f736764e2fb1cb699bcf6826c1c5d0315a08fde58e2c2b7d2ec88041d018 |
| seal-notice-public-v3.py | 7079ccecf20aab56fed14f6b2ddfb1e9305e2c7353feeef6578b49c027dbaf82 |
| independent-notice-public-v3.json | 911926190c9717f6dca2b0ee5a9faf4b5dcb163b0c6dad7099d494daa6f65201 |
| seal-notice-clean-v1.py | e5ed39ac18f57a93ca62d362840041e203e6c9726a9788eb62960b101175f9c3 |
| public-clean-v8/dependency-notice-manifest-controls.json | bfce35250ffcbc1c9908341c37592024a72b903008d475a834ec7e60abc1d72c |
| independent-notice-working-v1.json | 4f72e8255392057c835cd74e98bc7458ae1fdc8c2143c3c3ef0de6bf3fb8ad51 |
| independent-notice-clean-v1.json | 77e3647c7222b659b38547c4ea55ada8eeaff770ad54ae107df6345f1cd4f3f1 |
| final-source-v1.json | eede68362fb90344c977bdefc049ca30a973e305583fe5c021a4e194e1630c19 |
| prepare-native-readers-v1.py | fbdf27744dc148f9f12255870187316bc75d81e62ffc77bb64edcd55fc23109d |
| native-reader-preparation-v1.json | e4d29df291cb034ad2357a87b0efb4378960074e61338f8fe1ddc1289800a25e |
| native-parent-reader-v1.py | ce20508200953c01ade958af18234350e99197eb45535ade51be7ab293a71b3e |
| retained-transfer-native-reader-v1.py | 078c6fef3ecaffac9a97a81af489e4ac79a9cc60c6ef845f3edeb01d672f6513 |
| retained-copy-native-reader-v1.py | 62d8b45ea655d4342d8e3ed237a37b120f180e7bb41caf57271a7524f2801a84 |
| retained-upload-native-reader-v1.py | 98ab7f8eb74e1ca7be6ec33a340fd3fa976cda93be4f097800d386279ab45672 |
| retained-tree-native-reader-v1.py | 1c5fb8dbc3fd457b5a02a6ab2fc8688a502742bae2e47c03c0119f0cd5cdc66a |
| retained-failure-native-reader-v1.py | 3e91b2ad4e9d237d553d4a3317e6d213f2813743460267f39a3fdeb28da20782 |
| retained-resume-native-reader-v1.py | 532fe44d8d19714067aaed04ac23fa7cbd8da16b28c2d9a175344b251bb9f3ec |
| retained-local-link-native-reader-v1.py | 437016b865553ba3ead8853da97f11bc98c203e65adac013d909163b644eaaac |
| retained-notice-clean-reader-v1.py | 2193ea05e9ada660c8353d07e2aeb72a20004a91121e90e078df4ab84f887be3 |
| seal-notice-ci-v1.py | c574a27840d2ede2dc05732153dc47b2e95643cb20c523e7602f6c51dee9b8f3 |
| update-notice-documents-v1.py | 6c0bdce816d20c9d45c9763652bc3e463e9d01a4ad6919d9a6d04889361c8651 |
| prepare-tracking-readers-v1.py | c00cba11388f23a77d2d45d51ec6b6a8952943189848ba524b6f2bd8687611f7 |
| document-baseline-v1.json | 356c40c1924ae25f257cb71c0cdd65923b2d3d36f01ba8d63861829b2ca7f544 |
| document-transitions-v1.json | 427f6234296a1c9a3480cf90c07d44803b92a9e4e9b549edd0fc2784d20cbfe4 |
| ../transfer-late220-v1/collect-transfer-ci-v1.py | 4ca683e9cb75dffe0cb39819c1f03661dcc642e3e6ed82d55bbd355e8894bc54 |
| prepare-collector-v2.py | 817c32e676d8ed65cf39bd2f118565f94743ba4b9de67b084fcd725997a6ccb7 |
| collect-notice-ci-v2.py | e46aae20e6be85cb6d94f6c0c61ae3e49d319326c3fcc3461fbd3705d0cdd663 |
| original-collector-layout-refusal-v1.json | 12467e0212a996153a4b39d4d646050cda5fa0e3aa5a1837555492506e0ae19e |
| prepare-collector-v3.py | 4f5e77c3a53bdd2661449e7d692a7f61694edc0a9af2aea0f83404403134947b |
| collect-notice-ci-v3.py | bb36776dbae344e128a66ef280d302e979a96d416ab3ac5d6d453e65bb26515a |
| collector-build-object-refusal-v1.json | 82acd932bbf3c7de417198f962efce279bbda7f2c964aa64b0f97fa4255fed22 |

Private `FileCatReleaseEvidence/ci-37824396918-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 001e88719018c99e4327fa3fc62368bdf0bbfbfb19630ccc109d5511d1f2091b |
| independent-restore-ci-v1.json | 7704d5ae28d66d3dcfc44321d7b40a07a08dee7ee02da6f35de74614f2454fe9 |
| independent-notice-ci-v1.json | 94234cbdb342919b7e3840e3dcd04ea172d15511dceb76abfa06f67c9e8b52fe |
| run-native-stdout | 31ece1a871b34faf70eee0cd175c5d330063ec67a36ad9b0f6e55435fc2749f6 |
| jobs-native-stdout | 6ec52f37f46173b3f5ebc13c0002b19744dedbd1fbc08bca0c55d97f48c9d10e |
| artifacts-stdout | 5360fe4e64bb5f9ee5f211e07d6665ee8bd725a80cc7c9097dc49a80f671b8a1 |
| complete-run-log-archive-stdout | 70363995fa8b04d7e4e784982d61e2fe581ea5ae5c64821dae2429ba4ed5781b |
