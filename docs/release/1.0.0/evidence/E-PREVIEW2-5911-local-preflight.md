# Preview.2 exact-source Windows preflight — 29 October 2026

Source: `5911b0da121d18a8953b0ac0906fc7876a913b90` on `main`. This record is a local source preflight, not a hosted tagged asset, candidate qualification, protected draft or public release. The owner approved creating `v1.0.0-preview.2` at this exact commit **only after all required CI lanes pass**. Public publication requires a later exact-asset decision.

The first isolated `eng/publish.ps1 -Version 1.0.0-preview.2 -Runtime win-x64` attempt targeted `E:\FileCat\artifacts\preflight-preview2-5911`. `E:\FileCat\artifacts` is a junction to `V:\FileCat\artifacts`, so `Build-PrivilegedBootstrap.ps1` correctly refused to traverse it. This was an output-location failure after the App self-contained and framework-dependent publishes, before a complete package set; it is not counted as a pass. No bootstrap guard was changed.

The same source then ran the same publisher with `-ArtifactDirectory E:\FileCat\_preflight_preview2_5911`, whose parent `E:\FileCat` and new output path have no reparse-point ancestor. The publisher exited zero and produced two ZIPs and three exact-payload SPDX 2.3 documents. Its notice step copied 55 verified files; its SBOM engine reported the expected restored-package matching. NuGet emitted `NU1900` because the vulnerability service index was temporarily unavailable; this did not stop package generation, and hosted restore/graph controls remain separate. All three SBOMs name the full exact source commit without a dirty suffix.

| Local output | Bytes | SHA-256 |
|---|---:|---|
| `FileCat-1.0.0-preview.2-win-x64-fdd.zip` | 65,447,121 | `d7704efc384fdcea68063267008e8f466fa71740fc5a1d20569d695fe2c0a3d2` |
| `FileCat-1.0.0-preview.2-win-x64-portable.zip` | 102,327,887 | `afdf29b11f3c7cb2bfd9f671687e053a8991f03dbb917d7c97b8ee169665f6bd` |
| `sbom-1.0.0-preview.2-win-x64-fdd.spdx.json` | 144,309 | `9141b89f32e3802d8fdf64f125dcea75d5382a91b36c60586ecd7712ff873931` |
| `sbom-1.0.0-preview.2-win-x64-portable.spdx.json` | 268,562 | `466936282f9a1dc96322279c24bdbf2c3f62a10e533636c60c41f299755f5626` |
| `sbom-1.0.0-preview.2-win-x64-setup-payload.spdx.json` | 273,193 | `e007f56b88448d81f8ce4f7309d6f430eb1cc042bdb364161e0b1df29a2d91fa` |

Both ZIPs were extracted into separate owned folders and each `FileCat.exe --version` returned `FileCat 1.0.0-preview.2` with exit zero. The portable ZIP contains the zero-byte `FileCat.portable` marker; the framework-dependent ZIP has none. This preflight does not cover installer, ARM64, Linux or macOS assets, protected draft upload, downloaded-asset startup, GUI acceptance or final candidate. CI run [40961631336](https://github.com/benny-cz/FileCat/actions/runs/40961631336) was in progress at this observation; do not promote GATE-CI-FINAL or tag solely from this local result.
