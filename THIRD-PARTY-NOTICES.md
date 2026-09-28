# Third-party notices

FileCat is MIT-licensed (see `LICENSE`). It redistributes the components below. Every shipped
component carries an OSI-approved license without commercial dual-licensing (plan §20; required by
SignPath Foundation code signing). Regenerate the inventory with `eng/publish.ps1` (writes
`artifacts/sbom-<version>.json`) and update this file whenever a dependency changes.

| Component | Version | License | Notes |
|---|---|---|---|
| .NET runtime and libraries | 10.0 | MIT | Self-contained builds include the runtime |
| Avalonia (Avalonia, Desktop, Skia, HarfBuzz, Win32, X11, Native, FreeDesktop, AtSpi, Remote.Protocol, Themes.Fluent) | 12.1.1 | MIT | UI framework |
| Avalonia.Angle.Windows.Natives | 2.1.27548 | BSD-3-Clause (ANGLE) | Native OpenGL ES over Direct3D on Windows |
| SkiaSharp (+ native assets) | 3.119.4 | MIT; native Skia BSD-3-Clause | 2D graphics |
| HarfBuzzSharp (+ native assets) | 8.3.1.3 | MIT; native HarfBuzz "Old MIT" | Text shaping |
| MicroCom.Runtime | 0.11.6 | MIT | COM interop helper used by Avalonia |
| Tmds.DBus.Protocol | 0.94.1 | MIT | Linux desktop integration (D-Bus) |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | MVVM source generators |
| SSH.NET | 2026.0.0 | MIT | SFTP and SSH (ADR-17) |
| BouncyCastle.Cryptography | 2.7.0 | MIT | Cryptography used by SSH.NET |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | MIT | Logging interfaces used by SSH.NET |
| SharpCompress | 0.50.4 | MIT | Read-only 7z, RAR, xz, bzip2, and zstd (ADR-07, P8); Copyright (c) Adam Hathcock |
| LTRData.DiscUtils (Core, Streams, Iso9660, Udf) | 1.0.89 | MIT | Read-only ISO 9660 and UDF images (ADR-07, P8); DiscUtils by Kenneth Bell and contributors, maintained by LTR Data |
| LTRData.Extensions | 1.0.23 | MIT | Helpers used by DiscUtils |

Test-only (not shipped): sample archives from SharpCompress's test suite (MIT), listed in
`tests/FileCat.Core.Tests/TestData/Archives/README.md`.

Build-time only (not shipped): Avalonia.BuildServices (MIT) — its usage-statistics task is disabled in
`Directory.Build.targets`; Inno Setup (modified BSD) builds the installer.

No component is proprietary. No GPL code from Open Salamander, and no code or assets from
Total Commander or FAR Manager, are included; they served as behavioral references only.
The FileCat icon is original artwork (`eng/make-icon.ps1`).
