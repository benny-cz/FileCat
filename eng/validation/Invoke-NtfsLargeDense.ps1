# An opt-in, owned 76 GiB NTFS VHDX for the native ARM64 large dense removal gate.
param([Parameter(Mandatory)][string]$ScratchRoot)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ScratchRoot)
$runnerTemp = if ($env:RUNNER_TEMP) { [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\' } else { '' }
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or $env:RUNNER_ARCH -ne 'ARM64' -or
    !$runnerTemp -or !$root.StartsWith($runnerTemp, [StringComparison]::OrdinalIgnoreCase) -or
    $root -match '["%\r\n!^&|<>]' -or
    $root -cne [Text.Encoding]::ASCII.GetString([Text.Encoding]::ASCII.GetBytes($root)) -or
    (Test-Path -LiteralPath $root)) {
    throw 'Choose a fresh ASCII scratch directory beneath RUNNER_TEMP on the disposable Windows ARM64 runner.'
}
$hostDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($root))
if ($hostDrive.DriveType -ne 'Fixed' -or $hostDrive.DriveFormat -ne 'NTFS' -or $hostDrive.AvailableFreeSpace -lt 80GB) {
    throw 'The owned expandable VHDX requires at least 80 GiB free on a fixed NTFS host volume.'
}

New-Item -ItemType Directory -Path $root | Out-Null
$image = Join-Path $root 'large-dense.vhdx'
$diskpart = Join-Path $root 'create-disk.txt'
$receipt = Join-Path $root 'large-dense.json'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../tests/FileCat.Platform.Windows.Tests'))
$preflightResult = Join-Path $project 'TestResults/native-storage-dense-preflight.trx'
$testResult = Join-Path $project 'TestResults/native-storage-large-dense.trx'
$previousRoot = $env:FILECAT_TEST_NTFS_DENSE_ROOT
$previousJobs = $env:FILECAT_TEST_WINDOWS_STORAGE_ARM64_JOBS
$previousArchitecture = $env:FILECAT_TEST_WINDOWS_STORAGE_ABI_ARCHITECTURE
$previousProgress = $env:FILECAT_TEST_NTFS_DENSE_PROGRESS
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
$partitionId = $null
$volumeId = $null

function Get-OwnedDisk([string]$path) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        $mounted = Get-DiskImage -ImagePath $path
        if ($mounted.ImagePath -ine $path) { throw 'The disk image binding changed.' }
        $disks = @()
        if ($mounted.Attached) { $disks = @($mounted | Get-Disk -ErrorAction SilentlyContinue) }
        if ($disks.Count -gt 1) { throw 'The image bound to more than one disk.' }
        if ($disks.Count -eq 1) {
            if ($disks[0].IsBoot -or $disks[0].IsSystem -or $disks[0].Size -ne 76GB) {
                throw 'The image does not identify the owned 76 GiB disk.'
            }
            return $disks[0]
        }
        if ($timer.Elapsed.TotalSeconds -ge 30) { throw 'The owned image was not enumerated within 30 seconds.' }
        Start-Sleep -Milliseconds 250
    }
}

try {
    'create vdisk file="' + $image + '" maximum=77824 type=expandable' |
        Set-Content -LiteralPath $diskpart -Encoding ascii
    & diskpart.exe /s $diskpart | Set-Content -LiteralPath (Join-Path $root 'diskpart.log')
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $image -PathType Leaf)) { throw 'The owned VHDX could not be created.' }
    Mount-DiskImage -ImagePath $image -NoDriveLetter | Out-Null
    $disk = Get-OwnedDisk $image
    if ($disk.PartitionStyle -ne 'RAW') { throw 'The newly created disk is not empty.' }
    $disk | Initialize-Disk -PartitionStyle GPT | Out-Null
    $partition = Get-OwnedDisk $image | New-Partition -UseMaximumSize -AssignDriveLetter
    $partitionId = $partition.Guid
    $letter = [string]$partition.DriveLetter
    if (!$partitionId -or $letter -notmatch '^[A-Z]$') { throw 'The owned partition has no stable identity or letter.' }
    Format-Volume -Partition $partition -FileSystem NTFS -NewFileSystemLabel 'FCATDENSE' -AllocationUnitSize 4096 -Confirm:$false | Out-Null
    $volume = $partition | Get-Volume
    $volumeId = $volume.UniqueId
    $drive = [IO.DriveInfo]::new("${letter}:\")
    if (!$volumeId -or $volume.FileSystem -ne 'NTFS' -or $volume.FileSystemLabel -cne 'FCATDENSE' -or
        $volume.AllocationUnitSize -ne 4096 -or $drive.DriveType -ne 'Fixed' -or
        $drive.TotalSize -lt 74GB -or $drive.TotalSize -gt 78GB -or
        $drive.AvailableFreeSpace -lt (30000000001 + 35GB)) {
        throw 'The owned dense NTFS volume does not satisfy the fixture bounds.'
    }

    $instances = & fltmc.exe instances -v "${letter}:"
    if ($LASTEXITCODE -ne 0) { throw 'The Cloud Files filter state could not be inspected.' }
    if (!($instances -match '^\s*CldFlt\s')) {
        & fltmc.exe attach CldFlt "${letter}:"
        if ($LASTEXITCODE -ne 0) { throw 'The Cloud Files filter could not be attached to the owned volume.' }
    }

    [ordered]@{ Commit = $env:GITHUB_SHA; TestAssemblySha256 = (Get-FileHash (Join-Path $project 'bin/Release/net10.0/FileCat.Platform.Windows.Tests.dll')).Hash;
        Image = $image; VolumeId = $volumeId; VolumeRoot = $drive.RootDirectory.FullName;
        FreeHostBefore = $hostDrive.AvailableFreeSpace; FreeFixtureBefore = $drive.AvailableFreeSpace;
        AllocationUnitSize = $volume.AllocationUnitSize } | ConvertTo-Json | Set-Content -LiteralPath $receipt

    $env:FILECAT_TEST_NTFS_DENSE_ROOT = $drive.RootDirectory.FullName
    $env:FILECAT_TEST_WINDOWS_STORAGE_ARM64_JOBS = '1'
    $env:FILECAT_TEST_WINDOWS_STORAGE_ABI_ARCHITECTURE = 'Arm64'
    $env:FILECAT_TEST_NTFS_DENSE_PROGRESS = Join-Path $root 'dense-progress.log'
    $env:TEMP = $root
    $env:TMP = $root
    & (Join-Path $PSScriptRoot 'run-medium-integrity-test.ps1') -Project $project -LogFileName 'native-storage-dense-preflight.trx' `
        -Filter 'FullyQualifiedName~Dense_NTFS_volume_admits_and_removes_a_small_compressed_file'
    if ($LASTEXITCODE -ne 0) { throw 'The small dense-volume preflight failed.' }
    [xml]$preflight = Get-Content -LiteralPath $preflightResult -Raw
    $preflightCounts = $preflight.TestRun.ResultSummary.Counters
    if ($preflightCounts.total -ne '1' -or $preflightCounts.executed -ne '1' -or $preflightCounts.passed -ne '1') {
        throw 'The small dense-volume preflight must execute and pass before the 30 GB case.'
    }
    & (Join-Path $PSScriptRoot 'run-medium-integrity-test.ps1') -Project $project -LogFileName 'native-storage-large-dense.trx' `
        -Filter 'FullyQualifiedName~Reviewed_removal_above_the_enable_limit_preserves_dense_NTFS_streams'
    if ($LASTEXITCODE -ne 0) { throw 'The large dense native test failed.' }
    [xml]$result = Get-Content -LiteralPath $testResult -Raw
    $counts = $result.TestRun.ResultSummary.Counters
    if ($counts.total -ne '1' -or $counts.executed -ne '1' -or $counts.passed -ne '1' -or $counts.failed -ne '0') {
        throw 'Exactly one large dense native case must execute and pass.'
    }
    $observed = Get-OwnedDisk $image | Get-Partition | Where-Object Guid -EQ $partitionId | Get-Volume
    if ($observed.UniqueId -ne $volumeId -or $observed.FileSystemLabel -cne 'FCATDENSE') {
        throw 'The owned volume identity changed during the native case.'
    }
    [ordered]@{ Commit = $env:GITHUB_SHA; TestAssemblySha256 = (Get-FileHash (Join-Path $project 'bin/Release/net10.0/FileCat.Platform.Windows.Tests.dll')).Hash;
        PreflightTrxSha256 = (Get-FileHash $preflightResult).Hash; TrxSha256 = (Get-FileHash $testResult).Hash;
        PreflightPassed = 1; Passed = 1; Failed = 0; Skipped = 0;
        VolumeId = $volumeId; FreeHostAfter = $hostDrive.AvailableFreeSpace; FreeFixtureAfter = ([IO.DriveInfo]::new("${letter}:\")).AvailableFreeSpace } |
        ConvertTo-Json | Set-Content -LiteralPath $receipt
}
finally {
    $env:FILECAT_TEST_NTFS_DENSE_ROOT = $previousRoot
    $env:FILECAT_TEST_WINDOWS_STORAGE_ARM64_JOBS = $previousJobs
    $env:FILECAT_TEST_WINDOWS_STORAGE_ABI_ARCHITECTURE = $previousArchitecture
    $env:FILECAT_TEST_NTFS_DENSE_PROGRESS = $previousProgress
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
    if (Test-Path -LiteralPath $image -PathType Leaf) {
        $expected = [IO.Path]::GetFullPath((Join-Path $root 'large-dense.vhdx'))
        if ([IO.Path]::GetFullPath($image) -cne $expected -or !$root.StartsWith($runnerTemp, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to retire an image outside the owned runner scratch directory.'
        }
        $mounted = Get-DiskImage -ImagePath $image
        if ($mounted.Attached) {
            Get-OwnedDisk $image | Out-Null
            Dismount-DiskImage -ImagePath $image | Out-Null
        }
        if (!(Get-DiskImage -ImagePath $image).Attached) { Remove-Item -LiteralPath $image }
    }
}
