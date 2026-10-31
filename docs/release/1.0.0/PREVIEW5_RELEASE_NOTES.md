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

The release will contain a `*-assets.json` manifest for each platform, with the SHA-256 and byte size of its packages, SBOMs and the Linux AppImage runtime source bundle. Match a downloaded file to its platform manifest. The exact hosted asset inventory and manifest hashes will be added here after private-draft upload and independent verification, before public publication.

This preview will be built from one immutable source tag after all required CI gates pass. The tag and source commit will be recorded here after that gate. Stable 1.0.0 remains NO-GO.
