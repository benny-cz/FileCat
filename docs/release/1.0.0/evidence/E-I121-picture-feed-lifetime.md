# E-I121 — picture feeders retain active sources and closed viewers release bitmaps

I121/V12/V10/AI-03, medium resource lifetime defect. Preliminary Windows Insider 26220 host controls,
SDK 10.0.401. Source `b9cda9796af47585bad323e4a5eebd137eee2c63` plus the six-file overlay in
`independent-results.json`; native desktop/frame and exact-candidate qualification remain open.

Five unchanged-production controls fail. Four hold the second read of an actual owned PNG `FileContentSource`,
after the viewer/quick-view header read and during its real decoder worker's feed. Closing disposes the source
while that synchronous read remains held, on both successful and failing read routes. These failures are the
observed disposal count before release, not fixture timeouts. A loaded F3 viewer retains its decoded bitmap
after close. A normal quick-view picture control passes, with expected 320 × 200 dimensions and source cleanup.

`PagedReader.WithSource` now borrows ownership for the actual feeder callback. Closing retires the reader/cache
at once; its last active call/borrow releases the source. Runtime picture decoding uses that borrow, so its UI
demand can finish cancellation before a held read returns. The feeder checks cancellation after a read, before
another pipe write; worker failures cancel further feed demand and abandoned feed exceptions are observed.
The raw borrowed-source decoder overload drains its actual feeder before completing. Unreturned decoded
bitmaps are released; the F3 viewer rejects a result after close and explicitly clears/disposes its displayed bitmap.
Shell thumbnails, decoder sandbox/pixel/input/memory limits and cache policies retain their existing behavior.

All six controls and **20 affected App cases** pass, including five existing picture-viewer, seven quick-view
lifetime and two Shell-picture routes. Full App passes **269/290 with 21 declared skips**; full Core passes
**745/791 with 46 declared skips**, including all nine page-reader lifetime controls. Held-read cases verify
prompt cancellation, no disposal during a call, no additional read, exactly one eventual disposal and unchanged
owned-file SHA-256. Completed cases retain dimensions/visible captions and release their source/bitmap.

Independent checks verify **1,809 captured input files**, fourteen raw source copies per baseline/final stage,
unchanged baseline production against canonical Git blobs, identical new test source across all stages,
active final assemblies/shared Core bytes and direct case/skip inventories. Failed baseline inputs/results
remain immutable. Private root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/i121-picture-feed-lifetime-20261004`.

| Evidence | SHA-256 |
|---|---|
| Five-failure/one-positive baseline XML | `3178186b349ab450b715fa765cf98d41a56bd47bddb5752edcce19b492194ea3` |
| Full Core / App XML | `4637e792c022240880f2cc8ca53e4d403c56c2798a0a711c9debd47497569bc8` / `3ce579e504618b53baa276b05714021ffc84f6d62d3444ef85a29ccda4584404` |
| Core / App input manifest | `a90683a59037f8bf396e8b71326994e8c12c5ae423cf33910d22887f5816af67` / `b1ed890bdfc2f0cb062bed03a98ecfd0e3e1ccd48e91c01a25d59fbbaead9651` |
| Independent input/case/skip inventory | `87f59b693a2cc9bd6e5b86a711606714e32ad1abce3ee45f60b58165496a8e72` |

Clean source `a550fcdb6b6cd83504b585eedcd64e89527d6bf3` passes all four required lanes in
[CI run 37185172518](https://github.com/benny-cz/FileCat/actions/runs/37185172518), with three skipped
package jobs. Six direct XML inventories verify: Windows Core 744 passes/47 skips, App 275/15,
platform 166/33 and Remote 88/28; Linux/macOS App each 254/36. All 38 selected Windows Core cases and
18 picture/quick-view App cases on each platform pass, zero affected skips. Three raw artifact ZIPs match
their server digests. ARM64 lane/log success is retained without a direct XML artifact.

The exact self-contained source passes **34 Windows guest cases, zero skips**: sixteen page-reader/budget
and eighteen picture/quick-view cases, ending **2026-10-04 09:04:25 UTC**. All 686 payloads/687 ZIP
members/fourteen raw/canonical source copies, runner pins and retrieved output inventories verify.
Controller PID 12360 and workers 9500/12532 are absent; no process with an executable below the owned
root remains, including decoder children. The owned temp folder is empty at 09:06:23 UTC. Guest UUID
`9D224D56-1161-A849-ABA7-2581A980895C`, Windows Insider 26300, elevated; no native desktop or physical
USB/source-device access requested. Guest root:
`C:\Users\Public\FileCat-picturefeed-validation-b6cc3d2cc08f4206b2221a80996853ff`.
Retained host evidence: `clean-a550fcd` below the private I121 root; CI is sibling `ci-37185172518`.

The original guest harness wrongly expects seventeen Core cases; all actual sixteen pass, but its inventory
guard fails before App starts. Its outputs/cleanup remain retained. The corrected runner expects the exact
nine lifetime/seven budget cases and runs in a fresh root with the identical ZIP/payload/test bytes. A helper's
first attempt assumes a singleton worker record is an array; its additive successor handles that JSON shape
before preparing the corrected launch. No product failure or unavailable test is converted into a pass.

| Clean-source evidence | SHA-256 |
|---|---|
| CI metadata / full log | `751fd32ee1dab4ce6451a70d81bb97cf2efc27b3af47a5d539cc8fb25badea4b` / `42f6bd17a6229256d48be652a60ae97d5c1e275ce4337bb2d9c37749d9fe688c` |
| Windows / Linux / macOS ZIP | `415b2b847cb55688e39e4f479cfbe821c419dda4051e1626944aa53e595f57bb` / `6a0e9c7d84de77a7858057c1a8e9bbf5fad7098c23c1e65e44f0cdf5a509544e` / `d9d0a0c0d22baa1c3a4bb1523dc56f5e412b7afdee0e3e9d7a23951034331e7e` |
| Independent CI inventory | `8607d3ea8c18631bd48fe3aac445573f80ce5b476410bbf6985f1a96bad3d043` |
| Guest ZIP / manifest | `24430e696c7fd6ff318f8cab12a0cef20ab242376f9afe52d7c4dd7f98dbc437` / `af384be1c1f6324c30f529805bc488cbd2bc59756735c913b396113a4d2232ec` |
| Corrected runner / cleanup script | `09434f5f6cddfa4170a51a0e36ccab490c46dfea566da96342779cc496e08502` / `8dc5e08b64cd3a57f89a00fd2bc75e9ec990903d9270861e1812db5f7f432b76` |
| Original harness failure inventory | `3768113ea1b98fec9d51f51c7f714440a5d642e81fa8ed9bab926b55e4c98983` |
| Guest Core / App XML | `cc1ede7e4e8d6fb628e92d873ad45bf986914819529128912ada9da74e3f5126` / `d30732542f65de717d8acd4e7f360d2e85d789288240619c9daac445596fc52c` |
| Cleanup / independent guest inventory | `5349aaac2295d2f2ce0ab6f67eb987881b5bd9a0bb9cd77a6b3fc0c2051547c1` / `c17f6cc20a34030c0ac6962de6bee78c7b84682be93e51011e7d69da2873f32b` |

Per-device picture-feed bounds, many simultaneous decoders,
other direct `Source` consumers, native delivery/frame/AT and final candidate checks remain. These controls
do not qualify those scopes. No physical USB test was run; its source-change hold and overall **NO-GO** remain.
