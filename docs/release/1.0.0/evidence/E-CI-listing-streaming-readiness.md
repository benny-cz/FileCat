# E-CI — listing retirement and streaming readiness

2026-10-10 CEST. Original **38016421829 attempt 1**, source **5363a8be838f877f3bd04690b4f904597120e5cf**, remains **failed**. All **22 available archives** match their official metadata sizes/SHA256 digests; the complete run log and all **12 available TRX inventories /26,618 actual rows** are retained: **25,775 passed, 842 explicit skips, one failed**. All 48 new listing-ownership observations are present across four lanes: **47 passes and one Ubuntu streaming-readiness timeout at requested 4,096 rows**. The timeout is at line117 before disposal/retirement observations. It is the same full-count assumption corrected by [I301](E-I301-streaming-fixture-readiness.md), not evidence of a newly established product failure.

Windows x64, Windows ARM64 and macOS lanes succeed. Ubuntu Core fails, so later Linux suites/launcher controls do not run. Its always-run launcher artifact retention step also reports missing files. Unavailable downstream artifacts/results are explicitly absent, with no inferred pass, skip waiver or replacement of the failed original. Earlier fully audited d24a457 CI keeps its separate provenance.

Corrected **7bdaa89 /1394 blobs** passes 14 Core/50 affected App controls with no overlays and four affected Windows/Ubuntu native cases. Its hosted run **38017230777 attempt 1** is still awaiting complete collection/qualification at this record; partial lane status is not full CI evidence. All candidate/freeze/physical-source/publication gates remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute; nested records contain every available archive, raw failure/skip, complete log and command.

| File | SHA256 |
|---|---|
| `i300-ci-20261010-v1/independent-original-ci-final-v1.json` | `d9d73608ca174d766b4a657925b3c56a3b214769e941061cd7c5efbe81a7d316` |
| `E:/FileCat/artifacts/release-evidence/i300-ci-20261010-v1/collect-original.py` | `2b617d67684864fb07e0532238a5cfe4e2228b02b5f9c8d330a49a925561abb9` |
| `i300-ci-20261010-v1/run-view-original-v1.json` | `0a0692981cd45c49eb6f1c73abb0688996abb78b2a8ed54adb57bbdc705822a6` |
| `i300-ci-20261010-v1/linux-original-job-v1.txt` | `29a902f21c214912601ae7f13d53c4f6c09775b931002f9c7e17fe2e5c8e544b` |
| `i300-ci-20261010-v1/artifacts-original-v1.json` | `91815bc5debd6075b48cf482f3f972c0436e3f5f63f6bc64b8ffbc275372b185` |
