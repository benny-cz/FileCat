# E-I06-B4 — bounded long-line rendering controls

Recorded 2026-10-07. **Preliminary unchanged-product component evidence; I06 remains Open.** Product source is exactly fa13957ac8a34ac4075bfc7ae1da4d750f43e96c. Documentation discovery effaa90. No product fix or new issue ID is justified by these controls.

The I181 preflight used two very long report lines and could not reliably distinguish held-read orchestration from rendering work. This separate probe preloads and verifies an owned 262,144-byte ASCII source before laying out the actual TextViewer in unwrapped report mode at 1000 × 760. No provider call is held. Source bytes remain unchanged.

| Control | Actual result | Scope |
|---|---|---|
| Original headless mock drawing plus vector recording; newlines every 80 bytes | Pass; 41 rows of 79 characters; render/record phase 2556.9901 ms wall / 3421.875 ms process CPU. | The existing mock font/drawing backend and vector recording are part of this result. |
| Exact same mock payload; no newlines | Layout reaches sixteen rows of 16,384 characters. No render completion at the bounded fifteen-second observation limit; 15,609.375 ms externally observed process CPU increase. Owned process tree is stopped; no complete TRX exists. | Fifteen seconds is a probe limit, not a frozen product acceptance threshold. The retained timeout does not establish a native desktop product defect. |
| Additional original mock drawing inventory; 1024 characters | Pass; one row; 1539.5746 ms wall / 1687.5 ms CPU. | GeometryDrawing/nested DrawingGroup are retained. The initial observer asked for a Bounds property rather than GetBounds and did not traverse nested glyph geometry; null diagnostic fields do not prove missing output. |
| Actual Skia offscreen raster; newlines every 80 bytes | Pass; 41 rows; render and PNG save 117.7275 ms wall / 125 ms CPU; process private bytes after capture 34,652,160. | Actual raster is 1000 × 760. |
| Exact same Skia payload; no newlines | Pass; sixteen rows of 16,384 characters; render and PNG save 546.3582 ms wall / 671.875 ms CPU; process private bytes after capture 59,199,488. | Actual raster is 1000 × 760 and text clips at its right edge. The final raw trace contains after-render even though the controller's last live snapshot printed before-render. |

The native drawing control changes only the test builder to UseSkia with UseHeadlessDrawing=false and uses the existing locked dependencies. The observer changes its capture to RenderTargetBitmap and saves the actual PNG; product files remain canonical. Both native rasters were visually inspected, and the independent verifier checks every PNG chunk CRC, decodes all 760,000 pixels and establishes nonempty blue text on white. Normal/giant captures contain 94,480/62,160 blue text pixels, respectively. These files are component captures, not main-window README screenshots.

The independent seal checks all 1,086 canonical Git blobs/modes and the original archive, all three actual 141-file build inventories (423 file references), exact preceding payload reuse in both giant controls, raw passing inventories, all five traces, unchanged owned inputs and no remaining owned test process. Its 47 retained files include controllers, overlays, failures and output. A verifier preflight incorrectly assumed the archive filename canonical-source.zip; the retained failed checker/receipt remain, and the fresh verifier uses the actual source.zip without rerunning tests.

Timing includes offscreen raster/vector capture and PNG save on the shared elevated Windows Insider host. It does not establish native desktop frame/input performance, the exclusive reference-machine budget, Unicode/combining/bidi coverage, unlimited-line acceptance, human/accessibility qualification, installed candidate behavior, loaded-module provenance or physical-source safety. No persistent machine setting, physical source, candidate, contract or publication changes. The next executable I06 review is viewer checksum worker cancellation and current completion ownership.

Private `FileCatReleaseEvidence/long-line-layout-20261007-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | 958c4f026ecf690899cbde7b56093d1a268b3cd990cbda9f81010ba06842b529 |
| baseline-v1/observations.jsonl | dde2f36bf472344a2c403150e478fa11a2eaf0c408956467f903032ff22d8364 |
| baseline-v1/results/baseline.trx | eea15d7eba263e6f0e7264bc5a72dc02d555e9e52e30dfc1a3c64b476dcbb848 |
| giant-v2/command.json | 663e51daeafdc199cb4d4ec9802255e9089e7fc901a306ea0f64847cdf166917 |
| giant-v2/observations.jsonl | 65113d4ed5df2ebe467d073ffc854f1a94409599ffba68b53b6a156c242e216a |
| normal-v3/command.json | 311b1dc4012843e48ef61977bb2e55e4bdeb24f929262afb9206a724b8bc8d25 |
| normal-v3/observations.jsonl | 301060e52fd2feb143427c8f3b979d245582ba425acaf06bf134df70967c4a75 |
| normal-v3/results/baseline.trx | 711b0c59ccc42878809e7bd6fee36036d447c9b14ba1089c08bccc3273f6cc20 |
| skia-normal-v5/command.json | 6040ef2eaf96434fb769f5f52b11f62078a573c3af0f8597ab92ce2d12402ee2 |
| skia-normal-v5/observations.jsonl | 2a78c3daca8c7d89da68b1e831c3c8cbe6a06f763f8cc7b9fb67b1ed42d42bcb |
| skia-normal-v5/results/baseline.trx | 5d3e7e8ef018755a15f299f31e218b9e0a07c7222916793e009e8d6d46a6c113 |
| skia-normal-v5/owned-render.png | 93b10392c90beca53f1a18622685e37c66ff73f338e84b680aa28c4258775b39 |
| skia-giant-v6/command.json | 503526ab3b74dcd32d95ff07e3b208fd9e9418e3a7701363968c3df512ee320b |
| skia-giant-v6/observations.jsonl | 6cfc7a8e376805239ffd95ee3d825615bf205f430545a30fb4b04442fc18251a |
| skia-giant-v6/results/giant.trx | 192275becee99d8d2a2e87b1bb107d3e1cfb86704f120dea02de3874398202c4 |
| skia-giant-v6/owned-render.png | e0ed47574a1b46d9e7cbe1f59ee45b63e8bfb5666f026d0098d76f73aa402bd1 |
| independent-layout-v8.json | e5b59cb6c05badf8853ff01dacd6f1d41296f61bcc4ed4315d98a9cd759a04c1 |
