# FileCat 1.0.0-preview.9

An unsigned preview for file work and inspection on Windows, Ubuntu and macOS. This is a prerelease for evaluation, not the stable 1.0.0 release.

## What changed since preview.8

- **Hear about a newer release after startup.** FileCat checks its published GitHub releases in the background at most once every 24 hours and offers a newer release after the main window opens. The dialog can open that release page, defer the offer, or skip that exact version. A later version can still be offered. Disable automatic checks in **Settings → Privacy**; **Help → Check for updates** remains an explicit check. FileCat does not download or install updates.
- **Keep picture thumbnails steady while browsing.** The Picture grid retains unchanged thumbnails across redraws and folder refreshes, including when more than 64 tiles are visible. It keeps locally available photos warm while scrolling through an album, with visible tiles protected and offscreen answers trimmed to a 512-entry and 96-MiB cache. Changed pictures request a fresh thumbnail. OneDrive files marked **Online only** remain listed with **Open to download** instead of triggering background content reads.
- **Correct shipped owner metadata.** The license, package metadata and related notices identify the repository maintainer consistently.

The startup offer was exercised on a disposable Windows VM against the published preview.8 release, including skip, opt-out and manual-check behavior. The Picture-grid changes have focused rendering and native checks on Windows and macOS. These results and source CI do not establish installed stable-candidate qualification. The [release evidence index](https://github.com/benny-cz/FileCat/blob/main/docs/release/1.0.0/FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md) records their scope and remaining checks.

## Choose a package

| System | Packages | Notes |
|---|---|---|
| Windows 11 x64 | Setup, self-contained portable ZIP, framework-dependent ZIP | The setup includes the administrator-helper route. The framework-dependent ZIP needs the matching .NET desktop runtime and has no helper. Setup filenames omit `preview.9`; the app reports the preview version. |
| Windows 11 ARM64 | Native ARM64 setup and ZIPs | Hosted ARM64 build, tests and package start are required for this preview. Physical ARM64 hardware is not a qualified stable tier. Setup filenames likewise omit `preview.9`. |
| Ubuntu 24.04/26.04 x64 with GNOME | `.deb`, AppImage, portable `.tar.gz` | Other distributions and desktops are outside this preview's claimed support. |
| macOS 26+ on Apple silicon | `.app` ZIP | Ad-hoc signed, not notarized. On first start, use **Open Anyway** in System Settings → Privacy & Security and confirm. |

See the [capability matrix](https://github.com/benny-cz/FileCat/blob/main/docs/CAPABILITIES.md) for actions that differ by platform and location.

## Before working on important data

- Keep a separate backup. Recovery scans request read-only source access, but a mounted operating system can still change its own filesystem metadata. Recover to another physical disk; use a disk image or storage-level read-only protection when source bytes must stay unchanged. Ext4 and APFS undelete are not supported.
- Remote and device locations do not all have Recycle Bin or Trash support. Review the displayed delete mode and result. Replace guarantees also vary by provider.
- Treat unfamiliar files as untrusted. Page preview disables page scripts and network requests. Picture and PDF workers have platform-specific restrictions, but process separation is not complete containment for every parser and helper.
- Windows packages have no trusted release signature. Windows may warn about or block an unknown unsigned app. The macOS bundle is ad-hoc signed and needs the first-start step above. FileCat's update check opens the release page only and does not download or install a new build. Report suspected security issues through [private vulnerability reporting](https://github.com/benny-cz/FileCat/security/policy).

## Verify the downloads

The release contains a `*-assets.json` manifest for each platform, with the SHA-256 and byte size of its packages, SBOMs and the Linux AppImage runtime source bundle. Match a downloaded file to its platform manifest and the [exact hosted inventory](https://github.com/benny-cz/FileCat/blob/main/docs/release/1.0.0/evidence/E-PREVIEW9-TAG-20261103.md#exact-hosted-inventory).

The three hosted manifests were separately downloaded and found byte-identical
to their producer manifests:

| Manifest | SHA-256 |
|---|---|
| `FileCat-1.0.0-preview.9-windows-assets.json` | `5bea418b30bdb3a9a9931648f3be141d485e0ba9e26cfc426dee13b1c9ce1203` |
| `FileCat-1.0.0-preview.9-linux-assets.json` | `f6bef76eaee338c7b3665539445d3dd5c1ec9de0700f983041d7dfbdbdbcd411` |
| `FileCat-1.0.0-preview.9-macos-assets.json` | `6390aec22339a154d33e748704080389171f074b34edaa5e6192b520c8f73e4a` |

The immutable product source is `b333eebfa0b0745bc632c2b339330b2d454131c7`.
All five tagged source jobs and three package jobs passed; the 20 tagged test
receipts match the exact-source main run at 41,511 passed, 1,574 skipped and
zero failed. The owner-reviewed private draft's 24 hosted assets match the
producer manifests and six upload receipts. This remains an unsigned
prerelease. Stable 1.0.0 remains **NO-GO**.
