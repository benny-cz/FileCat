# Branch refresh and source change hints

A finished live result can refresh a selected captured folder in a new tab. The refresh opens each child below the verified live root and checks the captured name, identity, creation evidence, and mount or volume. It refuses changed ancestry, regular-file selections, and synthetic Other groups. The new tab has its own scope and totals. The earlier tab retains its marks, basket, and full-rescan eligibility.

The live owner can flag a root as possibly stale after a direct-child watcher notification or loss of watcher coverage. The watcher budget is shared across sessions. The warning does not change the captured result or start a scan. Changes deeper in a tree can go unobserved. Full provider, overflow, disconnect, and descendant coverage remain open.

| Check | Windows VM | Mac | Ubuntu VM |
| --- | --- | --- | --- |
| Branch refresh broad rendering run, r1336 | 850 passed, 1 platform skip | 851 passed | 851 passed |
| Branch refresh and change hints broad rendering run, r1339 | 852 passed, 1 timeout, 1 platform skip | 854 passed | 854 passed |
| Final selection guards and change hints, r1345 | 10 passed | 10 passed | 10 passed |
| Final native window journey, r1346 | Passed | Passed | Passed |

The broad rendering runs preceded the final correction for file selections and synthetic Other groups. The final focused runs exercised those guards on all three platforms. The initial branch fixture supplied a preview identity when it requested a completed indexed result. Its failure receipts and the corrected fixture are retained.

All nine branch and change-hint cases passed in the broad Windows run. The Files-metric catalog comparison exceeded its five-second UI wait in that run. All four catalog comparison cases passed in the isolated r1349 rerun against the final build. The Files case took 5.79 seconds end to end. The rerun retained the original timeout and did not change the comparison code or the test wait. A separate process appeared after the rerun and was left alone. The [evidence manifest](source-manifest-r1347-branch-refresh.json) indexes the original receipts and exact tested source.

The native journey issued real control events in FileCat, created an external file to trigger the stale warning, and refreshed Photos. Only the two captured Photos files appeared in the branch result. The initial result kept its marks and basket and retained the original filesystem fixture. FileCat exited cleanly on every test machine. This journey used application-rendered images and control events. Physical input, screen-reader qualification, package acceptance, and the independent release review remain open. The public command remains off.
