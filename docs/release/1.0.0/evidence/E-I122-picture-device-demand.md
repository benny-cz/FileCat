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

Clean-source `82f7488e4e9ff81a59a14b83171016eeec7c6e2a` passes **36/36 elevated Windows 26300 guest cases**,
zero skips: sixteen page-reader/budget and twenty picture-device/lifetime/viewer/quick-view cases. The self-contained
win-x64 payload verifies 687 files, 688 ZIP members and fifteen source copies against canonical Git blobs.
Execution ends at 09:42:21 UTC; independent cleanup at 09:44:47 confirms controller 7988, workers 12464/7580 and
all executable paths below the owned root absent, with no owned temporary files or decoder children.
Guest UUID: `9D224D56-1161-A849-ABA7-2581A980895C`; root:
`C:\Users\Public\FileCat-picturedevice-validation-5ad1437d40e3427eb2ccc829f485697d`.
Private clean root is `clean-82f7488` below the working evidence root above. This is native OS process execution
of headless controls, not desktop interaction or final candidate qualification.

[CI 37192262649](https://github.com/benny-cz/FileCat/actions/runs/37192262649) on that exact source is **failed**:
ARM64, Linux and macOS succeed; Windows has one existing directory-synchronization failure, with the target still
"old" and its replacement job AwaitingDecision. Its original decision request was not retained, so the failure's
exact mechanism is not claimed. All **58 affected Windows Core/App cases and twenty App cases on each Unix lane pass**,
zero affected skips. Windows full inventories: Core 744/47 skips, App 276/one failure/15 skips, Platform 166/33 skips,
Remote 88/28 skips. Linux/macOS App each 256/36 skips. Three artifact ZIPs verify against server SHA-256/size and
every extracted XML inventory; the complete live NTFS-history case passes. The failed run is retained, not rerun
or described as green. Synchronization investigation is separate from this device-feed remedy.

| Clean evidence | SHA-256 |
|---|---|
| Guest ZIP / manifest | `c76ac805802f56702b7d9b2f1782dcce5a6706b188195e973fc535a8fa65fa23` / `eb9f9cf1625a547d22514cad5018c1bf334ecac9ee61a71fec5277affd6c1030` |
| Guest runner / cleanup observer | `701608cd1836450d5b5bf3e1ce6c40855b7b1ffd5d5d0c8b81776f1721effaae` / `b167e25593c0bbd18e6a2c51864dafa6fc0e2f79e4d067ef043712a7f720c7b9` |
| Guest Core / App XML | `7ff19c6a8de612ea20abfa3655fe6592670960e1f9b03f77bcf76452b1c78170` / `1f53993763af5f9079bdf6b63c2a2649090a3d2e9696b4a5aa479a912712791c` |
| Guest cleanup / independent inventory | `098593c465dc9ea80f3c7ff713360fa9b26d7a93147fcbf70bdbac720b17d128` / `c0b6e760a980c86ebaacb2985dbead23c75c7997f9b8521835942bf776874e45` |
| Original CI metadata / full log | `535d78aa05b2245bf2e74ed517b9f05ff516cf746e4650537fe9a1048d9732a5` / `cf48a722d6d801976fbb1ee7fbb372876dd02bf1281342af3b9b055901145d61` |
| CI Windows / Linux / macOS ZIP | `29b7ffb14b0a8d7e27b5abc9d5cae9c97cb8ff1fe7fadcdb1bcc18d0ecddadab` / `1f9ad9a93a1d1f8ee072304bc75ab32bf808dd0d5fc5bd59b8c4aee61324b49b` / `130f0b35cbed88bd8bc73121cb8a448da33f88a3266f44f5a13cabfd22c1c86a` |
| Independent original CI inventory | `eab637c372e59f0119d7eec097b7f6295fc4c73593d418b6a2fc98c1d5a0cf04` |

Clean successor `08acc2f327a99f63e904e9195085d180f55e4447`, with only the synchronization fixture correction,
passes all four lanes in [CI 37194533201](https://github.com/benny-cz/FileCat/actions/runs/37194533201).
All 58 I122 affected Windows and twenty App cases per Unix lane pass, as part of the larger 75/37-case inventory.
Exact source/server digests/extracted XML verify in [E-I123](E-I123-synchronize-windows-fixture.md), independent
inventory `6fafb12ee41f9c4c1e183f07d694c5fe85c050a2cad37a9252bfcb5ac192e7b7`. The original failed CI remains retained.

Watchdog replacement/hard-cap cases, many simultaneous decoder processes/aggregate memory, other direct `Source`
consumers, concurrent copy/search, native queue/frame/AT and final candidate checks remain. No physical USB action
was run; its source-change hold and overall **NO-GO** remain.
