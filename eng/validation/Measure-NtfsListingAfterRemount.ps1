param(
    [Parameter(Mandatory)][string]$ScratchRoot,
    [ValidateSet(10000, 1000000)][int[]]$Entries = @(10000, 1000000),
    [ValidateRange(1, 5)][int]$Pairs = 3
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ScratchRoot)
$image = Join-Path $root 'listing.vhdx'
$diskPartInput = Join-Path $root 'create.txt'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../tests/FileCat.Platform.Windows.Tests'))
$qualification = Join-Path $PSScriptRoot 'run-medium-integrity-test.ps1'
$powerShell = (Get-Process -Id $PID).Path
$label = 'FCLIST_' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$filter = 'FullyQualifiedName~Enumeration_performance_records_native_and_managed_timings_on_a_prepared_directory'
$samples = [Collections.Generic.List[object]]::new()
$diskId = $partitionId = $volumeId = $null
$attached = $passed = $false
$savedEnvironment = @{
    FILECAT_ENUM_FIXTURE = $env:FILECAT_ENUM_FIXTURE
    FILECAT_ENUM_EXPECTED_COUNT = $env:FILECAT_ENUM_EXPECTED_COUNT
    FILECAT_ENUM_ROUTE = $env:FILECAT_ENUM_ROUTE
}

if ($Entries.Count -eq 0 -or @($Entries | Sort-Object -Unique).Count -ne $Entries.Count) {
    throw 'Choose one or both listing sizes.'
}
if (Test-Path -LiteralPath $root) { throw 'Choose a new scratch directory for the owned image.' }
if ($image -match '["\r\n]' -or $image -cne [Text.Encoding]::ASCII.GetString([Text.Encoding]::ASCII.GetBytes($image))) {
    throw 'Choose an ordinary ASCII scratch path for DiskPart.'
}
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($root))
if ($drive.DriveFormat -ne 'NTFS' -or $drive.AvailableFreeSpace -lt (256MB + 2048L * ($Entries | Measure-Object -Sum).Sum)) {
    throw 'The scratch drive needs sufficient local NTFS space for the owned image.'
}
New-Item -ItemType Directory -Path $root | Out-Null

function Get-OwnedDisk {
    $imageState = Get-DiskImage -ImagePath $image
    if (!$imageState.Attached -or $imageState.ImagePath -ine $image) { throw 'The owned image is not attached.' }
    $disks = @($imageState | Get-Disk)
    if ($disks.Count -ne 1 -or $disks[0].IsBoot -or $disks[0].IsSystem -or
        ($diskId -and $disks[0].UniqueId -ne $diskId)) {
        throw 'The image did not bind to the expected virtual disk.'
    }
    return $disks[0]
}

function Get-OwnedVolume {
    $disk = Get-OwnedDisk
    $matches = @($disk | Get-Partition | Where-Object Guid -EQ $partitionId)
    if ($matches.Count -ne 1) { throw 'The image did not bind to the expected partition.' }
    $volume = $matches[0] | Get-Volume
    if ($volume.FileSystemLabel -cne $label -or $volume.FileSystem -ne 'NTFS' -or
        $volume.AllocationUnitSize -ne 4096 -or ($volumeId -and $volume.UniqueId -ne $volumeId) -or
        [string]$volume.DriveLetter -notmatch '^[A-Z]$') {
        throw 'The image did not expose its expected 4 KiB NTFS volume.'
    }
    return $volume
}

Add-Type -TypeDefinition @'
using System.Globalization;
using System.IO;
public static class FileCatRemountedListingFixture
{
    public static void Seed(string directory, int count)
    {
        Directory.CreateDirectory(directory);
        for (int index = 0; index < count; index++)
        {
            string name = index.ToString("D7", CultureInfo.InvariantCulture) + ".bin";
            using (File.Open(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None)) {}
        }
    }
}
'@

try {
    "create vdisk file=`"$image`" maximum=4096 type=expandable" | Set-Content -LiteralPath $diskPartInput -Encoding ascii
    & (Join-Path $env:SystemRoot 'System32/diskpart.exe') /s $diskPartInput |
        Set-Content -LiteralPath (Join-Path $root 'diskpart.log')
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $image -PathType Leaf)) { throw 'Image creation failed.' }
    Mount-DiskImage -ImagePath $image -NoDriveLetter | Out-Null
    $attached = $true
    $disk = Get-OwnedDisk
    if ($disk.PartitionStyle -ne 'RAW') { throw 'The newly created image must be empty.' }
    $disk | Initialize-Disk -PartitionStyle GPT | Out-Null
    $disk = Get-OwnedDisk
    $diskId = $disk.UniqueId
    if (!$diskId) { throw 'The virtual disk exposed no unique identity.' }
    $partition = $disk | New-Partition -UseMaximumSize -AssignDriveLetter
    $partitionId = $partition.Guid
    if (!$partitionId -or $partition.DiskNumber -ne $disk.Number) { throw 'The allocated fixture partition could not be bound.' }
    Format-Volume -Partition $partition -FileSystem NTFS -NewFileSystemLabel $label -AllocationUnitSize 4096 -Confirm:$false | Out-Null
    $volume = Get-OwnedVolume
    $volumeId = $volume.UniqueId
    if (!$volumeId) { throw 'The fixture volume exposed no unique identity.' }
    foreach ($count in $Entries) {
        [FileCatRemountedListingFixture]::Seed((Join-Path "$($volume.DriveLetter):\" "entries-$count"), $count)
    }
    Dismount-DiskImage -ImagePath $image | Out-Null
    $attached = $false

    foreach ($count in $Entries) {
        foreach ($round in 1..$Pairs) {
            $routes = if ($round % 2) { 'managed', 'native' } else { 'native', 'managed' }
            foreach ($route in $routes) {
                if ((Get-DiskImage -ImagePath $image).Attached) { throw 'The fixture must be detached before each sample.' }
                Mount-DiskImage -ImagePath $image -Access ReadOnly | Out-Null
                $attached = $true
                $volume = Get-OwnedVolume
                $env:FILECAT_ENUM_FIXTURE = Join-Path "$($volume.DriveLetter):\" "entries-$count"
                $env:FILECAT_ENUM_EXPECTED_COUNT = "$count"
                $env:FILECAT_ENUM_ROUTE = $route
                $receipt = "native-listing-volume-cold-$count-$round-$route.trx"
                $started = [DateTime]::UtcNow

                # Each child lowers its own integrity. The controller keeps authority to detach the same image.
                & $powerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $qualification -Project $project -LogFileName $receipt -Filter $filter
                if ($LASTEXITCODE -ne 0) { throw "Listing sample failed ($receipt)." }
                [xml]$result = Get-Content -LiteralPath (Join-Path $project "TestResults/$receipt") -Raw
                $counters = $result.TestRun.ResultSummary.Counters
                if ($counters.total -ne '1' -or $counters.executed -ne '1' -or $counters.passed -ne '1') {
                    throw "Exactly one listing sample must pass ($receipt)."
                }
                $samples.Add([ordered]@{
                    Entries = $count; Round = $round; Route = $route; Drive = [string]$volume.DriveLetter
                    Utc = $started; Receipt = $receipt
                })
                Dismount-DiskImage -ImagePath $image | Out-Null
                $attached = $false
            }
        }
    }
    $passed = $true
}
finally {
    foreach ($variable in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($variable, $savedEnvironment[$variable])
    }
    try {
        if ($attached) {
            Get-OwnedDisk | Out-Null
            Dismount-DiskImage -ImagePath $image | Out-Null
        }
        if ($passed -and !(Get-DiskImage -ImagePath $image).Attached) { Remove-Item -LiteralPath $image }
    }
    finally {
        [ordered]@{ Disk = $diskId; Partition = $partitionId; Volume = $volumeId; Samples = $samples } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'mount-samples.json')
    }
}
