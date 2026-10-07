# E-I180 — report reads own current live completion

2026-10-07. Discovered against complete canonical product 4f25c5802a70f9f705c417d7e58878e9381e725d; canonical ReportWindow bytes are independently unchanged at intervening e9a9a98. Medium incorrect-report and resource-lifetime defect; must fix under I06/V10/V12. Remediated preliminarily at 90f2bebbee314be0c54e5a07ba5c4b5f85d7d1d9. Wider consumer/frame/search lifetimes, native interaction and candidate qualification remain open.

## Failure and correction

A report producer that returns after cancellation can replace a newer refreshed report with old text and replace its reader. Returning after the window closes changes the closed report and creates a new undisposed reader. Completed reads also retain their cancellation sources and active-source field. Three adverse original controls fail; ordinary successful and explanatory-failure controls pass.

The correction caches each linked token, checks cancellation, closure and current source ownership before applying text, clears only its own active source and disposes it after the producer task returns. Only current live completion re-enables refresh. A synchronous or uncooperative producer still returns before cancellation completes. Report content, refresh-place preservation, ordinary error explanations and search algorithms are unchanged; remaining report-search ownership is a separate executable review.

## Controlled validation and provenance

Five durable headless controls use a complete actual ReportWindow. Three hold its producer across refresh, closure or ordinary completion, then release it to read an actual owned report file. Two ordinary controls preserve successful content and the refusal explanation. The original full-product baseline records three failures/two passes. Working and fresh locked committed runs each pass the same thirteen affected report/text-viewer cases without skips, including all five new controls. Corrected replacement/closure observations preserve current text, status and reader identity; closure keeps its reader disposed. Finished linked sources are disposed and cleared; both owned file hashes and fixture cleanup verify.

The independent seal verifies 25 retained files, 423 actual test-payload pins, all 1,080 original raw-source overlays, and all 1,084 clean committed Git blobs/modes plus their archive. Canonical working/committed differences are CRLF only. Clean FileCat.dll SHA-256 c09e83825fec2b5df09ca3c84d7f766a7c30f08621606504e3886241f9f0e3e9. The earlier private verifier expected fourteen cases rather than the actual thirteen; its source and failed assertion remain retained. A fresh independent verifier checks the actual raw inventories without rerunning unchanged private controls. No product failure is inferred from that verifier mistake. The original CI collector then stopped after twelve successful seals because a count substitution changed historical run 37545177481 to nonexistent 37545227481. Its original source remains unchanged; a separate continuation rechecks all nineteen artifacts/fourteen raw inventories, completes only the remaining comparisons and verifies all twelve earlier seals unchanged. No CI or product test is rerun. First continuation proof SHA-256 4796d4e7583fa97d1a8b220b0f5d3103abfd8e7d81e7379e34f8e5f8108620bb remains retained. Audit v62b rejects its tail-source metadata because the executed comparison assigned the same script variable to the final case-proof path. Corrected metadata v3 independently rechecks unchanged original seals, native archive members and raw inventories, pins the actual tail source explicitly, and preserves the earlier metadata error; SHA-256 7237fabc2f21b03d195610c85a463d21e9f34f9593ee4dfad923174e57241d77. The CI/test results and artifact identities stay unchanged.

Original push CI [37571853937](https://github.com/benny-cz/FileCat/actions/runs/37571853937), attempt 1 at 90f2beb, is sealed green on policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26. All nineteen server digests/all members, fourteen complete TRX inventories, four compiler/tool receipts and 92 locked graphs verify. Each full 522-case App inventory equals the prior 517 names plus exactly five report-read additions; all 20 distinct new executions pass without skips, including twelve held-producer completion observations. Core retains 890 Windows/885 Unix names; I163–I179 subsets retain exact outcomes and explicit skips. ARM64 package version startup, headless drawing and installer compilation pass; tagged/manual package and draft jobs skip. No selected shipping artifact or candidate is produced. Component mechanics do not establish native desktop, human UX, physical-source, installed-candidate or reference-performance qualification. Earlier artifacts retain their exact producer. No Mac/VM setting, physical source, contract, candidate or publication changes occur.

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

Private `FileCatReleaseEvidence/ci-37571853937-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 0bac8558a7653b7e240fe6541e98fc8ba4ae4fdca7d9d7bde51952ce7353bb63 |
| independent-draft-guard-ci-v1.json | 84b9016e5d0eaca41f9ee3adfcc8627643895ce52795527ad8c7ef781859359f |
| independent-fixture-ci-v1.json | 20acf643c7753489918d24a87e3a9894642cf92ecb3a07e47c02208cc02ca59a |
| independent-i163-ci-cases-v1.json | 46c77eb5dfc1e152aac23c3e5841ad53a1c9dd71a6f4fd8b40a63982ccc48646 |
| independent-i164-ci-cases-v1.json | ede012530e5258ee6ad6e835f094228b796c0d8ae704a1dc0bd32f55a1fc417f |
| independent-i165-ci-cases-v1.json | b88edcff6f110df14205cdc0e5fdd1f8dcbc25dc1711c4c866bd6edd2dba2cff |
| independent-i166-ci-cases-v1.json | f442e67f31edef632c90a115614a6f32873a78ad3f6db896c0d2e4a5a664b86d |
| independent-i167-ci-cases-v1.json | 9e82b9cad77aa62fa655ed172333032817968f55e24df6e0268ef041571fd23d |
| independent-i168-ci-cases-v1.json | 042d266bf19b8fa17677cf3d4a4507c5b2b275e199c64c0b32c86aab51296257 |
| independent-i169-ci-cases-v1.json | b71eaebadb604cce8a82294287600f00a385478fcf2b0d8397ca056c141edb0b |
| independent-i170-ci-cases-v1.json | 5acd2b70c13509f7f30278f65506d2a0ee24eb85c1278e299c68b5599021701f |
| independent-i171-ci-cases-v1.json | 2b1bc75db489512cd1f0014ba81ca1393718237339463e8378f7e032ca349249 |
| independent-i172-ci-cases-v1.json | a79b6734f95fcc688fe51117418ca496c7908123c4430c5f06b02f040dadbc41 |
| independent-i173-ci-cases-v1.json | a825145b028b0c34977b9ea612bf99f79cc1a4d4b4c599ae3c52f992410fd492 |
| independent-i174-ci-cases-v1.json | b7e492b5760291fccecee72e56e0c6f69170e242be8d1898a6c59f3374725702 |
| independent-i175-ci-cases-v1.json | 3d2a06841a941c46c9b277fd5aceee0450ed1329d03940448e905b9bccfb0378 |
| independent-i176-ci-cases-v1.json | 87c46610145c9de273ed4875d700ebb8971da737263b3d82a353530c1b792b33 |
| independent-i177-ci-cases-v1.json | cc14409ea9ec3705a3d3972b910466de341475bfef23a70a51c837d9d6286c09 |
| independent-i178-ci-cases-v1.json | dc8b93d1962fab6f419d893d3dbe00a549d1e69f754f87c361afedf01c34041d |
| independent-i179-ci-cases-v1.json | eb349513d00494da53e062f45ffcb18c3b26b57f2e3ab2449274f943bc9f14f3 |
| independent-i180-ci-cases-v1.json | 4871e8e87d74d05c6e741a976595acb96bf85ef9f84a15ec631a5e1036f50180 |
| independent-producer-policy-ci-v1.json | e47279093bc392a4340c2db91312de1d356fdee15a0baa803357ee92aaff9873 |
| independent-restore-ci-v1.json | 63f8cb204d952db8f0879b7ae007956158b76acded1ba9d63c3ef4ad2dd0d10e |
| independent-separation-ci-v1.json | d92b4eb9e43b7f2b9759c0d799acee49f8f70d897e444329ddefb36b6489a718 |
