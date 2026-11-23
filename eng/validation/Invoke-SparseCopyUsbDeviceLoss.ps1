# Opt-in Windows 11 x64 physical USB loss fixture. The caller supplies the exact disposable disk and volume identities.
param(
    [Parameter(Mandatory)][string]$ControlRoot,
    [Parameter(Mandatory)][string]$FixtureRoot,
    [Parameter(Mandatory)][string]$TestAssembly,
    [Parameter(Mandatory)][string]$MediumIntegrityHelper,
    [Parameter(Mandatory)][string]$PnpInstanceId,
    [Parameter(Mandatory)][long]$DiskSize,
    [Parameter(Mandatory)][string]$VolumeId
)

$ErrorActionPreference = 'Stop'
$control = [IO.Path]::GetFullPath($ControlRoot)
$fixture = [IO.Path]::GetFullPath($FixtureRoot).TrimEnd('\')
$assembly = [IO.Path]::GetFullPath($TestAssembly)
$medium = [IO.Path]::GetFullPath($MediumIntegrityHelper)
$volumeRoot = [IO.Path]::GetPathRoot($fixture)
$ownedParent = 'C:\FileCatTests\'
if ($env:PROCESSOR_ARCHITECTURE -ne 'AMD64' -or
    (Get-CimInstance Win32_OperatingSystem).Caption -notlike '*Windows 11*' -or
    !$control.StartsWith($ownedParent + 'sparse-usb-device-loss-', [StringComparison]::OrdinalIgnoreCase) -or
    $control -match '["%\r\n!^&|<>]' -or
    !$fixture.StartsWith($volumeRoot + 'FileCatSparseUsbLoss-', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetDirectoryName($fixture) -ine $volumeRoot -or
    $fixture -match '["%\r\n!^&|<>]' -or
    $volumeRoot -ine 'E:\' -or
    (Test-Path -LiteralPath $control) -or (Test-Path -LiteralPath $fixture) -or
    [IO.Path]::GetFileName($assembly) -cne 'FileCat.Core.Tests.dll' -or
    !(Test-Path -LiteralPath $assembly -PathType Leaf) -or
    !(Test-Path -LiteralPath $medium -PathType Leaf) -or
    !(Test-Path -LiteralPath $ownedParent -PathType Container) -or
    (Get-Item -LiteralPath $ownedParent).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) -or
    $PnpInstanceId -notlike 'USBSTOR\DISK&VEN_*' -or $DiskSize -lt 1GB -or $DiskSize -gt 64GB -or
    !$VolumeId.StartsWith('\\?\Volume{', [StringComparison]::OrdinalIgnoreCase) -or
    @(Get-Process dotnet,MSBuild,testhost,VBCSCompiler -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'Use fresh owned paths and the exact idle disposable Windows 11 x64 USB fixture.'
}

function Assert-OwnedUsb {
    $partition = Get-Partition -DriveLetter E -ErrorAction Stop
    $disk = $partition | Get-Disk
    $volume = $partition | Get-Volume
    $pnp = Get-PnpDevice -InstanceId $PnpInstanceId -PresentOnly -ErrorAction Stop
    $cim = Get-CimInstance Win32_DiskDrive | Where-Object Index -EQ $disk.Number
    $drive = [IO.DriveInfo]::new('E:\')
    if ($disk.BusType -ne 'USB' -or $disk.Size -ne $DiskSize -or
        $cim.PNPDeviceID -ine $PnpInstanceId -or $pnp.Status -ne 'OK' -or
        $volume.UniqueId -ine $VolumeId -or $volume.FileSystemLabel -cne 'FCTEST' -or
        $volume.FileSystem -ne 'NTFS' -or $volume.AllocationUnitSize -ne 4096 -or
        $drive.DriveType -ne [IO.DriveType]::Removable) {
        throw 'The physical USB identity or filesystem changed.'
    }
    return $volume
}

function Wait-Marker([string]$name, [int]$seconds) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $path = Join-Path $control $name
    while (!(Test-Path -LiteralPath $path)) {
        if ($script:process -and $script:process.HasExited) { throw "The medium-integrity test exited before $name." }
        if ($timer.Elapsed.TotalSeconds -ge $seconds) { throw "The medium-integrity test did not reach $name." }
        Start-Sleep -Milliseconds 100
    }
}

$volume = Assert-OwnedUsb
$process = $null
$disableAttempted = $false
$deviceAbsent = $false
$restored = $false
$passed = $false
$status = $null
$child = Join-Path $control 'run-medium.ps1'
$receipt = Join-Path $control 'sparse-usb-device-loss.xml'
$statusPath = Join-Path $control 'status.json'
$destination = Join-Path $fixture 'partial-copy.bin'
try {
    New-Item -ItemType Directory -Path $control | Out-Null
    New-Item -ItemType Directory -Path $fixture | Out-Null
    if ((Get-Item -LiteralPath $fixture).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint))
        { throw 'The owned USB fixture directory is a reparse point.' }

    @'
$ErrorActionPreference = 'Stop'
try {
    . $env:FILECAT_SPARSE_LOSS_MEDIUM
    $ErrorActionPreference = 'Continue'
    & dotnet $env:FILECAT_SPARSE_LOSS_ASSEMBLY -method '*Windows_sparse_copy_reports_physical_usb_loss_after_a_partial_write*' -noLogo -noColor -xml $env:FILECAT_SPARSE_LOSS_RECEIPT 2>&1 |
        Out-File -LiteralPath $env:FILECAT_SPARSE_LOSS_LOG
    exit $LASTEXITCODE
}
catch { $_ | Out-File -LiteralPath $env:FILECAT_SPARSE_LOSS_LOG -Append; exit 1 }
'@ | Set-Content -LiteralPath $child
    $env:FILECAT_TEST_SPARSE_LOSS_CONTROL = $control
    $env:FILECAT_TEST_SPARSE_LOSS_VOLUME = $volumeRoot
    $env:FILECAT_TEST_SPARSE_LOSS_USB_DESTINATION_ROOT = $fixture
    $env:FILECAT_SPARSE_LOSS_MEDIUM = $medium
    $env:FILECAT_SPARSE_LOSS_ASSEMBLY = $assembly
    $env:FILECAT_SPARSE_LOSS_RECEIPT = $receipt
    $env:FILECAT_SPARSE_LOSS_LOG = Join-Path $control 'test.log'
    $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe') `
        -ArgumentList @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',"`"$child`"") -PassThru -WindowStyle Hidden
    Wait-Marker 'ready' 90
    $ready = [long](Get-Content -LiteralPath (Join-Path $control 'ready') -Raw)
    if ($ready -lt 4MB -or $ready -ge 16MB) { throw 'The copy did not reach the first partial write.' }
    Assert-OwnedUsb | Out-Null
    $disableAttempted = $true
    Disable-PnpDevice -InstanceId $PnpInstanceId -Confirm:$false -ErrorAction Stop
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (Test-Path -LiteralPath $volumeRoot) {
        if ($timer.Elapsed.TotalSeconds -ge 20) { throw 'The USB volume remained mounted after device disable.' }
        Start-Sleep -Milliseconds 200
    }
    $deviceAbsent = $true
    New-Item -ItemType File -Path (Join-Path $control 'detached') | Out-Null
    Wait-Marker 'retired' 90
}
catch {
    $status = [ordered]@{ Error = $_.ToString(); DisableAttempted = $disableAttempted }
    if (Test-Path -LiteralPath $control -PathType Container)
        { New-Item -ItemType File -Path (Join-Path $control 'aborted') -Force | Out-Null }
}
finally {
    foreach ($name in @('FILECAT_TEST_SPARSE_LOSS_CONTROL','FILECAT_TEST_SPARSE_LOSS_VOLUME',
            'FILECAT_TEST_SPARSE_LOSS_USB_DESTINATION_ROOT','FILECAT_SPARSE_LOSS_MEDIUM','FILECAT_SPARSE_LOSS_ASSEMBLY',
            'FILECAT_SPARSE_LOSS_RECEIPT','FILECAT_SPARSE_LOSS_LOG')) {
        [Environment]::SetEnvironmentVariable($name, $null)
    }
    if ($disableAttempted) {
        try {
            Enable-PnpDevice -InstanceId $PnpInstanceId -Confirm:$false -ErrorAction Stop
            $timer = [Diagnostics.Stopwatch]::StartNew()
            while (!$restored -and $timer.Elapsed.TotalSeconds -lt 40) {
                try { Assert-OwnedUsb | Out-Null; $restored = $true }
                catch { Start-Sleep -Milliseconds 500 }
            }
            if (!$restored) { throw 'The original USB volume was not restored.' }
            New-Item -ItemType File -Path (Join-Path $control 'reattached') -Force | Out-Null
        }
        catch {
            $status = [ordered]@{ Error = "USB restoration failed: $($_.ToString())"; DisableAttempted = $disableAttempted }
            New-Item -ItemType File -Path (Join-Path $control 'aborted') -Force | Out-Null
        }
    }
    if ($process) {
        if (!$process.WaitForExit(60000)) { $process.Kill(); $status = [ordered]@{ Error = 'The test process did not exit.' } }
        elseif ($process.ExitCode -ne 0 -and !$status) { $status = [ordered]@{ Error = "The test exited $($process.ExitCode)." } }
    }
}

if (!$status) {
    try {
        [xml]$result = Get-Content -LiteralPath $receipt -Raw
        $collection = $result.assemblies.assembly
        if ($collection.total -ne '1' -or $collection.passed -ne '1' -or $collection.failed -ne '0' -or $collection.skipped -ne '0')
            { throw 'Exactly one physical USB loss case must execute and pass.' }
        $partialPresent = Test-Path -LiteralPath $destination -PathType Leaf
        $partialLength = if ($partialPresent) { (Get-Item -LiteralPath $destination).Length } else { $null }
        $status = [ordered]@{ OsVersion = [Environment]::OSVersion.Version.ToString(); BusType = 'USB';
            DiskSize = $DiskSize; VolumeId = $VolumeId; AllocationUnitSize = $volume.AllocationUnitSize;
            TestAssemblySha256 = (Get-FileHash $assembly).Hash; XmlSha256 = (Get-FileHash $receipt).Hash;
            PartialPresent = $partialPresent; PartialLength = $partialLength; ProgressBeforeDetach = $ready;
            Restored = $restored; Passed = 1; Failed = 0; Skipped = 0 }
        $passed = $true
    }
    catch { $status = [ordered]@{ Error = $_.ToString(); Restored = $restored } }
}
else {
    $status['DeviceAbsent'] = $deviceAbsent
    $status['Restored'] = $restored
}
if (Test-Path -LiteralPath $control -PathType Container)
    { $status | ConvertTo-Json | Set-Content -LiteralPath $statusPath }

if ($restored -and $passed) {
    Assert-OwnedUsb | Out-Null
    if ([IO.Path]::GetFullPath($fixture).TrimEnd('\') -cne $fixture -or
        [IO.Path]::GetDirectoryName($fixture) -ine $volumeRoot -or
        !(Get-Item -LiteralPath $fixture).Attributes.HasFlag([IO.FileAttributes]::Directory) -or
        (Get-Item -LiteralPath $fixture).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw 'Refusing to retire a changed USB fixture directory.'
    }
    $children = @(Get-ChildItem -LiteralPath $fixture -Force)
    if ($children.Count -gt 1 -or ($children.Count -eq 1 -and
            ($children[0].Name -cne 'partial-copy.bin' -or
             $children[0].Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) -or
             $children[0].Attributes.HasFlag([IO.FileAttributes]::Directory)))) {
        throw 'Refusing to retire unexpected content in the owned USB fixture directory.'
    }
    if ($children.Count -eq 1) { Remove-Item -LiteralPath $children[0].FullName -Force }
    Remove-Item -LiteralPath $fixture
}
if (!$passed) { throw $status.Error }
