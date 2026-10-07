# E-I181 — report search owns current live completion

2026-10-07. Discovered against complete canonical product 90f2bebbee314be0c54e5a07ba5c4b5f85d7d1d9. Medium invalidated-result and cancellation-lifetime defect; must fix under I06/V10/V12/V13. Remediated preliminarily at fa13957ac8a34ac4075bfc7ae1da4d750f43e96c. Wider worker/frame/lifetime, native interaction and candidate qualification remain open.

## Failure and correction

A complete report searches its actual memory reader while one provider call is held. Releasing an older search replaces a newer hit and highlight, or changes refreshed report state with a hit from the previous text. Older misses overwrite current report status. Closing the report cancels the held search, but OperationCanceledException escapes FindAsync; actual native application shutdown/crash behavior is not measured here. Six adverse original controls fail and two live positives pass.

The correction gives each valid search an owned linked token and cancels previous demand on another search, refresh, text replacement or closure. Only completion that still owns the live report and exact reader can update the hit, highlight, scroll or status. Canceled demand returns without publishing a result; its source is cleared and disposed after the task returns. Empty search input allocates no source. Actual synchronous source calls still return before cancellation completes. Forward/backward search algorithms and report-read content/place/error behavior stay the same.

## Controlled validation and provenance

Eight durable complete-report headless controls cover replacement, refresh and closure with and without an old match, ordinary held completion and a live forward/backward positive. An actual owned UTF-8 report file produces the complete report; its actual MemoryContentSource/PagedReader remain, with one provider-call boundary decorated to hold the read. Six original failures/two positive passes are retained. Working and fresh locked committed runs each pass the same twenty-one affected report/text-viewer cases without skips, including all eight new controls and the five I180 read controls. Seven held-call observations preserve owned file hashes and current reader/text identity, retire active reads before completion and show no source disposal during a read. Superseded completion preserves current hit/status/highlight; current live completion finds byte 131082.

The independent seal verifies 40 retained files, 423 primary actual payload pins, 143 earlier preflight build files, all 1,084 original raw-source overlays and all 1,086 clean committed Git blobs/modes plus their archive. Canonical working/committed differences are CRLF only. Clean FileCat.dll SHA-256 adbc87162268d8f6d43e1c17a1838b89aa5efb73a904c5f4f4274046b05f4826. Owned fixtures are removed.

Earlier fixture failures remain distinct from product findings. The first two builds fail on an omitted namespace import and direct access to Core internal diagnostics; corrected controls use the existing reflection access pattern. A giant-line fixture then times out before a reliable result inventory exists. Its source, receipt, built bytes and diagnostics are preserved, and two owned temporary directories are archived with verified member hashes before removal. The short-line fixture isolates completion behavior and establishes the six actual failures. Giant-line layout is queued for a separate bounded review; no reference-performance conclusion is inferred from that fixture timeout.

Original push CI [37573707404](https://github.com/benny-cz/FileCat/actions/runs/37573707404), attempt 1 at fa13957, is sealed green on policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26. All nineteen server digests/all members, fourteen complete TRX inventories, four compiler/tool receipts and 92 locked graphs verify. Each full 530-case App inventory equals the prior 522 names plus exactly eight report-search additions; all 32 distinct new executions pass without skips, including 28 actual report/memory-reader held-call observations. Core retains 890 Windows/885 Unix names; I163–I180 subsets retain exact outcomes and explicit skips. ARM64 package version startup, headless drawing and installer compilation pass; tagged/manual package and draft jobs skip. No selected shipping artifact or candidate is produced. Headless mechanics do not establish native desktop, human UX, physical-source, installed-candidate or reference-performance qualification. Earlier artifacts retain their exact producer. No Mac/VM setting, physical source, contract, candidate or publication changes occur.

Private `FileCatReleaseEvidence/rs181-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v5/command.json | a029ccafdc2d71eb8a5f42da44ed6f5dc6e5c9d37fdfe8bd7dc9c3cdc40c357d |
| baseline-v5/results/baseline.trx | 0f9ba61362727bba5f54333943472e46592ddf872112fb9bab35ce8310602484 |
| working-v6/command.json | 7e9a27f4f12d6727abb3956efc953df26553a315f3b49a492fb3874f8f188ac6 |
| working-v6/results/baseline.trx | 52b36d522a246dd716c0537a002179df15fb150a894105e0a8d161dd963ce100 |
| independent-working-v7.json | 0f1cc0a81977b62d8df8dddd607e793356b72d9a20799fa745e40460def405c4 |
| clean-v8/command.json | 65ccf0a2d0167954c717251b9f661e0eacbc9d0ccf4036a57d99b5c2cd1e8cd7 |
| clean-v8/results/clean.trx | 4483322989ff031330b2ae3e3b0bdb34a3c052284a163f6d605e5c8d51e33a92 |
| independent-report-search-v9.json | 18f24814837b5661331fd642311ff3ee63c8639840f642cc9e2d9fa63fe5d633 |

Private `FileCatReleaseEvidence/ci-37573707404-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | de6546f2dc5a54151d8eb1de58534bbbe81bd958067fcb1dbb0a0840198bdc61 |
| independent-draft-guard-ci-v1.json | 0c1e25abc7c3de6f3c5239300273856badadf6deac2d2a195de2ef6ba5f9ee6e |
| independent-fixture-ci-v1.json | c3c9a53d84d71572801fb1cb9a684d4f901e034c3012e887e6c9c4232202a3e1 |
| independent-i163-ci-cases-v1.json | 8b27b04d493202ede158d3336cf3d17c4b160c3295a93a1d3947a724a227c5e8 |
| independent-i164-ci-cases-v1.json | 0ba29bd779b81bcc4c73b0774b25304cf250a6e48792877ffdfd38122da95b92 |
| independent-i165-ci-cases-v1.json | ad52b72bf0da1342f8ab3c224dd4ea6c0a8cba83920f9b1878207e43befd7a05 |
| independent-i166-ci-cases-v1.json | d08aa724fac23fc2e35f5ac9c4e9c3f4de076bb35049f2cc69713b8b109b71eb |
| independent-i167-ci-cases-v1.json | bd478116cf54d0ed27f6253eacce9761ff879a8e605b12cf160c0be0b8322fd5 |
| independent-i168-ci-cases-v1.json | 3c06623f2aacfe2591fab5cf54ab13a3f5752986d1140378944604b528919357 |
| independent-i169-ci-cases-v1.json | b6fea46e34d6cea1990791247441b2094a129411e4ebe7ba554356b79e248211 |
| independent-i170-ci-cases-v1.json | 73bfc7b02e25ebd57e3562cc12e7371ee7e534cfeeb472f2ff301790e751bcfd |
| independent-i171-ci-cases-v1.json | 461f5373c3080263f81cb3b81e9bb6c893ffff4cc6f458db60bc09c38ade6e41 |
| independent-i172-ci-cases-v1.json | 7598d8c663c0a5045f53f2bf9fc047c8abad78c9de474fdd97b7d5710922c638 |
| independent-i173-ci-cases-v1.json | 066c9432e3da48a9e73a353ee6cbb2fa6317f625247bc046f1e160af114e90d0 |
| independent-i174-ci-cases-v1.json | 953ea9d827effc349f93d4dca0c84acdada07b931c55a0d792dfc4ae4beaf4d2 |
| independent-i175-ci-cases-v1.json | d400b60ff9c3dd35369d055d27778145c8a9731a4d0e2e84beff2ae7bc6b100c |
| independent-i176-ci-cases-v1.json | 5b357b32a17da0e224e686b2787180d538fe09c2644a8a731295962cbc6cecf3 |
| independent-i177-ci-cases-v1.json | c59110b75988ae1d1dc23cb57f3877d0defaa743eb0359b5862388d33b7a0e18 |
| independent-i178-ci-cases-v1.json | b7d7099e2091eaa45bb8be359a69317c3daaf0b5976784b5633f5f416e9c2f66 |
| independent-i179-ci-cases-v1.json | 7d431ff5e0eb233566e7b89f124dfc0228f898dfb2c6acc7337d0fc002b10f04 |
| independent-i180-ci-cases-v1.json | 0a59bd4ff2837c271bca6fb31e89f71f57e8f29c7f70fe05760d48c20dba4564 |
| independent-i181-ci-cases-v1.json | 35a802984adb6a357a1cf95c3fdc386b216ba4451920dbafc973ac0c86040254 |
| independent-producer-policy-ci-v1.json | 7b0b3e58a64a6794e08619bdcc371c399a55de28b43223a50394f68c0275a8c7 |
| independent-restore-ci-v1.json | 26bbe65b074d1f8accb2b053f430606fb1e81a5c0989b7e5a16da3f701922e2c |
| independent-separation-ci-v1.json | e067d06d2983fca337267b6f939e3157c0fe3f970996f8520dd998de293d3f4d |
