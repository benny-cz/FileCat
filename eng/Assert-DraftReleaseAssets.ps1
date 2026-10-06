<# Read-only preview preflight. This does not authorize publication or stable promotion. #>
param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$ManifestSHA256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceCommit,
    [Parameter(Mandatory)][string]$Reference,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
    [string]$ReceiptPath,
    [switch]$VerifyUpload,
    [string]$UploadedAssetsJson,
    [long]$ExpectedReleaseID
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Policy = & (Join-Path $PSScriptRoot 'Assert-ProducerReference.ps1') -Reference $Reference -EventName push
if ($Policy.Mode -cne 'prerelease') { throw 'Draft asset upload requires a supported prerelease tag.' }
$Tag = $Reference.Substring('refs/tags/'.Length)
$Version = $Tag.Substring(1)
$Path = [IO.Path]::GetFullPath($ManifestPath)
$Directory = [IO.Path]::GetDirectoryName($Path)
$Current = Get-Item -LiteralPath $Directory -Force
while ($null -ne $Current) {
    if ($Current.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Asset paths cannot traverse links.' }
    $Current = $Current.Parent
}
function Get-AssetPin([string]$AssetPath) {
    $File = Get-Item -LiteralPath $AssetPath -Force
    if ($File.PSIsContainer -or ($File.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $File.Length -eq 0) { throw 'Asset is not a nonempty regular file.' }
    $Stream = [IO.File]::Open($AssetPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { [pscustomobject]@{Name=$File.Name;Bytes=$Stream.Length;SHA256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Stream)).ToLowerInvariant()} }
    finally { $Stream.Dispose() }
}
$ManifestPin = Get-AssetPin $Path
if ($ManifestPin.SHA256 -cne $ManifestSHA256) { throw 'Selected manifest bytes changed.' }
$Manifest = [IO.File]::ReadAllText($Path) | ConvertFrom-Json
if ($Manifest.SourceCommit -cne $SourceCommit -or $Manifest.Version -cne $Version -or $Manifest.Platform -cnotin @('windows','linux','macos')) { throw 'Manifest source, version or platform differs from producer identity.' }
if ($ManifestPin.Name -cne "FileCat-$Version-$($Manifest.Platform)-assets.json") { throw 'Manifest name differs from producer identity.' }
$NumericVersion = ($Version -split '-')[0]
$Names = @(switch ($Manifest.Platform) {
    windows { foreach ($Architecture in @('x64','arm64')) {
        "FileCat-$NumericVersion-win-$Architecture-setup.exe"; "FileCat-$Version-win-$Architecture-portable.zip"
        "FileCat-$Version-win-$Architecture-fdd.zip"; "sbom-$Version-win-$Architecture.json"
    } }
    linux { "FileCat-$Version-linux-x64.tar.gz"; "filecat_$($Version -replace '^(\d+\.\d+\.\d+)-', '$1~')_amd64.deb"; "FileCat-$Version-x86_64.AppImage" }
    macos { "FileCat-$Version-osx-arm64.zip" }
})
if (($Manifest.Files.Name -join "`n") -cne ($Names -join "`n")) { throw 'Manifest asset names differ from the platform contract.' }
$Pins = @(foreach ($Expected in $Manifest.Files) {
    $Actual = Get-AssetPin (Join-Path $Directory $Expected.Name)
    if ($Actual.Bytes -ne $Expected.Bytes -or $Actual.SHA256 -cne $Expected.SHA256) { throw 'Selected package bytes changed.' }
    $Actual
}) + $ManifestPin
if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { throw 'Read-only GitHub preflight requires an authenticated token.' }
$Headers = @{Authorization="Bearer $env:GH_TOKEN";Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28'}
$Api = "https://api.github.com/repos/$Repository"
$Requests = [Collections.Generic.List[string]]::new()
function Read-GitHub([string]$Suffix) {
    $Requests.Add($Suffix)
    try { Invoke-RestMethod -Uri "$Api/$Suffix" -Headers $Headers -Method Get -TimeoutSec 30 }
    catch { throw "Read-only GitHub preflight failed at $Suffix; no upload is authorized." }
}
$Object = (Read-GitHub "git/ref/tags/$Tag").object
for ($Depth=0; $Object.type -ceq 'tag' -and $Depth -lt 5; $Depth++) {
    if ($Object.sha -cnotmatch '^[0-9a-f]{40}$') { throw 'Malformed annotated tag object.' }
    $Object = (Read-GitHub "git/tags/$($Object.sha)").object
}
if ($Object.type -cne 'commit' -or $Object.sha -cne $SourceCommit) { throw 'Remote tag differs from the selected source commit.' }
$Releases = [Collections.Generic.List[object]]::new()
for ($Page=1; $Page -le 100; $Page++) {
    $Rows = @(Read-GitHub "releases?per_page=100&page=$Page")
    if ($Rows.Count -gt 100) { throw 'Malformed release page.' }
    foreach ($Row in $Rows) { if ($Row.tag_name -ceq $Tag) { $Releases.Add($Row) } }
    if ($Rows.Count -lt 100) { break }
}
if ($Page -gt 100 -or $Releases.Count -gt 1) { throw 'Release inventory is incomplete or ambiguous.' }
$ReleaseID = $null
$Assets = [Collections.Generic.List[object]]::new()
if ($Releases.Count -eq 1) {
    $Release = $Releases[0]
    if ($Release.draft -ne $true -or $Release.prerelease -ne $true -or $Release.id -le 0) { throw 'Existing release is not an unsigned preview draft.' }
    if ($Release.PSObject.Properties.Name -contains 'immutable' -and $Release.immutable) { throw 'Existing release is immutable.' }
    $ReleaseID = $Release.id
    for ($Page=1; $Page -le 100; $Page++) {
        $Rows = @(Read-GitHub "releases/$ReleaseID/assets?per_page=100&page=$Page")
        if ($Rows.Count -gt 100) { throw 'Malformed asset page.' }
        foreach ($Row in $Rows) { $Assets.Add($Row) }
        if ($Rows.Count -lt 100) { break }
    }
    if ($Page -gt 100) { throw 'Asset inventory is incomplete.' }
    foreach ($Asset in $Assets) {
        if ($Asset.id -le 0 -or [string]::IsNullOrEmpty($Asset.name)) { throw 'Malformed existing asset identity.' }
        foreach ($Pin in $Pins) {
            if (-not $VerifyUpload -and ($Asset.name -ieq $Pin.Name -or $Asset.label -ieq $Pin.Name)) { throw 'An existing release asset conflicts with the selected upload; preserve it even if its hash matches.' }
        }
    }
}
$UploadedIDs = [Collections.Generic.HashSet[long]]::new()
if ($VerifyUpload) {
    if ($null -eq $ReleaseID -or $ExpectedReleaseID -le 0 -or $ReleaseID -ne $ExpectedReleaseID) { throw 'Uploaded release ID differs from the remote preview draft.' }
    $Uploaded = ConvertFrom-Json -InputObject $UploadedAssetsJson -NoEnumerate
    if ($Uploaded -isnot [Array] -or $Uploaded.Count -ne $Pins.Count) { throw 'The action did not upload the complete selected asset set; a duplicate may have been skipped.' }
    foreach ($Pin in $Pins) {
        $Matches = @($Uploaded | Where-Object {$_.name -ceq $Pin.Name})
        if ($Matches.Count -ne 1) { throw 'Uploaded asset names differ from the selected set.' }
        $Match = $Matches[0]
        if ($Match.id -le 0 -or -not $UploadedIDs.Add([long]$Match.id) -or $Match.size -ne $Pin.Bytes -or $Match.digest -cne "sha256:$($Pin.SHA256)") { throw 'Uploaded asset identity, size or server digest differs from selected bytes.' }
        $Remote = @($Assets | Where-Object {$_.name -ieq $Pin.Name -or $_.label -ieq $Pin.Name})
        if ($Remote.Count -ne 1 -or $Remote[0].name -cne $Pin.Name -or $Remote[0].id -ne $Match.id -or $Remote[0].size -ne $Pin.Bytes -or $Remote[0].digest -cne "sha256:$($Pin.SHA256)") { throw 'Remote asset identity or bytes differ from the action upload.' }
    }
}
$Record = [ordered]@{
    UTC=[DateTime]::UtcNow.ToString('o');Repository=$Repository;Reference=$Reference;SourceCommit=$SourceCommit
    ManifestSHA256=$ManifestSHA256;Platform=$Manifest.Platform;SelectedAssets=$Pins;ExistingReleaseID=$ReleaseID
    ExistingAssetIDs=@($Assets | ForEach-Object {$_.id});ReadOnlyAPIPaths=@($Requests.ToArray())
    GuardSHA256=(Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant()
    GITHUB_RUN_ID=$env:GITHUB_RUN_ID;GITHUB_RUN_ATTEMPT=$env:GITHUB_RUN_ATTEMPT
    Mode=$(if ($VerifyUpload) {'verify-upload'} else {'preflight'});UploadedAssetIDs=@($UploadedIDs)
    ExistingNamesRefused=(-not $VerifyUpload);UploadNamesIDsSizesAndServerDigestsVerified=[bool]$VerifyUpload
    StablePromotionAuthorized=$false;CandidateQualified=$false
}
if ($ReceiptPath) {
    $Output = [IO.Path]::GetFullPath($ReceiptPath)
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Output))
    $Stream = [IO.File]::Open($Output,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $Bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Record | ConvertTo-Json -Depth 7));$Stream.Write($Bytes,0,$Bytes.Length) }
    finally { $Stream.Dispose() }
}
[pscustomobject]$Record
