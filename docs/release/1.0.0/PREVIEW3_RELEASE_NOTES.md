# FileCat 1.0.0-preview.3

An early FileCat preview for everyday file work and deeper inspection. Arrange local disks, removable storage, archives and remote servers in a multi-panel, multi-tab workspace; review where an operation will go and what happened afterward. This is a prerelease for evaluation, not the stable 1.0.0 release.

## What to try

- Browse and search across panels and tabs; save workspaces and working sets.
- Copy, move, rename and compare with visible plans, per-item results and an interrupted-job journal.
- Inspect text, binary, pictures, local HTML and Markdown, program structure, checksums and filesystem metadata without executing the inspected file. This preview includes the Windows Markdown Page Up/Down focus correction missing from preview.2.
- Browse nested ZIP, 7z, RAR, TAR and ISO images; edit ZIPs. The other archive formats are read-only.
- Scan supported NTFS, FAT and exFAT disks or images for deleted files, and browse SMB, SFTP, FTPS, FTP and Windows MTP devices where available.

See the [capability matrix](https://github.com/benny-cz/FileCat/blob/v1.0.0-preview.3/docs/CAPABILITIES.md) for actions that differ by platform and location.

## Choose a package

| System | Packages | Notes |
|---|---|---|
| Windows 11 x64 | Setup, self-contained portable ZIP, framework-dependent ZIP | The setup has the installed administrator-helper route. The framework-dependent ZIP needs the matching .NET desktop runtime and has no helper. The setup filename omits `preview.3`, but the app reports `1.0.0-preview.3`. |
| Windows 11 ARM64 | Native ARM64 setup and ZIPs | Built, tested and started on a hosted ARM64 runner; physical ARM64 hardware has not been qualified. The setup filename likewise omits `preview.3`. |
| Ubuntu 24.04/26.04 x64 with GNOME | `.deb`, AppImage, portable `.tar.gz` | Other distributions and desktops are outside this preview's claimed support. |
| macOS 26+ on Apple silicon | `.app` ZIP | Ad-hoc signed, not notarized. For the first start, use **Open Anyway** in System Settings → Privacy & Security and confirm. |

These packages have CI build and test coverage on their named platforms. A downloaded official Windows x64 portable ZIP was also opened in a Win11 VM, where a rendered Markdown page responded to Page Down and Page Up. This does not qualify every installer or physical-device path.

## Before working on important data

- Keep a separate backup. Recovery scans request read-only source access, but a mounted operating system can still change its own filesystem metadata. Recover to another physical disk; a disk image or storage-level read-only protection is preferable when source bytes must stay unchanged. Ext4 and APFS undelete are not supported.
- Remote and device locations do not all have Recycle Bin or Trash support. FileCat shows the delete mode and asks before permanent deletion. Replace guarantees also vary by provider; review the displayed result.
- Treat unfamiliar files as untrusted. Page preview disables page scripts and network requests. Linux x64/ARM64 and macOS picture workers restrict filesystem access and deny writes, network access and process creation; they have no OS whole-process memory limit. Other parser and helper routes have different boundaries, so process separation is not complete containment.
- These downloads do not have trusted release signing. Windows may show an unrecognized-app warning; the macOS app is ad-hoc signed and requires the first-start step above. There is no automatic updater. Report suspected security issues through [private vulnerability reporting](https://github.com/benny-cz/FileCat/security/policy).

## Verify the downloads

Each platform has a `*-assets.json` manifest with SHA-256 and byte size for its packages, SBOMs and, on Linux, the AppImage runtime source bundle. The manifest SHA-256 values for this exact release are:

| Manifest | SHA-256 |
|---|---|
| Windows | `59ce9797f50acedd59e2425b5518efa965770a6cc99d8edea14eae09a7dde258` |
| Linux | `9eb947825636b0e6c144bae783d4fd51b3e8263b989eea8f57ef71a954293461` |
| macOS | `add1ae4869937d2a802a2fecff6e40ddfb0cac2ed3cbc90f9a293870962c7082` |

The annotated tag points to source commit `8320e18a5bd418011a2706c18ac4bebb2e0e33c4`. The draft contains 24 assets, including those three manifests and platform SBOMs.
