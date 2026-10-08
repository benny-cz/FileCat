# I213 — concurrent page reads share the actual source call

**Preliminary remediation qualified at 2516b14ebbd3b01eefec3e707cbbd671d2e67418.** All 23 additions pass: 17 Core controls and six forced competing-page viewer controls. The final focused working run passes 66 related controls, including all 23 additions, six existing viewer-copy cases and unchanged picture/header assertions. The exact Git-canonical expanded run passes 1605 cases (1032 full Core, 90 maintained Remote and 483 targeted App), with 67 explicit existing skips. Full App separately passes 1076 cases/23 explicit skips. Every previously qualified local name/outcome/skip is retained. These are owned-file component/headless controls; native desktop, physical hardware and candidate qualification remain.

| Boundary | Qualified behavior |
|---|---|
| Blocking and visible-page demand | Requests for the same page and cache generation share one active source read. The owner publishes one completion outside the cache lock. All four owner/peer combinations retain exact bytes, single source-call count and safe disposal. A visible-page waiter yields its device worker while retaining demand until the actual shared read returns; another page on that same device can finish while the owner is held. |
| Cancellation, close and source failure | A waiting cancelled read observes cancellation after the shared actual call returns. Close retires the cache immediately and retains the source until that call returns. IO failures propagate a shared unavailable page; recovery can start a fresh call. Source disposal never occurs inside the controlled active read. |
| Independent pages and revisions | A different page or refreshed generation can finish while the old page is held. Refresh does not make a new generation await stale work; this is not an atomic content snapshot. |
| Viewer copy | A visible-page worker deliberately competes with full/range clipboard copy. The held page is read once, clipboard bytes remain exact, replacement/close preserve current ownership and the source closes safely. |
| Existing viewer/editor/report searches | Replacement search chunks also need the held page. Their fixtures now release that call before awaiting replacement, assert its single actual read, and retain stale status/cursor/highlight suppression, exact content and safe disposal. All 19 existing lifetime variants retain their names and pass. |

## Original failure and retained controls

I212's original CI 37719895437 at 8696012 passes all 300 interrupted-source additions. Windows x64, ARM64 and macOS pass; Ubuntu fails one existing viewer-copy read-count check. Its exact clipboard hash/length, ownership and source safety pass; the count is two instead of one. That original observation did not record read offsets, so it does not independently identify its second call. The fresh controlled baseline records two actual 64 KiB reads at offset 131072 from the copy and visible-page threads, reproducing the duplicate-read mechanism.

Unchanged 1d1a9e3 with only the final control overlays passes six Core checks and fails eleven; App passes ten and fails two. Ten Core and two App failures observe actual duplicate page reads. The cancelled waiter already finishes through its duplicate read before cancellation; that separate timing failure is retained. Sixteen ordinary/other positive controls pass, including the six existing App cases. Earlier baseline-v1/v2 and working-v3 forms remain; final focused working-v5 passes all 28. Broad working-v6 passes Core/Remote but preserves the existing viewer/editor/report replacement-search oracle timeouts. Those oracles previously depended on a duplicate page call completing while the first was held. Working-v7 preserves the first fix’s one header-admission failure: a shared-page waiter occupies a device worker until another held call times out. Working-v8 reproduces that regression with the device queue; baseline-v9 preserves the legacy duplicate read. Working-v10 retains a lambda/discard compile error with no executed tests. The final async wait path yields the device worker while retaining demand until actual completion. Working-v11 passes all 29 coalescing/copy checks; related working-v12 passes 66 controls. Canonical clean-v13 independently qualifies the expanded/full suites and retains all prior outcomes. No runtime failure is relabelled as native UI or hardware evidence.

## Remaining scope

This changes page admission within one reader/generation. Blocking source calls retain their existing caller execution model; wider shared-device admission is still open. It does not provide native UI frames, atomic source/content identity, same-size/time content detection, recursive directory identity, a physical-device safety conclusion or final-candidate qualification. The physical-source HOLD and explicit human GO requirement remain. No VM/Mac setup was changed.

## Provenance

Private `FileCatReleaseEvidence/pc213-v1`:

| File | SHA-256 |
|---|---|
| baseline-v1/command.json | 1fd3410853fe2f9558109af99226c2f3de244b632f82f03cd9efbe9e04a34f2f |
| baseline-v1/source.zip | 6ec9c79c40593f5cc2dc53324f786f37e726728c918848d374ebadb0687ba087 |
| baseline-v1/app-stderr.txt | 4d3f60b4f5451e084f15c2cfacf8b83097e36663f341b8ac3c17c8e11cadde41 |
| baseline-v1/app-stdout.txt | 59787c83fc46e41624d49c8053eed66504dc182fa911498931e613f422b09e8e |
| baseline-v1/results/app.trx | d96810c68489260e4225d1be381bd89799242aa18a7ce28f1a23d299d9106440 |
| baseline-v2/command.json | 7905d2551c37c65d69f7ba7b35b313ac3ccd22f4c2b6dcd84f3d9715bcf75468 |
| baseline-v2/source.zip | f1195cff4edf59cac01b75f0735d4ddcba90ebe6dbd0c29c3870a0960e4ae6fd |
| baseline-v2/app-stderr.txt | e74b29295fff9b4242218fe4df1735132b7a9c8d609d8430ccb28f9fec498b0f |
| baseline-v2/app-stdout.txt | 6bd03a5a52ea732c38a8b7f99bd0ca18129ec092dc24e7823405508ff28b50fe |
| baseline-v2/core-stderr.txt | 95ad823324a5c0cf0ae6b6ab0786b10665a6ea19e68f20208afe58ef589746ac |
| baseline-v2/core-stdout.txt | f845b033160bc21dd79eac0544d0a5e960c265b3d6657c74d16146bc3077374e |
| baseline-v2/results/app.trx | 4a4d0eafa7cd46f0ec51b4b8ea37b9b2307b8ab907cb81ae342823165b3fe28b |
| baseline-v2/results/core.trx | 7a5ce931e302387ae0582418d86adf7d2371d3006d23c8d40be28fe2c83b4067 |
| working-v3/command.json | 864abaa3a53a8f0edf4f2c02486fe1e9df3587e88ec67d358a7e1568b3fa04ce |
| working-v3/source.zip | 80d7a14f1925df450ad7fc1f3e50d05143a2402d7678c06b17ea3058123cdc66 |
| working-v3/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v3/app-stdout.txt | c0c2c7d61bc2c25dc04ceb8167529a1d98d13f5a592fa8173882ae803141a473 |
| working-v3/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v3/core-stdout.txt | 1e5a893417953a38ae10847c4497d80b76c61447a5ce16a40e6f28393d10fd62 |
| working-v3/results/app.trx | b29c25764f1ba2c64f4a9b654d0956901f073f5cdfdc654d536910035ae030ec |
| working-v3/results/core.trx | d70b7c29ea77da3ee34c665c9e147a1c9069159c65ba3351c7a952a1d1cb3808 |
| baseline-v4/command.json | f78e23285159b3a0cec143b67d74918f60f97eb740fa564795cc9c838bb955b8 |
| baseline-v4/source.zip | ccc32889eb1c5b5879509c1e4e276a29acc9f747bec62235790804f93b2bfc66 |
| baseline-v4/app-stderr.txt | dddb4c18c184eeaec74ebe3cf760bb21d27141ab889ca097287f22d3d69d8365 |
| baseline-v4/app-stdout.txt | 254a33dce16e39f1e7593a45abaa0d045bf7b3d85c31f22892890747011cd8f2 |
| baseline-v4/core-stderr.txt | 1db19f42305c32cb4e8d0496a2b2cdb2c08d40fd82b087428e4cd208ab4b9482 |
| baseline-v4/core-stdout.txt | d198bed27f0968e21eba5cd76f9acdc24de9ce2661e550e8f096b85f47e18a1e |
| baseline-v4/results/app.trx | 7daa02f961dce91450acb8cffef788e7859d3996ecced3fb89f57bfd7c7c4f17 |
| baseline-v4/results/core.trx | 58cba1a64e7b2f4b31754b12064583243cecb1f112a14930f298b525e7046fb1 |
| working-v5/command.json | 5e979880545ab694b1c5522cad7f4a0236ddb7e4eebc00e8d0d381530493ccf7 |
| working-v5/source.zip | 25f3ac448a2812d6d5e61f0bb254f417062f3fb63e4b3d30e96ac3519bec7f7e |
| working-v5/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v5/app-stdout.txt | 9901bf0c7496f998fb2b2c1c4e2ddba232dd1a901d81674a64fe0721db8502fe |
| working-v5/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v5/core-stdout.txt | 3a2acd4f7b0c600cde5049d93b6920fec40d39235a5160d21e15ca23451ed4c3 |
| working-v5/results/app.trx | efe0046216a2689fde352b3214fde4ba956e643a0661ac7bec15d1b8bfbf5dad |
| working-v5/results/core.trx | 8d6da85e8400f20d915047d677da72d8fd6a7ad5d8c8c5610afa35e1a096b589 |
| working-v6/command.json | 42801b8a182c3de5ff2800ed897e06e2342c5f47a96458d6bd3a0175636f4e63 |
| working-v6/source.zip | 99562e2c2608f1a884350615e1b19ed9167c072d6cad66c68ec295eea473bbce |
| working-v6/app-full-stderr.txt | edcec567936d9976cb69ab6a9b68e8bca1a5ea643c1718e1886c62c63622a507 |
| working-v6/app-full-stdout.txt | 292a6a47f4623810874d3bdd3a61c97b06c988c4c623b1ab46b0ca71d2d6692d |
| working-v6/app-stderr.txt | b0f2fb28e5633c9f7c3a5a1a6627264824ab593b162db3aba8b98e2d4bef6e81 |
| working-v6/app-stdout.txt | d5869b646b5610d13a2db318ffa66e33b2a27fb62955de975aa5e939ec7f418a |
| working-v6/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/core-stdout.txt | c0d9e2a96c8e2f9f0a50ce10a1136c85ecc02eaf489f800616ae35afe2b48dae |
| working-v6/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/remote-stdout.txt | 560ca5459a162479ed0d9fc4685397c04b836f6216cbdda88dc9c1d1ae337673 |
| working-v6/results/app-full.trx | 7217d51bbe87428d1b4c313faba475724c4a4789d623205fafed3bc44aa4c785 |
| working-v6/results/app.trx | 2525341d4ad879959103d2440bacad2bb4978425bf653e117a60a6aa9a3317de |
| working-v6/results/core.trx | a75fb005c090f687a518d78ee7336bf37a0e7110a6e6a79fcf2152201a9a49ab |
| working-v6/results/remote.trx | 373e7e9d4a5a5d2db13c97e293b711cfc72c638bc9defc20aed27c34c8c3dde0 |
| working-v7/command.json | ad5513f7d202c13a2a39af964b6a3fa2a2e228d18485cf4fe9fe6c016ef2e7ba |
| working-v7/source.zip | 1632fa360dcab89c53b8ce97d28ce8e3d500fb71029a594ebcbab0e7079852e8 |
| working-v7/app-full-stderr.txt | 4bf1e709baccc490e9eedc3457484ad561f1d4a7aee1af8a9a54b22cb0cda504 |
| working-v7/app-full-stdout.txt | cfadae39e213cf863ce6d92f7ccf4edd5bcc5f2fa3ccb7722cc344d9d67f2bf5 |
| working-v7/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v7/app-stdout.txt | 49d7fa801f76a2c9f54a29f21123d16a0eca74432642b3b846aae633c15828bb |
| working-v7/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v7/core-stdout.txt | 9997599119c088ff2961374cd7f4d569e7305f124c987900579d3a915d9c77f4 |
| working-v7/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v7/remote-stdout.txt | cd4b23c93c6fe812cb5e32f250b354b03db77cc1a4e099a76b3588d2218fc57b |
| working-v7/results/app-full.trx | d4c310ca98e906af022e30690f6c062314ff5e1da9da1e97750257e230eae534 |
| working-v7/results/app.trx | ce9e194a27971531ae3513f93756bf62de7e5e911dab496734cbbe3b4f1b6112 |
| working-v7/results/core.trx | a3f6b4d930cc5fb165c58db2b509aecc71c0e8c96fd7799f6ea8edb16d0319b7 |
| working-v7/results/remote.trx | 5fde7af8c4e37f7237ee8cc0abe93790daa2539c18c7714085a5e6ca1313e727 |
| working-v8/command.json | c1b03b386126236031beb8eb322ff3f048b3eafdab69bd78c0ea65c37217a5c3 |
| working-v8/source.zip | 98ec273550fcb377b271bd67dbe5cca69f7802d014b5dfa9b118f224200d1f95 |
| working-v8/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v8/app-stdout.txt | f97ce6c28d850be827064ae18c788ae18bf89a2676c5810e47a1c8d136555313 |
| working-v8/core-stderr.txt | caad397cb2bc8e3c7d0abdc259b17506900f7784d1f94553067d8011d852c920 |
| working-v8/core-stdout.txt | 76ab19207e53422f30e7fb6b9ba1613f0a56fee2918d643d4bff518a3684ff9a |
| working-v8/results/app.trx | 55b5d508e1e7d524612a33ace2bc9769c52c852c04c43be5bec2b36630d086c9 |
| working-v8/results/core.trx | a98b3c95ce666812a48b644db70428528c20a7deb0e39048a6f7b413d21827d2 |
| baseline-v9/command.json | 8b94fe6fbdb7eb7d0c3cd8d8ca70c22cb4009de7ec67a7aefd26ec5dd4260f2e |
| baseline-v9/source.zip | f1f92c5f18c95bdb1e34688a1a26bbaba0a8c5f22bbb72ac23920478a32192f4 |
| baseline-v9/app-stderr.txt | c938d4d12943c7b36d152f5949d65d4ad7eb9402e55a0ed977b919c9ddc9d048 |
| baseline-v9/app-stdout.txt | 82b6664c6b09f11b78eb774746b61b244870eff35f31fc2afaa3d255d8e5200b |
| baseline-v9/core-stderr.txt | e2d5e1cf11c79fb618478b6b20930f10541c46112a9b906dc717993e1ed86f0f |
| baseline-v9/core-stdout.txt | d8c9c93c03fd46ecc9d2d2d6460272d0f2408f9190250303e0b42bee792f071f |
| baseline-v9/results/app.trx | 7501e30a1956823b6c170e9fa0f5b87c64def617f6d7bdc340b206da6a88931e |
| baseline-v9/results/core.trx | 294f9e1054f87bbb8373733624856fbd524eeacd106b9d5884aa92a3b2e1f499 |
| working-v10/command.json | 599070dbcb0e5fccb6ddefe054d21874d88fd54523acac09b11f88e98a59a7c3 |
| working-v10/source.zip | 81fa8f078a7a63e04223f52c5ecb6328b5fcf06d1606574942a0538a94d843bd |
| working-v10/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v10/app-stdout.txt | facc185ae93305325ff1e8ef8afb2288078951bcecbe9ee823917a39d224dd39 |
| working-v10/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v10/core-stdout.txt | 12a2b713cf3ce70e05726238ad2e1903488498dda84da11879ca2d9137dd9e13 |
| working-v11/command.json | 6e71769e0fe70f1dfc0cd028af47eef0613da331cbc7aff83fa68566e89c0c64 |
| working-v11/source.zip | 065c8e82e366e30f14292ea32c26703fe8a75cc00b83d8a2f16ca14559398338 |
| working-v11/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v11/app-stdout.txt | dc0c4f8c3e96636e2ef35c6cadde2a22dd032094f25c90891e1a064b003387cf |
| working-v11/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v11/core-stdout.txt | 3d3684073b6ec1a9dddb6fe96025c1e3f6404c812ef72eb6c23000f2a8473e7f |
| working-v11/results/app.trx | 0f8a4905ebc664015260918f5a8006860746442c7643393b289ce23031231076 |
| working-v11/results/core.trx | c28bec1e2ae743ff26cc09adacacdf70338397478479b794ee24d313a655e4d7 |
| working-v12/command.json | 03df421e49c43c5c160f93040d2ab20e68aed49678c439c92daf05fed188a4db |
| working-v12/source.zip | 123ed2cc5a90792fdafc6f80ff29b2dd74258e2e2e1e6dd08ae24d57efe944fd |
| working-v12/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v12/app-stdout.txt | 22ab5edf9e95c70c85da5af82e8c84f10ff033bc2e205a29c1ac8601595f1a02 |
| working-v12/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v12/core-stdout.txt | f7c36eb01bf278b5bb27fa58d5074ca2afefe21360bd098792da7ce6728eddba |
| working-v12/results/app.trx | ba93274e8750460d84a3105f453e758dfc2fb8fa54cb58c842596b244db50810 |
| working-v12/results/core.trx | f6cfcb39f554d2ad52c0ab9784cbfee452399271cac3754e2a609205fd1e81a6 |
| clean-v13/command.json | 7c911c0126044c1aa45beed6c0426cc17795f7a31c6017378b77fc8afd1df01f |
| clean-v13/source.zip | 43b3b0e189986cc06bcf1ed037328882ea44e4b4fd99be60da9b5685892c1f8b |
| clean-v13/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v13/app-full-stdout.txt | 278b9c14ac5fb5d3451b0aa7addae4fbc60e6868d3972a70987ef27d94451577 |
| clean-v13/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v13/app-stdout.txt | 986906492f869e152ff2cc760a5450c4543be4849185f91c3fbacb9f27597b8d |
| clean-v13/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v13/core-stdout.txt | 67111bb9adf077becfc0eee9c8cf29d2616f7f4b185ca33ce66b16b1170bb75d |
| clean-v13/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v13/remote-stdout.txt | 8207d4b5809116a55596a47ab461cb9035e2476bdf9b1d3ff614991f9989e67b |
| clean-v13/results/app-full.trx | a191d3780fe605772100252664b240e2fc16799b645278c4352837411aa06a4d |
| clean-v13/results/app.trx | e2fa7cc7924c2556de2a5aaaf327e785a5ebeb8856603980a7f9ae2ee95f2c12 |
| clean-v13/results/core.trx | bdc920ccbe2700e4d26160aac4f2e571b94ff3d562967c3236d443b49ab9e783 |
| clean-v13/results/remote.trx | 6a230782795572bf5da74f924a93851160daa365840b235fecbde2fd11a39168 |
| run-page-coalescing-v1.py | 90d12c94c18f84ccaacf37b2646572ca6d7cf092d65b5b46b01f773118e8cdf7 |
| run-page-coalescing-v2.py | 5ef40aff57d81d04387e7b99b2aef9610a210d9d9d0e5bcda08847e572690c6c |
| run-page-coalescing-v3.py | 27b48d4a5175fb620f4dfbdad8712326e087c0eff1e0e9227838c18a70c29119 |
| run-page-coalescing-v4.py | 95824577ca6f192338f2452d488c1afdd7c149a0641c0910159d93dcc560b133 |
| run-page-coalescing-v5.py | e13f7c43a94774faa10f6e19c4e94213c8e96522e903fc10d348c6d3f4396215 |
| run-page-coalescing-v6.py | 3ab358cae7f1ae1c5a84479c2befe4c9c79d4980531cb5da4a018140a980a8f4 |
| seal-page-coalescing-v1.py | 52fb6547445b1cc1c119af99de8abb2fa6503127e46a2f6c51f8980b5c375084 |
| prepare-qualification-v1.py | ebd1b2c0c822be6cbba110b3a49ff7fd6761618ba54b3034473816054cf5b11c |
| prepare-final-focused-v1.py | dae611ce1c87f0992693b8b0e8e8acc0e5478d8d29361a744808289308e02e37 |
| independent-page-coalescing-clean-v1.json | ff356447b9304273ec4275a0ad00f1de7d9c8825b2e20c1211b252ae878b4f29 |
| update-documents-v1.py | 113233f946f5210864c0e4cb41fb332602271cee2750632e9811ff0a84510782 |
| document-transitions-v1.json | 2c9f8a297f96f5e77216104d3bf36dd0b52f399ef62e438e8e7918d8aa6632f5 |

## Original four-platform follow-up — 2516b14

CI 37725195680 attempt 1 passes Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26. All 23 additions pass on every lane (92 executions), with independently reconstructed source-call, byte, lifetime and device-yield observations. The original existing range-copy case now passes on all four lanes with one read at offset 131072, exact 8192-character clipboard hash, current-demand ownership and safe source lifetime. The I212 Ubuntu Failed→Passed transition is the only changed predecessor outcome; every other name/outcome/skip and the original failed attempt remain preserved. All 300 I212 additions, 188 I211, 180 I210, 212 I209 and 180 I208 executions remain passing.

Independent readers verify 20 server artifact digests/every ZIP member, 14 full raw TRX inventories, four exact build receipts and 92 actual locked dependency graphs, plus nine owned mirror/five launcher controls. Packages/draft publication are skipped on this main push. This resolves the pending component CI record; native desktop, broader admission, physical/hardware and candidate scope remain.

Private `FileCatReleaseEvidence/ci-37725195680-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | faf4a7b9560801e5b94bc504cd927f39b356c147c0c5a6ead756765683446cb3 |
| independent-restore-ci-v1.json | 353e96bf12a5361f7b05224fce8aca7ed4e65e7646a38657c02f958b58ef246b |
| independent-page-ci-audit-v1.json | 823c2c1b2995fa4be8f99b7d858cb0dc7949b24892a4280dcaacec42724a918b |
| run-native-stdout | ba6b3d68db15ca3f9021101c472e429f763bbee1f2586023585f212fff44033c |
| jobs-native-stdout | 0074a2cf58f7e7484a54d3eb0090d88f3f52584fdc13aa461d6693bcaaad2104 |
| artifacts-stdout | ef8afbe60230696b5884e8a0306655aa741d09a19fd2f0f5767dc40759bd4bb0 |

Private `FileCatReleaseEvidence/pc213-v1`:

| File | SHA-256 |
|---|---|
| prepare-green-ci-v1.py | debf9a987d38a1cff1d7c58990fa1df711349f401e94421c1743619414c7d3c0 |
| collect-page-ci-v1.py | a9fa3e4ec5a23464bc6b41702560d5b48d62ddbb0c62b5901ed9e755259544e5 |
| prepare-green-reader-v1.py | 85724d0f8915bdfd886eeb8c378ab23e1897e4b91ffa65269bc650b8d3b713e3 |
| seal-page-ci-v1.py | affeeb24353acadfa277448637a7e5e8d807c7d99d06dea3099f34f929901a86 |
| query-product-ci-v3.py | b7bed3e16f8d26941339decae766150cac718ef3bc269b4ba2ce31f1702129de |
| product-ci-query-v3/observation.json | 68924dc7987b9d2169f499bbb9f5d8644bb380cbb065263b27d9146edbde90ad |
| product-ci-query-v3/runs-stdout | dd18d7542e2fdddca61412c7d66ef11d2dc8824cdf4e204ed64ff034460c70d1 |
| product-ci-query-v3/jobs-37725195680-stdout | 0074a2cf58f7e7484a54d3eb0090d88f3f52584fdc13aa461d6693bcaaad2104 |
| update-green-documents-v1.py | 1fc728c47b3ea7daed3b2b59939bafc1dad6d8536152c947cb07ba32354d396b |
| green-document-transitions-v1.json | 9c36f5d33a821c0fef70b2f27287f181ae9bb664e80a210e8a3c87d4a073ecce |
