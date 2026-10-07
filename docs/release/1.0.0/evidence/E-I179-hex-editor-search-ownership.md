# E-I179 — hex-editor search completion owns current demand

2026-10-07. Discovered against complete canonical product 4f25c5802a70f9f705c417d7e58878e9381e725d; intervening documentation commits leave this implementation unchanged. Medium result-status and resource-lifetime defect; must fix under I06/V04/V12/V13. Remediated preliminarily at e9a9a98f1fba2c646095a3c0c352002aa3ca354b. Wider worker/consumer/frame lifetimes, native interaction and candidate qualification remain open.

## Failure and correction

A complete hex editor opens an owned protected file and searches its actual overlay/reader while one provider call is held. A newer search finds fresh at byte 64, with its selection cursor at byte 68. Releasing the older canceled search replaces that success with Search stopped. Searches completed ordinarily, canceled explicitly or completed after closure retain their cancellation sources and leave the active-source field populated. The already closed editor suppresses rendered status changes; its original failure here is source retention.

The correction caches each valid search token, checks current source/reader ownership and closure before applying completion, limits cancellation status to current live demand, clears only its own active field and disposes its source after the task returns. Invalid hexadecimal input still allocates no search source. Search algorithms, protected-file writes and existing two-argument reader calls are unchanged. An active synchronous source call still returns before cancellation is reported.

## Controlled validation and provenance

Eight durable complete-editor headless controls cover replacement/closure with and without an old match, completion, explicit cancellation, invalid hexadecimal input and an ordinary live positive. The fixture retains the actual protected file, overlay and reader; it decorates one provider-call boundary to hold that call. Corrected original controls record six failures/two positive passes. Working and fresh locked committed runs each record 63 passes/two explicit existing Windows Posix skips across the same 65 affected viewer/picture/editor/admission/lifetime names. All eight new controls pass without skips. Six corrected held-call observations retain unchanged bytes, no active reads at completion, no source disposal during a read, current completion ownership and disposed finished search sources. Ordinary matches keep the exact inclusive-end cursor.

The independent seal verifies 37 retained files, 423 primary actual payload files plus 282 earlier oracle-stage payload files, all 1,080 original raw-source overlays and all 1,082 clean committed Git blobs/modes plus their archive. Working production/test canonical differences are CRLF only. Clean FileCat.dll SHA-256 0649b6ef31f3fb8a182d9022ac16db20ab10a1c7f0105f7660e6ef8a84852b18. Owned temporary fixtures are removed.

Earlier fixture/controller failures remain separate. Initial verification opens an owned protected file with insufficient sharing rights; the corrected read-only oracle permits the existing protected handle. A second oracle compares the whole composed status line with its message prefix; the corrected check preserves offset/selection detail. The working runner then rejects the two explicit pre-existing Windows Posix skips despite a successful test-command exit; independent raw-inventory checks retain 63 passes/two skips and all eight new passes. Sources, receipts, actual payloads and complete raw results remain retained; none of these controller/oracle outcomes is promoted to a product defect or silently omitted.

Original push CI [37570128387](https://github.com/benny-cz/FileCat/actions/runs/37570128387), attempt 1 at e9a9a98, is sealed green on policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26. All nineteen server digests/all members, fourteen complete TRX inventories, four compiler/tool receipts and 92 locked graphs verify. Each full 517-case App inventory equals the prior 509 names plus exactly eight hex-editor search additions; all 32 distinct new executions pass without skips, including 24 held-call observations using the actual protected file, overlay and reader. Core retains 890 Windows/885 Unix names; I163–I178 subsets retain exact outcomes and explicit skips. ARM64 package version startup, headless drawing and installer compilation pass; tagged/manual package and draft jobs skip. No selected shipping artifact or candidate is produced. Component mechanics do not establish native desktop, human UX, physical-source, installed-candidate or reference-performance qualification. Earlier rebuilt-artifact evidence keeps its own exact producer. No Mac/VM settings, physical-source, contract, candidate or publication changes occur.

Private `FileCatReleaseEvidence/hs179-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v3/command.json | 194f77b721ff4762349fbb8f7f93c8ada80aec699e7f2611ed82372869722474 |
| baseline-v3/results/baseline.trx | 5a457924e7b27da37cd5279614450bdaecc6a1210f86846712824cd7bee2732d |
| working-v4/command.json | 52ff8601c147fa82f299ce9621c848449eb74dc724f494821433c4803eace110 |
| working-v4/results/baseline.trx | 57c8f54214f6802d85e216d771abdd06cc4f388d1460af86a151eaf386c252cc |
| independent-working-v6.json | 6496a0df4b2b407e7d26d6c8dca12333efcc1bdd068ea482ecae639daf8fb14e |
| clean-v7/command.json | a3d1224d4e072af7c8e03897c64a775c1c0bb2432140cbafd7fbda31ab38c322 |
| clean-v7/results/clean.trx | 62e4e84bb13765a1a9e8e58359ae3ec904c416b2a440ec178164baf50e07330c |
| independent-hex-editor-search-v8.json | 9cc3598c1d2fcdb54c864a38d971988533715843ca63f4acd9c5af11be43db52 |

Private `FileCatReleaseEvidence/ci-37570128387-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 36a31536aab86afece292813c97e59824cd1404e25bbb6c2b69e038e7662a252 |
| independent-draft-guard-ci-v1.json | 9683b549e756290bff216100a7b210a4df2cd61aae18ba447eb8bc0e358a16d5 |
| independent-fixture-ci-v1.json | 66ff34043dfd9ae4ab7c88afebe259297782e22539dba11e86b900f6a5dcca30 |
| independent-i163-ci-cases-v1.json | 84a9ec411441ebc4149b3f241e40b333d7ab462ac77154467d59487a8e3b5112 |
| independent-i164-ci-cases-v1.json | 2cdb4e234105f5ef1de2e33f0afcba01dbbc862c743daaa2b9ff297bad65d00f |
| independent-i165-ci-cases-v1.json | 16c673e39ea351cf24dd122b60708a5f260196d7313de8fbc22a1d6e83c4f06c |
| independent-i166-ci-cases-v1.json | 67b9bf5237f06d30d4815811b860130f2e912428d39adf3ac64eff1c62a8f975 |
| independent-i167-ci-cases-v1.json | ca57fd108638e18ab1de6f02486dc4a1c7660fadc16047f7bd5b2572197ba3a4 |
| independent-i168-ci-cases-v1.json | 3801f1ebc05f0539f0079e5f4415227f4e1d5f949ee4e0efa575de1dafa19449 |
| independent-i169-ci-cases-v1.json | 55e43e9d77a1769ea56298b3ad0fd196ae3fcb9c3b2e02814c31ac8dd200921d |
| independent-i170-ci-cases-v1.json | 74856dd4dbed7615b91cad6ed9670a65dafddb98727468f199512a7cad40d964 |
| independent-i171-ci-cases-v1.json | 1c47d2be4509b46691eec6c4231991783c39e9e044c5016159913e9a520b7247 |
| independent-i172-ci-cases-v1.json | a0ea92f828ead9395d92dbfe40b7b3d3126d66c8a2ed91fa31a69fb7b9027992 |
| independent-i173-ci-cases-v1.json | 4a11bfd11593ca4827d45f227d7a4b1c633e0ac2d7190aa39b2923b20d139f0a |
| independent-i174-ci-cases-v1.json | 53ace018d4762b18599fac36e2b8758a0c5d9b1195474ecd3f815b1ecb66fca6 |
| independent-i175-ci-cases-v1.json | 081eee5ed375816cc3553cf92179d2ccfcb6c640b81e18c3f24bf6ebead957a0 |
| independent-i176-ci-cases-v1.json | dd3efa1baca8ef6cb1edcfc039cf582e2adbf12ff5af32354ac59435ebe9c3d4 |
| independent-i177-ci-cases-v1.json | 285031fbb149bed18dfbbd4a42c6ccf1bb91d50dbf385e2db26681409fb1a61e |
| independent-i178-ci-cases-v1.json | 4142d5a2a00202c8ec8ed23e69e749b9edaed26e991a78129c9f3b829236cc80 |
| independent-i179-ci-cases-v1.json | 4ae9501706f4829b36a2f0ab0b92c5178671d6ae75818c86c5d739945318b14b |
| independent-producer-policy-ci-v1.json | 8f01e313941b811e325afa17f8285d3007124b313af89d5be19dcb38381e3c6b |
| independent-restore-ci-v1.json | 8fc18e0cd38d86ed44f108d7515bc08e4e34db5e66aee71fc12286a554c64339 |
| independent-separation-ci-v1.json | 8f4b9010885a4cb493b7c5190497ace55d44bace5f030fd3e77155dea62a6009 |
