# Preview.11 — exact source, immutable tag and release inventory

The owner selected `v1.0.0-preview.11` after the protected preview.10 tag
failed two ARM64 CI attempts and produced no packages or release.
[Preview.10 evidence](E-PREVIEW10-TAG-20261116.md) retains both failed jobs,
logs, receipts and the exact-source green main run. The old tag is preserved.

The source is `7eb4766f9398e095673b5cb1f92849dd07a4edde`.
Its only code change from preview.10 is a test collection declaration:
`ZipListingRevisionTests` runs without competing Core test I/O. Production
background priority, the 120-second wait and all content/change assertions
are unchanged. A locked restore, isolated build and all 22 existing provider
and revision tests passed locally, with no failures or skips. The corrected-source hosted ARM64 case also passed in **10.55 milliseconds**,
with the production background priority and all assertions retained.

[Main run 43873655390](https://github.com/benny-cz/FileCat/actions/runs/43873655390)
passed all five required source jobs, including ARM64 package startup and
installer checks. Its 20 primary TRX contain **60,091 passed, 5,742 skipped
and zero failed**; every result matches the preceding exact-source green
preview.10 main run by path, test name, outcome, skip message and multiplicity.
No passing result was lost and no new skip was introduced. The retained
20-file SHA-256 inventory hashes to
`416e53511d31e8b4e4667481999b2c0b9b28c2e57533ca7157279a4472ea3834`.
These primary counts do not claim execution of the separate opt-in native
archive qualification lanes described in the implementation records.

Only after that full CI and receipt comparison passed, the owner-selected
`v1.0.0-preview.11` annotated tag object
`bf9d99aac02706f08252243954ad1b4f8e93aef2` was created. Independent GitHub ref
and tag-object reads confirm that it peels to the exact source above. Active
rulesets 26082787 and 26082784 allow administrator creation and forbid tag
updates/deletion with no bypass actors for those mutations. Immutable releases
remain enabled; the `release` environment requires `benny-cz` review and accepts
only `v*` tags.

[Tagged run 43884729278](https://github.com/benny-cz/FileCat/actions/runs/43884729278)
passed all five required source jobs after the targeted macOS retry. The
[preview.11 notes](../PREVIEW11_RELEASE_NOTES.md) compare this release with the
last published version, preview.9, and pin feature documentation to the
immutable product source.

## Tagged attempt 1: macOS runner capacity

The tagged ARM64 Core step passed the corrected ZIP fixture. The macOS job
`133359834940` was cancelled without a runner (`runner_id: 0`, empty runner
name and zero steps). Its check annotation says the hosted runner was not
acquired after multiple attempts and records macOS ARM64 capacity constraints.
This is an allocation failure, not a failed FileCat test. The raw annotation
snapshot SHA-256 is `0c571d04055174a9e1244e0721e69908e367ef6d000f72ec196eb594a23ad61e`. The producer, Windows x64, ARM64 and Ubuntu jobs all passed. Both Windows
primary receipt sets match main exactly; the tagged ARM64 ZIP case passed in
14.78 milliseconds. After this first attempt completed, a targeted macOS-only
retry and its dependent jobs were requested at the same source/tag (attempt 2). The source and
immutable tag remain unchanged.

## Tagged Ubuntu fixture difference

The tagged Ubuntu job passed. Its 15,573 primary result rows match the
corrected-source main run except one filesystem probe:
`UnixDeviceTests.A_folder_reached_from_memory_through_a_link_is_on_the_disk_the_link_leads_to`
skips with the recorded reason that the test folder is on no disk this system
can name. It passed on the corrected-source main runner. This is a
runner-fixture availability difference at the identical source, not a code
change or failed assertion. Tagged Ubuntu totals are **15,049P/524S/0F**,
versus main Ubuntu **15,050P/523S/0F**. The final tagged aggregate retains
this difference. All 20 tagged primary TRX reconcile to main with this
single recorded change: **60,090 passed, 5,743 skipped and zero failed**.
Both Windows sets and all macOS rows match main exactly. The tagged
20-file raw TRX inventory SHA-256 is
`faf2070ab1fb66d671c5bdbd4732c91e4b1c90e17512a8500669ff4f3a3461cf`.

The macOS retry acquired runner job `133391779524`; Core, Remote, App,
Rendering, Page-engine and both IPC checks passed. App tests completed
at 2026-11-16T16:15:54Z. All three dependent package jobs were then
released. No product or fixture change followed the tag.

## Exact hosted inventory

All three tagged package jobs passed. The artifacts below belong to run
`43884729278` at source `7eb4766f9398e095673b5cb1f92849dd07a4edde`.
The container digests are GitHub-reported; payload files were separately
downloaded and hashed locally.

| Platform | Workflow artifact | Artifact ID | Reported container SHA-256 |
|---|---|---|---|
| windows | `filecat-release` | `14244968594` | `sha256:895dce4f46428748022d17a25589c696b2a6d00a664ced61e6fcd71c38659347` |
| linux | `filecat-linux` | `14245457783` | `sha256:8f1af467adc3acef50ec21a60f5dff5b71f5dd8fc193d9eaada2ee62e2c47833` |
| macos | `filecat-macos` | `14244963403` | `sha256:36d9788a708c4e213638f06d36c000c091b2b44055af2d57c4db4f0b8f12ac9d` |

All **24 unique producer files** match their manifests by name, byte size
and locally computed SHA-256. All three manifests bind version
`1.0.0-preview.11` and the exact source, with `CandidateQualified: false`
and `SigningVerified: false`. Manifest SHA-256 values:

- windows: `4ba448492049f4e426d2365b83609e50865771925d82437dd078eaaee4012105`
- linux: `d83c1b775a9535d6d1537f76b6fc302542d7d3b34f834c2aa47cc37d4dafe192`
- macos: `28505b0af25ab7f7a1972f3eb03a9c5f0868e8cdab38fdc5681ea0aba4039930`

The official Windows x64 portable ZIP passed path-traversal and symlink
screening, CRC and extraction to an owned ignored workspace folder.
Its portable marker exists; the noninteractive version check passed:

```text
ZIP members 447; safe paths, no symlinks, CRC pass; portable marker present
noninteractive package version: FileCat 1.0.0-preview.11, exit 0
```

This is a package-start check, not installed stable-candidate qualification.
The following exact inventory matches all 24 assets hosted in private draft
`447303206`; their IDs and digests reconcile with all six upload receipts.

| Hosted asset | Bytes | SHA-256 |
|---|---:|---|
| FileCat-1.0.0-preview.11-linux-assets.json | 1614 | d83c1b775a9535d6d1537f76b6fc302542d7d3b34f834c2aa47cc37d4dafe192 |
| FileCat-1.0.0-preview.11-linux-x64-appimage-runtime-source.tar.gz | 4704832 | be7816ce0e129b0417758f942378bed89350e04fb1700c7bd5caa1005d72e587 |
| FileCat-1.0.0-preview.11-linux-x64.tar.gz | 76292062 | d71cec74433f0cc8cdea06e60932a53e5d8e21bbea5a0d5763e66b9d6cfeb767 |
| FileCat-1.0.0-preview.11-macos-assets.json | 693 | 28505b0af25ab7f7a1972f3eb03a9c5f0868e8cdab38fdc5681ea0aba4039930 |
| FileCat-1.0.0-preview.11-osx-arm64.zip | 74541363 | 3797aed3d99b308a8d0141cfdbdf047bb7475fa12688386d963b0cb3eea86673 |
| FileCat-1.0.0-preview.11-win-arm64-fdd.zip | 69016010 | be50eadc3dfdeb9d01ba785362ffb4b6764c7527202ef2e11ed5620d205bccef |
| FileCat-1.0.0-preview.11-win-arm64-portable.zip | 104249698 | a5e6da6b07112d0979637e7e6a99f20f6e8a755ce2e351dbb1737e85b4fb2ed2 |
| FileCat-1.0.0-preview.11-win-x64-fdd.zip | 71747548 | 423e23eb30030ae319e482f7c9cffa2cd45efb38f9305d433a4441e19124d22f |
| FileCat-1.0.0-preview.11-win-x64-portable.zip | 108628324 | 4599155a8206c190e61524f1cf36b8cb257c032d5dbe5aa47579fff80bf5a55f |
| FileCat-1.0.0-preview.11-windows-assets.json | 2591 | 4ba448492049f4e426d2365b83609e50865771925d82437dd078eaaee4012105 |
| FileCat-1.0.0-preview.11-x86_64.AppImage | 70588920 | 28b6f1f5fe24658da62389074d94e13b2cc38fe8cf40fabd5a18c12d38187b26 |
| FileCat-1.0.0-win-arm64-setup.exe | 71560546 | 1cc2cc2b9d9020f5ac8521a316edfe24022e7725e548b47d5aa36042d34363f1 |
| FileCat-1.0.0-win-x64-setup.exe | 77204829 | 7008a90a3b9da5f6cce8ab822ee1423651931fd5a332d98542921237fc196b6c |
| filecat_1.0.0-preview.11_amd64.deb | 62222066 | e7f7a20df27c5d9bc282d7b0445780169496e3ab2936f33629b7b45b98f3d220 |
| sbom-1.0.0-preview.11-linux-x64-appimage.spdx.json | 333217 | ce20888eb3e9032dae4155cf3b16bb464967241dca5dbd2179860a85d12a3ddd |
| sbom-1.0.0-preview.11-linux-x64-deb.spdx.json | 323692 | 9047cffe8bb8d0e50d85aeafcefbbbf7fe9cfa146e2ccf2f48965b9e838a4eec |
| sbom-1.0.0-preview.11-linux-x64-tar.spdx.json | 311330 | 1951ab360015a9b9d650f3ee94e38d999119e03641e00b2af28b113cd632e9bc |
| sbom-1.0.0-preview.11-osx-arm64-macos-app.spdx.json | 318439 | 86a22032e26ca1aa95d4075e5eb2140d716bb077e920433acb68fab25093528f |
| sbom-1.0.0-preview.11-win-arm64-fdd.spdx.json | 203840 | 3d2ee83b11488db6e06267c49e8178326eb58ba11b77693438742f49a928313f |
| sbom-1.0.0-preview.11-win-arm64-portable.spdx.json | 328108 | e99545e78e4e22aeb6191b2b55dbce14f574ff1a008affb8f40c8e8a89ee0f6d |
| sbom-1.0.0-preview.11-win-arm64-setup-payload.spdx.json | 332793 | 2ea2fc33dfe6548196e26221dc2f4c117810b5c2ac7b88189278bbc1af0775c7 |
| sbom-1.0.0-preview.11-win-x64-fdd.spdx.json | 203829 | 07b4265913ea6596806eb5d35c02e0e342b451fe78fa788af18b4b74c669d925 |
| sbom-1.0.0-preview.11-win-x64-portable.spdx.json | 328082 | c5a54b29f7565ebccebf6d2489f4abd75717be3822a434ee3e62ece390a81790 |
| sbom-1.0.0-preview.11-win-x64-setup-payload.spdx.json | 332767 | 0999361304c3eb533691c3cfa19052699d5657e323a4248c63c7e5e7f7434094 |


## Owner-reviewed private draft

The owner approved the protected `release` review as `benny-cz` on
2026-11-16. The GitHub approval API independently confirms the approved
review for that environment. Tagged workflow `43884729278` then completed
successfully, including private upload and server-digest verification.

Draft release **447303206** has `tag_name: v1.0.0-preview.11`, exact
source `7eb4766f9398e095673b5cb1f92849dd07a4edde`, `draft: true`,
`prerelease: true` and no publication time. All 24 unique uploaded
asset names, byte sizes, SHA-256 server digests and states match the
downloaded producers above. All three hosted manifests were separately
downloaded through the asset API and are byte-identical to the producers.

Receipt artifact `draft-preview-receipts`, ID `14245289554`, has
GitHub-reported container SHA-256
`0958113b6fe3d8a9a4c24b7307380d996b9ddb63d87b01d3381a14e8064ea1cf`.
All three preflight and three uploaded receipts match source, tag,
workflow run, manifest hashes, selected names/bytes/hashes, release ID
and hosted upload IDs. `CandidateQualified` and `StablePromotionAuthorized`
remain false. The retained six-file raw receipt inventory SHA-256 is
`f55335a5e713f8855940b67a6d56389462e2c780485d743adf270942a6f757c6`.

A full private REST update explicitly retained the existing tag, exact
source, name, draft/prerelease flags and committed UTF-8 notes. A fresh
read confirmed all 24 asset identities unchanged and byte-exact
[committed preview.11 notes](https://github.com/benny-cz/FileCat/blob/9963fe4da0cff988110afe1cb6fe1d590a34c411/docs/release/1.0.0/PREVIEW11_RELEASE_NOTES.md)
at SHA-256 `eda773235e090363dd627d7353682b0083772b6c1d129fd0b0eab077f4fc4c78` (9103 bytes).
The retained final private API snapshot SHA-256 is
`762881f718103bc3824688db3287e4b5fed7ce1c0c5e74291c8726a6e9b4601a`.

## Publication decisions

DEC-09 private review and upload are complete. **DEC-07 remains pending**:
a separate owner decision on this exact hosted unsigned tag/assets/hashes and
committed notes is required before public publication.
The intended public flags are `prerelease: true` and `make_latest: false`.
Stable 1.0.0 remains **NO-GO**.
