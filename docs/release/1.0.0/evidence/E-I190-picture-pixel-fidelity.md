# E-I190 — keep decoded picture pixels at their actual size

2026-10-07. Medium picture-fidelity defect under I06/V10; remediated preliminarily at 57204597f8839029aa9f8efa397e8fbb460ecc9c. The viewer's existing actual-size/zoom route and decoded-pixel display motivate this correction; no new media-format or release contract is adopted.

The unchanged 3b84bc1 worker applies Mitchell cubic reconstruction even when decoded and output dimensions agree. Neighbor blending changes patterned opaque pixels and transparency before the display receives them. Twelve supported fixture cases fail: two raw BMP bounds, opaque/transparent lossless PNG and all eight JPEG EXIF orientations. Two BMP/PNG checkerboard downscaling controls pass. BMP/PNG expected bytes come from independently constructed rows; JPEG comparisons use the codec's sRGB premultiplied output before drawing, then independent integer pixel-index mapping because JPEG encoding is lossy. They do not assert original JPEG samples survive compression.

The worker now uses nearest sampling when both decoded dimensions equal the pre-orientation target dimensions, preserving each converted pixel through integer rotations/mirroring. An actual resize keeps Mitchell sampling, including any resize remaining after codec-native downsampling. Color-profile conversion still occurs during decode. Skia's [sampling definitions](https://api.skia.org/SkSamplingOptions_8h_source.html) distinguish single-point nearest sampling and the Mitchell cubic filter. The output format, limits, EXIF matrices, process boundary and containment policy remain unchanged.

Working and fresh locked committed Windows builds pass all 40 picture cases without skips: fourteen additions and the exact 26 prior case names/outcomes. Every supported actual-size expected pixel agrees, including premultiplied transparency and non-square EXIF transforms. Both checkerboard controls blend interior neighbors when shrunk; existing profile, hostile-input, actual-worker, view/zoom, admission, cancellation, ownership and Shell controls remain passing. These are component/worker controls, not native desktop frame or human acceptance.

The first fixture incorrectly assumed this Skia build would consume PNG eXIf orientation tags. Fifteen failures per first-stage inventory are fixture/preflight failures (fourteen orientation refusals and one turned-dimension assumption); the original raw PNG inputs, output/source and assertions remain. Those inventories separately reproduce four unscaled pixel failures. Fresh v3 uses supported JPEG EXIF data for all eight transforms and retains lossless PNG only without a transformed origin; it never labels ignored PNG tags a proven product defect or weakens pixel equality. A clean-source seal initially compares private CRLF overlays with canonical Git LF bytes. The failed seal remains; fresh v7 verifies only that line-ending normalization differs, while independently checking every exact Git blob, archive member and actual artifact. No product/test/CI rerun erases either failure.

The independent clean seal verifies 42 private files, 705 complete payload references, all 1,102 original and 1,106 clean raw source blobs/modes/archive members, actual header/dimensions/flags and independently parsed BMP/PNG/CRC/alpha rows and JPEG index mappings. Clean FileCat.dll SHA-256 527d70a7295cd3a47302e1186d2b76c8bb6ca4d76028838d12858b46359988b7. Only the worker sampling selection and fourteen new controls change relative to parent 9c9e8bd. Original [push CI 37606469638](https://github.com/benny-cz/FileCat/actions/runs/37606469638) attempt 1 is sealed green on Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26. Each full App inventory has 579 cases, preserving all 565 prior names/outcomes/skips plus fourteen; all 56 new native-codec/pixel executions pass without skips. Core remains 898 Windows/893 Unix, with all earlier Remote/Platform names/outcomes/skips retained. All nineteen selected server digests/members, fourteen raw inventories, four compiler receipts and 92 locked graphs are independently checked. Thirty-four CI proofs, the failed collector/continuation and empty attempted proof remain pinned. The collector first omits fourteen names in a historical App fallback; its raw captures and 31 completed proofs remain. The first raw-only continuation omits a datetime import and leaves an empty attempted proof, which is retained separately; fresh v3 finishes saved-data analysis only. No CI/test/build/artifact-request rerun erases either failure. Final catalogue audit first fails because the CI table header uses “Original CI” rather than its required private-root marker; the prior document bytes and failed audit are retained, and only that label is corrected before fresh saved-data reconciliation.

The original [Mac mapped-worker record](E-I03-MAC-picture-worker-images.md) preserves 675,653 differing channel bytes on its larger 1,024 × 1,024 patterned BMP. The repeat payload is pinned locally (142 inputs; archive 9f7d6a938d743d3707ecccc1388588e58f82eea0ba1338b6c4632a1adcf96c96); two bounded SSH connection attempts at 192.168.0.199 time out before any guest command runs. The user restarts the Mac and restores SSH. A fresh ordinary-user native repeat at exact 5720459 now returns every one of the 4,194,304 expected patterned BGRA bytes, zero differing channels, the exact FCPX/BMP header and natural exit zero. Expected/actual pixel SHA-256 is 766d6665108723b79f2d75388a98bd06629daba026be7900b1f3b09cb766a679. Actual worker PID 1255 is absent afterward. Independent Python reconstructs all original BMP rows/pixels, verifies all 142 input references, 44 retained native files and four controller/retrieval/reader/restoration sources. After retrieval and independent validation, all 162 files in the single owned Mac stage are rehashed with UID/path/no-symlink checks, worker absence and no stage-referencing process verified; that stage is removed and its parent preserved. No agent persistent settings, device/window or authorization changes occur. The first failed Mac capture remains at its original 3b84bc1 producer; its native current-file/SBOM subset is not silently promoted to the changed payload. No physical-source/USB testing, contract freeze, candidate, reference/human qualification or stable GO is claimed. Broader frames, formats, revision/race and qualification work remains in I06.

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

Private `FileCatReleaseEvidence/ci-37606469638-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 222015d7eb7e5e2a94f3c23cbadec71fb4ff735dae9bab9f44d10760f2a3b7e0 |
| independent-draft-guard-ci-v1.json | 5374d9e78197732462e203b8bf5e912ce40723755e03d8078289fcf5113c532e |
| independent-fixture-ci-v1.json | 1ab80707d7dcf7946dbc01bb79e1558c8ecbaab5f490cd08b89be7cc1d4884c4 |
| independent-i163-ci-cases-v1.json | a3716c3101164eb3bdfdcc3fe60e05b9b4a768452a63fa615822c8358c8fc8fc |
| independent-i164-ci-cases-v1.json | ec5358f74ba7d98a0d2a3c662cf135c367e9ca248bffc187c6f75972c50bce70 |
| independent-i165-ci-cases-v1.json | 973c7578e7961a38769dcec574584f2ca10201565f59d9507dca19dc3c360c5b |
| independent-i166-ci-cases-v1.json | 5660b01115865c5e845a15e6224d1cff794461fc95ced6786f474fb7d6bcf2dd |
| independent-i167-ci-cases-v1.json | 6e498ecedb1e012bc2028f37ed41827b4da38fd4299fef4a416e4a27bd846caf |
| independent-i168-ci-cases-v1.json | 4cec77b099d99a4435f3272f1d85a6dbf1d8ec39504e122489950366f7011b5e |
| independent-i169-ci-cases-v1.json | 91f8ca025f8cd14fa49bfdd7b08a0812a0545b442f38868cdf5390f605f56148 |
| independent-i170-ci-cases-v1.json | 6b365486d6c537ad8c2fc3a2ab40ee50a1a5ec3286eef4f60cef89c9499b0dae |
| independent-i171-ci-cases-v1.json | 77e1fb520955cc0384a48984a6bb135becc9bbac4741c04c36c5509cf185d8b6 |
| independent-i172-ci-cases-v1.json | 5ef59eed532e06649b2e98b28303bb41654d37991d6a00349cd183f4420a08ec |
| independent-i173-ci-cases-v1.json | 66839336e79c43934725e79cac7f708ab28133587601cec77af8464f2b3297d3 |
| independent-i174-ci-cases-v1.json | 45691bab6db3d8c904078740db7d7fb6b563eb8261393068c506a7f3fc23858b |
| independent-i175-ci-cases-v1.json | d0741cf15004fff4b1cd5bac93889af3f46f2c65efe5e0bead37c11bb86bd306 |
| independent-i176-ci-cases-v1.json | bcfeacc7e8e8c92b55b0b29dca941cb35ca93afbc82a56911e3f0557ed77a71e |
| independent-i177-ci-cases-v1.json | 94f7067082c98239947c3a51839378992a7bcda550c549a73697549d8dbbe70e |
| independent-i178-ci-cases-v1.json | 9401d25f943806673ad3fa646b2fdc4b1272ad262eab16e7f27bd969ba796740 |
| independent-i179-ci-cases-v1.json | 39c02104575adf7ccf4a4da175c573f70a38beb5c2366a1ccd159071537ffbf8 |
| independent-i180-ci-cases-v1.json | 4a7d2f7eea9bc43903108bec7cf8aeeddc6871e0b4b9a5af87fd1574940087be |
| independent-i181-ci-cases-v1.json | 1d87c372e055047c726832c0fd4c817dfceb4448c86df67462e60d852ff8e74a |
| independent-i182-ci-cases-v1.json | 8f043d3744ecc5038c200d312fbaf103da295c9bc5f4faf3a4c98f3b78b4ccd7 |
| independent-i183-ci-cases-v1.json | 83964d9f041098efc8dde85aed7f9ff7eefae33bf0fe1c47a989ff5b4e12255f |
| independent-i184-ci-cases-v1.json | df02b7e7bd18187af3f8bc4c044db04d4143a6f6920a072bdf3bd47da93bb604 |
| independent-i185-ci-cases-v1.json | c8916a44ff8c2f18a060b07b8a7527ec0b1a63ddb2f9152c1020da27036f8f84 |
| independent-i186-ci-cases-v1.json | 17b2b654fa1f5407bd3e37cf97d1878ff3814d7ac5b2158ae1eb6a1091bcddc0 |
| independent-i187-ci-cases-v1.json | c1456e6ba92aaa60243f6d7cbc649a2b2c46a003328196f6560fa83490a99e84 |
| independent-i188-ci-cases-v1.json | 91c8d46ab8d4c95012255da9b7e92a6d81d9a72cb43fbb93537f7c39b0baae59 |
| independent-i189-ci-cases-v1.json | 165432122f0d5f656688ede6dc104f6caaa32e40b631f71ce91e673446bb698c |
| independent-i190-ci-cases-v1.json | 81094ea7a20659ee6d2d1b1644f5606410c422219725bab876f2c6f30e4ecbaf |
| independent-producer-policy-ci-v1.json | bc3007e74777cfb8f8237ac509b36b8a3be8616965aa986c09d3b78fdfb9e6fe |
| independent-restore-ci-v1.json | da4fd6c74d12ea506e462da0967d0d0354b6caf137dbe7b49f41187cd71da431 |
| independent-separation-ci-v1.json | ecc8cde8f0f7d1ea2beab601990d7c321c02613931307b755a116432b7226a77 |
| independent-i190-ci-audit-v3.json | db749fa405605a2f59c8ea5c0a152300ad2ddda2e444e18e34dca526037ec7c8 |
| collector-app-additions-failure-v2.json | 3522d29cfccfbcc9b7d666ecc372eb8a6d26b89b484f534ca5fa176952fffb56 |
| continuation-missing-import-failure-v3.json | 9e1d50fc8d04cc068699f1f974e363d124e09169e1187951f42f5c495d23f341 |
| continuation-missing-import-v3/independent-i188-ci-cases-v1.json | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |

Private `FileCatReleaseEvidence/pf190-v1`:

| Retained path | SHA-256 |
|---|---|
| mac-v19/results/worker-command.json | 0364d812f97696a105ecb232196b134a52ee58f640b0ddee8d09744bc91898a7 |
| mac-v19/owned-restoration-v21.json | 4ee3a33ced02bfd8f795b42fb6681d370358c8c7201d495ccf2a75a3e1bcbf5c |
| mac-v19/independent-native-final-v22.json | 7c9e60e1ea85133049523933dd213d0c4342dbfc6535dd49dcd7ea53eec351bb |
