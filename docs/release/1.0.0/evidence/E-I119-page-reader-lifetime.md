# E-I119 — page demand stops at close and active source calls retain ownership

I119/V12/AI-03, medium resource lifetime defect. Preliminary Windows Insider 26220 host controls,
SDK 10.0.401. Source `2e27120d3754845be63affa8fe7b6e9449fd70ee` plus the two-file overlay in
`independent-results.json`. No native desktop/frame, physical-source or candidate qualification.

Eight unchanged-production controls fail. A wrapper around an actual owned `FileContentSource` holds a
synchronous read or revision call. Closing its `PagedReader` disposes the file handle before that call returns.
Successful/failing reads and both background/blocking routes reproduce the disposal race. A multi-page read
loses even its first page after close; a closed reader starts another background page request or accesses the
closed source during Refresh. An active refresh likewise loses its source before returning. The ninth,
positive control reads independently specified bytes, reuses the cached pages and releases their budget normally.
Fixture cleanup joins actual calls; failures are assertions or closed-file exceptions, not fixture timeouts.

The reader now accounts for its active read and revision/length calls under the cache lock. Close retires new
requests, clears the cache/error state and releases the shared budget immediately; the last active call disposes
the source once at its safe boundary. Blocking multi-page reads stop before another page, queued background
requests perform no provider I/O once closed, and late refresh/error results cannot update the retired reader.
Source disposal occurs outside the cache lock. Cache capacities, page size, eviction and device scheduling
retain their existing policies. Direct calls through `Source`, including picture feeds, require separate audit.

All nine controls and **38 affected cases** pass, including existing shared-budget, content and hex-overlay
checks. Full Core passes **745/791 with 46 declared skips**; full App passes **263/284 with 21 declared skips**,
including all seven quick-view lifetime and five picture-viewer cases. Independent checks verify 267 retained
baseline/final input files, eleven raw source copies per stage, unchanged baseline source against its Git blobs,
identical test source between baseline/final, active final assemblies/shared Core bytes and direct case/skip
inventories. The old-source failures, inputs and all final outputs remain immutable.

Private root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/i119-page-reader-lifetime-20261004`.

| Evidence | SHA-256 |
|---|---|
| Eight-failure/one-positive baseline XML | `f1d478fd98ddf9af8eca84a3c97a5fdc6b758fb72756c20c6970e559fd30e323` |
| Final Core / App XML | `7c14f40f607dab465a00ad2f9ee878ce24390811fe8fbffca10d705e2cfec2c7` / `579b9e62490e4bacf2000639651cc64fcb04492a3ea60a179627c1f83fbac956` |
| Final Core / App input manifest | `cce8e49d7b63b392936efb5f1edf0bbfdacac72de45b42757c10b4ec3bb1f372` / `411ee52a20504ea7e168556f77e0de2f2c79a90af6ad694d45750dd39fb7e193` |
| Independent input/case/skip inventory | `5d9eadf8926331b6309e6f3a1646642864f5fe75f8e63a7cadcbaca49013da98` |

Clean committed-source CI/guest controls are next. Many simultaneous active source calls, broader viewport
queue/device bounds, direct picture feeds, concurrent real copy/search, many-folder/partial-size controls,
native frame/AT and exact-candidate checks remain. Native automation initialization was rechecked on
2026-10-04 and again exited before input with “trusted Node process exited unexpectedly; kernel reset”.
Overall **NO-GO** and the physical USB source hold remain.
