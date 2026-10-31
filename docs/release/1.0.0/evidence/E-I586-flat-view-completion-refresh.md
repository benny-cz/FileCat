# I586 — Flat View could finish with an empty visible result

## User consequence and boundary

In an isolated native Mac instance at `9055a16a`, Flat View over an owned folder with `a/report.txt` and `b/report.txt` displayed **0 folders, 0 files** and **All files below … — complete**. The two files remained on disk with different known hashes. Pressing FileCat's **Reread** immediately showed both `report.txt` rows and **0 folders, 2 files · 33 bytes**. The recursive search had found the files; the result tab had missed its completion update. A user could otherwise conclude that the folder contained no files. This is a result-publication UX defect, not a new recursive-search algorithm or a file-integrity failure.

The fixture files were 17 and 16 bytes. Their SHA-256 values were `09ce58169b84448a83f35dc2108df2296d4a1ac1b0cf13e2b17a482409794af8` for `a/report.txt` and `b7d6831f211ee2dc3655bf9301a5ec1058bc6cdc6a011b5891d851f36fb8040b` for `b/report.txt`. Both subfolders had ordinary readable Mac permissions and no hidden flags. The result-set lifetime and search-engine tests accepted earlier did not verify this fast-completion visual state.

## Correction and bounded verification

Product `16fc03aa` posts the completion banner and one final result-tab refresh on the UI thread, guarded by the still-open tab and exact result-set session. Thus even a completion event that precedes subscription cannot leave the first enumeration stale. The focused App test checks two same-named nested files in a Flat View result, their distinct relative folders, and the automatically populated listing without a manual reread. Flat View's existing cancellation-source lifetime controls also run. Windows x64 focused App scope passes **3/3**.

A fresh Mac worktree at exact `16fc03aa` produced a local ad-hoc-signed ZIP, SHA-256 `a58196e8f0a0e8a8ae067ee49556679b8d0b655a9026ae1ec7f28ecf77716286`; the extracted app passed `codesign --verify --deep --strict`, and its App DLL SHA-256 was `4fd567b6632ac3a7881379e605619db8e7aab1d43cba04b62df3f86c52433a8a`. An isolated `--new-instance --data` native process at that exact source opened the unchanged owned fixture. **Commands → Flat view (all files below this folder)** showed both `report.txt` rows and **0 folders, 2 files · 33 bytes** within two seconds, with the completion banner, without Reread. Independent SHA-256 values still matched both originals. The test does not claim sorting, downstream file operations from duplicate names, or a final installed candidate.

## Disposition and stopping rule

Medium misleading-results UX issue, remediated preliminarily. The exact-source native after-check and focused test are complete. Latest-product producer, Windows x64, Windows ARM64, Ubuntu and macOS CI and skip reconciliation remain; final installed-candidate acceptance is separate. Public preview.5 predates I586. Reopen only for a contradictory visible result or a consequential CI/native regression. The exploratory clean streak resets to **0/20** after this correction; the preceding five clean journeys remain historical. Stable 1.0.0 remains **NO-GO**.
