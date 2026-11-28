# Download FileCat

**FileCat 1.0.0** is the first stable release, for Windows 11 x64. Its packages for Windows 11 ARM64, Ubuntu 24.04/26.04 x64 and macOS 26 or later on Apple silicon are labelled previews, built from the same source. The Windows packages are signed; see the [code signing policy](#code-signing-policy).

## Choose your download

Choose your operating system and processor architecture. The links below point to the exact [1.0.0 release](https://github.com/benny-cz/FileCat/releases/tag/v1.0.0).

| Your system | Start here | Other package options |
|---|---|---|
| Windows 11 · x64 (stable) | [Installer (.exe)](https://github.com/benny-cz/FileCat/releases/download/v1.0.0/FileCat-1.0.0-win-x64-setup.exe) | [Portable ZIP](https://github.com/benny-cz/FileCat/releases/download/v1.0.0/FileCat-1.0.0-win-x64-portable.zip) |
| Windows 11 · ARM64 (preview) | [Native ARM64 installer (.exe)](https://github.com/benny-cz/FileCat/releases/download/v1.0.0/FileCat-1.0.0-win-arm64-setup.exe) | [Native ARM64 portable ZIP](https://github.com/benny-cz/FileCat/releases/download/v1.0.0/FileCat-1.0.0-win-arm64-portable.zip) |
| Ubuntu 24.04/26.04 · x64 · GNOME (preview) | [Debian package (.deb)](https://github.com/benny-cz/FileCat/releases/download/v1.0.0/filecat_1.0.0_amd64.deb) | [AppImage](https://github.com/benny-cz/FileCat/releases/download/v1.0.0/FileCat-1.0.0-x86_64.AppImage) · [Portable tar.gz](https://github.com/benny-cz/FileCat/releases/download/v1.0.0/FileCat-1.0.0-linux-x64.tar.gz) |
| macOS 26+ · Apple silicon (preview) | [Application ZIP](https://github.com/benny-cz/FileCat/releases/download/v1.0.0/FileCat-1.0.0-osx-arm64.zip) | Ad-hoc signed; not notarized |

Read the release notes on the [release page](https://github.com/benny-cz/FileCat/releases/tag/v1.0.0) before installing, and check each download's SHA-256. GitHub shows it beside each asset, and each platform's manifest (`FileCat-1.0.0-windows-assets.json`, `FileCat-1.0.0-linux-assets.json`, `FileCat-1.0.0-macos-assets.json`) lists the name, size and SHA-256 of every package with the source commit that built it.

### Install with Scoop (Windows x64)

If you already use [Scoop](https://scoop.sh/), add [FileCat's maintainer-owned bucket](https://github.com/benny-cz/scoop-bucket) and install it from an ordinary PowerShell session:

```powershell
scoop bucket add filecat https://github.com/benny-cz/scoop-bucket
scoop install filecat/filecat
```

This installs the self-contained portable build for Windows 11 x64. Scoop preserves its `Data` directory across updates and normal uninstalls. It has the same capabilities as the portable ZIP, including no installed administrator helper. To update:

```powershell
scoop update
scoop update filecat
```

The [bucket documentation](https://github.com/benny-cz/scoop-bucket#readme) covers settings, removal and package validation.

### Which package fits your workflow?

- **Windows installer:** installs for all users, under Program Files by default, after administrator approval. It includes the administrator-helper route for supported operations. The x64 installer accepts Windows 11 and Windows Server 2016 and refuses Windows 10 (see [Platforms](../README.md#platforms)); the ARM64 installer needs Windows 11. Run over an earlier version, it replaces the program files and keeps your settings ([upgrading](SERVICING.md#how-users-upgrade)).
- **Windows portable ZIP:** includes the .NET runtime and keeps settings, history and other state in `Data/` beside the app. It has no administrator helper. Before you open administrator windows from it, read where to keep it in the [security policy](../SECURITY.md#administrator-windows-from-portable-copies). For disk recovery, put FileCat and its state on another disk, or use `--data` on another disk as described in the [README](../README.md#try-a-local-build).
- **Windows framework-dependent ZIP:** available for [x64](https://github.com/benny-cz/FileCat/releases/download/v1.0.0/FileCat-1.0.0-win-x64-fdd.zip) and [ARM64](https://github.com/benny-cz/FileCat/releases/download/v1.0.0/FileCat-1.0.0-win-arm64-fdd.zip). It needs the .NET 10 Runtime for its architecture and receives .NET security fixes through that runtime. These packages have no administrator helper. The .NET SDK is required only when [building from source](../README.md#try-a-local-build).
- **macOS application ZIP:** the app is ad-hoc signed and not notarized. On first start, use **Open Anyway** in System Settings → Privacy & Security and confirm. If extracting in Terminal, use macOS `ditto -x -k FileCat-1.0.0-osx-arm64.zip DESTINATION_DIR` to preserve the bundle's signing metadata; ordinary `unzip` did not preserve it in the preview.5 check.

**Archive writing:** the new worker routes cover Windows x64/NTFS, Linux x64/ext4 and macOS ARM64/APFS. Windows ARM64 has no qualified new archive-writing route. The framework-dependent Windows ZIPs cannot run the new worker. A portable x64 copy in a folder you own gives the worker read access to its program files on first use, never to its `Data` folder. A mounted AppImage cannot run the worker; use the `.deb` or extract it onto ext4. On macOS, FileCat samples the worker's memory and stops it above its budget; a fast allocation can exceed that budget between samples, so it is not a hard ceiling. See [what works where](CAPABILITIES.md#known-gaps-in-v1-planned-later) for target restrictions.

Windows ARM64 has been built, tested and started on a hosted ARM64 runner; physical ARM64 hardware has not been qualified. Other Linux distributions and desktops are outside the claimed support. See [what works where](CAPABILITIES.md) for platform and location limits.

The [release page](https://github.com/benny-cz/FileCat/releases/tag/v1.0.0) also contains the package manifests, software bills of materials (SBOMs), and Linux AppImage runtime source bundle. The [README](../README.md) introduces FileCat and its source-build route.

## Removing FileCat

- **Windows installer:** Settings → Apps → Installed apps → FileCat → Uninstall. It removes the files it installed, its shortcuts and its uninstall entry, then the program folder once it is empty, so other files in a folder you chose stay. It keeps, on purpose:
  - your settings, history and journals in `%APPDATA%\FileCat` and `%LOCALAPPDATA%\FileCat`;
  - `HKLM\SOFTWARE\FileCat.AdministratorTransfers`, the appearance and workspace you chose to remember for administrator windows, if any;
  - `HKLM\SOFTWARE\FileCat\PrivilegedHost\UsedPlans`, the administrator helper's record of the plans it has run (random plan identifiers and their times). A plan runs at most once, and the helper drops entries older than two days; removing the record at uninstall would let a just-used plan run again after a reinstall;
  - the per-user AppContainer profile `FileCat.ArchiveWriter.v1`, made for the archive-writing worker the first time it ran for you, which an administrator's uninstall cannot reach for every user.

  Delete them yourself if you want them gone.
- **Windows ZIPs:** delete the folder. A portable copy keeps its state in `Data/` inside it; a framework-dependent copy keeps it in `%APPDATA%\FileCat` and `%LOCALAPPDATA%\FileCat`, as an installed one does. The AppContainer profile can stay, as above.
- **Ubuntu:** `sudo apt remove filecat` removes the `.deb` and its AppArmor profile. For the AppImage or the tarball, delete the files. Your settings stay in your home folder (by default `~/.config/FileCat` and `~/.local/share/FileCat`).
- **macOS:** move FileCat.app to the Trash. Your settings stay in your account.

## Code signing policy

**The Windows packages are signed.** Both Setups and their uninstallers carry an Authenticode signature, and so does every program and library inside the Windows installers and ZIPs. Files that their vendors signed, such as Microsoft's, keep the vendor's signature. Every other one, FileCat's own included, is signed with FileCat's code-signing certificate: an individual-validated certificate from SSL.com, held in SSL.com's eSigner service. Windows shows the certificate's subject as the verified publisher. Each signature uses SHA-256 and carries an RFC 3161 timestamp, so it stays valid after the certificate expires. A ZIP file itself is not signed; the files inside it are. Check a file with `Get-AuthenticodeSignature FILE` in PowerShell (status `Valid`) or in its Properties → Digital Signatures. The previews, up to 1.0.0-preview.11, were unsigned.

**How signing runs.** Signing runs in GitHub Actions for a release-candidate tag, in the protected `signing` environment: the maintainer approves each run, administrator bypass is off, and only `main` and `v*` tags can use it. The credentials reach only the signing steps, on a machine that never built the packages. That job checks the unsigned files against the build job's manifest, signs one canary file and checks it against the pinned certificate before it signs the rest, verifies every signature against Windows' roots with revocation, and keeps the signed packages in a private draft release. Nothing is published until the maintainer approves it. A stable release publishes the qualified candidate's signed packages unchanged: its tag builds and signs nothing.

**Other platforms.** The macOS app is ad-hoc signed and not notarized: its signature shows that the bundle is intact but names no publisher. The Linux `.deb`, AppImage and tarball are not signed. Check them by their SHA-256.

SignPath Foundation declined FileCat's application on 2026-10-31 because the project did not yet have enough independent public visibility; the maintainer then chose the SSL.com route above.

The repository maintainer, [Marek Střihavka](https://github.com/benny-cz), owns source changes and release approval. See FileCat's [privacy policy](PRIVACY.md) and [security reporting policy](../SECURITY.md).
