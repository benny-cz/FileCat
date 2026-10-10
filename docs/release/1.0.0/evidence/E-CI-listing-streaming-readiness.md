# E-CI — listing retirement and streaming readiness

2026-10-10 CEST. Corrected **38017230777 attempt 1**, exact **7bdaa89dc15748fb481724d0b112f66d99535329 /1394 raw canonical blobs**, passes all four required lanes: Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26. All **27 available archives** match official metadata sizes/SHA256 digests. The independently parsed **14 TRX inventories /30,600 actual rows** contain **29,638 passes, 962 explicit skips and no failures**. All **48 added listing-ownership controls pass**; every **30,552 predecessor outcome, message and exact skip** is retained from the preceding qualified d24a457 run. All **92 restore graphs** match committed lockfiles, and four clean build receipts identify SDK **10.0.401** and this exact source.

Committed no-overlay local 14 Core/50 App controls and the [Windows/Ubuntu](E-I06-current-native-retirement.md) and [Mac](E-I06-current-mac-native-retirement.md) native follow-ups retain their own artifact identities. Hosted success does not qualify a candidate. All owner/freeze/physical-source/publication gates remain.

The first independent reader incorrectly assumed every Core inventory was named core-results.trx; the Windows x64 inventory is results.trx. A new reader derives actual added test-name/outcome/message multiplicities, requires all 48 new passes and retains every predecessor row. A separately captured recheck reproduces the old reader's refusal on unchanged collected evidence; it is labelled as a recheck, not the original execution output. No product test is rerun for that reader correction.

## Original failed run remains failed

Original **38016421829 attempt 1**, source **5363a8be838f877f3bd04690b4f904597120e5cf**, remains **failed**. All **22 available archives** match their official metadata sizes/SHA256 digests; the complete run log and all **12 available TRX inventories /26,618 actual rows** are retained: **25,775 passed, 842 explicit skips, one failed**. All 48 new listing-ownership observations are present across four lanes: **47 passes and one Ubuntu streaming-readiness timeout at requested 4,096 rows**. The timeout is at line117 before disposal/retirement observations. It is the same full-count assumption corrected by [I301](E-I301-streaming-fixture-readiness.md), not evidence of a newly established product failure.

Windows x64, Windows ARM64 and macOS lanes succeed. Ubuntu Core fails, so later Linux suites/launcher controls do not run. Its always-run launcher artifact retention step also reports missing files. Unavailable downstream artifacts/results are explicitly absent, with no inferred pass, skip waiver or replacement of the failed original. Earlier fully audited d24a457 CI keeps its separate provenance.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute; nested records contain every available archive, raw failure/skip, complete log and command.

| File | SHA256 |
|---|---|
| `i300-ci-20261010-v1/independent-original-ci-final-v1.json` | `d9d73608ca174d766b4a657925b3c56a3b214769e941061cd7c5efbe81a7d316` |
| `E:/FileCat/artifacts/release-evidence/i300-ci-20261010-v1/collect-original.py` | `2b617d67684864fb07e0532238a5cfe4e2228b02b5f9c8d330a49a925561abb9` |
| `i300-ci-20261010-v1/run-view-original-v1.json` | `0a0692981cd45c49eb6f1c73abb0688996abb78b2a8ed54adb57bbdc705822a6` |
| `i300-ci-20261010-v1/linux-original-job-v1.txt` | `29a902f21c214912601ae7f13d53c4f6c09775b931002f9c7e17fe2e5c8e544b` |
| `i300-ci-20261010-v1/artifacts-original-v1.json` | `91815bc5debd6075b48cf482f3f972c0436e3f5f63f6bc64b8ffbc275372b185` |
| `i301-ci-20261010-v1/independent-ci-final-v1.json` | `7150ece47b2d4049b8ba97609766b8e1af8b9bdef029e8c9b780207a4e4326ee` |
| `i301-ci-20261010-v1/independent-corrected-ci-final-v1.json` | `55f70e54ac736cc6300a2f1ecf22c4e0c18f6dd696b853a9391b22e76ca75f28` |
| `E:/FileCat/artifacts/release-evidence/i301-ci-20261010-v1/seal.py` | `30bf09c13fd95dd820eae084db6c71d6678f27cf0b90b6efff6348d06e42b8de` |
| `E:/FileCat/artifacts/release-evidence/i301-ci-20261010-v1/seal-v2.py` | `6c761efb1ed3c3c223f0f6167ad57d629ad43d0764cc3974e5542ca9fd04e8e5` |
| `E:/FileCat/artifacts/release-evidence/i301-ci-20261010-v1/collect-corrected.py` | `ac661e44ef9e36edadf285b6736738515430a5f44ddbe5fedd0563b3a9a18f7e` |
| `i301-ci-20261010-v1/reader-refusal-recheck-v1.json` | `e68b988ca62470b9a2ad2ccd915f70aefd8ecd34e46a76c0506ff22d8254ea49` |
