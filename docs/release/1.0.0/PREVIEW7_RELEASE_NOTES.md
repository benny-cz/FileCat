# FileCat 1.0.0-preview.7

An unsigned preview for file work and inspection on Windows, Ubuntu and macOS. This is a prerelease for evaluation, not the stable 1.0.0 release.

## What changed since preview.6

- **Keep Settings drafts under your control.** Settings stays in place as you switch tabs. Clicking outside it leaves the draft open; Escape and the title close button ask before discarding actual edits. An untouched or reverted draft closes directly. Cancel still discards changes, and invalid names stay visible for correction.
- **Find and edit settings more easily.** Search starts focused, finds tab destinations and avoids offering fields hidden in a collapsed advanced editor. A search with no matches keeps Enter from accepting Settings. New item names are ready for replacement, and the Keyboard shortcut chooser appears before the raw bindings editor, even in a short window.
- **Keep compact dialogs readable.** About, Advanced Find, Apply command, Pack, Create link and new connection adapt their fields to narrow windows. Related size, date, server and action controls stay together; long commands remain editable inside their fields.
- **Prevent accidental Ignored Folders changes.** Pressing Enter in an empty Add field leaves the editor open. Add and Remove are enabled only when they can act.
- **Polish the macOS panel.** The TARGET badge text is centered within its chip.

These changes have source-level and focused native checks; they do not establish installed stable-candidate qualification. The [release evidence index](https://github.com/benny-cz/FileCat/blob/main/docs/release/1.0.0/FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md) records their scope and remaining checks.

## Choose a package

| System | Packages | Notes |
|---|---|---|
| Windows 11 x64 | Setup, self-contained portable ZIP, framework-dependent ZIP | The setup includes the administrator-helper route. The framework-dependent ZIP needs the matching .NET desktop runtime and has no helper. Setup filenames omit `preview.7`; the app reports the preview version. |
| Windows 11 ARM64 | Native ARM64 setup and ZIPs | Hosted ARM64 build, tests and package start are required for this preview. Physical ARM64 hardware is not a qualified stable tier. Setup filenames likewise omit `preview.7`. |
| Ubuntu 24.04/26.04 x64 with GNOME | `.deb`, AppImage, portable `.tar.gz` | Other distributions and desktops are outside this preview's claimed support. |
| macOS 26+ on Apple silicon | `.app` ZIP | Ad-hoc signed, not notarized. On first start, use **Open Anyway** in System Settings → Privacy & Security and confirm. |

See the [capability matrix](https://github.com/benny-cz/FileCat/blob/main/docs/CAPABILITIES.md) for actions that differ by platform and location.

## Before working on important data

- Keep a separate backup. Recovery scans request read-only source access, but a mounted operating system can still change its own filesystem metadata. Recover to another physical disk; use a disk image or storage-level read-only protection when source bytes must stay unchanged. Ext4 and APFS undelete are not supported.
- Remote and device locations do not all have Recycle Bin or Trash support. Review the displayed delete mode and result. Replace guarantees also vary by provider.
- Treat unfamiliar files as untrusted. Page preview disables page scripts and network requests. Picture and PDF workers have platform-specific restrictions, but process separation is not complete containment for every parser and helper.
- Windows packages have no trusted release signature. Windows may warn about or block an unknown unsigned app. The macOS bundle is ad-hoc signed and needs the first-start step above. There is no automatic updater. Report suspected security issues through [private vulnerability reporting](https://github.com/benny-cz/FileCat/security/policy).

## Verify the downloads

The release contains a `*-assets.json` manifest for each platform, with the SHA-256 and byte size of its packages, SBOMs and the Linux AppImage runtime source bundle. Match a downloaded file to its platform manifest and the [exact hosted inventory](https://github.com/benny-cz/FileCat/blob/main/docs/release/1.0.0/evidence/E-PREVIEW7-TAG-20261103.md#exact-hosted-inventory). The release evidence records the downloaded manifests and their hashes.

The immutable product source is `8008a3a8523faf830d8ebaa4399de8d2d2563d6a`. This remains an unsigned prerelease. Stable 1.0.0 remains NO-GO.
