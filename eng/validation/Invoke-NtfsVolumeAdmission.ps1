param(
    [Parameter(Mandatory)][string]$ScratchRoot
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ScratchRoot)
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../tests/FileCat.Platform.Windows.Tests'))
$qualification = Join-Path $PSScriptRoot 'run-medium-integrity-test.ps1'
$powerShell = (Get-Process -Id $PID).Path
$filter = 'FullyQualifiedName~WindowsStorageInspectionTests.Prepared_volumes_have_specific_inspection_refusals'
$receipt = 'native-storage-volume-admission.trx'
$specs = @(
    @{ Variable = 'FILECAT_STORAGE_NATIVE_64K_TARGET'; Name = 'large-clusters'; Unit = 65536; ReadOnly = $false }
    @{ Variable = 'FILECAT_STORAGE_NATIVE_READONLY_TARGET'; Name = 'read-only'; Unit = 4096; ReadOnly = $true }
)
$savedEnvironment = @{}
$images = [Collections.Generic.List[string]]::new()
$observations = [Collections.Generic.List[object]]::new()

if (Test-Path -LiteralPath $root) { throw 'Choose a new scratch directory for the two fixture images.' }
if ($root -match '["\r\n]' -or $root -cne [Text.Encoding]::ASCII.GetString([Text.Encoding]::ASCII.GetBytes($root))) {
    throw 'Choose an ordinary ASCII scratch path for DiskPart.'
}
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($root))
if ($drive.DriveType -ne 'Fixed' -or $drive.DriveFormat -ne 'NTFS' -or $drive.AvailableFreeSpace -lt 1GB) {
    throw 'The scratch directory needs a local NTFS volume with at least 1 GiB free.'
}
New-Item -ItemType Directory -Path $root | Out-Null

function Get-OwnedDisk([string]$Image) {
    $state = Get-DiskImage -ImagePath $Image
    if (!$state.Attached -or $state.ImagePath -ine $Image) { throw 'The fixture image is not attached.' }
    $disks = @($state | Get-Disk)
    if ($disks.Count -ne 1 -or $disks[0].IsBoot -or $disks[0].IsSystem) {
        throw 'The image did not bind to one disposable disk.'
    }
    return $disks[0]
}

function Get-OwnedVolume([string]$Image, [string]$PartitionId, [string]$Label, [int]$Unit, [bool]$ReadOnly) {
    $disk = Get-OwnedDisk $Image
    $volumes = @($disk | Get-Partition | Where-Object Guid -EQ $PartitionId | Get-Volume)
    if ($volumes.Count -ne 1 -or !$volumes[0].UniqueId -or $volumes[0].FileSystemLabel -cne $Label -or
        $volumes[0].FileSystem -ne 'NTFS' -or $volumes[0].AllocationUnitSize -ne $Unit -or
        $disk.IsReadOnly -ne $ReadOnly -or [string]$volumes[0].DriveLetter -notmatch '^[A-Z]$') {
        throw 'The image did not expose the expected NTFS volume and read-only state.'
    }
    return $volumes[0]
}

try {
    foreach ($name in '64K','READONLY','FAT','EXFAT','REFS','MAPPED','REMOVABLE') {
        $variable = "FILECAT_STORAGE_NATIVE_${name}_TARGET"
        $savedEnvironment[$variable] = [Environment]::GetEnvironmentVariable($variable)
        [Environment]::SetEnvironmentVariable($variable, $null)
    }
    foreach ($spec in $specs) {
        $image = Join-Path $root "$($spec.Name).vhdx"
        $instruction = Join-Path $root "$($spec.Name)-diskpart.txt"
        $label = 'FCNTFS_' + [guid]::NewGuid().ToString('N').Substring(0, 8)
        $images.Add($image)
        "create vdisk file=`"$image`" maximum=512 type=expandable" | Set-Content -LiteralPath $instruction -Encoding ascii
        & (Join-Path $env:SystemRoot 'System32/diskpart.exe') /s $instruction |
            Set-Content -LiteralPath (Join-Path $root "$($spec.Name)-diskpart.log")
        if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $image -PathType Leaf)) { throw 'Image creation failed.' }

        Mount-DiskImage -ImagePath $image -NoDriveLetter | Out-Null
        $disk = Get-OwnedDisk $image
        if ($disk.PartitionStyle -ne 'RAW') { throw 'The newly created image must be empty.' }
        $disk | Initialize-Disk -PartitionStyle GPT | Out-Null
        $partition = Get-OwnedDisk $image | New-Partition -UseMaximumSize -AssignDriveLetter
        if (!$partition.Guid) { throw 'The allocated fixture partition could not be bound.' }
        Format-Volume -Partition $partition -FileSystem NTFS -NewFileSystemLabel $label -AllocationUnitSize $spec.Unit -Confirm:$false | Out-Null
        $volume = Get-OwnedVolume $image $partition.Guid $label $spec.Unit $false
        $target = Join-Path "$($volume.DriveLetter):\" 'filecat-qualification.txt'
        Set-Content -LiteralPath $target -Value 'Owned compression admission fixture' -Encoding ascii
        $digest = (Get-FileHash -LiteralPath $target).Hash

        if ($spec.ReadOnly) {
            Dismount-DiskImage -ImagePath $image | Out-Null
            Mount-DiskImage -ImagePath $image -Access ReadOnly | Out-Null
        }
        $observed = Get-OwnedVolume $image $partition.Guid $label $spec.Unit $spec.ReadOnly
        if ($observed.UniqueId -ne $volume.UniqueId) { throw 'The fixture volume identity changed.' }
        $target = Join-Path "$($observed.DriveLetter):\" 'filecat-qualification.txt'
        [Environment]::SetEnvironmentVariable($spec.Variable, $target)
        $observations.Add([ordered]@{ Case = $spec.Name; Variable = $spec.Variable; Target = $target;
            AllocationUnit = $spec.Unit; ReadOnly = $spec.ReadOnly; SHA256 = $digest })
    }

    & $powerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $qualification -Project $project -LogFileName $receipt -Filter $filter
    if ($LASTEXITCODE -ne 0) { throw 'A native NTFS volume admission case failed.' }
    [xml]$result = Get-Content -LiteralPath (Join-Path $project "TestResults/$receipt") -Raw
    $passed = @($result.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Passed')
    if ($passed.Count -ne $specs.Count) { throw 'Both prepared volume cases must execute and pass.' }
    foreach ($observation in $observations) {
        if (@($passed | Where-Object { $_.testName.Contains($observation.Variable) }).Count -ne 1 -or
            (Get-FileHash -LiteralPath $observation.Target).Hash -ne $observation.SHA256) {
            throw 'The passed receipt and retained file must match each prepared volume.'
        }
    }
    $observations | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'volume-admission.json') -Encoding UTF8
}
finally {
    foreach ($variable in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($variable, $savedEnvironment[$variable])
    }
    foreach ($image in $images) {
        if (!(Test-Path -LiteralPath $image -PathType Leaf)) { continue }
        if ((Get-DiskImage -ImagePath $image).Attached) {
            Get-OwnedDisk $image | Out-Null
            Dismount-DiskImage -ImagePath $image | Out-Null
        }
        if (!(Get-DiskImage -ImagePath $image).Attached) { Remove-Item -LiteralPath $image }
    }
}
