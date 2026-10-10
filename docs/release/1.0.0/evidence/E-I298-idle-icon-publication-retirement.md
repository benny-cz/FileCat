# E-I298 — retire cleared synchronous icon publications

2026-10-10 CEST. Producer **3a735fd58982a0177d0b58940138301974e43dae** with two declared product overlays (`IconRequestCache`, `NativeIconSource`) and one regression fixture; complete **1380-blob** exports. Separate actual Skia/native processes declare their test-builder/native-fixture overlays. Preliminary I06/V12 scope.

A synchronous cache iterator waiting for more work retains its last request, including the published image after Clear. The actual Windows native worker also retains its previous image in the loop’s local frame. Clearing only the request reference therefore leaves all six actual Windows controls failed. The final fix clears retired request image references on Clear/eviction without disposing published images, and ends each Windows shared-icon load in a non-inlined method before its worker waits again. Cache/queue capacities, active borrowers and async production code remain unchanged.

The same **24** default-headless and **24 actual Skia** controls exercise 16/32/64-pixel images, real blocked synchronous/async enumerators, retained cache positives and actual borrower release. Each original run retains **six failures/eighteen positives**; each final run passes all 24. Skia preserves every known byte in all twelve held-borrower cases. The default headless backend is qualified only for reachability/usability; its pixel stub is not fidelity evidence. Five focused/full default pixel flags differ while all ownership/usability relationships agree; the first over-strict reader refusal is retained, and the separate Skia byte oracle remains mandatory.

Six public-API Windows controls use real native stock extraction, actual NativeIconSource workers and size-clear transitions: all six original/cache-only controls fail and all six final controls pass, preserving borrowed bitmap hashes. Six additional actual async-worker controls already pass on the original producer and remain healthy in the final payload; their existing request method is invoked by reflection with an owned load callback, with no private state mutation or real Shell-helper claim. The intermediate five-product-overlay attempt and its passing controls remain, but unnecessary async/Mac/Linux changes are omitted from the final implementation. Earlier native raw fields named “unmodified” describe the non-injected worker invocation and do not override declared source overlays; final fixtures use “production” instead.

All **60 affected App passes/one exact skip** and **full App 1451/25 exact skips** pass. Every **1452 predecessor outcome/message record and all 25 exact skips** remain. The cache-only full App pass does not qualify the native failure. The initial probe’s twenty failures/four passes, headless pixel limitation, blocked async fixture cleanup and the later async wrapper expectation refusal are retained; they are not extra production defects. Owned files are archived/rechecked, then removed where unlocked.

This closes the demonstrated bounded stale ownership path, not aggregate picture limits, whole-process/native allocation peaks, submitted frames, Linux/Mac native worker incidence, throughput, human/reference or candidate qualification. Exact committed/original hosted follow-ups remain. I06, the physical-source HOLD and human GO remain.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute. Nested records retain complete commands, raw failures/skips, declared sources and owned restoration.

| File | SHA256 |
|---|---|
| `i06-icon-worker-retirement-20261010-v4/IconRequestCache.cs` | `54945cc9f4e6f40f2afd4a0b06e1e5b9a1da9c3947b6859d7dc5dcbee3d6223f` |
| `i06-icon-worker-retirement-20261010-v4/NativeIconSource.cs` | `5a01a8a0d1e7d7abbdb220118c0b8f678c7dec0e5179fd9576ea761605c5948f` |
| `i06-icon-worker-retirement-20261010-v4/IconWorkerRetirementTests.cs` | `859b6744d836d88437a3eb4d6c526116f35a14b541a7bf084b2d2a784c1681d3` |
| `i06-icon-worker-retirement-20261010-v4/NativeIconPublicationLifetimeTests.cs` | `e8a07e8deb48016cd9d32590c33736cd266663aa1c56311749351d326f0f7d4f` |
| `i06-icon-worker-retirement-20261010-v4/NativePerItemIconLifetimeTests.cs` | `8eab2587132faf02f42f6c597ef57557469601c9cec039d5bb319a3699aae762` |
| `i06-icon-worker-retirement-20261010-v4/FileListSmokeTests-skia.cs` | `341dff212b002f08c24d2ccdcd1149883c735ed28ed1fc50e074205e978b4ac2` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v1/original/app-controls/command.json` | `d3d3544c5798db83f208806b4d582c0cb531b3351441cbbeb9c131d85b6523d8` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v2/original/app-controls/command.json` | `0321d6f5c038e76472ebb83a17f89f04f522f801455698a83b0fef15722ce0b4` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v2/original-skia/app-controls/command.json` | `21c683a8ab9825b1c6e26ecdf30bd97be565a84019d9bf3106cc49856837c941` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v2/fixed/app-controls/command.json` | `f12b14f20947a33b2faa2651c0a1efe58a98c7ebc987e624a4ac0dfe8d883521` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v2/fixed-skia/app-controls/command.json` | `69df28e21a2fa76f85585a3b3c4a0edfa63d6ec3cbc2b333f7ad42eb81df5412` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v2/native-original/app-controls/command.json` | `a60f91604d0e0918080725637164279acc6f7a1b0faabf4d30821ce3f79e3b15` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v2/native-fixed/app-controls/command.json` | `aef7fd4b276abc379febb1b7ad7d3ff98e79ff2516b731c0096ca2c4646781a2` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v3/native-fixed/app-controls/command.json` | `591104be5bda6875803fed9d9cfc9645de6a6b29bf9fa75b743215cb81ef64e1` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v3/peritem-original/app-controls/command.json` | `6353c4c63c31facfdb6f927bee00129cdd72933f3720167b1e73d1860345e256` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v3/peritem-fixed/app-controls/command.json` | `fe731ee789cb626fc2c9140bb4abbd96af311ab3583c37f2deb10afef10c96df` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v4/fixed/app-controls/command.json` | `0d90f1bea9cc301d92078363db905a62fe0c32be22080c56aa93171b59df93d9` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v4/fixed-skia/app-controls/command.json` | `4c961be361914b28d1751f35389dfe74e95b9c86ffc237f7de56de3f3d0e8de6` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v4/native-fixed/app-controls/command.json` | `9ef504b9a4cea44663734e7bfe142763ecf45cea83b6382f67b9b1601b8e4885` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v4/fixed/icon-app-controls/command.json` | `216022f5808b7ed5010641fcb32e5bf8c09e2f22b00b211442b317d094107950` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v4/fixed/full-app/command.json` | `1d53f04066396d076c3b70ee1e1922155f094bacdfbb4a17856a8bae687269e7` |
| `i06-icon-worker-retirement-20261010-v4/seal-icon-worker-v2.py` | `189ca3e1f894fb0827f1817472f3428702aca3d561a587dceeb4b82ff46b22ab` |
| `i06-icon-worker-retirement-20261010-v4/independent-icon-worker-final-v2.json` | `87bb550a1d30d0da4ab66e1c3c65de751e5eddddbc1698c7f504bf3cb9bb4391` |
| `E:/FileCat/artifacts/release-evidence/i06-icon-worker-retirement-20261010-v4/owned-temporary-files-v1.zip` | `8b134065ee30fbc5a936d8ed3949d6de872d3e79e5b1dbf9e5baac5e113d4c39` |
