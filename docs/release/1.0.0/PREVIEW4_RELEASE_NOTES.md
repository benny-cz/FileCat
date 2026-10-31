# FileCat 1.0.0-preview.4

An unsigned preview for everyday file work and deeper inspection on Windows, Ubuntu and macOS. This is a prerelease for evaluation, not the stable 1.0.0 release.

## What changed since preview.3

- Paste files and nested folders copied from a ZIP opened in Windows Explorer into a FileCat directory, using bounded virtual-file staging and FileCat's Copy job.
- Use Markdown links to nearby files; a link that leaves the current document folder opens a visible resolved-path review before FileCat follows it.
- Search while reading: the viewer responds to typing, shows match feedback, wraps with a brief notice, and lets you close Find. Refresh keeps the reading position where possible and rechecks an open query against the new contents.
- Read recent Operations first in a drawer that uses less height and keeps details reachable. Transfer speed history is a compact disclosure within the report; pausing records a zero rate.
- In the command line, insert the focused quoted filename at the caret, return to an unfinished draft after browsing history, and find successful external-command output in the operation report.
- Navigate out of a denied folder with `..`, and use the parent row's navigation-only context menu. A Find result whose source changed before transfer review is refused with a re-search instruction.

These changes have bounded source and native checks in the [release evidence index](https://github.com/benny-cz/FileCat/blob/main/docs/release/1.0.0/FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md). They are not a claim that a signed or installed 1.0.0 candidate has qualified.

## Choose a package

| System | Packages | Notes |
|---|---|---|
| Windows 11 x64 | Setup, self-contained portable ZIP, framework-dependent ZIP | The setup includes the administrator-helper route. The framework-dependent ZIP needs the matching .NET desktop runtime and has no helper. Setup filenames omit `preview.4`; the app reports the preview version. |
| Windows 11 ARM64 | Native ARM64 setup and ZIPs | Hosted ARM64 build, tests and package start are required for this preview. Physical ARM64 hardware is not a qualified stable tier. Setup filenames likewise omit `preview.4`. |
| Ubuntu 24.04/26.04 x64 with GNOME | `.deb`, AppImage, portable `.tar.gz` | Other distributions and desktops are outside this preview's claimed support. |
| macOS 26+ on Apple silicon | `.app` ZIP | Ad-hoc signed, not notarized. On first start, use **Open Anyway** in System Settings → Privacy & Security and confirm. |

See the [capability matrix](https://github.com/benny-cz/FileCat/blob/v1.0.0-preview.4/docs/CAPABILITIES.md) for actions that differ by platform and location.

## Before working on important data

- Keep a separate backup. Recovery scans request read-only source access, but a mounted operating system can still change its own filesystem metadata. Recover to another physical disk; use a disk image or storage-level read-only protection when source bytes must stay unchanged. Ext4 and APFS undelete are not supported.
- Remote and device locations do not all have Recycle Bin or Trash support. Review the displayed delete mode and result. Replace guarantees also vary by provider.
- Treat unfamiliar files as untrusted. Page preview disables page scripts and network requests. Picture workers have platform-specific restrictions, but process separation is not complete containment for every parser and helper.
- Windows packages have no trusted release signature. Windows may warn about or block an unknown unsigned app. The macOS bundle is ad-hoc signed and needs the first-start step above. There is no automatic updater. Report suspected security issues through [private vulnerability reporting](https://github.com/benny-cz/FileCat/security/policy).

## Verify the downloads

The release contains a `*-assets.json` manifest for each platform, with the SHA-256 and byte size of its packages, SBOMs and the Linux AppImage runtime source bundle. Match a downloaded file to the manifest for its platform and to the published release inventory. The exact hosted manifest hashes and source commit will be added here only after the protected draft upload is independently verified.
