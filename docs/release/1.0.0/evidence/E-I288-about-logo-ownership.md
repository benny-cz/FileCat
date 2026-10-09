# E-I288 — About owns its logo through modal completion

2026-10-09. Original **63f5abad5abd6fef69e38cb8e03497e612fa5ae1**; five complete 1352-blob exports retain only declared fixture, product and separate Skia drawing overlays. Every image-bearing About header created a bitmap, but modal completion neither cleared the Image source nor disposed the bitmap. A held retired dialog therefore retained a usable bitmap until later collection. This establishes missing deterministic ownership, not an indefinitely rooted leak or ordinary native UI incidence.

ShowAsync now owns the logo for its whole asynchronous scope, clears the retired Image source in finally and disposes the bitmap afterward. Header construction uses that owned Image. DOS Commander's text header allocates no logo. Existing author, tagline, facts and action text remain unchanged. Copy, Show data folder, Close and cancellation retain their behavior.

Twenty-eight actual overlay cases cover seven themes and all four close routes, repeating each three times. Twenty-four original failures/four text-only passes plus a separate disposal calibration give **24 failures/5 passes**. The observer positively accesses each bitmap while open, then holds its Image/body/bitmap after the real modal task completes and verifies the body has no TopLevel. The original retains **72** readable bitmap owners across the 24 separate image cases; the correction clears every source and disposes every owner before fixture cleanup. The four DOS cases retain their text-only behavior. The disposal calibration independently proves public PixelSize access distinguishes live and disposed bitmaps. No GC closure oracle is used, and GC is not claimed to have been prevented.

Both default headless drawing and separate actual Skia runs reproduce the original 24 failures/5 passes and pass **all 29 corrected controls**. The default mock bitmap is 1×1 and supplies no native allocation evidence. Skia decodes the actual **256×256** PNG; those dimensions are checked against the exported asset. Pixel extents are not measured process/native allocation, peak memory, submitted-frame retirement or a native desktop workload. Held cohorts and per-run roots remain explicit.

The unchanged compiled full App suite passes **1398/25 exact skips**. All **1394** preceding App outcomes/messages and exact skip reasons remain. All 28 modal semantic observations match the corrected default controls. The first private fixture failed compilation on an unavailable GetVisualRoot extension; that untouched command/export survives, no tests ran, and a fresh observer uses the already owned window.

The selected final receipt records every archived, rehashed, removed or locked file under the five exact owned temporary roots. Exact committed/original hosted checks and broader dialog/picture/icon/native/reference/candidate ownership remain. No host UI, physical source, contract freeze or publication occurs; I106/I110 HOLD and required owner decisions/human GO remain.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute; nested receipts retain the complete sources, commands, payloads, failures and restoration.

| File | SHA256 |
|---|---|
| `i06-about-logo-ownership-20261009-v4/AboutLogoOwnershipTests.cs` | `78faaf252ce5667976b2fea5af346ecef6baf0aab7b1153cbf5d3385e57ecade` |
| `i06-about-logo-ownership-20261009-v4/AboutDialog.cs` | `e5785da22ce3b1ddd88a0d5af51903b2c082252bfc8f5cb7c0cd7be9ae36cc25` |
| `E:/FileCat/artifacts/release-evidence/i06-about-logo-ownership-20261009-v2/original/app-controls/command.json` | `c3056d5e809ef22567f298fe5b8660e738cc81422fbb64334ad522d99474f7ae` |
| `E:/FileCat/artifacts/release-evidence/i06-about-logo-ownership-20261009-v3/original/app-controls/command.json` | `7ca0b3e50f70c6a64a36fead2cbd7a7649e2100d6fe25fcb789bfda8717f194f` |
| `E:/FileCat/artifacts/release-evidence/i06-about-logo-ownership-20261009-v4/fixed/app-controls/command.json` | `742ebdfaa52b85612e90cd3c1b83132a24719227ef2423a91d28d5128e6c7371` |
| `E:/FileCat/artifacts/release-evidence/i06-about-logo-ownership-20261009-v5/fixed/app-controls/command.json` | `f44ddb9e1a9ead2b386eb72a68b8d9e9cf1bd0f05d464b0108e439e2cf45d9b1` |
| `E:/FileCat/artifacts/release-evidence/i06-about-logo-ownership-20261009-v4/fixed/full-app/command.json` | `e57f5396c34adf5f145d1e9f374dd106654ca6456b6e89e0bb1e88d41fec34cb` |
| `E:/FileCat/artifacts/release-evidence/i06-about-logo-ownership-20261009-v1/original/app-controls/command.json` | `217ef97f57356c4a837a8aff291abbd12c4be8761241c886bee96ba1b8988d8b` |
| `i06-about-logo-ownership-20261009-v2/preserved-build-refusal-v1.json` | `bee7a05a38c183a37acfb6e25d6fe908622a77ebdcc818b46a3f387059fae1ad` |
| `i06-about-logo-ownership-20261009-v4/seal-about-batch-v1.py` | `6c3221b6a02089c42590a15ef53c9add4f24620dca8d41cd84b31c7b9cf769ac` |
| `i06-about-logo-ownership-20261009-v4/independent-about-batch-final-v1.json` | `59fd97bbb933ddf41206ad0122323b388b74975fceaa8393caab23968df9185d` |
| `E:/FileCat/artifacts/release-evidence/i06-about-logo-ownership-20261009-v4/owned-temporary-files-v1.zip` | `a7d16ed79bcac9e2384227763285be29cc5c0127a317043cb2e0c260a4ede3b0` |
| `i06-about-logo-ownership-20261009-v5/FileListSmokeTests.cs` | `9a4c09625b50c38f475c4a708e5fb68c31f775678aa98b13dba764cd61b70062` |
| `E:/FileCat/artifacts/release-evidence/i06-about-logo-ownership-20261009-v4/fixed/source/src/FileCat.App/Assets/filecat.png` | `6025669ab8d5d4a33ab5853e5c6bf7882d954f8f683074cc79e40a2f5db8d5aa` |
