# Windows 11 x64 native qualification of partial sparse-copy cleanup on an owned, nearly full NTFS VHDX.
param(
    [Parameter(Mandatory)][string]$ScratchRoot,
    [Parameter(Mandatory)][string]$TestAssembly,
    [Parameter(Mandatory)][string]$MediumIntegrityHelper
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ScratchRoot)
$assembly = [IO.Path]::GetFullPath($TestAssembly)
$medium = [IO.Path]::GetFullPath($MediumIntegrityHelper)
$ownedParent = 'C:\FileCatTests\'
if ($env:PROCESSOR_ARCHITECTURE -ne 'AMD64' -or
    !$root.StartsWith($ownedParent + 'sparse-disk-full-', [StringComparison]::OrdinalIgnoreCase) -or
    $root -match '["%\r\n!^&|<>]' -or (Test-Path -LiteralPath $root) -or
    [IO.Path]::GetFileName($assembly) -cne 'FileCat.Core.Tests.dll' -or
    !(Test-Path -LiteralPath $assembly -PathType Leaf) -or
    !(Test-Path -LiteralPath $medium -PathType Leaf) -or
    (Get-Item -LiteralPath $ownedParent).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
    throw 'Use a fresh owned Windows 11 x64 fixture, exact Core test assembly and medium-integrity helper.'
}
$os = Get-CimInstance Win32_OperatingSystem
if ($os.Caption -notlike '*Windows 11*' -or
    @(Get-Process dotnet,MSBuild,testhost,VBCSCompiler -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'The owned Windows 11 VM must have no competing .NET test or build process.'
}
$hostDrive = [IO.DriveInfo]::new('C:\')
if ($hostDrive.DriveFormat -ne 'NTFS' -or $hostDrive.AvailableFreeSpace -lt 2GB) {
    throw 'The owned image requires a local NTFS host with at least 2 GiB free.'
}

New-Item -ItemType Directory -Path $root | Out-Null
$image = Join-Path $root 'sparse-full.vhdx'
$diskpart = Join-Path $root 'create-disk.txt'
$receipt = Join-Path $root 'sparse-disk-full.xml'
$status = Join-Path $root 'status.json'
$child = Join-Path $root 'run-medium.ps1'
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
            if ($disks[0].IsBoot -or $disks[0].IsSystem -or $disks[0].Size -ne 256MB) {
                throw 'The image does not identify the owned 256 MiB disk.'
            }
            return $disks[0]
        }
        if ($timer.Elapsed.TotalSeconds -ge 30) { throw 'The owned image was not enumerated within 30 seconds.' }
        Start-Sleep -Milliseconds 250
    }
}

try {
    "create vdisk file=`"$image`" maximum=256 type=expandable" | Set-Content -LiteralPath $diskpart -Encoding ascii
    & diskpart.exe /s $diskpart | Set-Content -LiteralPath (Join-Path $root 'diskpart.log')
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $image -PathType Leaf)) { throw 'The owned VHDX could not be created.' }
    Mount-DiskImage -ImagePath $image -NoDriveLetter | Out-Null
    $disk = Get-OwnedDisk $image
    if ($disk.PartitionStyle -ne 'RAW') { throw 'The newly created disk is not empty.' }
    $disk | Initialize-Disk -PartitionStyle GPT | Out-Null
    $partition = Get-OwnedDisk $image | New-Partition -UseMaximumSize -AssignDriveLetter
    $letter = [string]$partition.DriveLetter
    if (!$partition.Guid -or $letter -notmatch '^[A-Z]$') { throw 'The owned partition has no stable identity or letter.' }
    Format-Volume -Partition $partition -FileSystem NTFS -NewFileSystemLabel 'FCSPARSEFULL' -AllocationUnitSize 4096 -Confirm:$false | Out-Null
    $volume = $partition | Get-Volume
    $volumeId = $volume.UniqueId
    $drive = [IO.DriveInfo]::new("${letter}:\")
    if (!$volumeId -or $volume.FileSystem -ne 'NTFS' -or $volume.FileSystemLabel -cne 'FCSPARSEFULL' -or
        $volume.AllocationUnitSize -ne 4096 -or $drive.TotalSize -lt 128MB -or $drive.TotalSize -gt 320MB) {
        throw 'The owned volume does not match the sparse-copy fixture.'
    }

    $filler = Join-Path $drive.RootDirectory.FullName 'filler.bin'
    $buffer = [byte[]]::new(1MB)
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    $stream = [IO.FileStream]::new($filler, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        while ($drive.AvailableFreeSpace -gt 6MB) {
            $random.GetBytes($buffer)
            $stream.Write($buffer, 0, $buffer.Length)
            $stream.Flush($true)
        }
    }
    finally { $stream.Dispose(); $random.Dispose() }
    if ($drive.AvailableFreeSpace -lt 3MB -or $drive.AvailableFreeSpace -gt 7MB) {
        throw 'The owned NTFS volume did not reach the bounded free-space window.'
    }
    $freeBeforeCopy = $drive.AvailableFreeSpace

    @'
$ErrorActionPreference = 'Stop'
try {
    . $env:FILECAT_SPARSE_FULL_MEDIUM
    $ErrorActionPreference = 'Continue'
    & dotnet $env:FILECAT_SPARSE_FULL_ASSEMBLY -method '*Windows_sparse_copy_discards_partial_output_on_a_full_NTFS_volume*' -noLogo -noColor -xml $env:FILECAT_SPARSE_FULL_RECEIPT 2>&1 |
        Out-File -LiteralPath $env:FILECAT_SPARSE_FULL_LOG
    $testExit = $LASTEXITCODE
    "TestExit=$testExit" | Set-Content -LiteralPath $env:FILECAT_SPARSE_FULL_CHILD_STATUS
    exit $testExit
}
catch { $_ | Out-File -LiteralPath $env:FILECAT_SPARSE_FULL_LOG -Append; exit 1 }
'@ | Set-Content -LiteralPath $child
    $env:FILECAT_TEST_SPARSE_FULL_ROOT = $drive.RootDirectory.FullName
    $env:FILECAT_SPARSE_FULL_MEDIUM = $medium
    $env:FILECAT_SPARSE_FULL_ASSEMBLY = $assembly
    $env:FILECAT_SPARSE_FULL_RECEIPT = $receipt
    $env:FILECAT_SPARSE_FULL_LOG = Join-Path $root 'test.log'
    $env:FILECAT_SPARSE_FULL_CHILD_STATUS = Join-Path $root 'child-status.txt'
    $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe') `
        -ArgumentList @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$child) -PassThru -WindowStyle Hidden
    if (!$process.WaitForExit(180000)) { throw 'The medium-integrity sparse-copy test timed out.' }
    if (!(Test-Path -LiteralPath $env:FILECAT_SPARSE_FULL_CHILD_STATUS) -or
        (Get-Content -LiteralPath $env:FILECAT_SPARSE_FULL_CHILD_STATUS -Raw).Trim() -ne 'TestExit=0') {
        throw 'The medium-integrity sparse-copy test failed.'
    }
    [xml]$result = Get-Content -LiteralPath $receipt -Raw
    $collection = $result.assemblies.assembly
    if ($collection.total -ne '1' -or $collection.passed -ne '1' -or $collection.failed -ne '0' -or $collection.skipped -ne '0') {
        throw 'Exactly one disk-full sparse-copy case must execute and pass.'
    }
    if ((Get-OwnedDisk $image | Get-Partition | Where-Object Guid -EQ $partition.Guid | Get-Volume).UniqueId -ne $volumeId -or
        (Test-Path -LiteralPath (Join-Path $drive.RootDirectory.FullName 'partial-copy.bin'))) {
        throw 'The owned volume changed or the partial destination survived.'
    }
    [ordered]@{ OsVersion = [Environment]::OSVersion.Version.ToString(); VolumeId = $volumeId;
        FileSystem = $volume.FileSystem; AllocationUnitSize = $volume.AllocationUnitSize;
        TestAssemblySha256 = (Get-FileHash $assembly).Hash; XmlSha256 = (Get-FileHash $receipt).Hash;
        FreeBytesBeforeCopy = $freeBeforeCopy; FreeBytesAfterCleanup = $drive.AvailableFreeSpace;
        Passed = 1; Failed = 0; Skipped = 0 } |
        ConvertTo-Json | Set-Content -LiteralPath $status
}
catch {
    [ordered]@{ Error = $_.ToString(); VolumeId = $volumeId } | ConvertTo-Json | Set-Content -LiteralPath $status
    throw
}
finally {
    foreach ($name in @('FILECAT_TEST_SPARSE_FULL_ROOT','FILECAT_SPARSE_FULL_MEDIUM','FILECAT_SPARSE_FULL_ASSEMBLY',
            'FILECAT_SPARSE_FULL_RECEIPT','FILECAT_SPARSE_FULL_LOG','FILECAT_SPARSE_FULL_CHILD_STATUS')) {
        [Environment]::SetEnvironmentVariable($name, $null)
    }
    if (Test-Path -LiteralPath $image -PathType Leaf) {
        if ([IO.Path]::GetFullPath($image) -cne [IO.Path]::GetFullPath((Join-Path $root 'sparse-full.vhdx')) -or
            !$root.StartsWith($ownedParent + 'sparse-disk-full-', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to retire an image outside the owned fixture.'
        }
        $mounted = Get-DiskImage -ImagePath $image
        if ($mounted.Attached) { Get-OwnedDisk $image | Out-Null; Dismount-DiskImage -ImagePath $image | Out-Null }
        if (!(Get-DiskImage -ImagePath $image).Attached) { Remove-Item -LiteralPath $image }
    }
}
