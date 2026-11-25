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

## rc.3 dispositions

2026-11-24, branch `fix/l7-apphost-webview2-provenance`. Both open components now have a disposition in the tooling, so rc.3's SBOMs and inventory describe them. Product code is unchanged. rc.3's own assets still have to show it (last section).

### 1. The .NET apphost: an inventory entry, and a component of every SBOM

**Inventory.** `licenses/dependencies/index.json` gains `AppHostPacks`: one `Microsoft.NETCore.App.Host.<rid>` 10.0.14 pack per publish RID, as `RuntimePacks` has one runtime pack per RID. This fits the scope line, the resolved App graph with its RID alternatives: restore downloads these packs, or the SDK takes them from its own packs folder.
- **Each entry** pins the archive's SHA-256 and SHA-512 and the launcher template's path, size and SHA-256. `dotnet nuget verify --all` accepted all five archives (author: Microsoft, `9a1b131b…`; repository: nuget.org, `1f4b311d…`).
- **Texts:** the packs' `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` equal the runtime pack's retained texts byte for byte. So each entry names those, and no new notice file ships.

| RID | Archive SHA-256 | Template | Bytes | Template SHA-256 |
|---|---|---|---|---|
| win-x64 | `2a4547b0e6cf0dd15e723531daa5450d99d4e151105aec0d31f2b0c7cfb286db` | `runtimes/win-x64/native/apphost.exe` | 160,768 | `dbd60c6dca3629e09c1869806222878b6e7adb8ae4aecb64aaf38b7fc9e49b87` |
| win-arm64 | `57550b78a749ed9657e181ab03bd86f898de6ad0c6059d8308603f307cd31207` | `runtimes/win-arm64/native/apphost.exe` | 138,240 | `82b60e5f7410472823224564ab6955ed8c699e421c93f1f54a0a44d6115d0ff7` |
| linux-x64 | `61cf559c5ac8d26b1d140945c40419886d6d288075c330ede9e1c1e79e0abd91` | `runtimes/linux-x64/native/apphost` | 78,192 | `b9e93e08175583f7fa0be753decf8ed0c54953cccb308bb27d0c3a11bdb5c5db` |
| linux-arm64 | `80f6d86761a27190c0b1ad70923d002bd0277f949731550f95c3ef86606d1f4f` | `runtimes/linux-arm64/native/apphost` | 77,480 | `a2c6d05f6c6bc2e099dcaab60164e073a41d8c7b4398dabe04f9b72943d1b6a0` |
| osx-arm64 | `026be16e81c2190a764306d4d0e3e798a4d18d637230206a977ee0b404ebf4e1` | `runtimes/osx-arm64/native/apphost` | 158,704 | `6255480c40c12f63363349197ab2e766239302d6a49f04d92826614a6313ff19` |

**SBOMs.** `eng/ArtifactSbom` (new `AppHost.cs`) finds every payload file that carries the apphost's binding check: the two halves of the placeholder the SDK overwrites, which survive binding. On rc.2's payloads these are exactly `FileCat.exe` and `FileCat.ShellHost.exe` on Windows and `FileCat` on Linux and macOS. The macOS archive-worker launcher does not carry it, nor does the native `FileCat.PrivilegedHost.exe` of a local setup payload (below). Each such file must be a reviewed template of the payload's RID, bound to the DLL beside it:
- **Linux:** byte for byte the template with `FileCat.dll` written in.
- **macOS:** byte for byte the bound template up to the code signature. An ad-hoc signature replaces Microsoft's (the template's CodeDirectory has the hardened-runtime flag and a 9,020-byte CMS blob), and only the three load-command fields that size it change. Nothing follows the signature.
- **Windows:** every section of the template unchanged apart from the name. The headers are unchanged apart from the timestamp, section count, sizes, checksum, GUI subsystem and resource and certificate directories. One `.rsrc` section is added, holding exactly the app DLL's Win32 resources (rc.2: the same 10 resources as `FileCat.dll` or `FileCat.ShellHost.dll`). Once signed, one Authenticode signature follows the sections, and nothing else does.
  - The resource update rewrites the COFF timestamp: rc.2's launchers have the template's with its low 16 bits cleared (`0x6adaf2b3` becomes `0x6ada0000` on x64).

What the SBOM records:
- **The component:** the reviewed pack whose template the launchers match, with its version, MIT, its archive hash and its purl. FileCat DEPENDS_ON it.
- **The launchers:** each is GENERATED_FROM the pack, and its file comment says how its bytes relate to the template.
- **Self-contained payloads:** the apphost must be the runtime pack's .NET release.
- **The SDK's packs folder:** the SDK takes a pack from there when it has one, and restore then leaves no archive. In that case the template there must have the pinned SHA-256. When the archive is restored, the tool checks it, the restored template, and the retained texts as its members.

**Notices.** THIRD-PARTY-NOTICES.md gains a ".NET application host" row. `eng/validation/validate-dependency-notice-manifest.py` requires that row and one apphost pack per RID at the serviced runtime's version, with the runtime pack's texts.

### 2. WebView2's undeclared files: declared, not removed

**Declared.** The WebView2 entry gains `UnlistedPayloadFiles`. Each entry names the payload path, the archive member, its size and SHA-256, and what copies it:
- **`Microsoft.Web.WebView2.Core.xml`:** `lib_manual/netcoreapp3.0/Microsoft.Web.WebView2.Core.xml`. MSBuild copies a reference's documentation beside it, and the package's build targets add the Core reference.
- **`runtimes/win-x64/native/WebView2Loader.dll` and `runtimes/win-arm64/native/WebView2Loader.dll`:** content items of the package's `build/Common.targets`, picked by the target's processor whatever the RID. So the Linux x64 packages carry the x64 loader and macOS the arm64 one.

The SBOM tool requires each such payload file to be that member byte for byte, checks the restored copy as well, and records the file as EXPANDED_FROM_ARCHIVE the package.

**The Core DLL on Linux and macOS was already declared.** Their dependency manifests list `Microsoft.Web.WebView2.Core` as a `reference`. The index's `ReferenceComponents` attributes it to the package, and the tool checks its restored file against the archive. Every rc.2 Linux and macOS SBOM shows this.

**Why not removed.** Removing the files would change the packages, and nothing proves that Linux and macOS never load the Core DLL:
- **The code can load it:** `PageEngines.Create` (`src/FileCat.App/Controls/PageView.cs`) calls `WebView2Page.RuntimeVersion` in a branch only Windows takes. But the JIT can resolve `WebView2Page` when it compiles that method at run time; tiering recompiles hot ReadyToRun methods. Its `CoreWebView2ProcessFailedKind?` field is a WebView2 value type, so laying the class out needs the Core DLL. What happens without the file depends on JIT and tiering details that neither the source nor the manifests settle.
- **The host would not stop it:** a win-x64 framework-dependent copy without the Core DLL still starts and prints its version (`dotnet FileCat.dll --version`, checked below). So a missing file would matter only at run time, in the page view.
- **Removal is a build change:** the Linux and macOS manifests name the Core DLL as an app asset, so a clean removal means a per-RID change to the build's dependency graph and different dependency manifests.
- **Keeping them is inert:** the XML is documentation, and the loader is a Windows DLL that only `CoreWebView2Environment` loads, on Windows.

**A guard for the future.** The SBOM tool now refuses any payload file that is a member of a reviewed archive byte for byte, unless one of these declares it:
- a dependency manifest names it;
- the index lists it in `UnlistedPayloadFiles`;
- it is made from the apphost;
- it is a license or notice text the index retains, wherever it sits. For example, `licenses/sharpcompress/upstream/dotnet-LICENSE.TXT` is .NET's `LICENSE.TXT`.

Empty files are skipped. The archives searched are the payload's packages, its runtime pack, its apphost pack, and the App graph's other reviewed packages restored on the machine, build-only ones included.

### 3. Wording

THIRD-PARTY-NOTICES.md's SkiaSharp.NativeAssets.Linux.NoDependencies row now says that no FileCat package ships its files. The Linux packages ship `libSkiaSharp.so` from SkiaSharp.NativeAssets.Linux 3.119.4: rc.2's tarball copy is that member byte for byte (`66c856ea…`, 11,170,296 bytes), and the Linux dependency manifest does not list the NoDependencies package. `licenses/dependencies/README.md` describes `AppHostPacks` and `UnlistedPayloadFiles`.

### Checked on the development PC

| What | Result |
|---|---|
| rc.2's 8 payloads, unpacked from the digest-verified assets, each holding the new index as rc.3's packages will (the deb from its data tar and the AppImage from the desk check's tree, both without their symbolic links) | All 8 SBOMs written. Their files are rc.2's SBOMs' files, name and SHA-256, apart from the notice index and what these trees lack (the deb's control files, the AppImage's `.DirIcon` and runtime). Each has one more package, the apphost pack. Windows: 2 launchers each; Linux and macOS: 1; all from the 10.0.14 template. 2 WebView2 files are declared in each. No other payload file is a member of the 44 or 45 reviewed archives searched. |
| The same payloads (Windows FDD, Linux tar, macOS) with altered indexes | Refused, each for its own reason: without WebView2's declarations (it names the loader copy); without `AppHostPacks` (it names the launcher); with a wrong template pin; and with rc.2's own index. |
| A fresh unsigned win-x64 publish: `eng/publish.ps1 -Version 1.0.0-dev.l7apphost -ArtifactDirectory <new folder>`, at below-normal priority without build servers | Setup payload 401 files, portable 397, FDD 209. Each SBOM lists `Microsoft.NETCore.App.Host.win-x64` 10.0.14, with `FileCat.exe` and `FileCat.ShellHost.exe` GENERATED_FROM it (unsigned, so no signature in the relation), and declares WebView2's two copies. The setup payload's `FileCat.PrivilegedHost.exe`, FileCat's own native bootstrap, is not taken for an apphost. |
| That publish's FDD folder copied without `Microsoft.Web.WebView2.Core.dll`, run as `dotnet FileCat.dll --version` (the console host, so no window) | Prints `FileCat 1.0.0-dev.l7apphost`, exit 0. |
| `eng/validation/validate-artifact-sbom.py` | 59/59 controls (19 allowed, 40 refused; 61 on Linux and macOS with the symbolic-link cases), 26 of them new. Allowed: PE, ELF and Mach-O launchers, signed and unsigned, and a template from the SDK's packs folder. Refused: launchers changed in code, resources, trailing bytes, signature or name; an unreviewed pack or pin; retained texts that are not the pack's; an apphost of another release than the runtime. Also new: unlisted files declared, absent, undeclared, changed or mis-pinned; a runtime pack member copied undeclared; license texts that are and are not retained; an empty file. |
| `eng/validation/validate-dependency-notice-manifest.py` | 77/77. |

### Left for the tagged rc.3 run

- **The package jobs:** they run only on tags. They build the real SBOMs: Windows through the signing job (signed launchers), Linux on ubuntu-24.04 and macOS on macos-26. This PC checked the tool on rc.2's bytes from those jobs, not on a new Linux or macOS build.
- **Where the template comes from:** whether a runner's SDK has the 10.0.14 apphost pack in its packs folder or restores it is decided there. Both paths are covered.
- **The tie:** redo e4's tie against rc.3's 24 assets. Every SBOM should list the apphost pack, and WebView2's files should be EXPANDED_FROM_ARCHIVE. `eng/package-macos.sh` needs no change: its SBOM call is the same.
