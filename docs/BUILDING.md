# Building FileCat from source

This guide is for contributors and local development. The [README](../README.md)
has the short start and product overview. A local package is not an official,
signed or release-qualified artifact.

## Start with the pinned SDK

Install the .NET SDK selected by [`global.json`](../global.json). No paid
component or account is required for the ordinary source build.

```sh
dotnet build FileCat.slnx
dotnet test FileCat.slnx
dotnet run --project src/FileCat.App
```

Pass a folder after `--`, or use `--left PATH --right PATH`. `--profile NAME`
chooses an independent saved profile. `--data FOLDER` keeps every FileCat
write, including settings, history, logs, journals, caches and scratch, in
that folder. For recovery from a source disk, place `--data` on another disk.

## Make local packages

The package scripts write beneath `artifacts/` by default. Use a distinct
local version for experiments so the output cannot be confused with the
official preview or a future candidate.

| Host | Command | Output |
|---|---|---|
| Windows, PowerShell 7 | `pwsh ./eng/publish.ps1 -Version 1.0.0-local.1 -Runtime win-x64` | Self-contained payload, portable and framework-dependent ZIPs, and their SPDX 2.3 SBOMs |
| Ubuntu x64 | `./eng/package-linux.sh 1.0.0-local.1 linux-x64` | `.tar.gz`, `.deb`, AppImage, AppImage runtime source bundle, and SBOMs |
| Apple silicon Mac | `./eng/package-macos.sh 1.0.0-local.1` | Ad-hoc-signed `.app` ZIP and SBOM |

The Windows per-machine installer is assembled from a verified published
payload with the pinned Inno Setup compiler through
[`eng/Compile-WindowsInstaller.ps1`](../eng/Compile-WindowsInstaller.ps1).
Its recipe is [`eng/installer/FileCat.iss`](../eng/installer/FileCat.iss).
Official packaging also records the outer asset hashes in a platform manifest;
the SBOMs enumerate the bytes inside each distinct payload and their reviewed
NuGet/runtime identities.

The portable Windows ZIP includes `FileCat.portable`: state goes in `Data/`
beside the executable. The framework-dependent ZIP requires the appropriate
.NET desktop runtime and contains no administrator helper. Installed builds
have the separately qualified helper route.

## Update dependencies deliberately

Normal builds and publishes restore from the tracked `packages.lock.json`
files in locked mode. A missing lock, changed request or changed package
content fails. To update dependencies, edit
[`Directory.Packages.props`](../Directory.Packages.props), regenerate every
tracked project lock, and review the complete diff:

```powershell
git ls-files '*.csproj' | ForEach-Object {
    dotnet restore $_ --force-evaluate -p:FileCatUpdateDependencyLocks=true
    if ($LASTEXITCODE -ne 0) { throw "Dependency update failed: $_" }
}
```

For a separate RID restore, use
`dotnet restore -p:RuntimeIdentifier=win-x64`. `restore --runtime`
narrows the declared multi-RID graph and is rejected. The Linux ARM64 graph
retains packaging-script compatibility; this does not establish stable
Linux ARM64 platform support.

Packaging checks the frozen notice snapshot in
[`licenses/dependencies/index.json`](../licenses/dependencies/index.json)
against the App lock and published runtime version before copying full texts.
When changing packages or the SDK, update that snapshot from verified source
bytes and resolve its recorded provenance gaps.

The AppImage embeds the reviewed runtime in
[`eng/linux/appimage-runtime`](../eng/linux/appimage-runtime) without
downloading another. Packaging checks its pin and
[`licenses/appimage-runtime/index.json`](../licenses/appimage-runtime/index.json),
copies the wrapper notices, verifies that the AppImage starts with the pinned
runtime and writes the corresponding source bundle
`FileCat-<version>-linux-x64-appimage-runtime-source.tar.gz`. `APPIMAGE_RUNTIME`
may only select another copy of the same bytes. Update the runtime, pins,
source bundle and notice snapshot together.

The [release dashboard](release/1.0.0/FILECAT_1_0_RELEASE_EXECUTION_REPORT.md)
is the source for candidate qualification and remaining publication gates.
