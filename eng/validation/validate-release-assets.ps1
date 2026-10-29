param([string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Selector = Join-Path (Split-Path $PSScriptRoot -Parent) 'Select-ReleaseAssets.ps1'
$TemporaryBase = if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    $env:RUNNER_TEMP
}
elseif ($IsMacOS) {
    [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
}
else {
    [IO.Path]::GetTempPath()
}
$TemporaryParent = [IO.Path]::GetFullPath((Join-Path $TemporaryBase 'FileCatValidation'))
[void][IO.Directory]::CreateDirectory($TemporaryParent)
$TestRoot = Join-Path $TemporaryParent ('filecat-release-assets-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($TestRoot)
$Commit = '0123456789abcdef0123456789abcdef01234567'
$Version = '1.0.0-rc.1-extra'
$Contract = @{
    windows=@('FileCat-1.0.0-win-x64-setup.exe','FileCat-1.0.0-rc.1-extra-win-x64-portable.zip','FileCat-1.0.0-rc.1-extra-win-x64-fdd.zip','sbom-1.0.0-rc.1-extra-win-x64-setup-payload.spdx.json','sbom-1.0.0-rc.1-extra-win-x64-portable.spdx.json','sbom-1.0.0-rc.1-extra-win-x64-fdd.spdx.json',
        'FileCat-1.0.0-win-arm64-setup.exe','FileCat-1.0.0-rc.1-extra-win-arm64-portable.zip','FileCat-1.0.0-rc.1-extra-win-arm64-fdd.zip','sbom-1.0.0-rc.1-extra-win-arm64-setup-payload.spdx.json','sbom-1.0.0-rc.1-extra-win-arm64-portable.spdx.json','sbom-1.0.0-rc.1-extra-win-arm64-fdd.spdx.json')
    linux=@('FileCat-1.0.0-rc.1-extra-linux-x64.tar.gz','filecat_1.0.0-rc.1-extra_amd64.deb','FileCat-1.0.0-rc.1-extra-x86_64.AppImage','FileCat-1.0.0-rc.1-extra-linux-x64-appimage-runtime-source.tar.gz','sbom-1.0.0-rc.1-extra-linux-x64-tar.spdx.json','sbom-1.0.0-rc.1-extra-linux-x64-deb.spdx.json','sbom-1.0.0-rc.1-extra-linux-x64-appimage.spdx.json')
    macos=@('FileCat-1.0.0-rc.1-extra-osx-arm64.zip','sbom-1.0.0-rc.1-extra-osx-arm64-macos-app.spdx.json')
}
$Observations = [Collections.Generic.List[object]]::new()
try {
    foreach ($Platform in @('windows','linux','macos')) {
        $Root = Join-Path $TestRoot $Platform
        [void][IO.Directory]::CreateDirectory($Root)
        $Names = $Contract[$Platform]
        # GitHub renames uploaded assets whose names contain other characters (such as a Debian version's '~'), which
        # the exact post-upload name check would then refuse.
        if (@($Names | Where-Object { $_ -cnotmatch '^[0-9A-Za-z][0-9A-Za-z._-]*[0-9A-Za-z]$' }).Count) { throw 'A release asset name contains characters GitHub renames.' }
        foreach ($Name in $Names) { [IO.File]::WriteAllBytes((Join-Path $Root $Name), [Text.Encoding]::UTF8.GetBytes("owned contract fixture: $Name")) }
        $ExtraName = switch ($Platform) { windows {'unrelated-probe.exe'} linux {'unrelated-probe.AppImage'} macos {'unrelated-probe.zip'} }
        $Extra = Join-Path $Root $ExtraName
        [IO.File]::WriteAllText($Extra, 'must never be selected')
        $Output = Join-Path $Root 'github-output.txt'
        $Result = & $Selector -Platform $Platform -Version $Version -SourceCommit $Commit -ArtifactDirectory $Root -GitHubOutputPath $Output
        $Manifest = Get-Content -LiteralPath $Result.ManifestPath -Raw | ConvertFrom-Json
        if (($Manifest.Files.Name -join "`n") -cne ($Names -join "`n") -or $Result.Files.Count -ne $Names.Count + 1) { throw 'Selected assets differ from the external filename contract.' }
        foreach ($Pin in $Manifest.Files) {
            $Bytes = [IO.File]::ReadAllBytes((Join-Path $Root $Pin.Name))
            if ($Pin.Bytes -ne $Bytes.Length -or $Pin.SHA256 -cne [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()) { throw 'Manifest does not describe actual fixture bytes.' }
        }
        if ($Manifest.SourceCommit -cne $Commit -or $Manifest.CandidateQualified -or $Manifest.SigningVerified) { throw 'Manifest identity or evidence classification is incorrect.' }
        $Emitted = Get-Content -LiteralPath $Output
        if ($Emitted.Count -ne $Names.Count + 5 -or $Emitted[0] -cne ('files<<' + $Emitted[-3]) -or ($Emitted[1..($Emitted.Count-4)] -join "`n") -cne ($Result.Files -join "`n") -or $Emitted -match 'unrelated-probe') { throw 'Action file list differs from the manifest allowlist.' }
        if ($Emitted[-2] -cne "manifest_path=$($Result.ManifestPath.Replace('\','/'))" -or $Emitted[-1] -cne "manifest_sha256=$((Get-FileHash -LiteralPath $Result.ManifestPath).Hash.ToLowerInvariant())") { throw 'Manifest output identity differs from selected bytes.' }
        $Original = [IO.File]::ReadAllBytes($Result.ManifestPath)
        $Refused = $false
        try { & $Selector -Platform $Platform -Version $Version -SourceCommit $Commit -ArtifactDirectory $Root | Out-Null }
        catch { $Refused = $true }
        if (-not $Refused -or [Convert]::ToHexString([IO.File]::ReadAllBytes($Result.ManifestPath)) -cne [Convert]::ToHexString($Original)) { throw 'Existing manifest was not preserved.' }
        $Observations.Add(@{Platform=$Platform;Assets=$Names.Count;HashesVerified=$true;ExtraExcluded=$true;ExistingManifestPreserved=$true;GitHubSafeNames=$true})
    }
    foreach ($Kind in @('missing','empty','directory','invalid-version','glob-root')) {
        $Root = Join-Path $TestRoot $(if ($Kind -eq 'glob-root') { 'glob[fixture]' } else { $Kind })
        [void][IO.Directory]::CreateDirectory($Root)
        $Name = $Contract.macos[0]
        if ($Kind -eq 'empty') { [IO.File]::WriteAllBytes((Join-Path $Root $Name), @()) }
        if ($Kind -eq 'directory') { [void][IO.Directory]::CreateDirectory((Join-Path $Root $Name)) }
        if ($Kind -in @('invalid-version','glob-root')) { [IO.File]::WriteAllText((Join-Path $Root $Name), 'owned fixture') }
        $Output = Join-Path $Root 'github-output.txt'
        $Refused = $false
        try { & $Selector -Platform macos -Version $(if ($Kind -eq 'invalid-version') { '../1.0.0' } else { $Version }) -SourceCommit $Commit -ArtifactDirectory $Root -GitHubOutputPath $Output | Out-Null }
        catch { $Refused = $true }
        if (-not $Refused -or (Test-Path -LiteralPath $Output) -or @(Get-ChildItem -LiteralPath $Root -Filter '*-assets.json').Count) { throw "Incomplete/invalid set $Kind did not refuse before output." }
        $Observations.Add(@{Case=$Kind;RefusedBeforeOutput=$true})
    }
    $Record = @{UTC=[DateTime]::UtcNow.ToString('o');SelectorSHA256=(Get-FileHash -LiteralPath $Selector).Hash.ToLowerInvariant();ValidatorSHA256=(Get-FileHash -LiteralPath $PSCommandPath).Hash.ToLowerInvariant();PowerShell=$PSVersionTable.PSVersion.ToString();GITHUB_SHA=$env:GITHUB_SHA;GITHUB_RUN_ID=$env:GITHUB_RUN_ID;GITHUB_RUN_ATTEMPT=$env:GITHUB_RUN_ATTEMPT;SyntheticFixtures=$true;ActualPackagesQualified=$false;Cases=@($Observations.ToArray());Pass=$Observations.Count}
    if ($EvidenceDirectory) {
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($EvidenceDirectory))
        [IO.File]::WriteAllText((Join-Path $EvidenceDirectory 'release-assets-controls.json'), ($Record | ConvertTo-Json -Depth 7), [Text.UTF8Encoding]::new($false))
    }
    $Record | ConvertTo-Json -Depth 7
}
finally {
    $Resolved = [IO.Path]::GetFullPath($TestRoot)
    if ([IO.Path]::GetDirectoryName($Resolved) -ne $TemporaryParent.TrimEnd([IO.Path]::DirectorySeparatorChar) -or [IO.Path]::GetFileName($Resolved) -notmatch '^filecat-release-assets-[0-9a-f]{32}$' -or ((Get-Item -LiteralPath $Resolved).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected cleanup root.' }
    [IO.Directory]::Delete($Resolved, $true)
}
