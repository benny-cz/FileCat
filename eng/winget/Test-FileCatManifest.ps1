<# Downloads an immutable stable release and tests its WinGet manifest on an isolated GitHub Actions Windows runner. #>
param(
    [Parameter(Mandatory)][ValidatePattern('^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')][string]$Tag,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false # Every native exit code is checked explicitly below.
Set-StrictMode -Version Latest
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') {
    throw 'Installation tests run only on the isolated GitHub Actions Windows runner.'
}

$version = $Tag.Substring(1)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$evidenceDirectory = Join-Path $OutputDirectory 'evidence'
New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
$registryPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6F3C2B5E-9A41-4E57-8C5E-1F2A3B4C5D6E}_is1'
$installDirectory = Join-Path $env:ProgramFiles 'FileCat'
$uninstaller = Join-Path $installDirectory 'unins000.exe'
$report = [ordered]@{
    Tag = $Tag; Result = 'Failed'; RunnerOS = [Environment]::OSVersion.VersionString
    RunUrl = "https://github.com/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID"
    Commands = [Collections.Generic.List[object]]::new()
}
$installationAttempted = $false
$installerSigner = $null
$testFailure = $null

function Invoke-WinGet([string[]]$Arguments) {
    $log = Join-Path $evidenceDirectory "winget-$($report.Commands.Count + 1).log"
    & $script:wingetPath @Arguments 2>&1 | Tee-Object -FilePath $log | Out-Host
    $exitCode = $LASTEXITCODE
    $report.Commands.Add(@{ Arguments = $Arguments; ExitCode = $exitCode; Log = [IO.Path]::GetFileName($log) })
    if ($exitCode -ne 0) { throw "winget $($Arguments[0]) failed with exit code $exitCode; see $log." }
}

function Get-TrustedSignature([string]$File, [string]$ExpectedSigner) {
    $signature = Get-AuthenticodeSignature -LiteralPath $File
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate) {
        throw "Invalid Authenticode signature on ${File}: $($signature.Status)."
    }
    $signer = $signature.SignerCertificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
    if ($ExpectedSigner -and $signer -cne $ExpectedSigner) { throw "Unexpected signer on $File." }
    return @{ Status = [string]$signature.Status; Subject = $signature.SignerCertificate.Subject; CertificateSha256 = $signer }
}

function Find-WinGet {
    $command = Get-Command winget.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    # A freshly registered App Installer may not yet have a command alias on PATH.
    $package = Get-AppxPackage -Name Microsoft.DesktopAppInstaller | Sort-Object Version -Descending | Select-Object -First 1
    if ($package) { return Join-Path $package.InstallLocation 'winget.exe' }
}

function Test-WinGetVersion([string]$Executable) {
    if (-not $Executable) { return $false }
    $output = (& $Executable --version 2>&1 | Out-String).Trim()
    $exitCode = $LASTEXITCODE
    $report.WinGetVersion = $output
    return $exitCode -eq 0 -and $output -match '^v?\d+\.\d+\.\d+$' -and [version]$output.TrimStart('v') -ge [version]'1.28.0'
}

try {
    if ((Test-Path -LiteralPath $registryPath) -or (Test-Path -LiteralPath $installDirectory)) {
        throw 'FileCat is already present; refusing to modify an existing installation.'
    }
    $headers = @{ Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
    if ($env:GITHUB_TOKEN) { $headers.Authorization = "Bearer $env:GITHUB_TOKEN" }
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/benny-cz/FileCat/releases/tags/$Tag" -Headers $headers
    if ($release.tag_name -cne $Tag -or $release.draft -or $release.prerelease -or -not $release.immutable) {
        throw 'Only the exact published, immutable, non-prerelease tag is accepted.'
    }
    $assetName = "FileCat-$version-win-x64-setup.exe"
    $assets = @($release.assets | Where-Object name -CEQ $assetName)
    if ($assets.Count -ne 1) { throw "Expected exactly one $assetName release asset." }
    $asset = $assets[0]
    $installerUrl = "https://github.com/benny-cz/FileCat/releases/download/$Tag/$assetName"
    if ($asset.browser_download_url -cne $installerUrl -or $asset.digest -notmatch '^sha256:[a-fA-F0-9]{64}$') {
        throw 'The release asset has an unexpected URL or lacks its SHA-256 digest.'
    }
    $installer = Join-Path $OutputDirectory $assetName
    Invoke-WebRequest -Uri $installerUrl -OutFile $installer
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    if ($hash -ine $asset.digest.Substring(7) -or (Get-Item -LiteralPath $installer).Length -ne $asset.size) {
        throw 'The downloaded installer differs from the immutable release digest or size.'
    }
    $report.Release = @{ Url = $release.html_url; Immutable = $release.immutable; AssetId = $asset.id; InstallerUrl = $installerUrl; Sha256 = $hash; Bytes = $asset.size }
    $report.InstallerSignature = Get-TrustedSignature $installer
    $installerSigner = $report.InstallerSignature.CertificateSha256

    $script:wingetPath = Find-WinGet
    if (-not (Test-WinGetVersion $script:wingetPath)) {
        # Microsoft's documented bootstrap; no preview builds, Store agreements or publication credentials.
        Install-PackageProvider -Name NuGet -Force | Out-Null
        Install-Module -Name Microsoft.WinGet.Client -Force -Repository PSGallery -Scope CurrentUser
        Import-Module Microsoft.WinGet.Client
        Repair-WinGetPackageManager -AllUsers -Latest | Out-Host
        $script:wingetPath = Find-WinGet
        if (-not (Test-WinGetVersion $script:wingetPath)) { throw 'A stable WinGet >= 1.28.0 is required.' }
    }

    $generate = @{ Version = $version; InstallerSha256 = $hash; OutputDirectory = $OutputDirectory }
    $manifestDirectory = & "$PSScriptRoot/New-FileCatManifest.ps1" @generate
    Invoke-WinGet -Arguments @('validate', '--manifest', $manifestDirectory)
    Invoke-WinGet -Arguments @('settings', '--enable', 'LocalManifestFiles')
    $installationAttempted = $true
    # --source cannot be combined with --manifest. The local-manifest route does not search a catalog;
    # do not accept Store/source agreements to work around unrelated first-run source prompts.
    Invoke-WinGet -Arguments @('install', '--manifest', $manifestDirectory, '--silent', '--disable-interactivity', '--log', (Join-Path $evidenceDirectory 'install.log'))

    $arp = Get-ItemProperty -LiteralPath $registryPath
    $report.ARP = @{ DisplayName = $arp.DisplayName; DisplayVersion = $arp.DisplayVersion; Publisher = $arp.Publisher; InstallLocation = $arp.InstallLocation; ProductCode = $arp.PSChildName }
    if ($arp.DisplayVersion -cne $version -or $arp.Publisher -cne 'Marek Střihavka' -or [string]::IsNullOrWhiteSpace($arp.DisplayName)) {
        throw 'The installed ARP name, version or publisher does not match the package.'
    }
    if ($arp.InstallLocation.TrimEnd('\') -ine $installDirectory) { throw 'Unexpected installation directory.' }
    $binary = Join-Path $installDirectory 'FileCat.exe'
    $report.InstalledBinary = @{ Path = $binary; Sha256 = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash; Signature = (Get-TrustedSignature $binary $installerSigner) }

    # Name detection uses the actual installed ARP entry, never an assumed Inno display-name convention.
    $generate.ArpDisplayName = $arp.DisplayName
    $manifestDirectory = & "$PSScriptRoot/New-FileCatManifest.ps1" @generate
    Invoke-WinGet -Arguments @('validate', '--manifest', $manifestDirectory)
    $report.ManifestDirectory = "manifests/b/benny-cz/FileCat/$version"
}
catch {
    $testFailure = $_
    $report.Error = $_.Exception.Message
}
finally {
    if ($installationAttempted) {
        try {
            $report.UninstallerSignature = Get-TrustedSignature $uninstaller $installerSigner
            $uninstallLog = Join-Path $evidenceDirectory 'uninstall.log'
            $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$uninstallLog`"") -Wait -PassThru
            $report.UninstallExitCode = $process.ExitCode
            if ($process.ExitCode -ne 0) { throw "Uninstaller exited with $($process.ExitCode)." }
            # Inno's temporary child can take a moment to remove the original uninstaller and directory.
            for ($attempt = 0; $attempt -lt 20 -and ((Test-Path -LiteralPath $registryPath) -or (Test-Path -LiteralPath $installDirectory)); $attempt++) { Start-Sleep -Milliseconds 500 }
            if ((Test-Path -LiteralPath $registryPath) -or (Test-Path -LiteralPath $installDirectory)) { throw 'Uninstall left the ARP entry or installation directory behind.' }
            $report.RemovalVerified = $true
        }
        catch {
            $report.CleanupError = $_.Exception.Message
            if (-not $testFailure) { $testFailure = $_ }
        }
    }
    if (-not $testFailure) { $report.Result = 'Passed' }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'verification.json') -Encoding utf8
}
if ($testFailure) { throw $testFailure }
Write-Host "Validated ${Tag}: manifest, release hash, trusted signatures, silent install, ARP metadata and uninstall."
