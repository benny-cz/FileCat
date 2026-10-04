# E-I116 — initial archive searches retain provider warnings

I116/V13. Medium, partial search scope. Preliminary working-source evidence: base
`57de8af705a0f887def3b2d141f45bf8239a5cb7` plus the three-file overlay in `independent-results.json`.
Windows 26220 host, SDK 10.0.401, component/headless controls. No candidate/native input/frame evidence.

Two valid controls fail against unchanged production. An owned USTAR has a valid first member and a non-octal
size in the next header. The independent system TAR reader accepts the first member and rejects the next.
FileCat retains the usable prefix and its archive provider reports damage, but the Find adapter discards that
warning; the result and search log show no issue. An owned ZIP with duplicate names and a second member folder
likewise loses its provider warning. A first fixture mistakenly used a ZIP-only detector instead of the app's
composite detector; that four-failure attempt is retained and is not four product failures.

The adapter now forwards non-fatal provider warnings. Initial archive search reports an archive-wide warning once,
even when it is repeated for several member folders, within the existing 5,000-entry log bound. Usable members
remain available with their identities; the log keeps the original archive's folder/name for navigation. Member
contents, parser limits and nested-archive policy are unchanged. The compatible default interface overload keeps
older listers working; actual registered FileCat providers use the warning-aware route.

The two warning controls now pass. Two independent positive controls also pass for **TAR and gzip-TAR** result
narrowing through the actual archive provider: original duplicate ordinal, size/modification criteria, relative
folder and exact `x` content are preserved with archive discovery off. These extend I115's ZIP-only working cases.
All **32 affected archive/search-criteria controls pass**, followed by full Core **729/775 with 46 declared skips**
and App **258/279 with 21 declared skips**, including both new Find flows. An intermediate observer compared the
typed temporary path with a canonical disk-case path; its two failures are retained separately and the observer
now compares actual disk spelling.

Independent checks verify 1,387 baseline/final directory input files, eight final source copies, the active test
assemblies, every direct XML result/skip and 1,404 retained files. Directory inventories include unused nested
build outputs; qualification is limited to the active assemblies named in the manifest and actual run logs.
These are preliminary component/headless checks, not candidate/native presentation evidence.

Private root: `artifacts/release-evidence/i116-archive-discovery-warnings-20261004`.

| Evidence | SHA-256 |
|---|---|
| Valid baseline XML: two failures/two positive controls | `9d592c69d4a9d6bb5afe2ff529c0fa64124e5d807d9235f1dbe7473e8f770b9b` |
| Corrected 32-case XML | `41524a7438dca48416377cf4884ed7bdf2a089d9c276fc5402cf31681a6e102e` |
| Full Core / App XML | `41bd4a535283691d8da2c9d337104e7bf7547e32f28abefebb047e1a7ece0c40` / `82edbf9415d3fc8baacda3584ce92e09ae077ebed7dc6e95714c5c25e475cdb9` |
| Final source/payload manifest | `9de3a86a71978cf8c3cc8a1abbf09ceaa9e53fff1eef71f0b3a0c5ae51051740` |
| Independent input/XML/skip inventory | `017eb9765b51b17bf7d3acd33c862d81352f065768c5e5bf7319f4f182f74a6f` |

Clean successor `6ecf4a83a189f3035e2ebb306e8742cc6eb32228` passes all four required
[CI lanes](https://github.com/benny-cz/FileCat/actions/runs/37175881310); three package jobs skip. Six direct XML
inventories verify Windows Core 728 pass/47 skips, App 264/15, platform 165/33 and Remote 88/28; Linux/macOS App
each 243/36. All 34 affected Windows cases and both Find flows on Linux/macOS pass. Three retained artifact ZIPs
match their server SHA-256 digests.

The exact clean source passes **32 Core and two App controls, no skips**, in the elevated Windows 26300 VM,
UUID `9D224D56-1161-A849-ABA7-2581A980895C`, ending 2026-10-04 04:16:04 UTC. All 680 payload files,
681 ZIP members, eight committed source-content copies and retrieved XML verify. Controller 14256 and workers
12476/1956 are absent; owned temp is empty at 04:19:22 UTC. This is native OS execution of headless controls,
not native desktop input/frame evidence.

The first clean publish hits a full E: volume. Its 668 files (289,179,564 bytes) are copied and independently
hash-verified in the authorized second workspace before the redundant failed directory is removed from E:.
The successful publish and CI records use that workspace's `FileCatReleaseEvidence` directory. The failed
attempt is not qualified; its exact transferred bytes and manifest remain retained. No physical source or
historical release evidence is discarded. This writable local store does not resolve DEC-10's sealed-store gate.

Private successor roots under
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence`:
`i116-archive-discovery-warnings-20261004/clean-6ecf4a8-v2`, its sibling `failed-clean-6ecf4a8`, and `ci-37175881310`.

| Successor evidence | SHA-256 |
|---|---|
| CI metadata / complete log | `36cf8618e22c66238c7fda79a207a59e29e714e96ea40bce32ac7e3f39cd4ddf` / `8a78f6ca732adb13ab2aa185861334d698a46356822fe5f9c56308bdfbc87e19` |
| Independent CI inventory | `cfe8f9b52bf1e5f5fb6a36f1a4256ec35e51f9ea8ac79a75034def896912b53a` |
| Guest ZIP / input manifest | `681047d476e52935e5af459ab8c7c3b6617eb911e02dac2040485b0e13205115` / `399a22c549926a4a7f51987aede7e428a34ab3a51078cc031ba29db12e681a8f` |
| Guest runner / cleanup observer | `dbb89c651f7e5061efe0a0b77848711bfa02c0422b8494acd66105988595f1f0` / `179847fa256f207aeca7670dbfa6a2cccac6613638b17be37942deaadd40683d` |
| Guest Core / App XML | `81a4da2f7eee86c56d72c94af5570d0f308aaeb1e22add125e7174ea6904f836` / `5e088c80f9fbfe911c9f91ace5db3db3528b68ea763d574e5eaa9d73587e8287` |
| Guest cleanup result | `b92c72035d219ccff19b466edaa9817aa6ebbbbf160bb8bacdc7a8b948924272` |
| Independent native/failed-transfer inventory | `2e9e11c8c9d316cc2fe38fe239804803634dfb865c2c9b37db6256ab0b9a23d5` |

Remediation verified preliminarily on clean source. Other formats, native presentation/AT and exact candidate
checks remain required. USB G6 quarantine and overall **NO-GO** remain.
