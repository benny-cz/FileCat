param([string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Policy = Join-Path (Split-Path $PSScriptRoot -Parent) 'Assert-ProducerReference.ps1'
# External event/reference contract. These are synthetic names; no tag/ref or release API is used.
$Cases = @(
    @{Reference='refs/heads/main';EventName='push';Mode='development'},
    @{Reference='refs/heads/codex/package-validation';EventName='workflow_dispatch';Mode='development'},
    @{Reference='refs/pull/123/merge';EventName='pull_request';Mode='development'},
    @{Reference='refs/pull/123/head';EventName='pull_request';Mode='development'},
    @{Reference='refs/tags/v1.0.0-rc.1';EventName='push';Mode='prerelease'},
    @{Reference='refs/tags/v0.1.0-preview';EventName='workflow_dispatch';Mode='prerelease'},
    @{Reference='refs/tags/v1.0.0';EventName='push';Refused=$true},
    @{Reference='refs/tags/v1.0.0';EventName='workflow_dispatch';Refused=$true},
    @{Reference='refs/tags/v1.0.0+build.1';EventName='push';Refused=$true},
    @{Reference='refs/tags/v01.0.0-rc.1';EventName='push';Refused=$true},
    @{Reference='refs/tags/1.0.0-rc.1';EventName='push';Refused=$true},
    @{Reference='refs/tags/v1.0.0-rc..1';EventName='push';Refused=$true},
    @{Reference='refs/heads/';EventName='push';Refused=$true},
    @{Reference='refs/heads/main';EventName='pull_request';Refused=$true},
    @{Reference='refs/tags/v1.0.0-rc.1';EventName='pull_request';Refused=$true},
    @{Reference="refs/tags/v1.0.0-rc.1`nrefs/heads/main";EventName='push';Refused=$true}
)
$Results = foreach ($Case in $Cases) {
    $Result = $null; $ErrorText = $null
    try { $Result = & $Policy -Reference $Case.Reference -EventName $Case.EventName }
    catch { $ErrorText = $_.ToString() }
    $RefusalExpected = $Case.ContainsKey('Refused')
    if ($RefusalExpected) {
        if ($null -eq $ErrorText -or $null -ne $Result) { throw 'A denied producer reference emitted an eligibility result.' }
    }
    elseif ($null -ne $ErrorText -or $Result.Mode -cne $Case.Mode -or $Result.StablePromotionAuthorized) {
        throw 'An allowed development/prerelease reference differs from the external contract.'
    }
    [ordered]@{Reference=$Case.Reference;EventName=$Case.EventName;ExpectedMode=$(if ($RefusalExpected) {'refused'} else {$Case.Mode});Result=$Result;Error=$ErrorText;Passed=$true}
}
$Record = [ordered]@{
    UTC=[DateTime]::UtcNow.ToString('o');PolicySHA256=(Get-FileHash -LiteralPath $Policy).Hash.ToLowerInvariant()
    ValidatorSHA256=(Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant();PowerShell=$PSVersionTable.PSVersion.ToString()
    GITHUB_SHA=$env:GITHUB_SHA;GITHUB_RUN_ID=$env:GITHUB_RUN_ID;GITHUB_RUN_ATTEMPT=$env:GITHUB_RUN_ATTEMPT
    Cases=@($Results);Pass=@($Results).Count;SyntheticReferences=$true;ActualTagCreated=$false;PublicationOrPromotionAuthorized=$false
}
if ($EvidenceDirectory) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($EvidenceDirectory))
    [IO.File]::WriteAllText((Join-Path $EvidenceDirectory 'producer-reference-controls.json'),($Record | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
}
$Record | ConvertTo-Json -Depth 8
