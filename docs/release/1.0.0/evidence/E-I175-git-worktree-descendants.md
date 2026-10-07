# E-I175 — automatic Git badges refuse linked worktree descendants

2026-10-07. Discovered against canonical product source 2503ab0623fd05a98fd5781ebe7ccb4623adf06d; subsequent a239497 documentation changes leave this Git implementation unchanged. High automatic-path boundary defect; must fix under I16/V23 B10/V24. Remediated preliminarily at 641827885a00e68e90372a459364bbd998d518cc. Concurrent swaps, network-effect capture, Unix mounted paths and candidate qualification remain open.

## Failure and correction

The admission check validates repository metadata and the worktree root, but Git for Windows traverses junctions below that root while finding untracked files. A complete FileCat reader admits the owned junction and returns an Untracked badge when its target-only file exists, None after that file is removed, then Untracked when restored. The link stays in place throughout. Independent native Git returns target-link/ in the production normal-untracked mode and target-link/target-only.txt with all-untracked output. These observations establish actual traversal of an owned local target; unexpected remote contact through a network-directed link is a possible consequence, not a captured fact in this slice.

The correction applies the existing bounded Windows tree admission to default and linked worktrees, and to configured core.worktree targets resolved against the actual gitdir. It refuses reparse points before looking beneath them. The existing limits are 10,000 entries, depth 64, 1,048,576 accumulated path characters and 250 milliseconds per walk. Oversized or linked Windows worktrees conservatively get plain icons. This is a stable-path admission snapshot, not protection against a concurrent path swap. Unix behavior is unchanged and its mounted-path boundaries remain unqualified.

## Controlled validation and provenance

Eight durable controls exercise the complete reader on real owned repositories: four junction cases (direct, nested, configured and actual linked worktree), three ordinary nested-worktree positives and a deep-tree admission budget. Original source has five failures and three positive passes. All four complete-reader failures record an admitted snapshot with unchanged target bytes. Working and fresh locked committed runs each record 96 Passed/one explicit unavailable network-capture skip across all 97 Git cases; all eight additions pass without skips locally. No skipped capture is presented as evidence of zero network contact.

The independent seal verifies 110 retained local verification files, 423 actual test payload files, 110 sealed native-observer payload files, the original 1,072 raw-source overlays and all 1,074 clean committed Git blobs/modes plus their archive. Working module bytes differ from canonical bytes only by Git CRLF normalization; test bytes match exactly. Clean actual FileCat.dll SHA-256 d74f0d96dc25827f031a2b3a1cba72eb44608bcd9ee61ddd89a1b4cdb34e85d8. Native observers load that exact complete App module rather than a substituted Git implementation.

The clean native reader refuses the same junction with the target file present, absent and restored. Removing the exact checked owned junction nonrecursively restores ordinary badge availability; target bytes remain unchanged. A first native controller reaches all three correct refusals, then its cleanup spelling comparison fails; those source/results are retained. A bounded fresh controller verifies the same target's file identity before removal. An initial seal also rejects raw working/canonical byte equality; its corrected seal independently proves the CRLF-only difference. Both are controller preflight failures, separate from the reproduced product failures. The failed native observer payload remains in addition to the 110 sealed observer payload files.

Original push CI [37562601681](https://github.com/benny-cz/FileCat/actions/runs/37562601681), attempt 1 at 6418278, is pending at this local seal. Component/native CLI observations do not qualify desktop interaction, physical media, remote-contact capture, human UX or an installed candidate. Owned test fixtures clean up; no Mac/VM settings, physical source, contract, candidate or publication changes occur.

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
