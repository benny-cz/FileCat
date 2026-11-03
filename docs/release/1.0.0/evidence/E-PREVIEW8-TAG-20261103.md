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
targets the same source. Its producer and all four platform source jobs passed. The
tagged run's 20 downloaded TRX files exactly match the exact-source main run by
relative file, test name, outcome, message and multiplicity: **41,497 Passed /
1,572 NotExecuted / 0 Failed**. Three package jobs passed; their downloaded
producer files and manifests are checked below. The protected private draft
and its hosted assets have also been checked below.

Stable 1.0.0 remains **NO-GO**. Public preview.8 publication requires the separate
DEC-07 owner decision on the exact hosted assets and SHA-256 hashes after the protected
private draft has been independently reconciled.
## Exact hosted inventory

All three tagged package jobs passed. Their workflow artifacts are Windows
`filecat-release` ID `13244513371`, GitHub-reported SHA-256
`10352a0cd10d126bc25dc8497ddc3734906f32480f8186c648adfc354a7434f7`;
Linux `filecat-linux` ID `13244122560`, SHA-256
`c2ecd1183cd00c7b20246c20836280b9adeece85ee06363340f625c88857b37d`;
and macOS `filecat-macos` ID `13246485538`, SHA-256
`23dee8f785af70e7bcfd6ec313e0f00ec3f0fb4a31c72d2137d869a4d4e54a87`.
The downloaded archives contain 24 unique producer files. Every name, byte size
and locally computed SHA-256 matches its platform manifest. The manifests bind
the exact tag version and product source, and each states `CandidateQualified:
false` and `SigningVerified: false`. Their SHA-256 values are Windows
`27a4fd792d55b53ad6578816606a5542ef66e23b6123b48a493022de905e74a7`,
Linux `8bb2b1255ac6a88f7124e50e78dff9d661e7da31556fce8f63d424df431c9b57`
and macOS `e76774ad82699d35150e6ee24b69888be6e7e38514b27704f45590c0bb7c5f3c`.

The official Windows x64 portable ZIP has 447 members. Every member passed a
path-traversal and symlink screen, CRC passed, and the archive was extracted to
an owned ignored workspace directory. Its `FileCat.portable` marker exists;
`FileCat.exe --version` exited 0 and printed `FileCat 1.0.0-preview.8`.
This is a noninteractive package-start check, not installed stable-candidate
qualification.

The owner (`benny-cz`) approved the protected `release` environment review, and
the private draft job passed. A fresh authenticated API read of release
**432893243** returned `draft: true`, `prerelease: true`, no publication time
and 24 unique assets in `uploaded` state. Every hosted name, byte size and
server SHA-256 digest matches the independently downloaded producer file and
its platform manifest. The six workflow receipts (three preflight and three
upload) agree on repository, tag, source, manifest hashes, release ID and all
24 hosted upload IDs, with no extra or missing ID. This metadata check does
not claim every hosted large package was independently downloaded a second
time. The three hosted manifests were separately downloaded and found
byte-identical to their producer manifests. The receipts workflow artifact is
ID `13246522527`, with GitHub-reported SHA-256
`021eb76b1af40d64335519a164773fbf40f08416cac4b91a1b2c55e1f46486ae`.
The retained private API snapshot SHA-256 is
`b6378d3ddcafcba3e16b768a5552c1a5032203373fe3c3d54f0f4699139dec46`.

| Hosted asset | Bytes | SHA-256 |
|---|---:|---|
| FileCat-1.0.0-preview.8-linux-assets.json | 1606 | 8bb2b1255ac6a88f7124e50e78dff9d661e7da31556fce8f63d424df431c9b57 |
| FileCat-1.0.0-preview.8-linux-x64-appimage-runtime-source.tar.gz | 4704832 | be7816ce0e129b0417758f942378bed89350e04fb1700c7bd5caa1005d72e587 |
| FileCat-1.0.0-preview.8-linux-x64.tar.gz | 73603986 | 481d99447ce395ab85b580de8350ffbf0669ff7ad57bd9d18ec51dc1cdf201e9 |
| FileCat-1.0.0-preview.8-macos-assets.json | 690 | e76774ad82699d35150e6ee24b69888be6e7e38514b27704f45590c0bb7c5f3c |
| FileCat-1.0.0-preview.8-osx-arm64.zip | 71995857 | 6858509c26cecc2a6f6635a4ef9ece66e768f5efe41fbaa2394e303e5713a666 |
| FileCat-1.0.0-preview.8-win-arm64-fdd.zip | 66403586 | 3b97df31d12aa57981efa9e7f61c2c82d27c5d401a03176f977bbd7a81577621 |
| FileCat-1.0.0-preview.8-win-arm64-portable.zip | 101637270 | 68962d0b4ccb1fdf1514dda51530b9c16952dcf4dbffd1c63e99ab1c6b24939b |
| FileCat-1.0.0-preview.8-win-x64-fdd.zip | 68996783 | ec5389502ef270e9c85528199134b4dbadb5f0c05ee453abb1d8ee4bce76936e |
| FileCat-1.0.0-preview.8-win-x64-portable.zip | 105877555 | ee5b342a1083d74a3d7b2c90292b1178db1a8c0301432d04355aa30418ac63e6 |
| FileCat-1.0.0-preview.8-windows-assets.json | 2580 | 27a4fd792d55b53ad6578816606a5542ef66e23b6123b48a493022de905e74a7 |
| FileCat-1.0.0-preview.8-x86_64.AppImage | 68090360 | 0747b621e30395e4f5d0ab2327bb6dc8b21e1bc0eef1479bd9ec35488fd1f6de |
| FileCat-1.0.0-win-arm64-setup.exe | 69476912 | bf2df173d3a77b69f1631ddacb27cae29cc6924a41a393c8312e29399f9266f5 |
| FileCat-1.0.0-win-x64-setup.exe | 74983151 | b6752a70f675ac4c6d8c480392f79eb1e3a2d2b84079ec06e70446af0eb7be19 |
| filecat_1.0.0-preview.8_amd64.deb | 59852442 | 10fbf3e0849f18c26d78fec8f7e37ae75dd0cfe8797c93cb2f96846d4a21b6ff |
| sbom-1.0.0-preview.8-linux-x64-appimage.spdx.json | 331959 | 8b6c11e4b72f6242493fd09ab1fbacd9ad39c2561f3bfee07a9fc08303c53766 |
| sbom-1.0.0-preview.8-linux-x64-deb.spdx.json | 322435 | 75154ce13a11c300b941ad7638f8678790429f14a524d53029788edfe9e65d05 |
| sbom-1.0.0-preview.8-linux-x64-tar.spdx.json | 310073 | 1a4695e7469077614b621d2336cab24b6895f5b117af06769f375ec41a372833 |
| sbom-1.0.0-preview.8-osx-arm64-macos-app.spdx.json | 316541 | 8b2d23e39933a70e093a88cedd2d8cb673ec42eacdcca1128a96df8657ebfa75 |
| sbom-1.0.0-preview.8-win-arm64-fdd.spdx.json | 202526 | cd79361364bfd2f37365f2aec895baa7070f3ff1db53c89c50b3ec4707c9c6a5 |
| sbom-1.0.0-preview.8-win-arm64-portable.spdx.json | 326794 | 09503aefcdd58ec3f1a489272c835a53bba0b5c10dfd6624d83375c55024b245 |
| sbom-1.0.0-preview.8-win-arm64-setup-payload.spdx.json | 331425 | 1826e3bbd7dd75dd57134383fb855751184a9636c357599c2c77f852a2493ced |
| sbom-1.0.0-preview.8-win-x64-fdd.spdx.json | 202517 | 08e82511ecb56be572ff5ba46bf2c8fa95c308e9dae554d59fd340788e4791e5 |
| sbom-1.0.0-preview.8-win-x64-portable.spdx.json | 326770 | 6aac6dd7b9acd1456d1769b5888e3c6627cfbc9de2232a94de78fefeefea235f |
| sbom-1.0.0-preview.8-win-x64-setup-payload.spdx.json | 331401 | 47c7a317a12d3646f870c58edcac39570def8da1d866693533ac3d287f29e885 |

## Prepublication decision gate

The private draft body exactly matches the committed
[preview.8 notes](https://github.com/benny-cz/FileCat/blob/3f92a8cdc4e628c1d9ce58a42f7af44f91d0e95a/docs/release/1.0.0/PREVIEW8_RELEASE_NOTES.md),
SHA-256 `63452fcfcf92eb7c49a593ba6f3c81229a14917370008a5377c78b70e7238430`.
The first body-only REST update returned an `untagged-*` draft name, despite
retaining release ID **432893243**, draft/prerelease flags, exact body and all
24 assets. A second private REST update explicitly supplied the existing
`v1.0.0-preview.8` tag, exact source, name, notes and draft/prerelease flags.
An independent fresh read confirmed the correct tag association, source,
private state, exact notes and all 24 hosted assets and six receipts unchanged.
The retained final private API snapshot SHA-256 is
`c6dc31ba51013f7525418744ce1d86c2d6b4f22ad829094ccf3097ae6d36a68b`.
The immutable annotated tag object remains `774f8882`.

DEC-07 requires a separate owner decision on this exact hosted inventory. The
proposed publication is an **unsigned prerelease** with `make_latest: false`.
After publication, verify unauthenticated public state, exact notes, 24 asset
hashes and signed GitHub release attestation. Stable 1.0.0 remains **NO-GO**.
