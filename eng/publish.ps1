<#
.SYNOPSIS
  Builds FileCat release payloads (plan §19.3).
.DESCRIPTION
  Produces, under artifacts/:
    publish/<rid>/                      self-contained ReadyToRun build (input for the installer)
    FileCat-<ver>-<rid>-portable.zip    same build plus the FileCat.portable marker (state beside the exe)
    FileCat-<ver>-<rid>-fdd.zip         framework-dependent build (receives .NET servicing independently)
    sbom-<ver>.json                     package inventory (dotnet list --include-transitive)
  Signing is a release-infrastructure step (SignPath Foundation, ADR-15) applied to these outputs in CI.
#>
param(
    [string]$Version = "0.1.0-preview",
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root "artifacts"
$project = Join-Path $root "src/FileCat.App/FileCat.App.csproj"
$publish = Join-Path $artifacts "publish/$Runtime"
$fdd = Join-Path $artifacts "publish/$Runtime-fdd"
$versionPrefix = ($Version -split '-')[0]

Remove-Item -Recurse -Force $publish, $fdd -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

Write-Host "Publishing self-contained $Runtime ($Version)"
dotnet publish $project -c $Configuration -r $Runtime --self-contained true -p:PublishReadyToRun=true `
    -p:Version=$Version -p:VersionPrefix=$versionPrefix -p:DebugType=embedded -o $publish
if ($LASTEXITCODE -ne 0) { throw "self-contained publish failed" }

Write-Host "Publishing framework-dependent $Runtime"
dotnet publish $project -c $Configuration -r $Runtime --self-contained false -p:PublishReadyToRun=true `
    -p:Version=$Version -p:VersionPrefix=$versionPrefix -p:DebugType=embedded -o $fdd
if ($LASTEXITCODE -ne 0) { throw "framework-dependent publish failed" }

# The per-plan administrator helper (ADR-14) sits beside FileCat.exe; it runs only from Program Files.
$broker = Join-Path $root "src/FileCat.PrivilegedHost/FileCat.PrivilegedHost.csproj"
dotnet publish $broker -c $Configuration -r $Runtime --self-contained true -p:PublishReadyToRun=true `
    -p:Version=$Version -p:VersionPrefix=$versionPrefix -p:DebugType=embedded -o $publish
if ($LASTEXITCODE -ne 0) { throw "administrator helper publish failed" }
dotnet publish $broker -c $Configuration -r $Runtime --self-contained false -p:PublishReadyToRun=true `
    -p:Version=$Version -p:VersionPrefix=$versionPrefix -p:DebugType=embedded -o $fdd
if ($LASTEXITCODE -ne 0) { throw "framework-dependent administrator helper publish failed" }

# The Shell helper (TV-16) runs Windows thumbnail and icon handlers outside FileCat, at low integrity in a job object.
$shellHost = Join-Path $root "src/FileCat.ShellHost/FileCat.ShellHost.csproj"
dotnet publish $shellHost -c $Configuration -r $Runtime --self-contained true -p:PublishReadyToRun=true `
    -p:Version=$Version -p:VersionPrefix=$versionPrefix -p:DebugType=embedded -o $publish
if ($LASTEXITCODE -ne 0) { throw "Shell helper publish failed" }
dotnet publish $shellHost -c $Configuration -r $Runtime --self-contained false -p:PublishReadyToRun=true `
    -p:Version=$Version -p:VersionPrefix=$versionPrefix -p:DebugType=embedded -o $fdd
if ($LASTEXITCODE -ne 0) { throw "framework-dependent Shell helper publish failed" }

foreach ($dir in @($publish, $fdd)) {
    Copy-Item (Join-Path $root "LICENSE") $dir -Force
    Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") $dir -Force
}

# Portable ZIP: same binaries plus the marker that keeps settings and data beside the executable.
$portableStage = Join-Path $artifacts "stage-portable"
Remove-Item -Recurse -Force $portableStage -ErrorAction SilentlyContinue
Copy-Item -Recurse $publish $portableStage
New-Item -ItemType File -Path (Join-Path $portableStage "FileCat.portable") -Force | Out-Null
# Portable mode has no administrator retry: a helper in a user-writable folder could be replaced (ADR-14).
Remove-Item (Join-Path $portableStage "FileCat.PrivilegedHost.*") -Force
$portableZip = Join-Path $artifacts "FileCat-$Version-$Runtime-portable.zip"
Remove-Item $portableZip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $portableStage "*") -DestinationPath $portableZip
Remove-Item -Recurse -Force $portableStage

$fddZip = Join-Path $artifacts "FileCat-$Version-$Runtime-fdd.zip"
Remove-Item $fddZip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $fdd "*") -DestinationPath $fddZip

# Package inventory for the release SBOM and license gate (every shipped component must be OSI-licensed).
$sbom = Join-Path $artifacts "sbom-$Version.json"
dotnet list $project package --include-transitive --format json | Out-File -Encoding utf8 $sbom

Write-Host "Done:"
Get-ChildItem $artifacts -File | ForEach-Object { "  " + $_.Name + "  " + [math]::Round($_.Length / 1MB, 1) + " MB" }
