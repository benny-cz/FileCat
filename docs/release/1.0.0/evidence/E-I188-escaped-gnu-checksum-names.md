# E-I188 — escaped GNU checksum names preserve actual Unix file identity

2026-10-07. High checksum identity defect under I06/V13/V15; remediated preliminarily at 4c86041d2b6df3b4a22c01299f47a6a5d2b43245. Broader formats, source revisions and final candidate qualification remain open.

Original d04a5d7 treats the literal Unix filename owned\child.bin as owned/child.bin. Two escaped GNU SHA-256/MD5 manifests falsely finish Completed with “1 verified, all match” for the directory lookalike, while the intended literal file differs. Two generated GNU manifests emit the wrong name and verify the directory lookalike instead. All four original Mac adverse cases fail; four ordinary and legacy-unescaped-separator positives pass. These controls use distinct actual 26-byte/28-byte owned files, the actual manifest parser and a read-only JobManager verification job with LocalFileSystemProvider; no worker/provider substitution or physical source is used.

GNU manifest writing now preserves and escapes literal Unix backslashes, and explicitly escaped GNU parsing preserves them during resolution. Windows and legacy unescaped backslash separators retain their behavior. SFV writing is unchanged. This finite correction does not establish all BSD/SFV, ambiguous-name, root-folder, ending-backslash or revision behavior; MD5 retains its existing compatibility warning.

Working Windows and fresh locked committed Windows builds each retain all nineteen prior manifest case names/outcomes plus eight new cases: 23 pass and four explicitly skip filenames Windows cannot materialize. The actual ordinary-user ARM64 Mac payload passes all 27 without skips. Escaped cases now select the intended 26-byte literal file and report Failed/“0 match, 1 do not match”; generated cases select it and succeed. Ordinary/legacy cases, traversal/absolute/invalid-escape controls, byte counts, exact independent Python SHA-256/MD5 and owned-file contents agree. Actual CLI results and before/after payload pins are retained.

The original prototype expects CompletedWithIssues after a single intended mismatch; its original failures occur earlier at the wrong parsed path. Inspection of the actual zero-match job contract requires Failed. Before fixed execution, only that future assertion is corrected, retaining the original prototype and four original path failures. The clean helper subsequently prints an App assembly name absent from this Core payload and fails after saving the successful command and all 27 cases. Its source, exact failure, payload and raw outcomes remain; a fresh reader independently seals them using FileCat.Core.dll without any build/test rerun.

Independent seal e3a636f2700052c0c4bd1a90a36225aa56633fd3c54c47d3eb2608b50e9d31ec checks all 53 retained files, 574 actual Windows/Mac payload references, all 1,098 original raw Git blobs/modes/archive members and all 1,100 clean committed members. Clean FileCat.Core.dll SHA-256 ad3166c8dce73d8e4299a9ad8d4be052e572917b8106e709bc8925aa94148514. Correction changes only the GNU identity implementation and eight new controls relative to parent 55c1383. Both owned Mac stages and bounded awake helpers are removed after nine result files per stage are retrieved and independently hash compared; no persistent settings, SDK, VM or USB changes occur.

Original push CI [37590686259](https://github.com/benny-cz/FileCat/actions/runs/37590686259), attempt 1 at 4c86041, is sealed green on policy/all four lanes. All 32 distinct new executions reconcile: 24 pass and eight explicitly skip Unix literal filenames on Windows; both Unix lanes pass all eight new names. Full Core inventories retain all prior 890 Windows/885 Unix names/outcomes/skips plus exactly eight, giving 898/893 cases. Full App remains 559 and Remote/Platform outcomes remain unchanged. Nineteen selected server digests/every member, fourteen raw inventories, four compiler receipts and 92 locked graphs reconcile. Native installed-candidate qualification remains open. These actual filesystem/CLI controls do not qualify native GUI/human/reference/physical-source/installed-candidate behavior. No contract freeze, candidate or stable GO is claimed.

Private `FileCatReleaseEvidence/cm188-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-win-v1/command.json | f7c3b09ef2c8ea201aaf743acc10dabf51c58e7d7956b1d0cf0664c909ca8345 |
| baseline-win-v1/results/baseline.trx | 2c1178bdf1603fd2a6f27a515b99c968cc398f96c3559b7aa96a4638256e6793 |
| mac-baseline-v3/mac-command-v3.json | d51dace98f9552dcd8826f98836c7331994585289226c1cf0e1efde0b6113049 |
| mac-baseline-v3/mac-results.trx | 581cbf3f296572d73a8c656e6692b10056c3d9ef268058033809ef50eb339a23 |
| mac-payload-v1.json | 9ad9494dd029e48cc119df48b27ee2cbb42a4ebb820361ccf38aa5c004b3fccb |
| mac-payload-v1.zip | 5cb13e30b78339726bbfec5ce08911d89b7760b3f7520dbcd856426d800c288b |
| mac-baseline-restoration-v4.json | e7f4e3a9630442f1ac649ab63e5c416cb69a6e43dfcb92998033467d415ad639 |
| working-win-v4/command.json | b2a287fc3331ca1bf99e14b47f9c5473ca093318a6618aa4f92376cb24be1295 |
| working-win-v4/results/baseline.trx | fac6fbdbfa7f08ec0c734997630e577f00ecf895245611dfc6d93336e5c2f041 |
| mac-working-v5/mac-command-v3.json | b54a7fe50c0fdb65680cf82d454162197836187e634980ac1761f73496d94c97 |
| mac-working-v5/mac-results.trx | e46e01ec5039e1b331bb984fabd2b7451e2cbb7fc9ac6dc1e845e23d53644091 |
| mac-working-payload-v5.json | e3fa98900f979e8372b0b61706267e2b1ef725f7c572098db8dc1537ea3492d2 |
| mac-working-payload-v5.zip | d66e1a6315716eeb07d07d1f1b3f0b63b950a87173133feb1f0a736120687692 |
| mac-working-restoration-v6.json | ea8631bb30646cb22c45fcf2f62da04fe286657fd0494b92d3b6e5ff4192bf3e |
| independent-working-v6.json | 6d2869fdd740cedf0397c048b64db23ba875f4dd10a6d01c080ed9b4988e3149 |
| clean-v7/command.json | dd2da83042cbc2d076fa92c04078d7d31db061b5f8ebba7a0a63f9af054f86ce |
| clean-v7/results/clean.trx | 3cb19d3f3e887f2e5e1a2e7f25083099fcc7f72cf4891de0ed70915b86c1e0ca |
| clean-helper-assembly-name-failure-v8.json | 233a9ee3f3744f98cca18dcc31505cd8e8b6700e090c8aa6621423f96eae7a22 |
| independent-checksum-names-v8.json | e3a636f2700052c0c4bd1a90a36225aa56633fd3c54c47d3eb2608b50e9d31ec |

The collector retains 22 successful original-run proofs, then stops at an old I179 checkpoint assuming unchanged Core names. Fresh v2 verifies every saved member/raw inventory and completes the remaining proof writers while preserving all 22 original pins; no artifact request, build/test or CI run repeats. Independent verifier v3 subsequently retains its pre-I188-writer count guard (31 versus 32 existing proofs); fresh v4 verifies the current staged count and all original data without running product code. Both failed sources/exact assertions and the partial successful pins remain. Independent CI seal SHA-256 dc03be43d141923f435383d69044b1d6f87acd828f3c39329db8ff37127c75be verifies all 32 case/policy/restore proofs and the complete inventories. These tooling failures are separate from the passing original CI.

Private `FileCatReleaseEvidence/ci-37590686259-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 81e1298336108f88aee72d6f3ee9eddadb4dd1ed47d18fcb3233077f87dfa4eb |
| independent-draft-guard-ci-v1.json | 82cfc7def322746212e93f17862cd947aebd552246e81e52cb09830ef4f70a70 |
| independent-fixture-ci-v1.json | f0ea5c332b7795d4a12a4342b279240513a9bf2aa7abc4604bbee95783c250cd |
| independent-i163-ci-cases-v1.json | 14935330bafb434d7041acc07f26b4bb18cc87c129db9ecb6b683f49811ebd6a |
| independent-i164-ci-cases-v1.json | 8a83598561f2291b380bf45a62cd90d76c4214ec5be2fdc89935fb3fe048fc0a |
| independent-i165-ci-cases-v1.json | 6204137099719583949d015ee44a7c8d401b4d401b3f57b97f8c7ce330e5fd26 |
| independent-i166-ci-cases-v1.json | 36300c9709cb7c03ebaea2dc720bd7790aad12d21b8bd2f8604ab0041f71b262 |
| independent-i167-ci-cases-v1.json | 614cd65e4c9d65383e7c6d3f2801c5889d3122e04269b10acb3cc9e74a97592c |
| independent-i168-ci-cases-v1.json | 3ce9337d214c47fd149155932b79a35a0f2ec25042ce5bf49cbae21d4e5479ea |
| independent-i169-ci-cases-v1.json | c68651e7850c20569838c2af9436614d49749e0b2c11ea5364adc9d3c75dc07f |
| independent-i170-ci-cases-v1.json | 4d8774345c26e7a57ff3c292363af3ba4aeca7fcf91e2cc7ddb9b05eb604e8a5 |
| independent-i171-ci-cases-v1.json | 26ded17e81cbe5fc1eed3436572cb18bbaa162cb11d09e4266ffbf40c5b94f39 |
| independent-i172-ci-cases-v1.json | 687e9869c763a41fbeba94becf167c0c6c807577f13f61b520fc4111b861285e |
| independent-i173-ci-cases-v1.json | 37df15006b54aecae8ab7cff3ee2599741b54f7c1152601283ea190044126bf4 |
| independent-i174-ci-cases-v1.json | d213738704bfbbe8d59dd7230e5e523c7ed871f463e6ed17b303fef2bef152bb |
| independent-i175-ci-cases-v1.json | c0ddd49e30800f9bc6a265a427c1b96a8ef2178db96abcb1204a6042448310f7 |
| independent-i176-ci-cases-v1.json | dbf09a3ed8f757bbb2d67615aeee47568b30e08c5f65a98bab125ca3ff504531 |
| independent-i177-ci-cases-v1.json | 1f5230a7d64e3505bdff4d2bcaa7a2c02da70dc0899ed284f586757418ff608c |
| independent-i178-ci-cases-v1.json | d79a74c623b9fb336c1b621de39123a35c478b581e1da8dc9a1c0c51d9a795b6 |
| independent-i179-ci-cases-v1.json | b9a506ca4f910a726b1cb5a26263c17090143c824ab444481db5f4673eb4def3 |
| independent-i180-ci-cases-v1.json | 1690f4c46aa05a63edb3b3b7543751d0b036c5d32a73e7ecea9a8cf0039fdb7e |
| independent-i181-ci-cases-v1.json | 106037ae1b8d97c5dbedde162648c022c32e8a448442ff0f286a2a283020c5e0 |
| independent-i182-ci-cases-v1.json | 3bd2663bc97d3e6f009c54627f06ae5682ecba3949e5962874aa6c3b76e84d75 |
| independent-i183-ci-cases-v1.json | 9b803721d3d7ee632db7b7ee623ad92391266dd0efacfc92015ff5d1197b95d5 |
| independent-i184-ci-cases-v1.json | 6c2a18775743599457ac480bc15095a1730a9293ec085cdcd1d2caa0d7669ec5 |
| independent-i185-ci-cases-v1.json | 5dae3f3a8cd2a8da99022bbcb4e52972d7f03f044782c84a03ebbdf1b30f1306 |
| independent-i186-ci-cases-v1.json | 891e71c16d809f9673f91cb05d17a185f994e1b8a2a7c7124a71b175643ec169 |
| independent-i187-ci-cases-v1.json | 33f3679b70882c9b764af3fd292618bb28eec583259de0382b01faac4c895069 |
| independent-i188-ci-audit-v4.json | dc03be43d141923f435383d69044b1d6f87acd828f3c39329db8ff37127c75be |
| independent-i188-ci-cases-v1.json | db553384cb937ca0ba11e8901de8ae882de2e8bfeb377912b4f8e67cd02773a5 |
| independent-producer-policy-ci-v1.json | 17d99c3a4469ebc5f6a26e3e61509458a5a371bb529440c89a413c4ff9197c38 |
| independent-restore-ci-v1.json | da95c1ba53cd38aee46d11776cee1f2fe4b92ed28f5b806a9ffe27a0368ea5ff |
| independent-separation-ci-v1.json | c51ef771850a7834e5715286d8258b9ae45463adba7f03af7dd149a924678a58 |
| collector-core-growth-failure-v2.json | 7f7789bfff2ba662d9702b042a73a0cae330eb2487f9319ecf033ae2e0aa46b8 |
| verifier-staged-count-failure-v4.json | 0108c3621c940d4a32f30d6d2a6016c55b72965b43701154ceec3852116c3daa |
