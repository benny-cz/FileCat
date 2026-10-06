param(
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [switch]$RequireCleanCheckout
)
$ErrorActionPreference = 'Stop'
$Repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function ReadCommand([string]$Executable, [string]$Arguments, [int[]]$AllowedExitCodes = @(0)) {
    $Info = New-Object Diagnostics.ProcessStartInfo
    $Info.FileName = $Executable
    $Info.Arguments = $Arguments
    $Info.WorkingDirectory = $Repo
    $Info.UseShellExecute = $false
    $Info.CreateNoWindow = $true
    $Info.RedirectStandardOutput = $true
    $Info.RedirectStandardError = $true
    $Process = New-Object Diagnostics.Process
    $Process.StartInfo = $Info
    try {
        [void]$Process.Start()
        $Stdout = $Process.StandardOutput.ReadToEndAsync()
        $Stderr = $Process.StandardError.ReadToEndAsync()
        if (-not $Process.WaitForExit(30000)) {
            $Process.Kill()
            $Process.WaitForExit()
            throw "Build inventory command timed out: $Executable"
        }
        $Code = $Process.ExitCode
        $Text = $Stdout.GetAwaiter().GetResult()
        $ErrorText = $Stderr.GetAwaiter().GetResult()
        if ($Code -notin $AllowedExitCodes) { throw "Build inventory command failed: $Executable ($Code): $ErrorText" }
        return [pscustomobject]@{ ExitCode = $Code; Stdout = $Text }
    }
    finally { $Process.Dispose() }
}

$GlobalPath = Join-Path $Repo 'global.json'
$Selector = [IO.File]::ReadAllText($GlobalPath) | ConvertFrom-Json
$Selected = (ReadCommand 'dotnet' '--version').Stdout.Trim()
if ($Selector.sdk.rollForward -ne 'disable' -or $Selector.sdk.allowPrerelease -ne $false -or $Selected -ne $Selector.sdk.version) {
    throw 'The build must use the exact non-prerelease SDK in global.json.'
}
$Dirty = -not [string]::IsNullOrWhiteSpace((ReadCommand 'git' 'status --porcelain').Stdout)
if ($RequireCleanCheckout -and $Dirty) { throw 'Build provenance requires a clean source checkout.' }
# Only configuration key names are requested: credential/header values must never enter the receipt or logs.
$CredentialKeys = (ReadCommand 'git' 'config --local --name-only --get-regexp "(^http\..*\.extraheader$|^credential\.|^core\.sshCommand$|^include(if)?\.)"' @(0,1)).Stdout.Trim()
if ($RequireCleanCheckout -and -not [string]::IsNullOrWhiteSpace($CredentialKeys)) {
    throw 'The build checkout contains persisted authentication or included Git configuration.'
}
$Sdks = (ReadCommand 'dotnet' '--list-sdks').Stdout
$SdkLine = @($Sdks -split '\r?\n' | Where-Object { $_ -match ('^' + [regex]::Escape($Selected) + ' \[(.+)\]$') })
if ($SdkLine.Count -ne 1) { throw 'The selected SDK must have one resolved installation root.' }
[void]($SdkLine[0] -match '^\S+ \[(.+)\]$')
$SdkRoot = Join-Path $Matches[1] $Selected
$SdkFiles = @('dotnet.dll', 'MSBuild.dll', 'Roslyn/bincore/csc.dll') | ForEach-Object {
    $Path = Join-Path $SdkRoot $_
    $Item = Get-Item -LiteralPath $Path
    if (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'A compiler inventory file is linked.' }
    [pscustomobject]@{ Path = $_; Bytes = $Item.Length; SHA256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$Runner = [ordered]@{}
foreach ($Name in @('RUNNER_OS','RUNNER_ARCH','ImageOS','ImageVersion','GITHUB_RUN_ID','GITHUB_RUN_ATTEMPT','GITHUB_REF','GITHUB_SHA')) {
    $Runner[$Name] = [Environment]::GetEnvironmentVariable($Name)
}
$Receipt = [ordered]@{
    UTC = [DateTime]::UtcNow.ToString('o')
    SourceCommit = (ReadCommand 'git' 'rev-parse HEAD').Stdout.Trim()
    SourceTreeDirty = $Dirty
    ExactSDK = $Selected
    GlobalJsonSHA256 = (Get-FileHash -LiteralPath $GlobalPath -Algorithm SHA256).Hash.ToLowerInvariant()
    SDKRoot = $SdkRoot
    CompilerFiles = @($SdkFiles)
    DotnetInfo = (ReadCommand 'dotnet' '--info').Stdout
    InstalledSDKs = $Sdks
    InstalledRuntimes = (ReadCommand 'dotnet' '--list-runtimes').Stdout
    GitVersion = (ReadCommand 'git' '--version').Stdout.Trim()
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    OS = [Environment]::OSVersion.VersionString
    Runner = $Runner
    LocalAuthenticationConfigKeyNames = @($CredentialKeys -split '\r?\n' | Where-Object { $_ })
    HostedImageIsImmutable = $false
    CandidateQualified = $false
}
$Target = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $Target) { throw 'Build provenance output already exists.' }
[void][IO.Directory]::CreateDirectory((Split-Path -Parent $Target))
[IO.File]::WriteAllText($Target, ($Receipt | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Output "Recorded SDK $Selected build provenance: $Target"
