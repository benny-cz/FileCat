# E-I187 — conflict comparison completion belongs to its live dialog

2026-10-07. Medium worker/completion defect under I06/V04/V12/V13; remediated preliminarily at d04a5d7f4cecea52832156133d790f9f24df919d. Final candidate and broader lifetime/revision qualification remain open.

At original b0bb08c, pressing Compare content then Cancel, Skip or Replace within the same UI turn closes the actual conflict dialog. Its asynchronous worker later writes the identical-content result to the detached controls and reenables the button. All three adverse controls fail; four ordinary identical-content, same-size-different-content, different-size and missing-item controls pass. The owned 4 MiB materialized files and actual PortableFileOperations SHA-256 are used. A synchronization-context delegate records completion of the actual async-void click handler, leaving its worker, dialog and hashing intact. This demonstrates obsolete completion; it does not measure read cancellation latency or a held native syscall.

Each comparison now owns cancellation linked to the job. Ending the conflict dialog cancels outstanding comparison work; queued work and hashing receive the token, and cancellation is checked before hashing, between files and before return. Completion and button updates require a live dialog. The click worker clears and disposes its cancellation source after completion; closing the dialog does not dispose it underneath a synchronous read. Concurrent duplicate clicks are refused while comparison is active. Decision actions and existing content/error strings remain.

Working and fresh locked committed Windows builds pass all 28 affected cases without skips: the exact 21 prior checksum/overlay/escape/Find-comparison names and outcomes plus seven new controls. Each adverse case leaves closed controls at Comparing…/disabled after the actual click completes, with the correct CancelJob/Skip/Replace action. Ordinary results retain identical/different/size/error behavior and a usable compare button. Independent Python SHA-256 values agree, all existing owned bytes are unchanged, the missing item stays absent, and owned fixtures clean up. No test, product or controller preflight failure occurs.

Independent seal SHA-256 35ff3c8c4b77b0554f815ed0db255887420c5eeb04cdfced2e25916498e1ca68 verifies all 20 retained files, 423 actual payload references, the 1,095 original raw Git blobs/modes/archive members, and all 1,098 clean committed blobs/modes/archive members. Clean FileCat.dll SHA-256 856892c962a111ca28e88fabd87e9fa0a0d086a600582c57a6bc54f337962224. Only the dialog lifetime fix and its seven controls change in correction d04a5d7 relative to parent 01690b4b608abd0885d5da2ea7ddfced8ede8b55. The earlier baseline keeps its own source identity.

Original push CI [37586182003](https://github.com/benny-cz/FileCat/actions/runs/37586182003), attempt 1 at d04a5d7 is sealed green on policy/all four required lanes. Each full 559-case App inventory equals the prior 552 names plus exactly seven additions; all 28 distinct new executions pass without skips and retain actual decisions, current/closed state, unchanged owned bytes and exact independently checked hashes. Core retains 890 Windows/885 Unix names and all prior finite subsets retain names/outcomes/skips. Nineteen selected server digests/every member, fourteen raw inventories, four compiler/tool receipts and 92 locked graphs reconcile. ARM64 package version-start/headless drawing and installer compilation pass; native installed-candidate qualification remains open. A first bounded read-only status request times out; its raw files/pins remain and fresh status v2 succeeds without a CI rerun. These finite headless dialog/owned-file controls do not qualify native GUI, held synchronous I/O throughput/latency, shutdown/job cancellation races, source revisions, human UX, reference performance, physical sources or installed candidate behavior.

Private `FileCatReleaseEvidence/cc187-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | bd445c5ab8be97010bf0bba86bfa18c3344c807fada74057e4a169c0c8154fa5 |
| baseline-v1/results/baseline.trx | 73e871347d9e2df6860b4b6fb86ace677183999ec4e9f40ca1db1bb07b1750ac |
| working-v2/command.json | 1577d546cfe8ee9218527ca3b2e3c7fc3b32832ec8215f8ffa1767d9eb8c81d0 |
| working-v2/results/baseline.trx | 3f53543edec79c297197c862e0f7297086edf17895be830741dc8e49f339e791 |
| independent-working-v3.json | 642f08cb45b9c02c4b31d89882009391120baa382fc462cd6345f8ee8438b209 |
| clean-v4/command.json | 5d059cae699d08beb1b82e386577ffbaf02a597bebe85f40b576ca4cd5c99146 |
| clean-v4/results/clean.trx | 2f3b07622f509a3e1e6e4d7de25889669f856c036d1747b6973348b015c8f305 |
| independent-conflict-v5.json | 35ff3c8c4b77b0554f815ed0db255887420c5eeb04cdfced2e25916498e1ca68 |

The collector writes thirty successful original-run proofs, then its final new size-output assertion assumes Czech nonbreaking-space grouping. All four actual passed size cases use comma grouping, as the product formats sizes with CurrentCulture. The failed collector source, exact assertion, four raw observations and all thirty prior pins are retained in collector-case-format-failure-v1.json. Fresh v2 checks the complete saved raw inventories and the exact observed CI grouping/byte counts; no test, artifact request, successful collector stage or CI run is repeated. The final new case proof is 9cedea515aa92ead876fa7fc1166d7757b2fd8e0f02349f9debbfc3eecabfff7.

Private `FileCatReleaseEvidence/ci-37586182003-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | c38d4f2b70d54cf3018bc2aba86daa395324938f517de374af27b418b372f44b |
| independent-draft-guard-ci-v1.json | eef0e650be7ecaa9bb1cf0e8fdd9310ced5547dd8b3aeb15384683f1d91e5c08 |
| independent-fixture-ci-v1.json | 2dcaeda5b90aa86170ef81f0f165c418ea605e6673aa60d79214988b89fd4627 |
| independent-i163-ci-cases-v1.json | 75169a114e3a8f29cf79214070630feab7dc9b4e0193e28ae3f420b66b8aab92 |
| independent-i164-ci-cases-v1.json | 6f2b9b4123e9c2b79097b56240e6c5872c141884d52cd07d36d634b716c476c8 |
| independent-i165-ci-cases-v1.json | 7f22edf64dca8e15e561910a93487dcda02f0df73c3879a5d923dadefae1d1d7 |
| independent-i166-ci-cases-v1.json | 69c74901dd9c0395d08afc800164aa57b8f25fe31e61adde1d5b2335d09800c8 |
| independent-i167-ci-cases-v1.json | 2b57837d7e12d971ed44fc1dc3d5c2620e000ad597396eacba4884a2ad809767 |
| independent-i168-ci-cases-v1.json | d2d2686e4c66bcbc070fa348bc96aaf97fea39686967123dab06dc2d1fb12cbd |
| independent-i169-ci-cases-v1.json | 0a211638e59c53da07052a490a969270209d3fa4fae83f212d4345c348496f9f |
| independent-i170-ci-cases-v1.json | 863ff90592a4dcf9ab849a59796694faed0dc6fd79537d62364357e52b90fa91 |
| independent-i171-ci-cases-v1.json | 499d8bbf269db2a97cd759287b0806582eb9e2aad32bebe7c4d603c10e87d68d |
| independent-i172-ci-cases-v1.json | 114b71cf94e93a1d7266223c39debaf9a182551e6e69c900268739ea865f3d7f |
| independent-i173-ci-cases-v1.json | 3b9b140f06a606aae3fa3faf9a7ab9337e403a90c4189c7fca4f79e4444c376b |
| independent-i174-ci-cases-v1.json | 3443c2bec13d4689e1ff542b86462770a93bc9544f5392a32dd7653484cab9fe |
| independent-i175-ci-cases-v1.json | 61188efbd76f4aa11e95ca4e2a02a0786141178a638f0fc1ff2605317f1e447f |
| independent-i176-ci-cases-v1.json | c8dd5a4723c1875cd8da353d5f4f21dce4dbfe2fe82430c602f6e7c108b4ab8d |
| independent-i177-ci-cases-v1.json | 66b0f833868c95124dc4ef790b7905317ea5195aa1812488718598ebce6131ee |
| independent-i178-ci-cases-v1.json | e62e49083f5436f3c01436f180a0fa4d433c24d99f09d6aa9dba1f6599a0984a |
| independent-i179-ci-cases-v1.json | dab58402c7d931a70f616272435ed8113fd6ee419c929eb36064ffbd138608ff |
| independent-i180-ci-cases-v1.json | 0e41c15a7c2ccce12d556db12ea9b0a33bbdd170f8920b4ed2952ab1d52b5f9c |
| independent-i181-ci-cases-v1.json | 7193ecfcb682fc630c33c5265d51468cde57908072f44fb58b823d16d1dc24e4 |
| independent-i182-ci-cases-v1.json | 6f2f7204471e9418129d17cc9f570ebd8c53225146641c78d0ec7f6e0036dbd3 |
| independent-i183-ci-cases-v1.json | cb64b945a9be932a3672d1e0d93d9738ddeb0d5128645070378d7be9aa8507c3 |
| independent-i184-ci-cases-v1.json | 5340307d0ee467d7d209881d2f791325af9a971b50d5772de53a9ce672755441 |
| independent-i185-ci-cases-v1.json | 50b3393f40c2b8aaab024ff26d7d745f09b022fe3170bd889bd97159be5ed87d |
| independent-i186-ci-cases-v1.json | 5e3d9e5e0eaf7f9108922ddf20a6fe92d27616e6089a2f1a143f8394c162784c |
| independent-i187-ci-cases-v1.json | 9cedea515aa92ead876fa7fc1166d7757b2fd8e0f02349f9debbfc3eecabfff7 |
| independent-producer-policy-ci-v1.json | a1a91f997e1cf14eab0261a58ec72c3bff9262f60dae6eea766aee5bbe39cc3e |
| independent-restore-ci-v1.json | 1a13ae96eecca407e158358d4f50075a422cf4d7757cbbe325d767e3469d39c7 |
| independent-separation-ci-v1.json | 46ace40a2f496301995318e178a25631241f7553c593f7536cb7d96527f7dc6f |
| collector-case-format-failure-v1.json | da0edebab3f1817847f33339b1b440b005e24e0c6d7903cf7e74e29f16cbeeac |
