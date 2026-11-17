# Metadata candidate previews

Both analysis views offer candidate previews for captured locations selected explicitly by the user. The five built-in rules cover largest files, old large files, downloads and installers, .NET restore output, and Chrome HTTP cache records. The preview names the captured revision, rule version, scope, thresholds, and limitations. It counts uncertain and excluded entries and keeps the first 128 ranked candidates in memory. Individual evidence is available through recorded paths and context markers.

The .NET rule requires an exact restore filename under `obj` and a recorded project file beside that directory. The Chrome rule requires the recorded Google Chrome Default profile layout and both a Local State and Preferences record. The current rules use those narrow layouts. They leave other shapes and uncertain cases outside the candidate list.

The preview reads observations or FileCat's private index. It can inspect a saved or imported analysis without connecting to its sources. Running a preview and inspecting its evidence leave the source, marks, and review basket untouched. Staging candidates, saved custom recipes, explicit duplicate work, and growth insights are outside this slice.

| Check | Windows VM | Mac | Ubuntu VM |
| --- | --- | --- | --- |
| Rule and bounded index checks, r1348 | 11 passed | 11 passed | 11 passed |
| Native filesystem with memory and indexed dialogs, r1350 | 2 passed | 2 passed | 2 passed |
| Native FileCat window, recipe and branch journey, r1351 | Passed | Passed | Passed |

The native window journey required an explicit location, showed the matching restore file and its captured project marker, and excluded the lookalike folder. It kept the earlier marks and review basket, made the stale warning visible after a separate fixture write, and verified the fixture files on exit. All three machines were clear of test processes after collection.

The Mac application-rendered frame clipped the modal at 2×. The r1353 Mac compositor check reused the same application binaries and showed the title, candidate, evidence, and Close preview button fully visible. The complete desktop capture remains local. The [receipt index](source-proof-r1353-recipe-compositor.json) records its hash. The [functional evidence manifest](source-manifest-r1352-metadata-recipes.json) retains the exact source delta and three-platform receipts. The journey used native window control events. Physical input, assistive technology, package qualification, and the independent release review remain open.
