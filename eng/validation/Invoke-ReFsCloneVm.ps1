#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
Preliminary CloneCopyTests on a new disposable VHDX, locally and over same-server SMB.
Run only in the identified VMware Windows guest. Existing disks/letters/shares are never reused.
Keep logs and failed VHDX files; successful VHDX files are deleted only after identity-checked detach.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{32}$')][string]$RunId,
    [Parameter(Mandatory)][string]$ExpectedComputerName,
    [Parameter(Mandatory)][string]$BundleZip,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$BundleSha256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceSha
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$system = Get-CimInstance Win32_ComputerSystem
if ($system.Manufacturer -ne 'VMware, Inc.' -or $env:COMPUTERNAME -ne $ExpectedComputerName) {
    throw 'Refusing: this is not the identified VMware guest.'
}
if (-not (Test-Path -LiteralPath $BundleZip -PathType Leaf) -or
    (Get-FileHash -LiteralPath $BundleZip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $BundleSha256) {
    throw 'Refusing: the test bundle does not have the expected hash.'
}

$root = [IO.Path]::GetFullPath((Join-Path $env:PUBLIC "FileCat-refs-$RunId"))
$vhd = Join-Path $root 'fixture.vhdx'
$marker = Join-Path $root 'owner.txt'
$log = Join-Path $root 'harness.log'
$shareName = "fcclone_$RunId"
$ownedDiskId = $null
$mounted = $false
$shareCreated = $false
$passed = $false
$exitCode = 1
$fixtureBytes = 60GB

function Assert-NoReparseAncestors([string]$path) {
    for ($current = $path; $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Refusing a reparse point in the fixture path: $current"
            }
        }
    }
}
function Assert-OwnedRoot {
    Assert-NoReparseAncestors $root
    if ((Get-Content -LiteralPath $marker -Raw).Trim() -ne "$RunId $SourceSha") {
        throw 'Refusing: fixture ownership changed.'
    }
}
function Note([string]$message) {
    "$(Get-Date -Format o) $message" | Add-Content -LiteralPath $log -Encoding UTF8
}
function Get-FixtureDisk {
    Assert-OwnedRoot
    $image = Get-DiskImage -ImagePath $vhd
    if (-not $image.Attached -or [IO.Path]::GetFullPath($image.ImagePath) -ne $vhd) {
        throw 'Refusing: VHDX attachment identity changed.'
    }
    $disks = @($image | Get-Disk)
    if ($disks.Count -ne 1) { throw 'Refusing: ambiguous VHDX disk mapping.' }
    $disk = $disks[0]
    # STORAGE_BUS_TYPE.BusTypeFileBackedVirtual == 15. A physical or system disk cannot pass.
    if ($disk.IsBoot -or $disk.IsSystem -or [int]$disk.BusType -ne 15 -or $disk.Size -ne $fixtureBytes) {
        throw 'Refusing: the mapped disk is not the new disposable file-backed disk.'
    }
    if ($ownedDiskId -and $disk.UniqueId -ne $ownedDiskId) {
        throw 'Refusing: the virtual disk identity changed.'
    }
    return $disk
}
function Run-Case([string]$name, [string]$directory, [string]$binary) {
    $env:FILECAT_CLONE_DIR = $directory
    Note "BEGIN $name directory=$directory"
    & $binary -noLogo -showLiveOutput -class 'FileCat.Platform.Windows.Tests.CloneCopyTests' -trx (Join-Path $root "$name.trx") 2>&1 |
        Out-File -LiteralPath (Join-Path $root "$name.txt") -Encoding UTF8
    $caseExit = $LASTEXITCODE
    if ($caseExit -eq 0) {
        [xml]$trx = Get-Content -LiteralPath (Join-Path $root "$name.trx") -Raw
        $counters = $trx.TestRun.ResultSummary.Counters
        if ([int]$counters.total -ne 1 -or [int]$counters.passed -ne 1) {
            Note "NOT PASSED $name total=$($counters.total) passed=$($counters.passed) notExecuted=$($counters.notExecuted)"
            $caseExit = 2
        }
    }
    Note "END $name exit=$caseExit"
    return $caseExit
}

Assert-NoReparseAncestors $root
if (Test-Path -LiteralPath $root) { throw 'Refusing: fixture root already exists.' }
if (Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue) { throw 'Refusing: fixture share already exists.' }
if ((Get-Volume -FilePath $env:PUBLIC).SizeRemaining -lt 15GB) { throw 'Refusing: insufficient guest scratch space.' }
New-Item -ItemType Directory -Path $root | Out-Null
"$RunId $SourceSha" | Set-Content -LiteralPath $marker -Encoding UTF8
try {
    Note "source=$SourceSha bundle=$BundleSha256 user=$([Security.Principal.WindowsIdentity]::GetCurrent().Name)"
    Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $root 'os.json') -Encoding UTF8
    & "$env:ProgramFiles\dotnet\dotnet.exe" --list-runtimes | Out-File -LiteralPath (Join-Path $root 'runtimes.txt') -Encoding UTF8
    $bin = Join-Path $root 'bin'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($BundleZip), $bin)
    $binary = Join-Path $bin 'FileCat.Platform.Windows.Tests.exe'
    if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) { throw 'Missing Windows test executable.' }
    Get-FileHash -LiteralPath $binary -Algorithm SHA256 | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $root 'test-binary.json') -Encoding UTF8
    $env:DOTNET_ROOT = "$env:ProgramFiles\dotnet"

    Assert-OwnedRoot
    if (Test-Path -LiteralPath $vhd) { throw 'Refusing: VHDX already exists.' }
    $diskpartFile = Join-Path $root 'create.txt'
    "create vdisk file=`"$vhd`" maximum=61440 type=expandable" | Set-Content -LiteralPath $diskpartFile -Encoding ASCII
    & "$env:SystemRoot\System32\diskpart.exe" /s $diskpartFile 2>&1 |
        Out-File -LiteralPath (Join-Path $root 'diskpart.txt') -Encoding UTF8
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $vhd -PathType Leaf)) { throw 'VHDX creation failed.' }
    Mount-DiskImage -ImagePath $vhd -NoDriveLetter | Out-Null
    $mounted = $true
    $disk = Get-FixtureDisk
    if ($disk.PartitionStyle -ne 'RAW') { throw 'Refusing: newly created VHDX is not empty.' }
    $disk | Initialize-Disk -PartitionStyle GPT | Out-Null
    $disk = Get-FixtureDisk
    $ownedDiskId = $disk.UniqueId
    if (-not $ownedDiskId) { throw 'Refusing: virtual disk has no unique identity.' }
    $partition = $disk | New-Partition -UseMaximumSize -AssignDriveLetter
    $label = "FCCLONE_$($RunId.Substring(0,16))"
    $disk = Get-FixtureDisk
    if ($partition.DiskNumber -ne $disk.Number) { throw 'Refusing: partition is on another disk.' }
    try {
        $partition | Format-Volume -FileSystem ReFS -NewFileSystemLabel $label -Confirm:$false | Out-Null
    } catch {
        Note "Plain ReFS format refused: $($_.Exception.Message); trying Dev Drive on the same verified partition."
        $disk = Get-FixtureDisk
        if ($partition.DiskNumber -ne $disk.Number) { throw 'Refusing: partition mapping changed.' }
        $partition | Format-Volume -DevDrive -NewFileSystemLabel $label -Confirm:$false | Out-Null
    }
    $volume = $partition | Get-Volume
    if ($volume.FileSystem -ne 'ReFS' -or $volume.FileSystemLabel -ne $label) { throw 'ReFS fixture verification failed.' }
    $disk | Select-Object Number, UniqueId, BusType, Size, IsBoot, IsSystem, PartitionStyle | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $root 'disk.json') -Encoding UTF8
    $volume | Select-Object UniqueId, DriveLetter, FileSystem, FileSystemLabel, Size, SizeRemaining | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $root 'volume.json') -Encoding UTF8
    $volumeRoot = "$($volume.DriveLetter):\"
    "$RunId $SourceSha" | Set-Content -LiteralPath (Join-Path $volumeRoot 'owner.txt') -Encoding UTF8
    & "$env:SystemRoot\System32\fsutil.exe" fsinfo volumeinfo $volumeRoot |
        Out-File -LiteralPath (Join-Path $root 'fsutil.txt') -Encoding UTF8

    $local = (New-Item -ItemType Directory -Path (Join-Path $volumeRoot 'local')).FullName
    $localExit = Run-Case 'local' $local $binary
    $sharePath = (New-Item -ItemType Directory -Path (Join-Path $volumeRoot 'share')).FullName
    Get-FixtureDisk | Out-Null
    New-SmbShare -Name $shareName -Path $sharePath -FullAccess ([Security.Principal.WindowsIdentity]::GetCurrent().Name) | Out-Null
    $shareCreated = $true
    $shareExit = Run-Case 'smb' "\\localhost\$shareName" $binary
    # Run-Case also requires the TRX to show an executed, passing case.
    $passed = $localExit -eq 0 -and $shareExit -eq 0
    if ($passed) { $exitCode = 0 }
} catch {
    Note "FAILED $($_.Exception.ToString())"
} finally {
    try {
        if ($shareCreated) {
            Assert-OwnedRoot
            $share = Get-SmbShare -Name $shareName
            if ($share.Path -ne $sharePath) { throw 'Refusing share cleanup: its path changed.' }
            Remove-SmbShare -Name $shareName -Force -Confirm:$false
        }
        if ($mounted) {
            Get-FixtureDisk | Out-Null
            Dismount-DiskImage -ImagePath $vhd
            $mounted = $false
        }
        if ($passed -and (Test-Path -LiteralPath $vhd)) {
            Assert-OwnedRoot
            if ((Get-DiskImage -ImagePath $vhd).Attached) { throw 'Refusing to delete an attached VHDX.' }
            Remove-Item -LiteralPath $vhd
        }
        Note "END exit=$exitCode passed=$passed; logs and failed fixtures retained in $root"
    } catch {
        Note "CLEANUP REFUSED/FAILED $($_.Exception.ToString())"
        $exitCode = 1
    }
}
exit $exitCode
