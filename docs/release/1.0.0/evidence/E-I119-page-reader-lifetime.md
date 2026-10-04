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

Clean source `de1fd7145be13a4a64785cbe8ba88095a4f954e3` passes 38 Core and seven headless quick-view guest
cases, zero skips, ending **2026-10-04 06:08:51 UTC**. All 683 payloads/684 ZIP members/eleven raw/canonical
source copies and retrieved output pins verify. Controller PID 14188 and workers 1420/3824 are absent;
the owned temp folder is empty at 06:10:10 UTC. Guest UUID `9D224D56-1161-A849-ABA7-2581A980895C`,
Windows Insider 26300, elevated; no physical-source or native desktop interaction requested. Guest root:
`C:\Users\Public\FileCat-pagereader-validation-40fadd2a0ee44d01ae91a4f090949263`.
Retained host evidence: `clean-de1fd71` below the private I119 root.

An additive launch wrapper sets the guest working directory to its owned root; original request/payload pins
remain retained. The first output comparison rejects ten string-parameter names because xUnit native XML
adds a string-escaping layer. The successor verifier round-trips each escaped name from its exact host name,
checks the one-to-one case inventory and retains all raw names/mappings. Payload/tests were not rerun or changed.

[CI run 37181433089](https://github.com/benny-cz/FileCat/actions/runs/37181433089) has **three passing lanes,
one failing Windows lane and three skipped package jobs**. All 45 affected Windows cases and seven App cases
on Linux/macOS pass directly in XML. The only failure is the existing NTFS history fixture (I120): it dereferences
an absent log table while its actual report says the fixture's last change is older than the circular log still
holds. Six inventories verify: Windows Core 744/47 skips, App 269/15, platform 164/33 skips/one failure and
Remote 88/28; Linux/macOS App each 248/36. Three raw artifact ZIPs match server digests. Overall CI success
is still pending a successor; no rerun erased the original failure.

| Clean-source evidence | SHA-256 |
|---|---|
| CI metadata / full log | `3ea8de0b59fffdfed903b7831f3e91dcedc5acc4e127f21aecea915376b3dd65` / `49abf92d03aeded71a01e98603e489d19e3358ce32c851d0ddb78abf73985c32` |
| Windows / Linux / macOS ZIP | `432632984fb9f9cfc0df24a2d7aea4c8d915b660f5ba3f96ed832c665da5d135` / `419b5de8a1319967681a4341638e9f9178a5084d844bcef374d0b4f996c01449` / `c1f4dbc2474860af0fe3d5babffe068e165917621c644c505e3c869bbf0c8917` |
| Independent CI inventory | `ebdbd38c45118598a81210f945cb96f55df9f7bd04c63eef6b9119c377e40270` |
| Guest ZIP / manifest | `2d80bfebe68ac85775fb3590f91d1ef22265a2bd6661e792dd8101f7b9605b3b` / `7c694c61b51a45ea306082211a08933eae179be22c9ca60d2a558662b29082f3` |
| Runner / cleanup / launch wrapper | `574316a1c8dbf86e6960287a0a34747f4f214a33fc1524f1928ca35e22db3a10` / `e96b593796f23f2e9eea8c067796c58f8a32f419f8d65f7e71b3b9bf318d19b9` / `f9fe81966f2030fc81c1baef714b959d310d0691716f18bfa2085b15d6c1fd85` |
| Guest Core / App XML | `7e76d076ed67e2d0bb9a6ccc8e4b31796cb7bdfad14b643bd27c811eae5c15f1` / `e0960bd954189694639154be66ad5c9c1d0a9086e7c069293a494a82cd58178c` |
| Cleanup / independent guest inventory | `e3fbd02fe238ea1d605b82dac7dfae1f6acf3b74fd6c265d8cae57657804c708` / `146c459d5f9153dbd479eeb65fac7e6927eb09fa4395ce7ff08b0549c1424ba9` |

Many simultaneous active source calls, broader viewport queue/device bounds, direct picture feeds,
concurrent real copy/search, many-folder/partial-size controls, native frame/AT and exact-candidate checks
remain. Native automation initialization was rechecked on
2026-10-04 and again exited before input with “trusted Node process exited unexpectedly; kernel reset”.
Overall **NO-GO** and the physical USB source hold remain.
