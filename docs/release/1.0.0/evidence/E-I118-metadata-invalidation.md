# E-I118 — invalidation rejects in-flight stale metadata

I118/V12/V15, medium metadata truth defect. Windows Insider 26220 host, SDK 10.0.401,
component/headless controls. Source `da3a3d602624b4cf6bb0f4c3314c1ccc60d6af78` plus the two-file overlay
in `independent-results.json`. No native desktop/frame or candidate qualification.

Four controls fail on unchanged production. An owned file first matches its SHA-256 sidecar; the producer holds
that actual verification result while the sidecar changes to an independently generated mismatching digest.
The file's bytes, size and modification time remain unchanged. The actual verification service recognizes the
sidecar change and returns Differs, but full metadata invalidation and single-field Forget both permit the
in-flight old result to restore Matches. Explicit Compute likewise returns/caches Available after invalidation.
The fixture uses the app's SidecarsChanged → metadata invalidation route with an owned verification instance.

Cache publication and invalidation now share a short lock. Each queued/running/explicit production owns a
validity record; invalidation retires the affected records. Queued obsolete work is skipped, running calls
finish at their safe boundary and their values cannot republish. Invalidated explicit computation returns
NotRequested. Single-field Forget preserves unrelated work. Completion removes the demand record and wakes
visible rows to request fresh data; scheduler-canceled queued work also releases its record. Device limits,
cache limits and revision keys retain their existing policy.

An intermediate publication-only remedy passes API polling/full Core checks, but four stronger event controls
fail: once the initial invalidation redraw occurs while the old producer is held, finishing it sends no retry
notification. The complete remedy also notifies after obsolete demand ends. These four intermediate failures
are retained separately. Final controls wait for both actual notifications before issuing fresh Get calls.

All six final controls pass. Two positive controls verify selective invalidation and a rapidly changed viewport:
1,000 queued/offscreen demands never call their producers, two held workers do not block another device, latest
rows eventually become available, and an interactive request precedes their background work. The final priority
fixture frees one worker while retaining the other, avoiding an observer race between dequeuing and execution.
These assertions are component evidence; native request/queue traces and UI-thread timing remain open.

Full Core passes **736/782 with 46 declared skips**; full App passes **263/284 with 21 declared skips**.
Independent checks verify all 477 retained input files across baseline/intermediate/final stages, six source
copies per stage, final active assemblies/shared Core bytes and direct case/skip inventories. An initial
three-case/two-failure baseline and earlier passing polling/full-suite outputs remain retained, with their own
exact input manifests. Qualification uses `verified-core-v2`, `core-full-v2.trx` and `verified-app`/`app-full.trx`.

Private root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/i118-metadata-invalidation-20261004`.

| Evidence | SHA-256 |
|---|---|
| Four-failure/two-positive baseline XML | `182aebc35021d1e9481564c7542adb0ce5d4726b426486da79fa0d38f0c79ca9` |
| Intermediate missing-retry XML | `76cd3865484d09edc3feae2277dbaad79aab901b7eec8d8c9bc16c00e64963e9` |
| Final Core / App XML | `2c8bcfce3b13a2c7bd63cb64dfde404bcbedd81e8cb44ec349d8010b36ceffab` / `449af46abc2b1112131dc6a585fb40cab110c784d45b2ae8790ae307ce031cbf` |
| Final Core / App input manifest | `e19484e5c398b7e6db458d2fba0b12a11e53e74f13e27042ed31864bee824e04` / `e2a3a799d8d8c01823657b89678850a8eb7690c1dee6c3ef010f43a5d3f6efe5` |
| Independent input/case/skip inventory | `29270837c64a59d9a62234c779f5003f8e60aafb9be5994eea53c12622e6c4bb` |

Clean source `28961085ed490381afbb5e7b19600c212886bbe4` passes all four required lanes in
[CI run 37179889361](https://github.com/benny-cz/FileCat/actions/runs/37179889361); three package jobs skip.
The three actual downloaded artifact ZIPs match GitHub's server digests. Six direct XML inventories verify:
Windows Core 735/47 skips, App 269/15, platform 165/33 and Remote 88/28; Linux/macOS App each 248/36.
All eight selected metadata cases and both checksum UI cases pass in direct Windows XML; both App cases also
pass on Linux/macOS. ARM64's job/log status is retained; there is no direct ARM64 XML artifact in this run.
Raw metadata/log/ZIPs/XML and the independent inventory are under the sibling `ci-37179889361` root.

The exact self-contained win-x64 payload passes eight Core metadata and two headless checksum UI cases in the
Windows Insider 26300 VM, with zero skips, ending at **2026-10-04 05:38:47 UTC**. All 680 payload files,
681 ZIP members, eight raw/canonical source copies and retrieved output pins independently verify. The
controller PID 12516 and test PIDs 8156/3960 are absent, and the owned temp folder is empty at 05:40:26 UTC.
The guest UUID is `9D224D56-1161-A849-ABA7-2581A980895C`; its source/device/desktop flags explicitly record
no physical-source access or native desktop interaction. Guest root:
`C:\Users\Public\FileCat-metadatademand-validation-43eb2da6b5854c5ca0b1b4ce2727c07c`.
Retained host payload/scripts/request/XML/cleanup are under `clean-2896108` in the private I118 root.

| Clean-source evidence | SHA-256 |
|---|---|
| CI metadata / full log | `b7fc84d8bef9cc960c9d9dbeb0ee12d96565ee8eb520a15cfd97686aca0bd99c` / `f00cba3ea532fc97fab22d7908796732703da24498937bd1d766d1ec9f8ce814` |
| Windows / Linux / macOS artifact ZIP | `31c9a8dcd0f761e0c75cba77cd239a94199ce85afa8c64b28808a3a58d0ac9c1` / `993380a57703d8a60a59b51245b408c2d8a06f201f3960af65094eba29e6fc13` / `c96ea57dcbff49c8eddd0cc848147dedf305d0d1d165c9fce0e0b60bf3f0bc96` |
| Independent CI inventory | `36c798554a2751f35cea67911e8a44b96f799e69b12f5731238504cfac26f520` |
| Guest input ZIP / manifest | `b3bb31b24b309a1eba9f15d98989fd421d1b408238b90d441ee26a2a9f70f503` / `afa35c689c0b57e683d401242ee903318bd46a20146061bb249a0316a50a3123` |
| Runner / cleanup script | `5624a3d0a4b99393391ba609b222035b21270d18f8f5b324787abc5d1b2e0aca` / `9ce1cf3b8cc68c3098935a484559f86710d5f59688cd7d5d863e6613f5b95e90` |
| Guest Core / App XML | `1b701f6cc50120e120468fde16af5ea881c77e45519f2de41aa336f59a770d15` / `e7ac8cd1c1afc54e2d42951fab60f4bdc51c7e78bded0ce0dac95a2f625c3944` |
| Guest cleanup / independent payload/output inventory | `6d707e5eed3fa1569d95e3dd3b5059efcdc5e09c5de3bd6694ac40161a3b6974` / `b3b9cbf0e152ce69cd2d1ed82a981d5b7eb5edd322c31de80bc4df46bba41bc9` |

Concurrent real copy/search, broader viewport/page/picture demand, many-folder/partial-size controls,
native frame/AT and exact-candidate checks remain. Overall **NO-GO** and the physical USB source hold remain.
