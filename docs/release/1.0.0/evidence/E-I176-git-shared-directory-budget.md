# E-I176 — refuse oversized Git shared-directory metadata

2026-10-07. Discovered against complete canonical product 641827885a00e68e90372a459364bbd998d518cc; intervening documentation commits leave this implementation unchanged. High automatic-execution boundary defect; must fix under I16/V23 B10/V24. Remediated preliminarily at fcae7632a4cf13f089243e28065df2b140d613de. Broader indirect paths, concurrent swaps and candidate qualification remain open.

## Failure and correction

SharedWith silently ignores a commondir file larger than 4,096 bytes and continues admission without inspecting the shared configuration. Git accepts a valid long spelling made of 2,100 ./ segments followed by ../.. and a newline: the 4,206-byte file resolves to the same shared .git directory. On an owned linked worktree with a same-size tracked-file change, the complete original FileCat reader admits that path and Git executes a harmless owned clean filter from the unchecked shared configuration. The filter reads 22 owned input bytes, records an owned marker and returns those bytes unchanged. Small original spellings refuse the same filter before and after the long-path case. Native Git independently confirms the selected shared directory and filter recipe.

The correction refuses an existing commondir file over the admission budget rather than ignoring it. Ordinary spellings within the budget retain automatic badges; the exact 4,096-byte boundary passes. The normal shared-configuration checks remain in effect. No broad refactor or new external operation is introduced.

## Controlled validation and provenance

Eight durable complete-reader cases use actual owned Git linked worktrees: four oversized spellings (4,205/4,206/6,005/6,006 bytes), three ordinary spellings including the exact budget boundary, and one small shared-filter refusal. Original source records four failures/four positive passes. Working and fresh locked committed runs each record 104 Passed/one explicit unavailable network-capture skip across all 105 Git cases; all eight additions pass. The durable filter is inert cat; the separately retained native reproduction proves actual execution with the harmless owned marker.

The independent seal verifies 311 retained local files, 423 actual test payload files, 110 native-probe payload references/files, original 1,074 raw-source overlays and all 1,076 clean committed Git blobs/modes plus their archive. Working module bytes differ from canonical bytes only by Git CRLF normalization; test bytes match exactly. Clean actual FileCat.dll SHA-256 d9520c111d78676701bcbaa562de6592900efcdf06efa87e1a0c7d57d2efddbe. Native observers load the exact complete original/corrected App modules, rather than a substituted Git implementation.

The corrected native reader refuses all three small/long/restored spellings while the shared filter is configured, with no marker. Removing the filter and restoring the original spelling restores ordinary badge availability. Shared config and commondir bytes are restored exactly and markers removed. Retained earlier experiments are separate: trailing spaces after an internal newline form an invalid Git path and never establish the defect; a valid long path with a different-size tracked change admits the reader but stat-based status does not invoke the filter. Their original source/output and failed controller assertions remain preserved. The same-size control establishes execution. No network contact, user data or physical source is involved.

Original push CI [37564957822](https://github.com/benny-cz/FileCat/actions/runs/37564957822), attempt 1 at fcae763, is sealed green on policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26. All nineteen server digests/all members, fourteen complete TRX inventories, four compiler/tool receipts and 92 locked graphs verify. Each full 501-case App inventory equals the prior 493 names plus exactly eight shared-directory additions; all 32 distinct new executions pass without skips, including all sixteen oversized-path observations. Core retains 884 Windows/879 Unix names; I163–I175 subsets retain exact outcomes and explicit skips. Full App outcomes: macOS 417 Passed/84 NotExecuted; Ubuntu 415 Passed/86 NotExecuted; both Windows lanes 484 Passed/17 NotExecuted. ARM64 package version startup, headless drawing and installer compilation pass; tagged/manual package and draft jobs skip. No selected shipping artifact or candidate is produced. Component/native CLI evidence is preliminary and does not qualify desktop interaction, ordinary-user containment, physical media, human UX or an installed candidate. No Mac/VM settings, physical-source, contract, candidate or publication changes occur.

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

Private `FileCatReleaseEvidence/ci-37564957822-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | a2d46c8b7c7049ed3a407ef36643e648ec65d57c269e35d2a90c7d9169f506a7 |
| independent-draft-guard-ci-v1.json | 46931ba0dec22d28a452c5ac00c188f19ee2dad882de50475c902ffe367690e5 |
| independent-fixture-ci-v1.json | 66df50f322cc89181a9cd8eed47896ed5ab14963a8f1107ccdd40189405e99e6 |
| independent-i163-ci-cases-v1.json | 7f992a394be6bca77a4a66ff9802f9e8b1e8b802e74cf03157a7458ce2bf78d2 |
| independent-i164-ci-cases-v1.json | f3c9386bae186c4b30338ff58e181cd8d13224b6847077b75a572f2c07e4a5e9 |
| independent-i165-ci-cases-v1.json | e2e3663b1d063b60fce2a6333cf8eb0c32b0c91af54687df400ddedebfd4c8ee |
| independent-i166-ci-cases-v1.json | 743365d82cf73e21d3896f896517f29a912264097d20782f8fc6db7096ad22a1 |
| independent-i167-ci-cases-v1.json | d1dd79db4eef34fbf01e988ca3749609599e1569b0ff78ba68956f6f04dd40ef |
| independent-i168-ci-cases-v1.json | 1e04df9013d46ae38acd8994976b53fead3f4995a33f6d2bd2a6edb59287a62f |
| independent-i169-ci-cases-v1.json | c01e618286eefb4dd77d53590736fb41c7d44fe8f341f9f6de7b87dfa3f074df |
| independent-i170-ci-cases-v1.json | e843e640c30fd1865845ce19271ab42b991e6b552bf58a171ddd27f70e47349f |
| independent-i171-ci-cases-v1.json | 35ddebfd8501aa9ab9115611006bdebfaec0cb3d29f25715628132c0a7a2beaa |
| independent-i172-ci-cases-v1.json | 476048a83be24ede63cff3aae60017547accbb46a2c9305c917b22adb64c4d08 |
| independent-i173-ci-cases-v1.json | 0cb34f5d1a75fa163fdbd12031b65cc19b9e7b713e541a1478863fb95c6cfa32 |
| independent-i174-ci-cases-v1.json | dba708d8d6f6b3767dda67f840ba5a4f775ff5ad9ce451185288fb6dbc9d5c31 |
| independent-i175-ci-cases-v1.json | ecbcb981f99ed5df49c56e641958e397bf84133e6533b9244c31562e83de2c50 |
| independent-i176-ci-cases-v1.json | 95ba9980b93ae6be2af51f31733393a466046b37dd9c2a1ca30bf3905fdd0e17 |
| independent-producer-policy-ci-v1.json | be7a8a980498f44220ab6de99df77402b049a2938d6477049f91784a1a29fbf9 |
| independent-restore-ci-v1.json | 7882c8970d294458de08d030baaa8f2ef10a2c566670674f304eef211b4e4152 |
| independent-separation-ci-v1.json | 5883ee326f57f6c47c26b5f8c0e4b3aa8251a4788455f3bae63c553d7a48b2d5 |
