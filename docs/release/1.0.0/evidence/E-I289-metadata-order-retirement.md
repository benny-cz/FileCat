# E-I289 — forgotten metadata retires its ordering records

2026-10-09. Original **63f5abad5abd6fef69e38cb8e03497e612fa5ae1**; correction base **88a107d79dac1e9ac8ae0690e4d11455b3a14aa4**. Complete 1352/1355-blob exports retain only declared fixture/product overlays. The intervening changed-path inventory verifies unchanged Core, Archives, Core-test and central build inputs, including the exact MetadataService blob; the About change is separate.

Forget removed a field's cached values and timestamps, but left their keys in the eviction-order queue. Repeatedly recomputing and forgetting one field therefore accumulated obsolete path records while the live value count stayed small. The 50,000-value cache limit did not bound this history because eviction drained the queue only when the live cache exceeded its limit. Normal per-item verification settling can call this API; the controlled reproduction does not claim a historical native UI incidence rate.

Forget now filters matching ordering slots under the same publication lock, preserving the order of unrelated retained keys. Producers invalidated during Forget still cannot republish. Invalidate's existing complete cleanup remains. The pass scans the current ordering queue as well as the existing cache scan; large-cache throughput is not qualified by these controls. No new byte budget, value-count limit or aggregate process bound is introduced.

Six public-API controls compare per-field Forget with complete Invalidate across 16/512/8192 repetitions. They read one actual owned 20-byte file, verify every computed value, retain an unrelated field, retry both fields and check source bytes/hash unchanged. The original is **three failures/three passes**: after 8192 per-field forgets there are **8193 order records for one live cached value**, then 8194 records for two live values after retry. The correction passes **all six**, keeping ordering slots equal to live cached values. The unrelated value stays cached without recomputation under Forget; full Invalidate correctly recomputes it. Counts describe retained queue records, not measured managed/native allocation or a reference workload.

All **87 affected App metadata/verification controls** pass and retain their outcome/messages from the preceding full App run. The unchanged compiled full Core suite passes **3409/64 exact skips**, preserving all **3467** preceding Core outcome/message records and exact skip reasons. One PE display path is adapted only after verifying the same method. This batch does not replay the full App suite. Its separate earlier I288 full App producer remains 1398/25. The test launcher declares a short owned temporary root to avoid the already demonstrated GnuPG harness path sensitivity; no global GnuPG configuration changes.

The final receipt records every archived, rehashed, removed or locked file under both exact owned temporary roots. Exact committed checks are recorded below; original hosted checks pass as linked below; broader metadata/cache bytes, aggregate jobs/results/providers/pictures, arbitrary schedules/native/reference and candidate acceptance remain. No host UI, physical source, contract freeze or publication occurs; I106/I110 HOLD and required owner decisions/human GO remain.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute; nested receipts retain the complete sources, commands, payloads, failures and restoration.

| File | SHA256 |
|---|---|
| `i06-metadata-order-retirement-20261009-v2/MetadataOrderRetirementTests.cs` | `9282d114909172b5c9bc7f398d069fa8399f32e3ece736d0872331f874d30ed8` |
| `i06-metadata-order-retirement-20261009-v2/MetadataService.cs` | `e376a6d3b980c9a41020c10a7caeca18ef5557178ab7630dda67729932b849c6` |
| `E:/FileCat/artifacts/release-evidence/i06-metadata-order-retirement-20261009-v1/original/core-controls/command.json` | `ad74cfa9cfd863b94d117065365a442c9f17e606ddcdc7c46fbe2ae8af5a125b` |
| `E:/FileCat/artifacts/release-evidence/i06-metadata-order-retirement-20261009-v2/fixed/metadata-app-controls/command.json` | `9ea8a220447987ff3a16a89aa31f4baf1cb5b4d646a845c3fc71b171814246a4` |
| `E:/FileCat/artifacts/release-evidence/i06-metadata-order-retirement-20261009-v2/fixed/core-controls/command.json` | `29f84f14c3a0a3541ddf817b58329d98cca2e8bcb8202a4138c3193285ac8ad1` |
| `E:/FileCat/artifacts/release-evidence/i06-metadata-order-retirement-20261009-v2/fixed/full-core/command.json` | `bb9d86047edb10d7c1a9d34a044eafd056af10ae2c007cd9a3ec625ca2861afb` |
| `i06-metadata-order-retirement-20261009-v2/independent-original-diagnosis-v1.json` | `1e2ccdcd3a87ccba7e56885c335e03282cdbe3645f958f370a00a9634d62b975` |
| `i06-metadata-order-retirement-20261009-v2/seal-metadata-batch-v1.py` | `765b182d496ba8868b3adeaadebc0af2cf810b5ebbfb20e50bd60a6cee2c8cbd` |
| `i06-metadata-order-retirement-20261009-v2/independent-metadata-batch-final-v1.json` | `3b86fb78a6c0066eccec54f0311f94562ccd8cb4df3faa55c6366b3bda0276ed` |
| `E:/FileCat/artifacts/release-evidence/i06-metadata-order-retirement-20261009-v2/owned-temporary-files-v1.zip` | `7f8ebf003b88958dd01dab6257bc1740ab709bf8f442731aad3c2a054c4c6b4f` |


## Exact committed follow-up

No-overlay **96977d456767389424c07a8fde10e278c95615de** verifies all 1358 canonical Git blobs and passes all six controls, with every private/exact outcome/message and semantic observation matching apart from owned roots. Ordering slots equal live values in every case. Four owned temporary files are archived/rechecked; two are removed and two compiler locks remain explicitly recorded. No full suite, native, aggregate, throughput or candidate replay is claimed. Original hosted checks pass as linked below.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute; nested receipts retain complete source, commands, payloads, raw failures/skips and restoration.

| File | SHA256 |
|---|---|
| `i289-committed-20261009-v1/seal-exact-v1.py` | `a65d1a5b8fb483aa69d291ee961537792e320207bf2386159fd22497a228e617` |
| `i289-committed-20261009-v1/independent-exact-final-v1.json` | `8ec8ba21b6546a96eb8bbc09fcc3ba2abd5418e12e60148e23623ad4d5f4462f` |
| `E:/FileCat/artifacts/release-evidence/i289-committed-20261009-v1/committed/core-controls/command.json` | `b9001700c3bd9d38839312db7b01c9c1f1d175e7ea429424de52eca6c5048b20` |
| `E:/FileCat/artifacts/release-evidence/i289-committed-20261009-v1/committed/inputs.json` | `45f6160e361ea788e943dbab803508aad0f6d0d3ff8ac175927499d31bf91c25` |


## Original hosted follow-up

[37988669063 attempt 1](E-CI-metadata-order-retirement.md), exact 96977d4, passes all four required lanes. Its 30,024 records include 24 new metadata passes and 116 About passes beyond the preceding fully qualified 63f5abad producer. Every one of those 29,884 earlier qualified outcome/message records and exact skips remains. The separately failed 88a107d run is retained; this later successful unchanged DirectoryDiff case does not establish its historical cause. I291 repair, aggregate/throughput/native/candidate scope remains separate.
