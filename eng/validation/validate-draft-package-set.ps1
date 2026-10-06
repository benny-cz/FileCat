param([string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$Eng=Split-Path $PSScriptRoot -Parent
$Parent=[IO.Path]::GetFullPath((Join-Path $Eng '../artifacts'))
[void][IO.Directory]::CreateDirectory($Parent)
$Root=Join-Path $Parent ('filecat-draft-set-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($Root)
$Source='0123456789abcdef0123456789abcdef01234567';$Version='1.0.0-rc.1'
$Contract=@{
    windows=@('FileCat-1.0.0-win-x64-setup.exe','FileCat-1.0.0-rc.1-win-x64-portable.zip','FileCat-1.0.0-rc.1-win-x64-fdd.zip','sbom-1.0.0-rc.1-win-x64.json','FileCat-1.0.0-win-arm64-setup.exe','FileCat-1.0.0-rc.1-win-arm64-portable.zip','FileCat-1.0.0-rc.1-win-arm64-fdd.zip','sbom-1.0.0-rc.1-win-arm64.json')
    linux=@('FileCat-1.0.0-rc.1-linux-x64.tar.gz','filecat_1.0.0~rc.1_amd64.deb','FileCat-1.0.0-rc.1-x86_64.AppImage')
    macos=@('FileCat-1.0.0-rc.1-osx-arm64.zip')
}
$Cases=@(
    @{Name='preflight-complete'},@{Name='upload-complete';Verify=$true},
    @{Name='missing-platform';Error='Cannot find path'},
    @{Name='wrong-manifest-pin';Error='Selected manifest bytes changed'},
    @{Name='changed-last-platform';Error='Selected package bytes changed'},
    @{Name='existing-last-platform';Error='An existing release asset conflicts'},
    @{Name='upload-missing';Verify=$true;Error='Combined action upload is incomplete'},
    @{Name='upload-extra';Verify=$true;Error='Combined action upload is incomplete'},
    @{Name='upload-reused-cross-platform-id';Verify=$true;Error='Combined action upload contains duplicate'},
    @{Name='upload-cross-platform-name';Verify=$true;Error='Combined action upload contains duplicate'},
    @{Name='upload-wrong-platform';Verify=$true;Error='The action did not upload the complete'},
    @{Name='upload-remote-last-platform-hash';Verify=$true;Error='Remote asset identity or bytes differ'},
    @{Name='stable-ref';Error='Stable references cannot invoke a producer'}
)
$OriginalToken=$env:GH_TOKEN
$Observations=[Collections.Generic.List[object]]::new()
function Invoke-RestMethod($Uri,$Headers,$Method,$TimeoutSec){
    if($Method -cne 'Get' -or $Headers.Authorization -cne 'Bearer synthetic-control-token'){throw 'Unexpected transport contract.'}
    $Suffix=$Uri.Substring('https://api.github.com/repos/owned/fixture/'.Length);$Calls.Add($Suffix)
    if($Suffix -ceq "git/ref/tags/v$Version"){return [pscustomobject]@{object=[pscustomobject]@{type='commit';sha=$Source}}}
    if($Suffix -ceq 'releases?per_page=100&page=1'){return @([pscustomobject]@{id=51;tag_name="v$Version";draft=$true;prerelease=$true;immutable=$false})}
    if($Suffix -ceq 'releases/51/assets?per_page=100&page=1'){
        if($Case.ContainsKey('Verify')){return $Remote}
        if($Case.Name -eq 'existing-last-platform'){return @($Remote | Where-Object {$_.name -ceq 'FileCat-1.0.0-rc.1-osx-arm64.zip'})}
        return @()
    }
    throw 'Unexpected substituted API path.'
}
try{
    $env:GH_TOKEN='synthetic-control-token'
    foreach($Case in $Cases){
        $Directory=Join-Path $Root $Case.Name;[void][IO.Directory]::CreateDirectory($Directory)
        $Hashes=@{};$Uploaded=[Collections.Generic.List[object]]::new();$ExpectedPaths=[Collections.Generic.List[string]]::new()
        foreach($Platform in @('windows','linux','macos')){
            $PlatformRoot=Join-Path $Directory $Platform;[void][IO.Directory]::CreateDirectory($PlatformRoot)
            foreach($Name in $Contract[$Platform]){[IO.File]::WriteAllText((Join-Path $PlatformRoot $Name),"owned external package set: $Name")}
            $Selection=& (Join-Path $Eng 'Select-ReleaseAssets.ps1') -Platform $Platform -Version $Version -SourceCommit $Source -ArtifactDirectory $PlatformRoot
            $Hashes[$Platform]=(Get-FileHash -LiteralPath $Selection.ManifestPath).Hash.ToLowerInvariant()
            foreach($FilePath in $Selection.Files){
                $File=Get-Item -LiteralPath $FilePath
                $Uploaded.Add([pscustomobject]@{id=201+$Uploaded.Count;name=$File.Name;label=$null;size=$File.Length;digest=('sha256:'+(Get-FileHash -LiteralPath $File.FullName).Hash.ToLowerInvariant())})
                $ExpectedPaths.Add($FilePath.Replace('\','/'))
            }
        }
        $Remote=@($Uploaded | ForEach-Object {[pscustomobject]@{id=$_.id;name=$_.name;label=$_.label;size=$_.size;digest=$_.digest}})
        if($Case.Name -eq 'missing-platform'){[IO.File]::Delete((Join-Path $Directory "macos/FileCat-$Version-macos-assets.json"))}
        if($Case.Name -eq 'wrong-manifest-pin'){$Hashes.macos='0'*64}
        if($Case.Name -eq 'changed-last-platform'){[IO.File]::AppendAllText((Join-Path $Directory "macos/FileCat-$Version-osx-arm64.zip"),'changed')}
        if($Case.Name -eq 'upload-missing'){$Uploaded.RemoveAt($Uploaded.Count-1)}
        if($Case.Name -eq 'upload-extra'){$Uploaded.Add([pscustomobject]@{id=999;name='extra.zip';label=$null;size=1;digest=$null})}
        if($Case.Name -eq 'upload-reused-cross-platform-id'){$Uploaded[9].id=$Uploaded[0].id}
        if($Case.Name -eq 'upload-cross-platform-name'){$Uploaded[9].name=$Uploaded[0].name}
        if($Case.Name -eq 'upload-wrong-platform'){$Uploaded[9].name='wrong-platform.zip'}
        if($Case.Name -eq 'upload-remote-last-platform-hash'){$Remote[-1].digest='sha256:'+('0'*64)}
        $Arguments=@{ArtifactDirectory=$Directory;SourceCommit=$Source;Reference=$(if($Case.Name -eq 'stable-ref'){'refs/tags/v1.0.0'}else{"refs/tags/v$Version"});Repository='owned/fixture';WindowsManifestSHA256=$Hashes.windows;LinuxManifestSHA256=$Hashes.linux;MacOSManifestSHA256=$Hashes.macos;ReceiptDirectory=(Join-Path $Directory 'receipts')}
        $Output=Join-Path $Directory 'github-output.txt'
        if($Case.ContainsKey('Verify')){$Arguments.VerifyUpload=$true;$Arguments.ExpectedReleaseID=51;$Arguments.UploadedAssetsJson=ConvertTo-Json -InputObject @($Uploaded.ToArray()) -Depth 7}
        else{$Arguments.GitHubOutputPath=$Output}
        $Calls=[Collections.Generic.List[string]]::new();$Result=$null;$ErrorText=$null
        try{$Result=& (Join-Path $Eng 'Invoke-DraftPackageChecks.ps1') @Arguments}
        catch{$ErrorText=$_.ToString()}
        if($Case.ContainsKey('Error')){
            if($null -ne $Result -or $null -eq $ErrorText -or -not $ErrorText.StartsWith($Case.Error) -or (Test-Path -LiteralPath $Output)){throw "Package set refusal $($Case.Name) failed: $ErrorText"}
        }
        else{
            if($null -ne $ErrorText -or $Result.Files.Count -ne 15 -or ($Result.Files -join "`n") -cne ($ExpectedPaths -join "`n") -or $Result.PlatformReceipts.Count -ne 3 -or $Result.StablePromotionAuthorized -or $Result.CandidateQualified){throw "Complete package set failed: $ErrorText"}
            if(-not $Case.ContainsKey('Verify')){
                $Lines=Get-Content -LiteralPath $Output
                if($Lines.Count -ne 17 -or $Lines[0] -cne ('files<<'+$Lines[-1]) -or ($Lines[1..15] -join "`n") -cne ($ExpectedPaths -join "`n")){throw 'Combined action list differs from exact package set.'}
            }
        }
        $Observations.Add([ordered]@{Case=$Case.Name;Expected=$(if($Case.ContainsKey('Error')){'refused'}else{'allowed'});Error=$ErrorText;Result=$Result;ReadOnlySubstitutedAPIPaths=@($Calls.ToArray());NoUploadOrRebuild=$true;Passed=$true})
    }
    $Record=[ordered]@{UTC=[DateTime]::UtcNow.ToString('o');WrapperSHA256=(Get-FileHash -LiteralPath (Join-Path $Eng 'Invoke-DraftPackageChecks.ps1')).Hash.ToLowerInvariant();ValidatorSHA256=(Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant();GITHUB_SHA=$env:GITHUB_SHA;GITHUB_RUN_ID=$env:GITHUB_RUN_ID;GITHUB_RUN_ATTEMPT=$env:GITHUB_RUN_ATTEMPT;Pass=$Observations.Count;Cases=@($Observations.ToArray());SyntheticFixtures=$true;ActualGitHubAPIUsed=$false;ActualTagOrReleaseCreated=$false;StablePromotionAuthorized=$false}
    if($EvidenceDirectory){[void][IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($EvidenceDirectory));[IO.File]::WriteAllText((Join-Path $EvidenceDirectory 'draft-package-set-controls.json'),($Record | ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))}
    $Record | ConvertTo-Json -Depth 12
}
finally{
    $env:GH_TOKEN=$OriginalToken
    $Resolved=[IO.Path]::GetFullPath($Root)
    if([IO.Path]::GetDirectoryName($Resolved) -ne $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar) -or [IO.Path]::GetFileName($Resolved) -notmatch '^filecat-draft-set-[0-9a-f]{32}$' -or ((Get-Item -LiteralPath $Resolved).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Unexpected cleanup root.'}
    [IO.Directory]::Delete($Resolved,$true)
}
