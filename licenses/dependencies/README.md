# Retained dependency license and notice texts

These files preserve exact license and notice bytes from FileCat's pinned NuGet
packages or from the immutable upstream revisions declared in those packages.
`index.json` maps each package to its retained texts, source identity and hashes.
It includes build-only packages and RID alternatives from the resolved App graph;
their presence here does not mean their code is shipped in every FileCat package.

The index explicitly records missing full texts and unresolved provenance. In
particular, LTRData.Extensions declares MIT but has no retained full license text;
its declared source tree contains no license-named file. SharpCompress's RAR
implementation still requires a provenance determination beyond its root MIT
license. The exact .NET 10.0.12 runtime pack license and notices are also retained for
the five existing publish RIDs. Native subcomponents, runtime composition,
AppImage and installer obligations still require their own artifact-specific audit. This snapshot does not certify
complete notices or OSI-only eligibility.
