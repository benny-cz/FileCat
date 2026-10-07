# E-I186 — lifetime read-count controls establish their cache precondition

2026-10-07. Medium CI-fixture defect under I06/I12/V10/V11/V12; remediated preliminarily at b0bb08cbdadbf9aa0f1cc3f7c5c293ea2996ebd6. The change touches three test files and leaves product code unchanged. Final candidate qualification remains open.

Original CI 37580524539 at 9230f10 fails one macOS ViewerChecksumLifetimeTests ordinary whole-file case: expected three source calls, actual four, while full SHA-256/CRC-32, unchanged owned bytes, zero active reads, no disposal during a read and disposed/cleared cancellation ownership are correct. The test assumes a cached first page but only waits for PendingLoads to be zero; no request is guaranteed by that wait. Four calls are consistent with a cold four-page reader. Every published artifact and the raw failure remain in [I184](E-I184-hex-editor-copy-ownership.md).

Viewer checksum, viewer copy and hex-editor copy controls now read and verify the first 32 owned bytes explicitly, wait for pending loads, assert that page zero is cached, then arm the held source call. The existing three/one ordinary-call counts and exact no-later-read cancellation assertions remain intact. Each raw observation records FirstPageCachedBeforeArm. Clipboard content, exact hashes, current status, disposal and cleanup assertions remain.

All eighteen working Windows controls and the same eighteen on the ordinary-user ARM64 Mac pass without skips. Mac 27.0.1/.NET 10.0.12/UID 501 runs the identical 141-file portable payload via its standalone xUnit runner; no SDK, native GUI or authorization dialog is needed. The payload archive and all before/after bytes verify. The first controller preparer fails locally because a broad ROOT placeholder also changed DOTNET_ROOT; no controller or Mac test ran. A fresh controller succeeds. The first independent reader expects VSTest StdOut; the actual standalone TRX records JSON in TextMessages/Message and escapes display-name quotes. The failed reader/source/receipt stay intact; fresh v4 reads actual fields and preserves raw attributes. No test rerun conceals those preflight failures.

Fresh locked committed Windows build at b0bb08c passes all eighteen without skips. Independent seal SHA-256 4bac7957d2963a2b9569ccafb20131568efc75ce6d0c28b8a62d426b62646e81 verifies 32 retained files, all 1,095 original and clean raw blobs/modes/archive members, 564 Windows/Mac/clean payload references and every complete-case observation. Clean FileCat.dll SHA-256 d0fb63dc5b58975c37a14bdfbc6e4a4ffa35f2ae23ca4ee7eaa8cab042701ce8. The comparison verifies that only the three test files changed.

The owned ten-minute Mac awake helper stops and its process is confirmed absent. All nine result files are hash-compared before the owned stage is removed; restoration SHA-256 e8beb380f8e7d0c549a786e00f0435bd54f5d0de4063de3a4eaf84af59321588. No persistent Mac setting changes, VM changes or physical-source access occur.

Original push CI [37582929698](https://github.com/benny-cz/FileCat/actions/runs/37582929698), attempt 1 at b0bb08c is sealed green on policy and all four required lanes. Each full 552-case App inventory has unchanged names, outcomes and skips; all eighteen updated cases pass on every lane (72 distinct executions), each recording the verified first-page cache precondition, exact hashes, current lifetime and unchanged owned bytes. Core retains 890 Windows/885 Unix names and all earlier finite subsets retain names/outcomes/skips. Nineteen selected server digests/every member, fourteen raw inventories, four compiler/tool receipts and 92 locked dependency graphs reconcile. ARM64 package version-start/headless drawing and installer compilation pass; these do not provide native installed-candidate qualification. These finite file/component/headless controls do not qualify native desktop/clipboard/shutdown, human UX, reference performance, physical sources or installed candidate behavior.

Private `FileCatReleaseEvidence/wc186-v1`:

| Retained path | SHA-256 |
|---|---|
| working-v2/command.json | 4f9580528c1825acf08e13e360396639bac3d92eb06e272e08274aadb29852ad |
| working-v2/results/baseline.trx | 46d05f1c852dd9add12536d207d9618da77af9f405533a7d4d3f23e46ca56b70 |
| independent-working-v4.json | a6a1fd2d466e76754a8f2c01e70076dcb309ee958abaa82fc761b8130ee87c4b |
| mac-v3/mac-command-v3.json | 7184b78de2b6ac3ea35458ba632896a0d34c01c803ffbeb9a555534456680094 |
| mac-v3/mac-results.trx | bf96744e7d99dac34f81ea8c18552fd22c3964244515ac87ee90b1e1d43b3f15 |
| mac-payload-v1.json | 7d1cc9f8a120c01936181cd4a90a29bbc3f98e72e9b90b68ff72578c8cf56d43 |
| clean-v5/command.json | 2fda035b5aa5183fdf577a8af4c0dbce01563d9ea38ae9e76793a66410600098 |
| clean-v5/results/clean.trx | 8e8d7900b116d4b4ea0014846225093e67085f2888deac5fde2970303700f67b |
| independent-warm-v6.json | 4bac7957d2963a2b9569ccafb20131568efc75ce6d0c28b8a62d426b62646e81 |
| mac-restoration-v7.json | e8beb380f8e7d0c549a786e00f0435bd54f5d0de4063de3a4eaf84af59321588 |

Private `FileCatReleaseEvidence/ci-37582929698-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 89a3737e38eac315c32933c0a70f10c23edc8bc8f26aba76e6302146ba04ee8c |
| independent-draft-guard-ci-v1.json | e02ce2f44073b06a9df4fe2869cb82c261bd71a850055f7d336ea2dd729f6342 |
| independent-fixture-ci-v1.json | c59683887dd122be354abd053a9659123c2c96ccf8116c9d318bd4a6ae82cb68 |
| independent-i163-ci-cases-v1.json | 7169e33e5e625abf188958ddb2edf7d18244ce7a7f83cbde9d786b9db9b18a0b |
| independent-i164-ci-cases-v1.json | e111a85361fcb81ba6802a18e9b37f2688f645daeadddbd424b0f687bea2ff7e |
| independent-i165-ci-cases-v1.json | a9c278de03929d3c4326f77eb6b9ed778683af6b0de720d8d214df7aef2079fa |
| independent-i166-ci-cases-v1.json | 08bdfd8b00febb966741aa27ec8febf79827dbf767c4b70a95cb6f5cb12a0b9d |
| independent-i167-ci-cases-v1.json | 9c7d6e13b3d8f1977e56e234c1003811125c840db74367478f5df56c2f868ac2 |
| independent-i168-ci-cases-v1.json | 428129cab07360bd65a253cbad7b9f4bbe78d3def2c8892d0e7663859b0ec06b |
| independent-i169-ci-cases-v1.json | ec5a6dade05e3aac196bda6693f40eebdff4fac86f2030b2797a3e85a79561bd |
| independent-i170-ci-cases-v1.json | 24bfcb76a366cde17a4aad533a0ce8027c834e22b737892001b3592177ef15de |
| independent-i171-ci-cases-v1.json | 8935f53910c8bacebcefa673a880b743634e5eb8400024768b7b0819368548c8 |
| independent-i172-ci-cases-v1.json | 579a3360660638e5b8c3bb380603bb9f1b1f875cc0a91fdf6475087e03589060 |
| independent-i173-ci-cases-v1.json | 4f0227402e5ea657ca2ad4ecc5361e122b836d09515e72b516088dd5ed7b37be |
| independent-i174-ci-cases-v1.json | d4e233d19a7328bc60ba6de393a756002bff993a5a201b0eaf3061d7b2401879 |
| independent-i175-ci-cases-v1.json | c1d685e607feb1cf2a9acb8643b885979e1c150deb4f18a5e597d810311403a5 |
| independent-i176-ci-cases-v1.json | acf0086a546226d672ec02c15b2e62fc204138e53d391e76d69059a37934eabe |
| independent-i177-ci-cases-v1.json | ab73f6760c679e5a6b6cbf5e1bd57cc6df0e1c658859bc30da7f9d23f412e5e9 |
| independent-i178-ci-cases-v1.json | 63bbc201e7ca4423337be6ed2babaab338323bd72ce106e60eb7113e43586d76 |
| independent-i179-ci-cases-v1.json | 782ef4220b57b4eaac558104fb7f2973bdeafa67503459ab1cefd46e9d4c5b70 |
| independent-i180-ci-cases-v1.json | b11ac0ee1bc2a42e037932fd6693a687bc75a4764c66ca818ca3789f8a2a4bc7 |
| independent-i181-ci-cases-v1.json | 9d8889374f87d3e6c9729b55e4303eaf8deb3f7b3e063b3de6b5caad2aa87d17 |
| independent-i182-ci-cases-v1.json | 59c92ecb16ddb9b62e785288502be535386d061ec8d4cf99eb06a7061d1ca90d |
| independent-i183-ci-cases-v1.json | 766966d5e218acd269971abafd9e3af6d78e80426d59a38ef00b5a61b97b1cb1 |
| independent-i184-ci-cases-v1.json | 1b6a44744ee7ec6101cbbb9938e6771c785693fcea56676425033ac78caeaedc |
| independent-i185-ci-cases-v1.json | 13321f775ab0e2654745282e25de6675205c7ffb08f416b65a3ba9fa5e426f26 |
| independent-i186-ci-cases-v1.json | d72b53b83e43d95025a77419003f39c258ee4076e6a822ef79c4d265517ef32a |
| independent-producer-policy-ci-v1.json | e2100bdc1080dd483a5b2407a5590950a567aaaa43c31e8818b3869780673c4a |
| independent-restore-ci-v1.json | 3b968d7849af03e7da092e30e217ced6d7b6d2144a9baa4c4d446b3961119823 |
| independent-separation-ci-v1.json | 355cdf7fa0b17e6e4f226d7152840983a245f91b0c0c0efce003c8d39e22600c |
