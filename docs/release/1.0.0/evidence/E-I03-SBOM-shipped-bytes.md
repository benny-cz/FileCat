# E-I03-SBOM-SHIPPED — each SBOM describes the bytes its package ships; references by allowlist; restored package files checked

**Release engineering for I03-SBOM-ENGINE and REL-REVIEW-CODEX-20261029 (R-M15 and two release-engineering Lows), on `main` at `bff7a1110727f96c79e52052bbae2f0930dbc07b` (2026-10-29). Preliminary component and package evidence; not hosted CI of this commit, candidate qualification or release GO.**

## What the review found

The [2026-10-29 review](E-REVIEW-2026-10-29-codex-commits.md) of `c7ed9540` found that the SBOMs did not describe the bytes FileCat ships (R-M15):
- The AppImage SBOM was written before `appimagetool` ran, so it lacked `.DirIcon`, hashed the runtime before appimagetool wrote its digest into it, and filed the runtime under FileCat's license.
- The setup SBOM omitted Inno Setup's license text, loader and uninstaller.
- `eng/publish.ps1 -ArtifactDirectory` moved the payload and SBOMs, while the installer still packed `artifacts/publish`.
- The Linux and macOS SBOMs listed `FileCat.ShellHost`, which those packages delete after publishing.

Two of its release-engineering Lows belong to the same code:
- LTRData.Extensions' copyright existed only in the notice index's JSON.
- The SBOM tool attributed `reference` libraries to any reviewed package whose ID they start with, and it hashed only each package's `.nupkg`, while publishing copies files from the restored package folder.

## What changed

| Commit | Change |
|---|---|
| `f80a2822` | **Projects.** A project whose runtime files the payload lacks is not listed as shipped; the root package's comment names what the manifest lists but the payload omits. **AppImage.** `eng/package-linux.sh` writes the SBOM after `appimagetool`, from the AppImage's own `--appimage-extract` tree plus the runtime bytes the AppImage starts with; the runtime is filed under its own `type2-runtime` package and licenses. An AppImage whose SBOM cannot be written is kept only as `.rejected`. **Setup.** The setup SBOM files `licenses/InnoSetup-6.7.1.txt` under an Inno Setup package, whose comment names the loader and uninstaller that the pinned compiler supplies. **Installer.** `eng/Compile-WindowsInstaller.ps1` takes `-ArtifactDirectory`, refuses a payload that differs from its setup SBOM (`eng/Assert-SetupPayloadMatchesSbom.ps1`), and passes `PayloadDir` and the output folder to `eng/installer/FileCat.iss`. |
| `4e5c5075` | `THIRD-PARTY-NOTICES.md` gives LTRData.Extensions' MIT notice in full, with the copyright its signed package declares ("2011 - 2026, Olof Lagerkvist, LTR Data"). |
| `bff7a111` | **References by allowlist.** `licenses/dependencies/index.json` names each reference a package ships and the package file it comes from: Microsoft.Web.WebView2 → `Microsoft.Web.WebView2.Core` from `lib_manual/netcoreapp3.0`. Any other reference is refused, including one that only shares the package's ID as a prefix. **Restored files.** Every file the dependency manifests name for a package, a reference or the runtime pack must exist in the restored folder with the bytes of the reviewed archive's member, or no SBOM is written. Each run prints how many it compared. |

The first Linux and macOS runs of the allowlist commit, never pushed, refused to write any SBOM: "Published reference component omits its reviewed parent package". On those RIDs the manifest lists WebView2's Core reference without the package, which has no files there. The extra parent check was dropped before push, and two new controls cover that layout.

## Verification

| Machine | Run | Result |
|---|---|---|
| Windows x64 host | `eng/publish.ps1 -Version 1.0.0-inst.1 -Runtime win-x64` from the clean commit | Setup-payload SBOM 327 files, including Inno Setup's license text under its own package; portable 323; FDD 135. 32 reviewed packages each. 233, 233 and 46 restored files match their archives: each is exactly the number of package, reference and runtime-pack files the manifests name. |
| Windows x64 host | `eng/validation/validate-artifact-sbom.py` | 33/33 controls (10 allowed, 23 refused), each refusal for its own reason. |
| Windows x64 host | `eng/validation/validate-dependency-notice-manifest.py` | 71/71. |
| Ubuntu 24.04.5 VM | `eng/package-linux.sh 1.0.0-refs3.1 linux-x64` from the clean commit | Exit 0 in 55 s. tar 317, deb 329 and AppImage 339 files, 229 restored files matched in each. The AppImage SBOM holds `.DirIcon`. Its `.appimage/runtime` SHA-256 (`0d3ffc830a28a419…`) equals that of the AppImage's first 944,632 bytes, filed under `type2-runtime` `fedfad51`, `MIT AND LGPL-2.1-only AND BSD-2-Clause AND BSD-3-Clause AND Zlib`. No SBOM lists FileCat.ShellHost; each root comment names it. WebView2's Core reference is attributed in all three. |
| Ubuntu 24.04.5 VM | Artifact-SBOM controls | 35/35 (11 allowed, 24 refused; the symbolic-link cases run on Unix only). |
| macOS 27 arm64 Mac | `eng/package-macos.sh 1.0.0-refs3.1` from the clean commit | Exit 0. 317 files; 229 restored files matched. ShellHost is not listed, and the root comment names it. The reference is attributed. |
| macOS 27 arm64 Mac | Artifact-SBOM controls | 35/35. |
| Win11 VM (administrator) | Inno Setup 6.7.1 acquired with `eng/Acquire-InnoSetup.ps1`; the host's setup payload and its SBOM copied to `C:\fc-inst\art`; `eng/Compile-WindowsInstaller.ps1 -Version 1.0.0 -ArtifactDirectory C:\fc-inst\art` | **One payload byte changed:** refused before Inno Setup ran ("0 missing, 0 extra, 1 changed (first: FileCat.deps.json)"), and no setup program was written. **The real payload:** compiled from `C:\fc-inst\art\publish\win-x64` into `C:\fc-inst\art`, with `/DPayloadDir` and `/O` naming that folder. No `artifacts` folder appeared in the repository. The receipt shows exit 0, inputs unchanged, 119 frozen compiler files verified before and after, and `PayloadMatchesSetupSbom`. The setup program is 72,390,526 bytes, SHA-256 `20cd4f3499c931d5a69bb7c0fca64226d79e90a8c5ab3189d6c7026c186a7f27`. |
| Win11 VM (administrator) | Silent install into `C:\fc-inst\installed`, then uninstall | The installed tree holds all 327 SBOM files with their SBOM hashes, including Inno Setup's license text. Its only other files are Inno Setup's `unins000.exe` and `unins000.dat`, which the Inno Setup package's comment names. The installed `FileCat.exe --version` prints `FileCat 1.0.0-inst.1`. Install and uninstall both exit 0. |

The new controls are:
- **References:** one that only shares the package's ID as a prefix, one the index does not name, and one whose manifest names a different file are refused. A reference whose package has no files in the manifest is allowed, and refused once its restored file changes.
- **Restored files:** changed package, resource (an escaped archive name), reference and runtime-pack files are refused, as are a missing one, a manifest path outside the package, a manifest file the archive lacks, and (on Unix) a linked restored file.

## Retained raw evidence

The host runs are under the session scratchpad: the publish is `inst-publish`, and the controls are `sbom-controls-073651` and `notice-controls-073438`. Package logs are `/home/benny/pkg-refs3*.log` on the Ubuntu VM and `~/FileCatValidation/pkg-mac-refs3.log` on the Mac. Selected SHA-256 values:

| File | SHA-256 |
|---|---|
| `FileCat-1.0.0-refs3.1-x86_64.AppImage` | `512823e8e71a97c2393748c728e051440cab6b780a0e19540c5098fb8ec0e172` |
| `sbom-1.0.0-refs3.1-linux-x64-appimage.spdx.json` | `1db65d5f65057d8edcbbfd16b2d143594cfb99403bf03b31c8395498aa965d83` |
| `filecat_1.0.0-refs3.1_amd64.deb` | `ef152836e0784c899bf2de07a9d2d5dd39ea250b1305eba2c929f47ea7d0afdf` |
| `FileCat-1.0.0-refs3.1-osx-arm64.zip` | `ae7a61bdc9023cfd58bfce5b14a06a176eb6ae605a35ac2b1f62697bc6bd4f86` |
| `sbom-1.0.0-refs3.1-osx-arm64-macos-app.spdx.json` | `2ad666cdee00e2a2b6678ae4c9461b64428326a6271679755e102962a2d69806` |

## What this does not show

- Hosted CI of this commit. The Windows ARM64 lane's "installer compiles" step is skipped while I555's App-test failures stand.
- The Windows ARM64 installer, signing, and any frozen candidate's SBOM.
