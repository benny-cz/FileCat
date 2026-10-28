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

The index explicitly records unresolved provenance. LTRData.Extensions 1.0.23 has
an author- and repository-signed NuGet archive whose metadata declares the SPDX
`MIT` expression and package copyright. The snapshot retains byte-identical MIT
reference text from an exact `spdx/license-list-data` revision, as prescribed for
NuGet license expressions. Its declared source tree still has no license-named file,
so this does not claim an upstream source-tree license file or reproducible binary.
SharpCompress's RAR implementation still requires a provenance determination beyond
its root MIT license. The exact .NET 10.0.14 runtime pack license and notices are also retained for
the five existing publish RIDs. Native subcomponents, runtime composition,
AppImage and installer obligations still require their own artifact-specific audit. This snapshot does not certify
complete notices or OSI-only eligibility.
