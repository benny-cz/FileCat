# Preview.5 private draft and hosted inventory — 31 October 2026

The owner conditionally authorized a public preview.5 if a meaningful update existed. I581 makes published preview releases discoverable from the app's explicit update check; I582 makes canonically equivalent Mac filenames match panel filters, Find files and mask selection. These changes are after preview.4 and meet that condition. [The source and immutable-tag record](E-PREVIEW5-PRETAG-CI-20261031.md) identifies exact product commit `8adaf1f17d2120a9c3ceb4c596b6b98354e4d3cc`, annotated tag object `31503f395bf2e3a5bb885ebae9ad621e00c75a8e` and the independently verified peel. No later source change is represented by this tag.

[Tagged run 41229526161](https://github.com/benny-cz/FileCat/actions/runs/41229526161) passed producer, Windows x64, Windows ARM64, Ubuntu, macOS, three package jobs and the owner-reviewed protected private-draft job. Its 20 test-result files exactly matched the source main run: **41,282 Passed / 1,535 NotExecuted / 0 Failed**, with no added, removed, changed or newly skipped predecessor outcome. The owner approved the GitHub `release` environment review for the **private upload**. The downloaded `draft-preview-receipts` workflow artifact is ID `13054854466` with GitHub-reported SHA-256 `69cca448eb8168e4c4be0586c1a757188ad93b108d0069021f093853031c1830`.

## Private draft and independent verification

GitHub release **430143580** has exact tag `v1.0.0-preview.5`, `draft: true`, `prerelease: true`, no publication time and exactly **24 uploaded assets**. The three hosted producer manifests were independently downloaded and rehashed: Windows `9df0a14f4810888aeb9282cc8195accf6936551fea9d41056f376477a714ea04`, Linux `eac464c4cffa1d47a969ccda741877a1574dbf7ac959d7e2c2924af1a2ec6d75`, and macOS `bf175b45babef5e9af71cf90ac53b5e2d0167b83c0f02d5dc0c286e413d65a51`. Each names the same source, version and platform. The live release API has zero extra, missing or duplicate names/IDs and zero size, SHA-256 or upload-state mismatches against those manifests. The six downloaded preflight/upload receipts name the same repository, tag, source, platform and manifest hash, and their upload receipts match all **24 exact hosted IDs**, sizes and server digests. Each receipt explicitly states that stable promotion and candidate qualification are false. This metadata comparison does not mean every large package was downloaded.

The official hosted `FileCat-1.0.0-preview.5-win-x64-portable.zip` was downloaded separately. Its **102,419,012 bytes** and locally computed SHA-256 `a12a740e31622f7e5c6270cbe9833fa1e846179e8c052478439b29a859b6e50b` match its producer manifest and GitHub server digest. Its 366 ZIP members passed a path traversal screen before extraction into an ignored, owned workspace directory. The `FileCat.portable` marker exists, and `FileCat.exe --version` exited zero with `FileCat 1.0.0-preview.5` and no stderr. This is a noninteractive official-package startup check, not installed stable-candidate or GUI qualification.

## Exact hosted inventory

| Hosted asset | Bytes | SHA-256 |
|---|---:|---|
| FileCat-1.0.0-preview.5-linux-assets.json | 1606 | eac464c4cffa1d47a969ccda741877a1574dbf7ac959d7e2c2924af1a2ec6d75 |
| FileCat-1.0.0-preview.5-linux-x64-appimage-runtime-source.tar.gz | 4704832 | be7816ce0e129b0417758f942378bed89350e04fb1700c7bd5caa1005d72e587 |
| FileCat-1.0.0-preview.5-linux-x64.tar.gz | 70366145 | dbb1a5df70d76635bb1c6b7b9efb2f2102d40db6fef9dadedc4001f87def1d55 |
| FileCat-1.0.0-preview.5-macos-assets.json | 690 | bf175b45babef5e9af71cf90ac53b5e2d0167b83c0f02d5dc0c286e413d65a51 |
| FileCat-1.0.0-preview.5-osx-arm64.zip | 68910860 | 55e384f02827d032a161db4f9ce782f1e74582bc6aa7d8f655db9fde8096fc26 |
| FileCat-1.0.0-preview.5-win-arm64-fdd.zip | 63109859 | 7972bad9c844dfa7a428e75596aaf501fcda10c2089d0bbdc2176c60c2483cbc |
| FileCat-1.0.0-preview.5-win-arm64-portable.zip | 98343534 | 5e3595459cd1822d6f7c3eb3725fd1c99bbfcccf8c2d2fb969dd8e6b218ca671 |
| FileCat-1.0.0-preview.5-win-x64-fdd.zip | 65538249 | 434200689cd043a29400489f3c80d65d2ab3b161ee856cb8cc995d759d4e8e86 |
| FileCat-1.0.0-preview.5-win-x64-portable.zip | 102419012 | a12a740e31622f7e5c6270cbe9833fa1e846179e8c052478439b29a859b6e50b |
| FileCat-1.0.0-preview.5-windows-assets.json | 2579 | 9df0a14f4810888aeb9282cc8195accf6936551fea9d41056f376477a714ea04 |
| FileCat-1.0.0-preview.5-x86_64.AppImage | 65190392 | 3fa8f252845da93bd5e46ef35713a4994589d5db4954413b941a4f5b8ba1ed8e |
| FileCat-1.0.0-win-arm64-setup.exe | 67216977 | dd88697aace6cdf294008de3be675414bca003f237cce242a7292b31716f1822 |
| FileCat-1.0.0-win-x64-setup.exe | 72534964 | ef46aef8538ee39ecdd8a944da1259387ef6856f04c5730df9caceba4a03fe86 |
| filecat_1.0.0-preview.5_amd64.deb | 57230244 | 95d2ad10bafdf407defce47a010e78776e7999d488bd6e4cbb86fbe0608bf3ea |
| sbom-1.0.0-preview.5-linux-x64-appimage.spdx.json | 275562 | 0aa36e79e64dc1c3b30462b2cf0ce4b271ce381a7e18e617e8ca402444c4b9f2 |
| sbom-1.0.0-preview.5-linux-x64-deb.spdx.json | 266326 | 22369a9564dc2ddc49405b9f75f406f20d95c17cc31f765314afa80447768725 |
| sbom-1.0.0-preview.5-linux-x64-tar.spdx.json | 254828 | 793264964f362695619f381a5a0b0e2023c1a974129d65fee5bf312fe4636acf |
| sbom-1.0.0-preview.5-osx-arm64-macos-app.spdx.json | 259932 | c2726ded6552b20a8db8242d0541dd0e93f281b7212a584c95b62154bd57d317 |
| sbom-1.0.0-preview.5-win-arm64-fdd.spdx.json | 144318 | f57c1accfebf9d97ceed70bdb4c07d260414420aaa9771456bbd78066d1ca950 |
| sbom-1.0.0-preview.5-win-arm64-portable.spdx.json | 268586 | f3da7f46d1aa0e101b010bc0c49be16c0930c671b1c44eee1c770a90f68806ce |
| sbom-1.0.0-preview.5-win-arm64-setup-payload.spdx.json | 273217 | 66e1cdb55360a60245c54c0fe6f8b2644f327524a97bd5e502c242ef7f9e4a45 |
| sbom-1.0.0-preview.5-win-x64-fdd.spdx.json | 144309 | 2593d87ca17012bab7cbc99694ae33208bfc15681c53756d093fa1ac8ee2d0d8 |
| sbom-1.0.0-preview.5-win-x64-portable.spdx.json | 268562 | f33ba5fd6a407deb050c2a913108235a2c6d2cad596ba24635d2624ed36357c1 |
| sbom-1.0.0-preview.5-win-x64-setup-payload.spdx.json | 273193 | dbf9be3f11a7d6dcbe20bf0342a247f17dda8e27118d71537f141073d5edb08d |

## Scope and stopping rule

These checks qualify the **private unsigned preview.5 draft** for the owner's conditional preview publication decision. Before changing publication state, re-read the live immutable tag, draft ID/tag/body and all 24 hosted names, IDs, byte sizes and server digests against this table and the six receipts. Publish only as a prerelease with `make_latest: false`, then verify the public API state, release attestation and exact notes. A failed or changed comparison stops publication; the tag must never be rewritten. Windows trusted signing, installed final-candidate qualification and stable 1.0.0 publication remain **NO-GO**.
