# E-I126 — comparison revision calls retain their file until completion

I126/V12/V13/I22, medium resource-lifetime defect. Preliminary Windows host, SDK 10.0.401.
Original production payload: clean `749f55f52154d1f76449658a83861e1ec159041d` retained with E-I125.
Working source: `33ea62b138d07c741a91d28465facafc107f079e` plus `CompareWindow.cs` and
`CompareRevisionLifetimeTests.cs`.

An isolated headless probe references the actual original production DLLs and wraps owned real files to hold a
revision call at a controlled boundary. Four cases cover the activation revision check or the byte page reader's
Refresh, each followed by closing the comparison or F5 reopening its files. Initial comparison completes and
correctly reports identical content before the held call starts. Every original case disposes the file **while
its revision call is active**; resuming fails on the disposed handle. All four fail, exit 1, with explicit active/
disposal/error observations. Both file hashes remain unchanged.

`ViewSource` counted only reads; GetRevision and Length bypassed its lifetime gate. Activation also used raw
sources. It now captures the corresponding views and rejects a result if those views have been replaced. Views
count Read, GetRevision and Length calls under their existing lock, refuse new calls after close and complete
the idle task after all active calls finish. Initial comparison/align/search runs retain their tracked lifetime.
No Core production source changes.

Identical probe source with corrected production assemblies passes all four cases. A second controlled swap
changes **only `FileCat.dll`**, keeping the other six baseline production assemblies byte-identical; all four
pass there too. While held, the file remains open with one active revision call. After explicit release, revision
succeeds, the actual file source disposes once, active calls return to zero and hashes stay unchanged. Independent
verification checks all twenty-four retained real-file fixtures against expected bytes/hash.

Four regressions cover the same routes through the actual comparison window and FileContentSource, with bounded
waits and cleanup of only their verified owned temporary folder. All **41 affected headless App and 50 Core
controls pass**, zero skips. Full App passes **275/296 with 21 declared skips**. Existing comparisons, synchronization/
replacement, operation routes, decoder/quick-view lifetimes and scheduler/cache controls pass.

Independent verification checks observations and loaded-copy DLLs, identical probe source, the App-only swap,
**1,434 captured inputs and twenty-one raw sources per stage**, canonical unchanged Git source, active final test
assemblies, shared final Core DLL, complete XML inventories and every skip reason. The first Core run used the
inherited I125 assembly stamped 924d6d8; its 50 passes remain retained. Refreshing the build to stamp 33ea62b and
match the App's Core DLL passes the same 50 cases; Core source is unchanged. The initial standalone probe restore
failure (NU1015) ran no test. A PID-only cleanup observer flagged PID 58896 reused by `conhost.exe`; identity-aware
successor snapshots verify owned workers/executable children are absent. Intermediate records remain retained.

Private root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/i126-compare-revision-lifetime-20261004`.

| Evidence | SHA-256 |
|---|---|
| Baseline / corrected production App DLL | `c26b5d84cace7873f4678531c570b89a9fb7af59e70ca2de10fb2f2d70eb4017` / `a39a2d16f4b00499da6a6f3da543ee1249f37ad7f89f36191451fe8e885bbcc5` |
| Baseline / corrected probe XML | `762457a6f2e4de0437c054953ffa43226c3e3f66c6ea76fc5bedc8cc8aeaaa03` / `89fbfc7a2bcbb28f90ec6b2fbe3b73c4765bd870a8ae9529f01eabe39d16aad6` |
| App-only assembly swap XML | `fcc18ebeb277413c66e5ad40d0752500d159befd3a47dd05401d3e1f51b42348` |
| Full App XML | `f336c4db9d730bf792b04cfa8ec414e226bedb3be899bfae7f9db1c6c077b5cb` |
| Final Core / App input manifests | `0ae43e26676ed21450c733a04a0d1945c064f19421f681b6e2bf2a5ff049e9ef` / `d1b951c6bc8589ad0488ac636df514e499523ee2245676b4099292fb411a8686` |
| Final owned cleanup / independent inventory | `6cf960a9e8b42a8e74d59426b8371f81dddc945856ec86e2055b099bff0a1f5f` / `16007480650dd7e6193cf9c827400801713c1e45ac0e9483c663fe9ef5e0eb41` |

Clean-source CI and SDK-free Windows guest validation are next. Native desktop/AT/candidate and wider content
lifetimes remain unqualified. Updated Windows Computer Use skill 26.930.41038 initialization still returns
`trusted Node process exited unexpectedly; kernel reset, rerun your request` before any app input; the exact
initialization/error record is retained. No USB action occurred; its historical source-change gate stays held.
Overall **NO-GO** remains.
