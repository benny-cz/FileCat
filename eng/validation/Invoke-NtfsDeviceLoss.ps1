param(
    [Parameter(Mandatory)][string]$ScratchRoot
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ScratchRoot)
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or $env:RUNNER_ARCH -ne 'ARM64' -or
    ![IO.Path]::IsPathFullyQualified($env:RUNNER_TEMP) -or
    !($root.StartsWith(([IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) -or
    $root -match '["\r\n]' -or $root -cne [Text.Encoding]::ASCII.GetString([Text.Encoding]::ASCII.GetBytes($root)) -or
    (Test-Path -LiteralPath $root)) {
    throw 'Choose a fresh ASCII scratch directory on the disposable Windows ARM64 runner.'
}
New-Item -ItemType Directory -Path $root | Out-Null

$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../tests/FileCat.Platform.Windows.Tests'))
$qualification = Join-Path $PSScriptRoot 'run-medium-integrity-test.ps1'
$powerShell = (Get-Process -Id $PID).Path

function Get-FixtureDisk([string]$Image) {
    $mounted = Get-DiskImage -ImagePath $Image
    $disks = @($mounted | Get-Disk)
    if (!$mounted.Attached -or $mounted.ImagePath -ine $Image -or $disks.Count -ne 1 -or
        $disks[0].IsBoot -or $disks[0].IsSystem -or $disks[0].Size -ne 256MB) {
        throw 'The mounted device is outside the owned VHDX fixture.'
    }
    return $disks[0]
}

function Wait-FixtureMarker([string]$CaseRoot, $Process, [string]$Name) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (!(Test-Path -LiteralPath (Join-Path $CaseRoot $Name))) {
        if ($Process.HasExited) { throw "The native test exited before $Name with code $($Process.ExitCode)." }
        if ($timer.Elapsed.TotalSeconds -ge 45) { throw "Timed out waiting for $Name." }
        Start-Sleep -Milliseconds 150
    }
}

function Get-FixtureVolume($Partition, [char]$Letter) {
    $volume = $Partition | Get-Volume
    if ($volume.DriveLetter -ne $Letter -or $volume.FileSystem -ne 'NTFS' -or
        $volume.FileSystemLabel -cne 'FCDeviceLoss' -or $volume.AllocationUnitSize -ne 4096 -or !$volume.UniqueId) {
        throw 'The owned partition does not expose the expected NTFS volume.'
    }
    return $volume
}

function Ensure-FixtureCloudFilter($Volume) {
    $drive = "$($Volume.DriveLetter):"
    $instances = & fltmc.exe instances -v $drive
    if ($LASTEXITCODE) { throw 'Could not inspect minifilters on the owned volume.' }
    if (!($instances -match '^\s*CldFlt\s')) {
        & fltmc.exe attach CldFlt $drive
        if ($LASTEXITCODE) { throw 'Could not attach the Cloud Files filter to the owned volume.' }
    }
}

$cases = @(
    @{ Phase = 'before-set'; Variable = 'FILECAT_STORAGE_NATIVE_DEVICE_LOSS_CONTROL'
        Test = 'Detaching_the_owned_volume_after_intent_does_not_mutate_or_replay_the_prepared_call' }
    @{ Phase = 'after-set'; Variable = 'FILECAT_STORAGE_NATIVE_DEVICE_LOSS_AFTER_SET_CONTROL'
        Test = 'Detaching_after_native_compression_returns_retains_uncertainty_without_replay' }
)
foreach ($case in $cases) {
    $caseRoot = Join-Path $root $case.Phase
    New-Item -ItemType Directory -Path $caseRoot | Out-Null
    $image = Join-Path $caseRoot 'fixture.vhdx'
    $command = Join-Path $caseRoot 'diskpart.txt'
    $trx = "native-storage-device-loss-$($case.Phase).trx"
    $test = $null
    $previous = [Environment]::GetEnvironmentVariable($case.Variable)
    try {
        "create vdisk file=`"$image`" maximum=256 type=expandable" | Set-Content -LiteralPath $command -Encoding ascii
        & diskpart.exe /s $command | Set-Content -LiteralPath (Join-Path $caseRoot 'diskpart.log')
        if ($LASTEXITCODE -or !(Test-Path -LiteralPath $image -PathType Leaf)) { throw 'DiskPart could not create the fixture.' }
        Mount-DiskImage -ImagePath $image -NoDriveLetter | Out-Null
        $disk = Get-FixtureDisk $image
        if ($disk.PartitionStyle -ne 'RAW') { throw 'The newly created fixture disk is not empty.' }
        $disk | Initialize-Disk -PartitionStyle GPT | Out-Null
        $partition = Get-FixtureDisk $image | New-Partition -UseMaximumSize -AssignDriveLetter
        $partitionId = $partition.Guid
        $letter = $partition.DriveLetter
        if (!$partitionId -or [string]$letter -notmatch '^[A-Z]$') { throw 'The new fixture partition could not be bound.' }
        Format-Volume -Partition $partition -FileSystem NTFS -NewFileSystemLabel 'FCDeviceLoss' -AllocationUnitSize 4096 -Confirm:$false | Out-Null
        $volume = Get-FixtureVolume $partition $letter
        Ensure-FixtureCloudFilter $volume

        $target = Join-Path "${letter}:\" 'filecat-device-loss.bin'
        $payload = [byte[]]::new(1MB)
        $payload[91] = 78
        [IO.File]::WriteAllBytes($target, $payload)
        $hash = (Get-FileHash -LiteralPath $target).Hash
        Set-Content -LiteralPath (Join-Path $caseRoot 'target.txt') -Value $target -Encoding ascii
        [Environment]::SetEnvironmentVariable($case.Variable, $caseRoot)
        $test = Start-Process -FilePath $powerShell -ArgumentList @(
            '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', "`"$qualification`"",
            '-Project', "`"$project`"", '-LogFileName', $trx, '-Filter', "FullyQualifiedName~$($case.Test)") -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput (Join-Path $caseRoot 'test.stdout.log') -RedirectStandardError (Join-Path $caseRoot 'test.stderr.log')
        Wait-FixtureMarker $caseRoot $test 'prepared'
        Get-FixtureDisk $image | Out-Null
        Dismount-DiskImage -ImagePath $image | Out-Null
        if ((Get-DiskImage -ImagePath $image).Attached -or (Get-Volume -DriveLetter $letter -ErrorAction SilentlyContinue)) {
            throw 'The fixture remained accessible after dismount.'
        }
        New-Item -ItemType File -Path (Join-Path $caseRoot 'detached') | Out-Null
        Wait-FixtureMarker $caseRoot $test 'retired'

        Mount-DiskImage -ImagePath $image | Out-Null
        $partitions = @(Get-FixtureDisk $image | Get-Partition | Where-Object Guid -eq $partitionId)
        if ($partitions.Count -ne 1) { throw 'The original partition is absent after remount.' }
        if ($partitions[0].DriveLetter -ne $letter) {
            if (Get-Volume -DriveLetter $letter -ErrorAction SilentlyContinue) { throw 'The original fixture drive letter is occupied.' }
            Set-Partition -InputObject $partitions[0] -NewDriveLetter $letter
        }
        $remounted = Get-FixtureVolume $partitions[0] $letter
        if ($remounted.UniqueId -ne $volume.UniqueId -or (Get-FileHash -LiteralPath $target).Hash -ne $hash) {
            throw 'Remount must expose the same volume and canary bytes.'
        }
        Ensure-FixtureCloudFilter $remounted
        New-Item -ItemType File -Path (Join-Path $caseRoot 'reattached') | Out-Null
        if (!$test.WaitForExit(45000) -or $test.ExitCode -ne 0) { throw 'The native test failed after remount.' }
        [xml]$result = Get-Content -Raw -LiteralPath (Join-Path $project "TestResults/$trx")
        $counters = $result.TestRun.ResultSummary.Counters
        if ($counters.total -ne '1' -or $counters.executed -ne '1' -or $counters.passed -ne '1') {
            throw 'The selected native case must execute and pass.'
        }
        [ordered]@{ Phase = $case.Phase; AllocationUnit = $remounted.AllocationUnitSize; PayloadSHA256 = $hash
            Test = $case.Test; InterruptedJournalSHA256 = (Get-FileHash -LiteralPath (Join-Path $caseRoot 'interrupted.journal')).Hash
            FreshRetryJournalSHA256 = (Get-FileHash -LiteralPath (Join-Path $caseRoot 'fresh-retry.journal')).Hash
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $caseRoot 'device-loss.json')
    }
    finally {
        [Environment]::SetEnvironmentVariable($case.Variable, $previous)
        if ($test -and !$test.HasExited) {
            New-Item -ItemType File -Path (Join-Path $caseRoot 'aborted') | Out-Null
            [void]$test.WaitForExit(10000)
        }
        if (Test-Path -LiteralPath $image -PathType Leaf) {
            if ((Get-DiskImage -ImagePath $image).Attached) {
                Get-FixtureDisk $image | Out-Null
                Dismount-DiskImage -ImagePath $image | Out-Null
            }
            if (!(Get-DiskImage -ImagePath $image).Attached) { Remove-Item -LiteralPath $image }
        }
    }
}
