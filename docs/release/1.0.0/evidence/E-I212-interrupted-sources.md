# I212 — interrupted root source review and queued execution

**Preliminary remediation qualified at 8696012ac55a046e845f36e2e39d144e6d88d1d7.** All 75 additions pass (59 App, 16 Core). The final working run qualifies those 75; the exact Git-canonical clean expanded run passes 639 targeted checks (112 Core, 90 Remote, 437 App), with eight explicit existing skips. Full clean App separately passes 1070 cases with 23 explicit skips. Every previously qualified targeted/full App name/outcome/skip is retained. These are owned-file component/headless controls, with synthetic App identity/path replies and actual portable Core filesystem operations; native identity/desktop/hardware/candidate qualification is not claimed.

| Boundary | Qualified behavior |
|---|---|
| Before confirmation | Each available source root is captured on its shared device worker. The record includes item kind/link target, size, times, attributes, available file identity and resolved path. Metadata read again after the identity/path calls must still match. Initially missing roots may be omitted; the remaining reviewed root can continue. Unavailable sources are not described as definitely gone. |
| Before partial cleanup | Approval rechecks every reviewed source. Disappearance, rename, changed metadata/kind, reported identity/resolved path/link target or unavailable info/identity/path refuses continuation before staged-file deletion. The old journal and staged file remain. Cancel remains a no-op. |
| After the kept-file alert | A further source check runs before submitting. Changes during that alert refuse submission and leave the old journal open. Previously completed, approved cleanup cannot be undone; this is not a transaction. |
| Active/queued device work | Info/identity/path calls run on shared source workers. Active revalidation stays owned until its native call returns; ended demand cannot clean up, submit or close the journal. The queued controls observe the actual accepted close/shutdown, not the earlier close request. |
| Queued job and later roots | The reviewed root records travel with the local copy/move request. A changed/unavailable or unreviewed root refuses the request before destination creation/discovery; each root is checked again before processing. A later root changed during the first transfer is kept while the first destination remains exact. |

## Retained baseline and adverse qualification

The unchanged 698b47b producer executes all 59 App controls: two cancellation positives pass and 57 checks fail. These are not 57 demonstrated data-loss defects. The final baseline distinguishes unchecked/changed-source admission or outcome failures, seven ordinary positive worker checkpoints absent from the old caller, six unavailable revalidation checkpoints or legacy job-read holds, and one synthetic-link fixture that reaches a legacy job decision timeout. The queued removed-source cases already report a failed missing item; their diagnostic differs from the new pre-change refusal. Raw outcomes and actual emitted observations retain those distinctions. Core guards are new API controls and do not pretend to execute on a producer without that API.

Baseline-v1/v2 preserve two fixture compile errors (ambiguous imported types, then a button method/type name collision); they execute no tests. Baseline-v3 retains the earlier 51-case form. Working-v4 passes 58/59, but its queued-close check waits long enough for the watchdog to admit work before shutdown. Working-v6 retains the broader intermediate run: its copy control never invoked the selected publication hook, queued-close/end counts use the close request rather than OnClosed, and a full-suite existing Windows icon control misses its checkpoint. Working-v7 retains the ineffective copy hook; working-v8 corrects that hook but still has the wrong close epoch. Final working-v9 observes the accepted end boundary and all 75 pass. Canonical clean-v10 independently qualifies the final source and expanded/full suites; earlier failures remain sealed and are not overwritten by that run.

## Remaining scope

Root metadata/available identity/resolved path is a conservative path check. It does not hash same-size/time content, freeze directory descendants, guarantee native identity where unavailable, hold mutation handles atomically, or close races between separate checks and path-based native operations. Parent/destination alias substitution, staged/rename/native atomic identity, inaccessible/linked/large source and provider matrices, cleanup access/sharing, physical-source holds and exact-candidate/native repeats remain in I06/V03/V08/V11/V12/V23. Old journal reconciliation records submission, not guaranteed completion of the newly queued job. No VM/Mac temporary setup, physical source, freeze or publication was changed.

## Provenance

Private `FileCatReleaseEvidence/is212-v1`:

| File | SHA-256 |
|---|---|
| baseline-v1/command.json | f5e2cc84969778011ba393395332f78eeb8329887798202239e75a97370612ca |
| baseline-v1/source.zip | edbeb12ad572a5765b25ec07d827352568f6db0af50697c037e55b8020e93958 |
| baseline-v1/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| baseline-v1/app-stdout.txt | 5b3df8fd9bed23d8b6438158f1f66a88a8f818eb9eddf5f98f1d58db19b42c8e |
| baseline-v2/command.json | c0daa319085985f192242fd9991462369c18d9900399d106f454b642aa389ab9 |
| baseline-v2/source.zip | 7dfb8524b2c56da6dd06ab221a1a757a286d1dddcfb033feefa3726a35f608c3 |
| baseline-v2/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| baseline-v2/app-stdout.txt | 25ee76e8145da74b16f9f5f86c1219fc8cb35750da41d45251c08c94b947afcc |
| baseline-v3/command.json | ea7753479a77d78f1700cf49af60a07825237859412f57a6693f470ec67d1860 |
| baseline-v3/source.zip | f1c33f16f2080b69fb6bc05543c3eb404f4044f8318492e895bf44ee71125700 |
| baseline-v3/app-stderr.txt | a5f04a0087368430b5dc8791964d6e9c0b4680c39397c0a3a4426e76714c1871 |
| baseline-v3/app-stdout.txt | cc5f10890bff11f55d77d9278f07d8621ca7bb65a60b1f236aeacfe98bab18be |
| baseline-v3/results/app.trx | be0c02c92725c78bbd73bf125edde6e6daa12bbdedc0a6c014cc147d3a3e8987 |
| working-v4/command.json | caae98a95db393591cf55315460359b0e6bef4903e2736837a87865202d35ead |
| working-v4/source.zip | 9f8146fd3964051a847cecc3beedb795e632189670a83a302e9d67cdfea1f112 |
| working-v4/app-stderr.txt | bd0f92a779eef771f7e92cb094e49ef10630ad2b38f2329fe81a14b2ef5a18a5 |
| working-v4/app-stdout.txt | 43915f23f0b5f6058a1de9e9b74f452a6becd146f1dc0568f4c359febd34b9a2 |
| working-v4/results/app.trx | 143591132e74a86cdbe6137b26f6fd38fd4bd86d554eb582b624bdf0431e391a |
| baseline-v5/command.json | f9a4058f35788633ae345cc6d623e93d2de327db6ca6aec2f85a5e21b3d30460 |
| baseline-v5/source.zip | a0a147224d16fd399f67cfcf8a7311f72bde7713f26631b68a9190f3ba5a589e |
| baseline-v5/app-stderr.txt | 1aabd4c52435ae004a2ec96d295f3a58e68cc00218c6648db7a8f578c8f68d67 |
| baseline-v5/app-stdout.txt | 12d4a7d95222d821a5a7cb0033678c149ff8af2404e6e88d05a4dc405ab8913d |
| baseline-v5/results/app.trx | 8ba984dae6f5fea56d4dc4c258423f9818fc7436088a293bd39bba8a5e9123db |
| working-v6/command.json | 9a26ceaef33e76f742bab98b2a608b01b6e20982db66ad3654b04a96b5ba0b0f |
| working-v6/source.zip | daf3e9134a4dc9ffa73d5eda61ffa6ec106ad72e3da8003951bd2ea2db2f45a0 |
| working-v6/app-full-stderr.txt | 768fc1b5824cc33cead9b89a80366a0d50f1120c4f3ee993008952bf0815b1b8 |
| working-v6/app-full-stdout.txt | 83e5e1aa6c9e0c43860833251240f511aec17627133b233cd7b5419c210dd43a |
| working-v6/app-stderr.txt | c1de3af29da693c7257ff3002e228436a980e900a578c91a78b75e492eabf1e9 |
| working-v6/app-stdout.txt | 2888641579ca562432e255203c95a020e7e5669ebff4d3b036878215154ea883 |
| working-v6/core-stderr.txt | 1909ed4ebe3c86cc680cbb8c7e503113de5bbf6ce73d308a4f023d294924d534 |
| working-v6/core-stdout.txt | d7880ac4a8799f89b423d842410d9bc37dafcf96646f8ef1edec18051a67be91 |
| working-v6/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v6/remote-stdout.txt | 9b1be78f7e4ab5b09ea7476557259e5c6448bdc69c2b54e3e86e15f758d5581e |
| working-v6/results/app-full.trx | a17f4d0679cc479398a2cd4fe21e3c0837503a44af3a8e4743b6372b8e5fe375 |
| working-v6/results/app.trx | c06126f1039ee5b31e4d91b2701ffc77f825fa76bab4380773090de4666a273d |
| working-v6/results/core.trx | 8b5da399aa7501bc4ae956924a3daeac2f51a53e278e0e4dd4cd5d6dd8769b02 |
| working-v6/results/remote.trx | a48a050c1065e0e31657e23cdff82923cab8fd642ef5ced9da8be9a4e04f0166 |
| working-v7/command.json | 19c961c07c2f7590be71821709efb6a081689c379007a3ebfae08032b097cf0d |
| working-v7/source.zip | aff3c14d8decad4bd87a863e0505416970e193accc8a49b7d6ad078ea1c11f3f |
| working-v7/app-stderr.txt | 7f7aa2e2425540cf1e906787a802fa5e2fc812c06ddd28d7b6afcd02ed90f743 |
| working-v7/app-stdout.txt | b0923debce564103526caca7e74c33d2f28158bd7c07291d3a649e84e7aaddb7 |
| working-v7/core-stderr.txt | bbeb2ea2ab0ca9d1d93749d323c69c06d057ac6e32f77afd625cef64963a28e6 |
| working-v7/core-stdout.txt | da2d969eb1f2e70a062ffdb698ce9d6bbc1613b6dfc9a0fd49033a3ec815e08e |
| working-v7/results/app.trx | de5dec81b06f2e7ccde5cd6da7bae452c1a4ea932c74de010b1cfd29b58feb5f |
| working-v7/results/core.trx | 27fc44266c3d4904bf0683b2ba7f549c054f6ec93f08c27bcd9305f256b8ce05 |
| working-v8/command.json | b29c32b496b7ee12c65575424d5001beabf56ea7600e926dcbd5f31433b605f8 |
| working-v8/source.zip | e827e90d3e6fa67dc036ad4af7e5ca74fc09b5db0aa36247c92d5a273f68d9ca |
| working-v8/app-stderr.txt | 8ce82f4e78fc7c7b346f6605b518b2bf0fbc57bb3a8a2a13b3fa20a7ba67f4ab |
| working-v8/app-stdout.txt | 78bb940d7cd8c226d516123956ead3899125af1c69c4e89aeeeeae55d368acf3 |
| working-v8/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v8/core-stdout.txt | 2138b131c7770f1133eeccce0d98754e842c33ee29321b87b3b26d9679d853f2 |
| working-v8/results/app.trx | 44fc842398c9a12063272ea839c216c52695d2da93849db7fb4a74bcb7b05b52 |
| working-v8/results/core.trx | 5f4d427555838c36530d244f404a58b16b876b011ec9117047ba2b848fc6d9de |
| working-v9/command.json | 5be029b73b25f028a87f851458e94513f02ced2e5c836bafdbc350a1bae95188 |
| working-v9/source.zip | 0402a901109658068f616a4195b498430042d82ae431a96c61b9210796ff9045 |
| working-v9/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v9/app-stdout.txt | ceb3f579f6ec8cf1f37cc539bbdc7ed411743b1987b281d54c078733e9bacf1f |
| working-v9/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v9/core-stdout.txt | 1aaf679b3da212a99b74527225517a10e7699a2aae4a45eb24644f560e10e1c4 |
| working-v9/results/app.trx | 7fbbf54a6c7103788f59e99a750733d541814e4aacd7346876d318d9acdb7bb3 |
| working-v9/results/core.trx | 69b752f817fe3acf129f5b40ce44ebef21dee85006edf0e0e4cd0c67b798520b |
| clean-v10/command.json | 4e43135d2832b409c697d85644c30c9c78268326cf475d85713ff60711deb83a |
| clean-v10/source.zip | 8b64f231d630c27f02dd41f9b09376560db9b8fbe919bfa634b73b37ea9b1b36 |
| clean-v10/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v10/app-full-stdout.txt | acc609310e24fb4282576bf1de10e367dc4b99d9463dbc018a0cbc7b4fb67119 |
| clean-v10/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v10/app-stdout.txt | d06a44f2730d80226b8cd347c5764dcc7e43b916805d74b464d9dd1c35797ce7 |
| clean-v10/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v10/core-stdout.txt | 19012c625a1956da4896cae58eab16277f570f53165ecbf56c16db1a28084772 |
| clean-v10/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v10/remote-stdout.txt | 700492eca2c53868b6f2173048a1a8ea07dfd41d9412067fa6745c184ffcc2ef |
| clean-v10/results/app-full.trx | ef67d1d0ce9f7ef0596105695ab96cc4b120d747376b3bef186a4e741a0c6d78 |
| clean-v10/results/app.trx | 8f015b593e01ad777e6f7fd503ec143143259089cd45cd33c423bd0d48a72391 |
| clean-v10/results/core.trx | 5cd56b454c2a57b459eae3e01a712896863c72ec37719e22b50f23cd3b8885a2 |
| clean-v10/results/remote.trx | b947f3c60c57e16298ae670d131102bd27a786cd2f0958e6ab2c23007f0187ea |
| run-interrupted-source-v1.py | ace86e16c729b42b2e4b1fc27874d53ae5536c77a92aa4059e0f6b0a4f6001a3 |
| run-interrupted-source-v2.py | 03b12862a7ef2148f48c0aad3e408c8c465ab9de700343885d2846a764aa0ccd |
| run-interrupted-source-v3.py | b7ec11b42184f2fe990b42138eca0e44416d431b234279296e761f5d645a545e |
| run-interrupted-source-v4.py | aeb20d974ad634cebe855aded07c40ae254542c0ad15c4b4cdb9553bb65a00fb |
| seal-interrupted-source-v1.py | c856bc630f20c41976baa3f46d77a6b42a699ff2c223cc085b9a200cb42aac86 |
| independent-interrupted-source-clean-v1.json | 0644f3fceadc2c5d5761b533789ca029c8d2ab61b0b9cba38a395146958673dd |
| update-documents-v1.py | 08475226726a7fa8d880a068fb9bf1c271ff0c379651c0f01c79800986b2dc9c |
| document-transitions-v1.json | 4fa795a83f81bc7638a6197e4934e35d508874d76a2bb20ca77de027f4bff473 |
