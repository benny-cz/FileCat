# E-I175 — automatic Git badges refuse linked worktree descendants

2026-10-07. Discovered against canonical product source 2503ab0623fd05a98fd5781ebe7ccb4623adf06d; subsequent a239497 documentation changes leave this Git implementation unchanged. High automatic-path boundary defect; must fix under I16/V23 B10/V24. Remediated preliminarily at 641827885a00e68e90372a459364bbd998d518cc. Concurrent swaps, network-effect capture, Unix mounted paths and candidate qualification remain open.

## Failure and correction

The admission check validates repository metadata and the worktree root, but Git for Windows traverses junctions below that root while finding untracked files. A complete FileCat reader admits the owned junction and returns an Untracked badge when its target-only file exists, None after that file is removed, then Untracked when restored. The link stays in place throughout. Independent native Git returns target-link/ in the production normal-untracked mode and target-link/target-only.txt with all-untracked output. These observations establish actual traversal of an owned local target; unexpected remote contact through a network-directed link is a possible consequence, not a captured fact in this slice.

The correction applies the existing bounded Windows tree admission to default and linked worktrees, and to configured core.worktree targets resolved against the actual gitdir. It refuses reparse points before looking beneath them. The existing limits are 10,000 entries, depth 64, 1,048,576 accumulated path characters and 250 milliseconds per walk. Oversized or linked Windows worktrees conservatively get plain icons. This is a stable-path admission snapshot, not protection against a concurrent path swap. Unix behavior is unchanged and its mounted-path boundaries remain unqualified.

## Controlled validation and provenance

Eight durable controls exercise the complete reader on real owned repositories: four junction cases (direct, nested, configured and actual linked worktree), three ordinary nested-worktree positives and a deep-tree admission budget. Original source has five failures and three positive passes. All four complete-reader failures record an admitted snapshot with unchanged target bytes. Working and fresh locked committed runs each record 96 Passed/one explicit unavailable network-capture skip across all 97 Git cases; all eight additions pass without skips locally. No skipped capture is presented as evidence of zero network contact.

The independent seal verifies 110 retained local verification files, 423 actual test payload files, 110 sealed native-observer payload files, the original 1,072 raw-source overlays and all 1,074 clean committed Git blobs/modes plus their archive. Working module bytes differ from canonical bytes only by Git CRLF normalization; test bytes match exactly. Clean actual FileCat.dll SHA-256 d74f0d96dc25827f031a2b3a1cba72eb44608bcd9ee61ddd89a1b4cdb34e85d8. Native observers load that exact complete App module rather than a substituted Git implementation.

The clean native reader refuses the same junction with the target file present, absent and restored. Removing the exact checked owned junction nonrecursively restores ordinary badge availability; target bytes remain unchanged. A first native controller reaches all three correct refusals, then its cleanup spelling comparison fails; those source/results are retained. A bounded fresh controller verifies the same target's file identity before removal. An initial seal also rejects raw working/canonical byte equality; its corrected seal independently proves the CRLF-only difference. Both are controller preflight failures, separate from the reproduced product failures. The failed native observer payload remains in addition to the 110 sealed observer payload files.

Original push CI [37562601681](https://github.com/benny-cz/FileCat/actions/runs/37562601681), attempt 1 at 6418278, is sealed green on policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26. All nineteen server digests/all members, fourteen complete TRX inventories, four compiler/tool receipts and 92 locked graphs verify. Each full 493-case App inventory equals the prior 485 names plus exactly these eight additions. Both Windows lanes pass all eight; each Unix lane passes the three ordinary controls and explicitly skips the four Windows-junction and one Windows traversal-budget cases. The 32 distinct new execution IDs record 22 passes/ten explicit skips. Core retains 884 Windows/879 Unix names; I163–I174 subsets reconcile. Full App outcomes: app-test-results-macos-26: 409 Passed/84 NotExecuted; app-test-results-ubuntu-24.04: 407 Passed/86 NotExecuted; test-results-windows: 476 Passed/17 NotExecuted; test-results-windows-arm64: 476 Passed/17 NotExecuted. ARM64 package version startup, headless drawing and installer compilation pass; tagged/manual package and draft jobs skip. No selected shipping artifact or candidate is produced. Component/native CLI observations do not qualify desktop interaction, physical media, remote-contact capture, human UX or an installed candidate. Owned test fixtures clean up; no Mac/VM settings, physical source, contract, candidate or publication changes occur.

Private `FileCatReleaseEvidence/git-worktree-child-paths-20261007-v1`:

| Retained path | SHA-256 |
|---|---|
| native-git-v1/native-git-v1.json | 002c61d35afafb27f13b02b62966839811ba548ba8b0d17416d0d33abff9c280 |
| full-reader-original-v2/full-reader-original-v2.json | c21beaee988c386f2a1a520fed3e5daeffddca4ad0c1611b5d316eabcb5e1ac0 |
| durable-baseline-v3/command.json | 5068c6ea594f4de39c913344613cb1c238476ead95a83ae0fff2f8935883b425 |
| durable-baseline-v3/results/baseline.trx | 6125bf4aaf6a41419719291b9f2b98a2cc0b4d662171434d4087707530291d40 |
| working-fixed-v4/command.json | ce5aafee25da291165d1bc1bb2ba1cce0e0aab763ffa066c93168b83b37befaf |
| working-fixed-v4/results/fixed.trx | df50fdea91750d422c138c49ebfba6a215d0420553828e6bd2302d9ba8f68124 |
| independent-working-v5.json | ab40561da0d08b039368b239325ca88289af050882d36511693e7bcfb5f423be |
| clean-committed-v6/command.json | a701dd3550db6140f4c04ce53940f9040e7eed73daee913ba47413640be96cfe |
| clean-committed-v6/results/clean.trx | b0f87a7940c23bfb15ae2a022d2d27b17f2834526c1bacea651d9392882c133a |
| full-reader-clean-v8/full-reader-clean-v8.json | 3cb24c28515ed8fead373f4bf5415308e85324859df1fee0fff266175c92f0d3 |
| independent-worktree-tree-v10.json | c71662510a33861294ef0396b3817a4ac49426aebb4981edf859ca10b2ab3cc9 |

Private `FileCatReleaseEvidence/ci-37562601681-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 04303a3f03a269c5b9262ae6e331249c03cd1dcc4402fdd31ff0f984ee14a020 |
| independent-draft-guard-ci-v1.json | a445389c99df16f87b805f04cd250a55c4cdce196f20e2ab237a68fb10563f8d |
| independent-fixture-ci-v1.json | 7ae14226b17e3d6d0d5de156ec57c31c6a918f3e8ec0144da42d6a262d980b07 |
| independent-i163-ci-cases-v1.json | ef0c5d7f043f5ba3983d6ccf19a303fc62c6cf9193256d06dfd59bb6d0b613af |
| independent-i164-ci-cases-v1.json | f63f7732341afb9f466866a23fb9ccbd5e13bbe0286939ed5789ef074a789c5c |
| independent-i165-ci-cases-v1.json | 055d21eb3863d8575f69e32492c45a35d37689ee8f825bae2745d8ef7b3df1a3 |
| independent-i166-ci-cases-v1.json | 13ae4fcaba6ab2ea743ace6a82ecf75970c78b6aa64a5136a87b3e605db365b5 |
| independent-i167-ci-cases-v1.json | d8daab5398d0799413f20bf11e576bcb0a09d6a235e29cbf7aca00aab46ef22d |
| independent-i168-ci-cases-v1.json | 571eb3fb0a23b00999484379df6ddee9400ce33aa99d65275beee53fd50b4086 |
| independent-i169-ci-cases-v1.json | 40a378f3d46636456ac99fb349f1ab0c68cf6d6cb31b4ee33542859b8ed564c3 |
| independent-i170-ci-cases-v1.json | 6f3d0c65189690b4e284271c286d6462ac757ebeb030704f2bedc6e3fcb1a7ad |
| independent-i171-ci-cases-v1.json | cb47ad6885e6e1bcb4b440aaba8190c16b6011005978e291634c753f523dbd03 |
| independent-i172-ci-cases-v1.json | ea2a9a9ec0aed8103516373bb6758f9f5530be93843a2d08bb8c320d6c1c6869 |
| independent-i173-ci-cases-v1.json | 26c3f293db360d94601ae846a0cdccd3d0dee11cf7f482e8a9e56199674f3a22 |
| independent-i174-ci-cases-v1.json | b278c9dda07a57606fd7c42c8bee245220c55280dff7333f27a94f68e4e34cc3 |
| independent-i175-ci-cases-v1.json | cc71a4dca274392f6675dfee2a3781f3b08e88376b0b3a29da7182f8c0aef284 |
| independent-producer-policy-ci-v1.json | 3e255409c1b06e52e6fac24ac5eed1a98d9f8c2748b413cbec89daf6cc7315f2 |
| independent-restore-ci-v1.json | 4402ebedabc65a787c28c980402c7eeac5e776fc5c1bdfa83774b8263f277479 |
| independent-separation-ci-v1.json | 2f4497df2dd15e9a053a9aa63e95ac9a17c9d791ef0805fde9c397ac1609813c |
