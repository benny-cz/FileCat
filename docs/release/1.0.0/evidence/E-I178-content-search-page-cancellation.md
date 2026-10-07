# E-I178 — cancel content search between actual page calls

2026-10-07. Discovered against complete canonical product 9f5f135c511b6a2bf5c70a24f11ae1fd2446047b; intervening documentation commits leave this implementation unchanged. Medium provider-demand and cancellation-truth defect; must fix under I06/V12/V13. Remediated preliminarily at 4f25c5802a70f9f705c417d7e58878e9381e725d. Broader consumer/worker/frame lifetimes, native interaction and candidate qualification remain open.

## Failure and correction

Forward byte, backward byte and text searches check cancellation before a one-megabyte blocking reader call. That call can load multiple 64 KiB pages without observing the search token. In each owned 256 KiB control, cancellation while the first actual source read is held still allows three more source reads and returns a match at byte 64.

A cancellation-aware PagedReader overload now checks the token before each page call and immediately after it returns; all three search modes pass their token. The existing two-argument public reader API remains available. An active synchronous source read returns normally before cancellation is reported; this does not claim interrupting a source mid-call or change the search algorithm.

## Controlled validation and provenance

Six durable Core controls use an owned real file: canceled and ordinary positive searches in each of the three modes. Original source records three adverse failures/three positive passes. Working and fresh locked committed runs each pass all 46 affected content/search/page-reader controls without skips. Both working and fresh App runs pass the same 54 affected viewer/picture/admission/lifetime cases without skips. Corrected cancellation makes exactly one actual source read, reports OperationCanceledException and returns no match; ordinary searches retain four reads and the exact match at byte 64. Structured observations retain unchanged file hashes, no active reads at completion and no source disposal during a read; owned temporary fixtures are removed.

The independent seal verifies 36 retained files, 528 actual test payload files, all 1,078 original raw-source overlays and all 1,080 fresh committed Git blobs/modes plus their archive. Working production and test overlays differ from canonical bytes only by Git CRLF normalization. Clean FileCat.dll SHA-256 722a5a9a6c52ab8b38af2e987b364e0ec5b6c85f43c79c0a8dcd62fc960dc196; clean FileCat.Core.dll SHA-256 c3b1d3778ee5128c5e3fe3afe92ed941171c3190e5cc0e3aa496aa1b545bb2fc.

Original push CI [37568308393](https://github.com/benny-cz/FileCat/actions/runs/37568308393), attempt 1 at 4f25c58, remains pending at this local seal. Component controls do not establish native desktop, human UX, physical-source, installed-candidate or reference-performance qualification. Prior rebuilt-artifact evidence keeps its own exact producer. No Mac/VM settings, physical-source, contract, candidate or publication changes occur.

Private `FileCatReleaseEvidence/content-search-page-stop-20261007-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | 842c8f0be34890b293c09a53f4f52efed54de93908f196478702ee84f848bdba |
| baseline-v1/results/baseline.trx | 1eb471fc2da03dd953c8da8f695b446369bc3d0b155d5b82b20b5fb20a3d9da3 |
| working-v2/command.json | 507053295387d86aafd6af0b6c3b35365f93751e1d99b42eaa8f5de42b38d454 |
| working-v2/results/baseline.trx | 56c24df317a594217d2726faf7545655fb6cfec2bcb7a9247a9ffd04946255e0 |
| independent-working-v3.json | ffc5d3f186cf7f9900cf232676060422e8a3e8f8a78d1618a21e222a4161c901 |
| independent-app-working-v5.json | 5b8096f8235a38678434cf794d24b1f5bac83bb7d79910d275a63e5ebb3f92d0 |
| independent-content-search-v7.json | 0d0524288d07afdb74ece5ca5fef61531add3eabb2284507a9b9d50c8d213cfa |

Private `FileCatReleaseEvidence/cs178-app-v4`:

| Retained path | SHA-256 |
|---|---|
| working-v4/command.json | e7a29d56e96daeff1f696136bc0a88277e19714fe6f69378107054c9425f3d66 |
| working-v4/results/baseline.trx | 821c9d04b053dfcb08ba268d4b8b8c0a9163bf3b51506bc645a526ebe7696130 |

Private `FileCatReleaseEvidence/cs178-clean-v6`:

| Retained path | SHA-256 |
|---|---|
| clean-v6/core/command.json | 42df5f8f14bae558bc7360c9a164ea08d41be609470dd685c69d9aaf16c68dc9 |
| clean-v6/core/results/clean.trx | 0458cdeee7010cd0cbc51791ae3530a1e54d1f163fc1c09642aa7f1ade4a2d52 |
| clean-v6/app/command.json | 2ff1b3b754255ce6e7900dac02a3c424c1baeb07891f516bf626f7e2f6fffe18 |
| clean-v6/app/results/clean.trx | 518432e9284c72523a84c3ed6bd8ad8bd4dfa2d551924bf3a0c88a6e5ebbefde |

