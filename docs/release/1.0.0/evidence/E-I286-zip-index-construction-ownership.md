# E-I286 — failed ZIP indexing releases its native source

2026-10-09. Original **1fefd5e269c52206c3be4d6475fdd0584dc1f4be**; complete 1347-blob exports with only declared fixture/product overlays. ZipIndex.Build guarded the ZipArchive constructor, then built the member index outside that guard. The runtime reads the central directory lazily. A ZIP with a damaged central signature or inconsistent entry count therefore passes construction, fails directory parsing and leaves its FileStream open until finalization. No index is published, so Release cannot find that source.

The correction keeps construction and the entire index build inside one ownership guard. The successful index takes ownership; failure attempts archive and file closure, preserves the original parsing exception, and logs secondary cleanup errors. Healthy cached indexes and leased-content behavior are otherwise unchanged.

Eighteen actual native Windows cases cover constructor/header refusal and two lazy directory failures, both public listing and content-open routes, and one/four/sixteen repeated attempts. Every case first opens a healthy owned ZIP, reads all 4096 patterned member bytes and checks cache retention and release. A direct runtime control distinguishes constructor failure from later Entries failure. The sharing observations run within an isolated bounded no-GC interval, with collection counters unchanged; exclusive FileShare.None succeeds only after the source closes. All owned archive bytes remain unchanged, failed cache count stays zero, and a healthy read/release succeeds afterward.

Original **twelve failures/six passes** become **eighteen passes**. Across **126 observations**, the original 84 lazy-directory failures leave the file open while 42 constructor failures close it. Every corrected observation closes before GC. These are repeated sharing observations, not an independently enumerated count of distinct leaked handles. Input ZIP timestamps, whole-ZIP hashes, GC counter values and owned roots differ between runs; complete per-run values remain and are not asserted equal. Member bytes, error types/messages and the closure oracles agree. Unix skips explicitly state that the native Windows sharing oracle is unavailable there.

The same compiled full Core suite passes **3391/64 exact skips**. All **3437** preceding local records (3373 passes/64 skips), their outcome/message multiplicities and exact skip reasons remain. One assembly-path display label is adapted only after checking the same PE test method. The private first correction failed compilation because the extracted helper omitted its path parameter; that unchanged command/export survives, no tests ran, and the corrected helper uses a fresh stage.

Exact committed follow-up is recorded below; original hosted checks remain. This does not qualify every malformed archive, native library failure, concurrent cache race, aggregate active/leased/listing memory, other platforms, native GUI/reference workloads or a candidate. No host UI, physical source, contract freeze or publication occurs; I106/I110 HOLD and explicit human GO remain.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute; nested receipts retain the complete sources, commands, payloads, failures and restoration.

| File | SHA256 |
|---|---|
| `i06-zip-build-retirement-20261009-v2/ZipIndexConstructionOwnershipTests.cs` | `d2164d137e5027af52a53653ec32403e7c764a272fd24dc0d7b9bcef43c229df` |
| `i06-zip-build-retirement-20261009-v2/ZipProvider.cs` | `2240e491afce333d4f6c9ce619b5c21acbc0367add587ee7342819030d887651` |
| `E:/FileCat/artifacts/release-evidence/i06-zip-build-retirement-20261009-v1/original/core-controls/command.json` | `2e46acc1107ff28dc088bd3733cc5ca753431a30b1c267a36ef75f8479d8f210` |
| `E:/FileCat/artifacts/release-evidence/i06-zip-build-retirement-20261009-v1/fixed/core-controls/command.json` | `a5e04dbdad4756c697a19358b5a717fdb14cfac527a98e196b11ffd894b6d949` |
| `E:/FileCat/artifacts/release-evidence/i06-zip-build-retirement-20261009-v2/fixed/core-controls/command.json` | `5de638211300531bc861cfa2a66028ce412d09754371c04de6d98a9a781a0601` |
| `E:/FileCat/artifacts/release-evidence/i06-zip-build-retirement-20261009-v2/fixed/full-core/command.json` | `91850ad124d44b9202ceb8d62ef71ed77fd5014400699ddbf21b3acfd837bb52` |
| `i06-zip-build-retirement-20261009-v1/independent-original-diagnosis-v1.json` | `4c25ab4a63097c4c190c40829603b14368f95d0fb7504c661b959c044e389f95` |
| `i06-zip-build-retirement-20261009-v2/preserved-build-refusal-v1.json` | `a42be98b4f60097fc2d83c36e4b2b784e271d83ecf58b2102f936245f68711cb` |
| `i06-zip-build-retirement-20261009-v2/seal-zip-build-v1.py` | `d6f2cdfd70beed756c3844c150a51cf0465570c2b3ee1e7844d9eb142a59f6cb` |
| `i06-zip-build-retirement-20261009-v2/independent-zip-build-final-v1.json` | `ed27ff19717ccd44650aa8668ac1e567abba48e745108f64781417abd06db8c8` |
| `E:/FileCat/artifacts/release-evidence/i06-zip-build-retirement-20261009-v2/owned-temporary-files-v1.zip` | `2fd97a4a21a76611c9a8f9f2528990415e3c7c456d8414ec1cbab4e5cf88ed5e` |


## Exact committed follow-up

No-overlay **ffe6159e43b32d83873c5ad35e0469b837112e14** verifies all 1349 canonical Git blobs and passes all eighteen native Windows controls. All 126 closure observations and private/exact outcome/message semantics agree. Per-run header timestamp hashes, GC counter values and roots remain qualified. Three owned files are archived/rechecked; one is removed and two compiler locks remain in the exact owned root. No full-suite replay, host UI, physical source or candidate qualification.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute; nested receipts retain the complete sources, commands, payloads, failures and restoration.

| File | SHA256 |
|---|---|
| `i286-committed-20261009-v1/seal-exact-v1.py` | `f52a020c08062aa446d2e3841ad3d613304a42c9618b2d70c668f7b0bac25601` |
| `i286-committed-20261009-v1/independent-exact-final-v1.json` | `b6bcd0918ced4bb7fad963405974a0a7fc79195ef955eb71f05e764697b5fcbe` |
| `E:/FileCat/artifacts/release-evidence/i286-committed-20261009-v1/committed/core-controls/command.json` | `13c4591093158754c74077825c30e9b733e6b1435b5827ef6ef33a39c025eb2b` |
| `E:/FileCat/artifacts/release-evidence/i286-committed-20261009-v1/committed/inputs.json` | `c6e2a17d094c3752ae1be9104b44148a9270269a67da5a096824e36123b5b855` |
