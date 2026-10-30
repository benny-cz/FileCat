# Preview.2 CI gate and immutable tag — 29 October 2026

**Tag and private-draft precursor only. No public release or final-candidate acceptance.** The owner explicitly approved creating `v1.0.0-preview.2` at `5911b0da121d18a8953b0ac0906fc7876a913b90` after all required CI lanes passed. [The exact-source Windows local preflight](E-PREVIEW2-5911-local-preflight.md) produced two version-starting ZIPs and three SPDX documents. The earlier `v1.0.0-preview.1` tag remains immutable at `be40902e` and its protected draft failed before upload; it lacks AC-59 and I563.

## Exact-source CI and skip reconciliation

[Main run 40961631336](https://github.com/benny-cz/FileCat/actions/runs/40961631336) completed **success** at full SHA `5911b0da121d18a8953b0ac0906fc7876a913b90`: producer policy, Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26 all passed. The package and draft jobs skipped by design on a `main` push. [Product-source run 40959875460](https://github.com/benny-cz/FileCat/actions/runs/40959875460) also passed all five required jobs at `9a2d45ac`; the intervening commits changed only README and release records.

The four official test-result artifacts of run 40961631336 and the same four of the prior green product run [40936988471](https://github.com/benny-cz/FileCat/actions/runs/40936988471) were downloaded from GitHub and their 20 TRX files per run parsed by artifact, project, test name and outcome multiplicity. Current: **41,116 Passed / 1,531 NotExecuted / 0 Failed**. Prior: **41,102 Passed / 1,527 NotExecuted / 0 Failed**. No prior pass became a skip or failure. The four additional skips are the newly added `LinuxPageEngineTests.WebKitGTK_routes_a_contained_markdown_link_without_replacing_the_current_page`: three non-Linux lanes report “WebKitGTK is Linux's”; hosted Ubuntu reports “No X display.” That exact native Ubuntu WebKitGTK case passed in [I563's native receipt](E-I563-markdown-linked-viewer.md). The other changes are three new contained-link Core passes per platform, a renamed Markdown link expectation passing on all four, and one new Windows PageView pass on each Windows lane. This comparison is a bounded skip reconciliation, not an installed-viewer claim.

The current four official test artifact digests reported by GitHub were `sha256:866e9f82a68297dc89c8554d670977834d92dc6130c047a47c42c4c40523d862` (macOS), `sha256:76241d53bb0b2f6dfc023cf968045e0bce16c2caf244402021906fe1dfdc674d` (Windows ARM64), `sha256:b1e86a3b0e892f9a5b947df939d27fb5e3626dcadadb8ae7907312c05d254275` (Ubuntu) and `sha256:5c08d27ced6edab31e9f37d1493f72dfed99d41bfd5223b28d7e8a134a19af34` (Windows x64). The eight downloaded artifact payloads, comparison script and raw output are retained in the release owner's private store as `V:\FileCat\artifacts\release-evidence\preview2-ci-40961631336-40936988471.zip`: 41,572,468 bytes, SHA-256 `73e8269c3095fc93b1efff44e044ee46f76d03f32053b7aad369ee69c1c96cfa`, 76 entries including 40 TRX. The local extraction was removed after this archive was made. This record does not claim an independent rehash of the server ZIP bytes.

## Tag identity and next gate

Immediately before tagging, local HEAD and remote `main` both resolved to `5911b0da121d18a8953b0ac0906fc7876a913b90`; remote `v1.0.0-preview.2` was absent. An annotated tag was created at that exact commit and pushed. GitHub reported its configured admin bypass of the creation restriction. Independent GitHub API reads resolve `refs/tags/v1.0.0-preview.2` to annotated tag object `5dceff760fade2f64932d73a11e1b7e243b21544`, which peels to that exact commit. [Tag run 40967715439](https://github.com/benny-cz/FileCat/actions/runs/40967715439) started with the same `headSha`.

## Tag run attempt 1: aggregate ARM64 timeout

At 23:26 UTC, tag run 40967715439 attempt 1 concluded **cancelled**. Producer policy, Windows x64, Ubuntu and macOS passed. The Windows ARM64 job started at 22:41:16 and reached its 45-minute `timeout-minutes` limit at 23:26:23. GitHub marks the job **cancelled**, even though every substantive step passed: Core, Platform, Remote, App and Rendering tests; the ARM64 package start and window draw; installer compilation; and artifact uploads. Its last cleanup step completed at 23:26:20, just beyond the aggregate budget. The successful main-run ARM64 lane at the same source took 43 minutes 26 seconds, so this is a runner-duration difference, not evidence of a product failure. The tag run's three package jobs and protected draft skipped because the required ARM64 job did not conclude success. `gh release view v1.0.0-preview.2` reported no release.

An exact-tag **failed-jobs rerun** was requested as attempt 2; the tag object and source commit were not changed. The rerun and package outcomes follow. Do not rewrite the existing tag. No public publication is authorized. Stable 1.0.0 remains NO-GO.

## Tag run attempt 2: source and package gates passed

The ARM64 rerun started at 23:55:13 UTC and completed **success** at 00:38:13 UTC, within its unchanged 45-minute budget. Core, Platform, Remote, App and Rendering tests, package start/window draw, installer compilation, artifact uploads and cleanup all passed. The producer policy, Windows x64, Ubuntu and macOS source jobs were also successful. All three package jobs then completed **success**: Linux at 00:44:03, Windows at 00:46:01 and macOS at 00:57:14 UTC on 30 October. The protected `release` environment waited for owner review; the owner approved it in GitHub, and the private-draft job completed **success** at 01:02:02 UTC. This is the positive I562 private-draft control. REL-PIPELINE remains Open for its separate tag update/delete and `release/1.0` required-check controls.

The owner subsequently reported a Markdown Page Up/Down scrolling defect. Its Windows focus correction is on main at `e2c40436`, after this tag; [I564](E-I564-markdown-page-keyboard-focus.md) records the successful post-fix native source-viewer check and remaining packaged-candidate check. Preview.2 can validate the private release pipeline, but its immutable binaries do not contain I564 and are not a public-preview recommendation. The later source needs its own CI and exact-tag decision.

## Private draft: independently checked hosted inventory

GitHub release ID **428547179** is `draft: true`, `prerelease: true`, tag `v1.0.0-preview.2`; no public release was published. The workflow's [draft receipt artifact](https://github.com/benny-cz/FileCat/actions/runs/40967715439/artifacts/12945652187) (server artifact digest `sha256:65f717caea18492caacba19c1817f142b9be42678ecb31ed2140fccd72c7a45d`) contains six preflight/upload receipts, tied to exact source, ref, release ID and 24 selected assets. Its post-upload checks reported names, IDs, byte sizes and server SHA-256 digests verified for every platform.

Separately, the three small **hosted** producer manifests were downloaded through the release-asset API, hashed locally and parsed. Their SHA-256 digests are `2b4a2646a875a55eb79eb1bfd35c85e1ae7153b1d4c230e4ff221b33154de315` (Windows, 13 assets including manifest), `4d563358e4db1262033117c0eab0e6765536a9383370961e5d84a8408fd61067` (Linux, eight), and `473e3d3d92ccca7230f7d7a68cf7dcb46186e069cb027519a1af07206deecd59` (macOS, three). Each parsed file's name, byte size and SHA-256 was compared to the live GitHub release list, with exactly 24 unique uploaded names. The locally rehashed manifest digests also matched the server's own digests and the producer/receipt values. This checks hosted metadata and the manifest bytes; it does not independently download and hash all large binaries.

| Hosted asset | Bytes | SHA-256 |
|---|---:|---|
| `filecat_1.0.0-preview.2_amd64.deb` | 57145530 | `e4573c847a6cca1f052101503eba7e83c5d24be1249b1d04f42a120d56efc833` |
| `FileCat-1.0.0-preview.2-linux-assets.json` | 1606 | `4d563358e4db1262033117c0eab0e6765536a9383370961e5d84a8408fd61067` |
| `FileCat-1.0.0-preview.2-linux-x64-appimage-runtime-source.tar.gz` | 4704832 | `be7816ce0e129b0417758f942378bed89350e04fb1700c7bd5caa1005d72e587` |
| `FileCat-1.0.0-preview.2-linux-x64.tar.gz` | 70271445 | `168093f872ccd4f17a6f716ec6d2ebf2bce32993c0a91b7a32ea721f81254c0a` |
| `FileCat-1.0.0-preview.2-macos-assets.json` | 690 | `473e3d3d92ccca7230f7d7a68cf7dcb46186e069cb027519a1af07206deecd59` |
| `FileCat-1.0.0-preview.2-osx-arm64.zip` | 68818646 | `c568de6f737a1783e174c21a1f50fd282099998e1b275d30fb37619bc40bb7d9` |
| `FileCat-1.0.0-preview.2-win-arm64-fdd.zip` | 63010655 | `16c05749da3de9810f7f1dd405f65cd502ad7f783394907ee0322c5b97409084` |
| `FileCat-1.0.0-preview.2-win-arm64-portable.zip` | 98244333 | `530ef8522c2c8ded9a8bb53d08d9a26807301d2c935ef04d2bad0d64259a991a` |
| `FileCat-1.0.0-preview.2-win-x64-fdd.zip` | 65447192 | `14b719e102349e15891e9f4669fd4d9a8293a80afe01a88363e67e8ad40e0406` |
| `FileCat-1.0.0-preview.2-win-x64-portable.zip` | 102327958 | `218b2c52dbb485150247f0051430723aaf836e2f13bb28479ea296c6c35f6919` |
| `FileCat-1.0.0-preview.2-windows-assets.json` | 2579 | `2b4a2646a875a55eb79eb1bfd35c85e1ae7153b1d4c230e4ff221b33154de315` |
| `FileCat-1.0.0-preview.2-x86_64.AppImage` | 65104376 | `6da0181e84437ee5d9905f9078c515c96be1ffa4cd1b0f70229faa6c02e2f75e` |
| `FileCat-1.0.0-win-arm64-setup.exe` | 67147192 | `1efc9ca45832eb5b35b0e81088bdeff8c9447603109a19ea4530a9228f6c91a0` |
| `FileCat-1.0.0-win-x64-setup.exe` | 72467550 | `c39cabad60135eb1fea952adcedbbc473ed0f6f55d92aaecd5ac20bf86ff2b92` |
| `sbom-1.0.0-preview.2-linux-x64-appimage.spdx.json` | 275562 | `0a0287882c2069e1a0c7b886a3c5e276f55b62faa3ae2f7f43e551272a8d0971` |
| `sbom-1.0.0-preview.2-linux-x64-deb.spdx.json` | 266326 | `c8bb090b9c47368e24752adc941aaa88f5f3a54ebf9cec661f7695327faf5a82` |
| `sbom-1.0.0-preview.2-linux-x64-tar.spdx.json` | 254828 | `d748ce8b96f01d908581d5c41dd9ab1052e697d495579ed473d0d8e4aef380ea` |
| `sbom-1.0.0-preview.2-osx-arm64-macos-app.spdx.json` | 259932 | `cb83a27c0249c2d68f9147098e2118c7d9bd38a2b5f0fdf078f71ab70259489a` |
| `sbom-1.0.0-preview.2-win-arm64-fdd.spdx.json` | 144318 | `5d7b4a56b6d9913097e18337a00dee4f1f5f1838caa0f7598c7c2008bc0839db` |
| `sbom-1.0.0-preview.2-win-arm64-portable.spdx.json` | 268586 | `ea5334c5b604d563f40959da8f2f7a461a4861e8ded4126b7ca7f80d047d9f19` |
| `sbom-1.0.0-preview.2-win-arm64-setup-payload.spdx.json` | 273217 | `22ef3bba0093998ad87680f811b8aebe5deb682d0835a747d16149b0cdf98aa4` |
| `sbom-1.0.0-preview.2-win-x64-fdd.spdx.json` | 144309 | `1c12eeb845b08772b1a269cc12d87490be0c584657dd784601e88cd141680936` |
| `sbom-1.0.0-preview.2-win-x64-portable.spdx.json` | 268562 | `421295e3362429de6d968d36b2d929ab63a43b9dba67f2b1333f7e9e6643bfca` |
| `sbom-1.0.0-preview.2-win-x64-setup-payload.spdx.json` | 273193 | `e6bba5fed1cd5a4834774d8ba2597556b739a30449cb553d20c7616d3464fe5c` |
