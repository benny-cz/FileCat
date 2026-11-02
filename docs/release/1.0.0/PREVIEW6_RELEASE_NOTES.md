# FileCat 1.0.0-preview.6

An unsigned preview for file work and inspection on Windows, Ubuntu and macOS. This is a prerelease for evaluation, not the stable 1.0.0 release.

## What changed since preview.5

- **Preview PDF pages in FileCat.** F3 now shows bounded PDF page images with page navigation and document Info. Damaged or encrypted files fall back to a clear error or byte view.
- **Keep more of what a copy means.** Mac file and folder copies preserve supported resource forks, extended attributes and access-control lists, or refuse a transfer they cannot complete safely. Linux copies preserve POSIX access and default ACLs. Sparse local copies preserve holes on the checked Ubuntu and Windows paths, reducing avoidable disk allocation. Mac sparse-hole preservation remains unqualified.
- **Avoid acting on the wrong Unix file.** FileCat refuses ambiguous Unix filenames whose invalid byte sequences collapse to the same displayed name. This conservative guard can also refuse a genuine name containing the replacement character; rename it with a byte-aware tool before using it in FileCat.
- **Make Windows file actions reliable.** Right-click Properties opens FileCat's owned sheet; Send to compressed folder creates a verified ZIP; Copy and Cut publish the selected file paths; Share in Explorer hands off to the Windows Share UI. Cross-panel drags can now review FTPS transfers, Registry export or copy, and whole-drive contents copy. Two separate clicks on a filename start inline rename, while a double-click still opens it.
- **Make navigation and review clearer.** Find states its archive-content limit; Flat View refreshes when its recursive search completes; hidden-item counts and saved column profiles are visible. Command search and comparison/synchronization rows have readable names for assistive technology, and shortcut editing has a direct search route.
- **Improve platform details.** Mac Delete and Undo say Trash, Properties opens a FileCat filesystem record, Finder tags display their names, and a new text file opens with an available editor. Windows case-sensitive Git badges stay distinct. Slow SFTP host-key approval can recover without a manual refresh. The Steampunk header has connected glass with a softer edge blend.

The [release evidence index](https://github.com/benny-cz/FileCat/blob/main/docs/release/1.0.0/FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md) records bounded source and native checks for these changes. The preview does not establish signed-package or installed stable-candidate qualification.

## Choose a package

| System | Packages | Notes |
|---|---|---|
| Windows 11 x64 | Setup, self-contained portable ZIP, framework-dependent ZIP | The setup includes the administrator-helper route. The framework-dependent ZIP needs the matching .NET desktop runtime and has no helper. Setup filenames omit `preview.6`; the app reports the preview version. |
| Windows 11 ARM64 | Native ARM64 setup and ZIPs | Hosted ARM64 build, tests and package start are required for this preview. Physical ARM64 hardware is not a qualified stable tier. Setup filenames likewise omit `preview.6`. |
| Ubuntu 24.04/26.04 x64 with GNOME | `.deb`, AppImage, portable `.tar.gz` | Other distributions and desktops are outside this preview's claimed support. |
| macOS 26+ on Apple silicon | `.app` ZIP | Ad-hoc signed, not notarized. On first start, use **Open Anyway** in System Settings → Privacy & Security and confirm. |

See the [capability matrix](https://github.com/benny-cz/FileCat/blob/main/docs/CAPABILITIES.md) for actions that differ by platform and location.

## Before working on important data

- Keep a separate backup. Recovery scans request read-only source access, but a mounted operating system can still change its own filesystem metadata. Recover to another physical disk; use a disk image or storage-level read-only protection when source bytes must stay unchanged. Ext4 and APFS undelete are not supported.
- Remote and device locations do not all have Recycle Bin or Trash support. Review the displayed delete mode and result. Replace guarantees also vary by provider.
- Treat unfamiliar files as untrusted. Page preview disables page scripts and network requests. Picture and PDF workers have platform-specific restrictions, but process separation is not complete containment for every parser and helper.
- Windows packages have no trusted release signature. Windows may warn about or block an unknown unsigned app. The macOS bundle is ad-hoc signed and needs the first-start step above. There is no automatic updater. Report suspected security issues through [private vulnerability reporting](https://github.com/benny-cz/FileCat/security/policy).

## Verify the downloads

The release contains a `*-assets.json` manifest for each platform, with the SHA-256 and byte size of its packages, SBOMs and the Linux AppImage runtime source bundle. Match a downloaded file to its platform manifest and the [exact hosted inventory](https://github.com/benny-cz/FileCat/blob/1a7581283ed85f38bf31b626ed0114e613adf3e8/docs/release/1.0.0/evidence/E-PREVIEW6-TAG-20261102.md#exact-hosted-inventory). The three hosted manifests were separately downloaded and rehashed:

| Manifest | SHA-256 |
|---|---|
| `FileCat-1.0.0-preview.6-windows-assets.json` | `7064b573a265adb0e211fc7543c0ef3acb62223ae8c9f7596180f0c204ad52ce` |
| `FileCat-1.0.0-preview.6-linux-assets.json` | `04b091191208dc0e9b856f3d0045e047f02987a6e8cd4783ba8b7518fa803cdc` |
| `FileCat-1.0.0-preview.6-macos-assets.json` | `40fe063bd30cda7aad3ac28ef0fb10cbc819a383d8a1399a4c4b7801f31af6b2` |

The immutable source is `014a6472eff5dc45e23a8069849102b22291634f`. All five tagged source jobs and three package jobs passed, and the private draft's 24 hosted assets match the producer manifests and upload receipts. This remains an unsigned prerelease. Stable 1.0.0 remains NO-GO.
