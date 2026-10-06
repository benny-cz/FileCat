param([string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Eng = Split-Path $PSScriptRoot -Parent
$Guard = Join-Path $Eng 'Assert-DraftReleaseAssets.ps1'
$Parent = [IO.Path]::GetFullPath((Join-Path $Eng '../artifacts'))
[void][IO.Directory]::CreateDirectory($Parent)
$Root = Join-Path $Parent ('filecat-draft-controls-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($Root)
$Source = '0123456789abcdef0123456789abcdef01234567'
$Version = '1.0.0-rc.1'
$Reference = "refs/tags/v$Version"
$Cases = @(
    @{Name='windows-new';Platform='windows'}, @{Name='linux-draft';Platform='linux'}, @{Name='macos-annotated';Platform='macos'},
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
    @{Name='uploaded-remote-hash-change';Verify=$true;Error='Remote asset identity or bytes differ'}
)
$Observations = [Collections.Generic.List[object]]::new()
$OriginalToken = $env:GH_TOKEN
# This function substitutes only the GET transport in this script's child scope.
# No network, tag/ref creation, release mutation or real authentication occurs.
function Invoke-RestMethod($Uri,$Headers,$Method,$TimeoutSec) {
    if ($Method -cne 'Get' -or $TimeoutSec -ne 30 -or $Headers.Authorization -cne 'Bearer synthetic-control-token') { throw 'Unexpected transport contract.' }
    $Suffix = $Uri.Substring('https://api.github.com/repos/owned/fixture/'.Length)
    $Calls.Add($Suffix)
    if ($Case.Name -eq 'api-failure') { throw 'Synthetic unavailable API.' }
    if ($Suffix.StartsWith('git/ref/tags/')) {
        if ($Suffix -cne "git/ref/tags/v$Version") { throw 'Unexpected reference request.' }
        return [pscustomobject]@{object=[pscustomobject]@{type=$(if ($Case.Name -in @('macos-annotated','annotated-depth')) {'tag'} else {'commit'});sha=$(if ($Case.Name -eq 'remote-source-change') {'1123456789abcdef0123456789abcdef01234567'} else {$Source})}}
    }
    if ($Suffix.StartsWith('git/tags/')) { return [pscustomobject]@{object=[pscustomobject]@{type=$(if ($Case.Name -eq 'annotated-depth') {'tag'} else {'commit'});sha=$Source}} }
    if ($Suffix -ceq 'releases?per_page=100&page=1') {
        if ($Case.Name -in @('windows-new','macos-annotated')) { return @() }
        $Draft = [pscustomobject]@{id=51;tag_name="v$Version";draft=($Case.Name -ne 'published-release');prerelease=$true;immutable=($Case.Name -eq 'immutable-release')}
        if ($Case.Name -eq 'ambiguous-release') { return @($Draft,$Draft) }
        return @($Draft)
    }
    if ($Suffix -ceq 'releases/51/assets?per_page=100&page=1') {
        if ($Case.ContainsKey('Verify')) { return $RemoteUploads }
        if ($Case.Name -eq 'duplicate-next-page') {
            return @(1..100 | ForEach-Object {[pscustomobject]@{id=$_;name="unrelated-owned-$_.zip";label=$null;digest=$null}})
        }
        if ($Case.Name.StartsWith('duplicate-')) {
            $Name = if ($Case.Name -eq 'duplicate-manifest') {[IO.Path]::GetFileName($Selection.ManifestPath)} elseif ($Case.Name -eq 'duplicate-label') {'other-owned.zip'} elseif ($Case.Name -eq 'duplicate-case-name') {$PackageName.ToUpperInvariant()} else {$PackageName}
            return @([pscustomobject]@{id=101;name=$Name;label=$(if ($Case.Name -eq 'duplicate-label') {$PackageName} else {$null});digest=$(if ($Case.Name -eq 'duplicate-same-digest') {"sha256:$($Selection.Manifest.Files[0].SHA256)"} else {'sha256:' + ('0' * 64)})})
        }
        return @([pscustomobject]@{id=101;name='unrelated-owned.zip';label=$null;digest=$null})
    }
    if ($Suffix -ceq 'releases/51/assets?per_page=100&page=2' -and $Case.Name -eq 'duplicate-next-page') { return @([pscustomobject]@{id=101;name=$PackageName;label=$null;digest=$null}) }
    throw 'Unexpected transport request.'
}
try {
    foreach ($Case in $Cases) {
        $Directory = Join-Path $Root $Case.Name
        [void][IO.Directory]::CreateDirectory($Directory)
        $Platform = if ($Case.ContainsKey('Platform')) {$Case.Platform} else {'macos'}
        $Names = switch ($Platform) {
            windows {@('FileCat-1.0.0-win-x64-setup.exe','FileCat-1.0.0-rc.1-win-x64-portable.zip','FileCat-1.0.0-rc.1-win-x64-fdd.zip','sbom-1.0.0-rc.1-win-x64.json','FileCat-1.0.0-win-arm64-setup.exe','FileCat-1.0.0-rc.1-win-arm64-portable.zip','FileCat-1.0.0-rc.1-win-arm64-fdd.zip','sbom-1.0.0-rc.1-win-arm64.json')}
            linux {@('FileCat-1.0.0-rc.1-linux-x64.tar.gz','filecat_1.0.0~rc.1_amd64.deb','FileCat-1.0.0-rc.1-x86_64.AppImage')}
            macos {@('FileCat-1.0.0-rc.1-osx-arm64.zip')}
        }
        foreach ($Name in $Names) { [IO.File]::WriteAllText((Join-Path $Directory $Name),"external owned contract: $Name") }
        $Selection = & (Join-Path $Eng 'Select-ReleaseAssets.ps1') -Platform $Platform -Version $Version -SourceCommit $Source -ArtifactDirectory $Directory
        $PackageName = $Selection.Manifest.Files[0].Name
        $Hash = (Get-FileHash -LiteralPath $Selection.ManifestPath).Hash.ToLowerInvariant()
        if ($Case.Name -eq 'manifest-hash-change') { [IO.File]::AppendAllText($Selection.ManifestPath,' ') }
        if ($Case.Name -eq 'package-hash-change') { [IO.File]::AppendAllText((Join-Path $Directory $PackageName),'changed') }
        if ($Case.Name -in @('wrong-source','wrong-version','unexpected-name')) {
            $Manifest = Get-Content -Raw -LiteralPath $Selection.ManifestPath | ConvertFrom-Json
            if ($Case.Name -eq 'wrong-source') {$Manifest.SourceCommit='1123456789abcdef0123456789abcdef01234567'}
            if ($Case.Name -eq 'wrong-version') {$Manifest.Version='1.0.0-rc.2'}
            if ($Case.Name -eq 'unexpected-name') {$Manifest.Files[0].Name='../outside.zip'}
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
            if ($Case.Name -eq 'uploaded-skipped-duplicate') {$Uploaded=@($Uploaded[0])}
            if ($Case.Name -eq 'uploaded-extra') {$Uploaded+=[pscustomobject]@{id=999;name='extra.zip';label=$null;size=1;digest=$null}}
            if ($Case.Name -eq 'uploaded-wrong-name') {$Uploaded[0].name='unexpected.zip'}
            if ($Case.Name -eq 'uploaded-wrong-hash') {$Uploaded[0].digest='sha256:' + ('0' * 64)}
            if ($Case.Name -eq 'uploaded-wrong-size') {$Uploaded[0].size++}
            if ($Case.Name -eq 'uploaded-reused-id') {$Uploaded[1].id=$Uploaded[0].id}
            if ($Case.Name -eq 'uploaded-remote-id-change') {$RemoteUploads[0].id++}
            if ($Case.Name -eq 'uploaded-remote-hash-change') {$RemoteUploads[0].digest='sha256:' + ('0' * 64)}
            $UploadParameters=@{VerifyUpload=$true;UploadedAssetsJson=(ConvertTo-Json -InputObject $Uploaded -Depth 5);ExpectedReleaseID=$(if ($Case.Name -eq 'uploaded-wrong-release') {52} else {51})}
        }
        $env:GH_TOKEN = if ($Case.Name -eq 'missing-token') {''} else {'synthetic-control-token'}
        $Receipt = Join-Path $Directory 'preflight.json'
        $Result = $null; $ErrorText = $null
        try { $Result = & $Guard -ManifestPath $Selection.ManifestPath -ManifestSHA256 $Hash -SourceCommit $Source -Reference $(if ($Case.Name -eq 'stable-reference') {'refs/tags/v1.0.0'} else {$Reference}) -Repository owned/fixture -ReceiptPath $Receipt @UploadParameters }
        catch { $ErrorText = $_.ToString() }
        if ($Case.ContainsKey('Error')) {
            if ($null -ne $Result -or $null -eq $ErrorText -or -not $ErrorText.StartsWith($Case.Error) -or (Test-Path -LiteralPath $Receipt)) { throw "Denied case $($Case.Name) differs from external refusal contract: $ErrorText" }
        }
        elseif ($null -ne $ErrorText -or $Result.SelectedAssets.Count -ne @($Names).Count+1 -or $Result.StablePromotionAuthorized -or $Result.CandidateQualified -or -not (Test-Path -LiteralPath $Receipt)) { throw "Positive case $($Case.Name) failed: $ErrorText" }
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
