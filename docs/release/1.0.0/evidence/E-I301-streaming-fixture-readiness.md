# E-I301 — streaming retirement controls observe published rows

2026-10-10 CEST. The first exact no-overlay check of **5363a8be838f877f3bd04690b4f904597120e5cf /1393 canonical blobs** retains **13 Core passes and one timeout**. The 65,536-row streaming control waited for its full requested count while deliberately keeping enumeration open. The timeout occurs at that readiness wait, before retirement or weak-reachability observations. It does not establish a FileCat product failure; no original partial-row count was logged.

The fixture now waits for a positive initial publication and captures the actual published count while the listing is still Loading. It retains the requested workload separately. Disposal must collect all five captured owners, keep reported rows/reservations zero and prevent late repopulation. Full-count completed controls and separately owned selection-lease controls remain. There is no production-code change or new skip.

The corrected **single test-file overlay** on those same 1393 canonical blobs passes **14 Core controls** (12 listing ownership plus two unchanged GnuPG short-root controls) and **50 affected App controls** (panel/tab/QuickView/folder-count/workspace lifetimes). All actual TRX rows, output, commands, payloads and source inputs are freshly checked. Streaming observations publish 16/16, 4096/4096 and **1024/65536** rows respectively; every captured owner then collects, with zero final rows/reservations. The smaller count is a recorded streaming observation, not full-scale completion evidence.

The original exact-source timeout remains adverse evidence. Earlier full Core's long-TEMP GnuPG failure remains a distinct fixture limit. This correction does not rerun or relabel those results. Committed 7bdaa89 no-overlay 14 Core/50 App and [four affected Windows/Ubuntu native cases](E-I06-current-native-retirement.md) now pass. [Original 5363a8b hosted CI](E-CI-listing-streaming-readiness.md) retains 47 new ownership passes/one Ubuntu readiness timeout. Corrected hosted qualification remains; I06, the physical-source HOLD, all owner/freeze/candidate/publication gates and NO-GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute; nested records retain payload/source and raw results.

| File | SHA256 |
|---|---|
| `i301-fixture-20261010-v1/independent-fixture-final-v1.json` | `840a6d04f824d4899bffcb248abff4d8eb5bb0e27b4d976069ad6e108c497ade` |
| `E:/FileCat/artifacts/release-evidence/i301-fixture-20261010-v1/seal.py` | `3b5c738017dc6e3ab5e1c9d66530f37af517109723b73c78bc6e08e9b2fd2692` |
| `run-i301-fixture-20261010-v1.py` | `4e38593239eeb9fdd7c8cad3f6e1aa3500fce90210cac21423a68d137971dd46` |
| `run-i300-committed-20261010-v1.py` | `359e4035df65ca98a829f7607ed53eff286ff2716abfdcc7cd3441d039ab2658` |
| `E:/FileCat/artifacts/release-evidence/i301-fixture-20261010-v1/fixed/inputs.json` | `4ab66cbb220032e8f5171475b4b3e7e3f0fd675d288bf9f1c021a758b113bfd9` |
| `E:/FileCat/artifacts/release-evidence/i301-fixture-20261010-v1/fixed/source.zip` | `4894f34dd80b8ea3367010be42c8051148adf3007ed154e2244d90ddf7a38b00` |
| `i301-fixture-20261010-v1/original-test.cs` | `348d6b6986bdfb1fda19be22da63a87c422c28d10c2ec1e18040b96abde8035f` |
| `E:/FileCat/artifacts/release-evidence/i301-fixture-20261010-v1/fixed/batch-final-v1.json` | `db68e49f684950e0859f4db1468e3c4c5ff49835426ea937074e5b3c0f178ae5` |
| `E:/FileCat/artifacts/release-evidence/i301-fixture-20261010-v1/fixed/source/tests/FileCat.Core.Tests/ListingDisposeOwnershipTests.cs` | `dd5ca3f7cef63547666ceed09512cf04f9901be0f3f88eb97a50c5915c62f501` |
| `E:/FileCat/artifacts/release-evidence/i300-committed-20261010-v1/committed/inputs.json` | `f407ab5727856e1c8d7ce4bd0866ec92e0f38991d169ffd3f5f9ca8ad8b9bd19` |
| `E:/FileCat/artifacts/release-evidence/i300-committed-20261010-v1/committed/core-controls/command.json` | `ce3fcff16b1d16a29d61d529a14e30d397aa58ad8f6af3beb55551d63370986c` |
| `E:/FileCat/artifacts/release-evidence/i300-committed-20261010-v1/committed/core-controls/results/results.trx` | `f6a19f12e780112b91763aeab38e1a24d919e086989e5f9ba4960e5f9fa7539a` |
| `E:/FileCat/artifacts/release-evidence/i300-committed-20261010-v1/committed/core-controls/stdout.txt` | `d019bd8c1a6735337218625055a91acb0460b9a56ec779f04c7d99767ee18b85` |
| `E:/FileCat/artifacts/release-evidence/i300-committed-20261010-v1/committed/core-controls/stderr.txt` | `8f64742b3b9eed075ed91dc3617c073bc92366fc3e8df7ea53300a0706e6129f` |
| `E:/FileCat/artifacts/release-evidence/i301-fixture-20261010-v1/fixed/core-controls/command.json` | `ffbabfa036bcc8e111bdc73f2eb1c7beb6ad5b334563ba10372c906f101e4515` |
| `E:/FileCat/artifacts/release-evidence/i301-fixture-20261010-v1/fixed/core-controls/results/results.trx` | `236e59047d76072961c97bd2464fe9ff36a527360c345d9b1430573e66ef20b7` |
| `E:/FileCat/artifacts/release-evidence/i301-fixture-20261010-v1/fixed/core-controls/stdout.txt` | `21c47d9a78a5e25d0751d5fd28c5d56572818352b1dcafe48f15926c47a06aca` |
| `E:/FileCat/artifacts/release-evidence/i301-fixture-20261010-v1/fixed/core-controls/stderr.txt` | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `E:/FileCat/artifacts/release-evidence/i301-fixture-20261010-v1/fixed/app-controls/command.json` | `0f384af849be5578f85b8422df9656587143972a21144ee4063bd1b668d62fa1` |
| `E:/FileCat/artifacts/release-evidence/i301-fixture-20261010-v1/fixed/app-controls/results/results.trx` | `6e8b1f00a02f3ba7b446cf301c78d79c2fefcddf0bad752c80f346dd92197027` |
| `E:/FileCat/artifacts/release-evidence/i301-fixture-20261010-v1/fixed/app-controls/stdout.txt` | `8a211af08e5437a1c4a72ec29ee74d5c203f7767d71e99c3686a98779d79bf35` |
| `E:/FileCat/artifacts/release-evidence/i301-fixture-20261010-v1/fixed/app-controls/stderr.txt` | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
