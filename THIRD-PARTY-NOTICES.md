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

Build-time only (not shipped): Avalonia.BuildServices (MIT) — its usage-statistics task is disabled in
`Directory.Build.targets`; Inno Setup (modified BSD) builds the installer.

No component is proprietary. No GPL code from Open Salamander, and no code or assets from
Total Commander or FAR Manager, are included; they served as behavioral references only.
The FileCat icon is original artwork (`eng/make-icon.ps1`).
