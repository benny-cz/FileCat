# E-I180 — report reads own current live completion

2026-10-07. Discovered against complete canonical product 4f25c5802a70f9f705c417d7e58878e9381e725d; canonical ReportWindow bytes are independently unchanged at intervening e9a9a98. Medium incorrect-report and resource-lifetime defect; must fix under I06/V10/V12. Remediated preliminarily at 90f2bebbee314be0c54e5a07ba5c4b5f85d7d1d9. Wider consumer/frame/search lifetimes, native interaction and candidate qualification remain open.

## Failure and correction

A report producer that returns after cancellation can replace a newer refreshed report with old text and replace its reader. Returning after the window closes changes the closed report and creates a new undisposed reader. Completed reads also retain their cancellation sources and active-source field. Three adverse original controls fail; ordinary successful and explanatory-failure controls pass.

The correction caches each linked token, checks cancellation, closure and current source ownership before applying text, clears only its own active source and disposes it after the producer task returns. Only current live completion re-enables refresh. A synchronous or uncooperative producer still returns before cancellation completes. Report content, refresh-place preservation, ordinary error explanations and search algorithms are unchanged; remaining report-search ownership is a separate executable review.

## Controlled validation and provenance

Five durable headless controls use a complete actual ReportWindow. Three hold its producer across refresh, closure or ordinary completion, then release it to read an actual owned report file. Two ordinary controls preserve successful content and the refusal explanation. The original full-product baseline records three failures/two passes. Working and fresh locked committed runs each pass the same thirteen affected report/text-viewer cases without skips, including all five new controls. Corrected replacement/closure observations preserve current text, status and reader identity; closure keeps its reader disposed. Finished linked sources are disposed and cleared; both owned file hashes and fixture cleanup verify.

The independent seal verifies 25 retained files, 423 actual test-payload pins, all 1,080 original raw-source overlays, and all 1,084 clean committed Git blobs/modes plus their archive. Canonical working/committed differences are CRLF only. Clean FileCat.dll SHA-256 c09e83825fec2b5df09ca3c84d7f766a7c30f08621606504e3886241f9f0e3e9. The earlier private verifier expected fourteen cases rather than the actual thirteen; its source and failed assertion remain retained. A fresh independent verifier checks the actual raw inventories without rerunning unchanged private controls. No product failure is inferred from that verifier mistake.

Original push CI [37571853937](https://github.com/benny-cz/FileCat/actions/runs/37571853937), attempt 1 at 90f2beb, remains pending at this local seal. Component mechanics do not establish native desktop, human UX, physical-source, installed-candidate or reference-performance qualification. Earlier artifacts retain their exact producer. No Mac/VM setting, physical source, contract, candidate or publication changes occur.

Private `FileCatReleaseEvidence/rl180-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | 509e8e161b52dec055c249d97493ad41323cb0cd9c4e117934a0786d30b0a3ee |
| baseline-v1/results/baseline.trx | 60f8767c51fa7fa38e4790dfbb9f53bae4270947f0eac087fdca6e06093ddd9b |
| working-v2/command.json | f751bfaa25e146a0dd2c42ef65c4a3f2ab8ae929b33f99a3b71e2cd296b2f859 |
| working-v2/results/baseline.trx | 3264db4029352be1a47f06f872d6aac8761e1ac598226c67b73db40046b0e595 |
| independent-working-v4.json | 6af052b16a19b2a68097aeea99fc541713480fce1fd01b66695d66279371383f |
| clean-v5/command.json | a9e01d4f8b1a68713e4106e3eb026e5728d915ffc58fc1e6eb5e67fd0c1607b7 |
| clean-v5/results/clean.trx | 5d86495a7978672b52447ff8aebccbe3abc8f7bf75b7163d5a5359f7c301e662 |
| independent-report-read-v6.json | 6b315525274bcafb485dbda5f47029f97391c025a4716b2940dc23fb488689ac |
