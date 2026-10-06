# Third-party notices

FileCat is MIT-licensed (see `LICENSE`). The table records its dependency license declarations.
Full license and notice texts retained from pinned packages and their declared source revisions
are in `licenses/dependencies`, with exact source identities and hashes in `index.json`.
The snapshot includes the pinned .NET 10.0.12 runtime pack texts, build-only packages and RID
alternatives; it does not establish which components ship in each artifact or complete native
license eligibility. LTRData.Extensions full-text provenance, SharpCompress's RAR provenance and
AppImage/native obligations remain under review before stable release. Regenerate the package
inventory with `eng/publish.ps1` (writes `artifacts/sbom-<version>-<rid>.json`) and review the
snapshot whenever dependencies or the SDK change.
The Linux AppImage also includes available wrapper texts in
`licenses/appimage-runtime`. Its index pins the original runtime input and the
declared source archives for these texts; complete static-library composition,
versions and source obligations remain under review.

| Component | Version | License | Notes |
|---|---|---|---|
| .NET runtime and libraries | 10.0.12 | MIT | Self-contained builds include the pinned runtime; framework-dependent builds use the separately serviced runtime |
| Avalonia (Avalonia, Desktop, Skia, HarfBuzz, Win32, X11, Native, FreeDesktop, AtSpi, Remote.Protocol, Themes.Fluent) | 12.1.1 | MIT | UI framework |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | BSD-3-Clause (ANGLE) | Native OpenGL ES over Direct3D on Windows |
| SkiaSharp (+ native assets) | 3.119.4 | MIT; native Skia BSD-3-Clause | 2D graphics |
| HarfBuzzSharp (+ native assets) | 8.3.1.3 | MIT; native HarfBuzz "Old MIT" | Text shaping |
| MicroCom.Runtime | 0.11.6 | MIT | COM interop helper used by Avalonia |
| Tmds.DBus.Protocol | 0.94.1 | MIT | Linux desktop integration (D-Bus) |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | MVVM source generators |
| Microsoft.Web.WebView2 (Core API and loader) | 1.0.3179.45 | BSD-3-Clause | The viewer's web page view on Windows (D-51). FileCat uses an external WebView2 Runtime and does not redistribute that browser runtime |
| SSH.NET | 2026.0.0 | MIT | SFTP and SSH (ADR-17) |
| BouncyCastle.Cryptography | 2.7.0 | MIT | Cryptography used by SSH.NET, and Ed25519 and BLAKE2b for minisign signatures beside files |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | MIT | Logging interfaces used by SSH.NET |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | MIT | Resolved App runtime dependency |
| FluentFTP | 55.0.0 | MIT | FTP and FTPS (ADR-17 addendum, P8) |
| SharpCompress | 0.50.4 | MIT | Read-only 7z, RAR, xz, bzip2, and zstd (ADR-07, P8); Copyright (c) Adam Hathcock |
| LTRData.DiscUtils (Core, Streams, Iso9660, Udf) | 1.0.89 | MIT | Read-only ISO 9660 and UDF images (ADR-07, P8); DiscUtils by Kenneth Bell and contributors, maintained by LTR Data |
| LTRData.Extensions | 1.0.23 | MIT | Helpers used by DiscUtils |
| AppImage type2-runtime (Linux AppImage only) | declared commit 8f39b89, exact runtime input pinned by checksum | MIT root; linked components have their own licenses | The pinned recipe declares libfuse 3.15.0, squashfuse 0.5.2, zstd, zlib, mimalloc and musl. Original root/libfuse/squashfuse texts are retained in `licenses/appimage-runtime`; complete actual static composition, source obligations and binary/source reproducibility remain unqualified. The tarball and Debian package omit this wrapper |
| Inno Setup installer engine, loader and uninstaller (Windows setup package only) | 6.7.1 | Inno Setup license (modified BSD) | Generated installer bytes; Copyright (C) 1997-2026 Jordan Russell, portions Copyright (C) 2000-2026 Martijn Laan. Full license installed as `licenses/InnoSetup-6.7.1.txt`; compiler inputs pinned in `eng/toolchains/inno-setup.json` |

Test-only (not shipped): sample archives from SharpCompress's test suite (MIT), listed in
`tests/FileCat.Core.Tests/TestData/Archives/README.md`; pyftpdlib (MIT) as the FTP/FTPS server in tests; disk images
made by FileCat's own `eng/make-recovery-fixtures.sh` (generated text only; `TestData/Recovery/README.md`).

Build-time only (not shipped): Avalonia.BuildServices (MIT) — its usage-statistics task is disabled in
`Directory.Build.targets`; appimagetool 1.9.1 (MIT, pinned by
checksum in `eng/package-linux.sh`) builds the AppImage.

No code from Open Salamander, and no code or assets from Total Commander or FAR Manager,
are included; they served as behavioral references only.
The FileCat icon is original artwork (`eng/make-icon.ps1`).
