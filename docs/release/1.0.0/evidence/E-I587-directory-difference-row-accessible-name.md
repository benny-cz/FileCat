# I587 — Directory comparison spoke internal records as result names

## User consequence and accepted boundary

An isolated exact-`16fc03aa` native Mac session compared two owned directory trees with **Content** and **Include subfolders** selected. The visible preview correctly reported **4 differences in 2 folders: 1 only left, 1 only right, 2 different; 1 the same**. Its four native accessibility rows, however, were named with the generated `TreeDiffEntry { RelativePath = …, Kind = …, Left = FileCat.Core.Resources.EntryData, …, RemovalReview = … }` record text. The first useful filename and status were buried among implementation objects and path/review fields; this is a concrete assistive-navigation and comprehension problem. Existing directory-diff tests accepted comparison results and result-set handoff, but did not assert the native row names. I583 corrected a different command-search result control; it is a precedent, not a passed check for this list.

The fixture had `changed.txt` and `sub/nested.txt` with equal-size but different bytes, `only-left.txt`, `only-right.txt`, and matching `common.txt`. All eight source files and their SHA-256 values were independently recorded before and after; no comparison action wrote either tree. The test is a read-only preview, not a synchronization or destructive action.

## Correction and bounded verification

Product `699356bade8353611cfc8bd410c8bd821c4c7793` gives each `TreeDiffEntry` a concise human status shared by the visual status cell and its native list-row name. A row now names its relative path, status and any useful detail, without source objects or review internals. The focused Windows App automation-peer test failed before the change because it received the generated record string; after the change, the affected `DirectoryDiffTests` class passes **4/4**.

A fresh Mac worktree at exact `699356ba` produced an ad-hoc-signed local package. ZIP SHA-256 was `94cdc253d751a1b68eef82993dd9c29fc0daeeea7e4c4cc7c554a90f1425f9c0`, App DLL SHA-256 `69f99377e73644b1b40604dfd79ae703ae7dbde3fed21709c260f5b17895dae1`, and the extracted app passed `codesign --verify --deep --strict`. A new isolated `--new-instance --data` process repeated the exact read-only comparison. Native AX named the four rows **`changed.txt, different, Same size, different content`**, **`sub/nested.txt, different, Same size, different content`**, **`only-left.txt, only left`**, and **`only-right.txt, only right`**; the four-difference summary stayed exact. Independent hashes of all eight fixture files remained unchanged. This checks native row comprehension on Mac and the focused peer contract on Windows, not screen-reader speech, every filter view, or a final installed candidate.

## Disposition and stopping rule

Medium accessibility/UX issue, remediated preliminarily. Latest-product producer, Windows x64, Windows ARM64, Ubuntu and macOS CI with skip reconciliation remain, as does exact installed-candidate acceptance. Public preview.5 predates I587. Reopen only for a contradictory native/assistive result or consequential CI regression. The exploratory clean streak resets to **0/20** after the exact-source native correction; the four preceding clean I586-source journeys remain historical. Stable 1.0.0 remains **NO-GO**.
