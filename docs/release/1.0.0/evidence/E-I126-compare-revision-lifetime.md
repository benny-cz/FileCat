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

Clean source `e406c9649a8f7b0bfc799e12604f41cfeaed20d1` passes all four required lanes in
[CI 37200743448](https://github.com/benny-cz/FileCat/actions/runs/37200743448); three package jobs skip.
Direct Windows XML records Core **748 pass/47 skips**, App **281/15**, Windows platform **166/33** and Remote
**88/28**. The live NTFS history case passes. Linux and macOS App each pass **260/36**. All **91 affected Windows
Core/App cases** and **41 affected App cases per Unix lane** pass without affected skips. The ARM64 lane succeeds
without a retained direct XML artifact. Independent verification checks three server artifact digests/ZIP sizes,
extracted XML bytes, six complete case/skip inventories and exact selected affected names.

The same clean source is published self-contained for the SDK-free Windows Insider 26300 guest, VM UUID
`9D224D56-1161-A849-ABA7-2581A980895C`. Its owned root is
`C:/Users/Public/FileCat-comparerevision-validation-d26a77576b364f6daa5655dc771ab486`.
All **50 Core and 41 headless App controls pass**, zero skips, ending at **12:11:02.9185495 UTC** on 2026-10-04.
Independent verification checks **693 payload files, 694 ZIP members and twenty-one canonical sources** against
pre-launch pins and exact direct XML names. At **12:14:17.4222956 UTC**, controller PID 10712 and worker PIDs
5748/8888 are absent, no executable children remain below the owned root, and its temporary folder is empty.
The four comparison lifetime regressions, preceding scheduler/watchdog/cap controls and selected consumers pass.
These are owned component/headless controls, without native desktop input or physical source-device access.

Private clean payload/guest root: `clean-e406c96` below the working evidence root above. Private CI root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/ci-37200743448`.

| Clean evidence | SHA-256 |
|---|---|
| CI metadata / complete log | `48a0552faa3075da5396b3ed3d42a58160aa0c016d226bbc5fc80391e7167689` / `6e6d0191939391a253d1a25f074bba7c5b812fc03451dc84eee2a972f87be073` |
| Windows artifact 11302789075, 379835 bytes | `45548c320486297135910783e9047b8fcfdf307b0a32561d62bc391850fef5bd` |
| Linux App artifact 11302784088, 82311 bytes | `e33b29b76ab2d9fa635865709838d3ab32c130629965525295423b6fd556d147` |
| macOS App artifact 11302966266, 83272 bytes | `f4ade932bc1e51339e136d0f9e0489a350ff4fc61111f3b9ea120352fdeccf90` |
| Independent CI inventory | `f39cab004c51b283a83b5289978fbf69724da5907508451871b47a76d8d4a3ba` |
| Guest ZIP / payload manifest | `847ab681b3f999c96e28a417dc67627e33e265e7bc39b6f529798610328fe6c8` / `e6052beb3382637e4ceb04f19861d7706816c4f6288c21b05d6c4e1a8645b8d7` |
| Guest runner / cleanup script | `71a68c962ffb684d51a074fa8e74811cc55c73a971686b078dec2a0b93066779` / `63bc9630212646a76d8cf420618c45622a63a0d8074954939f7c3bb6650eb57c` |
| Guest Core / App XML | `4088aac7a63f2fa0572cb3afca176def4ec616981234d16151e7cf3207a117dc` / `3aefb7e1ed2128641484fca55a60cfcfcdcca35e84d6921764802814eddc4fdb` |
| Guest exit / cleanup | `f2dfda628ccde2b4f1c11b9a5c324b2ce3f19a6d46fea947276fff1ea1b350f5` / `64c35194ee6b2fc0640681143b592ea5520584be250b6c97275506659bd3268c` |
| Independent guest inventory | `e18e2638b90338eb5bfb4aeec9d822db207e2f078cc2551bd7a426baff43e482` |

Native desktop/AT/candidate and wider content lifetimes remain unqualified. Updated Windows Computer Use skill
26.930.41038 initialization still returns `trusted Node process exited unexpectedly; kernel reset, rerun your request`
before any app input. A separate plain `nodeRepl.write("Node runtime ready");` call fails identically without loading
Computer Use. Both exact invocation/error records are retained; neither observes current desktop state.
The plain-runtime record SHA-256 is `3dbeb6643a6185fde455a93fba9798c537922dea55d28585636c8701810954bc`.
After resuming at 12:25:57 UTC, the same plain call reports `node_repl kernel exited unexpectedly` with diagnostic
`windows sandbox failed: helper_unknown_error: setup refresh had errors`. One kernel reset and retry at
12:26:12 UTC fail identically (kernel PIDs 10744 and 36368, exit code 1). Complete tool results are retained in
`windows-node-runtime-resumption-1226.json`, SHA-256
`069307e146da11561176029a5721f2711217fb8f8a58a3efc7aad455d2ed3187`.
No Computer Use import, input or desktop observation occurred. A full Codex restart is requested as the next
setup recovery attempt; restoration is not yet established.
Live Windows interaction needs the runtime restored. No USB action occurred; its historical source-change gate
stays held. Both VMs remain running. Overall **NO-GO** remains.
