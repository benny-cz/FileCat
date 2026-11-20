# Windows 11 x64 native sparse-copy device-loss fixture on an owned 256 MiB NTFS VHDX.
param(
    [Parameter(Mandatory)][string]$ScratchRoot,
    [Parameter(Mandatory)][string]$TestAssembly,
    [Parameter(Mandatory)][string]$MediumIntegrityHelper
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ScratchRoot)
$assembly = [IO.Path]::GetFullPath($TestAssembly)
$medium = [IO.Path]::GetFullPath($MediumIntegrityHelper)
$hostedRunner = $env:GITHUB_ACTIONS -eq 'true' -and $env:RUNNER_OS -eq 'Windows' -and
    $env:RUNNER_ARCH -in @('ARM64', 'X64')
$ownedParent = if ($hostedRunner) { [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\' }
    else { 'C:\FileCatTests\' }
if ((!$hostedRunner -and $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') -or
    !$root.StartsWith($ownedParent + 'sparse-device-loss-', [StringComparison]::OrdinalIgnoreCase) -or
    $root -match '["%\r\n!^&|<>]' -or (Test-Path -LiteralPath $root) -or
    [IO.Path]::GetFileName($assembly) -cne 'FileCat.Core.Tests.dll' -or
    !(Test-Path -LiteralPath $assembly -PathType Leaf) -or
    !(Test-Path -LiteralPath $medium -PathType Leaf) -or
    (Get-Item -LiteralPath $ownedParent).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
    throw 'Use a fresh owned Windows 11 VM or Actions runner fixture, exact Core test assembly and medium-integrity helper.'
}
$os = Get-CimInstance Win32_OperatingSystem
if ($os.Caption -notlike '*Windows 11*') {
    throw 'The owned fixture requires Windows 11.'
}
# The hosted runner is private to this job; its preceding build leaves compiler
# servers running. On the shared VM, reject any existing build or test process.
if (!$hostedRunner -and
    @(Get-Process dotnet,MSBuild,testhost,VBCSCompiler -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'The owned Windows 11 VM must have no competing .NET test or build process.'
}
$hostDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($root))
if ($hostDrive.DriveFormat -ne 'NTFS' -or $hostDrive.AvailableFreeSpace -lt 2GB) {
    throw 'The owned image requires a local NTFS host with at least 2 GiB free.'
}

New-Item -ItemType Directory -Path $root | Out-Null
$image = Join-Path $root 'sparse-device-loss.vhdx'
$diskpart = Join-Path $root 'create-disk.txt'
$receipt = Join-Path $root 'sparse-device-loss.xml'
$status = Join-Path $root 'status.json'
$child = Join-Path $root 'run-medium.ps1'
$volumeId = $null
$partitionId = $null
$letter = $null
$process = $null

function Get-OwnedDisk([string]$path) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        $mounted = Get-DiskImage -ImagePath $path
        if ($mounted.ImagePath -ine $path) { throw 'The disk image binding changed.' }
        $disks = @()
        if ($mounted.Attached) { $disks = @($mounted | Get-Disk -ErrorAction SilentlyContinue) }
        if ($disks.Count -gt 1) { throw 'The image bound to more than one disk.' }
        if ($disks.Count -eq 1) {
            if ($disks[0].IsBoot -or $disks[0].IsSystem -or $disks[0].Size -ne 256MB) {
                throw 'The image does not identify the owned 256 MiB disk.'
            }
            return $disks[0]
        }
        if ($timer.Elapsed.TotalSeconds -ge 30) { throw 'The owned image was not enumerated within 30 seconds.' }
        Start-Sleep -Milliseconds 250
    }
}

function Wait-Marker([string]$name, [int]$seconds) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $path = Join-Path $root $name
    while (!(Test-Path -LiteralPath $path)) {
        if ($process -and $process.HasExited) { throw "The medium-integrity test exited before $name." }
        if ($timer.Elapsed.TotalSeconds -ge $seconds) { throw "The medium-integrity test did not reach $name." }
        Start-Sleep -Milliseconds 100
    }
}

try {
    'create vdisk file="' + $image + '" maximum=256 type=expandable' |
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
    Format-Volume -Partition $partition -FileSystem NTFS -NewFileSystemLabel 'FCSPARSELOSS' -AllocationUnitSize 4096 -Confirm:$false | Out-Null
    $volume = $partition | Get-Volume
    $volumeId = $volume.UniqueId
    $drive = [IO.DriveInfo]::new("${letter}:\")
    if (!$volumeId -or $volume.FileSystem -ne 'NTFS' -or $volume.FileSystemLabel -cne 'FCSPARSELOSS' -or
        $volume.AllocationUnitSize -ne 4096 -or $drive.TotalSize -lt 128MB -or $drive.TotalSize -gt 320MB) {
        throw 'The owned sparse-copy NTFS volume does not satisfy the fixture bounds.'
    }

    @'
$ErrorActionPreference = 'Stop'
try {
    . $env:FILECAT_SPARSE_LOSS_MEDIUM
    $ErrorActionPreference = 'Continue'
    & dotnet $env:FILECAT_SPARSE_LOSS_ASSEMBLY -method '*Windows_sparse_copy_reports_device_loss_after_a_partial_write*' -noLogo -noColor -xml $env:FILECAT_SPARSE_LOSS_RECEIPT 2>&1 |
        Out-File -LiteralPath $env:FILECAT_SPARSE_LOSS_LOG
    $testExit = $LASTEXITCODE
    "TestExit=$testExit" | Set-Content -LiteralPath $env:FILECAT_SPARSE_LOSS_CHILD_STATUS
    exit $testExit
}
catch { $_ | Out-File -LiteralPath $env:FILECAT_SPARSE_LOSS_LOG -Append; exit 1 }
'@ | Set-Content -LiteralPath $child
    $env:FILECAT_TEST_SPARSE_LOSS_CONTROL = $root
    $env:FILECAT_TEST_SPARSE_LOSS_VOLUME = $drive.RootDirectory.FullName
    $env:FILECAT_SPARSE_LOSS_MEDIUM = $medium
    $env:FILECAT_SPARSE_LOSS_ASSEMBLY = $assembly
    $env:FILECAT_SPARSE_LOSS_RECEIPT = $receipt
    $env:FILECAT_SPARSE_LOSS_LOG = Join-Path $root 'test.log'
    $env:FILECAT_SPARSE_LOSS_CHILD_STATUS = Join-Path $root 'child-status.txt'
    $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe') `
        -ArgumentList @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',"`"$child`"") -PassThru -WindowStyle Hidden
    Wait-Marker 'ready' 90
    $ready = [long](Get-Content -LiteralPath (Join-Path $root 'ready') -Raw)
    if ($ready -lt 4MB -or $ready -ge 16MB) { throw 'The test did not reach the intended partial write.' }
    Dismount-DiskImage -ImagePath $image | Out-Null
    if ((Get-DiskImage -ImagePath $image).Attached -or
        (Get-Volume -DriveLetter $letter -ErrorAction SilentlyContinue)) { throw 'The owned volume remained accessible after detachment.' }
    New-Item -ItemType File -Path (Join-Path $root 'detached') | Out-Null
    Wait-Marker 'retired' 90

    Mount-DiskImage -ImagePath $image | Out-Null
    $partitions = @(Get-OwnedDisk $image | Get-Partition | Where-Object Guid -EQ $partitionId)
    if ($partitions.Count -ne 1) { throw 'The original partition is absent after remount.' }
    if ($partitions[0].DriveLetter -ne $letter) {
        if (Get-Volume -DriveLetter $letter -ErrorAction SilentlyContinue) { throw 'The original fixture drive letter is occupied.' }
        Set-Partition -InputObject $partitions[0] -NewDriveLetter $letter
    }
    $remounted = $partitions[0] | Get-Volume
    if ($remounted.UniqueId -ne $volumeId -or $remounted.FileSystemLabel -cne 'FCSPARSELOSS') {
        throw 'The owned volume identity changed after remount.'
    }
    $destination = "${letter}:\partial-copy.bin"
    $partialPresent = Test-Path -LiteralPath $destination -PathType Leaf
    $partialLength = if ($partialPresent) { (Get-Item -LiteralPath $destination).Length } else { $null }
    New-Item -ItemType File -Path (Join-Path $root 'reattached') | Out-Null
    if (!$process.WaitForExit(60000) -or $process.ExitCode -ne 0) { throw 'The medium-integrity device-loss case failed.' }
    [xml]$result = Get-Content -LiteralPath $receipt -Raw
    $collection = $result.assemblies.assembly
    if ($collection.total -ne '1' -or $collection.passed -ne '1' -or $collection.failed -ne '0' -or $collection.skipped -ne '0') {
        throw 'Exactly one sparse-copy device-loss case must execute and pass.'
    }
    [ordered]@{ OsVersion = [Environment]::OSVersion.Version.ToString(); VolumeId = $volumeId;
        AllocationUnitSize = $remounted.AllocationUnitSize; TestAssemblySha256 = (Get-FileHash $assembly).Hash;
        XmlSha256 = (Get-FileHash $receipt).Hash; PartialPresent = $partialPresent; PartialLength = $partialLength;
        ProgressBeforeDetach = $ready; Passed = 1; Failed = 0; Skipped = 0 } |
        ConvertTo-Json | Set-Content -LiteralPath $status
}
catch {
    [ordered]@{ Error = $_.ToString(); VolumeId = $volumeId } | ConvertTo-Json | Set-Content -LiteralPath $status
    throw
}
finally {
    foreach ($name in @('FILECAT_TEST_SPARSE_LOSS_CONTROL','FILECAT_TEST_SPARSE_LOSS_VOLUME','FILECAT_SPARSE_LOSS_MEDIUM',
            'FILECAT_SPARSE_LOSS_ASSEMBLY','FILECAT_SPARSE_LOSS_RECEIPT','FILECAT_SPARSE_LOSS_LOG','FILECAT_SPARSE_LOSS_CHILD_STATUS')) {
        [Environment]::SetEnvironmentVariable($name, $null)
    }
    if ($process -and !$process.HasExited) {
        New-Item -ItemType File -Path (Join-Path $root 'detached') -Force | Out-Null
        New-Item -ItemType File -Path (Join-Path $root 'reattached') -Force | Out-Null
        if (!$process.WaitForExit(10000)) { $process.Kill() }
    }
    if (Test-Path -LiteralPath $image -PathType Leaf) {
        if ([IO.Path]::GetFullPath($image) -cne [IO.Path]::GetFullPath((Join-Path $root 'sparse-device-loss.vhdx')) -or
            !$root.StartsWith($ownedParent + 'sparse-device-loss-', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to retire an image outside the owned fixture.'
        }
        $mounted = Get-DiskImage -ImagePath $image
        if ($mounted.Attached) { Get-OwnedDisk $image | Out-Null; Dismount-DiskImage -ImagePath $image | Out-Null }
        if (!(Get-DiskImage -ImagePath $image).Attached) { Remove-Item -LiteralPath $image }
    }
}
