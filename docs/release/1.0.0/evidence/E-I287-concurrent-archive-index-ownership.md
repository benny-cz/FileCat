# E-I287 — concurrent archive index construction has one owner

2026-10-09. Original **ffe6159e43b32d83873c5ad35e0469b837112e14**; four complete 1349-blob source exports retain the exact declared overlays. Concurrent cache misses could build competing ZIP/TAR indexes and overwrite the cached winner without retiring the losers. Provider Release then sees only the winner. ZIP opens its source during construction; TAR opens its source lazily, so duplicate TAR indexes alone do not establish a native TAR leak.

Both providers now serialize index lookup, construction, publication, stale eviction and retirement under a provider-owned lock. Nested archive path resolution occurs before that lock. The existing ZIP construction failure guard remains. This serializes unrelated archive builds within the same provider too; unrelated-archive throughput and reference acceptance are not qualified. No new aggregate memory budget or hard limit is invented.

Twelve start-gated dedicated-thread controls cover ZIP/TAR, 64/5000 empty members plus one 4096-byte patterned payload, and two/six/twelve callers. The observer invokes the actual private index-admission method through reflection, deliberately holds returned indexes/native wrappers, checks the actual cache, and then reads a complete public listing and payload. Release is observed through SafeFileHandle.IsClosed or CanRead plus independent Windows exclusive FileShare.None access. No GC closure oracle is used. Lazy TAR sources are never forced open on discarded indexes. This scoped observer establishes ownership at admission; it is not evidence of ordinary UI incidence or a native host workload trace.

The original is **eleven failures/one pass**. Five ZIP cases leave **33** observed sources open after Release; the serial ZIP case reuses its single index. Six TAR cases also construct duplicates, but only their cached lazy source opens and all six sources close. Across twelve separate cases the observer sees **79 index instances for 80 requests**; this is a sum, not simultaneous aggregate or peak memory. The correction passes **all twelve**: one index per case and every actual source closes. All original/fixed inputs remain byte-identical within each case, listings/member bytes agree, and owned roots are removed. Per-run ZIP header hashes and roots remain qualified rather than asserted equal.

Four focused archive App controls pass. The unchanged compiled full suites pass **Core 3403/64 exact skips** and **App 1369/25 exact skips**. All **3455** preceding Core outcomes/messages and all **1394** preceding App outcomes/messages retain their exact skip reasons; one PE display path is adapted only after verifying the same method. The successful Core repeat and App suite actually overlap.

The initial full Core command is preserved at **3401 passes/65 skips/one failure**: native GnuPG agent creation fails in one existing test and causes another existing test to skip. The same two compiled controls reproduce **one failure/one skip** under the long owned temporary path, then pass **both** under a short owned path. Repeating the full Core suite without source, payload, build or global GnuPG configuration changes restores both passes and the original 64 skips; every other outcome/message record remains unchanged. This supports path sensitivity in this harness, not a traced socket-limit cause. The initial paired-suite wrapper subsequently refuses its expected-success assertion; that raw failed receipt is retained.

The earliest fixture export does not compile (nullable cleanup inference); the second export has six real ZIP failures and six observer NullReferenceExceptions from assuming unopened lazy TAR sources exist. Those six observer failures are not product failures. Both original scripts, exports and receipts remain, followed by the corrected observer's eleven failures/one pass and independently sealed final results. Twenty-five owned temporary files across five bounded roots are archived, rehashed and removed; no locks remain.

Exact committed follow-up is recorded below; original hosted checks pass as linked below. Aggregate active/retained/leased/listing memory, explicit Release/member-use races, cancellation, adversarial schedules, other consumers, native process/frame/reference workloads and candidate acceptance remain in I06. This does not resume physical-source tests, operate host UI, freeze a contract or publish a candidate/stable release. I106/I110 HOLD and explicit owner/human GO remain.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute; nested receipts retain the complete sources, commands, payloads, failures and restoration.

| File | SHA256 |
|---|---|
| `i06-archive-concurrent-ownership-20261009-v4/ArchiveIndexConcurrencyOwnershipTests.cs` | `293aabaff74822937d6ed2b8daa2e27d1ad15785cdc012a77da6fea91cc6025c` |
| `i06-archive-concurrent-ownership-20261009-v4/ZipProvider.cs` | `e29115327f1d29e4f7ff00059d7e34e353635c88a5677d35b09d54be2564a63b` |
| `i06-archive-concurrent-ownership-20261009-v4/ArchiveProvider.cs` | `7b15819876743153d9fbd86d6dde5b330616071b86522e707fe2b902e53bd58d` |
| `E:/FileCat/artifacts/release-evidence/i06-archive-concurrent-ownership-20261009-v3/original/core-controls/command.json` | `3b59d534924c03c33e3d2a798155f1745c9d5fc6a6db81c721afd9334f6a18c2` |
| `E:/FileCat/artifacts/release-evidence/i06-archive-concurrent-ownership-20261009-v1/original/core-controls/command.json` | `fddbbfbeee8be051cb23f44e09504aa4aa8ca05ecbfd62c35c9a8a6976bf596a` |
| `E:/FileCat/artifacts/release-evidence/i06-archive-concurrent-ownership-20261009-v2/original/core-controls/command.json` | `75683092e903dea47251946b741eb5a7c17e4de0ebfb801fba137a14877993ee` |
| `E:/FileCat/artifacts/release-evidence/i06-archive-concurrent-ownership-20261009-v4/fixed/core-controls/command.json` | `56604e0d3975b67e8f6ea0d416583c48c817515def27e94e4a7acaaf6ed55568` |
| `E:/FileCat/artifacts/release-evidence/i06-archive-concurrent-ownership-20261009-v4/fixed/archive-app-controls/command.json` | `9b8afe4445a36650d4d065950cc5cecbab55bcb3eaacacf549f44a0d235ecbfa` |
| `E:/FileCat/artifacts/release-evidence/i06-archive-concurrent-ownership-20261009-v4/fixed/full-core-short-v1/command.json` | `b60ebb7b55c639db9ca0805aeef4a8238319fe3143e230f5e7572eb5bb394039` |
| `E:/FileCat/artifacts/release-evidence/i06-archive-concurrent-ownership-20261009-v4/fixed/full-app/command.json` | `d8b483fe1606aeec467b58b21b7035709055af116c5dabcb59cbc3e53187a647` |
| `E:/FileCat/artifacts/release-evidence/i06-archive-concurrent-ownership-20261009-v4/fixed/full-core/command.json` | `f2b9e93f83efd86d512b415b94b1118db035fa8d6c2c164c3a92169b68fc95b3` |
| `E:/FileCat/artifacts/release-evidence/i06-archive-concurrent-ownership-20261009-v4/fixed/gpg-long-controls/command.json` | `8c38f94daf184c2e0d3143dde4c9994500ef29ce48509b56f279375cb871fb23` |
| `E:/FileCat/artifacts/release-evidence/i06-archive-concurrent-ownership-20261009-v4/fixed/gpg-short-controls/command.json` | `d808852109c3ca326a7b1ff17ae6662d71f112fa824e37079ede22a9abc5bd95` |
| `i06-archive-concurrent-ownership-20261009-v4/gpg-path-controls-and-core-final-v1.json` | `478e94045c3ed64a4c822cc08f534a12148c09b941781ffd2df0ae590bcb7bd6` |
| `i06-archive-concurrent-ownership-20261009-v4/independent-original-diagnosis-v1.json` | `d83a8389a76a2b4661d2d1bbe482624431c10fa6622dc3dd0e2d1f25ab8ca038` |
| `i06-archive-concurrent-ownership-20261009-v3/preserved-observer-refusal-v1.json` | `e23d55a1ec8535dc9717ae7cd3a590302883f08d3daa10f94cc2f4ce2b8429ee` |
| `i06-archive-concurrent-ownership-20261009-v4/seal-concurrent-batch-v1.py` | `077d9328a578d32700b7238af740cb777616759cb9776c20b9e536d030aadf4c` |
| `i06-archive-concurrent-ownership-20261009-v4/independent-concurrent-batch-final-v1.json` | `4d3c4040f69d5f7127dc958c1daee64ad7fb6da00453df9b4e789ab36b943bcd` |
| `E:/FileCat/artifacts/release-evidence/i06-archive-concurrent-ownership-20261009-v4/owned-temporary-files-v1.zip` | `d2642bc60fa67b75aec21d76e31ddc2cb4302aca2da8f49e37197ba417bd8b78` |


## Exact committed follow-up

No-overlay **63f5abad5abd6fef69e38cb8e03497e612fa5ae1** verifies all 1352 canonical Git blobs and passes all twelve controls. Every private/exact outcome/message and ownership observation agrees; all twelve actual source wrappers close and native Windows exclusive access succeeds. Per-run ZIP header hashes and roots remain qualified. Three owned files are archived/rechecked, one removed and two compiler locks retained. No full-suite replay, aggregate memory/arbitrary-schedule/native-frame or candidate qualification.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute; nested receipts retain the complete sources, commands, payloads, failures and restoration.

| File | SHA256 |
|---|---|
| `i287-committed-20261009-v1/seal-exact-v1.py` | `8ebed34afac722cd4603f32ac3a711c45bc84b2f81e392e0e409402da06a2db4` |
| `i287-committed-20261009-v1/independent-exact-final-v1.json` | `2ce3d55a2708a50764baa7c79405cbfed37c706298428e63e4f7286b43e3a71d` |
| `E:/FileCat/artifacts/release-evidence/i287-committed-20261009-v1/committed/core-controls/command.json` | `6ff1975502894d68e6112bb670490a37e703851e9332298d57e8c5de3fa12a25` |
| `E:/FileCat/artifacts/release-evidence/i287-committed-20261009-v1/committed/inputs.json` | `e8dd5f5a632c9b7b94ab9909a97074f3d023ceb4830935cd7b193a9937c9462b` |


## Original hosted follow-up

[37984994009 attempt 1](E-CI-concurrent-index-ownership.md) at exact 63f5abad passes four required lanes: 29,884 actual records/48 new passes, preserving all 29,836 predecessor outcomes/messages and exact skips. All 48 sources close; 24 Windows cases also verify exclusive-sharing before/after. Unix sharing remains explicitly unavailable. Aggregate/arbitrary-schedule/throughput/native/reference/candidate scope remains.
