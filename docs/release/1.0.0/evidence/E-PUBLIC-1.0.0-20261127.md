# E-PUBLIC-1.0.0-20261127 — FileCat 1.0.0 published, and section 13.3's public download checks

2026-11-27 (UTC). Successor to [E-PROMOTION-1.0.0-20261127](E-PROMOTION-1.0.0-20261127.md), after the owner's [GO](E-GO-1.0.0-20261127.md). **FileCat 1.0.0 is released.**

## Publication

- **The owner** reviewed the private draft "FileCat 1.0.0" (release 460975687) and pressed Publish release. It was published at 2026-11-27T19:25:12Z: [release `v1.0.0`](https://github.com/benny-cz/FileCat/releases/tag/v1.0.0).
- **GitHub reports:**
  - published, not a prerelease, and immutable;
  - the latest release: `/releases/latest` answers `v1.0.0`;
  - 24 assets.
- **"Set as the latest release":** the edit page showed no such checkbox. Every earlier release was a prerelease, so GitHub made 1.0.0 the latest without one.
- **The release page lists 26 entries.** They are the 24 assets and the two "Source code" archives GitHub adds to every release, made from the tag's commit when downloaded. Preview.11's page shows the same 24 plus 2.

## Section 13.3's public download checks: all passed

1. **The anonymous release check,** `eng/Check-PublicRelease.ps1` with the values the GO names: `RESULT fail=False`.
   - **The release:** published and not a prerelease; titled "FileCat 1.0.0"; the latest release; `v1.0.0` peels to `fa6678c58aa639f62c4bcf793e6e84e6f1027200`; the release text's SHA-256 is the notes' (`79cd59a2c4b8ee2d352fd1ddc6f234edd3db7817e44e3b891e19d3da1ffcd3dc`).
   - **The manifests:** each has the SHA-256 the GO named and names 1.0.0 and the source (`refs/tags/v1.0.0-rc.10`).
   - **The assets:** all 24, downloaded without a GitHub token, match GitHub's digests and the manifests' pins; none is unlisted.
   - **Signatures:**
     - both Setups are Authenticode-valid and timestamped;
     - every PE file in the four Windows ZIPs is valid: 59 and 246 for each architecture, 610 in all.
   - **GitHub's attestation:** `gh release verify v1.0.0` passes, and `gh release verify-asset` attests 24 of 24.
   - **Links:** 19 in the release text and in the README, DOWNLOAD, SECURITY and PRIVACY files at `v1.0.0` all answer.
2. **A clean install from the public Setup** in the Windows 11 VM, with c1's script inside the OneDrive guard: `RESULT fail=False`. The guard found OneDrive closed and unchanged before and after.
   - **Round 1:** the downloaded Setup (`8a0dcd5f45ea0ab0…`, as the manifest pins it) installed over rc.10's identical bytes.
   - **The installed files:** the 402 files of the setup payload's SBOM match, and 249 installed PE files verify (57 FileCat, 192 vendor, 0 failed, 4/4 controls).
   - **Uninstall:** the uninstaller is valid and signed by the Setup's signer. Uninstalling removed the folder, the entry and the Start menu shortcut.
   - **Round 2,** a fresh install: exit 0, `FileCat 1.0.0`, one uninstall entry and the administrator helper.
   - **The other copies:** the portable copy and the framework-dependent copy, on .NET 10.0.14, print `FileCat 1.0.0`.
3. **A preview's update notice (I581),** on the Ubuntu 24.04.5 VM, with row a25. The VM downloaded both tarballs anonymously from the releases, each checked against its published SHA-256. a25 passed, 8 of 8:
   - **The public 1.0.0:** Check for updates says "FileCat 1.0.0 is the latest release." and downloads nothing. A new profile checks at startup; with automatic checks off, FileCat makes no connection at startup.
   - **Preview.11's public build:** it shows "Update available": "FileCat 1.0.0 is available; this is 1.0.0-preview.11. FileCat never downloads or installs updates itself." Its buttons are Not now, Skip this version and Open release page.
   - **The harness change:** before publication, a25 counted "1.0.0 is available" as a draft's offer. It now expects it once GitHub's `/releases/latest` answers `v1.0.0`; a draft or RC offer still fails.

## What closes

- **I18** (release control) closes after the promotion and these checks, as the owner accepted. The register then has 667 remediated, 22 closed and 1 unresolved row: I677, the known issue the release notes name.
- **The acceptance ledger:** I581-PREVIEW-UPDATE, REL-PIPELINE and REL-GO-PUBLISH are Accepted. Every row is Accepted or Optional.
- **The release notes:** the published text is `RELEASE_NOTES_1.0.0.md` at `3d38c787`. It is now merged into `main` with its sources (`ef06d12e`).

## Kept

On the release session's host, `E:\FileCat\.tmp\fq-rc10\public`:

| File | SHA-256 |
|---|---|
| `public-check.txt` (the check's own result) | `a0020f194231fad4…` |
| `public-release.json` (the release as GitHub reported it) | `f27e772ccc7b9d11…` |
| `gh-release-verify.txt` (the attestation output) | `a2b111acedec94b3…` |
| `public-check.log` | `7aa1773f8f2d51fd…` |
| `c1-public-1.0.0/c1.txt` | `151191510ead4e2b…` |
| `c1-public-1.0.0/host.log` | `ad80c36a194882c1…` |
| `ubuntu-a25-public-1.0.0.tar.gz` (a25's results and two screenshots) | `24441faea1f0ad78…` |
| `post-publish-ubuntu.log` | `865324cf6e4c2296…` |

They and the downloaded assets stay outside the sealed REP, which a GO record must not include. A successor package may carry them.
