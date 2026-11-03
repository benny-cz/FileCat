# Preview.8 source, tag and release inventory — 3 November 2026

The unsigned `v1.0.0-preview.8` annotated tag object
`774f8882490c8cece1ead58e594250c42f0be885` was independently read back from
GitHub and peels to exact product source `0ca7a7a4cac065122bc41f85e6c84e6dbbb01e52`.
It includes I622's Analyze folder
chooser correction and I623's same-window picture browsing, thumbnail grid and final
state-aware View menu wording since preview.7. The [preview.8 notes](../PREVIEW8_RELEASE_NOTES.md)
describe these changes and the preview limits. This is source and prerelease evidence;
it is not installed stable-candidate qualification.

GitHub's active creation ruleset 26082787 permits repository administrators to create
`v1.0.0*` tags. The separate active no-update/no-delete ruleset 26082784 covers
`refs/tags/v1.0.0*` and has zero bypass actors. The protected `release` environment
requires the owner `benny-cz` to review the private draft upload. GitHub had no
preview.8 tag or release before preparation.

The preceding green product source `4b0fb0fb8d08c68bd3f69abc50e045da0d50e166`
passed the producer and Windows x64, Windows ARM64, Ubuntu and macOS jobs in
[main run 41641247809](https://github.com/benny-cz/FileCat/actions/runs/41641247809).
Its 20 downloaded TRX files contain **41,497 Passed / 1,572 NotExecuted / 0 Failed**.
Compared with the preview.7 exact-source main run's 20 TRX files, 36 passing rows were
added and no prior row was removed or changed. Those additions are the I622 chooser,
I623 picture/grid and Skia rendering cases on all four platform lanes. The final
menu-wording product commit passed all five required jobs in
[main run 41648160388](https://github.com/benny-cz/FileCat/actions/runs/41648160388).
Its 20 downloaded TRX files exactly match the preceding green product run by relative
file, test name, outcome, message and multiplicity: **41,497 Passed / 1,572
NotExecuted / 0 Failed**. Against preview.7, 36 passing rows were added, with no
removed or changed prior row. The
[preview.8 tagged run 41656633973](https://github.com/benny-cz/FileCat/actions/runs/41656633973)
targets the same source. Its five source jobs, three package jobs, 20 TRX files,
producer manifests, packages and protected private draft require separate checks.

Stable 1.0.0 remains **NO-GO**. Public preview.8 publication requires the separate
DEC-07 owner decision on the exact hosted assets and SHA-256 hashes after the protected
private draft has been independently reconciled.
