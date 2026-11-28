$ErrorActionPreference = 'Stop'

$architecture = $env:PROCESSOR_ARCHITEW6432
if (-not $architecture) { $architecture = $env:PROCESSOR_ARCHITECTURE }
if ($architecture -ne 'AMD64' -or $env:ChocolateyForceX86 -eq 'true') {
    throw 'The FileCat Chocolatey package requires native x64 Windows.'
}

$windows = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$product = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\ProductOptions'
$build = [int]$windows.CurrentBuildNumber
# Match the x64 Setup build/edition check. The package deliberately serves native x64 only.
$server2016 = $build -eq 14393 -and $product.ProductType -in @('ServerNT', 'LanmanNT')
if ($build -lt 22000 -and -not $server2016) {
    throw 'FileCat requires Windows 11, Windows Server 2016, or a Windows Server build of at least 22000 (x64).'
}

$url = '__INSTALLER_URL__'
$checksum = '__INSTALLER_SHA256__'
$temp = [IO.Path]::GetTempPath()
$installer = Join-Path $temp "FileCat-setup-$([guid]::NewGuid()).exe"
$log = Join-Path $temp "FileCat-Chocolatey-install-$([guid]::NewGuid()).log"
try {
    $download = Get-ChocolateyWebFile -PackageName 'filecat' -FileFullPath $installer `
        -Url64bit $url -Checksum64 $checksum -ChecksumType64 'sha256'
    # Enforce the package pin even if Chocolatey was configured to ignore its checksum check.
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ine $checksum) {
        throw 'The downloaded FileCat installer does not match the package SHA-256.'
    }
    Write-Host "FileCat Setup log: $log"
    Install-ChocolateyInstallPackage -PackageName 'filecat' -FileType 'exe' -File $download `
        -SilentArgs "/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010 /LOG=`"$log`"" `
        -ValidExitCodes @(0, 3010)
}
finally {
    if (Test-Path -LiteralPath $installer) { Remove-Item -LiteralPath $installer -Force -ErrorAction Continue }
}
