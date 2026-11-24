# E-REL-I03-PROVENANCE-RC2-20261124 — rc.2's 24 assets against the dependency inventory, and PDFium against its upstream release

2026-11-24. These are the [rc.2 checklist](../FILECAT_1_0_RC2_FQ_CHECKLIST.md)'s L7 items:
- **e4**, for REL-I03-PROVENANCE: tie the dependency inventory to the 24 assets.
- **e5**, for I611: check the PDFium binaries against the kept upstream release.

**Result:**
- **e5 is done.**
- **e4's tie is complete with no mismatch.** Two components have no disposition yet, so REL-I03-PROVENANCE's sealed composition (checklist d13) stays open.

## Method

- **The work:** a desk check of the downloaded, digest-verified assets in `E:\FileCat\.tmp\fq-rc2\assets\` against the tag's `licenses/dependencies/index.json`. The inventory's SHA-256 is `cdc1e89f…`; all 10 SBOMs record it as their notice index, and every package ships the file unchanged.
- **Handling the packages:** they were only unpacked, never run. Nothing was downloaded.
- **Scripts and outputs:** kept with the working copy under `E:\FileCat\.tmp\fq-rc2\l7\`. They are scratch, not retained evidence.
- **What I re-checked myself:** the x64 `pdfium.dll` in the portable ZIP. It is 5,812,016 bytes, SHA-256 `e3d1157c…`. With its 9,520-byte Authenticode block cut off and the PE checksum and security directory cleared, it is the upstream file: 5,802,496 bytes, `15df9ddd…`, byte for byte.

## e5 — PDFium

**The upstream release.** bblanchon/pdfium-binaries `chromium/7690` (`VERSION` 147.0.7690.0), four archives:
- **Where:** they were downloaded on 2026-11-02 to `.tmp`, so they were not retained. They are now kept in `V:\FileCat\artifacts\release-evidence\pdfium-upstream-chromium-7690\` with `SHA256SUMS`.
- **Pins:** each archive's hash equals the inventory's `AssetSHA256` pin.

| Platform | Shipped (SHA-256, bytes) | Upstream = I611 record = inventory pin = NuGet member | The difference |
|---|---|---|---|
| Windows x64 (portable, FDD, Setup's SBOM) | `e3d1157c38dc23bc450064cd3b9ab69cd738acb54ab5f5db52a1daa68038b800`, 5,812,016 | `15df9dddd81eddc5a177946aa5e34cda821ebc46a51440ecb607f91e99644895`, 5,802,496 | FileCat's Authenticode signature, 9,520 bytes appended, and the PE checksum it sets. |
| Windows ARM64 (the same three) | `4a7c513bab3111cf6d6bc4d06db22a7c35428bdde446df1e94a45124c61bcbb9`, 5,375,792 | `e13c75645d70b40a1857b90106a16c036ad951f17b389ec3d4671f235238bb89`, 5,366,272 | The same. |
| Linux x64 (tarball, .deb, AppImage) | `8100bc0812e7064d00e26e1d41fbbb618d52240ed730dc3bd0e8ca5ce5c54078` | the same | None: byte for byte. |
| macOS arm64 | `d1f7ae0c057c717be4367d606c4a26d679110f22a3da561f875baa28ff4378de`, 5,512,336 | `40720223c09c96b894faa4d6b22fb78a1675b160f10402a25b79d3e31763eba1`, 5,526,320 | The code signature. `codesign`'s ad-hoc signature (28,992 bytes) replaced the linker's ad-hoc one (42,976 bytes), and the two size fields that describe it changed. Everything before the signature is identical. |

**The rest of PDFium:**
- **SBOMs:** each package's SBOM lists the shipped hash.
- **Notices:** the 64 vendor notice files, 16 per archive, equal the inventory's entries and the tag's files, and every package ships them.
- **What it loads:** only system libraries on each platform.
- **Versions:** `Directory.Packages.props` and the App's lock file pin PDFtoImage 5.2.1 and bblanchon.PDFium 147.0.7690, with the inventory's content hashes. Each package's `FileCat.deps.json` agrees.
- **Wording:** the I611 record and the inventory's README speak of the "NuGet/publish inputs". That is true only before signing; the table above gives the shipped hashes.

## e4 — the inventory against the 24 assets

**What matches:**
- **The assets:** all 21 manifest entries match in size and SHA-256, and the 3 producer manifests match the qualification record.
- **The payloads:** all 8 package payloads match their SBOMs in every name, size, SHA-1 and SHA-256, and in the recomputed tree hash and verification code. The macOS ZIP also holds 225 `._*` entries, archive metadata with code signatures.
- **Setup against portable:** each Setup's SBOM is its portable ZIP's, minus the `FileCat.portable` marker, plus the 4 `FileCat.PrivilegedHost` files and the Inno Setup license. The ARM64 Setup is attested by its SBOM only.
- **The NuGet packages:** each payload's packages (33 on Windows, 32 on Linux and macOS, plus a runtime pack when self-contained) are in the inventory, with the right version and content hash. The SBOMs' package lists match exactly: name, version, archive hash, license and source. All 48 local NuGet archives match the inventory.
- **Every file the dependency manifests name traces back to its archive, in one of three ways:**
  - byte for byte;
  - the same apart from a signature: `pdfium.dll`, `Avalonia.dll` and `Avalonia.Metal.dll` on Windows, and the native libraries on macOS;
  - compiled ahead of time from the same assembly, matched by module ID.
- **Notices:** `licenses/dependencies` ships complete in every payload, 125 of 125 files equal to the tag.

**In packages but without an inventory entry:**
1. **The .NET apphost** (`Microsoft.NETCore.App.Host.<rid>` 10.0.14). It ships as `FileCat.exe` and `FileCat.ShellHost.exe` on Windows, and as `FileCat` on Linux and macOS.
   - Linux: the 10.0.14 template byte for byte, with `FileCat.dll` written in.
   - macOS: the same apart from the signature.
   - Windows: the code sections are identical.
   - Its MIT text ships, but neither the inventory nor any SBOM lists it as a component.
2. **WebView2's loader and XML documentation** (`runtimes/<rid>/native/WebView2Loader.dll`, `Microsoft.Web.WebView2.Core.xml`).
   - They equal Microsoft.Web.WebView2 1.0.3179.45's archive members, but the dependency manifests do not name them, so the SBOM tool's archive check does not cover them.
   - They also ship in the Linux and macOS packages, together with `Microsoft.Web.WebView2.Core.dll`, though only Windows uses them.

**Covered by their own inventories:**
- **The AppImage runtime:** the same as `eng/linux/appimage-runtime/runtime-x86_64` apart from its digest field, and its source asset matches its pin.
- **Not checked here:**
  - The Inno Setup loader and uninstaller inside the Setups. Their header reads 6.7.0 against the pinned 6.7.1 compiler, possibly a format version.
  - FileCat's own SharpCompress, SSH.NET, XADMaster and rardecode code, which `licenses/*` and THIRD-PARTY-NOTICES.md describe.

**In the inventory but in no package:** build-only and other-platform entries, as the inventory's scope line allows:
- Avalonia.BuildServices;
- the WebAssembly native assets;
- SkiaSharp.NativeAssets.Linux.NoDependencies (THIRD-PARTY-NOTICES.md calls it an "Additional Linux native asset", but Linux ships `libSkiaSharp.so` from SkiaSharp.NativeAssets.Linux);
- the linux-arm64 runtime pack.

The inventory's own flags for a full artifact SBOM, complete native notices and OSI eligibility are still false.

## Stop

- **e5:** stops here. I611's record should cite the shipped hashes above and the retained upstream archives when the candidate's records are sealed.
- **e4:** stops when two dispositions are recorded: the apphost (inventory or SBOM entry, or an explicit exclusion), and WebView2's undeclared files (declared, or left out of the non-Windows packages). REL-I03-PROVENANCE then closes with the sealed composition (checklist d13).
