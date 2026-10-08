# I226 — resumed-source ownership during validation

Producer `17d3af62197cf3fdd1d160489b43e58ae503476b` passes **240 additions / 4269 canonical expanded checks / 70 explicit skips**, plus full App **1191/25**. Original CI **37809727002 attempt 1** passes all four required lanes with **960 addition executions / no new platform skips**. Tests use real owned files and local destinations, with controlled provider failures. This is preliminary component and hosted-platform evidence.

## Defect and correction

After a source read fails, FileCat reopens it and checks its revision and two 64 KiB ranges before resuming. Previously, only IO/access failures disposed that reopened source. Other revision/prefix exceptions escaped before the caller took ownership. The corrected unchanged-runtime baseline reproduces **120 reopened sources left undisposed**, measured before explicit fixture cleanup; no real user's source is used.

Resume now retains ownership until all admission checks succeed and clears its local ownership reference when handing content to the caller. A finally block disposes an unaccepted source on every exit. Successful sources remain owned by the copy until its existing disposal. Existing known IO/access retry/skip handling remains. Failed prepublication copies discard staging and refund progress through the already qualified common-transfer cleanup. This correction does not establish atomic source identity or interruption of a blocked provider call.

## Controls and retained adverse results

The 240 additions cross 131,073/262,145-byte files, new/replacement destinations and Native/ReadBack. There are 224 failure cases across revision/head-read/tail-read/read-after-resume and NotSupported, ObjectDisposed, InvalidOperation, InvalidData, cancellation, IO and access exceptions; 16 positive controls resume unchanged content or restart changed content. The first owned source drops after 131,072 bytes. An alternate owned source supplies changed-content controls. ReadBack opens a separate owned source for comparison.

Each raw case records original/alternate/prior/destination SHA-256, exact read offsets/buffer lengths, open/dispose counts, temporary namespace, root completeness, job state, issue/decision text and copied/verified totals. Independent readers regenerate these observations, including the head/tail admission reads and resumed offset. Leak assertions happen before the fixture disposes all of its owned sources; later fixture cleanup is explicitly not credited to FileCat. All eight prior resume tests remain.

The initial private fixture fails compilation with CS9105 before any tests or TRX exist; its exact source archive, actual payload references, commands and diagnostics remain. The first compiled 176-case baseline retains 120 failures. Expanding it to 240 adds 48 assertions that incorrectly expect the latest known IO/access validation reason in the final skipped-transfer summary. The first expanded candidate retains those 48 assertions after fixing the lifetime; byte/lifetime/cleanup checks pass. A fresh fixture records decision requests and validates the latest cause there while preserving the original dropped-transfer summary. The product fix does not change for this fixture correction. The corrected final baseline passes 120 additions and fails 120; the final candidate passes all 240 additions plus eight prior resume cases.

The first evidence reader refuses the initial compile stage because it has no TRX. That reader and refusal remain immutable. A fresh reader explicitly validates the incomplete compile stage's source/overlays/actual payloads/diagnostics and does not invent a result inventory. All intermediate sources, failed TRX, compile outputs and reader/fixture corrections remain pinned.

Canonical Core passes 2218/61, maintained Remote 1440/7, affected App 611/2 and full App 1191/25, all at `17d3af62197cf3fdd1d160489b43e58ae503476b` without overlays. Twenty native artifact digests/every member, fourteen complete inventories, four toolchains and 92 locked graphs verify. I218–I226 repeat with **2534 addition names / 10104 native passes / 32 explicit older platform skips**. Earlier copy/verification/upload byte/lifetime/metadata oracles and all 16 current metadata preconditions remain strict.

The [I225 record](E-I225-transfer-failure-and-link-scope.md) and [I224 record](E-I224-upload-tree-evidence.md) retain their original unavailable Mac hosted-runner attempts separately from their identical-source qualifying repeats. Every earlier failure, timeout, metadata coverage gap and unavailable output keeps its original identity. Later green results do not qualify missing historical evidence.

## Remaining scope and invalidation

This does not close all of I06 or V02/V07/V08/V12/V23. Actual server drop/reconnect, permissions and cleanup failures, wider provider/open/admission/account semantics, native link/alias/atomic identity, same-size/reverted changes, check-to-delete intervals, blocking I/O/resources/reference, native desktop/human workflows and exact candidate remain. Owned-file fault evidence does not substitute for physical-source safety or native consent/handle qualification.

Affected rebuilt artifacts get new identities. No VM/Mac setup, physical source, persistent Git/SSH configuration, freeze, candidate or stable publication changed. The physical HOLD, human contract/custody decisions and explicit stable GO remain.

## Selected provenance

Private `FileCatReleaseEvidence/resume-lifetime226-v1`:

| File | SHA-256 |
|---|---|
| baseline-v1/command.json | 600960410a3a6e55df434d6d2485fcfdcbdce8622be1015469066b591b229aba |
| baseline-v1/source.zip | 4cc80c033038692ccb4facb98798ed286ac43b5c46092dd7952ea276420501d2 |
| baseline-v1/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| baseline-v1/core-stdout.txt | b43b51eba445de4f49514eae7e2c9b66bf3efd9764c7cd110b3d96460ca7ed46 |
| baseline-v2/command.json | 98c8ec3ab18e8f6966d3323882a917f97243736ae927eb148aa89004683122ee |
| baseline-v2/source.zip | d34dec85abf732c2062814da7e068ed266a1b4e5f9e8d3830ddf393440404904 |
| baseline-v2/core-stderr.txt | 8a383642f0ac13189b7d21004ba03d09436ce4383234a909a5a480f962ecd93e |
| baseline-v2/core-stdout.txt | ad3fd047f1511b249314ee0280d6d1a9e35b6f1fee9741b27ea12d9cff135d7e |
| baseline-v2/results/core.trx | d399f28acae47999e801e990618374000f52d824e5fddca425da01e8a5ce0150 |
| baseline-v3/command.json | a19b246f1397169cc037948ce04dc173bb156b70a65062b973bba47e4cd4bd19 |
| baseline-v3/source.zip | e35b695214d035bd8711d0c1eb1ee4804b01922028b1a5c27d92a508995c2c1a |
| baseline-v3/core-stderr.txt | 6eb741c9bec48244b27a956af95a764129471ba757cdb4cb5de7bd4a3765bf56 |
| baseline-v3/core-stdout.txt | 9246db1977cb28c800f392376a9a7e9c38aaab2ef2e07671f29915df7c32887a |
| baseline-v3/results/core.trx | caaabeca2ef4c0bccabc281b404252fd50354919846e6dbed5727089cc423fae |
| working-v4/command.json | b303cb6a38db4da3cf8046447ddd06a66db31999f56b594a84c2ac9de2f9f412 |
| working-v4/source.zip | 69775a97441ae850620c8428bd3da4b91603ad1610001f261aca648e1f1549e7 |
| working-v4/core-stderr.txt | 3500f99ecb89a07812e3b3e0c643f9e65a98103a79c20b27267d4c499339334e |
| working-v4/core-stdout.txt | ef028ac7d52a575d2325d9c29933a5228a62f7ff874ab308ec33db40b477c34a |
| working-v4/results/core.trx | 8eefa7916adb6a09c8f6a12aafda9c43035f9979c51b438362daf4b49ce4204f |
| baseline-v5/command.json | a50f9d0c4469d39a47c1ff8d144a8805a05ae0caccaf36665320366083ec89a7 |
| baseline-v5/source.zip | 6e157c8361a07aab69d531078bdd61e5e41033fbe5ebaf4bd042fb1908f3cf54 |
| baseline-v5/core-stderr.txt | 75de2c41d7894c4bb78b5f8e2c405a68d19c3c60619a375c9d23b4c0bc98ac37 |
| baseline-v5/core-stdout.txt | 0a3eb48bd35efd61ebca42dca8a52630a8bb01ec393f7cb686ae8345eca1b9bc |
| baseline-v5/results/core.trx | b2a4866b8969b37b391b40cb51a05800a572364da42fb1d80faf7c7134515b5d |
| working-v6/command.json | 2a73bf59f98bf66be5bd54bf8683d4d225147343984f3acf2d8a83b32f983d70 |
| working-v6/source.zip | c822eeef7c3df135dd1c3897bd9e9f2abb792f3fe5e40e11eb11a2e686477eb2 |
| working-v6/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/core-stdout.txt | eb04722a55ebae75663032d47216b0acb9d3b96d42c954a2fcbf303b8e441f06 |
| working-v6/results/core.trx | 0a055c296b026a7f01e29c2da4d0ee86add48bab457274c5fd2ef2ffd9338c4f |
| working-v7/command.json | 2c85f92bc680eba2bec273bc051361c2cd47c3dba22edf60ba732ec36ee4b91f |
| working-v7/source.zip | 257fc4f2edbcea55462515a76d697d5611034b43a0eefa49dff4fea18517c699 |
| working-v7/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v7/core-stdout.txt | 6535299b62852cdd084895b18be26ca455c0746a5bf90e42e016221f5e52b2e3 |
| working-v7/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v7/remote-stdout.txt | 02ed42542294fdb49e8339a5160ed14a367f1f3a4ca9b4c48d1ae770d7e2ca7a |
| working-v7/results/core.trx | 19777e95f5ab98b438e2c810faddda73531fd108a4c1304dea49248ec8776bf6 |
| working-v7/results/remote.trx | 55436b5a3714d6c126c991212a7af09e233468a6f225ebc9e06cf5cd11329190 |
| ResumeLifetimeEvidenceTests-v1.cs | b57ab5830f1d5e741449f8579fb35c0282672cd7ccf48f63763cc63a2c646fbc |
| ResumeLifetimeEvidenceTests-v2.cs | 7f8e3f3fa0c1197edfbf98f1a286a8882c086422380e23b5e4de36b7531f4309 |
| ResumeLifetimeEvidenceTests-v3.cs | fad9884fbe116cd8b5dd443a4eece510e8c3b5a0fe4ef8ba86f40104150e5fec |
| ResumeLifetimeEvidenceTests-v4.cs | d9170bbe7e64768b451c06ebdf41640d0576e1b556d1d066a1d634a239146847 |
| run-resume-lifetime-v1.py | f8eba2afa236da45aa1b470377260f0c4e96ac54efbf0fa769d51d30fd5a416f |
| run-resume-lifetime-v2.py | 412b12bc59a66bed3457a46c2938da46055325a9dd2e2dccbb5794eff873d596 |
| run-resume-lifetime-v3.py | b719d5f1a1e695d00596b01d6d876ec4b5bafda52d231abe9938828898f99e05 |
| run-resume-lifetime-v4.py | 47fc60aa4a8210d8e1632992ea8edc4d0055c79325c9e797b0ca2a80faebe44c |
| fixture-compile-correction-v1.json | bb96814e35d5c1aa21fbb2fb1351e3aade8d8c462bf2842c1edf00ff1d0dfebb |
| decision-fixture-correction-v1.json | f2edd1af866c1adf13c09be9f48b757083b3f86a3a8cb9fbfb7d41fac3f03daf |
| correct-private-fixture-v1.py | 5b27477f001d9fe8e4d88d582e93e997106e7ba3af86a1fd7f9a2d538204469c |
| correct-decision-fixture-v1.py | ce5a3188a74c319aa07cb5b08d9811cfe2f46cee1d6161029fc911156d393ca5 |
| prepare-expanded-candidate-v1.py | 840bd1671905c36a344834f842473228274112c6d6b8254d9b5c66f944ff1c6e |
| candidate-preparation-v1.json | 586ea4f1743796fa783f29882942a80a4a574f629fa20f10c821426091b12599 |
| preparation-v1.json | 47503729a5b6db41c7a8b23284a9afe6ab87304e3dec854a61379fe685dcb545 |
| StreamTransferExecutor-before-v1.cs | 6285ed839b1cf164c1ed0e9e0b03d46e84d87eb73d97fa34c833743d43d9792e |
| StreamTransferExecutor-candidate-v1.cs | 2e4798311d5913971cdef0c8fd8729adc6d122b3ea2d03baa0a51a82f4152ed6 |
| prepare-broad-v1.py | 1eefb92deaf43f7acb5756b82a8122167b18e63abdaf9c306ad460f64f03df78 |
| run-resume-broad-v1.py | 29b33c45e5f8bfbf5b93e8cb0f5dde1b8baffbbba71d284af6fa201144d3b9a7 |
| seal-resume-working-v1.py | c6c8ba5c78921be3f82cd731af68b8034cae80f9597a0c89b749d3221f496f3b |
| correct-working-reader-v1.py | 7513b9a7670093611f809574f81106f24da02df69b9e2056d47ded411795d83d |
| working-reader-refusal-v1.json | 0525cf1bb5a567e1d074b4960109fd133d86945778ad03e9779ef5efca896779 |
| seal-resume-working-v2.py | e0d58f7a33e91219f2cad67fed793deff34e92638a93a0ec13753d9bfd2c6217 |
| ../ResumeLifetimeEvidenceTests-preparation-v1.cs | 939be707b7c6d85108721c27385e7e614c30bba3cddf2e3ddead27493d64b54f |
| ../prepare-resume-lifetime226-v1.py | dc79169bf77aa67844937fa045170434e4db2054a5ab677c8d07f571ffd85e4f |
| clean-v8/command.json | 0fb929f6f14c49df4fbbabb1f2b3e0a8e281e0007b365a8a24b3d7206d04fe7a |
| clean-v8/source.zip | f67d676e3dc69934c97790d3877f3022a53098039476bfea91344914e0cf6fe8 |
| clean-v8/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/app-full-stdout.txt | e3ccc001e3207affcc861ff1328cbf97a8a7e752df7389e719cf2f91818c6fc9 |
| clean-v8/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/app-stdout.txt | 37775a15e2940e9fe3e7246774b28a9057f313506e64281bed945197cf3f9ac8 |
| clean-v8/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/core-stdout.txt | e978f43f6f66b76ac7099c7ba8e4a09f02ed6c3836306221304cffe233846a75 |
| clean-v8/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v8/remote-stdout.txt | e7c8cef271febf2a64a3467efb1428ce8ec034d928c3d714d0498502be34572c |
| clean-v8/results/app-full.trx | 3e6790beb1a5776e98a646643c9889abbc5548d8dd6b6f275007f9c41ddd9f37 |
| clean-v8/results/app.trx | 08aa86e0785b329918af31f70d6a0015a88ea35e6734d58a10c637a232c05ec5 |
| clean-v8/results/core.trx | 46bf64a9eba777f1ad670309b9efeadb78bf8e2b136a06fb99cc5b20e6099e06 |
| clean-v8/results/remote.trx | 2a77013374a77d57c8f726da06d4a0df76fa36b3553646881667ac4d60a239e2 |
| prepare-canonical-v1.py | 0930f20b7eb0d70df3c4e753a396fe3f79ee0b3addb83a04ba6df68e70a6a1ee |
| run-resume-canonical-v1.py | 383e221f40b52ca63ab27f743c4cc2914e11bad4b996c965f8aea4b519d57f96 |
| independent-resume-working-v2.json | 406bbe463900452248292cc7971e1de288f653681699d2b146be6c6152831e5d |
| seal-resume-clean-v1.py | 7543d70c6c538df1feea828d64ada143ede1422c2729432a50227499be2c3f6f |
| independent-resume-clean-v1.json | 7fee47deba404af633f4a3e17ae7dc8f0c5d4f50bc46ee9fade2df43433cb61d |
| native-parent-reader-v1.py | d9de3c123e03b2d1bc12565f53b8b4c06e74256385ce6d03a5ea285e80dd03bb |
| retained-transfer-native-reader-v1.py | dfb95154e4e5e51fc82cb38d79907bc6695d0c98d4bebfa31ae745048e935c53 |
| retained-copy-native-reader-v1.py | 7c42991da13a5c30d3be690762eb6983eef5901c5780947c4adc9a625e09a713 |
| retained-upload-native-reader-v1.py | fda49ce17a51aae723415f807210bdad04615809dece9cea9782450844b667e3 |
| retained-tree-native-reader-v1.py | ce86733adbd3c0f45871929a960a2ad993bb3e26845cc42e7ffea96708e2a450 |
| retained-failure-native-reader-v1.py | 9b1f118428d876bb2c936a8ad5101c06ee4f2bfb48ee70ec63f7d93ed5ac6f9a |
| prepare-native-readers-v1.py | c05a44c8d39203079b25802474b9d816c53916b31c4766403e37a0df12db958e |
| seal-resume-ci-v1.py | 9731630a09903e08dfdcffa733ed9f6790170e59f2b495f8c20d072ceac31956 |
| final-native-source-v1.json | 797ac9ff714b3cd5a9417cc167a09554a5216f735c6e17f6038f63e1d15b7883 |
| prepare-tracking-readers-v1.py | 0c6e92198257ef777848350f6015554246f812b2407640170f8fc919c6b7329c |
| document-baseline-v1.json | 9673fadd61536a05f72a7d8b9be311501d7dac96dcf9fd5c07e98da0bafc976d |
| document-transitions-v1.json | 8cb828327ae4a6a3cd402bba5786166305bcb01017fc48b3fea741218e7c324e |
| update-resume-documents-v1.py | e8fe33efcbfab8dc56be016a9f05414a49774ca006631df54e4e44633b2b89e9 |
| update-resume-documents-v2.py | fb8570bcc9ae5108e93c2aa3ff32fee797d760d4376c1f15e097bb69cb06e991 |
| prepare-document-correction-v1.py | fa343d70fb6e664ddc1262b77239680e637a182a094218a0cef81b07f424c243 |
| document-path-refusal-v1.json | 6751ea0dacca37c3464880978dce3dd5828d946619d6a27a575997e257493c99 |
| ../release-assets-20261006/resolved-audit-records-v146.py | 9cf543ba78375f19d90f023f350c5d3752ef6d8fd6eaa798524ecf33a3b68694 |

Private `FileCatReleaseEvidence/ci-37809727002-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | e969218a7eab267fa431cedc1d72de801b3139b9d05f057506553e916ba57a78 |
| independent-restore-ci-v1.json | acf53709cec17fe3ba50d462219d8ef4b7dd8ccb33078748c88fd7cb13e7e3e4 |
| independent-resume-ci-v1.json | df895c2e996ac1710986e564c382ef3a1d2937e151833f2fcf63b2dd0cb05e14 |
| run-native-stdout | 26e9f69a937ae3d634ba973c43488c10c48a7a7ce81d553cc5294f2c5f63069d |
| jobs-native-stdout | 1b48ce85a364712884948d04c15f065028e525d09d0e976fc8557600565344c0 |
| artifacts-stdout | 1340157a8e09a661b9783a996a88ad544e8e72c64e43a816cc6e88e5ed7455f7 |
| complete-run-log-archive-stdout | 9696d3d9fa2fccc39877997f61bfe8cc19343d9779b328be533dd8fc4aa69d68 |
