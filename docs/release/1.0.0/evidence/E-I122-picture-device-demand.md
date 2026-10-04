# E-I122 — picture feeds share their provider's device workers

I122/V12/AI-03, medium bounded-resource defect. Preliminary Windows Insider 26220 host controls,
SDK 10.0.401. Source `6b2d731b711e81cd17367b51274d9dd179ae15be` plus three App files and the new test
file in `independent-results.json`. Core scheduler, quarantine threshold and worker caps are unchanged.

Two unchanged-production controls fail. Actual owned PNG files hold their second reads during real decoder
feeds. Three F3 viewers on one provider device, or three rapid quick-view selections, produce three concurrent
held reads despite that device's two-worker setting. Both controls also complete a separate device's known
44 × 30 picture. Failures are the observed three-versus-two call count, not fixture timeouts; no claim is made
that the healthy device failed. The original source ownership remedy from I121 remains effective.

Runtime picture feeds now run as interactive work through `DeviceIoScheduler`, using the provider's device
key. Quick view passes its selected item's key; F3's provider launcher passes the same typed parent key.
Direct local viewers derive their filesystem key, and content without an identified provider uses a shared
fallback key. The actual scheduled callback borrows the reader's source; cancellation may complete its task
early while the callback retains ownership until a held read returns. Canceled queued feeds never borrow/read.
Raw borrowed-source decoding, worker sandbox/input/pixel/memory limits and existing scheduler policies retain
their behavior. This does not impose an aggregate cap on decoder processes or their combined memory.

Both device controls and **22 affected App cases** pass, including all six I121 lifetime cases and existing
picture/quick-view/Shell routes. Each device control observes two held calls, a healthy 44 × 30 picture,
no third feed and no canceled queued read after release. Quick view's abandoned third open performs zero
reads; the third F3 reads only its initial header. Source disposal occurs once after active calls, owned-file
hashes stay unchanged. Full App passes **271/292 with 21 declared skips**; full Core passes **745/791 with
46 declared skips**. Independent checks verify **1,812 captured input files**, fifteen raw source copies per
stage, baseline production against canonical Git blobs, identical test source, active assemblies/shared Core
bytes and direct inventories. A separate additive scope inventory verifies the original/corrected/full-suite
call-count traces and healthy pictures. The broader-bounds flag in the original inventory remains false.

Private root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/i122-picture-device-demand-20261004`.
The first corrected build rejects an ambiguous `Location` type; the additive build uses the explicit Core
namespace. All failed build/baseline inputs and results remain retained.

| Evidence | SHA-256 |
|---|---|
| Two-failure baseline XML | `75a89606f254dd5c1e3384d679272432a1e6a6342eac606ce7a9e8f1920c34d3` |
| Full Core / App XML | `01053acb58e98db1210d7695792c4f9125db3af0db778a5edd3e76e4574dd422` / `c383c4e4a8f326ca09bff2865ab8b17395e56c6625ec5dab3e8800eea7a3aa50` |
| Core / App input manifest | `0057b02611a293b85d1cde79e60b476b4896c43f39f7fc68cae73219da064691` / `7b39b8cec1c529a17045755149ae8fd061fc9472a77b8a832e25dd276b5a9a02` |
| Independent input/case/skip inventory | `a330310cb2537ccd43ed217c8786243ee1f0772cedf8a26d4991d67b60e93e24` |
| Controlled device-demand scope inventory | `8ac2a450a73cdffd9232546324ef6b36324b98370d40d82e725173ed9317a089` |

Clean-source CI/Windows guest execution is next. Watchdog replacement/hard-cap cases, many simultaneous
decoder processes/aggregate memory, other direct `Source` consumers, concurrent copy/search, native queue/frame/AT
and final candidate checks remain. No physical USB action was run; its source-change hold and overall **NO-GO** remain.
