# Download FileCat

**FileCat 1.0.0-preview.3** is available for Windows, Ubuntu and macOS. This is a preview for evaluation; stable 1.0.0 is still in validation. These packages do not have trusted release signing.

## Choose your download

Choose your operating system and processor architecture. The links below point to the exact [1.0.0-preview.3 release](https://github.com/benny-cz/FileCat/releases/tag/v1.0.0-preview.3).

| Your system | Start here | Other package options |
|---|---|---|
| Windows 11 · x64 | [Installer (.exe)](https://github.com/benny-cz/FileCat/releases/download/v1.0.0-preview.3/FileCat-1.0.0-win-x64-setup.exe) | [Portable ZIP](https://github.com/benny-cz/FileCat/releases/download/v1.0.0-preview.3/FileCat-1.0.0-preview.3-win-x64-portable.zip) |
| Windows 11 · ARM64 | [Native ARM64 installer (.exe)](https://github.com/benny-cz/FileCat/releases/download/v1.0.0-preview.3/FileCat-1.0.0-win-arm64-setup.exe) | [Native ARM64 portable ZIP](https://github.com/benny-cz/FileCat/releases/download/v1.0.0-preview.3/FileCat-1.0.0-preview.3-win-arm64-portable.zip) |
| Ubuntu 24.04/26.04 · x64 · GNOME | [Debian package (.deb)](https://github.com/benny-cz/FileCat/releases/download/v1.0.0-preview.3/filecat_1.0.0-preview.3_amd64.deb) | [AppImage](https://github.com/benny-cz/FileCat/releases/download/v1.0.0-preview.3/FileCat-1.0.0-preview.3-x86_64.AppImage) · [Portable tar.gz](https://github.com/benny-cz/FileCat/releases/download/v1.0.0-preview.3/FileCat-1.0.0-preview.3-linux-x64.tar.gz) |
| macOS 26+ · Apple silicon | [Application ZIP](https://github.com/benny-cz/FileCat/releases/download/v1.0.0-preview.3/FileCat-1.0.0-preview.3-osx-arm64.zip) | Ad-hoc signed; not notarized |

Read the [preview release notes](release/1.0.0/PREVIEW3_RELEASE_NOTES.md) and check the [published asset inventory and SHA-256 hashes](release/1.0.0/evidence/E-PREVIEW3-TAG-20261030.md#exact-hosted-inventory-for-the-separate-public-preview-decision) before installing.

### Which package fits your workflow?

- **Windows installer:** includes the administrator-helper route for supported operations. The installer filenames omit `preview.3`; these links still install the preview, and the app reports `1.0.0-preview.3`. Windows may show an unrecognized-app warning because the packages lack trusted release signing.
- **Windows portable ZIP:** includes the .NET runtime and keeps settings, history and other state in `Data/` beside the app. It has no administrator helper. For disk recovery, put FileCat and its state on another disk, or use `--data` on another disk as described in the [README](../README.md#try-a-local-build).
- **Windows framework-dependent ZIP:** available for [x64](https://github.com/benny-cz/FileCat/releases/download/v1.0.0-preview.3/FileCat-1.0.0-preview.3-win-x64-fdd.zip) and [ARM64](https://github.com/benny-cz/FileCat/releases/download/v1.0.0-preview.3/FileCat-1.0.0-preview.3-win-arm64-fdd.zip) if you have the matching .NET runtime. These packages have no administrator helper. The .NET SDK is required only when [building from source](../README.md#try-a-local-build).
- **macOS application ZIP:** the app is ad-hoc signed and not notarized. On first start, use **Open Anyway** in System Settings → Privacy & Security and confirm.

Windows ARM64 has been built, tested and started on a hosted ARM64 runner; physical ARM64 hardware has not been qualified. Other Linux distributions and desktops are outside this preview's claimed support. See [what works where](CAPABILITIES.md) for platform and location limits.

The [release page](https://github.com/benny-cz/FileCat/releases/tag/v1.0.0-preview.3) also contains the package manifests, software bills of materials (SBOMs), and Linux AppImage runtime source bundle. The [README](../README.md) introduces FileCat and its source-build route.

## Code signing policy

The current Windows preview packages are **unsigned**. FileCat has applied to SignPath Foundation for code signing future Windows releases. No SignPath acceptance, certificate or signed FileCat package is claimed. If the application is accepted, this page will identify the signed packages and signing policy before they are offered as signed downloads. The proposed credit for that future service is: “Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).”

The repository maintainer, [benny-cz](https://github.com/benny-cz), owns source changes and release approval. Signing roles and controls will be published after the provider confirms them. See FileCat's [privacy policy](PRIVACY.md) and [security reporting policy](../SECURITY.md).
