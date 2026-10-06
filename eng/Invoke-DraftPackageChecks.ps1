<# Assemble/read-check this run's three unsigned preview artifacts. No build, signing or release mutation. #>
param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceCommit,
    [Parameter(Mandatory)][string]$Reference,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$WindowsManifestSHA256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$LinuxManifestSHA256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$MacOSManifestSHA256,
    [Parameter(Mandatory)][string]$ReceiptDirectory,
    [string]$GitHubOutputPath,
    [switch]$VerifyUpload,
    [string]$UploadedAssetsJson,
    [long]$ExpectedReleaseID
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$Policy=& (Join-Path $PSScriptRoot 'Assert-ProducerReference.ps1') -Reference $Reference -EventName push
if($Policy.Mode -cne 'prerelease'){throw 'Draft package checks require a supported prerelease tag.'}
$Version=$Reference.Substring('refs/tags/v'.Length)
$Root=[IO.Path]::GetFullPath($ArtifactDirectory)
if($Root -match '[\r\n\[\]{}()*?!]'){throw 'Artifact root cannot contain action glob metacharacters or newlines.'}
$Specifications=@(
    @{Platform='windows';Hash=$WindowsManifestSHA256},
    @{Platform='linux';Hash=$LinuxManifestSHA256},
    @{Platform='macos';Hash=$MacOSManifestSHA256}
)
$Uploaded=$null
if($VerifyUpload){
    $Uploaded=ConvertFrom-Json -InputObject $UploadedAssetsJson -NoEnumerate
    if($Uploaded -isnot [Array] -or $Uploaded.Count -ne 15){throw 'Combined action upload is incomplete or contains unexpected assets.'}
    $IDs=[Collections.Generic.HashSet[long]]::new()
    $UploadNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($Asset in $Uploaded){
        if($Asset.id -le 0 -or -not $IDs.Add([long]$Asset.id) -or -not $UploadNames.Add([string]$Asset.name)){throw 'Combined action upload contains duplicate IDs or names.'}
    }
}
$Results=[Collections.Generic.List[object]]::new()
$Paths=[Collections.Generic.List[string]]::new()
$Names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($Specification in $Specifications){
    $Platform=$Specification.Platform
    $Directory=Join-Path $Root $Platform
    $ManifestPath=Join-Path $Directory "FileCat-$Version-$Platform-assets.json"
    $Arguments=@{
        ManifestPath=$ManifestPath;ManifestSHA256=$Specification.Hash;SourceCommit=$SourceCommit
        Reference=$Reference;Repository=$Repository
        ReceiptPath=(Join-Path $ReceiptDirectory "$Platform-$(if($VerifyUpload){'uploaded'}else{'preflight'}).json")
    }
    if($VerifyUpload){
        $Manifest=Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
        $PlatformNames=@($Manifest.Files.Name)+[IO.Path]::GetFileName($ManifestPath)
        $Subset=@($Uploaded | Where-Object {$_.name -cin $PlatformNames})
        $Arguments.VerifyUpload=$true
        $Arguments.ExpectedReleaseID=$ExpectedReleaseID
        $Arguments.UploadedAssetsJson=ConvertTo-Json -InputObject $Subset -Depth 7
    }
    $Result=& (Join-Path $PSScriptRoot 'Assert-DraftReleaseAssets.ps1') @Arguments
    foreach($Asset in $Result.SelectedAssets){
        if(-not $Names.Add($Asset.Name)){throw 'Package platforms contain duplicate upload names.'}
        $Paths.Add((Join-Path $Directory $Asset.Name).Replace('\','/'))
    }
    $Results.Add($Result)
}
if($Paths.Count -ne 15){throw 'Combined selected upload differs from the three-platform contract.'}
if($VerifyUpload -and -not $UploadNames.SetEquals($Names)){throw 'Combined uploaded names differ from the selected package set.'}
if($GitHubOutputPath){
    if($VerifyUpload){throw 'Post-upload verification cannot emit an upload file list.'}
    $Delimiter='FILECAT_DRAFT_FILES_'+[guid]::NewGuid().ToString('N')
    [IO.File]::AppendAllText([IO.Path]::GetFullPath($GitHubOutputPath),"files<<$Delimiter`n"+($Paths -join "`n")+"`n$Delimiter`n",[Text.UTF8Encoding]::new($false))
}
[pscustomobject]@{Mode=$(if($VerifyUpload){'verify-upload'}else{'preflight'});Files=@($Paths.ToArray());PlatformReceipts=@($Results.ToArray());SourceCommit=$SourceCommit;StablePromotionAuthorized=$false;CandidateQualified=$false}
