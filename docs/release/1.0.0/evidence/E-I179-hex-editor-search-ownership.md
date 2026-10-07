# E-I179 — hex-editor search completion owns current demand

2026-10-07. Discovered against complete canonical product 4f25c5802a70f9f705c417d7e58878e9381e725d; intervening documentation commits leave this implementation unchanged. Medium result-status and resource-lifetime defect; must fix under I06/V04/V12/V13. Remediated preliminarily at e9a9a98f1fba2c646095a3c0c352002aa3ca354b. Wider worker/consumer/frame lifetimes, native interaction and candidate qualification remain open.

## Failure and correction

A complete hex editor opens an owned protected file and searches its actual overlay/reader while one provider call is held. A newer search finds fresh at byte 64, with its selection cursor at byte 68. Releasing the older canceled search replaces that success with Search stopped. Searches completed ordinarily, canceled explicitly or completed after closure retain their cancellation sources and leave the active-source field populated. The already closed editor suppresses rendered status changes; its original failure here is source retention.

The correction caches each valid search token, checks current source/reader ownership and closure before applying completion, limits cancellation status to current live demand, clears only its own active field and disposes its source after the task returns. Invalid hexadecimal input still allocates no search source. Search algorithms, protected-file writes and existing two-argument reader calls are unchanged. An active synchronous source call still returns before cancellation is reported.

## Controlled validation and provenance

Eight durable complete-editor headless controls cover replacement/closure with and without an old match, completion, explicit cancellation, invalid hexadecimal input and an ordinary live positive. The fixture retains the actual protected file, overlay and reader; it decorates one provider-call boundary to hold that call. Corrected original controls record six failures/two positive passes. Working and fresh locked committed runs each record 63 passes/two explicit existing Windows Posix skips across the same 65 affected viewer/picture/editor/admission/lifetime names. All eight new controls pass without skips. Six corrected held-call observations retain unchanged bytes, no active reads at completion, no source disposal during a read, current completion ownership and disposed finished search sources. Ordinary matches keep the exact inclusive-end cursor.

The independent seal verifies 37 retained files, 423 primary actual payload files plus 282 earlier oracle-stage payload files, all 1,080 original raw-source overlays and all 1,082 clean committed Git blobs/modes plus their archive. Working production/test canonical differences are CRLF only. Clean FileCat.dll SHA-256 0649b6ef31f3fb8a182d9022ac16db20ab10a1c7f0105f7660e6ef8a84852b18. Owned temporary fixtures are removed.

Earlier fixture/controller failures remain separate. Initial verification opens an owned protected file with insufficient sharing rights; the corrected read-only oracle permits the existing protected handle. A second oracle compares the whole composed status line with its message prefix; the corrected check preserves offset/selection detail. The working runner then rejects the two explicit pre-existing Windows Posix skips despite a successful test-command exit; independent raw-inventory checks retain 63 passes/two skips and all eight new passes. Sources, receipts, actual payloads and complete raw results remain retained; none of these controller/oracle outcomes is promoted to a product defect or silently omitted.

Original push CI [37570128387](https://github.com/benny-cz/FileCat/actions/runs/37570128387), attempt 1 at e9a9a98, remains pending at this local seal. Component mechanics do not establish native desktop, human UX, physical-source, installed-candidate or reference-performance qualification. Earlier rebuilt-artifact evidence keeps its own exact producer. No Mac/VM settings, physical-source, contract, candidate or publication changes occur.

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
