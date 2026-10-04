# E-I117 — discovery fixture controls the late-name cutoff

I117, low validation reliability; required CI evidence. ARM64
[run 37176852050](https://github.com/benny-cz/FileCat/actions/runs/37176852050) at exact
`aed64a7bd1d9f88672137f6a9dc2ff9c20e2dedb` fails the late-name test after one second with an empty host list.
That Core lane has one failure, 727 passes and 47 skips; the other three required lanes pass. Original metadata,
failed/full logs and three server-digest-checked result ZIPs are retained. There is no ARM64 direct XML artifact
or scheduling trace; the exact failed scheduling sequence is not established.

The fixture starts a one-second discovery timer before confirming any probe or metadata request was handled.
An empty list does not exercise I23's late-name lookup. The replacement uses the same discovery implementation
with an internal controlled probe token. Its dedicated loopback device thread receives the metadata request,
closes the probe window, then replies with the name. Controls cover immediate setup and a 1,200 ms delay before
the probe reply. Both assert the actual name/workgroup and that the reply followed the cutoff. Owned device
threads/sockets are joined/closed on success and failure. Public discovery still uses its existing timed cutoff;
metadata timeouts, destinations, redirect policy and naming cancellation remain unchanged.

Both controls pass in the eight-case network class and full Core **730/776 with 46 declared skips**. The
seven related App controls (Network places and saved searches/duplicates) also pass. A deliberately coupled
name/probe cancellation mutation fails both controls with an aborted metadata connection. It is a negative
regression control, not a new product-baseline failure. The original fixture also passes subsequent clean
`ab919ed` CI, confirming intermittent behavior rather than establishing a platform regression.

Working source: `ab919ed78eae0dfa8287249fd4ec07d38cbdc37c` plus the two-file overlay recorded in
`independent-results.json`. Independent checks verify 218 mutation/final input files, exact source copies,
active assemblies, direct case/skip inventories and the retained original failure. This is preliminary
component/headless evidence. Clean committed-source CI and a combined Windows guest run are next.

Private roots under
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence`:
`i117-discovery-fixture-cutoff-20261004` and `ci-37176852050`.

| Evidence | SHA-256 |
|---|---|
| Original CI metadata / failed log | `6cc10fcb33d0182162d56d8a57191243ed4d8d9b97c44300eca3412871462adb` / `56ff3b0aa0d3fc104d047a4089b85e39bd1937c12b8e368793905ceded7c59c3` |
| Full Core / related App XML | `f2161dea11a4e70dc4584e946861ac387a8ff6e8c5273283366e7f61ed6eaab5` / `8364405bb0fb71051f10c9c82047548530d37f885e22df910d352e21a3460032` |
| Two-failure mutation XML | `4417614c0f1b757d70c4a929462c426da94cb9db30c067bf4989075b62ae9ee1` |
| Final Core / App input manifest | `1f8a4db06ec0e4df2621fec6b50ecf02258d0b00f32a356ca927df13575e786d` / `49ca572c755751acd4645fa4c806f54ae0a29189850684c39e6185086808d18b` |
| Independent input/XML/failure inventory | `4111826facd03864dd1f07516381af7e1e20fb4179bd5b65219df0a3ef3eda8a` |

Clean successor `da3a3d602624b4cf6bb0f4c3314c1ccc60d6af78` passes all four required
[CI lanes](https://github.com/benny-cz/FileCat/actions/runs/37178237458); three package jobs skip. Six direct XML
inventories and three server-digest-matching artifact ZIPs verify. Windows Core 729/47 skips, App 269/15,
platform 165/33 and Remote 88/28; Linux/macOS App each 248/36. All eight network and five saved-search/duplicate
Windows cases pass; all five saved-search/duplicate cases pass on Linux/macOS.

The exact clean self-contained source passes **eight Core and seven App cases, zero skips**, in Windows VM
26300, UUID `9D224D56-1161-A849-ABA7-2581A980895C`, ending 2026-10-04 04:57:00 UTC. All 685 payloads,
686 ZIP members, 13 committed source-content copies, retrieved scripts/XML and output pins independently verify.
Controller 10744 and workers 12504/12224 are absent; owned temp is empty at 04:58:52 UTC.
This is native OS execution of headless/component controls, not desktop input/frame qualification.

Private successor roots in the same store: `ci-37178237458` and
`i117-discovery-fixture-cutoff-20261004/clean-da3a3d6`.

| Clean successor evidence | SHA-256 |
|---|---|
| CI metadata / complete log | `df98e75e0919a6ffb7151306dce0da1b5d00d617b7073fcc8585dd0de20663e9` / `9d849073560f16ecff6e3203178b85f9e7a913bd649fb00f35463d9e90d032ff` |
| Independent CI inventory | `6881692af659638cd5a7fe795aa7646f08c3813e7cdd32c8cad976b88e95aba0` |
| Guest ZIP / manifest | `b831926ecbf60c15f83dde612ae57f8a514cc0d6f057a9bca32ba9ab118571c2` / `e6f59a9816b5614e4fc570ab2d5ec4ede5f9f3f7d8b6d35e632a7179152c2adc` |
| Guest runner / cleanup observer | `4fc2cca2678348b00c0e617675fd834a88f6420dcff8dfa1f7290bdf808b3baf` / `25d30285436d9a427dd5cc43c669f0555c5cf33473fbfded2d280874ebcc78b8` |
| Guest Core / App XML | `a59bcf298f9f7ff52a6b79635f5327e0e00f46d627ae31279330b3e5fe0ab399` / `aa4e1c5e822f252c9b4632a08c02b927ac140c1170d33a88f1286e9575feffc7` |
| Guest cleanup / independent native inventory | `d8c410fabb2b8df83e4ec3176bd65a3515fa3483f907c61b7a7802a12dc3fdbb` / `9bd7d065bf198f873d8d24bd0599cc972a2347aee2e3907c924c9ffd3aabdbce` |

Fixture remediation verified preliminarily. Native real-device/candidate discovery and exact candidate regression remain required. Overall **NO-GO** and
the USB source hold remain.
