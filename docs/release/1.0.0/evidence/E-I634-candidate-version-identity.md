# I634 — A release candidate's bytes call themselves `1.0.0-rc.N`, so promoted unchanged they would offer 1.0.0 as an update to itself

**Source:** the RC producer identity of `f9b662e0` (2026-10-30, [release-branch controls](E-REL-PIPELINE-release-branch-20261030.md#checked-pr-positive-control)), as built by `v1.0.0-rc.1` at `8f122365` ([rc.1](E-RC1-TAG-20261124.md)). **Disposition:** blocks promoting a candidate whose bytes carry the RC label (REL-FQ-CANDIDATE, REL-GO-PUBLISH). **Status:** remediated preliminarily at `bbcbdbdc` ([PR #21](https://github.com/benny-cz/FileCat/pull/21), merged into main as `fac2ec40`); found 2026-11-24 while recording rc.1's packages. [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) is the first candidate built with the fix.

## Fault

Release plan section 10.4 step 4 builds a candidate from `v1.0.0-rc.N` with "explicit productVersion=1.0.0", and section 12.2 adds: "Do not qualify `1.0.0-rc.N` binaries and silently rename/rebuild them as `1.0.0`." Section 13.1 then publishes the qualified bytes as 1.0.0 without rebuilding or repackaging them.

The producers keep the RC label inside the bytes:

| Where | rc.1 shows | Set by |
|---|---|---|
| The app's informational version: About, `--version`, the update check | `1.0.0-rc.1+8f1223654063…` | `eng/publish.ps1` (line 60), `eng/package-linux.sh` (35), `eng/package-macos.sh` (31) |
| The .deb's control `Version` | `1.0.0~rc.1` | `eng/package-linux.sh` (183) |
| The tarball's top folder | `FileCat-1.0.0-rc.1/` | `eng/package-linux.sh` (180) |
| File names of the ZIPs, tarball, .deb, AppImage, SBOMs and manifests | `…-1.0.0-rc.1-…` | each producer and `eng/Select-ReleaseAssets.ps1` |

The same candidate already presents 1.0.0 elsewhere:
- **Windows Setup:** `FileCat-1.0.0-win-x64-setup.exe` and `FileCat-1.0.0-win-arm64-setup.exe`, with `AppVersion` 1.0.0.
- **macOS bundle:** `CFBundleShortVersionString` 1.0.0.
- **Assembly and file versions:** `1.0.0.0`.

In run 45350703310, the Linux and macOS package jobs' start checks printed `FileCat 1.0.0-rc.1`, as the 30 October preflight recorded for its local Windows build.

## Consequence if rc.1's bytes became 1.0.0

From `UpdateCheck.cs` and `ReleaseVersion.cs`:

- `UpdateCheck.CurrentVersion` is the informational version up to `+`: `1.0.0-rc.1`.
- A current version with a pre-release part asks GitHub for every release, prereleases included (`ReleaseApiFor`), not only `/latest`.
- `ReleaseVersion.IsNewer("1.0.0-rc.1", "v1.0.0")` is true: with equal numbers, a final release is newer than a pre-release.
- Update checks are on by default (`AppSettings.CheckForUpdates = true`). Every stable 1.0.0 user would therefore be offered "FileCat 1.0.0" at startup, at most once a day, until they skip it. A manual check would report the same "newer" release.
- Those users would also be offered later prereleases, which only preview builds should see.
- About would show "Version 1.0.0-rc.1 (8f12236)", `--version` would print `FileCat 1.0.0-rc.1`, and the package manager would list `1.0.0~rc.1`.

These follow from reading the code. They have not been shown on a machine, which would need a published 1.0.0 release.

## Remedy

A candidate's packages are 1.0.0 packages throughout:
- informational version `1.0.0+<source commit>`;
- .deb version `1.0.0`;
- folders and file names with `1.0.0`.

The candidate reference stays where it identifies the candidate rather than the product: the producer manifests and receipts, the SBOMs' source identity (the commit) and the draft's name. The asset selection and draft checks then accept 1.0.0 names under a `v1.0.0-rc.N` reference. A successor candidate, `v1.0.0-rc.2`, follows the change.

## Change

- **Producers.** `publish.ps1`, `package-linux.sh` and `package-macos.sh` build a candidate with informational version `1.0.0+<commit>`, and name every file, the .deb version and the tarball folder 1.0.0.
- **Selection.** `Select-ReleaseAssets.ps1` selects a candidate as `-Version 1.0.0 -CandidateRef refs/tags/v1.0.0-rc.N` and records the reference in the manifest. Without a reference, it refuses a release version or an RC-named set.
- **Draft checks.** `Assert-DraftReleaseAssets.ps1` and `Invoke-DraftPackageChecks.ps1` expect 1.0.0 names under an RC tag, with a manifest naming that tag. A preview's manifest must name none.
- **CI.** The candidate's selection steps pass the reference, and each package job checks the version its packages report:
  - a candidate: `FileCat 1.0.0`, plus, on Windows, product version `1.0.0+<commit>`;
  - anything else: the package's own version.
- **Previews and manual runs** keep their versions and names.

## Tests

- **Validators.** The three release-asset validators gain the candidate naming and refusal cases, and pass with 16, 46 and 17 cases (8, 42 and 17 before). Each of six weakened guards was caught by a validator.
- **App test.** `UpdateCheckTests.A_candidate_build_is_not_offered_the_release_it_becomes`: a 1.0.0 build asks `/latest` and is not offered `v1.0.0`, where an RC-labelled build asks for prereleases and finds `v1.0.0` newer.
- **Real producers in candidate mode,** at `bbcbdbdc` with `-Version 1.0.0-rc.2 -ProductVersion 1.0.0 -CandidateRef refs/tags/v1.0.0-rc.2`:
  - **Windows (win-x64), on the development host:** the two ZIPs and three SBOMs are named 1.0.0. `FileCat.exe --version` prints `FileCat 1.0.0`, with product version `1.0.0+bbcbdbdc…` and file version `1.0.0.0`; no SBOM mentions an RC. This is the package job's new check, run by hand.
  - **Ubuntu 24.04 VM:** all seven files are named 1.0.0, and the .deb's version is `1.0.0`. The .deb's binary, the tarball (folder `FileCat-1.0.0`) and the AppImage each print `FileCat 1.0.0`, and the informational version is `1.0.0+bbcbdbdc…`. No SBOM mentions an RC.
  - **macOS:** not built by hand. The change there is the same two lines as on Linux. [Manual run 45359812179](https://github.com/benny-cz/FileCat/actions/runs/45359812179) of the branch built it as `1.0.0-dev.2996`, and the package job's new check confirmed the ZIP's name and the app's version. The Linux package job did the same for the .deb, the tarball and the AppImage.
- **CI.** [PR #21's run 45359654262](https://github.com/benny-cz/FileCat/actions/runs/45359654262) passed every job in its first attempt. The candidate-only steps run only on an RC tag, so the next candidate's run is their first.

## What rc.1 still shows

rc.1 is the first real run of the signed candidate pipeline: tests, packages, the owner's approval, eSigner signing, verification and both private drafts. Those results stand. Its bytes are not a candidate for promotion.

## Finite check and stop

1. **Source.** At the successor candidate's source, building with `-Version 1.0.0-rc.N -ProductVersion 1.0.0 -CandidateRef refs/tags/v1.0.0-rc.N` gives informational version `1.0.0+<commit>`, `--version` prints `FileCat 1.0.0`, and every package name, the .deb version and the tarball folder say 1.0.0. The selection and draft checks accept that set under the RC reference, and refuse an RC-named one.
2. **Candidate.** The successor's draft holds only 1.0.0-named assets, and an installed package reports 1.0.0 in About, `--version` and its package manager.
3. **Update check.** A candidate build asks GitHub's `/latest` and finds no newer release when 1.0.0 is the latest release (unit test).

Steps 1 and 3 pass at `bbcbdbdc`. Step 2 passed in rc.2's run 45377416067 for every name and for `--version` and the package manager: all 24 draft assets are named 1.0.0, and the package jobs' new checks passed on all three platforms. About on each installed platform remains, in the installed-candidate checks. Stop when all three pass. Reopen only for:
- a change to how candidates are versioned or named;
- an installed candidate that reports a pre-release version.
