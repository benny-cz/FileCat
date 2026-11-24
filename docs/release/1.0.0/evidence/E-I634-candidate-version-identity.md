# I634 — A release candidate's bytes call themselves `1.0.0-rc.N`, so promoted unchanged they would offer 1.0.0 as an update to itself

**Source:** the RC producer identity of `f9b662e0` (2026-10-30, [release-branch controls](E-REL-PIPELINE-release-branch-20261030.md#checked-pr-positive-control)), as built by `v1.0.0-rc.1` at `8f122365` ([rc.1](E-RC1-TAG-20261124.md)). **Disposition:** blocks promoting a candidate whose bytes carry the RC label (REL-FQ-CANDIDATE, REL-GO-PUBLISH). **Status:** open; found 2026-11-24 while recording rc.1's packages.

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

## Remedy (proposed)

A candidate's packages are 1.0.0 packages throughout:
- informational version `1.0.0+<source commit>`;
- .deb version `1.0.0`;
- folders and file names with `1.0.0`.

The candidate reference stays where it identifies the candidate rather than the product: the producer manifests and receipts, the SBOMs' source identity (the commit) and the draft's name. The asset selection and draft checks then accept 1.0.0 names under a `v1.0.0-rc.N` reference. A successor candidate, `v1.0.0-rc.2`, follows the change.

## What rc.1 still shows

rc.1 is the first real run of the signed candidate pipeline: tests, packages, the owner's approval, eSigner signing, verification and both private drafts. Those results stand. Its bytes are not a candidate for promotion.

## Finite check and stop

1. **Source.** At the successor candidate's source, building with `-Version 1.0.0-rc.N -ProductVersion 1.0.0 -CandidateRef refs/tags/v1.0.0-rc.N` gives informational version `1.0.0+<commit>`, `--version` prints `FileCat 1.0.0`, and every package name, the .deb version and the tarball folder say 1.0.0. The selection and draft checks accept that set under the RC reference, and refuse an RC-named one.
2. **Candidate.** The successor's draft holds only 1.0.0-named assets, and an installed package reports 1.0.0 in About, `--version` and its package manager.
3. **Update check.** A candidate build asks GitHub's `/latest` and finds no newer release when 1.0.0 is the latest release (unit test).

Stop when all three pass. Reopen only for:
- a change to how candidates are versioned or named;
- an installed candidate that reports a pre-release version.
