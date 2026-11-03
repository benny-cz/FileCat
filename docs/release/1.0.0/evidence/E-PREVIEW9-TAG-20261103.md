# Preview.9 source and release inventory — 3 November 2026

The unsigned `v1.0.0-preview.9` annotated tag object
`666d90bba3a937e294f9ea45ca7931a8b86dcb0f` was independently read back from
GitHub and peels to exact source `b333eebfa0b0745bc632c2b339330b2d454131c7`.
The source adds a background startup offer for newer published releases, corrects
shipped owner attribution, and stabilizes Picture-grid thumbnails through unchanged
refreshes and scrolling while avoiding automatic reads of offline files. The
[preview.9 notes](../PREVIEW9_RELEASE_NOTES.md) describe the user-visible changes
and preview limits. These are source and prerelease checks; they do not qualify
an installed stable candidate.

GitHub's active creation ruleset 26082787 permits repository administrators to
create `v1.0.0*` tags. The separate active no-update/no-delete ruleset 26082784
covers `refs/tags/v1.0.0*` and has zero bypass actors. The protected `release`
environment requires the owner `benny-cz` to review the private draft upload.
GitHub had no preview.9 tag or release before preparation.

The exact source passed the producer, Windows x64, Windows ARM64, Ubuntu and
macOS jobs in [main run 41711782919](https://github.com/benny-cz/FileCat/actions/runs/41711782919).
Its 20 downloaded TRX files contain **41,511 Passed / 1,574 NotExecuted / 0 Failed**.
Compared with the preview.8 exact-source main run's 20 TRX files, 14 passing
rows and two intentional Unix skips for the Windows offline-attribute test were
added. No prior row was removed or changed. The new passes cover startup update
offer/skip behavior and Picture-grid thumbnail stability and offline refusal on
the applicable platform lanes. The [preview.9 tagged run](https://github.com/benny-cz/FileCat/actions/runs/41719277473)
targets the same source and passed all five required source jobs. Its 20
downloaded TRX files exactly match the main run by relative file, test name,
outcome, message and multiplicity: **41,511 Passed / 1,574 NotExecuted / 0
Failed**. The three package jobs and protected private draft passed; their
producer and hosted inventories are checked below.

Stable 1.0.0 remains **NO-GO**. Public preview.9 publication requires the
separate DEC-07 owner decision on the exact hosted assets and SHA-256 hashes
after the protected private draft has been independently reconciled.

## Exact hosted inventory

All three tagged package jobs passed. Their workflow artifacts are Windows
`filecat-release` ID `13274057681`, GitHub-reported SHA-256
`9d3fede01664a88f0fec410d8d800bac7041c3d57dab25177cb8e7cbcf2cf33a`;
Linux `filecat-linux` ID `13274626719`, SHA-256
`aff5287bc8822091496ebce7f0479e800d27774db4cf674936b5db831024743d`;
and macOS `filecat-macos` ID `13274156697`, SHA-256
`f4b3ce6decb61ca87627911374554dc626807f0c77263bbd2eef76537b21f0d7`.
The downloaded archives contain 24 unique producer files. Every name, byte
size and locally computed SHA-256 matches its platform manifest. The manifests
bind the exact tag version and source, and each states `CandidateQualified:
false` and `SigningVerified: false`. Their SHA-256 values are Windows
`5bea418b30bdb3a9a9931648f3be141d485e0ba9e26cfc426dee13b1c9ce1203`,
Linux `f6bef76eaee338c7b3665539445d3dd5c1ec9de0700f983041d7dfbdbdbcd411`
and macOS `6390aec22339a154d33e748704080389171f074b34edaa5e6192b520c8f73e4a`.

The official Windows x64 portable ZIP has 447 members. Every member passed a
path-traversal and symlink screen, CRC passed, and the archive was extracted to
an owned ignored workspace directory. Its `FileCat.portable` marker exists;
`FileCat.exe --version` exited 0 and printed `FileCat 1.0.0-preview.9`.
This is a noninteractive package-start check, not installed stable-candidate
qualification. The owner-reviewed private draft is reconciled below.

| Hosted asset | Bytes | SHA-256 |
|---|---:|---|
| FileCat-1.0.0-preview.9-linux-assets.json | 1606 | f6bef76eaee338c7b3665539445d3dd5c1ec9de0700f983041d7dfbdbdbcd411 |
| FileCat-1.0.0-preview.9-linux-x64-appimage-runtime-source.tar.gz | 4704832 | be7816ce0e129b0417758f942378bed89350e04fb1700c7bd5caa1005d72e587 |
| FileCat-1.0.0-preview.9-linux-x64.tar.gz | 73607173 | 4967349f1de76ef84f1dac8f838d94b5fd019c43ca494143c8e64ddab4c234aa |
| FileCat-1.0.0-preview.9-macos-assets.json | 690 | 6390aec22339a154d33e748704080389171f074b34edaa5e6192b520c8f73e4a |
| FileCat-1.0.0-preview.9-osx-arm64.zip | 72002789 | dc3a8934681783382e33525c45485479d76e5090b0a1b918a3f7f981ad4b631b |
| FileCat-1.0.0-preview.9-win-arm64-fdd.zip | 66412015 | d3ec64be70b6326924efa5f6d5b3f2fc207b2878d4bb01b1b66653c7016e5bad |
| FileCat-1.0.0-preview.9-win-arm64-portable.zip | 101645697 | 720858bdeed816d3c6ecca20404b3ba167e9ef9b9a86641e78ae5e98c9624279 |
| FileCat-1.0.0-preview.9-win-x64-fdd.zip | 69004413 | 043ce6da862c49b223ad85ddb43da6a7b4d0adc11041ab961bcd23be6ba4a1ec |
| FileCat-1.0.0-preview.9-win-x64-portable.zip | 105885184 | 92e60986c1f8d2632c9a0b2b6098e2b3994fa63f08690c9b7941b78638867ac1 |
| FileCat-1.0.0-preview.9-windows-assets.json | 2580 | 5bea418b30bdb3a9a9931648f3be141d485e0ba9e26cfc426dee13b1c9ce1203 |
| FileCat-1.0.0-preview.9-x86_64.AppImage | 68094456 | e282ca9feb555eb6561e6f4168c2e536b63894e5c09d30f69d6c8cb9eb62775c |
| FileCat-1.0.0-win-arm64-setup.exe | 69479146 | d3a4c64fc07cb81e6b20b218c66f1cf717131248cdbd366eabcf9fd9e8bed82f |
| FileCat-1.0.0-win-x64-setup.exe | 74989787 | 3dfc8ef43937c492071916f777cd7b2e58b654b40c477806cc8448f3172a640e |
| filecat_1.0.0-preview.9_amd64.deb | 59859092 | 39bba7c6a37374f8112492e12c7800b1edb8f4a9119d6f738be0c8e0285774d3 |
| sbom-1.0.0-preview.9-linux-x64-appimage.spdx.json | 332383 | da98502cb79688b45a8dc37c1e9b0d4773c153184ed8af6c65ac56961a02fb1c |
| sbom-1.0.0-preview.9-linux-x64-deb.spdx.json | 322859 | e5036b37319a3dfed39bb4b01b591edc72b36bbc897c9564d456f28d3fdf3e87 |
| sbom-1.0.0-preview.9-linux-x64-tar.spdx.json | 310497 | e82935e443e99629ad56dd1f1ba0b0baf051b97b704559b7f08202c8703c14f9 |
| sbom-1.0.0-preview.9-osx-arm64-macos-app.spdx.json | 316965 | 7ebd36bc787485721a0f2b48a68901c3382f3b890489643e55c29f0b9199cb1b |
| sbom-1.0.0-preview.9-win-arm64-fdd.spdx.json | 203003 | 87531b117534232d3932992ba018dd154e65d53a62fa11b740e7f63a4b6b28a7 |
| sbom-1.0.0-preview.9-win-arm64-portable.spdx.json | 327271 | 36ed754e078bd1a2b808a85d8289e7e2dc2f0269884f4d7fa2b447cba86b47f3 |
| sbom-1.0.0-preview.9-win-arm64-setup-payload.spdx.json | 331955 | 912aee1104c32405b66e395f8610ae66a9269c8ecdea4e5cc0e0ced64f5bf6fd |
| sbom-1.0.0-preview.9-win-x64-fdd.spdx.json | 202994 | 56da3d9faeb52b8c7d5f57c9474598298ce6cdaed36ce6b440d90ed7b08a0619 |
| sbom-1.0.0-preview.9-win-x64-portable.spdx.json | 327247 | 536497cec516304dc4f81fbed38f67410330a34ae4a0999f093bbba0d59d337c |
| sbom-1.0.0-preview.9-win-x64-setup-payload.spdx.json | 331931 | ea77e7354f7b33bfeaf2637e11d0f0a1777b6dcecf87a1abac1a4f7bcaa2fb52 |

## Private draft review

The owner (`benny-cz`) approved the protected `release` environment review,
and the private-draft job passed. A fresh authenticated API read of release
**433271735** returned `draft: true`, `prerelease: true`, no publication time
and 24 unique assets in `uploaded` state. Every hosted name, byte size and
server SHA-256 digest matches the independently downloaded producer file and
its platform manifest. The six workflow receipts (three preflight and three
upload) agree on repository, tag, source, manifest hashes, release ID and all
24 hosted upload IDs, with no extra or missing ID. This metadata check does
not claim every hosted large package was independently downloaded a second
time. The three hosted manifests were separately downloaded and found
byte-identical to their producer manifests. The receipts workflow artifact is
ID `13274574383`, with GitHub-reported SHA-256
`f148c707785175b5a95731f13cd9462660f339473ebcf81d8dd33db65cb9d656`.
The retained private API snapshot SHA-256 is
`a8baa21eaebc9b70e9496f50f49e85caa30c20fd0ed505e802be0558eed7ca38`.

The initial draft body was empty. The first private REST update explicitly
supplied the existing tag, exact source, name, committed notes and
draft/prerelease flags. A later UTF-8 byte comparison found that its
Windows-constructed payload had altered a few Unicode symbols in the notes.
Its retained intermediate private API snapshot SHA-256 is
`3a563e70f981e7f1f4aba8a13922c0cc8906368b7142027eade28bd45ba47875`.
A second private REST update supplied an explicit UTF-8 payload before
publication. Its response and a byte comparison confirmed the existing
`v1.0.0-preview.9` tag, source `b333eebf`, `draft: true`,
`prerelease: true`, no publication time, the exact
[committed preview.9 notes](https://github.com/benny-cz/FileCat/blob/75af9469/docs/release/1.0.0/PREVIEW9_RELEASE_NOTES.md)
at SHA-256 `bc9ffd473e710f3d2c82d34c48e461d5e50445bab9fde5df3f4af350cb22cd63`,
and all 24 hosted asset IDs, names, sizes, states and digests unchanged.
The corrected private response SHA-256 is
`0c42d7f15fb1e55e438ad068d8ff84e928d72851fbacfed1acca2b08becac411`.

## Public preview publication

The owner separately approved public publication of the exact 24-asset
unsigned preview.9 inventory and committed notes. The final private
response matched the approved tag object `666d90bba3a937e294f9ea45ca7931a8b86dcb0f`,
peeled product source `b333eebf`, release ID, corrected notes and all 24
hosted asset identities. GitHub published the same draft at
**2026-11-03T22:24:32Z** as a prerelease with an explicit
`make_latest: false` request: [FileCat v1.0.0-preview.9](https://github.com/benny-cz/FileCat/releases/tag/v1.0.0-preview.9).

An unauthenticated public release-by-tag API read returned HTTP 200, the
same ID/tag, `draft: false`, `prerelease: true`, `immutable: true`,
byte-exact committed notes and all **24 unchanged uploaded assets** matching
the inventory above. The retained public API snapshot SHA-256 is
`9783da3f87adc5d884e5134c865c8ea84f325850ec4ca17ade0ea656fd542cc1`.
GitHub CLI's signed release-attestation verification passed. Its verified
in-toto statement binds the annotated tag object
`666d90bba3a937e294f9ea45ca7931a8b86dcb0f` and the exact SHA-256 of
all 24 assets; the retained JSON verification SHA-256 is
`be2ce415583ae122409aa64cd82c73163d5aa0cfb91d6353475d72ac3789db25`.

The official public `FileCat-1.0.0-preview.9-win-x64-portable.zip` was
downloaded again without authentication. Its **105,885,184 bytes** and
locally computed SHA-256
`92e60986c1f8d2632c9a0b2b6098e2b3994fa63f08690c9b7941b78638867ac1`
match the producer manifest, GitHub server digest and signed attestation;
`gh release verify-asset` passed. The byte-identical producer ZIP had
already passed safe extraction, CRC and noninteractive
`FileCat 1.0.0-preview.9` start checks. This release-integrity check is
not a trusted Windows code signature or installed stable-candidate
qualification. Stable 1.0.0 remains **NO-GO**.
