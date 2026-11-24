<# Selects exact package outputs and records their bytes; this is not candidate qualification or promotion. #>
param(
    [Parameter(Mandatory)][ValidateSet('windows','linux','macos')][string]$Platform,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceCommit,
    [string]$ArtifactDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'),
    [string]$GitHubOutputPath,
    # A release candidate's packages are FileCat 1.0.0 built from its v1.0.0-rc.N tag (I634): they are selected as
    # -Version 1.0.0 with that tag's reference, which the manifest records. Without one, a release version or an RC-named
    # set is refused.
    [string]$CandidateRef
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not [string]::IsNullOrEmpty($CandidateRef)) {
    if ($CandidateRef -cnotmatch '^refs/tags/v1\.0\.0-rc\.[1-9][0-9]*$' -or $Version -cne '1.0.0') {
        throw 'A candidate is selected as version 1.0.0 with its exact v1.0.0-rc.N reference.'
    }
}
elseif ($Version -cnotmatch '-') {
    throw 'A release-version package set needs its candidate reference.'
}
elseif ($Version -cmatch '^1\.0\.0-rc\.') {
    throw 'Candidate packages are named 1.0.0 and selected with their candidate reference (I634).'
}
$Root = [IO.Path]::GetFullPath($ArtifactDirectory)
if ($Root -match '[\r\n\[\]{}()*?!]') { throw 'Artifact root cannot contain action glob metacharacters or newlines.' }
$Current = Get-Item -LiteralPath $Root -Force
if (-not $Current.PSIsContainer) { throw 'Artifact root must be a directory.' }
while ($null -ne $Current) {
    if ($Current.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Artifact paths cannot traverse links.' }
    $Current = $Current.Parent
}
$NumericVersion = ($Version -split '-')[0]
$Names = @(switch ($Platform) {
    windows {
        foreach ($Architecture in @('x64','arm64')) {
            "FileCat-$NumericVersion-win-$Architecture-setup.exe"
            "FileCat-$Version-win-$Architecture-portable.zip"
            "FileCat-$Version-win-$Architecture-fdd.zip"
            "sbom-$Version-win-$Architecture-setup-payload.spdx.json"
            "sbom-$Version-win-$Architecture-portable.spdx.json"
            "sbom-$Version-win-$Architecture-fdd.spdx.json"
        }
    }
    linux {
        "FileCat-$Version-linux-x64.tar.gz"
        # Named with FileCat's version: GitHub renames assets whose names contain the Debian version's '~'.
        "filecat_${Version}_amd64.deb"
        "FileCat-$Version-x86_64.AppImage"
        "FileCat-$Version-linux-x64-appimage-runtime-source.tar.gz"
        "sbom-$Version-linux-x64-tar.spdx.json"
        "sbom-$Version-linux-x64-deb.spdx.json"
        "sbom-$Version-linux-x64-appimage.spdx.json"
    }
    macos { "FileCat-$Version-osx-arm64.zip"; "sbom-$Version-osx-arm64-macos-app.spdx.json" }
})
$Files = foreach ($Name in $Names) {
    $Path = Join-Path $Root $Name
    $File = Get-Item -LiteralPath $Path -Force
    if ($File.PSIsContainer -or ($File.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $File.Length -eq 0) {
        throw "Required package is not a nonempty regular file: $Name"
    }
    $Stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $Length = $Stream.Length
        $Digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Stream)).ToLowerInvariant()
    }
    finally { $Stream.Dispose() }
    [ordered]@{ Name=$Name; Bytes=$Length; SHA256=$Digest }
}
$ManifestName = "FileCat-$Version-$Platform-assets.json"
$ManifestPath = Join-Path $Root $ManifestName
if (Test-Path -LiteralPath $ManifestPath) { throw 'An asset manifest already exists; preserve its original bytes.' }
$Manifest = [ordered]@{
    SourceCommit=$SourceCommit; Platform=$Platform; Version=$Version
}
if (-not [string]::IsNullOrEmpty($CandidateRef)) { $Manifest.CandidateRef = $CandidateRef }
$Manifest.SelectorSHA256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
$Manifest.Files = @($Files)
$Manifest.CandidateQualified = $false
$Manifest.SigningVerified = $false
$Manifest.InstallerVersionSuffixPreserved = ($Platform -ne 'windows' -or $Version -eq $NumericVersion)
$Utf8 = [Text.UTF8Encoding]::new($false)
$Stream = [IO.File]::Open($ManifestPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $Bytes = $Utf8.GetBytes(($Manifest | ConvertTo-Json -Depth 5) + "`n")
    $Stream.Write($Bytes, 0, $Bytes.Length)
}
finally { $Stream.Dispose() }
$Paths = @($Names + $ManifestName | ForEach-Object { (Join-Path $Root $_).Replace('\','/') })
if ($GitHubOutputPath) {
    $Delimiter = 'FILECAT_RELEASE_ASSETS_' + [guid]::NewGuid().ToString('N')
    [IO.File]::AppendAllText([IO.Path]::GetFullPath($GitHubOutputPath), "files<<$Delimiter`n" + ($Paths -join "`n") + "`n$Delimiter`n", $Utf8)
    [IO.File]::AppendAllText([IO.Path]::GetFullPath($GitHubOutputPath), "manifest_path=$($ManifestPath.Replace('\','/'))`nmanifest_sha256=$((Get-FileHash -LiteralPath $ManifestPath).Hash.ToLowerInvariant())`n", $Utf8)
}
[pscustomobject]@{ ManifestPath=$ManifestPath; Files=$Paths; Manifest=$Manifest }
