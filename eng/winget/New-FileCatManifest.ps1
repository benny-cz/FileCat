<# Generates the three community-repository files; installation evidence comes from Test-FileCatManifest.ps1. #>
param(
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$InstallerSha256,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ArpDisplayName
)
$ErrorActionPreference = 'Stop'
$packageId = 'benny-cz.FileCat'
$productCode = '{6F3C2B5E-9A41-4E57-8C5E-1F2A3B4C5D6E}_is1'
$manifestDirectory = Join-Path $OutputDirectory "manifests/b/benny-cz/FileCat/$Version"
New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null

function Write-Manifest([string]$Suffix, [string]$Content) {
    # The upstream repository requests UTF-8 and CRLF, independently of the producer's OS.
    $text = ($Content.TrimEnd() -replace '\r?\n', "`r`n") + "`r`n"
    [IO.File]::WriteAllText((Join-Path $manifestDirectory "$packageId$Suffix.yaml"), $text, [Text.UTF8Encoding]::new($false))
}

Write-Manifest '' @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.28.0.schema.json

PackageIdentifier: $packageId
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.28.0
"@

$arp = ''
if ($ArpDisplayName) {
    if ($ArpDisplayName -match '[\r\n\x00-\x1f]') { throw 'ARP DisplayName must be a single line.' }
    $quotedName = $ArpDisplayName.Replace("'", "''")
    $arp = @"
AppsAndFeaturesEntries:
  - DisplayName: '$quotedName'
    ProductCode: '$productCode'
"@ + "`n"
}
# Inno's built-in WinGet switches already supply /SP-, /VERYSILENT, /SUPPRESSMSGBOXES and /NORESTART.
Write-Manifest '.installer' @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.28.0.schema.json

PackageIdentifier: $packageId
PackageVersion: $Version
MinimumOSVersion: 10.0.22000.0
InstallerType: inno
Scope: machine
UpgradeBehavior: install
ProductCode: '$productCode'
${arp}Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/benny-cz/FileCat/releases/download/v$Version/FileCat-$Version-win-x64-setup.exe
    InstallerSha256: $($InstallerSha256.ToUpperInvariant())
ManifestType: installer
ManifestVersion: 1.28.0
"@

Write-Manifest '.locale.en-US' @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.28.0.schema.json

PackageIdentifier: $packageId
PackageVersion: $Version
PackageLocale: en-US
Publisher: Marek Střihavka
PublisherUrl: https://github.com/benny-cz
PublisherSupportUrl: https://github.com/benny-cz/FileCat/issues
PackageName: FileCat
PackageUrl: https://github.com/benny-cz/FileCat
License: MIT
LicenseUrl: https://github.com/benny-cz/FileCat/blob/v$Version/LICENSE
ShortDescription: Multi-panel file manager with built-in viewers and recovery
Description: |-
  FileCat brings local disks, archives and remote servers into a multi-panel,
  multi-tab workspace. Built-in viewers display text, hex, pictures, PDF pages,
  local HTML and Markdown; structured inspectors show executable and media metadata.
  Read-only NTFS, FAT and exFAT scans help recover deleted files to another disk.
Tags:
  - file-manager
  - multi-panel
  - file-recovery
  - archives
  - sftp
  - hex-viewer
ReleaseNotesUrl: https://github.com/benny-cz/FileCat/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: 1.28.0
"@
Write-Output $manifestDirectory
