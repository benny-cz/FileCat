# E-I190 — keep decoded picture pixels at their actual size

2026-10-07. Medium picture-fidelity defect under I06/V10; remediated preliminarily at 57204597f8839029aa9f8efa397e8fbb460ecc9c. The viewer's existing actual-size/zoom route and decoded-pixel display motivate this correction; no new media-format or release contract is adopted.

The unchanged 3b84bc1 worker applies Mitchell cubic reconstruction even when decoded and output dimensions agree. Neighbor blending changes patterned opaque pixels and transparency before the display receives them. Twelve supported fixture cases fail: two raw BMP bounds, opaque/transparent lossless PNG and all eight JPEG EXIF orientations. Two BMP/PNG checkerboard downscaling controls pass. BMP/PNG expected bytes come from independently constructed rows; JPEG comparisons use the codec's sRGB premultiplied output before drawing, then independent integer pixel-index mapping because JPEG encoding is lossy. They do not assert original JPEG samples survive compression.

The worker now uses nearest sampling when both decoded dimensions equal the pre-orientation target dimensions, preserving each converted pixel through integer rotations/mirroring. An actual resize keeps Mitchell sampling, including any resize remaining after codec-native downsampling. Color-profile conversion still occurs during decode. Skia's [sampling definitions](https://api.skia.org/SkSamplingOptions_8h_source.html) distinguish single-point nearest sampling and the Mitchell cubic filter. The output format, limits, EXIF matrices, process boundary and containment policy remain unchanged.

Working and fresh locked committed Windows builds pass all 40 picture cases without skips: fourteen additions and the exact 26 prior case names/outcomes. Every supported actual-size expected pixel agrees, including premultiplied transparency and non-square EXIF transforms. Both checkerboard controls blend interior neighbors when shrunk; existing profile, hostile-input, actual-worker, view/zoom, admission, cancellation, ownership and Shell controls remain passing. These are component/worker controls, not native desktop frame or human acceptance.

The first fixture incorrectly assumed this Skia build would consume PNG eXIf orientation tags. Fifteen failures per first-stage inventory are fixture/preflight failures (fourteen orientation refusals and one turned-dimension assumption); the original raw PNG inputs, output/source and assertions remain. Those inventories separately reproduce four unscaled pixel failures. Fresh v3 uses supported JPEG EXIF data for all eight transforms and retains lossless PNG only without a transformed origin; it never labels ignored PNG tags a proven product defect or weakens pixel equality. A clean-source seal initially compares private CRLF overlays with canonical Git LF bytes. The failed seal remains; fresh v7 verifies only that line-ending normalization differs, while independently checking every exact Git blob, archive member and actual artifact. No product/test/CI rerun erases either failure.

The independent clean seal verifies 42 private files, 705 complete payload references, all 1,102 original and 1,106 clean raw source blobs/modes/archive members, actual header/dimensions/flags and independently parsed BMP/PNG/CRC/alpha rows and JPEG index mappings. Clean FileCat.dll SHA-256 527d70a7295cd3a47302e1186d2b76c8bb6ca4d76028838d12858b46359988b7. Only the worker sampling selection and fourteen new controls change relative to parent 9c9e8bd. Original push CI 37606469638 attempt 1 is pending; earlier green CI remains at 3b84bc1.

The original [Mac mapped-worker record](E-I03-MAC-picture-worker-images.md) preserves 675,653 differing channel bytes on its larger 1,024 × 1,024 patterned BMP. The repeat payload is pinned locally (142 inputs; archive 9f7d6a938d743d3707ecccc1388588e58f82eea0ba1338b6c4632a1adcf96c96); two bounded SSH connection attempts at 192.168.0.199 time out before any guest command runs. The repeat remains queued awaiting Mac reachability; no guest stage/helper/settings/device/window or authorization change occurs. No physical-source/USB testing, contract freeze, candidate, reference/human qualification or stable GO is claimed. Broader frames, formats, revision/race and qualification work remains in I06.

Private `FileCatReleaseEvidence/pf190-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v3/command.json | 970c8c36351a41b6344d406c1aa9a232691d96c17a096836e43bf5eb7fbdacb2 |
| baseline-v3/results/baseline.trx | 5cfe5849f1f1ea761d4fa49be7cf5f239b12e37ca1073457469d7882215e6f58 |
| working-v3/command.json | d93b3b84520aaa892f65351933992c6345f0f15f14172586be609517ce3dcec6 |
| working-v3/results/baseline.trx | eb99988d4ffd374f5f13bc62c5662932eeed38d257fe75a03cac32b2c61e29e8 |
| independent-working-v4.json | aa3ccf69360666f72b6118a5085b13b748ea77bec90aae75187dfe056b8b7fba |
| clean-v5/command.json | bb81750a65ca8526e6052c6b5a75d1ba9b4caaf2dfbf2db65f549bc563a940c1 |
| clean-v5/results/clean.trx | c42671524b3e732899e4c14f0a8ae107beacf82cb60f473a64479499e3745437 |
| independent-clean-v7.json | 3ab698a03703c9c30e3148830cd7772f44063ce2702bcecdbcfd52aa612c76d2 |
| mac-v8/stage-create-command.json | 451962cf59a83aacd0fe4e9334b876706e67a23f3115e4ee23224f62c4969d5d |
| mac-v8/readiness-retry-v10.json | 14e2bf218e4347e59400153fab5f4ad65e40bf56cf4d7a3e8f1977b100449054 |
| mac-v8/inputs.json | c8b64d6e0c6f422104597ee620662773af11897206c9c5d04d3c4413eb73026b |
