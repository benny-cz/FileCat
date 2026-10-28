# E-I03-DRAFT-PACKAGES — exact unsigned cross-platform preview set

**Preliminary I03 package-set child accepted at source `8ae4229b9e93ed698a669e730b84ba685380188b`; this is not a signed or frozen candidate, final qualification, publication approval or release GO.**

## Producer and selected bytes

Windows x64/ARM64, Ubuntu x64 and Apple Silicon builds used clean detached worktrees at the same source, locked restore and exact .NET SDK 10.0.101. Each build provenance record says `SourceTreeDirty: false`, `HostedImageIsImmutable: false` and `CandidateQualified: false`. Raw evidence is retained under `V:\FileCat\artifacts\release-evidence\i03-cross-platform-draft-20261028-v4`.

The production manifests select exactly 23 nonempty files: 13 Windows, seven Linux and three macOS. Every payload size and SHA-256 was independently rechecked against its platform manifest. The aggregate record `selected-package-set.json` is SHA-256 `225232db2456e0f8563db9bce0db88c70d4f0095c055d316964aec657c4e9dca`; `selected-package-set-sha256.txt` retains every selected file hash. The three manifest identities are:

| Platform | Manifest SHA-256 | Selected files |
|---|---|---:|
| Windows | `8a29af0079aa8ce3e2b907e0fc7b1548677f6c057d28fe4fc93ef9ee56ee576f` | 13 |
| Linux | `daae228fc36f1119cf02d5b8644f280a1c19bd1e063bdb61d5133156f5f8e5ba` | 7 |
| macOS | `3bd34e22a1c886fd87c1a0d90fcf67e74ad9aa9a74704b03c356166b27aeb64d` | 3 |

The selected set contains both Windows setup executables, four Windows ZIPs, six Windows payload-specific SPDX documents, Linux tar/deb/AppImage plus three payload-specific SPDX documents, and the macOS app ZIP plus its payload-specific SPDX document. It is an unsigned local preview set, not the release candidate.

## Native package checks

- On Windows 11 build 26300 in the disposable VM, the exact x64 portable ZIP (`180f5387…69e45`) was expanded and its hidden `--version` process exited zero with `FileCat 1.0.0-rc.1`; the executable product version binds source `8ae4229b…`. Embedded public notices and the LTRData MIT text hash to `06e63c31…97665` and `7f62225e…58c4`. A first PowerShell GUI-process invocation captured empty stdout and is retained separately as a test-oracle failure.
- On Ubuntu 26.04.1, tar, deb and AppImage version checks exit zero. The deb contains seven required icon sizes. Static extraction of the AppImage finds exactly eleven reviewed AppImage-runtime notice files with the committed hashes, including musl, zlib, zstd and mimalloc full texts. Its public notice and LTRData hashes match the Windows payload.
- On the physical Apple Silicon Mac, the app ZIP's ad-hoc signature verifies, `--version` exits zero with `FileCat 1.0.0-rc.1`, and all seven icon frames are present.

The Ubuntu native archive is 193,694,915 bytes, SHA-256 `213404310bc7c218f7c2e1b7c71db50fa9c519ce809fc4070437da42fc739544`. The Mac native archive is SHA-256 `b359c2b6327c8447c2647663df0d933b4df786101c0404d682f12134b763518a`. A clean Mac notice run at the exact source passes 63/63 controls (seven allowed, 56 refused); its retrieved archive is SHA-256 `43ce05fc0639f2405fc3076ffc5c0ab750a0f5df059c5dce3ac7c0bec1c2c7b8`.

## Release-path refusal

The combined draft-package validator passes all 13 synthetic contract/mutation cases. Authenticated read-only preflight validates local bytes separately for Windows, Linux and macOS, then the aggregate route, and all four attempts stop at the same required boundary:

`Read-only GitHub preflight failed at git/ref/tags/v1.0.0-rc.1; no upload is authorized.`

The prerelease tag does not exist. No tag, GitHub release, asset upload, signing, stable promotion or publication was created or authorized. Temporary Inno 6.7.1 tooling and owned VM extraction stages were removed after their receipts were retained.

## Remaining boundary

This accepts only the deterministic 23-file unsigned preview composition and its bounded native smokes. It does not resolve libfuse LGPL static-link source/relinkability obligations, establish legal eligibility, reproduce the AppImage runtime bit for bit, resolve SharpCompress RAR eligibility (`EXT-02`), freeze the release contract, qualify a signed candidate, seal the REP or authorize stable publication. Those requirements remain in `REL-I03-PROVENANCE`, `EXT-04`, `REL-FQ-CANDIDATE` and `REL-GO-PUBLISH`.
