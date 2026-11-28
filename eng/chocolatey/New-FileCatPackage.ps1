<# Packs one exact, verified stable release. Run on Windows with Chocolatey CLI on PATH. #>
param(
    [Parameter(Mandatory)][ValidatePattern('^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z')][string]$Tag,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$version = $Tag.Substring(1)
$assetName = "FileCat-$version-win-x64-setup.exe"
$project = 'https://github.com/benny-cz/FileCat'
$installerUrl = "$project/releases/download/$Tag/$assetName"
$headers = @{ Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
if ($env:GITHUB_TOKEN) { $headers.Authorization = "Bearer $env:GITHUB_TOKEN" }
$choco = (Get-Command choco.exe -CommandType Application).Source
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/benny-cz/FileCat/releases/tags/$Tag" -Headers $headers
if ($release.tag_name -cne $Tag -or $release.draft -or $release.prerelease -or -not $release.immutable) {
    throw 'An exact published, immutable stable release is required.'
}
$assets = @($release.assets | Where-Object name -CEQ $assetName)
if ($assets.Count -ne 1) { throw "Expected one release asset named $assetName." }
$asset = $assets[0]
if ($asset.browser_download_url -cne $installerUrl -or $asset.digest -notmatch '^sha256:[0-9a-f]{64}\z') {
    throw 'The release asset URL or SHA-256 metadata is invalid.'
}

$OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stage = Join-Path $OutputDirectory ('.pack-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path (Join-Path $stage 'tools') | Out-Null
try {
    $installer = Join-Path $stage $assetName
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -UseBasicParsing -Uri $installerUrl -OutFile $installer -TimeoutSec 300
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    $bytes = (Get-Item -LiteralPath $installer).Length
    if ($hash -ine $asset.digest.Substring(7) -or $bytes -ne $asset.size) {
        throw 'The downloaded installer differs from the immutable release digest or size.'
    }

    $nuspec = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'filecat.nuspec.template')).Replace('__VERSION__', $version)
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or
        $null -eq $signature.TimeStamperCertificate -or
        $signature.SignerCertificate.GetNameInfo('SimpleName', $false) -cne ([xml]$nuspec).package.metadata.authors) {
        throw "Expected a valid, timestamped FileCat publisher signature. Authenticode status: $($signature.Status)."
    }

    $nuspecPath = Join-Path $stage 'filecat.nuspec'
    [IO.File]::WriteAllText($nuspecPath, $nuspec)
    $installScript = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'tools/chocolateyInstall.ps1')).
        Replace('__INSTALLER_URL__', $installerUrl).Replace('__INSTALLER_SHA256__', $hash)
    [IO.File]::WriteAllText((Join-Path $stage 'tools/chocolateyInstall.ps1'), $installScript)
    foreach ($file in @('chocolateyUninstall.ps1', '.skipAutoUninstall')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "tools/$file") -Destination (Join-Path $stage "tools/$file")
    }

    & $choco pack $nuspecPath --output-directory $stage | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Chocolatey pack exited with code $LASTEXITCODE." }
    $packageName = "filecat.$version.nupkg"
    $package = Join-Path $stage $packageName
    $receipt = [ordered]@{
        ReleaseId = $release.id; Tag = $Tag; AssetId = $asset.id; InstallerUrl = $installerUrl
        InstallerBytes = $bytes; InstallerSha256 = $hash; Authenticode = [string]$signature.Status
        Signer = $signature.SignerCertificate.Subject; SignerThumbprint = $signature.SignerCertificate.Thumbprint
        TimestampSigner = $signature.TimeStamperCertificate.Subject
        Package = $packageName; PackageSha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    }
    Copy-Item -LiteralPath $package -Destination $OutputDirectory -Force
    $receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory "filecat.$version.json") -Encoding UTF8
    Write-Output (Join-Path $OutputDirectory $packageName)
}
finally { Remove-Item -LiteralPath $stage -Recurse -Force }
