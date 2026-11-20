# FileCat 1.0.0-preview.5

An unsigned preview for everyday file work and deeper inspection on Windows, Ubuntu and macOS. This is a prerelease for evaluation, not the stable 1.0.0 release.

## What changed since preview.4

- **Find newer previews from the app.** A preview build's explicit update check now considers published GitHub prereleases. It still ignores drafts, does not download or install an update, and asks before opening the release page. Stable builds continue to use the stable-release channel.
- **Find macOS files by the name you see.** Panel filters, Find files and mask-based selection now match equivalent composed and decomposed Unicode spellings. FileCat keeps the original filename and bytes; ASCII matching and explicit regular-expression behavior are unchanged.

The first change corrects a preview.4 update check that said no release was available even while preview.4 was public. The second corrects an owned Mac filename that disappeared under the visually identical typed filter. Both have bounded before/after and native source-build evidence in the [release evidence index](https://github.com/benny-cz/FileCat/blob/main/docs/release/1.0.0/FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md). They do not claim that a signed or installed 1.0.0 candidate has qualified.

## Choose a package

| System | Packages | Notes |
|---|---|---|
| Windows 11 x64 | Setup, self-contained portable ZIP, framework-dependent ZIP | The setup includes the administrator-helper route. The framework-dependent ZIP needs the matching .NET desktop runtime and has no helper. Setup filenames omit `preview.5`; the app reports the preview version. |
| Windows 11 ARM64 | Native ARM64 setup and ZIPs | Hosted ARM64 build, tests and package start are required for this preview. Physical ARM64 hardware is not a qualified stable tier. Setup filenames likewise omit `preview.5`. |
| Ubuntu 24.04/26.04 x64 with GNOME | `.deb`, AppImage, portable `.tar.gz` | Other distributions and desktops are outside this preview's claimed support. |
| macOS 26+ on Apple silicon | `.app` ZIP | Ad-hoc signed, not notarized. On first start, use **Open Anyway** in System Settings → Privacy & Security and confirm. |

See the [capability matrix](https://github.com/benny-cz/FileCat/blob/main/docs/CAPABILITIES.md) for actions that differ by platform and location.

## Before working on important data

- Keep a separate backup. Recovery scans request read-only source access, but a mounted operating system can still change its own filesystem metadata. Recover to another physical disk; use a disk image or storage-level read-only protection when source bytes must stay unchanged. Ext4 and APFS undelete are not supported.
- Remote and device locations do not all have Recycle Bin or Trash support. Review the displayed delete mode and result. Replace guarantees also vary by provider.
- Treat unfamiliar files as untrusted. Page preview disables page scripts and network requests. Picture workers have platform-specific restrictions, but process separation is not complete containment for every parser and helper.
- Windows packages have no trusted release signature. Windows may warn about or block an unknown unsigned app. The macOS bundle is ad-hoc signed and needs the first-start step above. There is no automatic updater. Report suspected security issues through [private vulnerability reporting](https://github.com/benny-cz/FileCat/security/policy).

## Verify the downloads

The release contains a `*-assets.json` manifest for each platform, with the SHA-256 and byte size of its packages, SBOMs and the Linux AppImage runtime source bundle. Match a downloaded file to its platform manifest and the [exact hosted inventory](https://github.com/benny-cz/FileCat/blob/4087c20f/docs/release/1.0.0/evidence/E-PREVIEW5-TAG-20261031.md#exact-hosted-inventory). The three hosted manifests were independently downloaded and rehashed:

| Manifest | SHA-256 |
|---|---|
| `FileCat-1.0.0-preview.5-windows-assets.json` | `9df0a14f4810888aeb9282cc8195accf6936551fea9d41056f376477a714ea04` |
| `FileCat-1.0.0-preview.5-linux-assets.json` | `eac464c4cffa1d47a969ccda741877a1574dbf7ac959d7e2c2924af1a2ec6d75` |
| `FileCat-1.0.0-preview.5-macos-assets.json` | `bf175b45babef5e9af71cf90ac53b5e2d0167b83c0f02d5dc0c286e413d65a51` |

The immutable preview source is `8adaf1f17d2120a9c3ceb4c596b6b98354e4d3cc`. All five tagged source jobs and three package jobs passed; this is an unsigned prerelease, not the stable 1.0.0 candidate. Stable 1.0.0 remains NO-GO.
