# I221 — finite complete verification and reopened provider evidence

Producer `8900f14a7cae6a6b94c60b1e1386bc8a3a1d14fd` passes **96 additions**, **2043 canonical expanded tests/70 explicit skips** and full App **1191/25**. Original CI **37775932177 attempt 1** passes all four required lanes, including **384 addition executions/no new platform skips**. This is owned-file/component and hosted-platform qualification; wider servers/accounts, native interaction, reference performance and candidate scope remain.

## Reproduction and correction

The corrected unchanged-runtime baseline at f5f5f74 passes 39 of 96 additions and fails 57. Forty-four cases publish despite changed revision, contradictory length, uncertain/lost-range evidence or an invalid empty-source read; 22 replace an existing owned destination. These are synthetic provider declarations over actual owned files, real staging/publication and original destination hashes. They do not prove physical-user source loss or real-server behavior. Six overlong verification cases preserve destinations but consume two MiB beyond the copied length. Six actual-file growth/truncation cases return a hash after the length changes; one empty-file hash ignores cancellation already requested.

File hashing now uses the opening length, optionally requires the caller's expected length, bounds consumption to that length plus one end probe, and refuses truncation/growth. Cancellation is checked before opening and before every read, including empty inputs. Local read-back and the final copied-move check pass their expected length. Generic streams without an expected length retain their existing read-to-end semantics.

Provider read-back retains the copied revision when available, checks complete/consistent length and partial evidence, requires exact EOF and refuses invalid read counts. Revision checks run before and after the complete read. Real SFTP queries a server for revisions, so the final implementation avoids per-buffer metadata requests; every accepted controlled verification records exactly two calls. Stable unknown-length/revision sources and partial-capable sources with no missing ranges/caveat remain accepted. After an explicit resume/restart, the new content revision supplies timestamps, read-back and the copied-source callback. Eight positive controls perform owned reads, a controlled disconnection, unchanged resume or actual changed-source restart and new/replacement publication.

## Matrix and retained failures

The 84 provider cases cover empty/65,537-byte/1,048,577-byte sources, new/replacement destinations, stable/unknown/clean-partial controls, revisions changing before/during reads, contradictory length, caveats/lost ranges before/during reads, finite overrun, negative read counts, early EOF and unchanged/changed restart. Twelve file cases cover ordinary hashing, already-requested cancellation and actual append/repeated growth/truncation at a controlled progress boundary. No infinite source is supplied to the baseline.

All 124 preceding transfer/resume controls remain passing: 220 targeted checks. Canonical Core passes 1342/61, maintained Remote 90/7, affected App 611/2 and full App 1191/25. The expanded total is 2043/70; the extra affected-App completion case from ae19f86 is now rerun here. Every preceding Core/Remote/affected/full-App name/outcome/skip remains. All four native inventories contain the 96 additions with passing byte/publication/disposal/query-bound oracles. Twenty artifact digests/every member, fourteen inventories, four toolchain receipts, 92 locked graphs, nine mirror/five launcher controls and every earlier byte/deletion oracle verifies independently. One retained I218 Ubuntu timestamp precondition does not qualify in this repeat. I218–I221 repeat at this producer: **308 distinct addition names/1200 passing executions/32 explicit platform skips**.

Original raw runs remain. Four initial assertions required CompletedWithIssues when a single refused root correctly becomes Failed. Six contradictory-length fixture cases initially used a misspelled literal and did not introduce the stated contradiction; that intermediate failure and the corrected original-runtime baseline remain. The first independent working reader repeated the narrow state assumption and wrote no qualification; its script/refusal and corrected reader are retained. The first implementation checked revisions per read; the final query count is bounded to two. None of these fixture/tool corrections replaces original product failures or proves historical scheduling.

The original native reader refused one I218 Ubuntu observation: the 65,537-byte both-preserved case retained the exact current file hash but recorded `metadataMatches=false`. That current repeat cannot qualify unchanged-metadata bypass for this case; its original stronger evidence remains at its original source. The full 252 retained I218 outcomes and every byte/deletion oracle verify, as do the other 251 original per-case oracles. The original reader scripts and independently isolated refusal are preserved. The corrected reader accepts only that exact pinned observation as an explicit coverage gap; it does not relax another case or claim that gap is filled. Strengthening and rerunning the metadata fixture is queued in I06.

## Invalidation and remaining scope

Rebuilt artifacts lose affected binary qualification. Exact current source/payload, broad local suites and the original four-platform attempt are requalified here. [I220](E-I220-transfer-version-and-progress.md) and [I218](E-I218-interrupted-copy-cleanup.md)/[I219](E-I219-interrupted-copy-selection.md) retain their original source identities; their controls repeat in this run with the timestamp-precondition limit below.

Copy-phase length/revision/admission, generic/SFTP-upload stream bounds, wider providers/permissions, initial descendants/followed links, native atomic handles/aliases, same-size or reverted-revision changes during reads/check-to-delete intervals, blocking I/O/resource/reference throughput, Shell/helper/DPI/formats, human/native workflows and candidate remain in I06/V01–V24. This is weak provider revision evidence, not an atomic snapshot. No VM/Mac setup or physical source changed. Physical HOLD, contract/custody decisions and explicit human stable GO remain.

## Selected provenance

Private `FileCatReleaseEvidence/transfer-verification221-v1`:

| File | SHA-256 |
|---|---|
| baseline-v1/command.json | 8a328a2e98b325836aef7c5c6df42de6534adb358651a7a87c40416eca4f6757 |
| baseline-v1/source.zip | a92b56f77da3b82295c9c94e24a5756a74828924b06fd6c6e489792226cdab8a |
| baseline-v1/core-stderr.txt | 24eafde46265c1613b854010fc6beb2273beeed57c1b296753100f1a68a53ff0 |
| baseline-v1/core-stdout.txt | 0f00537cd812212eaf43e4f5389d45a109dd332c4b80e19e2067c0c57929f8e6 |
| baseline-v1/results/core.trx | 762aa10f165b1448af99c4986911c0063fbbea1ffa2544bea6a497057e649d79 |
| baseline-v2/command.json | 57e8da3e6bbf96d986844c66634caa5c128354325500d4c8db25bb51cded06ce |
| baseline-v2/source.zip | 20a20e8e0e43d9a3f47e9bafd27580381fd3635a64f7aad79317be1a2061ff2f |
| baseline-v2/core-stderr.txt | 8113e4d70ee71aecdbea8ee4d6178f4907447e30e5c933f5dc7504fc8a6444d1 |
| baseline-v2/core-stdout.txt | df2ec00dfd43126e8ed9112e9650eb20ab7249dfe82332e2749230c30250785c |
| baseline-v2/results/core.trx | b4092af41c274865211e984ac58346d81b7dfc57c48641eb789a2a66fcdab6fb |
| working-v3/command.json | 6592f5082a4890c6c5de2590f6f87b4dcc756410e5fbcf9819e81d047bf737f3 |
| working-v3/source.zip | 18311b0cca45f2b0132848f5e941530d38c0cec9eeceb5fa8c684e077ad448e0 |
| working-v3/core-stderr.txt | 192a8b1fb4dc4a765f0c02012593c6961ee8b211665d39214205a80f2efec609 |
| working-v3/core-stdout.txt | db4938d1a396b459ece66d0d1e578b744f0932a9b8cd808f65fe39ea928b8bef |
| working-v3/results/core.trx | a589b7b22421317a2671be381ed59d2d716f3defd5bdc715ddc9775f199e2dd6 |
| baseline-v4/command.json | c8bb5ee713948a10f872522a9deb46fb73a064d09781311cb6f2434cda1f4f70 |
| baseline-v4/source.zip | d8048511a19b7c84d6323e3aec172f092134cbc43155c6f50fa75fe7bdb01781 |
| baseline-v4/core-stderr.txt | 6b4998485e1db5659374a2eeeae84cfab8002e1fd6507618a57da60e8dc0a87d |
| baseline-v4/core-stdout.txt | 54510dc8a08f600aa060ca6ea66d7ab374214a99a9e10cb42f5c1e535dacbf18 |
| baseline-v4/results/core.trx | 0ffb4a7d6f44611ad385291f1a7f18e218a4aba64dc2155f2c06813d53b3d40a |
| working-v5/command.json | 8ee374f7dc43c3da05a72bf04da4d11a7dc7600155cea32213ca753458d538d2 |
| working-v5/source.zip | f7560426ea8b59588dae15bb5357a0e4ccaafbbf9f33a1cca9a3b63eb77833c4 |
| working-v5/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v5/core-stdout.txt | 706a8804ab50a395c8de8d56da0e1ee61eba8b964df2350a2e365cc2e1b876f9 |
| working-v5/results/core.trx | 522112d8e33da8b3a4cd43cb3bc169477d0ce4ebcdfbe82acba5c6de0ae25076 |
| working-v6/command.json | 0287a1c7dd1ac99d25f831b91dbc5b4da80f6990287b27432d0151bbc4570b63 |
| working-v6/source.zip | 4440db9aa372f718293a060a54a704a2dac833c72679c9f85ba198cce96f71f4 |
| working-v6/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/core-stdout.txt | c73d9ab032f59505e1026586b7976395132f0edffacd89afcbaabe7cc06cc86d |
| working-v6/results/core.trx | 8a394ebb2482892e676eaebecc64d4c8ac6293b800ef32b21ce509913e12024b |
| baseline-v7/command.json | 6ff640410c4acce33ffe1d99d6c079fb9a99be1aa5ba38eaea4bc3488f4ab8c3 |
| baseline-v7/source.zip | ef1e2a8e0232194657ea73f68608c4912295e9b9cc05e9bdc7b20b1de18cdfff |
| baseline-v7/core-stderr.txt | 3f022e3945da9a2e1cf2c7a8e55cc20afe98b122671184da1f57d092e22b28ec |
| baseline-v7/core-stdout.txt | c65d6d144fe31ea020a76dafd677dd71b407006db4a16b9986ad0fd5c223826c |
| baseline-v7/results/core.trx | c2ae4e22112779fef4e88f07a9793d59a866232145bbe1fc8999ae32c3d1f9a1 |
| working-v8/command.json | 68bb783759c47a25e5136ce9072ab6a040ecb147ed5d073651c8f030bd496aec |
| working-v8/source.zip | 5d6c314e7659066cef23039ff152cb8c56b246bded21dad7690567f05c29ccf9 |
| working-v8/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v8/core-stdout.txt | 7fecd3b426bffc461a3da114e5c5149b4eda20432ed449d5dd2ab4c58758d73e |
| working-v8/results/core.trx | 0f03441464f9a51123be1b34dbd5364c3fb13b8b9ba5aaf46bbaa1be076de3ac |
| run-verification-v1.py | ed5320c4080f6b947880a8bd071c910ade4041a1685bf28d5baebe480856071b |
| run-verification-broad-v2.py | 6a04565cfd3ab249c10240429c278b0a802501f4559c3d8695f8386cfebdde4d |
| seal-verification-working-v1.py | d72a56c7261e5a8d8c6a51f324f64d397fd0efec615076bc8dbbbe108ebdafad |
| working-reader-refusal-v1.json | 1003d9bdfa15370336f9f9787512f3fa237b84d3247da1c0b4fb45b2e2236d75 |
| seal-verification-working-v2.py | 493012e79613e16c5c14d9aaead2ca6aeb99c9fcea510d42892aa916480c3e1d |
| clean-v9/command.json | 073ffcfc2cb84d4d36cd6534d6b59a5fb56b0b2fa15191c998ccdba9744217d2 |
| clean-v9/source.zip | 411fdce5d049c3db2489f5998be5184a1189c963fc148084bc6e64355a2577e6 |
| clean-v9/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v9/app-full-stdout.txt | 2ea282664da3eedcd9ef9f18826ecce53dc16b8582207290bc441075a09404b9 |
| clean-v9/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v9/app-stdout.txt | 040cc4d7f3a73b77fb8e693e0dcb628b2cd3b4e4990b221cc1ed3488e4116130 |
| clean-v9/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v9/core-stdout.txt | a8aa376b822bcceec8532dfb0c6b3c0075a0112fa94729b67efebda1922003ba |
| clean-v9/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v9/remote-stdout.txt | 1e9dd3dc2d03721908be1899e5240b0fc1357489da8d18d1ee8367eae008a186 |
| clean-v9/results/app-full.trx | e743297894c16fccf2373a5128007344068d3ddc130cc4aeb07fdb4dc110c97a |
| clean-v9/results/app.trx | ce6a9f702ca6fd15ec3ca07bb07b72b7d11d553abf394cc22c6a812d22e5ea7d |
| clean-v9/results/core.trx | 1f294508e088c8e803b0092d06f7dd75caf0ba9ea5f1ef8716552a8a15b68b88 |
| clean-v9/results/remote.trx | 2753d6e96b004361d272101d3e931f3abc4a8b5f27d3c22a1d3951dccc809654 |
| seal-verification-clean-v1.py | 541dea8ef5338e760d3bafc70d4b8b75e9009616b86722d9a43eb387ebe712a2 |
| independent-verification-working-v2.json | 97598b36a55dd6ae42fd0cd3a6d431df545bd48bf5e205e6eabb79fc27af2e6f |
| independent-verification-clean-v1.json | 299808b45b3a6b1564586e76327dcf399195676c96c5e6384227d8e607612f69 |
| native-parent-reader-v1.py | 8686a448bb80e577f4bf20353b472f3068ac594cdc21e2024432537e116d4ea3 |
| retained-transfer-native-reader-v1.py | dcd4a8bb55453fb13bb1a207411fc12e9c90623e337b229e5a2a32c25d74222d |
| seal-verification-ci-v1.py | b8fe51ad4c8f10aa5a36f4cb10dddf64cc062523b5e944e9c269c896e948f934 |
| final-native-source-v1.json | 9c18f818febb689a327e5c294ab51c97a8421ec5516ce2082455fd4f03f80d99 |
| document-baseline-v1.json | b8127d6620ece17b92804d23e9f43a8eeede6dadb1aa654c1aa5887d72c87908 |
| update-verification-documents-v1.py | bb65347c1df0fa5d864ca50c7ca02d0f69cf161bccb514fda5dceb5df5f04928 |
| document-transitions-v1.json | d4506563557ed52056c1114da25e3e4726ff9008b1ca6ae067c8ebbb6a21fa23 |
| native-parent-reader-v2.py | 46b0d5f78b3fdca59129945a08b215309f5cb69f73034a6e9374a2f460051bc6 |
| retained-transfer-native-reader-v2.py | 91296feda3093d6bd00590eccd647a541a4061f4b80bd2bbf14efd934ea7822f |
| seal-verification-ci-v2.py | e8ed9e9bf399b5a0fde5289c5596cc48fdf59fb5930412f18b9cd3049e962a98 |
| native-retained-oracle-refusal-v1.json | 7d19e53ef07d356524b7a4aaeca079e2d08e0a38c1c7a832f7aa7851ccf05eac |
| diagnose-retained-oracle-v1.py | 0e76bc9b472f3783bee478dd47e6652013aa7ea212b3242bdf55dcb946e07e86 |
| update-verification-documents-v2.py | c8119487ac625250b6bdab96116e0def31737f46ba09dd67351869b35c45217a |

Private `FileCatReleaseEvidence/ci-37775932177-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | fd7a60ee52b9e4178f4f40f961b828c6af462f7ca767f1b47d95e4afd2bf5268 |
| independent-restore-ci-v1.json | 8b369faac10a5a37944fd095329788dde05301a3c7421d5405327c590f613083 |
| independent-verification-ci-v2.json | 1e2325256aa43b00fac9560891735319cd73ff79e507c0b9e0e4d20745e2c6b5 |
| run-native-stdout | f2b08cee31c5350607650b986b7990d3d63e4e292e8cc30dae3cf49a97be204d |
| jobs-native-stdout | 2b7b56c87e21ee43c6458635e12754028ec31b8792c56a8326cc67bce62f8076 |
| artifacts-stdout | 8eaeb12f8589794caff5452a877669556b2b36452797e254c659a739b531788a |
| complete-run-log-archive-stdout | 2a3cdc8f18f01d816036ac1a0cea20a662ef940f1f0e18f84058fd4cf605d5c1 |
