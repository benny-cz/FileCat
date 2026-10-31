# I588 — Synchronization review announced a class name for each step

## User consequence and accepted boundary

On the exact `699356ba` native Mac build, **Commands → Compare directories → Synchronize** over owned left/right folders showed a correct read-only Update plan: one `only-left.txt` copy and three differences left alone. The native accessibility row for that proposed step was named `FileCat.Core.Compare.SyncItem`. Its visible cells and checkbox were understandable, but a user moving through review rows with assistive technology could not hear the action, file or current inclusion. The neighboring I587 directory-difference result rows had already been corrected; this is a separate synchronization-plan list, and existing Sync tests checked choices and jobs rather than the native row peer. No synchronization was started.

## Correction and bounded verification

Product `b28e2ad458b53035c78f823f4517a4e0777136e7` makes the sync item describe its action, relative path, included/excluded choice and reason. The actual Windows App automation-peer test failed before with `FileCat.Core.Compare.SyncItem`; the affected `DirectoryDiffTests` class then passed **5/5** with the new row-name test, including an excluded-state check.

A fresh Mac worktree at exact `b28e2ad4` produced an ad-hoc-signed local ZIP, SHA-256 `97960501fd5db2eb7e04b48abc35cd6be41c9e87fff9dc75d4f6a55dcbcb1226`; its App DLL SHA-256 was `1f69d5a308025ff5a42ef0dbba33235265d2f0399a63b03c3c76afb0290fdcd0`. The extracted bundle passed `codesign --verify --deep --strict`. In a new isolated process, the recursive content comparison still reported four differences, and the sync review's AX row read **`copy only-left.txt, included, Only in the source`**. Toggling its checkbox changed the row to **`copy only-left.txt, excluded, Only in the source`** and the summary to **Nothing to do**; Cancel returned to the comparison. The eight owned source hashes matched the preceding I587 fixture receipt. This checks the reviewed, non-mutating Mac UI and Windows peer, not spoken screen-reader output, every mode or final installed candidate.

## Disposition and stopping rule

Medium accessibility/UX issue, remediated preliminarily. Latest-product producer, Windows x64, Windows ARM64, Ubuntu and macOS CI with skip reconciliation remain, as does exact installed-candidate acceptance. On that candidate, open a reviewed synchronization with a copy and excluded step; accessible row navigation must name the action, relative path, current choice and reason, with no class names, while Cancel leaves both trees unchanged. Stop after that affected route and latest-product five-lane CI; do not replay accepted synchronization safety or I587 difference rows without a new consequence. Public preview.5 predates I588; stable 1.0.0 remains **NO-GO**. The exploratory streak resets to **0/20**.
