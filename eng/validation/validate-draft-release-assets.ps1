param([string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Eng = Split-Path $PSScriptRoot -Parent
$Guard = Join-Path $Eng 'Assert-DraftReleaseAssets.ps1'
$TemporaryBase = if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    $env:RUNNER_TEMP
}
elseif ($IsMacOS) {
    [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
}
else {
    [IO.Path]::GetTempPath()
}
$Parent = [IO.Path]::GetFullPath((Join-Path $TemporaryBase 'FileCatValidation'))
[void][IO.Directory]::CreateDirectory($Parent)
$Root = Join-Path $Parent ('filecat-draft-controls-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($Root)
$Source = '0123456789abcdef0123456789abcdef01234567'
$Version = '1.0.0-preview.3'
$Reference = "refs/tags/v$Version"
$Cases = @(
    @{Name='windows-new';Platform='windows'}, @{Name='windows-new-irm-empty';Platform='windows'},
    @{Name='linux-draft';Platform='linux'}, @{Name='linux-draft-irm-empty-assets';Platform='linux'},
    @{Name='macos-annotated';Platform='macos'},
    @{Name='release-malformed-row';Error='Malformed release entry'},
    @{Name='asset-malformed-row';Error='Malformed asset entry'},
    @{Name='duplicate-package';Error='An existing release asset conflicts'},
    @{Name='duplicate-same-digest';Error='An existing release asset conflicts'},
    @{Name='duplicate-manifest';Error='An existing release asset conflicts'},
    @{Name='duplicate-label';Error='An existing release asset conflicts'},
    @{Name='duplicate-case-name';Error='An existing release asset conflicts'},
    @{Name='duplicate-next-page';Error='An existing release asset conflicts'},
    @{Name='manifest-hash-change';Error='Selected manifest bytes changed'},
    @{Name='package-hash-change';Error='Selected package bytes changed'},
    @{Name='wrong-source';Error='Manifest source, version or platform differs'},
    @{Name='wrong-version';Error='Manifest source, version or platform differs'},
    @{Name='unexpected-name';Error='Manifest asset names differ'},
    @{Name='remote-source-change';Error='Remote tag differs'},
    @{Name='stable-reference';Error='Stable references cannot invoke a producer'},
    @{Name='rc-reference';Error='Draft asset upload requires a 1.0.0 preview tag'},
    @{Name='published-release';Error='Existing release is not an unsigned preview draft'},
    @{Name='immutable-release';Error='Existing release is immutable'},
    @{Name='api-failure';Error='Read-only GitHub preflight failed'},
    @{Name='missing-token';Error='Read-only GitHub preflight requires'},
    @{Name='ambiguous-release';Error='Release inventory is incomplete or ambiguous'},
    @{Name='annotated-depth';Error='Remote tag differs'},
    @{Name='uploaded-complete';Verify=$true},
    @{Name='uploaded-skipped-duplicate';Verify=$true;Error='The action did not upload the complete selected asset set'},
    @{Name='uploaded-extra';Verify=$true;Error='The action did not upload the complete selected asset set'},
    @{Name='uploaded-wrong-name';Verify=$true;Error='Uploaded asset names differ'},
    @{Name='uploaded-wrong-hash';Verify=$true;Error='Uploaded asset identity, size or server digest differs'},
    @{Name='uploaded-wrong-size';Verify=$true;Error='Uploaded asset identity, size or server digest differs'},
    @{Name='uploaded-reused-id';Verify=$true;Error='Uploaded asset identity, size or server digest differs'},
    @{Name='uploaded-wrong-release';Verify=$true;Error='Uploaded release ID differs'},
    @{Name='uploaded-remote-id-change';Verify=$true;Error='Remote asset identity or bytes differ'},
    @{Name='uploaded-remote-hash-change';Verify=$true;Error='Remote asset identity or bytes differ'},
    # The signed-candidate path (-SignedCandidate): each case behaves as the preview case its name ends with.
    @{Name='candidate-windows-new';Platform='windows';Candidate=$true},
    @{Name='candidate-linux-draft';Platform='linux';Candidate=$true},
    @{Name='candidate-uploaded-complete';Verify=$true;Candidate=$true},
    @{Name='candidate-preview-reference';Candidate=$true;Error='Signed-candidate asset upload requires a 1.0.0 RC tag'},
    @{Name='candidate-published-release';Candidate=$true;Error='Existing release is not a signed-candidate draft'}
)
$Observations = [Collections.Generic.List[object]]::new()
$OriginalToken = $env:GH_TOKEN
# This function substitutes only the GET transport in this script's child scope.
# No network, tag/ref creation, release mutation or real authentication occurs.
function Invoke-RestMethod($Uri,$Headers,$Method,$TimeoutSec) {
    if ($Method -cne 'Get' -or $TimeoutSec -ne 30 -or $Headers.Authorization -cne 'Bearer synthetic-control-token') { throw 'Unexpected transport contract.' }
    $Suffix = $Uri.Substring('https://api.github.com/repos/owned/fixture/'.Length)
    $Calls.Add($Suffix)
    if ($Base -eq 'api-failure') { throw 'Synthetic unavailable API.' }
    if ($Suffix.StartsWith('git/ref/tags/')) {
        if ($Suffix -cne "git/ref/tags/v$CaseVersion") { throw 'Unexpected reference request.' }
        return [pscustomobject]@{object=[pscustomobject]@{type=$(if ($Base -in @('macos-annotated','annotated-depth')) {'tag'} else {'commit'});sha=$(if ($Base -eq 'remote-source-change') {'1123456789abcdef0123456789abcdef01234567'} else {$Source})}}
    }
    if ($Suffix.StartsWith('git/tags/')) { return [pscustomobject]@{object=[pscustomobject]@{type=$(if ($Base -eq 'annotated-depth') {'tag'} else {'commit'});sha=$Source}} }
    if ($Suffix -ceq 'releases?per_page=100&page=1') {
        # Invoke-RestMethod returns an empty JSON array as one empty array object,
        # which @(...)-wrapping must not turn into one malformed release row.
        if ($Base -eq 'windows-new-irm-empty') { Write-Output -NoEnumerate -InputObject ([object[]]@()); return }
        if ($Base -eq 'release-malformed-row') { return @([pscustomobject]@{message='synthetic malformed row'}) }
        if ($Base -in @('windows-new','macos-annotated')) { Write-Output -NoEnumerate -InputObject ([object[]]@()); return }
        $Draft = [pscustomobject]@{id=51;tag_name="v$CaseVersion";draft=($Base -ne 'published-release');prerelease=$true;immutable=($Base -eq 'immutable-release')}
        if ($Base -eq 'ambiguous-release') { return @($Draft,$Draft) }
        return @($Draft)
    }
    if ($Suffix -ceq 'releases/51/assets?per_page=100&page=1') {
        if ($Base -eq 'linux-draft-irm-empty-assets') { Write-Output -NoEnumerate -InputObject ([object[]]@()); return }
        if ($Base -eq 'asset-malformed-row') { return @([pscustomobject]@{message='synthetic malformed asset'}) }
        if ($Case.ContainsKey('Verify')) { return $RemoteUploads }
        if ($Base -eq 'duplicate-next-page') {
            return @(1..100 | ForEach-Object {[pscustomobject]@{id=$_;name="unrelated-owned-$_.zip";label=$null;digest=$null}})
        }
        if ($Base.StartsWith('duplicate-')) {
            $Name = if ($Base -eq 'duplicate-manifest') {[IO.Path]::GetFileName($Selection.ManifestPath)} elseif ($Base -eq 'duplicate-label') {'other-owned.zip'} elseif ($Base -eq 'duplicate-case-name') {$PackageName.ToUpperInvariant()} else {$PackageName}
            return @([pscustomobject]@{id=101;name=$Name;label=$(if ($Base -eq 'duplicate-label') {$PackageName} else {$null});digest=$(if ($Base -eq 'duplicate-same-digest') {"sha256:$($Selection.Manifest.Files[0].SHA256)"} else {'sha256:' + ('0' * 64)})})
        }
        return @([pscustomobject]@{id=101;name='unrelated-owned.zip';label=$null;digest=$null})
    }
    if ($Suffix -ceq 'releases/51/assets?per_page=100&page=2' -and $Base -eq 'duplicate-next-page') { return @([pscustomobject]@{id=101;name=$PackageName;label=$null;digest=$null}) }
    throw 'Unexpected transport request.'
}
try {
    foreach ($Case in $Cases) {
        $IsCandidate = $Case.ContainsKey('Candidate')
        $Base = $Case.Name -replace '^candidate-', ''
        $CaseVersion = if ($IsCandidate) {'1.0.0-rc.1'} else {$Version}
        $CaseReference = "refs/tags/v$CaseVersion"
        $Directory = Join-Path $Root $Case.Name
        [void][IO.Directory]::CreateDirectory($Directory)
        $Platform = if ($Case.ContainsKey('Platform')) {$Case.Platform} else {'macos'}
        $Names = switch ($Platform) {
            windows {@('FileCat-1.0.0-win-x64-setup.exe','FileCat-1.0.0-preview.3-win-x64-portable.zip','FileCat-1.0.0-preview.3-win-x64-fdd.zip','sbom-1.0.0-preview.3-win-x64-setup-payload.spdx.json','sbom-1.0.0-preview.3-win-x64-portable.spdx.json','sbom-1.0.0-preview.3-win-x64-fdd.spdx.json','FileCat-1.0.0-win-arm64-setup.exe','FileCat-1.0.0-preview.3-win-arm64-portable.zip','FileCat-1.0.0-preview.3-win-arm64-fdd.zip','sbom-1.0.0-preview.3-win-arm64-setup-payload.spdx.json','sbom-1.0.0-preview.3-win-arm64-portable.spdx.json','sbom-1.0.0-preview.3-win-arm64-fdd.spdx.json')}
            linux {@('FileCat-1.0.0-preview.3-linux-x64.tar.gz','filecat_1.0.0-preview.3_amd64.deb','FileCat-1.0.0-preview.3-x86_64.AppImage','FileCat-1.0.0-preview.3-linux-x64-appimage-runtime-source.tar.gz','sbom-1.0.0-preview.3-linux-x64-tar.spdx.json','sbom-1.0.0-preview.3-linux-x64-deb.spdx.json','sbom-1.0.0-preview.3-linux-x64-appimage.spdx.json')}
            macos {@('FileCat-1.0.0-preview.3-osx-arm64.zip','sbom-1.0.0-preview.3-osx-arm64-macos-app.spdx.json')}
        }
        $Names = @($Names | ForEach-Object { $_.Replace('1.0.0-preview.3', $CaseVersion) })
        foreach ($Name in $Names) { [IO.File]::WriteAllText((Join-Path $Directory $Name),"external owned contract: $Name") }
        $Selection = & (Join-Path $Eng 'Select-ReleaseAssets.ps1') -Platform $Platform -Version $CaseVersion -SourceCommit $Source -ArtifactDirectory $Directory
        $PackageName = $Selection.Manifest.Files[0].Name
        $Hash = (Get-FileHash -LiteralPath $Selection.ManifestPath).Hash.ToLowerInvariant()
        if ($Base -eq 'manifest-hash-change') { [IO.File]::AppendAllText($Selection.ManifestPath,' ') }
        if ($Base -eq 'package-hash-change') { [IO.File]::AppendAllText((Join-Path $Directory $PackageName),'changed') }
        if ($Base -in @('wrong-source','wrong-version','unexpected-name')) {
            $Manifest = Get-Content -Raw -LiteralPath $Selection.ManifestPath | ConvertFrom-Json
            if ($Base -eq 'wrong-source') {$Manifest.SourceCommit='1123456789abcdef0123456789abcdef01234567'}
            if ($Base -eq 'wrong-version') {$Manifest.Version='1.0.0-rc.2'}
            if ($Base -eq 'unexpected-name') {$Manifest.Files[0].Name='../outside.zip'}
            [IO.File]::WriteAllText($Selection.ManifestPath,($Manifest | ConvertTo-Json -Depth 6))
            $Hash = (Get-FileHash -LiteralPath $Selection.ManifestPath).Hash.ToLowerInvariant()
        }
        $Calls = [Collections.Generic.List[string]]::new()
        $UploadParameters = @{}
        if ($Case.ContainsKey('Verify')) {
            $Uploaded = @(for ($Index=0; $Index -lt $Selection.Files.Count; $Index++) {
                $File=Get-Item -LiteralPath $Selection.Files[$Index]
                [pscustomobject]@{id=(201+$Index);name=$File.Name;label=$null;size=$File.Length;digest=('sha256:' + (Get-FileHash -LiteralPath $File.FullName).Hash.ToLowerInvariant())}
            })
            $RemoteUploads = @($Uploaded | ForEach-Object {[pscustomobject]@{id=$_.id;name=$_.name;label=$_.label;size=$_.size;digest=$_.digest}})
            if ($Base -eq 'uploaded-skipped-duplicate') {$Uploaded=@($Uploaded[0])}
            if ($Base -eq 'uploaded-extra') {$Uploaded+=[pscustomobject]@{id=999;name='extra.zip';label=$null;size=1;digest=$null}}
            if ($Base -eq 'uploaded-wrong-name') {$Uploaded[0].name='unexpected.zip'}
            if ($Base -eq 'uploaded-wrong-hash') {$Uploaded[0].digest='sha256:' + ('0' * 64)}
            if ($Base -eq 'uploaded-wrong-size') {$Uploaded[0].size++}
            if ($Base -eq 'uploaded-reused-id') {$Uploaded[1].id=$Uploaded[0].id}
            if ($Base -eq 'uploaded-remote-id-change') {$RemoteUploads[0].id++}
            if ($Base -eq 'uploaded-remote-hash-change') {$RemoteUploads[0].digest='sha256:' + ('0' * 64)}
            $UploadParameters=@{VerifyUpload=$true;UploadedAssetsJson=(ConvertTo-Json -InputObject $Uploaded -Depth 5);ExpectedReleaseID=$(if ($Base -eq 'uploaded-wrong-release') {52} else {51})}
        }
        $env:GH_TOKEN = if ($Base -eq 'missing-token') {''} else {'synthetic-control-token'}
        $Receipt = Join-Path $Directory 'preflight.json'
        $Result = $null; $ErrorText = $null
        try { $Result = & $Guard -ManifestPath $Selection.ManifestPath -ManifestSHA256 $Hash -SourceCommit $Source -Reference $(if ($Base -eq 'stable-reference') {'refs/tags/v1.0.0'} elseif ($Base -eq 'rc-reference') {'refs/tags/v1.0.0-rc.1'} elseif ($Base -eq 'preview-reference') {$Reference} else {$CaseReference}) -Repository owned/fixture -ReceiptPath $Receipt -SignedCandidate:$IsCandidate @UploadParameters }
        catch { $ErrorText = $_.ToString() }
        if ($Case.ContainsKey('Error')) {
            if ($null -ne $Result -or $null -eq $ErrorText -or -not $ErrorText.StartsWith($Case.Error) -or (Test-Path -LiteralPath $Receipt)) { throw "Denied case $($Case.Name) differs from external refusal contract: $ErrorText" }
        }
        elseif ($null -ne $ErrorText -or $Result.SelectedAssets.Count -ne @($Names).Count+1 -or $Result.Kind -cne $(if ($IsCandidate) {'signed-candidate'} else {'unsigned-preview'}) -or $Result.StablePromotionAuthorized -or $Result.CandidateQualified -or -not (Test-Path -LiteralPath $Receipt)) { throw "Positive case $($Case.Name) failed: $ErrorText" }
        if (-not $Case.ContainsKey('Error')) {
            $VerifyExpected=$Case.ContainsKey('Verify')
            if ($Result.UploadNamesIDsSizesAndServerDigestsVerified -ne $VerifyExpected -or $Result.ExistingNamesRefused -eq $VerifyExpected -or ($VerifyExpected -and $Result.UploadedAssetIDs.Count -ne @($Names).Count+1)) { throw 'Positive upload/preflight evidence classification differs from contract.' }
        }
        $Observations.Add([ordered]@{Case=$Case.Name;Expected=$(if ($Case.ContainsKey('Error')) {'refused'} else {'allowed'});Error=$ErrorText;Result=$Result;ReadOnlyAPIPaths=@($Calls.ToArray());NoMutationRequests=$true;Passed=$true})
    }
    $Record = [ordered]@{
        UTC=[DateTime]::UtcNow.ToString('o');GuardSHA256=(Get-FileHash -LiteralPath $Guard).Hash.ToLowerInvariant()
        ValidatorSHA256=(Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant();PowerShell=$PSVersionTable.PSVersion.ToString()
        GITHUB_SHA=$env:GITHUB_SHA;GITHUB_RUN_ID=$env:GITHUB_RUN_ID;GITHUB_RUN_ATTEMPT=$env:GITHUB_RUN_ATTEMPT
        Cases=@($Observations.ToArray());Pass=$Observations.Count;SyntheticFixtures=$true;ActualGitHubAPIUsed=$false
        ActualTagOrReleaseCreated=$false;PublicationOrPromotionAuthorized=$false
    }
    if ($EvidenceDirectory) {
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($EvidenceDirectory))
        [IO.File]::WriteAllText((Join-Path $EvidenceDirectory 'draft-release-controls.json'),($Record | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    }
    $Record | ConvertTo-Json -Depth 8
}
finally {
    $env:GH_TOKEN = $OriginalToken
    $Resolved = [IO.Path]::GetFullPath($Root)
    if ([IO.Path]::GetDirectoryName($Resolved) -ne $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar) -or [IO.Path]::GetFileName($Resolved) -notmatch '^filecat-draft-controls-[0-9a-f]{32}$' -or ((Get-Item -LiteralPath $Resolved).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected cleanup root.' }
    [IO.Directory]::Delete($Resolved,$true)
}
