# E-I344 — continue owned fixture cleanup after scratch retirement

2026-10-11 CEST. Source **fd851d1c363a21870a1c3e08364973c6d127613d /1568 canonical raw Git blobs**, declared observation-only/helper overlays. This addresses test isolation within [I06](E-I06-resource-progress.md), preserving the two original [I341 cleanup refusals](E-I341-local-staging-ownership.md) without retrospectively assigning their cause.

An observation-only `TempDir` overlay records actual exceptions without changing disposal behavior. Thirty unchanged listing-class runs on each ordinary-user Unix platform reproduce **nine Ubuntu and thirty Mac `FileNotFoundException` events** during attribute normalization of concurrently retired listing scratch streams. The outer catch abandons deletion of the rest of that test directory. Each affected root retains its complete 200 one-byte fixture files: **1800 Ubuntu/6000 Mac files**. Complete archives, hashes, stack frames and file memberships are retained before independent restoration. Every run still reports 21 passes and one explicit existing Unix hidden-attribute skip; passing test assertions alone did not establish cleanup.

The correction handles an already absent file/directory at the individual normalization step and continues deleting the owned tree. Other I/O/access failures retain their original handling. The same thirty runs per platform observe **five Ubuntu/75 Mac missing-scratch events** while leaving **zero residual files**. Across the paired observations, all **2640 results/2520 passes/120 exact skips** are retained. One trace still sees another scratch pathname while it retires; final absence is independently verified.

Final sources omit the observer. Combined [I345](E-I345-native-identity-observation.md) qualification passes **24 local/36 native whole-byte controls**, preserves every **4046 predecessor outcome/message and 108 exact skips**, and adds **88 listing results/86 passes/two explicit Unix skips**. Windows runs under measured high RID 12288/session 0; Ubuntu UID 1000 and Mac UID 501 use pinned .NET 10.0.12. Independent postchecks verify **1327 native files**, absent owned processes/temporary roots and a stopped Windows listener. Three host owned roots/13 files are archived, hash/exclusive-open checked and restored.

Earlier reader refusals preserve an initially omitted Unix skip, a preparation error and runtime stack/retiring-entry differences. Fresh v4 inspects the actual retained data without rerunning tests. No host UI, physical source, persistent setting or package changes. Wider product/native allocation, reference hardware, human and installed-candidate resource qualification remains; no I06 closure or publication claim.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, original failures/skips, complete bytes and restoration.

| File | SHA256 |
|---|---|
| `i344-i345-validation-20261011-v4/independent-final-v1.json` | `e1594708a9564a5f4835a3478267f220180e86a756079903afd7e33bf68b1610` |
| `i344-i345-validation-20261011-v4/retained-tool-sources-v1.json` | `35eb8ab22578207a91f2a7d033a4378716353bbd841f700cd0f96b2b270f4157` |
