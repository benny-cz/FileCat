# Third-party notices

FileCat is MIT-licensed (see `LICENSE`). The table records its dependency license declarations.
Full license and notice texts retained from pinned packages and their declared source revisions
are in `licenses/dependencies`, with exact source identities and hashes in `index.json`.
The snapshot includes the pinned .NET 10.0.14 runtime pack texts, build-only packages and RID
alternatives; it does not establish which components ship in each artifact or complete native
license eligibility. AppImage/native obligations remain under review before stable release.
FileCat builds SharpCompress from source without its UnRAR-derived RAR and PPMd code and ships it as
`FileCat.SharpCompress.dll`; `licenses/sharpcompress` (its component inventory `COMPONENTS.md`, `NOTICE`
and full license texts) ships with every package. Regenerate the exact
SPDX inventory whenever dependencies or the SDK change: `eng/publish.ps1` writes the Windows
`portable`, `fdd`, and `setup-payload` manifests; `eng/package-linux.sh` writes the Linux `tar`,
`deb`, and `appimage` manifests; and `eng/package-macos.sh` writes the macOS `macos-app` manifest.
Their filenames are `artifacts/sbom-<version>-<rid>-<artifact-kind>.spdx.json`. Review every
manifest together with the retained notice snapshot.
The Linux AppImage also includes the wrapper texts in `licenses/appimage-runtime`.
Its index pins the runtime input, which FileCat keeps in `eng/linux/appimage-runtime`,
and the source archives of these texts. That runtime statically links libfuse
(LGPL-2.1), so every AppImage release also publishes
`FileCat-<version>-linux-x64-appimage-runtime-source.tar.gz`: the exact libfuse,
squashfuse and type2-runtime sources, a README and `rebuild.sh`, which rebuilds
the runtime and relinks the AppImage with a modified libfuse.

| Component | Version | License | Notes |
|---|---|---|---|
| .NET runtime and libraries | 10.0.14 | MIT | Self-contained builds include the pinned runtime; framework-dependent builds use the separately serviced runtime |
| Avalonia (Avalonia, Desktop, Skia, HarfBuzz, Win32, X11, Native, FreeDesktop, AtSpi, Remote.Protocol, Themes.Fluent) | 12.1.4 | MIT | UI framework |
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
| SharpCompress (FileCat's build, `FileCat.SharpCompress.dll`) | 0.50.5 (commit e4193af8) | MIT AND Apache-2.0 AND MS-PL AND BSD-2-Clause AND BSD-3-Clause AND Zlib AND Unlicense AND Info-ZIP | Read-only 7z, xz, bzip2, and zstd (ADR-07, P8); Copyright (c) Adam Hathcock and SharpCompress contributors. Built from `src/ThirdParty/SharpCompress` without its RAR support and PPMd variant H, which it ported from UnRAR; 7z PPMd decodes through FileCat's PPMd model (rardecode row). SharpCompress itself is MIT; its parts from other projects keep their own licenses, listed file by file in `licenses/sharpcompress/COMPONENTS.md`, with `NOTICE` and the full texts, shipped with FileCat. Its ZIP implode and reduce decoders (unused by FileCat) derive from Info-ZIP code, whose license is not OSI-approved: an open release decision |
| rardecode (ported source in FileCat's RAR reader) | v2.4.5 | BSD-2-Clause | `src/FileCat.Archives/Rar` ports its RAR decoding, and `src/ThirdParty/SharpCompress/Compressors/PPMd/PpmdModelH.cs` its PPMd variant H model, which RAR 3 and 7z PPMd share; Copyright (c) 2015 Nicholas Waples. Full text below and in `licenses/rardecode/LICENSE`; no UnRAR source |
| XADMaster (ported source in FileCat's RAR 1.5 decoder) | commit 7cb9ee0a | LGPL-2.1-or-later | `src/FileCat.Archives.Rar15` ports its RAR 1.5 decoding (XADRAR15Handle) into the separate, replaceable `FileCat.Archives.Rar15.dll`; Copyright (c) 2017-present MacPaw Way Ltd., originally by Dag Ågren. Notice below; full text in `licenses/xadmaster/LICENSE`, shipped with FileCat; no UnRAR source |
| LTRData.DiscUtils (Core, Streams, Iso9660, Udf) | 1.0.89 | MIT | Read-only ISO 9660 and UDF images (ADR-07, P8); DiscUtils by Kenneth Bell and contributors, maintained by LTR Data |
| LTRData.Extensions | 1.0.23 | MIT | Helpers used by DiscUtils; the signed package's SPDX expression and copyright metadata are bound to retained canonical MIT text |
| AppImage type2-runtime (Linux AppImage only) | commit fedfad51; upstream GitHub asset 657374641, kept in `eng/linux/appimage-runtime` and pinned by checksum | MIT root; libfuse LGPL-2.1; other linked components have their own licenses | The official producer log binds Alpine 3.21 digest `ce6475…3507`, libfuse 3.15.0, squashfuse 0.5.2, musl 1.2.5-r11, zstd 1.5.6-r2, zlib 1.3.2-r1 and mimalloc 2.1.7-r0. Original source license texts are retained in `licenses/appimage-runtime`. The runtime's corresponding source, with a rebuild and relink script, is published with every AppImage release as `FileCat-<version>-linux-x64-appimage-runtime-source.tar.gz`. The tarball and Debian package omit this wrapper |
| Inno Setup installer engine, loader and uninstaller (Windows setup package only) | 6.7.1 | Inno Setup license (modified BSD) | Generated installer bytes; Copyright (C) 1997-2026 Jordan Russell, portions Copyright (C) 2000-2026 Martijn Laan. Full license installed as `licenses/InnoSetup-6.7.1.txt`; compiler inputs pinned in `eng/toolchains/inno-setup.json` |

Test-only (not shipped): sample archives from SharpCompress's test suite (MIT), listed in
`tests/FileCat.Core.Tests/TestData/Archives/README.md`; the SharpCompress 0.50.5 package (MIT; its RAR and PPMd
variant H decoders are ported from UnRAR) as the reference reader FileCat's RAR and 7z PPMd decoding is compared
with; pyftpdlib (MIT) as the FTP/FTPS server in tests; disk images
made by FileCat's own `eng/make-recovery-fixtures.sh` (generated text only; `TestData/Recovery/README.md`).

Build-time only (not shipped): Avalonia.BuildServices (MIT) — its usage-statistics task is disabled in
`Directory.Build.targets`; appimagetool 1.9.1 (MIT, pinned by
checksum in `eng/package-linux.sh`) builds the AppImage.

No code from Open Salamander, and no code or assets from Total Commander or FAR Manager,
are included; they served as behavioral references only.
The FileCat icon is original artwork (`eng/make-icon.ps1`).

## rardecode (BSD-2-Clause)

FileCat's RAR reader, and the PPMd variant H model in `FileCat.SharpCompress.dll` that RAR 3 and 7z PPMd
share, contain code ported from rardecode (https://github.com/nwaples/rardecode).

```
Copyright (c) 2015, Nicholas Waples
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## XADMaster (LGPL-2.1-or-later)

FileCat's RAR 1.5 decompression, the separate assembly `FileCat.Archives.Rar15.dll`, is a C# port of
code from XADMaster (https://github.com/MacPaw/XADMaster, commit
7cb9ee0abbb163f261e4cb74501e15067032319c), The Unarchiver's archive library.

```
Copyright (c) 2017-present, MacPaw Way Ltd. All rights reserved.
XADMaster was originally written by Dag Ågren.

This library is free software; you can redistribute it and/or
modify it under the terms of the GNU Lesser General Public
License as published by the Free Software Foundation; either
version 2.1 of the License, or (at your option) any later version.

This library is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
Lesser General Public License for more details.
```

The full license text is `licenses/xadmaster/LICENSE`, which FileCat's packages ship with this file.
The library's complete corresponding source is `src/FileCat.Archives.Rar15` in FileCat's public
source repository; you may modify it and replace the DLL with your own build
(`licenses/xadmaster/README.md`).
