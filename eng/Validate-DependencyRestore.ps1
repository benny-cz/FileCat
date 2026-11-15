#Requires -Version 7
param([Parameter(Mandatory=$true)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $Evidence) { throw 'Dependency restore evidence already exists.' }
[void][IO.Directory]::CreateDirectory($Evidence)

function Capture([string]$Executable, [string[]]$Arguments, [string]$StdoutPath, [string]$StderrPath) {
    $Info = [Diagnostics.ProcessStartInfo]::new()
    $Info.FileName = $Executable
    $Info.WorkingDirectory = $Repo
    $Info.UseShellExecute = $false
    $Info.CreateNoWindow = $true
    $Info.RedirectStandardOutput = $true
    $Info.RedirectStandardError = $true
    foreach ($Argument in $Arguments) { [void]$Info.ArgumentList.Add($Argument) }
    $Process = [Diagnostics.Process]::new()
    $Process.StartInfo = $Info
    try {
        [void]$Process.Start()
        $Stdout = $Process.StandardOutput.ReadToEndAsync()
        $Stderr = $Process.StandardError.ReadToEndAsync()
        if (-not $Process.WaitForExit(240000)) {
            $Process.Kill($true)
            $Process.WaitForExit()
            throw "Dependency inventory command timed out: $Executable"
        }
        $Text = $Stdout.GetAwaiter().GetResult()
        $ErrorText = $Stderr.GetAwaiter().GetResult()
        if ($StdoutPath) { [IO.File]::WriteAllText($StdoutPath, $Text, [Text.UTF8Encoding]::new($false)) }
        if ($StderrPath) { [IO.File]::WriteAllText($StderrPath, $ErrorText, [Text.UTF8Encoding]::new($false)) }
        return [pscustomobject]@{ExitCode=$Process.ExitCode; Stdout=$Text; Stderr=$ErrorText}
    }
    finally { $Process.Dispose() }
}
function Pin([string]$Path) {
    $Item = Get-Item -LiteralPath $Path
    if (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Linked dependency input: $Path" }
    return [ordered]@{Bytes=$Item.Length; SHA256=(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
}
function GitText([string[]]$Arguments) {
    $Result = Capture 'git' $Arguments '' ''
    if ($Result.ExitCode -ne 0) { throw 'Cannot enumerate tracked dependency inputs.' }
    return $Result.Stdout
}
$Projects = @((GitText @('ls-files','-z','--','*.csproj')).Split([char]0) | Where-Object { $_ } | Sort-Object)
if ($Projects.Count -eq 0) { throw 'No tracked projects found.' }
$Selector = Get-Content -LiteralPath (Join-Path $Repo 'global.json') -Raw | ConvertFrom-Json
$SDK = Capture 'dotnet' @('--version') '' ''
if ($SDK.ExitCode -ne 0 -or $SDK.Stdout.Trim() -ne $Selector.sdk.version -or $Selector.sdk.rollForward -ne 'disable' -or $Selector.sdk.allowPrerelease) { throw 'The exact non-prerelease SDK is required.' }
[xml]$Props = Get-Content -LiteralPath (Join-Path $Repo 'Directory.Build.props') -Raw
$RIDs = @($Props.Project.PropertyGroup.RuntimeIdentifiers.Split(';'))
$Frameworks = @('net10.0') + @($RIDs | ForEach-Object { "net10.0/$_" })
$ExpectedFrameworks = ($Frameworks | Sort-Object) -join "`n"
$Inputs = @('Directory.Build.props','Directory.Build.targets','Directory.Packages.props','NuGet.Config','global.json') | ForEach-Object {
    $Identity = Pin (Join-Path $Repo $_)
    [ordered]@{Path=$_; Bytes=$Identity.Bytes; SHA256=$Identity.SHA256}
}
$Records = [Collections.Generic.List[object]]::new()
$ReceiptPath = Join-Path $Evidence 'dependency-restore.json'
$Receipt = [ordered]@{
    UTC=[DateTime]::UtcNow.ToString('o')
    SourceCommit=(GitText @('rev-parse','HEAD')).Trim()
    SourceTreeDirty=-not [string]::IsNullOrWhiteSpace((GitText @('status','--porcelain')))
    GITHUB_SHA=$env:GITHUB_SHA; GITHUB_RUN_ID=$env:GITHUB_RUN_ID; GITHUB_RUN_ATTEMPT=$env:GITHUB_RUN_ATTEMPT
    SDK=$SDK.Stdout.Trim(); PowerShell=$PSVersionTable.PSVersion.ToString(); ValidatorSHA256=(Pin $PSCommandPath).SHA256
    Inputs=@($Inputs); ExpectedFrameworks=$Frameworks; ProjectCount=$Projects.Count; Projects=@(); Complete=$false
    ExtractedPackageFilesQualified=$false; NativeRuntimeAndLicenseProvenanceQualified=$false; CandidateQualified=$false
}
# What one project's lock must hold, read from the project as MSBuild evaluates it rather than assumed for all: its target
# framework, one runtime-specific graph for each runtime it restores for, and the lock version its package management
# writes (2 under central package management, 1 outside it). Directory.Build.props gives every project the release
# runtimes above, so a project that adds or clears runtimes says so in its own file, where review sees it: the archive
# worker restores for linux-musl-arm64 alone and the libraries it references add that runtime, while the archive-writing
# validation tools, which nothing in the release references, restore for none. The receipt records each expectation.
function Expectation([string]$ProjectPath, [string]$ProjectEvidence) {
    $Arguments = @('msbuild',$ProjectPath,'-nologo','-getProperty:TargetFramework','-getProperty:TargetFrameworks','-getProperty:RuntimeIdentifiers','-getProperty:ManagePackageVersionsCentrally')
    $Result = Capture 'dotnet' $Arguments (Join-Path $ProjectEvidence 'evaluation.json') (Join-Path $ProjectEvidence 'evaluation-stderr.txt')
    if ($Result.ExitCode -ne 0) { throw "Cannot evaluate the project's dependency inputs: $ProjectPath" }
    $Properties = ($Result.Stdout | ConvertFrom-Json -AsHashtable).Properties
    $Framework = [string]$Properties['TargetFramework']
    if (-not $Framework -or $Framework.Contains(';') -or [string]$Properties['TargetFrameworks']) { throw "Expected exactly one target framework: $ProjectPath" }
    $ProjectRIDs = @(([string]$Properties['RuntimeIdentifiers']).Split(';') | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Sort-Object -Unique)
    return [ordered]@{
        Frameworks=@(@($Framework) + @($ProjectRIDs | ForEach-Object { "$Framework/$_" }) | Sort-Object)
        LockVersion=$(if ([string]$Properties['ManagePackageVersionsCentrally'] -eq 'true') { 2 } else { 1 })
        RuntimeIdentifiers=$ProjectRIDs
    }
}
function SaveReceipt {
    $Receipt.Projects = @($Records.ToArray())
    [IO.File]::WriteAllText($ReceiptPath, ($Receipt | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}
SaveReceipt
foreach ($Project in $Projects) {
    $ProjectPath = Join-Path $Repo $Project
    $Directory = Split-Path -Parent $ProjectPath
    $LockPath = Join-Path $Directory 'packages.lock.json'
    $ProjectEvidence = Join-Path $Evidence ($Project -replace '\.csproj$', '')
    [void][IO.Directory]::CreateDirectory($ProjectEvidence)
    $Expected = Expectation $ProjectPath $ProjectEvidence
    if (-not (Test-Path -LiteralPath $LockPath -PathType Leaf)) { throw "Missing dependency lock: $Project" }
    $LockPin = Pin $LockPath
    $Lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json -AsHashtable
    $LockFrameworks = ($Lock.dependencies.Keys | Sort-Object) -join ', '
    if ($Lock.version -ne $Expected.LockVersion -or $LockFrameworks -cne ($Expected.Frameworks -join ', ')) {
        throw "Unexpected dependency lock frameworks: $Project (lock version $($Lock.version): $LockFrameworks; the project restores version $($Expected.LockVersion): $($Expected.Frameworks -join ', '))"
    }
    [IO.File]::Copy($LockPath, (Join-Path $ProjectEvidence 'packages.lock.json'))
    $Arguments = @('restore',$ProjectPath,'--force','-p:RestoreLockedMode=true','-p:FileCatUpdateDependencyLocks=false')
    $Result = Capture 'dotnet' $Arguments (Join-Path $ProjectEvidence 'stdout.txt') (Join-Path $ProjectEvidence 'stderr.txt')
    $Entry = [ordered]@{Project=$Project; ProjectSHA256=(Pin $ProjectPath).SHA256; LockSHA256=$LockPin.SHA256; LockVersion=$Expected.LockVersion; ExpectedFrameworks=$Expected.Frameworks; Arguments=$Arguments; RestoreExitCode=$Result.ExitCode; StdoutSHA256=(Pin (Join-Path $ProjectEvidence 'stdout.txt')).SHA256; StderrSHA256=(Pin (Join-Path $ProjectEvidence 'stderr.txt')).SHA256; GraphVerified=$false}
    $Records.Add($Entry)
    SaveReceipt
    if ($Result.ExitCode -ne 0) { throw "Locked restore failed for $Project; retained evidence: $ProjectEvidence" }
    if ((Pin $LockPath).SHA256 -cne $LockPin.SHA256) { throw "Restore changed a dependency lock: $Project" }
    $AssetsPath = Join-Path $Directory 'obj/project.assets.json'
    $Assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json -AsHashtable
    foreach ($Framework in $Expected.Frameworks) {
        foreach ($Package in $Lock.dependencies[$Framework].GetEnumerator()) {
            if ($Package.Value.type -eq 'Project') { continue }
            $Key = "$($Package.Key)/$($Package.Value.resolved)"
            if (-not $Assets.targets[$Framework].Contains($Key) -or $Assets.libraries[$Key].sha512 -cne $Package.Value.contentHash) { throw "Actual restored package differs from lock: $Project $Framework $Key" }
        }
    }
    [IO.File]::Copy($AssetsPath, (Join-Path $ProjectEvidence 'project.assets.json'))
    $Entry.AssetsSHA256 = (Pin $AssetsPath).SHA256
    $Entry.GraphVerified = $true
    SaveReceipt
    Write-Output "Locked restore verified: $Project"
}
$Receipt.Complete = $true
SaveReceipt
Write-Output "Retained $($Projects.Count) actual locked restore graphs: $ReceiptPath"
