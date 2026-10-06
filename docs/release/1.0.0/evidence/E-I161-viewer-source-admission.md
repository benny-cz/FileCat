# E-I161 — viewer header and visible page source admission

2026-10-06. High responsiveness/resource-admission defect under I06/I08/V12. Preliminary committed/native/CI remediation at **8975fdf11a733535d640609b827557842002a14a**; broader consumer and final candidate qualification remain open. No release candidate or human stable GO.

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

## Exact committed native and CI seal

Clean export verifies **932 raw Git blobs**, blob identities, archive members, unchanged source files and all four native helper publication modes with their actual compiler reports/output receipts. Three self-contained App test payloads are compiled from that export. Windows 26300 guest passes **26/26**; Ubuntu 26.04.1 UID 1000 and physical macOS 27.0.1 UID 501 each pass **25/26 with one declared skip**. All three picture-device demand cases, including the new header case, actually pass in every environment. Each retrieved complete XML/TRX inventory, unique execution identities, transport/payload/result pins, exact fixture bytes, disposal observations, test-runner absence and empty owned test temporary directory independently verify. The Windows payload has 354 pinned files and each Unix payload has 350. No persistent guest/Mac setup changes or physical USB source tests occur in this slice.

[CI 37451501259 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37451501259) succeeds in all four required lanes. Ten original downloaded artifact server digests, four clean source/build receipts, fourteen complete per-case TRX inventories and both native Windows executables/report-map sets verify. All 409 App display names and Core names match with explicitly recorded native path/Windows-only case differences; display multiplicities and unique execution IDs are retained. The new header and page lifetime cases actually pass in each lane (**eight executions**), and all fourteen I160 nonce cases pass across Windows x64/ARM64. ARM64 startup/drawing and installer compilation succeed; tag package jobs remain skipped. Actual generated installer bytes are not retrieved from this run; compiler recipe receipts are producer observations.

The failed original run **37446927662** remains failed. This successor establishes preliminary regression remediation, not native GUI input, physical-source zero-write evidence, complete I06/I08/I17, artifact publication or final candidate qualification.

Paths below remain relative to the private root above; `../` refers to its `FileCatReleaseEvidence` parent.

| Retained path | SHA-256 |
|---|---|
| clean-v1/producer.json | 17189d6004d64c8e9fa6a9bd79f3573e3c63dbf7838b9a8141621d06edf398f7 |
| clean-v1/windows-executed/independent-guest-v1.json | 77ed35937b0958fdfe7f014508981393772521fe10afcea6523e9bafcde0e13d |
| clean-v1/linux-executed/independent-guest-v1.json | 831a35dfc8a2cace06d147424d65257bf63d4cd5b14609762b67eb7ce4d9a05c |
| clean-v1/mac-executed-v3/independent-native-v1.json | d50890d334dbca662dd980d1db65d03e83e0272d91d2d7056d4630aa1e924267 |
| independent-committed-native-v1.json | d2ab8ee1ef9ef37d55099fd7321cea984e9303187d4b2fc8839e0cda2df61407 |
| ../broker-loader-20261006/viewer-committed-v1/producer-native-bootstrap-v1.json | 7e5a453eb36fedcf4fcb190252e739dfe87e1c44f9054f0f83db63640203bc72 |
| ../ci-37451501259-attempt1/independent-ci.json | 4565e27fa7fb75943751b9f8f89a2e626fd145175f563ae4595fca2ca625f948 |
| ../ci-37451501259-attempt1/independent-ci-native-bootstrap-v1.json | f3b3d65960e7f24e6f077f9d63172b91bce23c4c20a96a4730c83da0057f4f96 |
| ../ci-37451501259-attempt1/independent-ci-native-inputs-v1.json | c469e56466b63f2c3c916526ea5c50b257d41c9cf460d76cfda632c3bfdf0900 |
| ../ci-37451501259-attempt1/independent-ci-nonce-v1.json | 2614af49bb4a4214a477ace0a06be0a26bc819538ca2db2a5149cb77a7230be4 |
| ../ci-37451501259-attempt1/independent-ci-viewer-v1.json | 6e7ab751f1fa471be062de52dfd0f85e61c4ac36f1ec8f66304d9dab171effc7 |
| independent-seal-v1.json | 149c1d117ffbe3098986fdcc8f8e8996d3110560e08a4436f4d74374c17f67e7 |
