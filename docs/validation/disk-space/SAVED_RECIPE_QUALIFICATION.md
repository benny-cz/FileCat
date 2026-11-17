# Saved custom recipe filters

Both analysis views let users save a declarative filter, choose its captured locations, and preview its matches. A saved recipe belongs to one recorded session, epoch, and set of root bindings. The view lists recipes for the matching capture when it is reopened. The selected locations constrain the whole query, including an `OR` term that names a different root.

The private recipe codec accepts only its versioned fields. It uses the existing bounded filter grammar and refuses malformed, oversized and duplicate fields. Recipe files use generated names, share the guarded private catalog writer lock, and publish create-only. They remain separate from the named inventory entries and profile-identity record. A failed read or save preserves existing records.

| Check | Windows VM | Mac | Ubuntu VM |
| --- | --- | --- | --- |
| Rule, scope and private-store checks, r1354 | 20 passed | 20 passed | 20 passed |
| Native filesystem and rendered dialog workflows, r1354 | 5 passed | 5 passed | 5 passed |
| Native FileCat window, saved filter and branch refresh, r1355 | Passed on guarded retry | Passed | Passed |

The native filesystem dialog tests saved and reopened a query that referred to two different roots. Both storage modes presented just the requested root and preserved the files in each location. The preview only reads captured metadata and never places candidates in marks or the review basket.

The native window journey saved a filter, reopened it from the private store, and inspected its recorded file. It then refreshed a branch and checked the original marks, review basket, and source files. Windows postponed its first launch when another process appeared. The passing retry verified the same uploaded files and ran with the ordinary interactive account. The Mac compositor showed the complete saved-filter preview at 2×. The [qualification manifest](source-manifest-r1356-saved-recipes.json) indexes the exact source and test receipts. The full Mac desktop image remains local. Physical input, assistive technology, public command registration, and the independent release review remain open.
