# E-I03-SBOM — exact payload SBOMs and macOS release controls

**Status:** preliminary component acceptance for `I03-SBOM-ENGINE`; not candidate qualification and not release GO.

## Source and observed failure

- `c7ed95409cb61290750e830a1c956c328016087e` introduces one SPDX 2.3 document per distinct Windows, Linux and macOS payload. The generator records every payload file and checksum, binds the reviewed dependency snapshot and restored package archives, and emits SBOM filenames in the release allowlist.
- GitHub run [40717843968](https://github.com/benny-cz/FileCat/actions/runs/40717843968), macOS 26 ARM64 job `122920423177`, passed checkout, exact .NET 10.0.101 restore/build and Core tests, then failed the release-asset controls before portable tests.
- Exact PowerShell 7.6.7 on the authorized ARM64 Mac reproduces exit 1 at `Select-ReleaseAssets.ps1`: the synthetic fixture used `[IO.Path]::GetTempPath()` under `/var/folders`, while `/var` is a macOS system symlink and the production selector correctly refuses paths traversing links.

## Correction and bounded controls

`49ecb362635aecabe7966f1567ca4d65951aff23` keeps the production link refusal unchanged. The three synthetic release validators now use `RUNNER_TEMP` when CI supplies it and an ordinary user's non-linked home on macOS otherwise. The same commit also corrects a discovered SBOM provenance assumption: build-generated reference projections such as WebView2 are attributed to their reviewed parent package while their exact published bytes remain file records; the generator does not claim false byte identity with a package member. Every restored NuGet archive is still verified against its reviewed archive SHA-256 and its own SHA-512 receipt, and runtime-pack metadata remains separately bound.

| Environment | Result | Scope |
|---|---|---|
| Physical ARM64 Mac, macOS 27.0.1, PowerShell 7.6.7 | Three release suites pass with CI-style `RUNNER_TEMP`; the first suite also passes with `RUNNER_TEMP` absent. Exact SBOM controls pass 18/18 (six allowed, twelve refused). | Reproduces and corrects the macOS fixture failure; no hosted pass inferred. |
| Windows x64 host | Three release suites and dependency-notice controls pass. Exact SBOM controls pass 17/17 (five allowed, twelve refused; symbolic-link positive is Unix-only). | Cross-platform regression control; no host UI used. |
| Ubuntu 26.04 VMware guest | Exact SBOM controls pass 18/18. A real Linux packaging observation writes tar and deb SBOMs and builds the deb before AppImage acquisition refuses changed runtime bytes. | Generator semantics pass; the package command is not promoted to a complete package-suite pass. |

The refusal suite includes unknown and unused packages, unknown/hashed references, a changed parent package archive, a changed runtime archive, a wrong runtime hash, missing/forbidden runtime placement, output aliasing, duplicate logical paths, an existing output and (on Unix) a symbolic-link payload. The generated-reference positive changes the projection bytes and verifies that the SBOM records the changed exact SHA-256 without weakening parent-package provenance.

## Newly exposed AppImage pin gap

The current script expects AppImage runtime SHA-256 `156f4bdbde9c52d01814600013e0a273f0118dc2de98975f3c8c63427ec79074`, but the mutable upstream `continuous` URL returned `617aa77314d57c8b8e4fae9c3f0b35af7b30d5529cb69e98f64f92510ed98a46` on 2026-10-28. The package script correctly exits 2 before AppImage creation. `I03-APPIMAGE-RUNTIME-PIN` therefore remains Open: select and review an immutable runtime source (or a reviewed new runtime), retain its source/licence/provenance, and pass the real AppImage plus SBOM and tamper-refusal controls. The checksum must not be updated merely to accept mutable bytes.

## Retained raw evidence

Raw records are under `E:\FileCat\artifacts\release-evidence\ci-c7ed-macos-controls-20261028-v1` and `E:\FileCat\artifacts\release-evidence\i03-macos-ci-sbom-controls-20261028-v3`. Selected SHA-256 values:

| Record | SHA-256 |
|---|---|
| Hosted Mac build provenance | `6cdea7561a3934b1804d356cd96e4fa321953ff9e3e3aa222f7167f7abbae00e` |
| Physical-Mac baseline failure | `2e1324f410c41d73d8766f2e56f2d686629434493416c35ef96f7d38e3259214` |
| Mac release-asset controls | `21c0ddeb19040d66ca5791d174097142092a932c10a0753859aa6ac0f481de6d` |
| Mac draft-release controls | `cceb19e874fed00117d74264b5df2d6efd0f26b644308c0360818d240529e703` |
| Mac combined-package controls | `7f81d15a36fd5f5f2b55dd93f6b3d7228c5e0bc3e05c15adc201e79edee4ec95` |
| Mac SBOM controls | `c31aed8f0418eb48f3cbd44865c8f9bd7c2de6901243618fec6e2f5da9bae823` |
| Windows SBOM controls | `22db1e206361a3095e271424166a3c8bbcd370898fbb0da841ae7cad034c8115` |
| Ubuntu SBOM controls | `62b650285fb0e1e3a87bca1e0650ddf72823bed9264bd845d7c08905b20a422c` |
| AppImage-pin failure log | `534a052bd8188ef83f5fa087a51c8d1ec72bcfb3f507a2d8f038045f114037c5` |

## Limits and stopping conditions

`I03-SBOM-ENGINE` accepts only the generator/archive/refusal semantics above. `REL-I03-PROVENANCE` remains Open for complete actual Windows/Linux/macOS payload generation, static/dynamic composition, unresolved licence/provenance dispositions and exact frozen-candidate identity. `GATE-CI-FINAL` remains Open until the pushed `49ecb362` implementation (or its documentation-only descendant) passes every required hosted lane with skips reconciled. No contract, release candidate, signing, REP custody or stable publication is claimed.
