# Retained dependency license and notice texts

These files preserve exact license and notice bytes from FileCat's pinned NuGet
packages or from the immutable upstream revisions declared in those packages.
`index.json` maps each package to its retained texts, source identity and hashes.
The `PackageInputAuditSHA256` and `UpstreamTextAuditSHA256` fields identify the
original retained-text audit. Later package additions have their own exact
archive hashes, NuGet content hashes, and declared source revisions in the
indexed package and file entries. `System.CodeDom` and `System.Management`
10.0.0 retain their NuGet third-party notices and the MIT license bytes from
their package-declared `dotnet/dotnet` source revision.
It includes build-only packages and RID alternatives from the resolved App graph;
their presence here does not mean their code is shipped in every FileCat package.
A package whose build targets reference an assembly directly, which a published dependency
manifest then lists as a `reference` rather than under the package, names it in
`ReferenceComponents` with the package file it comes from: Microsoft.Web.WebView2's
`Microsoft.Web.WebView2.Core.dll`. The artifact SBOM tool (`eng/ArtifactSbom`) attributes a
reference to a package only through that list, and checks each file publishing takes from a
restored package folder against the member of the reviewed archive.
A package whose build targets copy files into the app folder that no dependency manifest
names lists them in `UnlistedPayloadFiles`, each with the package file it is, its size and
SHA-256 and what copies it: Microsoft.Web.WebView2's `Microsoft.Web.WebView2.Core.xml`
(MSBuild copies a reference's documentation beside it) and its x64 and arm64
`runtimes/<rid>/native/WebView2Loader.dll` (content items its build targets add for every
RID, Linux and macOS included). The SBOM tool requires each such payload file to be that
member byte for byte. It refuses any other payload file that is a reviewed archive's member
byte for byte, unless a dependency manifest names it or it is a license or notice text
retained here.

The index explicitly records unresolved provenance. LTRData.Extensions 1.0.23 has
an author- and repository-signed NuGet archive whose metadata declares the SPDX
`MIT` expression and package copyright. The snapshot retains byte-identical MIT
reference text from an exact `spdx/license-list-data` revision, as prescribed for
NuGet license expressions. Its declared source tree still has no license-named file,
so this does not claim an upstream source-tree license file or reproducible binary.
SharpCompress is no longer a package here: FileCat ships its own build of it without the
UnRAR-derived RAR and PPMd code (`src/ThirdParty/SharpCompress`), whose license texts and
component inventory are in `licenses/sharpcompress`; the package is a test-only reference reader.
FileCat also builds SSH.NET from `src/ThirdParty/SshNet`. The upstream license and
notice remain indexed as source texts. The shipped project names its MIT and ISC
license texts in `licenses/sshnet` and in the artifact SBOM.
The exact .NET 10.0.14 runtime pack license and notices are also retained for
the five existing publish RIDs. `AppHostPacks` pins the matching apphost packs
(`Microsoft.NETCore.App.Host.<rid>` 10.0.14) for the same RIDs: their archive hashes and
signatures, and the path, size and SHA-256 of the launcher template that every package's
`FileCat.exe` and `FileCat.ShellHost.exe` (Windows) or `FileCat` (Linux, macOS) is made from.
Their `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` equal the runtime pack's retained texts
byte for byte, so their `RetainedNoticePaths` name those. The SBOM tool lists the pack in
every package's SBOM and checks how each launcher's bytes relate to the template.
Native subcomponents, runtime composition,
AppImage and installer obligations still require their own artifact-specific audit. This snapshot does not certify
complete notices or OSI-only eligibility.

The PDFtoImage 5.2.1 MIT text is retained from its package-declared source revision.
The bblanchon.PDFium 147.0.7690 NuGet packages declare Apache-2.0 but carry no
license or notice members. The snapshot retains their declared source repository's
MIT text and the exact `chromium/7690` upstream release archive's `LICENSE` and
15 `licenses/` members for each of Windows x64, Windows ARM64, Linux x64 and
macOS ARM64. The archive SHA-256 values are pinned in `index.json`; the four
native library hashes independently match the corresponding NuGet/publish inputs.
This establishes which vendor-supplied notice set accompanies each shipped PDFium
binary. It does not prove a reproducible build or audit every linked native
component independently. SkiaSharp's extra Linux no-dependencies package retains
the two license/notice members from its exact NuGet archive.
