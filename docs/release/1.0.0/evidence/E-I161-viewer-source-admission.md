# E-I161 — viewer header and visible page source admission

2026-10-06. High responsiveness/resource-admission defect under I06/I08/V12. Preliminary source correction and host component validation; exact committed native repeats, successor CI and candidate qualification remain pending. No release candidate or human stable GO.

The original exact-source macOS CI run **37446927662 attempt 1** on **37c88c8498f1a00693646a044c1149629748ccef** fails the existing picture-viewer demand case: expected two held reads, observed three. Its original failure remains retained in E-I160; its historical scheduling path is not established by that log alone.

A new controlled headless viewer case on unchanged App production code independently reproduces three simultaneous first reads on one provider device. All three captured stacks run through `DetectEncodingAsync` on the general thread pool. A separate healthy device renders its exact 44 × 30 picture. The retained original observer has a test-source overlay; it is not represented as a clean committed test build. A header-only working correction still fails: the first two calls now run on device workers, but a visible `PagedReader.RequestPage` demand bypasses that admission. This failed intermediate control is preserved.

The correction schedules both viewer encoding/header reads and its asynchronous page demand through the provider's shared device scheduler. Header demand cancels when the viewer closes. The existing three-parameter PagedReader constructor stays binary-compatible; a new optional scheduled-demand overload is used by the viewer. Cancellation removes queued page demand without reading; active demand stays pending and keeps its source alive until the actual synchronous call returns. Blocking reads by other consumers are not automatically scheduled by this overload, and broader I06/I08 remains open.

The final controlled comparison uses **identical final test DLL, common final Core DLL and every other payload byte**; only `FileCat.dll` changes between the retained original viewer and the corrected viewer. The common Core includes the new optional admission implementation, while the original viewer still calls its retained old constructor. The original viewer admits three calls and fails; the corrected viewer admits two, keeps the third absent and passes. Both runs record exactly one disposal per source, zero disposals during source calls, unchanged fixture hashes and natural test-runner exits. This is headless component testing, not native GUI interaction or physical-device qualification.

Host affected tests pass **3/3 App** and **17/17 Core lifetime/cache**. Full working-production suites pass **Core 820/56 declared skips/876** and **App 386/23 declared skips/409**, with no failures. The full App run precedes the final additional cleanup logging. A rebuild attempted during that run fails on its held executable; after the run exits, the rebuild succeeds and the isolated final observer comparison passes. The failed build and original controls remain evidence, not successful validation. No physical USB source or guest setting is changed.

Private root: `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\picture-header-20261006`.

| Retained path | SHA-256 |
|---|---|
| baseline-v1/producer.json | 3d9fa0181177a5ce8d1b7bb91c7eacad860eca8f469ca18d0eb75aa6be00b4f6 |
| baseline-v1/header-baseline.trx | ea442db91e86a15fbe798b80d4cee46403f1e924ca26bb3adebc6679389bbe92 |
| working-v1/header-working.trx | aaa4f4110a50d43ef1c77f11f569f20c6fabfeb3735dcd6b7aa49f7eee90d2d3 |
| working-v2/header-working.trx | 0f18388fb6c1a98643dc55fd460d9af8687b14650d5ed677e98a5c2c48a9d706 |
| working-v2-core/page-lifetime-working.trx | d8e990cc6b542be7844eb2bc683e0953e6147f56a3f1af18e9c1a25032d34585 |
| working-v2-full/core-full.trx | 1f1b4780ab83ff1210dd95dd87e6d2c8113cb0ed7e15fb637a96d7af976e253b |
| working-v2-full/app-full.trx | 4aaeae7763e1a57e4fc551605414465f5b4268854b63c96eb4678162b0384781 |
| final-comparison-v1/independent-comparison-v1.json | 99a2937ff05ffa93ee6b0299327648db0e500cae445e60e2ef1125a719837d20 |

Current source checkpoint: 139/161 preliminary remediations, one Closed, 21 remaining issue remediations. Every campaign still needs final candidate qualification; NO-GO.
