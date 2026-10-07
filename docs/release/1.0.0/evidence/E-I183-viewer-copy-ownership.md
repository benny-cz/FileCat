# E-I183 — viewer copy completion owns current live demand

2026-10-07. Original complete canonical product 576e55637b8f0a05bfe4ef963a9c13d2082d9afa. Medium invalidated-result and cancellation-lifetime defect; must fix under I06/V10/V11/V12. Remediated preliminarily at 25432ce5ac8c21cd069ad2e8a518bb2755e2280d; native/broader/candidate scope remains.

An old whole/range hex copy overwrites a newer 32-byte copy's clipboard and status. Closing while its real source read is held makes CopyAsync throw NullReferenceException when it later asks the closed headless window for its clipboard. Four original adverse controls fail; two ordinary copies pass. Actual native shutdown or desktop clipboard effects are not measured.

The correction snapshots the clipboard, mode and selected byte range while the window is live. Each copy owns a linked cancellation source, superseded by another copy and canceled by closure. Hex copying uses the actual PagedReader cancellation overload, keeping the synchronous source call alive until it returns and stopping before later pages. Hex conversion runs with background work; the existing 1 MiB copy limit and single-byte fallback remain. Only current live completion submits the clipboard operation or updates status; the cancellation source clears/disposes after completion. Text copying participates in the same completion ownership. Already submitted asynchronous platform clipboard operations are not recalled; native clipboard ordering remains wider qualification.

Six durable complete-viewer headless controls cover replace/close/ordinary completion for whole-file and 4096-byte selected copies. The actual FileContentSource/PagedReader stay in use, with one decorated provider boundary holding the read. A newer cached 32-byte copy finishes while the old call is held. Observations record clipboard string lengths and hashes, avoiding huge clipboard dumps; independent Python computes exact whole/range/new-copy hashes. Tests compare actual complete hex strings as well. Owned 262,144-byte sources stay unchanged, no source disposal occurs during reads, and canceled work starts no later source read. All corrected cases have no exception, dispose their cancellation source and clear current ownership.

Original: four adverse failures/two positives. Working and fresh locked committed builds each pass the same sixty-six affected viewer/picture/QuickView/admission cases without skips, exactly sixty prior cases plus six additions. All six additions pass. The independent seal checks 20 retained files, 423 actual payload file references, all 1,089 original canonical blobs/modes and all 1,091 clean committed blobs/modes plus every archive member. Working/committed differences are CRLF only. Clean FileCat.dll SHA-256 3f51c05e6860359cc3e7ddb926125807cbcc823a5b25f82a093d6b68fc360692. Owned fixtures clean up. No product/fixture preflight failure occurs in this slice.

Original push CI [37578155139](https://github.com/benny-cz/FileCat/actions/runs/37578155139), attempt 1 at 25432ce remains pending at this local seal. Headless clipboard mechanics do not qualify native desktop/shutdown, text/Info copy or single-byte/cap boundaries beyond this selected-range scope, source-revision races, provider errors, reference performance, physical sources, human UX or installed candidate behavior. No persistent machine, contract, candidate or publication changes.

Private `FileCatReleaseEvidence/cp183-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | 78af81e319807b88805e1ab7d285796e4dd2f8b19fe14889af8af3c1a50f9e80 |
| baseline-v1/results/baseline.trx | 7b1fceccd4908e0741f20f157563d20a59da5e815ccd079cd9560e2ae8e2c8d4 |
| working-v2/command.json | e995930a868d953c5d764a7619e5d5bcb98d37c591b07729f9651bdc93706a64 |
| working-v2/results/baseline.trx | 9b897268a06e0d23e2f1238f58901a3336f62a3d95ce1f8946d5a7f9b21f4147 |
| independent-working-v3.json | 539cf4aa5b5cd2a308c63bd89657b861fbac490a6f28fe736fd6fda1a598a14c |
| clean-v4/command.json | 7a4b7440b21cf287870f5d7f6205e03be8391c8e2a515c63f97ae1a3b20775e2 |
| clean-v4/results/clean.trx | 47b80d879b3b6465dc29f8ee914053666402b6a5a07d9ffb419f7789d535cda2 |
| independent-copy-v5.json | 7264997d73e66dc49c83a8c2c38f63480b9982ebdad3b4f48e28a89a3127c9ac |
