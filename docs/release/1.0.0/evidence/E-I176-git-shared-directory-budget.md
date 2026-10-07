# E-I176 — refuse oversized Git shared-directory metadata

2026-10-07. Discovered against complete canonical product 641827885a00e68e90372a459364bbd998d518cc; intervening documentation commits leave this implementation unchanged. High automatic-execution boundary defect; must fix under I16/V23 B10/V24. Remediated preliminarily at fcae7632a4cf13f089243e28065df2b140d613de. Broader indirect paths, concurrent swaps and candidate qualification remain open.

## Failure and correction

SharedWith silently ignores a commondir file larger than 4,096 bytes and continues admission without inspecting the shared configuration. Git accepts a valid long spelling made of 2,100 ./ segments followed by ../.. and a newline: the 4,206-byte file resolves to the same shared .git directory. On an owned linked worktree with a same-size tracked-file change, the complete original FileCat reader admits that path and Git executes a harmless owned clean filter from the unchecked shared configuration. The filter reads 22 owned input bytes, records an owned marker and returns those bytes unchanged. Small original spellings refuse the same filter before and after the long-path case. Native Git independently confirms the selected shared directory and filter recipe.

The correction refuses an existing commondir file over the admission budget rather than ignoring it. Ordinary spellings within the budget retain automatic badges; the exact 4,096-byte boundary passes. The normal shared-configuration checks remain in effect. No broad refactor or new external operation is introduced.

## Controlled validation and provenance

Eight durable complete-reader cases use actual owned Git linked worktrees: four oversized spellings (4,205/4,206/6,005/6,006 bytes), three ordinary spellings including the exact budget boundary, and one small shared-filter refusal. Original source records four failures/four positive passes. Working and fresh locked committed runs each record 104 Passed/one explicit unavailable network-capture skip across all 105 Git cases; all eight additions pass. The durable filter is inert cat; the separately retained native reproduction proves actual execution with the harmless owned marker.

The independent seal verifies 311 retained local files, 423 actual test payload files, 110 native-probe payload references/files, original 1,074 raw-source overlays and all 1,076 clean committed Git blobs/modes plus their archive. Working module bytes differ from canonical bytes only by Git CRLF normalization; test bytes match exactly. Clean actual FileCat.dll SHA-256 d9520c111d78676701bcbaa562de6592900efcdf06efa87e1a0c7d57d2efddbe. Native observers load the exact complete original/corrected App modules, rather than a substituted Git implementation.

The corrected native reader refuses all three small/long/restored spellings while the shared filter is configured, with no marker. Removing the filter and restoring the original spelling restores ordinary badge availability. Shared config and commondir bytes are restored exactly and markers removed. Retained earlier experiments are separate: trailing spaces after an internal newline form an invalid Git path and never establish the defect; a valid long path with a different-size tracked change admits the reader but stat-based status does not invoke the filter. Their original source/output and failed controller assertions remain preserved. The same-size control establishes execution. No network contact, user data or physical source is involved.

Original push CI [37564957822](https://github.com/benny-cz/FileCat/actions/runs/37564957822), attempt 1 at fcae763, remains pending at this local seal. Component/native CLI evidence is preliminary and does not qualify desktop interaction, ordinary-user containment, physical media, human UX or an installed candidate. No Mac/VM settings, physical-source, contract, candidate or publication changes occur.

Private `FileCatReleaseEvidence/git-common-dir-size-20261007-v1`:

| Retained path | SHA-256 |
|---|---|
| native-original-v3/native-original-v3.json | 993c5ea21362f4a61b74f4bb457f00365b3f87e4a2a85caadc526ff43b42481b |
| durable-baseline-v4/command.json | d7b162699fa2c14028e2b58569953260818ceb2b5bd6b117de65eb5b431264fd |
| durable-baseline-v4/results/baseline.trx | eee0116e55190e87e7e68f90e7f5350e3fab667ec35aeda7ca888024c33c1973 |
| working-fixed-v5/command.json | c274d72318a31a1069aa35257a85e7ac0a120fa501e939801753a8d7c9afe2ce |
| working-fixed-v5/results/fixed.trx | 5b3f75c58fe248be8ade15ea02937cadd23a54c3508828e93dff8060bb582009 |
| independent-working-v7.json | 8e9f2075853d188df1da9e3ee0048d4b49850677e5173ec68cb5d177643111bd |
| clean-committed-v8/command.json | 95fcc703dec862be3135fa0f2dfe9eb24aee3fc2790795764279e8adab68e878 |
| clean-committed-v8/results/clean.trx | ea48e4fe256e7297c9ac56f8f6ed91b563e449539e76229f09c7f4df45ff9049 |
| native-clean-v10/native-clean-v10.json | 1af26c8b5be67a30922870cedc53a44a0cfa9c32fece86e07c211094e8342424 |
| independent-shared-directory-v11.json | 9d81a0acf8b422c029ce8e9883b6a109445d0eaaef9c45ec1e59b1566034fe4d |
