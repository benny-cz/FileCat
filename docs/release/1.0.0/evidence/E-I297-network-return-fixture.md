# E-I297 — observe a ready Network return view

2026-10-10 CEST. Original **55a01ec8397540d4b94650fbc2918b159577fa9e** and declared owned test overlays. This corrects a validation fixture; no Network browsing production code changes.

The I296-only full App run passes **1424**, retains **25 exact skips**, and fails the existing Network-place test once at its post-return TryGetFocused assertion. All eighteen history controls pass. That run remains failed. Its historical cause is unavailable because the original fixture did not record the return state or timing.

The fixture counted 400 twenty-millisecond turns and then asserted focus without requiring a completed return listing. A controlled **12-second** delay in only the owned provider’s second KnownServers callback demonstrates that the old fixture can actually **pass while State is Loading**; the ordinary control passes Complete. Native TRX counts are **two passes**, not the wrapper’s expected one failure/one pass. The wrapper’s refusal and unchanged raw results remain. This observable readiness gap does not attribute the earlier failure to a particular native delay.

The corrected fixture waits up to **30 seconds** for the Network root, Complete state and the expected localhost focus, then asserts both completion and focus explicitly. The same delayed and ordinary controls plus both existing native Network cases pass (**four**), with actual localhost share enumeration and the earlier administrative-share assertions preserved. The two delayed/ordinary additions are retained as regression tests; non-Windows runs retain an explicit platform skip. No system/network settings or real callback outside the owned service change.

The combined [I296](E-I296-operation-history-retirement.md) payload passes all **63 affected App controls**, full Core **3470/64** and full App **1427/25 exact skips**. Every **3534/1432** preceding Core/App outcome/message/exact skip remains; two Network controls and eighteen history controls are added. Original/fixed/full native observations and all declared canonical source exports are independently rechecked. This is preliminary protocol/listing readiness, not desktop input, process-memory, human/reference/candidate or historical-cause qualification. Exact committed/original hosted follow-ups remain.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute. Nested receipts retain complete commands, raw outputs and source identities.

| File | SHA256 |
|---|---|
| `i297-network-return-deadline-20261010-v1/NetworkPlaceTests-original.cs` | `71be91f610f5015246d7705a405b0be2c5e828655e0e642fad177f29cd17d786` |
| `i297-network-return-deadline-20261010-v1/NetworkPlaceTests.cs` | `09840d712572378ca4ff46bdb52e9a88d125e8235377b4713ec15d7a9761e29d` |
| `i297-network-return-deadline-20261010-v1/NetworkPlaceDeadlineTests.cs` | `35aaf0c9381e2ef3997600dac24ac19dc752a219ee4d974fade9f1baccabebff` |
| `E:/FileCat/artifacts/release-evidence/i297-network-return-deadline-20261010-v1/original/app-controls/command.json` | `99985584aca6f8f4e514fe7ab8ed585594dc7cbb4f2ec970fd5baba87c0abf75` |
| `E:/FileCat/artifacts/release-evidence/i06-history-network-retirement-20261010-v1/fixed/network-app-controls/command.json` | `7726a20e6de1573e73d56c64e023d0b3d7960311b90fafb97aa1a1e02c8a11ed` |
| `E:/FileCat/artifacts/release-evidence/i06-history-row-retirement-20261010-v2/fixed/full-app/command.json` | `f6d91fb862bde423b6b99a9062636a38023d8cbfe6ba6df49145c537f2f2bce6` |
| `i06-history-network-retirement-20261010-v1/seal-history-network-v1.py` | `ee6768a42a798ae76999c22aff122e1421e2b223117a41d43a75144ccebff46d` |
| `i06-history-network-retirement-20261010-v1/independent-history-network-final-v1.json` | `ad6518ec26460fe433cbeef6536394370373f6930cf3a0bfa8c29bbe7d1b1dc6` |
