# E-I284 — exclude link metadata bytes from discovered content work

2026-10-09. Original canonical main **c7416b6aa4e6ab087f540d9687d3db58008f1638**, with exact fixture/production overlays. Transfer root discovery counts a link's own metadata size as content work, unlike child enumeration and admission, which count zero for a literal link. Totals take the maximum of discovered and admitted work, rather than adding both. A link metadata size larger than the followed contents therefore inflates progress even when bytes and publication are correct. Larger referents can mask it. The correction counts zero discovered content bytes for link roots; explicitly followed ordinary contents still add their admitted bytes.

Sixty-four cases use actual owned files and symbolic links through JobManager/portable operations: 17/1,048,577-byte contents, Copy/Move, Native/ReadBack verification, early/late discovery, and literal/follow/skip/ordinary modes. Root link metadata is deliberately fixed at 83 bytes; bounded gates control discovery/execution ordering. Original **40 failures/24 passes** become **64 passes**: 32 literal/skip and eight small-follow failures; sixteen ordinary and eight large-follow healthy controls. Exact item/content/verification/work totals, transferred bytes, source/referent/target hashes, link identity, decisions and source retirement verify. All 192 original/fixed/full-Core fixture sets are absent.

The full combined Core suite passes **3373/64 exact skips**, with all 3373 preceding logical outcome/message multiplicities retained after excluding the new controls; one PE-inspector assembly-path label adaptation is individually checked. The combined App suite passes **1355/25 exact skips**. All no-build full-suite payload bytes equal their preceding control payloads. Controlled root metadata/order demonstrates the accounting mechanism; it does not prove the exact historical native scheduling or every native copy engine.

[Original I282 CI](E-CI-follow-link-publication.md) independently retains 31 Unix ordinary-follow observations with correct content/safety fields but inflated BytesTotal. Each old total equals the maximum of 17 and UTF-8 source-link path bytes. The old test did not assert that total, so its passing status cannot qualify accounting. This new controlled reproduction is consistent with those values; no historical stack/scheduling trace is invented. Exact committed/hosted corrected accounting checks remain. This is a progress/work-accounting defect; no new content-loss finding is asserted. Physical-source HOLD and all broader/candidate gates remain.


## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve the exact exports, commands, payloads, raw failures, skips and owned restoration.

| File | SHA256 |
|---|---|
| `link-accounting-i06-v1/preparation-v1.json` | `447e31fd1b80de760d7a4850a5072759d92ca29a9f6d2541caaa4b7122feac30` |
| `link-accounting-i06-v1/Executors.cs` | `9421460bd4491b649810f6743c5f21672d024dbf8630267eed22069ad1147910` |
| `link-accounting-i06-v1/LinkDiscoveryAccountingTests.cs` | `5927e196ea5e0ab0f8c56d6bf2454de2f2e9bcdde0c6fecc0ca5044dd780d352` |
| `E:/FileCat/artifacts/release-evidence/link-accounting-i06-v1/original/command.json` | `163abf74eca11b810959742ed7d558906c526d69ae48b236852f0e129b62a115` |
| `E:/FileCat/artifacts/release-evidence/i06-resource-batch-20261009-v1/combined/core-controls/command.json` | `61d90ba7ac0c1d86cec54f82d1365ee698eb8743f542a341ef4196ed2c8f7e78` |
| `E:/FileCat/artifacts/release-evidence/i06-resource-batch-20261009-v1/combined/full-core/command.json` | `a492f1f1d33525403e80dbc7ea035a5718a0ce8b8d7067abef3aeeab2b3dbcb4` |
| `i06-resource-batch-20261009-v1/independent-batch-final-v1.json` | `904847d6d8b709ad6a740d8dbc7735910e704721affa7aac7de55a7f77189559` |
