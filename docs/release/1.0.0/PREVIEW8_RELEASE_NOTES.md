# FileCat 1.0.0-preview.8

An unsigned preview for file work and inspection on Windows, Ubuntu and macOS. This is a prerelease for evaluation, not the stable 1.0.0 release.

## What changed since preview.7

- **Choose another Analyze folder field without restarting.** Invoking **View → Analyze folder** opens the field chooser every time. It shows the current sort, lets Escape leave that sort alone, and keeps the sort direction when you repeat the same field. Choosing a different field starts its analysis.
- **Browse pictures in one viewer.** When you open a picture from a panel, Space and Backspace move through supported pictures in that tab's visible folder order. Navigation stops at either end. If the source tab leaves the folder or hides the current picture, the viewer stops browsing that sequence. A picture that fails to open leaves the last good picture in place.
- **Show pictures in the file panel.** **View → Show pictures** changes the active tab to a thumbnail grid; **View → Hide pictures** returns it to the file list. The grid still includes folders and other files, keeps the tab's sorting, filtering, marks and file actions, and adapts to narrower panels. Visible supported pictures receive bounded background thumbnails; files without a usable thumbnail remain visible with an icon.

The automatic picture path supports PNG, JPEG, GIF, WebP, BMP and ICO content. Other formats, including HEIC, remain ordinary files. Files over 64 MiB are not automatically decoded for the grid. These changes have source-level, focused native and hosted CI checks; they do not establish installed stable-candidate qualification. The [release evidence index](https://github.com/benny-cz/FileCat/blob/main/docs/release/1.0.0/FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md) records their scope and remaining checks.

## Choose a package

| System | Packages | Notes |
|---|---|---|
| Windows 11 x64 | Setup, self-contained portable ZIP, framework-dependent ZIP | The setup includes the administrator-helper route. The framework-dependent ZIP needs the matching .NET desktop runtime and has no helper. Setup filenames omit `preview.8`; the app reports the preview version. |
| Windows 11 ARM64 | Native ARM64 setup and ZIPs | Hosted ARM64 build, tests and package start are required for this preview. Physical ARM64 hardware is not a qualified stable tier. Setup filenames likewise omit `preview.8`. |
| Ubuntu 24.04/26.04 x64 with GNOME | `.deb`, AppImage, portable `.tar.gz` | Other distributions and desktops are outside this preview's claimed support. |
| macOS 26+ on Apple silicon | `.app` ZIP | Ad-hoc signed, not notarized. On first start, use **Open Anyway** in System Settings → Privacy & Security and confirm. |

See the [capability matrix](https://github.com/benny-cz/FileCat/blob/main/docs/CAPABILITIES.md) for actions that differ by platform and location.

## Before working on important data

- Keep a separate backup. Recovery scans request read-only source access, but a mounted operating system can still change its own filesystem metadata. Recover to another physical disk; use a disk image or storage-level read-only protection when source bytes must stay unchanged. Ext4 and APFS undelete are not supported.
- Remote and device locations do not all have Recycle Bin or Trash support. Review the displayed delete mode and result. Replace guarantees also vary by provider.
- Treat unfamiliar files as untrusted. Page preview disables page scripts and network requests. Picture and PDF workers have platform-specific restrictions, but process separation is not complete containment for every parser and helper.
- Windows packages have no trusted release signature. Windows may warn about or block an unknown unsigned app. The macOS bundle is ad-hoc signed and needs the first-start step above. There is no automatic updater. Report suspected security issues through [private vulnerability reporting](https://github.com/benny-cz/FileCat/security/policy).

## Verify the downloads

The release contains a `*-assets.json` manifest for each platform, with the SHA-256 and byte size of its packages, SBOMs and the Linux AppImage runtime source bundle. Match a downloaded file to its platform manifest and the exact hosted inventory in the release evidence record. The three hosted manifests will be separately downloaded and rehashed before public publication.

This remains an unsigned prerelease. Stable 1.0.0 remains NO-GO.
