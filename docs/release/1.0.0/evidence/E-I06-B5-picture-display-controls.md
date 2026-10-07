# E-I06-B5 — picture display raster controls

Recorded 2026-10-07. **Preliminary unchanged-product component evidence; I06 remains Open.** Exact product source is 57204597f8839029aa9f8efa397e8fbb460ecc9c; documentation discovery is 85e079bd06226f129ccb9951cd3180cb9ccb06af. No product change or new issue ID is established by these controls.

Following the [I190 worker pixel correction](E-I190-picture-pixel-fidelity.md), this separate probe checks the actual PictureView rendering path. It supplies an owned 32 × 18 BGRA bitmap directly, lays out the production control and calls RenderTargetBitmap.Render with Avalonia's actual Skia drawing backend. The two overlays are test observers and the existing test builder's UseSkia / UseHeadlessDrawing=false setting. This does not run a desktop window, decoder process or full worker-to-view integration, and it does not substitute a mock DrawImage implementation.

| Control | Actual result | Oracle and limit |
|---|---|---|
| Pixel-aligned actual size, opaque pattern | All 2,304 channel bytes exact. | Independently reconstructed source pixels; zero offsets, 32 × 18 raster. |
| Pixel-aligned 2× enlargement, opaque pattern | All 9,216 channel bytes exact. | Each original source pixel becomes an exact 2 × 2 block in 64 × 36. |
| Pixel-aligned actual size, alternating transparent/opaque pattern | All 2,304 channel bytes exact. | Opaque pixels retain their bytes; fully transparent pixels reveal the independently reconstructed 16-pixel checkerboard. Intermediate alpha is not covered by this display control. |
| Pixel-aligned 2× enlargement, alternating transparent/opaque pattern | All 9,216 channel bytes exact. | Exact expanded source pixels and the checkerboard's actual view-coordinate tiling. |
| Fit at 50%, alternating black/white source | All 294 interior RGB channels are 127; output alpha is opaque. | Verifies blending when shrinking instead of aliasing to black or white. Edge-filter and general resampling accuracy are not fully qualified. |
| Fit at 50%, opaque coloured pattern | Native 16 × 9 raster retained; opaque output. | Diagnostic capture, without an invented exact resampling oracle. |
| Actual size in an odd 33 × 19 view | Production centres at (0.5, 0.5); native raster retained. | Comparing that shifted raster's original-size region with the unshifted source yields 1,704 channel differences. This diagnostic does not establish pixel-aligned rendering or a product defect; native fractional placement/DPI/drag workflows remain queued. |

All seven raw cases pass their observation checkpoints without skips. The independent Python reader verifies every PNG chunk CRC, inflates all 6,675 raster pixels and compares them with the raw test observations; it separately reconstructs the four aligned expected outputs and the shrinking checkerboard oracle. It verifies all 1,106 canonical Git blobs/modes/archive members, both actual 141-file payloads (282 references), both complete source exports with only the declared test overlays, all 22 retained files and the exact native drawing-builder source. Both bounded commands exit naturally with code zero and no owned test executable remains at sealing. The enlarged transparent raster was visually inspected as a component capture.

The suspected extra filtering of pixel-aligned actual-size display is not reproduced. These finite controls justify keeping the production interpolation behavior unchanged. They do not close I06 or qualify other render scales, partially transparent pixels, colour-managed display, pan/fractional alignment, DPI changes, queued frames and GPU/compositor lifetime, native keyboard/mouse, Shell/provider paths, reference performance, human/accessibility testing or the installed candidate. No persistent machine, physical-source/USB, contract, candidate or publication change occurs. The next executable review covers source revision and picture-worker ownership/bounds.

Private `FileCatReleaseEvidence/picture-display-20261007-v1`:

| Retained path | SHA-256 |
|---|---|
| native-v1/command.json | f277df55c93d26ca00aa0558817a3d8c3950b1808daad50f8dfa4e5c0800ef01 |
| native-v1/results/display.trx | c2543cbd3cab52533302ae995d295a2fccb26a024a93266cf5e9d5f7ea03fb7f |
| native-v2/command.json | 1d1ddb94d47809855c9e73f25da0e72150818e97d56cffac4b7f0ab9966bf217 |
| native-v2/results/display.trx | 04ac33f7230f38038180226dca404c2c5b4e2430d389846860316e6ecf477ae6 |
| native-v1/native-33x19.png | a0260e45814b68a5a468f2dfa15851c2d8ed51cb823fecd77d9e94928cc99443 |
| native-v1/native-32x18.png | c673ea1f16b22986355c87b8383078b73bb9e0b148d6305ead49c462496e1ba5 |
| native-v1/native-64x36.png | e698de4162c4d1198b418d472b6bae2e430bbc21248781a5ea0b6cfc524873ee |
| native-v1/native-16x9.png | 4425df77d9b18890f3e0cd742cf48c60d82fb889283fa483c903bc28add875f4 |
| native-v2/native-16x9.png | c4ecc8b8b4d29fafdd87f1bfe57ca4ea8c14d95eb74c1df4ba31427bb7cf6abb |
| native-v2/native-32x18.png | 32757f7d15f3950d9ebc34a87cec1ff362b4082110cccbda10b7b034a3c0b18e |
| native-v2/native-64x36.png | ff814e4e56a3c3c5ba8740cf37c22372c263c998cfcc5cab5b9475cd1a3a1f22 |
| seal-display-v3.py | d3b8a039fcd0ef88cb67ae9261046f5c77b357fdd8fc21512bb03a8c409bd0d6 |
| independent-display-v3.json | eac33545fe5acf906fe2230c17521a56a40d315302fb1035dab4509ee9af23ec |
