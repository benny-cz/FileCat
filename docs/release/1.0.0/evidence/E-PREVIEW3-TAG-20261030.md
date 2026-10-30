# Preview.3 immutable tag and private-draft qualification — 30 October 2026

The owner explicitly approved annotated `v1.0.0-preview.3` at full CI-green commit `8320e18a5bd418011a2706c18ac4bebb2e0e33c4` after [all five required main jobs and skip reconciliation passed](E-PREVIEW3-PRETAG-CI-20261030.md). Immediately before tagging, remote `main` was `1245cd0441f5886d3abe7cc5fb9ce834c8161427`, whose changes from the approved commit were only six release-record files, and remote/local `v1.0.0-preview.3` was absent. The approved commit was verified as a commit and ancestor of main.

An annotated tag was created and pushed once. The remote ruleset reported an admin bypass of its tag-creation restriction. GitHub's ref API identifies tag object `17491a88630d455fcee5506ebe3f0ec772c901b0`; its independent tag-object API response peels to the approved full commit, with message `FileCat v1.0.0-preview.3 private draft candidate`. [Tag run 41033296020](https://github.com/benny-cz/FileCat/actions/runs/41033296020) started with the same `headSha`.

The tag and source are immutable. [Tag run 41033296020](https://github.com/benny-cz/FileCat/actions/runs/41033296020) then passed producer policy, Windows x64, Windows ARM64, Ubuntu, macOS and all three package jobs. The private-draft job waited on the GitHub `release` environment; pending-deployments API identified `benny-cz` as the required reviewer. The owner approved that deployment in GitHub, and the draft job completed **success** at 10:41:10 UTC. No public release was authorized by either approval.

## Private draft and independent hosted verification

GitHub release **428910888** remains `draft: true`, `prerelease: true`, tag `v1.0.0-preview.3`, with no publication time. It has exactly **24 uploaded assets**: 13 Windows, eight Linux and three macOS. The workflow's `draft-preview-receipts` artifact ID **12971390442** has server artifact digest `sha256:af07d5a3bd257748c95bb422a8b0d11df35755f4e666a93cadeb3a4515ce21a5`. Its six preflight/upload receipts bind the exact repository, tag, source commit, run, platform, release ID and selected asset IDs; all three upload receipts report names, IDs, sizes and server digests verified. They do not authorize stable promotion or claim candidate qualification.

Separately, the three **hosted** producer manifests were downloaded through the draft-release interface and locally hashed. Their SHA-256 values are `59ce9797f50acedd59e2425b5518efa965770a6cc99d8edea14eae09a7dde258` (Windows), `9eb947825636b0e6c144bae783d4fd51b3e8263b989eea8f57ef71a954293461` (Linux), and `add1ae4869937d2a802a2fecff6e40ddfb0cac2ed3cbc90f9a293870962c7082` (macOS). Each manifest names the approved source and preview.3 version. An independent comparison matched every selected name, byte size and SHA-256 to the live GitHub release list, with no duplicate, missing or extra asset. It also matched each upload receipt's exact asset IDs and selected records. A second live release API read after the VM test found the same 24 IDs, names, sizes, digests and uploaded states; the release was still private. This compares hosted metadata and locally rehashed manifest bytes; the large binaries were not all independently downloaded.

## Official Windows x64 package input

The **official hosted** `FileCat-1.0.0-preview.3-win-x64-portable.zip` was downloaded and independently hashed at **102,331,983 bytes**, SHA-256 `cf39209edb5142be8188e65f9248061b70c8dff0563c8e894c1730d580436ab8`, matching both the hosted manifest and GitHub server digest. After transfer into the disposable Win11 VMware guest, the guest rehashed the same digest, extracted the ZIP and obtained `FileCat 1.0.0-preview.3` with exit zero. The first UI attempt stopped at a Windows lock screen with `SendKeys` access denied; this was a guest session/setup condition, not a product result. The guest was restored to its previously verified interactive snapshot `updated #38`, and the official ZIP was retransferred and checked again.

The actual Page viewer then opened `paging.md` (9,061 bytes, SHA-256 `b819dd13d048861cacf0a0e806a6c7efe1afc23e002a44d507763dc935491db2`) at its heading. A Page Down key after viewer activation moved the first visible section to **Section 5**; a Page Up key after another activation returned to the heading. Foreground-window and focus receipts identify `paging.md — FileCat Viewer`; the two key stages had Win32 focus on Avalonia's `ViewerWindow` root, which is the consequential I564 path. The three unedited screenshots have SHA-256 values `6c1bd5d1a3fd47a60b6712372eeba1ddc24234f924b0ad46acbe9ee8da141190` (open), `6ce56ffbcb0a207e454ed88441f5b1b5ca1b5f66011a93d07884ba017bcee2aa` (down), and `b1ad56ba18fd18847f158f807395c15b75d54119226c0095996e371f768bbab4` (up). The test app was closed. This extends I564's Windows package-input check from a local build to an official hosted portable ZIP; it is not an installed-candidate qualification.

The downloaded manifests, API snapshots, six workflow receipts, exact official ZIP, guest scripts, fixture, receipts, comparison script/output and screenshots are retained in the owner's private store as `V:\FileCat\artifacts\release-evidence\preview3-draft-428910888-verification.zip`: **115,676,027 bytes**, SHA-256 `fcdecb57d0ee6e247f190b2f33229785442fc2716d322c1049e445d30baf3147`. The archive was rehashed after copying and contains 30 entries, including all three manifests, the ZIP, three UI screenshots and the verification output.

## Private draft release notes

[The prepared notes](../PREVIEW3_RELEASE_NOTES.md) describe the preview packages, platform limits, signing and recovery cautions, and three hosted manifest hashes. They were uploaded as the private draft's body. An independent release API read confirmed the hosted body exactly matches the prepared Markdown, with `draft: true`, `prerelease: true`, no publication time and all 24 assets retained. The post-edit 46,947-byte API snapshot is preserved at `V:\FileCat\artifacts\release-evidence\preview3-draft-428910888-release-after-notes.json`, SHA-256 `ff7af7475e49f1096306435aa91947834a475011371e85a3073b38bdc5544453`. The local notes file hashes to `182850bba0b48aaa7c7c70161ac73ad7d63d6ca032f7f4bf36b4a935863ad2d6`. Editing this private body did not publish the release.

## Exact hosted inventory for the separate public-preview decision

| Hosted asset | Bytes | SHA-256 |
|---|---:|---|
| FileCat-1.0.0-preview.3-linux-assets.json | 1606 | 9eb947825636b0e6c144bae783d4fd51b3e8263b989eea8f57ef71a954293461 |
| FileCat-1.0.0-preview.3-linux-x64-appimage-runtime-source.tar.gz | 4704832 | be7816ce0e129b0417758f942378bed89350e04fb1700c7bd5caa1005d72e587 |
| FileCat-1.0.0-preview.3-linux-x64.tar.gz | 70276289 | d5840dfde13b499d252ddaa87e46b6743a8b8e76e2d73e3e322dfb0f9f8db7fd |
| FileCat-1.0.0-preview.3-macos-assets.json | 690 | add1ae4869937d2a802a2fecff6e40ddfb0cac2ed3cbc90f9a293870962c7082 |
| FileCat-1.0.0-preview.3-osx-arm64.zip | 68821380 | 269329c2f9ce10dc9eba44d5120e6d64cf8e55385a470d9b0e362efa43caa0ca |
| FileCat-1.0.0-preview.3-win-arm64-fdd.zip | 63015848 | f70dda2dbdac0d343426c62efa849c6d11d7b69a3859438db63c6737413363f1 |
| FileCat-1.0.0-preview.3-win-arm64-portable.zip | 98249523 | 19358df8a8e90c900c3ccf6ad8bfc3e1a972969824a94c1a48989bb5ce112142 |
| FileCat-1.0.0-preview.3-win-x64-fdd.zip | 65451220 | 9d08e57839b8b455edd61774c7bb509abcc79f115914d7a8913527d06ebfc3db |
| FileCat-1.0.0-preview.3-win-x64-portable.zip | 102331983 | cf39209edb5142be8188e65f9248061b70c8dff0563c8e894c1730d580436ab8 |
| FileCat-1.0.0-preview.3-windows-assets.json | 2579 | 59ce9797f50acedd59e2425b5518efa965770a6cc99d8edea14eae09a7dde258 |
| FileCat-1.0.0-preview.3-x86_64.AppImage | 65120760 | 0f798f65f7792578e2931dfa14702ee7e37ecc5eedbd6b6c8c37143a5795402f |
| FileCat-1.0.0-win-arm64-setup.exe | 67150477 | 8c3f82267723ddfd3dece3a4521fca8ff6e6be85ba2c64e2591ef0c18a10c47d |
| FileCat-1.0.0-win-x64-setup.exe | 72471786 | 20a836b5614461b33aa6b91871799100b7fb14dc3245c18afcda22a4edbbaef7 |
| filecat_1.0.0-preview.3_amd64.deb | 57158138 | b4d6d692868ca86502cf7f25866d64a7fd4c2112ad540ce90d4e849bd72b90c0 |
| sbom-1.0.0-preview.3-linux-x64-appimage.spdx.json | 275562 | 2c18add923909e3ad9c9c1d28f79fea6c50e7c15193952a39fc12d33331aaf96 |
| sbom-1.0.0-preview.3-linux-x64-deb.spdx.json | 266326 | 5f41316f17b6ca2945293d9351f8e0369f1fff408c6ef52588adcde05b10071f |
| sbom-1.0.0-preview.3-linux-x64-tar.spdx.json | 254828 | 364dc6625d0b2d6e34ca91d23d297c63767c615721719a74da7373b546bc4f5a |
| sbom-1.0.0-preview.3-osx-arm64-macos-app.spdx.json | 259932 | 9d8f35796ede827025e0e7064312c692ddb63ca87bb2695225b42f5f3dce1058 |
| sbom-1.0.0-preview.3-win-arm64-fdd.spdx.json | 144318 | 5168175d649ad1446e1a91ecdd269e19b8b15f9730ed98859c1f6218178ac9a1 |
| sbom-1.0.0-preview.3-win-arm64-portable.spdx.json | 268586 | 980215cf48e9907656293a9640823f4d3fc0d859f324df2b0927ccf3eaa48f6c |
| sbom-1.0.0-preview.3-win-arm64-setup-payload.spdx.json | 273217 | d5fa2f150b7527566519c0868edf397b208b1e15d45b7ac040b3450baf2d1c31 |
| sbom-1.0.0-preview.3-win-x64-fdd.spdx.json | 144309 | e5a548273ea9078a7e5899eb57a8da01c7be45e2ae89dd13d8643b1a06ebb53a |
| sbom-1.0.0-preview.3-win-x64-portable.spdx.json | 268562 | 1754d711ffd3a0c4a957bd7d0c81dafb79f81945b808424d78e27e4337bea109 |
| sbom-1.0.0-preview.3-win-x64-setup-payload.spdx.json | 273193 | 69cecb56a5dd087bd143666a6cae365ae7252f3e293413331038e697e69e1556 |

The private draft and official Windows portable input check passed at their exact scope. Read-only GitHub ruleset inspection found active tag ruleset `26082784` covering `refs/tags/v1.0.0*` with update and deletion rules and no bypass actors; creation ruleset `26082787` restricts creation with an admin bypass. The approved tag push reported that creation bypass. No attempt was made to rewrite or delete an immutable release tag merely to test the negative rule, and no `release/1.0` required-check branch exists yet. REL-PIPELINE remains Open for those negative/branch controls. Candidate installation, signatures, consent and stable GO retain their own gates.

The owner was offered the exact 24 hosted assets and hashes above, the prepared release notes, and the remaining pipeline limits for the separate DEC-07 public-preview decision. The owner chose **Keep the draft private for now**. A subsequent GitHub API read confirms release 428910888 still has `draft: true`, `prerelease: true`, no publication time and 24 assets. No public preview is authorized; a later public decision must explicitly supersede this choice. Stable 1.0.0 remains NO-GO.
