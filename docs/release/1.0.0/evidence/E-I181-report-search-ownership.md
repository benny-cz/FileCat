# E-I181 — report search owns current live completion

2026-10-07. Discovered against complete canonical product 90f2bebbee314be0c54e5a07ba5c4b5f85d7d1d9. Medium invalidated-result and cancellation-lifetime defect; must fix under I06/V10/V12/V13. Remediated preliminarily at fa13957ac8a34ac4075bfc7ae1da4d750f43e96c. Wider worker/frame/lifetime, native interaction and candidate qualification remain open.

## Failure and correction

A complete report searches its actual memory reader while one provider call is held. Releasing an older search replaces a newer hit and highlight, or changes refreshed report state with a hit from the previous text. Older misses overwrite current report status. Closing the report cancels the held search, but OperationCanceledException escapes FindAsync; actual native application shutdown/crash behavior is not measured here. Six adverse original controls fail and two live positives pass.

The correction gives each valid search an owned linked token and cancels previous demand on another search, refresh, text replacement or closure. Only completion that still owns the live report and exact reader can update the hit, highlight, scroll or status. Canceled demand returns without publishing a result; its source is cleared and disposed after the task returns. Empty search input allocates no source. Actual synchronous source calls still return before cancellation completes. Forward/backward search algorithms and report-read content/place/error behavior stay the same.

## Controlled validation and provenance

Eight durable complete-report headless controls cover replacement, refresh and closure with and without an old match, ordinary held completion and a live forward/backward positive. An actual owned UTF-8 report file produces the complete report; its actual MemoryContentSource/PagedReader remain, with one provider-call boundary decorated to hold the read. Six original failures/two positive passes are retained. Working and fresh locked committed runs each pass the same twenty-one affected report/text-viewer cases without skips, including all eight new controls and the five I180 read controls. Seven held-call observations preserve owned file hashes and current reader/text identity, retire active reads before completion and show no source disposal during a read. Superseded completion preserves current hit/status/highlight; current live completion finds byte 131082.

The independent seal verifies 40 retained files, 423 primary actual payload pins, 143 earlier preflight build files, all 1,084 original raw-source overlays and all 1,086 clean committed Git blobs/modes plus their archive. Canonical working/committed differences are CRLF only. Clean FileCat.dll SHA-256 adbc87162268d8f6d43e1c17a1838b89aa5efb73a904c5f4f4274046b05f4826. Owned fixtures are removed.

Earlier fixture failures remain distinct from product findings. The first two builds fail on an omitted namespace import and direct access to Core internal diagnostics; corrected controls use the existing reflection access pattern. A giant-line fixture then times out before a reliable result inventory exists. Its source, receipt, built bytes and diagnostics are preserved, and two owned temporary directories are archived with verified member hashes before removal. The short-line fixture isolates completion behavior and establishes the six actual failures. Giant-line layout is queued for a separate bounded review; no reference-performance conclusion is inferred from that fixture timeout.

Original push CI [37573707404](https://github.com/benny-cz/FileCat/actions/runs/37573707404), attempt 1 at fa13957 remains pending at this local seal. Headless mechanics do not establish native desktop, human UX, physical-source, installed-candidate or reference-performance qualification. Earlier artifacts retain their exact producer. No Mac/VM setting, physical source, contract, candidate or publication changes occur.

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
