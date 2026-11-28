#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z')][string]$Version,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [switch]$IsolatedMachine,
    [switch]$CheckGui
)

$ErrorActionPreference = 'Stop'
if (-not $IsolatedMachine -or -not [Environment]::Is64BitProcess) {
    throw 'Run from 64-bit PowerShell on a disposable Windows test machine only.'
}
$os = Get-CimInstance Win32_OperatingSystem
if ($CheckGui -and ($os.ProductType -ne 1 -or [int]$os.BuildNumber -lt 22000 -or (Get-Process -Id $PID).SessionId -eq 0)) {
    throw 'GUI qualification requires an interactive Windows 11 desktop session.'
}
$registry = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6F3C2B5E-9A41-4E57-8C5E-1F2A3B4C5D6E}_is1'
$appDirectory = Join-Path $env:ProgramFiles 'FileCat'
$app = Join-Path $appDirectory 'FileCat.exe'
$roaming = Join-Path $env:APPDATA 'FileCat'
$local = Join-Path $env:LOCALAPPDATA 'FileCat'
$choco = (Get-Command choco.exe -CommandType Application).Source
$installedPackages = & $choco list filecat --exact --limit-output
if ($LASTEXITCODE -ne 0 -or $installedPackages -match '^filecat\|' -or
    (@($registry, $appDirectory, $roaming, $local) | Where-Object { Test-Path -LiteralPath $_ }) -or
    (Get-Process FileCat -ErrorAction SilentlyContinue)) {
    throw 'The test requires a clean VM and profile with no existing FileCat installation, package, or data.'
}

$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$EvidenceDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$package = Join-Path $PackageDirectory "filecat.$Version.nupkg"
$receipt = Get-Content -Raw -LiteralPath (Join-Path $PackageDirectory "filecat.$Version.json") | ConvertFrom-Json
$report = [ordered]@{
    Result = 'Failed'; OS = $os.Caption; OSVersion = $os.Version
    UBR = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').UBR
    Architecture = $env:PROCESSOR_ARCHITECTURE; PowerShell = $PSVersionTable.PSVersion.ToString()
    Chocolatey = (& $choco --version | Select-Object -First 1)
    PackageSha256 = (Get-FileHash -LiteralPath $package).Hash
    Commands = [Collections.Generic.List[object]]::new()
    Gui = 'Untested'; VersionToVersionUpgrade = 'Untested'
}

function Invoke-ChocoStep([string]$Step, [string[]]$Arguments) {
    $log = Join-Path $EvidenceDirectory "$Step.log"
    $command = [ordered]@{ Arguments = $Arguments; ExitCode = $null; Log = "$Step.log"; InstallerLog = $null }
    $report.Commands.Add($command)
    $Arguments = $Arguments + "--log-file=$log"
    & $choco @Arguments | ForEach-Object {
        if ($_ -match '^FileCat (?:Setup|uninstall) log: (?<path>.+\.log)$') { $command.InstallerLog = $Matches['path'] }
        Write-Host $_
    }
    $command.ExitCode = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw "Chocolatey $Step exited with code $LASTEXITCODE. See $log." }
}

function Assert-Installation {
    $arp = Get-ItemProperty -LiteralPath $registry
    $versionInfo = (Get-Item -LiteralPath $app).VersionInfo
    if ($arp.DisplayVersion -ne $Version -or $arp.Publisher -cne $publisher -or
        $arp.InstallLocation.TrimEnd('\') -ine $appDirectory -or
        $versionInfo.ProductVersion -notmatch ('^' + [regex]::Escape($Version) + '(?:\+.*)?$')) {
        throw 'Installed FileCat registry or executable metadata differs from the package.'
    }
    $registered = $arp.UninstallString.Trim('"')
    if ([IO.Path]::GetFileName($registered) -ne 'unins001.exe') {
        throw "Expected Setup to choose an alternate uninstaller; registered: $registered"
    }
    foreach ($path in @($app, $registered)) {
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $receipt.SignerThumbprint) {
            throw "The installed file has an unexpected signature: $path"
        }
    }
    $report.Installation = $arp | Select-Object DisplayName,DisplayVersion,Publisher,InstallLocation,UninstallString
    $report.InstalledProductVersion = $versionInfo.ProductVersion
}

try {
    if ($report.PackageSha256 -cne $receipt.PackageSha256 -or $receipt.Tag -cne "v$Version" -or $receipt.Authenticode -ne 'Valid') {
        throw 'Package does not match its verified build receipt.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $reader = [IO.StreamReader]::new($archive.GetEntry('filecat.nuspec').Open())
        try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $manifest.package.metadata
        $publisher = $metadata.authors
        $reader = [IO.StreamReader]::new($archive.GetEntry('tools/chocolateyInstall.ps1').Open())
        try { $installScript = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($metadata.id -cne 'filecat' -or $metadata.version -cne $Version -or $metadata.title -cne 'FileCat' -or
            -not $archive.GetEntry('tools/chocolateyUninstall.ps1') -or -not $archive.GetEntry('tools/.skipAutoUninstall') -or
            -not $installScript.Contains($receipt.InstallerUrl) -or -not $installScript.Contains($receipt.InstallerSha256) -or
            ($archive.Entries | Where-Object Name -Like '*.exe')) {
            throw 'Unexpected .nupkg metadata or contents.'
        }
    }
    finally { $archive.Dispose() }
    $report.PackageMetadata = 'Passed'

    # An occupied Inno filename forces Setup to register unins001.exe and doubles as the unrelated-file fixture.
    New-Item -ItemType Directory -Path $appDirectory | Out-Null
    $unrelated = Join-Path $appDirectory 'unins000.dat'
    Set-Content -LiteralPath $unrelated -Value 'Unrelated test file' -Encoding ASCII
    $unrelatedHash = (Get-FileHash -LiteralPath $unrelated).Hash
    $installArgs = @('install', 'filecat', "--version=$Version", "--source=$PackageDirectory", '--yes', '--no-progress')
    Invoke-ChocoStep 'install' $installArgs
    Assert-Installation

    New-Item -ItemType Directory -Path $roaming,(Join-Path $local 'journal') -Force | Out-Null
    $settings = Join-Path $roaming 'settings.json'
    $history = Join-Path $roaming 'history.json'
    $journal = Join-Path $local 'journal/retained-test.txt'
    Set-Content -LiteralPath $settings -Value '{"SchemaVersion":1,"Theme":"ClassicDark","CheckForUpdates":false}' -Encoding ASCII
    Set-Content -LiteralPath $journal -Value 'Retained test journal' -Encoding ASCII
    if ($CheckGui) {
        $process = Start-Process -FilePath $app -PassThru
        try {
            $deadline = (Get-Date).AddSeconds(60)
            do {
                Start-Sleep -Milliseconds 250
                $process.Refresh()
            } until ($process.HasExited -or $process.MainWindowHandle -ne 0 -or (Get-Date) -ge $deadline)
            if ($process.HasExited -or $process.MainWindowTitle -notmatch 'FileCat') { throw 'FileCat did not open a main window.' }
            $report.GuiWindow = $process.MainWindowTitle
            $report.GuiSession = $process.SessionId
            if (-not $process.CloseMainWindow() -or -not $process.WaitForExit(30000) -or $process.ExitCode -ne 0) {
                throw 'FileCat did not close cleanly after the startup check.'
            }
            $report.Gui = 'Passed'
        }
        finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force } }
    }
    $preferences = Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
    if ($preferences.Theme -ne 'ClassicDark' -or $preferences.CheckForUpdates -ne $false) { throw 'Unexpected test settings.' }
    Set-Content -LiteralPath $history -Value '{"SchemaVersion":1,"SearchNames":["retention-fixture"]}' -Encoding ASCII
    $fixtures = Get-FileHash -LiteralPath $settings,$history,$journal

    Invoke-ChocoStep 'same-version-reinstall' ($installArgs + '--force')
    Assert-Installation
    foreach ($fixture in $fixtures) {
        if ((Get-FileHash -LiteralPath $fixture.Path).Hash -ne $fixture.Hash) { throw "Reinstall changed $($fixture.Path)." }
    }
    $report.SameVersionReinstall = 'Passed'

    Invoke-ChocoStep 'uninstall' @('uninstall', 'filecat', "--version=$Version", '--yes', '--no-progress')
    $installedPackages = & $choco list filecat --exact --limit-output
    if ($LASTEXITCODE -ne 0 -or $installedPackages -match '^filecat\|' -or
        (Test-Path -LiteralPath $registry) -or ((Get-ChildItem -LiteralPath $appDirectory -Force).Name -join ',') -ne 'unins000.dat') {
        throw 'Uninstall did not remove the package, product registration or installed files.'
    }
    foreach ($fixture in $fixtures) {
        if ((Get-FileHash -LiteralPath $fixture.Path).Hash -ne $fixture.Hash) { throw "Uninstall changed $($fixture.Path)." }
    }
    if ((Get-FileHash -LiteralPath $unrelated).Hash -ne $unrelatedHash) { throw 'Uninstall changed the unrelated file.' }
    $report.UninstallAndDataRetention = 'Passed'
    $report.Result = 'Passed'
}
catch {
    $report.Error = $_.ToString()
    throw
}
finally {
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'validation.json') -Encoding UTF8
    foreach ($command in $report.Commands) {
        if ($command.InstallerLog -and (Test-Path -LiteralPath $command.InstallerLog -PathType Leaf)) {
            Copy-Item -LiteralPath $command.InstallerLog -Destination $EvidenceDirectory
        }
    }
}
