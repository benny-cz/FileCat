# I373 — verification sidecar scans retained every visited folder

2026-10-12 CEST. **High resource issue; remediated preliminarily at pushed `bafaf4c618e68d828a33714869ba150ca13284c5`.** `VerificationService` held each parsed `FolderSidecars` object in its session-wide `_folders` map. Its five-second freshness rule caused rescanning but never removed old folders. Browsing more folders could therefore retain their paths, source lists and checksum/signature claims indefinitely. This finding does not establish a whole-process memory peak.

At exact committed `42ddc97e419d78beba9a2c4bde6e8c77da834bb7`, with only the declared new test, an owned 521-folder traversal left **521 cached entries**. The bounded-retention assertion failed with `Actual: 521`; its raw TRX and log remain failed. The test also prepares an owned checksum sidecar and requires a subsequent folder change to invalidate its visible result after the original cache entry has been evicted. No physical source, network location or host UI was used.

The correction retains at most **512 recently accessed folder scans**, without holding the cache lock during disk scanning. A cache hit updates access order without extending the five-second freshness period. `FolderChanged` conservatively raises `SidecarsChanged` after rescanning a folder whose prior parsed state was evicted, so a previously shown verification result can be recomputed. This is a count bound on cached folders, not a byte bound for one large sidecar set or a guarantee about all verification consumers. The separate case-sensitive Unix folder-identity concern is tracked as [I374](E-I374-unix-verification-folder-identity.md) and is not folded into this result.

The corrected affected inventory first passed **25 cases with one existing skip**. Adding a concurrent 640-folder scan gave **26 passes and the same skip**. A separate full Core run passed **4,017 cases with 93 explicit skips and zero failures**. The independent reader reparsed all four TRX files and compared the full Core case-name/outcome/message multiset to the exact preceding I372 Core run: every predecessor record remained and exactly two new I373 cases passed. It also rehashed the original Git source, declared baseline test, final product diff, final test source and raw logs. A comment-only clarification to the `FolderChanged` documentation followed the full Core test build; the later full-solution build at final local source produced **41 existing warnings and zero errors**. A further no-build final-source Core repeat passed **26 cases/one unchanged skip**, with all names/outcomes/messages matching the prior affected run; its separate reader pins the final TRX, log, build and earlier audit. The installed/native and original-push CI results are separate pending qualifications. Host App UI tests were not run because the owner reserved the workstation; guest/hosted routes remain available.

| Private receipt in `i373-verification-folder-retirement-20261012-v1` | SHA-256 | Meaning |
|---|---|---|
| `baseline-product.cs` | `61b3c88e0d095fe164f3107b7cbfd17fcf5f8b8f551023e6a21a6e6d7b5d9120` | Unchanged `42ddc97` verification-service source. |
| `baseline-test-overlay.cs` | `97edfb17df7d8afd3e8a8930f2cb6e370da78f58394110a77c5f4f8862c84064` | Declared initial regression source. |
| `i373-baseline.trx` | `3656ca40afc2f45edf3ba91410bfcd202d936353264e196617d7e909e3177791` | One expected failed case; 521 retained entries. |
| `i373-fixed-targeted.trx` | `25d4eeed9e10c82777287c8b739fbd09a72f032ed7ecc152120e669997e1499f` | 25 affected passes/one existing skip. |
| `i373-fixed-targeted-final.trx` | `3aecfed6048d5069abf9f43a4de2846d2a7050d5031d2416ef619da1521ef137` | 26 affected passes/one existing skip, including concurrency. |
| `i373-fixed-core.trx` | `036b03f9f2118046f4a5a7c8d8a1b11c4b9c362a2a95640af4eed9c13501787e` | Full Core: 4,017 passes/93 explicit skips. |
| `final-product-overlay.patch` | `55b3eaeda59e1b58335508ba9e9155bc98945244b28e77404e4386d5bd6d3766` | Reviewed correction against committed source, including the later comment clarification. |
| `final-test-source.cs` | `77e0d04ec0277e05655f7060a08835a628114874e3ed834cec27c3af70da9ec9` | Both final regression cases. |
| `independent-local-audit-v1.json` | `3727353198d4b05b1975ced6b9c927b38c3d700ae731a4a4212554e30ef27220` | Source/raw rehash and exact predecessor-case comparison. |
| `full-solution-build.log` | `a283491ef6f9c308bc9c91f49dd507d8ad6d13855f1c27b334dcbc37960a1f88` | Final local source build: 41 warnings, zero errors. |
| `i373-final-source-targeted.trx` | `4a945bc40193c9afe05e8300701c449ec01b6960b17f9a274ac444d72d7f25ac` | 26 passes/one skip after the final solution build. |
| `independent-final-source-audit-v1.json` | `3b0b222891975794dd823b1ecddb16a1fed4ff4eac5b3258d4245c5585979b9b` | Rehashes final source/build/test and confirms identical affected outcomes. |

I06 still needs aggregate sidecar bytes, simultaneous consumer and native allocation observations, as well as chosen-artifact qualification. Physical-source validation remains on HOLD; no candidate, contract freeze or stable GO is inferred.
