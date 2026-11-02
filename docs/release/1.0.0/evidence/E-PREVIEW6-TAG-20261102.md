# Preview.6 tagged source and producer inventory — 2 November 2026

The unsigned `v1.0.0-preview.6` annotated tag object
`8fc7fd205f8352281df8672e99cb829383a7aef3` was independently read back from GitHub and
peels to exact product commit `014a6472eff5dc45e23a8069849102b22291634f`. The prepared
[preview.6 notes](../PREVIEW6_RELEASE_NOTES.md) and refreshed [Steampunk gallery capture](../../../screenshots/CAPTURE-2026-11-02-STEAMPUNK.md)
are on later documentation commit `4a229a1844c04488938839e972df16ddad714f3c` on `main`.
Changes between the tag source and that documentation commit are release, validation and
screenshot records only; no product, package, dependency or test source changed.

[Exact-source main run 41416035969](https://github.com/benny-cz/FileCat/actions/runs/41416035969)
passed the producer and Windows x64, Windows ARM64, Ubuntu and macOS jobs. Its 20 downloaded
TRX files matched the preceding green run 41415495652 by relative file, test name, outcome,
message and multiplicity: **41,421 Passed / 1,572 NotExecuted / 0 Failed**, with no lost pass,
new skip or changed record. The [tagged run 41422916812](https://github.com/benny-cz/FileCat/actions/runs/41422916812)
passed those five source jobs and all three package jobs. Its 20 downloaded TRX files matched
the exact-source main run on the same fields with zero differences.

The three workflow package artifacts are Windows `filecat-release` ID `13133283060`, server
SHA-256 `650c05f8329714fd04386bcbe68428382848eaa1c41f8f1563f4555772006cd8`; Linux
`filecat-linux` ID `13133207828`, server SHA-256
`bc5602e040779d1051a7e1bbe5929488daa9f949de40c347e74cb929dde50092`; and macOS
`filecat-macos` ID `13133297488`, server SHA-256
`7ffc6ba5eddae6253073d42a936d00f1e8b2bb59c02b1d0420a3ba217ba005c9`.
They were downloaded from this tag run. All 24 extracted files were independently read and
hashed; their names, byte sizes and SHA-256 values match the three source/version/platform
producer manifests, which state `CandidateQualified: false` and `SigningVerified: false`.
The manifest SHA-256 values are Windows
`7064b573a265adb0e211fc7543c0ef3acb62223ae8c9f7596180f0c204ad52ce`, Linux
`04b091191208dc0e9b856f3d0045e047f02987a6e8cd4783ba8b7518fa803cdc`, and macOS
`40fe063bd30cda7aad3ac28ef0fb10cbc819a383d8a1399a4c4b7801f31af6b2`.

The official Windows x64 portable ZIP has 447 members. Every member passed a path traversal
screen before extraction into an owned ignored workspace directory. Its `FileCat.portable`
marker exists; `FileCat.exe --version` exited 0 with `FileCat 1.0.0-preview.6` and empty
stderr. This is a noninteractive package start check, not installed candidate qualification.

## Exact hosted inventory

The table below describes the verified workflow package artifacts and the identical 24
uploaded assets in private draft **431344306**. A fresh authenticated GitHub API read found
`draft: true`, `prerelease: true`, no publication time, and 24 unique assets in `uploaded`
state. Every hosted name, byte size and server SHA-256 digest matches the independently
downloaded producer file and its platform manifest. The six downloaded workflow receipts
(three preflight and three upload) agree on the repository, tag, source, manifest hashes,
release ID and all 24 hosted upload IDs, with no extra or missing ID. This metadata check
does not claim every hosted large package was independently downloaded a second time.
The three hosted manifests were separately downloaded and rehashed to the manifest hashes
above. The receipts workflow artifact is ID `13133559282` with GitHub-reported SHA-256
`2958451d9e648b695e1436074b8460abb607e6573dc113a81499bf16076f8f6e`.

| Producer asset | Bytes | SHA-256 |
|---|---:|---|
| FileCat-1.0.0-preview.6-linux-assets.json | 1606 | 04b091191208dc0e9b856f3d0045e047f02987a6e8cd4783ba8b7518fa803cdc |
| FileCat-1.0.0-preview.6-linux-x64-appimage-runtime-source.tar.gz | 4704832 | be7816ce0e129b0417758f942378bed89350e04fb1700c7bd5caa1005d72e587 |
| FileCat-1.0.0-preview.6-linux-x64.tar.gz | 73554720 | 380fc8ea67701b298092a90ea6f2e6e02904b62343ba9d8b0e30059503282919 |
| FileCat-1.0.0-preview.6-macos-assets.json | 690 | 40fe063bd30cda7aad3ac28ef0fb10cbc819a383d8a1399a4c4b7801f31af6b2 |
| FileCat-1.0.0-preview.6-osx-arm64.zip | 71951977 | 4c49472e9a8a4bb1dbe4954bcc99ef323515549eb76211a5c618f4cd3aa9883d |
| FileCat-1.0.0-preview.6-win-arm64-fdd.zip | 66358925 | 0ccf2cb0e26c053a78ab5c604bdff91ebe95f4bb9eb00dec1afaa352fb624186 |
| FileCat-1.0.0-preview.6-win-arm64-portable.zip | 101592610 | ce35b7d131c863911a20b0158f1fa40a4cdd01547a6f0b214f7e9f72dbfec3b3 |
| FileCat-1.0.0-preview.6-win-x64-fdd.zip | 68950601 | 99bdda1555f9916d95ac13611fc9228fdac9a40af83fddd0ae96eebd77aa224d |
| FileCat-1.0.0-preview.6-win-x64-portable.zip | 105831375 | c8714be70dfabc4be3c5db96881fa82c88df02cd8e5388e4b7259435a7c1de9a |
| FileCat-1.0.0-preview.6-windows-assets.json | 2580 | 7064b573a265adb0e211fc7543c0ef3acb62223ae8c9f7596180f0c204ad52ce |
| FileCat-1.0.0-preview.6-x86_64.AppImage | 68045304 | e2581f812eab2380383889f98adcdffc1481775b561736c019859d0af1f94d90 |
| FileCat-1.0.0-win-arm64-setup.exe | 69445189 | a58aa5ace338d7ec415bf8a82190412a05bbfb35ba017722d3917c0434f8dfc4 |
| FileCat-1.0.0-win-x64-setup.exe | 74953263 | 6b47981088a3071c52e8425dd4fbddb2cbbb13a0ceaf9ba00c87087b4e498f9d |
| filecat_1.0.0-preview.6_amd64.deb | 59807432 | 5067ac3f2090dcd4b5f1c04aa4c1c79e7bf7d8952ee09e9a25c0f1276c4886dd |
| sbom-1.0.0-preview.6-linux-x64-appimage.spdx.json | 331959 | 5bfd083a30c808a598eefcacc3b4c258c1089e17a8d5c1068f472f2983618ebc |
| sbom-1.0.0-preview.6-linux-x64-deb.spdx.json | 322435 | a1a2e7f96b28301c595590a5b0618ae2cc513f050163c8d09b3d55d203aea1e9 |
| sbom-1.0.0-preview.6-linux-x64-tar.spdx.json | 310073 | 4f848d232ddb05eb96b55ba2e09a0afd023bffd3cbfae7844cc75be427eb66fe |
| sbom-1.0.0-preview.6-osx-arm64-macos-app.spdx.json | 316541 | a63c2fd2c8c36b7d8e13e3e483254bedcc0e6ca363c5627c47ebc4c56a40371d |
| sbom-1.0.0-preview.6-win-arm64-fdd.spdx.json | 202526 | 7dc5cb6f576cab1bf0dc6b3e7d283bd272c1b4fbce6e435bb0c09eb0578676e3 |
| sbom-1.0.0-preview.6-win-arm64-portable.spdx.json | 326794 | f8598fcf3b259a39c9d6da667183ab3abd1efe624f5a80651813b5add6a66a40 |
| sbom-1.0.0-preview.6-win-arm64-setup-payload.spdx.json | 331425 | 663136620197fadbe7bf9ec6c836c4541514b5ea09dd2687be179a0d03a207cc |
| sbom-1.0.0-preview.6-win-x64-fdd.spdx.json | 202517 | be5749ac167cbc91a5c0d0b1ae250a21a6060c840dae0ddd1e1b3b8ac19ab236 |
| sbom-1.0.0-preview.6-win-x64-portable.spdx.json | 326770 | 87c9b4c20ab7e48431ed2d72d7cf9b9b35e7df8e804279872a16b227b0dfcf61 |
| sbom-1.0.0-preview.6-win-x64-setup-payload.spdx.json | 331401 | 925aef303351a830957a057dcc9165c1c9133cc9a2dd32ee9bfbf86ed4aae2a9 |

## Publication stop

The `benny-cz` maintainer approved the protected `release` environment review for the
**private** upload. The draft body now exactly equals the committed
[preview.6 notes](https://github.com/benny-cz/FileCat/blob/0a206f02e907971d0f2534df3d6f50e80048cadd/docs/release/1.0.0/PREVIEW6_RELEASE_NOTES.md),
SHA-256 `01b1ff2d02e8001b19aa828286834ad7b4741532fc6f24097ac28b138717613c`.
A fresh authenticated prepublication read still found the same annotated tag object and
peeled source, private release ID, exact notes and all 24 hosted assets/receipts unchanged.
The retained API snapshot has SHA-256
`d712467ac6400e06b5e0d6af9846a53550ed662778fe9ee6ebd1f4b1a485f48e`.
DEC-07 requires a separate owner decision on this exact hosted inventory. Publish only as an
unsigned prerelease with `make_latest: false`; verify the public state, exact notes and
release attestation afterward. No stable 1.0.0 candidate, trusted Windows signing or
stable publication is implied by this preview work.
