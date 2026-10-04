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

Clean committed-source CI/guest controls are next. Concurrent real copy/search, broader viewport/page/picture
demand, many-folder/partial-size controls, native frame/AT and exact-candidate checks remain. Overall **NO-GO**
and the physical USB source hold remain.
