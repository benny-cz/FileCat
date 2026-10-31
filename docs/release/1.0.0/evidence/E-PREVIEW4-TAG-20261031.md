# Preview.4 tag, private draft and hosted inventory — 31 October 2026

The owner approved annotated immutable `v1.0.0-preview.4` only at `c08f8c52bfdcade91ee6ad438ac89ce8d565f5b0` after the exact-source main CI and skip comparison passed. The tag's GitHub object `028fe871e477a2170422f1de9b2a5e04d14be711` independently peels to that commit. [Pretag evidence](E-PREVIEW4-PRETAG-CI-20261031.md) records the absent-tag and remote-main checks, 41,246 passed/1,535 explicitly not executed/zero failed tests, and no lost pass or new skip.

[Tag run 41198371834](https://github.com/benny-cz/FileCat/actions/runs/41198371834) at that exact source passed producer policy, Windows x64, Windows ARM64, Ubuntu, macOS, all three package jobs and the owner-reviewed protected private-draft job. Its 20 TRX files matched the main-source run's outcomes exactly. The owner approved the `release` environment deployment in GitHub for **private upload only**. The `draft-preview-receipts` workflow artifact ID `13042993776` has GitHub digest `sha256:dfd1749e801eac415360de2cef35afc2bd12f2978fb7da0b9149eb5208fb24be`.

## Private draft and independent verification

GitHub release **429934195** has exact tag `v1.0.0-preview.4`, `draft: true`, `prerelease: true`, no publication time and exactly **24 uploaded assets**. Its three hosted producer manifests were downloaded and independently SHA-256 rehashed: Windows `5eda29c52938872a1e8b488dde09a8555a0a31afcea28b75d18d5c9849c2470e`, Linux `f7650a60fd41747f65a742c589216c95a08721512adab2752a1aa0ee770685f9`, and macOS `7f9dcf1c67256924c1fc45275ca422b7a7166deeb4e95e6e84c8c0cdb619eae7`. Each manifest names the exact approved source and preview version. Comparing the live release API with those manifests found zero missing, extra or duplicate names or IDs and zero byte-size, SHA-256 or upload-state mismatches. All six independently downloaded preflight/upload receipts name the same repository, tag, source, platform and manifest hash. Their three upload receipts match all 24 exact asset IDs, names, sizes and server digests and explicitly report stable promotion and candidate qualification as false. The hosted metadata comparison does not mean all large binaries were downloaded.

The official hosted `FileCat-1.0.0-preview.4-win-x64-portable.zip` was downloaded separately: **102,415,723 bytes**, SHA-256 `fe5b1d0df3c1059098409ada807819733788bf622664675f82890943fde6e373`, matching its producer manifest and GitHub server digest. It was extracted into an ignored owned workspace directory on Windows. Its `FileCat.portable` marker was present; `FileCat.exe --version` exited zero and returned `FileCat 1.0.0-preview.4` with no stderr. This is a noninteractive official-package startup check, not a Windows installed-candidate or GUI acceptance test.

The [prepared release notes](../PREVIEW4_RELEASE_NOTES.md) now include the verified manifest hashes and source commit. They were uploaded to the **private** draft with draft, prerelease and exact tag explicitly retained. A fresh release API read found the body byte-for-byte equal to the local notes file, `draft: true`, `prerelease: true`, `published_at: null`, the exact tag and all 24 unchanged assets. The notes file SHA-256 is `0078cbe55039ec7880087d661177eb2698915a075840c8df0f61821d4c2ee2df`; the local API snapshot SHA-256 is `c2bde4514a4e5675deab993aa47242bf27d2a9438fa2d9d35d99e9c06fc4ea94`. The downloaded private receipts, API snapshots, verification scripts and package bytes are retained in the ignored workspace `.tmp/preview4-ci`; release assets and tag remain unchanged.

## Exact hosted inventory for the public-preview decision

| Hosted asset | Bytes | SHA-256 |
|---|---:|---|
| FileCat-1.0.0-preview.4-linux-assets.json | 1606 | f7650a60fd41747f65a742c589216c95a08721512adab2752a1aa0ee770685f9 |
| FileCat-1.0.0-preview.4-linux-x64-appimage-runtime-source.tar.gz | 4704832 | be7816ce0e129b0417758f942378bed89350e04fb1700c7bd5caa1005d72e587 |
| FileCat-1.0.0-preview.4-linux-x64.tar.gz | 70363231 | 0a42c6e841765f17f2455fbd261574ff442424b2ad2827e24c53f73e8a7e7a5f |
| FileCat-1.0.0-preview.4-macos-assets.json | 690 | 7f9dcf1c67256924c1fc45275ca422b7a7166deeb4e95e6e84c8c0cdb619eae7 |
| FileCat-1.0.0-preview.4-osx-arm64.zip | 68907119 | 2c5202cf96e2a1688c83abc4434691e49ea241fac0241decff5cf96d7e222736 |
| FileCat-1.0.0-preview.4-win-arm64-fdd.zip | 63104830 | a628b51e14d760562facf0415c398bb5c99b1e61ad0b67f7a56a1ebc395dc743 |
| FileCat-1.0.0-preview.4-win-arm64-portable.zip | 98338503 | acbc556b063a32efbe8406958ff51eb4632577f654885fe5f52ef3d071071209 |
| FileCat-1.0.0-preview.4-win-x64-fdd.zip | 65534961 | 4bc4707fd91530cd8d2e285076b90105d8905517a9dac5e9a589feac3d49ce67 |
| FileCat-1.0.0-preview.4-win-x64-portable.zip | 102415723 | fe5b1d0df3c1059098409ada807819733788bf622664675f82890943fde6e373 |
| FileCat-1.0.0-preview.4-windows-assets.json | 2579 | 5eda29c52938872a1e8b488dde09a8555a0a31afcea28b75d18d5c9849c2470e |
| FileCat-1.0.0-preview.4-x86_64.AppImage | 65190392 | dc71d50e0b1cce1d73f22dd3648a32982d0a624531f76e319759577b42306bad |
| FileCat-1.0.0-win-arm64-setup.exe | 67216008 | a1696c200e25cfe73f3b46fa5b42d6713088f7b966c5fecca5dd14a915a52b3c |
| FileCat-1.0.0-win-x64-setup.exe | 72539448 | 9bd18158f5d4753e5e78045c02039d9104c94d452c75a46cd9a72e0372c23e74 |
| filecat_1.0.0-preview.4_amd64.deb | 57235430 | f426f6353af3fe7f50fdf73e1c3914d6d9c2aa53a4f172cbe156437c0976c028 |
| sbom-1.0.0-preview.4-linux-x64-appimage.spdx.json | 275562 | bfb4101da3180684ad0d67074a13d9987e3c33fe3590ac39dbf12d725288094e |
| sbom-1.0.0-preview.4-linux-x64-deb.spdx.json | 266326 | 3b4d81d3674ea81e4270fdf9598d9c2509544f5362c3d897b5aff13bb6561bb1 |
| sbom-1.0.0-preview.4-linux-x64-tar.spdx.json | 254828 | 3cb265ff24da2b96d5010ea4aad1a712e3183bebcd64c2957cce15294ff50586 |
| sbom-1.0.0-preview.4-osx-arm64-macos-app.spdx.json | 259932 | 1daa22f184e5e1ab96e1e80630445ba33fe64a4c89a867fce3ad2d1d37394761 |
| sbom-1.0.0-preview.4-win-arm64-fdd.spdx.json | 144318 | 1e5f72c341e51ac28c0729c6096c7b303d9c72d969a2090fa4041c40b39d20c5 |
| sbom-1.0.0-preview.4-win-arm64-portable.spdx.json | 268586 | af345da1e83ac72f2d7222996a5cae991d798bf67c9da0335fbc8f887b7271fb |
| sbom-1.0.0-preview.4-win-arm64-setup-payload.spdx.json | 273217 | f4fcc67978f19b0ad277b34e76e7ecf2ffc8c749e244cbd5b8a25e9ea87e23cd |
| sbom-1.0.0-preview.4-win-x64-fdd.spdx.json | 144309 | 8fe2dd6ebc2157661a2c6aae1429f5c270c5dc64b26c4c91ebbfe8d0738cc84a |
| sbom-1.0.0-preview.4-win-x64-portable.spdx.json | 268562 | 6904e309ceda376407452c4331e9fc39d4c033cd074fa7033c3a91874c99b6f5 |
| sbom-1.0.0-preview.4-win-x64-setup-payload.spdx.json | 273193 | 7adf45460cf7ca237301e9193fb3a81d8132e9a075c0201a15563c30be195898 |

## Disposition and stopping rule

The private draft and exact hosted inventory pass their stated preview scope. No signed or installed stable candidate exists, and stable 1.0.0 remains **NO-GO**. Public preview.4 publication requires a separate owner decision on **release 429934195**, this exact 24-asset inventory and the prepared notes. Until then, keep `draft: true`; do not rewrite the immutable tag or substitute preview.3 assets. On explicit publication approval, re-read the live tag object, draft/body and every hosted name, size and server SHA-256 against this table, publish only as an unsigned prerelease, then verify public API state and update README/download records. If approval is withheld, stop with the private draft intact.

## Public preview publication — 31 October 2026

The owner explicitly approved publishing **release 429934195** as an unsigned `v1.0.0-preview.4` prerelease with the exact 24 hosted assets and hashes in the table above and the prepared notes. This is a separate decision from the immutable tag and protected private-upload approvals; it does not approve stable 1.0.0.

Immediately before publication, a live tag-ref read still identified annotated object `028fe871e477a2170422f1de9b2a5e04d14be711`, which peeled to the approved `c08f8c52bfdcade91ee6ad438ac89ce8d565f5b0`. A fresh private draft read confirmed the exact release ID/tag, `draft: true`, `prerelease: true`, no publication time, all 24 uploaded names/IDs/byte sizes/server SHA-256 digests against the three independently rehashed producer manifests and six receipts, and body equality with the prepared notes. The publication request explicitly retained the tag and prerelease state, set `draft: false` and `make_latest: false`; GitHub returned publication at **2026-10-31T13:00:42Z** and the [public preview.4 URL](https://github.com/benny-cz/FileCat/releases/tag/v1.0.0-preview.4).

An **unauthenticated** GitHub release-by-tag API read then found release 429934195, `draft: false`, `prerelease: true`, that publication time, the exact notes body, and all 24 unique uploaded asset IDs/names/byte sizes/server SHA-256 digests unchanged from the approved inventory. The locally retained public API snapshot SHA-256 is `2da896c9c4dbc1dcc0d2f2d26cc124f0c9670117df80601c5a81112bc6b37389`. No package, manifest or tag was changed in publication. The preview is public; signed Windows packages, installed final-candidate qualification and stable 1.0.0 remain separate and **NO-GO**.
