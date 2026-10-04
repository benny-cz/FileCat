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

Clean-source CI/Windows guest execution is next. Per-device picture-feed bounds, many simultaneous decoders,
other direct `Source` consumers, native delivery/frame/AT and final candidate checks remain. These controls
do not qualify those scopes. No physical USB test was run; its source-change hold and overall **NO-GO** remain.
