# Preview.7 tagged source and release inventory — 3 November 2026

The unsigned `v1.0.0-preview.7` annotated tag object
`0c5eff2729cc3846b73344faf65bfa4d4e65d5e5` was independently read back from GitHub and
peels to exact product commit `8008a3a8523faf830d8ebaa4399de8d2d2563d6a`. The
[preview.7 notes](../PREVIEW7_RELEASE_NOTES.md) were prepared on the later main documentation
commit `f3137f33`; intervening changes after the product source are documentation only.
GitHub's active creation ruleset 26082787 allows the repository-admin role to create these
tags, which explains the administrative-bypass message on push. The separate active
no-update/no-delete ruleset 26082784 has no bypass actors and covers `refs/tags/v1.0.0*`.

[Exact-source main run 41553304616](https://github.com/benny-cz/FileCat/actions/runs/41553304616)
passed the producer and Windows x64, Windows ARM64, Ubuntu and macOS jobs. Its 20 downloaded
TRX files contain **41,461 Passed / 1,572 NotExecuted / 0 Failed**. Compared with the preview.6
tagged source's 20 TRX files, 40 passes were added; four removed passing rows were replaced
by renamed dependent-control passing rows on the four platform lanes. No other prior row was
lost or changed. The later main documentation run 41553919087 also passed all five required
jobs at the same product source.

The [preview.7 tagged run 41598896828](https://github.com/benny-cz/FileCat/actions/runs/41598896828)
targets that exact product commit. Its producer and all four platform source jobs passed.
The tagged run's 20 downloaded TRX files exactly match the 20 exact-source main files by
relative file, test name, outcome, message and multiplicity: **41,461 Passed / 1,572
NotExecuted / 0 Failed**, with no changed row. The three package jobs must complete before
the protected private-draft upload. The platform producer manifests, all package bytes,
hosted inventory and six upload receipts require independent comparison. The package
manifests are expected to state `CandidateQualified: false` and `SigningVerified: false`.
Stable 1.0.0 remains **NO-GO**.


## Exact hosted inventory

The three workflow package artifacts are Windows `filecat-release` ID `13217070190`,
GitHub-reported SHA-256 `84940178ca9286a26e453024a10e7f42cffd5f31a4f2a95bd08d0f1e241a58b3`;
Linux `filecat-linux` ID `13216370142`, SHA-256
`ea1c781cb917e3d8225f8c0b0bc2d3d5f5dd95ab352bd378f37e946b3260a629`; and
macOS `filecat-macos` ID `13215509643`, SHA-256
`1b07617b9b92b633e3b79effdf9cffaf9bac3ce33247eed2fca9b6ec7d120ca9`.
All three package jobs passed on the tagged run. The downloaded archives contain 24 unique
producer files. Every name, byte size and locally computed SHA-256 matches the platform
manifest. The manifests bind the exact tag version and product source, and each states
`CandidateQualified: false` and `SigningVerified: false`. Their SHA-256 values are Windows
`5f375b94e2c9b5064f4f8d5f48860ab7f6aef7ae9d2f819a7fdc91aeefce2b9f`, Linux
`5487efb39bc87b5f47adafe094eb4c7cbadbf18b98d334b6843826c4d9dabb67`, and macOS
`e6da69d7e11330c5a4eb9848f52a430564a1d58fd7b6ea59961d411f59208497`.

The official Windows x64 portable ZIP has 447 members. Every member passed a path-traversal
and symlink screen, the ZIP's CRC check passed, and it was extracted into an owned ignored
workspace directory. Its `FileCat.portable` marker exists; `FileCat.exe --version` exited 0
and printed `FileCat 1.0.0-preview.7`. This is a noninteractive package start check, not
installed stable-candidate qualification.

The owner (`benny-cz`) approved the protected `release` environment review, and the
private draft job passed. A fresh authenticated API read of release **432451806** returned
`draft: true`, `prerelease: true`, no publication time and 24 unique assets in `uploaded`
state. Every hosted name, byte size and server SHA-256 digest matches the independently
downloaded producer file and its platform manifest. The six workflow receipts (three
preflight and three upload) agree on repository, tag, source, manifest hashes, release ID
and all 24 hosted upload IDs, with no extra or missing ID. This metadata check does not
claim that every hosted large package was independently downloaded a second time. The
three hosted manifests were separately downloaded and found byte-identical to the
producer manifests above. The receipts workflow artifact is ID `13216946585`, with
GitHub-reported SHA-256
`4a4664a3843475cbdeb81c746d88ede651011799f648682a93c440a58d87265b`.
The retained private API snapshot SHA-256 is
`8bed620acd97f979436e62f79443c3204fa8fa6aba89b2f39804f23efe9039e2`.

| Hosted asset | Bytes | SHA-256 |
|---|---:|---|
| FileCat-1.0.0-preview.7-linux-assets.json | 1606 | 5487efb39bc87b5f47adafe094eb4c7cbadbf18b98d334b6843826c4d9dabb67 |
| FileCat-1.0.0-preview.7-linux-x64-appimage-runtime-source.tar.gz | 4704832 | be7816ce0e129b0417758f942378bed89350e04fb1700c7bd5caa1005d72e587 |
| FileCat-1.0.0-preview.7-linux-x64.tar.gz | 73574682 | 2a689455861698216a57cd895e0d3a479141de17dda2102131de880d0dfc0d70 |
| FileCat-1.0.0-preview.7-macos-assets.json | 690 | e6da69d7e11330c5a4eb9848f52a430564a1d58fd7b6ea59961d411f59208497 |
| FileCat-1.0.0-preview.7-osx-arm64.zip | 71963258 | 24a4f76a24de0454bbb13b33f69212cdae876038d35b4be4bd46a6d14b95298d |
| FileCat-1.0.0-preview.7-win-arm64-fdd.zip | 66372570 | 4c39e505473b46de07d001993d48993eae2787ada6cd228fe11363a11eaf9dae |
| FileCat-1.0.0-preview.7-win-arm64-portable.zip | 101606255 | 039278e7088f66d102d9de8a4f220a12cc2604400d04733a376a31cf1ec03e11 |
| FileCat-1.0.0-preview.7-win-x64-fdd.zip | 68961677 | fb24e7940c80dc1a306f4f787a126c6ea656627f6cdf060a6ea7cf57ba76ae89 |
| FileCat-1.0.0-preview.7-win-x64-portable.zip | 105842450 | 21d9de695aa3d2543005f344d37c36e278a1063cfa8d76eacdfe1298faa30cad |
| FileCat-1.0.0-preview.7-windows-assets.json | 2580 | 5f375b94e2c9b5064f4f8d5f48860ab7f6aef7ae9d2f819a7fdc91aeefce2b9f |
| FileCat-1.0.0-preview.7-x86_64.AppImage | 68057592 | ad35ad03fbddd75724d3f45071c0648ed4c9e95a76c17445e52d8a57f463a534 |
| FileCat-1.0.0-win-arm64-setup.exe | 69453314 | 910781bff42a3919e6e6bf8fb4ac89dac305692ae323b561e3e5564057abdca2 |
| FileCat-1.0.0-win-x64-setup.exe | 74951292 | fd4a1108c51d57c11eac67446f706820e497c25037387dc26369a9f5be3acdbb |
| filecat_1.0.0-preview.7_amd64.deb | 59824188 | e784f77bdc6342b38481e1d18c9ebd559f26db19e705e3a221b8ce404c87a2b7 |
| sbom-1.0.0-preview.7-linux-x64-appimage.spdx.json | 331959 | 44138f01ef341fb7d7f6dadd06b34e327ae7429a0d36639bf252da58f0f5c862 |
| sbom-1.0.0-preview.7-linux-x64-deb.spdx.json | 322435 | 73a505d6bc517281ed306784a10481e4d05db02346800bef6847dc091c94af51 |
| sbom-1.0.0-preview.7-linux-x64-tar.spdx.json | 310073 | 737cb7291f2348d8c9a4aadc10f6c0829f5a3eac08bf29f9302e5174099850a0 |
| sbom-1.0.0-preview.7-osx-arm64-macos-app.spdx.json | 316541 | f4bfe3b38d27e0cbcd598a9a331b0310d5b5380b304192cf0556f6d4344cdf6e |
| sbom-1.0.0-preview.7-win-arm64-fdd.spdx.json | 202526 | b803c64e73e0a65f7ca2b02cfad8684395a58fd4d1e1e3fe9205f930add23732 |
| sbom-1.0.0-preview.7-win-arm64-portable.spdx.json | 326794 | 761114d0952dead9b12d5f1c6b5722eaf89d5577b5b9cb48f1317d4862cff763 |
| sbom-1.0.0-preview.7-win-arm64-setup-payload.spdx.json | 331425 | ed69a93a24df5beb4aa8f6ddf586fc96e54cb6d34bfe53e2c6bd4afdcb131e57 |
| sbom-1.0.0-preview.7-win-x64-fdd.spdx.json | 202517 | d03f487170c7a797dfc9626f31744e3766f7029ae1fc9a9296298f0343da2f03 |
| sbom-1.0.0-preview.7-win-x64-portable.spdx.json | 326770 | e3e383de29d28e65bb73e249dbfe0805b3052b3320ac447e4aa2d19bb8424473 |
| sbom-1.0.0-preview.7-win-x64-setup-payload.spdx.json | 331401 | 943e7fdda06bce6a73fcf61cb8870eeb795d7251eee5f24b5346284dcb22e2cb |

The private draft body still needs the exact committed preview.7 notes. DEC-07 requires a
separate owner decision on this exact hosted inventory before public unsigned publication.
Stable 1.0.0 remains **NO-GO**.
